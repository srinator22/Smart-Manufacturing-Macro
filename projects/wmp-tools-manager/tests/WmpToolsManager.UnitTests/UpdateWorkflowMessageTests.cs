// Pins the exact sentences the update dialog shows and the exact guard that stops an unverified stage
// from being applied. These are the only description of the workflow a user ever sees, so a message
// that silently became empty, or a guard that stopped checking one of its three paths, is a defect
// nothing else in the suite would notice.

using WmpToolsManager.Application;
using WmpToolsManager.Core;
using WmpToolsManager.UnitTests.Fakes;

namespace WmpToolsManager.UnitTests;

public sealed class UpdateWorkflowMessageTests
{
    private const string NothingStaged = "Nothing verified is staged, so no update was started.";

    private readonly UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();

    [Fact]
    public async Task CreatesTheStagingFolderAndWritesTheDigestFileItVerifiedAgainst()
    {
        UpdateWorkflow workflow = scenario.Build();
        UpdateCheck check = await workflow.CheckForUpdateAsync();

        StageResult stage = await workflow.StageUpdateAsync(check);

        string stagingDirectory = Path.Combine(UpdateScenario.StateRoot, "staging", "0.6.0");
        Assert.Equal(stagingDirectory, Assert.Single(scenario.InstallState.CreatedDirectories));
        KeyValuePair<string, string> written = Assert.Single(scenario.InstallState.WrittenFiles);
        Assert.Equal(Path.Combine(stagingDirectory, "SHA256SUMS.txt"), written.Key);
        Assert.Equal(UpdateScenario.PublishedSums, written.Value);
        Assert.Equal(stage.PackageZipPath, Assert.Single(scenario.InstallState.ReadCatalogPaths));
    }

    [Fact]
    public async Task PinsTheStagedAndAppliedMessages()
    {
        UpdateWorkflow workflow = scenario.Build();
        UpdateCheck check = await workflow.CheckForUpdateAsync();

        StageResult stage = await workflow.StageUpdateAsync(check);
        Assert.Equal("WMP Inventor Tools 0.6.0 is downloaded and verified.", stage.Message);

        LaunchResult launch = workflow.LaunchApply(stage);
        Assert.Equal(
            "Close Inventor to finish; the update applies automatically. "
            + "Leave the PowerShell window open until it reports the installed version.",
            launch.Message);
    }

