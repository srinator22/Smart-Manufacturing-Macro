// The concurrency defect this suite pins: the installer waits for Inventor to exit, so after Download
// and install the dialog can be closed, reopened, and pressed again - and a second powershell.exe would
// rewrite the Addins root alongside the first. These cases drive the guard through the fakes, so the
// pid is a value the test chose rather than a real process it has to race.

using WmpToolsManager.Application;
using WmpToolsManager.Core;
using WmpToolsManager.UnitTests.Fakes;

namespace WmpToolsManager.UnitTests;

public sealed class PendingApplyGuardTests
{
    private const int Pid = RecordingProcessLauncher.LaunchedPid;

    private static readonly string MarkerPath = Path.Combine(UpdateScenario.StateRoot, "pending-apply.json");

    private const string WaitingForUpdate =
        "An update to 0.6.0 is already waiting for Inventor to close (PowerShell process 4242). "
        + "Close Inventor to let it finish.";

    [Fact]
    public async Task RecordsTheLaunchedUpdateBesideInstalledJson()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        UpdateWorkflow workflow = scenario.Build();
        UpdateCheck check = await workflow.CheckForUpdateAsync();
        StageResult stage = await workflow.StageUpdateAsync(check);

        LaunchResult launch = workflow.LaunchApply(stage);

