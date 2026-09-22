using WmpToolsManager.UI;
using WmpToolsManager.UnitTests.Fakes;

namespace WmpToolsManager.UnitTests;

public sealed class UpdateViewModelTests
{
    [Fact]
    public void StartsInTheCheckingStateBeforeAnythingHasRun()
    {
        UpdateViewModel viewModel = new(UpdateScenario.WithAvailableUpdate().Build());

        Assert.Equal("WMP Tools Manager - Updates", viewModel.Title);
        Assert.Equal(UpdateViewModel.CheckingMessage, viewModel.StatusMessage);
        Assert.False(viewModel.HasChecked);
        Assert.False(viewModel.CanDownload);
        Assert.False(viewModel.IsRollbackVisible);
        Assert.False(viewModel.IsUpdateStaged);
        Assert.Empty(viewModel.Plugins);
        Assert.Empty(viewModel.Errors);
        Assert.Equal("unknown", viewModel.InstalledVersionText);
        Assert.Equal("unknown", viewModel.LatestVersionText);
    }

    [Fact]
    public void RefusesToBeBuiltWithoutAWorkflow() => Assert.Throws<ArgumentNullException>(() => new UpdateViewModel(null!));

    [Fact]
    public async Task ShowsAnAvailableUpdateWithBothVersionsAndItsNotes()
    {
        UpdateViewModel viewModel = new(UpdateScenario.WithAvailableUpdate().Build());

        await viewModel.CheckAsync();

        Assert.True(viewModel.HasChecked);
        Assert.Equal("0.5.0", viewModel.InstalledVersionText);
        Assert.Equal("0.6.0", viewModel.LatestVersionText);
        Assert.Equal("WMP Inventor Tools 0.6.0 is available. You have 0.5.0.", viewModel.Summary);
        Assert.Equal("Select Download and install to stage this release.", viewModel.StatusMessage);
        Assert.True(viewModel.HasNotes);
        Assert.Contains("The update command.", viewModel.NotesExcerpt, StringComparison.Ordinal);
        Assert.True(viewModel.CanDownload);
        Assert.False(viewModel.HasErrors);
        Assert.Single(viewModel.Plugins);
    }

    [Fact]
    public async Task ShowsUpToDateAndOffersNoDownload()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        scenario.InstallState.Installed = WmpToolsManager.Core.ReleaseJson.ParseInstalledState(
            UpdateScenario.InstalledJson.Replace("0.5.0", "0.6.0", StringComparison.Ordinal));
        UpdateViewModel viewModel = new(scenario.Build());

        await viewModel.CheckAsync();

        Assert.Equal("WMP Inventor Tools 0.6.0 is the latest release.", viewModel.StatusMessage);
        Assert.False(viewModel.CanDownload);
    }

    [Fact]
    public async Task ShowsANetworkFailureAsAProblemRatherThanThrowing()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        scenario.Source.Sums = new(null, "'https://github.invalid' did not answer within 10 seconds.");
        UpdateViewModel viewModel = new(scenario.Build());

        await viewModel.CheckAsync();

        Assert.True(viewModel.HasErrors);
        Assert.Contains("did not answer", Assert.Single(viewModel.Errors), StringComparison.Ordinal);
        Assert.False(viewModel.CanDownload);
        Assert.False(viewModel.HasNotes);
    }

    [Fact]
    public async Task ReplacesTheInstalledPluginListWithTheStagedCatalog()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        UpdateViewModel viewModel = new(scenario.Build());
        await viewModel.CheckAsync();
        Assert.Equal("file-naming-manager", Assert.Single(viewModel.Plugins).DisplayName);

        await viewModel.DownloadAndStageAsync();

        Assert.Equal(2, viewModel.Plugins.Count);
        Assert.Equal("File Naming Manager", viewModel.Plugins[0].DisplayName);
        Assert.Equal("WMP Tools Manager", viewModel.Plugins[1].DisplayName);
        Assert.All(viewModel.Plugins, plugin =>
            Assert.Equal("beta - not yet validated in live Inventor", plugin.Maturity));
    }

    [Fact]
    public async Task TellsTheUserToCloseInventorOnceTheUpdateIsStagedAndLaunched()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        UpdateViewModel viewModel = new(scenario.Build());
        await viewModel.CheckAsync();

        await viewModel.DownloadAndStageAsync();

        Assert.True(viewModel.IsUpdateStaged);
        Assert.StartsWith(UpdateViewModel.StagedMessage, viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.False(viewModel.CanDownload);
        Assert.Single(scenario.Launcher.Launches);
    }

    [Fact]
    public async Task ShowsAVerificationFailureAndStartsNothing()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        scenario.HashVerifier.Results[UpdateScenario.PackageName] = new("deadbeef", null);
        UpdateViewModel viewModel = new(scenario.Build());
        await viewModel.CheckAsync();

        await viewModel.DownloadAndStageAsync();

        Assert.False(viewModel.IsUpdateStaged);
        Assert.Equal("The downloaded package failed verification and was not applied.", viewModel.StatusMessage);
        Assert.True(viewModel.HasErrors);
        Assert.Empty(scenario.Launcher.Launches);
    }

    [Fact]
    public async Task DoesNothingWhenAskedToStageBeforeAnyCheck()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        UpdateViewModel viewModel = new(scenario.Build());

        await viewModel.DownloadAndStageAsync();

        Assert.False(viewModel.IsUpdateStaged);
        Assert.Empty(scenario.Launcher.Launches);
        Assert.Equal(UpdateViewModel.CheckingMessage, viewModel.StatusMessage);
    }

    [Fact]
    public async Task OffersRollbackOnlyWhenAnArchivedInstallExists()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        UpdateViewModel viewModel = new(scenario.Build());
        await viewModel.CheckAsync();
        Assert.False(viewModel.IsRollbackVisible);

        scenario.InstallState.PreviousInstallExists = true;
        await viewModel.CheckAsync();

        Assert.True(viewModel.IsRollbackVisible);
    }

    [Fact]
    public async Task RollsBackThroughTheStagedInstallerAndReportsIt()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        scenario.InstallState.PreviousInstallExists = true;
        scenario.InstallState.StagedInstaller =
            Path.Combine(UpdateScenario.StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");
        UpdateViewModel viewModel = new(scenario.Build());
        await viewModel.CheckAsync();

        viewModel.RollbackToPrevious();

        Assert.Contains("the previous version is restored automatically", viewModel.StatusMessage, StringComparison.Ordinal);
        (string fileName, string arguments) = Assert.Single(scenario.Launcher.Launches);
        Assert.Equal("powershell.exe", fileName);
        Assert.Contains("-Rollback -WaitForInventor", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RaisesPropertyChangedForTheStateTheWindowBindsTo()
    {
        UpdateViewModel viewModel = new(UpdateScenario.WithAvailableUpdate().Build());
        List<string?> changed = [];
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.CheckAsync();

        Assert.Contains(nameof(UpdateViewModel.Summary), changed);
        Assert.Contains(nameof(UpdateViewModel.StatusMessage), changed);
        Assert.Contains(nameof(UpdateViewModel.LatestVersionText), changed);
        Assert.Contains(nameof(UpdateViewModel.CanDownload), changed);
        Assert.Contains(nameof(UpdateViewModel.IsRollbackVisible), changed);
        Assert.Contains(nameof(UpdateViewModel.HasNotes), changed);
    }
}