    [Fact]
    public void PinsTheRollbackMessages()
    {
        scenario.InstallState.PreviousInstallExists = true;
        UpdateWorkflow workflow = scenario.Build();

        Assert.Equal(
            $"No copy of Install-WmpInventorTools.ps1 was found under "
            + $"'{UpdateScenario.StateRoot}'. "
            + "Roll back from PowerShell with the command in this project's README instead.",
            workflow.RollbackToPrevious().Message);

        scenario.InstallState.StagedInstaller =
            Path.Combine(UpdateScenario.StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");
        Assert.Equal(
            "Close Inventor to finish; the previous version is restored automatically. "
            + "Leave the PowerShell window open until it reports the restored version.",
            workflow.RollbackToPrevious().Message);
    }

    [Fact]
    public void PinsTheMessageForAnEmptyPreviousRoot()
    {
        UpdateWorkflow workflow = scenario.Build();

        Assert.Equal(
            $"There is no archived install under '{Path.Combine(UpdateScenario.StateRoot, "previous")}'.",
            workflow.RollbackToPrevious().Message);
    }

    [Fact]
    public async Task ExplainsAnInstalledStateItCouldNotFind()
    {
        scenario.InstallState.Installed = new(null, "No installed.json was found.");
        UpdateWorkflow workflow = scenario.Build();

        UpdateCheck check = await workflow.CheckForUpdateAsync();

        Assert.Equal(
            $"No installed.json was found. Expected it at "
            + $"'{Path.Combine(UpdateScenario.StateRoot, "installed.json")}'. "
            + "This add-in was probably installed from a working tree rather than a release.",
            Assert.Single(check.Errors));
    }

    [Fact]
    public async Task ExplainsAnInstalledVersionItCouldNotParse()
    {
        scenario.InstallState.Installed = ReleaseJson.ParseInstalledState("""{ "version": "nightly" }""");
        UpdateWorkflow workflow = scenario.Build();

        UpdateCheck check = await workflow.CheckForUpdateAsync();

        Assert.Equal(
            "installed.json records the version 'nightly', which is not MAJOR.MINOR.PATCH.",
            Assert.Single(check.Errors));
    }

    [Fact]
    public async Task ExplainsWhyThereIsNothingToStage()
    {
        scenario.InstallState.Installed = ReleaseJson.ParseInstalledState(
            UpdateScenario.InstalledJson.Replace("0.5.0", "0.6.0", StringComparison.Ordinal));
        UpdateWorkflow workflow = scenario.Build();
        UpdateCheck check = await workflow.CheckForUpdateAsync();

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.Equal("There is no update to download.", stage.Message);
        Assert.Equal("WMP Inventor Tools 0.6.0 is the latest release.", Assert.Single(stage.Errors));
    }

    [Fact]
    public async Task RefusesToApplyAStageMissingAnyOneOfItsThreePaths()
    {
        UpdateWorkflow workflow = scenario.Build();
        UpdateCheck check = await workflow.CheckForUpdateAsync();
        StageResult verified = await workflow.StageUpdateAsync(check);
        Assert.True(verified.Verified);

        Assert.Equal(NothingStaged, workflow.LaunchApply(verified with { Verified = false }).Message);
        Assert.Equal(NothingStaged, workflow.LaunchApply(verified with { PackageZipPath = null }).Message);
        Assert.Equal(NothingStaged, workflow.LaunchApply(verified with { Sha256SumsPath = null }).Message);
        Assert.Equal(NothingStaged, workflow.LaunchApply(verified with { InstallerScriptPath = null }).Message);
        Assert.Empty(scenario.Launcher.Launches);

        Assert.True(workflow.LaunchApply(verified).Launched);
        Assert.Single(scenario.Launcher.Launches);
    }

    [Fact]
    public void ReportsAFailedStageWithNoPathsAtAll()
    {
        StageResult failed = StageResult.Failed("it went wrong", ["because"]);

        Assert.False(failed.Verified);
        Assert.Null(failed.Version);
        Assert.Null(failed.PackageZipPath);
        Assert.Null(failed.Sha256SumsPath);
        Assert.Null(failed.InstallerScriptPath);
        Assert.Empty(failed.Plugins);
        Assert.Equal("it went wrong", failed.Message);
        Assert.Equal("because", Assert.Single(failed.Errors));
    }

    [Fact]
    public void DescribesAnInstalledPluginByItsIdBecauseTheCatalogIsNotOnDisk()
    {
        PluginSummary summary = PluginSummary.FromInstalled(new InstalledPlugin { Id = "wmp-tools-manager", Maturity = "beta" });

        Assert.Equal("wmp-tools-manager", summary.Id);
        Assert.Equal("wmp-tools-manager", summary.DisplayName);
        Assert.Equal(string.Empty, summary.Description);
        Assert.Equal("beta - not yet validated in live Inventor", summary.MaturityDescription);
    }

    [Fact]
    public void FallsBackToTheCatalogIdOnlyWhenTheDisplayNameIsMissing()
    {
        Assert.Equal(
            "a",
            PluginSummary.FromCatalog(new CatalogPlugin { Id = "a", Maturity = "stable" }).DisplayName);
        Assert.Equal(
            "A Plugin",
            PluginSummary.FromCatalog(new CatalogPlugin { Id = "a", DisplayName = "A Plugin", Maturity = "stable" }).DisplayName);
    }

    [Fact]
    public void RefusesToSummariseNothing()
    {
        Assert.Throws<ArgumentNullException>(() => PluginSummary.FromInstalled(null!));
        Assert.Throws<ArgumentNullException>(() => PluginSummary.FromCatalog(null!));
    }
}
