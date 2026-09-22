// Every case here drives the workflow through fakes that complete synchronously, so the assertions
// await the real task rather than a timeout. The point of the suite is that no failure mode escapes as
// an exception: an update check runs inside an Inventor command callback, where one would be fatal.

using WmpToolsManager.Application;
using WmpToolsManager.Core;
using WmpToolsManager.UnitTests.Fakes;

namespace WmpToolsManager.UnitTests;

public sealed class UpdateWorkflowTests
{
    private const string StateRoot = @"C:\Users\Tester\AppData\Local\WMP\InventorTools";
    private const string AddinsRoot = @"C:\Users\Tester\AppData\Roaming\Autodesk\Inventor 2027\Addins";
    private const string PackageDigest = "9f2c4a1b7d8e6f30a1b2c3d4e5f60718293a4b5c6d7e8f901a2b3c4d5e6f7081";
    private const string InstallerDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string PackageName = "WmpInventorTools-0.6.0.zip";

    private const string PublishedSums =
        PackageDigest + "  " + PackageName + "\n" +
        InstallerDigest + "  Install-WmpInventorTools.ps1\n";

    private const string InstalledJson = """
        {
          "version": "0.5.0",
          "installedUtc": "2026-09-20T08:00:00Z",
          "plugins": [
            { "id": "file-naming-manager", "maturity": "beta", "installDirectory": "FileNamingManager", "manifestName": "a.addin" },
            { "id": "wmp-tools-manager", "maturity": "beta", "installDirectory": "WmpToolsManager", "manifestName": "b.addin" }
          ]
        }
        """;

    private const string CatalogJson = """
        {
          "version": "0.6.0",
          "plugins": [
            { "id": "file-naming-manager", "displayName": "File Naming Manager", "description": "Naming.", "maturity": "beta" },
            { "id": "smart-manufacturing-exporter", "displayName": "Smart Manufacturing Exporter", "description": "Export.", "maturity": "stable" }
          ]
        }
        """;

    private readonly FakeReleaseSource source = new();
    private readonly FakeInstallState installState = new();
    private readonly FakeHashVerifier hashVerifier = new();
    private readonly RecordingProcessLauncher launcher = new();

    [Fact]
    public async Task ReportsAnAvailableUpdateWithItsNotesAndInstalledPlugins()
    {
        GivenInstalled(InstalledJson);
        source.Sums = new(PublishedSums, null);
        source.Payload = new("""{ "tag_name": "v0.6.0", "body": "### Added\n- Update command." }""", null);

        UpdateCheck check = await Workflow().CheckForUpdateAsync();

        Assert.Equal(UpdateDecision.UpdateAvailable, check.Decision);
        Assert.Equal(new SemanticVersion(0, 5, 0), check.InstalledVersion);
        Assert.Equal(new SemanticVersion(0, 6, 0), check.LatestVersion);
        Assert.Equal(PackageName, check.PackageFileName);
        Assert.Equal(PublishedSums, check.Sha256SumsContent);
        Assert.Contains("Update command.", check.NotesExcerpt, StringComparison.Ordinal);
        Assert.Equal("v0.6.0", Assert.Single(source.RequestedTags));
        Assert.Equal(2, check.Plugins.Count);
        Assert.Equal("beta - not yet validated in live Inventor", check.Plugins[0].MaturityDescription);
        Assert.Empty(check.Errors);
        Assert.True(check.CanStage);
        Assert.Equal("WMP Inventor Tools 0.6.0 is available. You have 0.5.0.", check.Summary);
    }

    [Fact]
    public async Task ReportsUpToDateWhenTheInstalledVersionEqualsThePublishedOne()
    {
        GivenInstalled(InstalledJson.Replace("0.5.0", "0.6.0", StringComparison.Ordinal));
        source.Sums = new(PublishedSums, null);

        UpdateCheck check = await Workflow().CheckForUpdateAsync();

        Assert.Equal(UpdateDecision.UpToDate, check.Decision);
        Assert.False(check.CanStage);
    }

    [Fact]
    public async Task ReportsAnInstalledBuildAheadOfTheLatestRelease()
    {
        GivenInstalled(InstalledJson.Replace("0.5.0", "0.7.0", StringComparison.Ordinal));
        source.Sums = new(PublishedSums, null);

        UpdateCheck check = await Workflow().CheckForUpdateAsync();

        Assert.Equal(UpdateDecision.InstalledNewer, check.Decision);
        Assert.False(check.CanStage);
    }