        Assert.True(launch.Launched);
        Assert.Equal(Pid, launch.ProcessId);
        (string path, PendingApply marker) = Assert.Single(scenario.InstallState.WrittenPendingApplies);
        Assert.Equal(MarkerPath, path);
        Assert.Equal(Pid, marker.Pid);
        Assert.Equal("0.6.0", marker.Version);
        Assert.Equal(PendingApply.UpdateKind, marker.Kind);
        Assert.NotEqual(default, marker.LaunchedUtc);
    }

    [Fact]
    public void RecordsALaunchedRollbackAsARollbackAgainstTheInstalledVersion()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        scenario.InstallState.PreviousInstallExists = true;
        scenario.InstallState.StagedInstaller =
            Path.Combine(UpdateScenario.StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");

        LaunchResult launch = scenario.Build().RollbackToPrevious();

        Assert.True(launch.Launched);
        (string path, PendingApply marker) = Assert.Single(scenario.InstallState.WrittenPendingApplies);
        Assert.Equal(MarkerPath, path);
        Assert.Equal(Pid, marker.Pid);
        Assert.Equal("0.5.0", marker.Version);
        Assert.Equal(PendingApply.RollbackKind, marker.Kind);
    }

    [Fact]
    public void RecordsAnUnknownVersionForARollbackWhoseInstalledStateCannotBeRead()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        scenario.InstallState.Installed = new(null, "No installed.json was found.");
        scenario.InstallState.PreviousInstallExists = true;
        scenario.InstallState.StagedInstaller =
            Path.Combine(UpdateScenario.StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");

        Assert.True(scenario.Build().RollbackToPrevious().Launched);

        Assert.Equal("an unknown version", Assert.Single(scenario.InstallState.WrittenPendingApplies).Value.Version);
    }

    [Fact]
    public void RecordsNothingWhenTheLaunchItselfFailed()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        scenario.InstallState.PreviousInstallExists = true;
        scenario.InstallState.StagedInstaller =
            Path.Combine(UpdateScenario.StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");
        scenario.Launcher.Result = new(false, "blocked");

        Assert.False(scenario.Build().RollbackToPrevious().Launched);

        Assert.Empty(scenario.InstallState.WrittenPendingApplies);
    }

    [Fact]
    public async Task RefusesASecondLaunchOfTheSameStagedUpdate()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        UpdateWorkflow workflow = scenario.Build();
        UpdateCheck check = await workflow.CheckForUpdateAsync();
        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.True(workflow.LaunchApply(stage).Launched);

        // The first launch is what the state root now records, and its process is still waiting.
        Assert.Equal(Pid, Assert.Single(scenario.InstallState.WrittenPendingApplies).Value.Pid);
        scenario.Launcher.RunningPids.Add(Pid);

        LaunchResult second = workflow.LaunchApply(stage);

        Assert.False(second.Launched);
        Assert.Equal(WaitingForUpdate, second.Message);
        Assert.Single(scenario.Launcher.Launches);
    }

    [Fact]
    public async Task RefusesToStageWhileAnApplyIsStillWaiting()
    {
        UpdateScenario scenario = UpdateScenario
            .WithAvailableUpdate()
            .WithPendingApply(Pid, "0.6.0", PendingApply.UpdateKind);
        UpdateWorkflow workflow = scenario.Build();
        UpdateCheck check = await workflow.CheckForUpdateAsync();

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.False(stage.Verified);
        Assert.Equal(WaitingForUpdate, stage.Message);
        Assert.Empty(stage.Errors);
        Assert.Empty(scenario.Source.RequestedAssets);
        Assert.Empty(scenario.InstallState.CreatedDirectories);
    }

    [Fact]
    public void RefusesToRollBackWhileAnApplyIsStillWaiting()
    {
        UpdateScenario scenario = UpdateScenario
            .WithAvailableUpdate()
            .WithPendingApply(Pid, "0.6.0", PendingApply.UpdateKind);
        scenario.InstallState.PreviousInstallExists = true;
        scenario.InstallState.StagedInstaller =
            Path.Combine(UpdateScenario.StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");

        LaunchResult launch = scenario.Build().RollbackToPrevious();

        Assert.False(launch.Launched);
        Assert.Equal(WaitingForUpdate, launch.Message);
        Assert.Empty(scenario.Launcher.Launches);
    }

    [Fact]
    public void WordsARefusalCausedByAWaitingRollbackAsARollback()
    {
        UpdateScenario scenario = UpdateScenario
            .WithAvailableUpdate()
            .WithPendingApply(99, "0.6.0", PendingApply.RollbackKind);
        scenario.InstallState.PreviousInstallExists = true;
        scenario.InstallState.StagedInstaller =
            Path.Combine(UpdateScenario.StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");

        Assert.Equal(
            "A rollback from 0.6.0 is already waiting for Inventor to close (PowerShell process 99). "
            + "Close Inventor to let it finish.",
            scenario.Build().RollbackToPrevious().Message);
    }

    [Fact]
    public async Task ReportsTheWaitingApplyOnTheCheckItself()
    {
        UpdateScenario scenario = UpdateScenario
            .WithAvailableUpdate()
            .WithPendingApply(Pid, "0.6.0", PendingApply.UpdateKind);

        UpdateCheck check = await scenario.Build().CheckForUpdateAsync();

        Assert.True(check.HasPendingApply);
        Assert.Equal(Pid, check.PendingApply!.Pid);
        Assert.Equal(WaitingForUpdate, check.PendingApply.WaitingMessage);
        Assert.Empty(check.Errors);
        Assert.Equal(MarkerPath, Assert.Single(scenario.InstallState.ReadPendingApplyPaths));
    }

    [Fact]
    public async Task IgnoresAMarkerWhoseProcessHasFinished()
    {
        UpdateScenario scenario = UpdateScenario
            .WithAvailableUpdate()
            .WithStalePendingApply(Pid, "0.6.0", PendingApply.UpdateKind);
        UpdateWorkflow workflow = scenario.Build();

        UpdateCheck check = await workflow.CheckForUpdateAsync();

        Assert.False(check.HasPendingApply);
        Assert.Null(check.PendingApply);
        Assert.Empty(check.Errors);
        Assert.Null(workflow.GetActivePendingApply());

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.True(stage.Verified);
        Assert.True(workflow.LaunchApply(stage).Launched);
    }

    [Fact]
    public async Task IgnoresAMalformedMarkerButSaysSoUnderProblems()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        scenario.InstallState.Pending = PendingApplyJson.Parse("not json at all");
        UpdateWorkflow workflow = scenario.Build();

        UpdateCheck check = await workflow.CheckForUpdateAsync();

        Assert.False(check.HasPendingApply);
        string error = Assert.Single(check.Errors);
        Assert.StartsWith("pending-apply.json is not valid JSON:", error, StringComparison.Ordinal);
        Assert.Contains(MarkerPath, error, StringComparison.Ordinal);
        Assert.Contains("the next launch replaces it", error, StringComparison.Ordinal);

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.True(stage.Verified);
        Assert.True(workflow.LaunchApply(stage).Launched);
    }

    [Fact]
    public async Task SaysNothingAboutAStateRootThatSimplyHasNoMarker()
    {
        UpdateCheck check = await UpdateScenario.WithAvailableUpdate().Build().CheckForUpdateAsync();

        Assert.False(check.HasPendingApply);
        Assert.Empty(check.Errors);
    }

    [Fact]
    public async Task ReportsAMarkerItCouldNotWriteRatherThanClaimingTheGuardIsInPlace()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        UpdateWorkflow workflow = scenario.Build();
        UpdateCheck check = await workflow.CheckForUpdateAsync();
        StageResult stage = await workflow.StageUpdateAsync(check);
        scenario.InstallState.ThrowOnWritePendingApply = new UnauthorizedAccessException("access is denied");

        LaunchResult launch = workflow.LaunchApply(stage);

        Assert.True(launch.Launched);
        Assert.Contains("could not be written (access is denied)", launch.Message, StringComparison.Ordinal);
        Assert.Contains("do not start another until this one finishes", launch.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsALauncherThatCannotNameTheProcessItStarted()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        UpdateWorkflow workflow = scenario.Build();
        UpdateCheck check = await workflow.CheckForUpdateAsync();
        StageResult stage = await workflow.StageUpdateAsync(check);
        scenario.Launcher.Result = new(true, "started", null);

        LaunchResult launch = workflow.LaunchApply(stage);

        Assert.True(launch.Launched);
        Assert.Empty(scenario.InstallState.WrittenPendingApplies);
        Assert.Contains("its process id is unknown", launch.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnswersWhatIsPendingWithoutRunningACheck()
    {
        UpdateScenario scenario = UpdateScenario
            .WithAvailableUpdate()
            .WithPendingApply(Pid, "0.6.0", PendingApply.UpdateKind);

        PendingApply? pending = scenario.Build().GetActivePendingApply();

        Assert.NotNull(pending);
        Assert.Equal(Pid, pending.Pid);
        Assert.Equal(Pid, Assert.Single(scenario.Launcher.ProbedPids));
    }
}