    [Fact]
    public async Task ReportsANetworkFailureAsTextAndDecidesNothing()
    {
        GivenInstalled(InstalledJson);
        source.Sums = new(null, "'https://github.invalid' did not answer within 10 seconds.");

        UpdateCheck check = await Workflow().CheckForUpdateAsync();

        Assert.Equal(UpdateDecision.Unknown, check.Decision);
        Assert.Null(check.LatestVersion);
        Assert.Equal(new SemanticVersion(0, 5, 0), check.InstalledVersion);
        Assert.Contains("did not answer within 10 seconds.", Assert.Single(check.Errors), StringComparison.Ordinal);
        Assert.False(check.CanStage);
        Assert.Empty(source.RequestedTags);
    }

    [Fact]
    public async Task ReportsAMalformedDigestFileAndDecidesNothing()
    {
        GivenInstalled(InstalledJson);
        source.Sums = new("nothing useful here", null);

        UpdateCheck check = await Workflow().CheckForUpdateAsync();

        Assert.Equal(UpdateDecision.Unknown, check.Decision);
        Assert.Contains("SHA256SUMS.txt", Assert.Single(check.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplainsAMissingInstalledStateWithoutFailingTheCheck()
    {
        source.Sums = new(PublishedSums, null);

        UpdateCheck check = await Workflow().CheckForUpdateAsync();

        Assert.Equal(UpdateDecision.Unknown, check.Decision);
        Assert.Equal(new SemanticVersion(0, 6, 0), check.LatestVersion);
        Assert.Empty(check.Plugins);
        string error = Assert.Single(check.Errors);
        Assert.Contains("No installed.json was found.", error, StringComparison.Ordinal);
        Assert.Contains(StateRoot, error, StringComparison.Ordinal);
        Assert.False(check.CanStage);
    }

    [Fact]
    public async Task ReportsAnInstalledVersionItCannotParse()
    {
        GivenInstalled("""{ "version": "nightly", "plugins": [] }""");
        source.Sums = new(PublishedSums, null);

        UpdateCheck check = await Workflow().CheckForUpdateAsync();

        Assert.Equal(UpdateDecision.Unknown, check.Decision);
        Assert.Contains("'nightly'", Assert.Single(check.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DegradesToNoNotesWhenTheRateLimitedApiFails()
    {
        GivenInstalled(InstalledJson);
        source.Sums = new(PublishedSums, null);
        source.Payload = new(null, "API rate limit exceeded.");

        UpdateCheck check = await Workflow().CheckForUpdateAsync();

        Assert.Equal(UpdateDecision.UpdateAvailable, check.Decision);
        Assert.True(check.CanStage);
        Assert.Equal(string.Empty, check.NotesExcerpt);
        Assert.Contains("Release notes are unavailable", Assert.Single(check.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DegradesToNoNotesWhenTheApiPayloadIsUnreadable()
    {
        GivenInstalled(InstalledJson);
        source.Sums = new(PublishedSums, null);
        source.Payload = new("not json", null);

        UpdateCheck check = await Workflow().CheckForUpdateAsync();

        Assert.Equal(UpdateDecision.UpdateAvailable, check.Decision);
        Assert.Contains("Release notes are unavailable", Assert.Single(check.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OffersRollbackOnlyWhenAnArchivedInstallExists()
    {
        GivenInstalled(InstalledJson);
        source.Sums = new(PublishedSums, null);

        Assert.False((await Workflow().CheckForUpdateAsync()).HasPreviousInstall);

        installState.PreviousInstallExists = true;
        Assert.True((await Workflow().CheckForUpdateAsync()).HasPreviousInstall);
    }

    [Fact]
    public async Task StagesVerifiesAndListsThePluginsOfTheDownloadedRelease()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        GivenGoodDigests();
        installState.Catalog = ReleaseJson.ParseCatalog(CatalogJson);

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.True(stage.Verified);
        Assert.Empty(stage.Errors);
        Assert.Equal(new SemanticVersion(0, 6, 0), stage.Version);
        Assert.Equal(Path.Combine(StateRoot, "staging", "0.6.0", PackageName), stage.PackageZipPath);
        Assert.Equal(Path.Combine(StateRoot, "staging", "0.6.0", "SHA256SUMS.txt"), stage.Sha256SumsPath);
        Assert.Equal(
            Path.Combine(StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1"),
            stage.InstallerScriptPath);
        Assert.Equal(2, source.RequestedAssets.Count);
        Assert.Equal(PackageName, source.RequestedAssets[0]);
        Assert.Equal("Install-WmpInventorTools.ps1", source.RequestedAssets[1]);
        Assert.Equal(PublishedSums, installState.WrittenFiles[stage.Sha256SumsPath!]);
        Assert.Equal(2, stage.Plugins.Count);
        Assert.Equal("File Naming Manager", stage.Plugins[0].DisplayName);
        Assert.Equal("Smart Manufacturing Exporter", stage.Plugins[1].DisplayName);
        Assert.Equal("stable", stage.Plugins[1].MaturityDescription);
    }

    [Fact]
    public async Task EverythingItWritesStaysUnderTheStateRoot()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        GivenGoodDigests();

        StageResult stage = await workflow.StageUpdateAsync(check);

        StagingPaths paths = new(StateRoot);
        Assert.All(installState.CreatedDirectories, path => Assert.True(paths.IsUnderStateRoot(path)));
        Assert.All(installState.WrittenFiles.Keys, path => Assert.True(paths.IsUnderStateRoot(path)));
        Assert.True(paths.IsUnderStateRoot(stage.PackageZipPath));
        Assert.True(paths.IsUnderStateRoot(stage.InstallerScriptPath));
    }

    [Fact]
    public async Task RefusesToStageWhenThereIsNoUpdate()
    {
        GivenInstalled(InstalledJson.Replace("0.5.0", "0.6.0", StringComparison.Ordinal));
        source.Sums = new(PublishedSums, null);
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await workflow.CheckForUpdateAsync();

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.False(stage.Verified);
        Assert.Equal("There is no update to download.", stage.Message);
        Assert.Empty(source.RequestedAssets);
    }

    [Fact]
    public async Task KeepsAMismatchedDownloadAndRefusesToApplyIt()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        hashVerifier.Results[PackageName] = new("dead" + PackageDigest[4..], null);

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.False(stage.Verified);
        Assert.Null(stage.PackageZipPath);
        string error = Assert.Single(stage.Errors);
        Assert.Contains("SHA-256 mismatch", error, StringComparison.Ordinal);
        Assert.Contains("do not use it", error, StringComparison.Ordinal);
        Assert.Contains(PackageName, error, StringComparison.Ordinal);
        Assert.Equal(new LaunchResult(false, "Nothing verified is staged, so no update was started."), workflow.LaunchApply(stage));
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task RefusesToRunAnInstallerScriptThatFailsVerification()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        hashVerifier.Results[PackageName] = new(PackageDigest, null);
        hashVerifier.Results["Install-WmpInventorTools.ps1"] = new(PackageDigest, null);

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.False(stage.Verified);
        Assert.Contains("failed verification and was not run", stage.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsADigestFileThatCarriesNoInstallerDigest()
    {
        GivenInstalled(InstalledJson);
        source.Sums = new(PackageDigest + "  " + PackageName + "\n", null);
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await workflow.CheckForUpdateAsync();
        hashVerifier.Results[PackageName] = new(PackageDigest, null);

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.False(stage.Verified);
        Assert.Contains("carries no digest for 'Install-WmpInventorTools.ps1'", Assert.Single(stage.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsADownloadThatNeverArrived()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        source.Downloads[PackageName] = new(null, 0, "the connection was reset");

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.False(stage.Verified);
        Assert.Contains($"Downloading {PackageName} failed.", stage.Message, StringComparison.Ordinal);
        Assert.Equal("the connection was reset", Assert.Single(stage.Errors));
    }

    [Fact]
    public async Task ReportsAnInstallerDownloadThatNeverArrived()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        GivenGoodDigests();
        source.Downloads["Install-WmpInventorTools.ps1"] = new(null, 0, "404");

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.False(stage.Verified);
        Assert.Contains("Downloading Install-WmpInventorTools.ps1 failed.", stage.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsAFileSystemFailureWhilePreparingTheStagingFolder()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        installState.ThrowOnWrite = new UnauthorizedAccessException("access is denied");

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.False(stage.Verified);
        Assert.Contains("could not be prepared", stage.Message, StringComparison.Ordinal);
        Assert.Equal("access is denied", Assert.Single(stage.Errors));
    }

    [Fact]
    public async Task ReportsADigestItCannotCompute()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        hashVerifier.Results[PackageName] = new(null, "the file is locked");

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.False(stage.Verified);
        Assert.Equal("the file is locked", Assert.Single(stage.Errors));
    }

    [Fact]
    public async Task StagesAVerifiedPackageEvenWhenItsCatalogCannotBeRead()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        GivenGoodDigests();
        installState.Catalog = new(null, "The package contains no catalog.json.");

        StageResult stage = await workflow.StageUpdateAsync(check);

        Assert.True(stage.Verified);
        Assert.Empty(stage.Plugins);
        Assert.Equal("The package contains no catalog.json.", Assert.Single(stage.Errors));
    }

    [Fact]
    public async Task HandsTheVerifiedPackageToTheInstallerAndTellsTheUserToCloseInventor()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        GivenGoodDigests();
        StageResult stage = await workflow.StageUpdateAsync(check);

        LaunchResult launch = workflow.LaunchApply(stage);

        Assert.True(launch.Launched);
        Assert.Contains("Close Inventor to finish", launch.Message, StringComparison.Ordinal);
        (string fileName, string arguments) = Assert.Single(launcher.Launches);
        Assert.Equal("powershell.exe", fileName);
        Assert.Equal(
            ApplyCommandLine.BuildApplyArguments(
                stage.InstallerScriptPath!, stage.PackageZipPath!, stage.Sha256SumsPath!, AddinsRoot, StateRoot),
            arguments);
        Assert.Contains("-WaitForInventor", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PassesThroughAFailureToStartTheInstaller()
    {
        UpdateWorkflow workflow = Workflow();
        UpdateCheck check = await GivenAnAvailableUpdate(workflow);
        GivenGoodDigests();
        StageResult stage = await workflow.StageUpdateAsync(check);
        launcher.Result = new(false, "'powershell.exe' could not be started: not found");

        LaunchResult launch = workflow.LaunchApply(stage);

        Assert.False(launch.Launched);
        Assert.Equal("'powershell.exe' could not be started: not found", launch.Message);
    }

    [Fact]
    public void RefusesToRollBackWithNothingArchived()
    {
        LaunchResult launch = Workflow().RollbackToPrevious();

        Assert.False(launch.Launched);
        Assert.Contains("There is no archived install", launch.Message, StringComparison.Ordinal);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public void ExplainsRollbackWhenNoInstallerScriptExistsAnywhere()
    {
        installState.PreviousInstallExists = true;

        LaunchResult launch = Workflow().RollbackToPrevious();

        Assert.False(launch.Launched);
        Assert.Contains("No copy of Install-WmpInventorTools.ps1", launch.Message, StringComparison.Ordinal);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public void RollsBackThroughTheStagedInstaller()
    {
        installState.PreviousInstallExists = true;
        string installer = Path.Combine(StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");
        installState.StagedInstaller = installer;

        LaunchResult launch = Workflow().RollbackToPrevious();

        Assert.True(launch.Launched);
        Assert.Contains("the previous version is restored automatically", launch.Message, StringComparison.Ordinal);
        (string fileName, string arguments) = Assert.Single(launcher.Launches);
        Assert.Equal("powershell.exe", fileName);
        Assert.Equal(ApplyCommandLine.BuildRollbackArguments(installer, AddinsRoot, StateRoot), arguments);
        Assert.Contains("-Rollback -WaitForInventor", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void PassesThroughAFailureToStartTheRollback()
    {
        installState.PreviousInstallExists = true;
        installState.StagedInstaller = Path.Combine(StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");
        launcher.Result = new(false, "blocked");

        Assert.Equal(new LaunchResult(false, "blocked"), Workflow().RollbackToPrevious());
    }

    [Fact]
    public void RefusesAnIncompleteComposition()
    {
        StagingPaths paths = new(StateRoot);

        Assert.Throws<ArgumentNullException>(() => new UpdateWorkflow(null!, installState, hashVerifier, launcher, paths, AddinsRoot));
        Assert.Throws<ArgumentNullException>(() => new UpdateWorkflow(source, null!, hashVerifier, launcher, paths, AddinsRoot));
        Assert.Throws<ArgumentNullException>(() => new UpdateWorkflow(source, installState, null!, launcher, paths, AddinsRoot));
        Assert.Throws<ArgumentNullException>(() => new UpdateWorkflow(source, installState, hashVerifier, null!, paths, AddinsRoot));
        Assert.Throws<ArgumentNullException>(() => new UpdateWorkflow(source, installState, hashVerifier, launcher, null!, AddinsRoot));
        Assert.ThrowsAny<ArgumentException>(() => new UpdateWorkflow(source, installState, hashVerifier, launcher, paths, " "));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new UpdateWorkflow(source, installState, hashVerifier, launcher, paths, AddinsRoot, 0));
    }

    [Fact]
    public async Task RefusesANullCheck() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Workflow().StageUpdateAsync(null!));

    [Fact]
    public void RefusesANullStage() => Assert.Throws<ArgumentNullException>(() => Workflow().LaunchApply(null!));

    [Fact]
    public void ExposesTheRootsItWasComposedWith()
    {
        UpdateWorkflow workflow = Workflow();

        Assert.Equal(StateRoot, workflow.Paths.StateRoot);
        Assert.Equal(AddinsRoot, workflow.AddinsRoot);
    }

    private UpdateWorkflow Workflow() =>
        new(source, installState, hashVerifier, launcher, new StagingPaths(StateRoot), AddinsRoot);

    private void GivenInstalled(string json) => installState.Installed = ReleaseJson.ParseInstalledState(json);

    private void GivenGoodDigests()
    {
        hashVerifier.Results[PackageName] = new(PackageDigest, null);
        hashVerifier.Results["Install-WmpInventorTools.ps1"] = new(InstallerDigest, null);
    }

    private async Task<UpdateCheck> GivenAnAvailableUpdate(UpdateWorkflow workflow)
    {
        GivenInstalled(InstalledJson);
        source.Sums = new(PublishedSums, null);
        return await workflow.CheckForUpdateAsync();
    }
}
