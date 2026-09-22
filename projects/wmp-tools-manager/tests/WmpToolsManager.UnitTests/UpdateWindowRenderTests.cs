// Purpose: Guard that UpdateWindow actually renders its plugin table, notes, problem list and buttons
//   without WPF binding failures, and that the rendered visual tree carries the expected containers and
//   text - not just that Show() ran without throwing.
// Inputs: An UpdateViewModel driven to the "staged" state over the fake ports, so the grid holds the
//   two plugins of the staged catalog.json with their maturity.
// Outputs: A hard failure (thrown exception, collected binding-error trace, or a visual tree missing
//   the expected DataGridRow containers or text) if the template did not actually render.
// Dependencies: WmpToolsManager.UI window/view-model, WPF's own data-binding trace source, and
//   System.Windows.Media.VisualTreeHelper.
// Assumptions: The view model is driven to its final state on the test thread before the window is
//   shown, so the STA thread does no asynchronous work and the assertions never race a dispatcher.
//   UpdateWindow re-checks on Loaded only when HasChecked is false, which this fixture has already set.
//   A WPF window that nobody ever showed crashed on open in this workspace once because Run.Text binds
//   TwoWay by default against a read-only property (see SmartExportWindowRenderTests); this window uses
//   plain single-binding TextBlocks only. DataGrid virtualizes by default, but a grid with two rows
//   realizes every row once UpdateLayout() returns, so the fixture is intentionally small.
// Validation source: the worker specification's UI acceptance criterion (render test asserting
//   realized rows), mirroring FileNamingWindowRenderTests.

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WmpToolsManager.Core;
using WmpToolsManager.UI;
using WmpToolsManager.UnitTests.Fakes;

namespace WmpToolsManager.UnitTests;

public sealed class UpdateWindowRenderTests
{
    [Fact]
    public async Task UpdateWindowShowsTheStagedPluginTableWithoutBindingFailures()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        scenario.InstallState.PreviousInstallExists = true;
        scenario.InstallState.StagedInstaller =
            Path.Combine(UpdateScenario.StateRoot, "staging", "0.6.0", "Install-WmpInventorTools.ps1");

        UpdateViewModel viewModel = new(scenario.Build());
        await viewModel.CheckAsync();
        await viewModel.DownloadAndStageAsync();

        Assert.Equal(2, viewModel.Plugins.Count);
        Assert.True(viewModel.IsRollbackVisible);

        Exception? threadException = null;
        List<string> bindingErrors = [];
        BindingErrorListener listener = new(bindingErrors);
        int dataGridRowCount = -1;
        int visibleRollbackButtons = 0;
        List<string> renderedTexts = [];

        Thread staThread = new(() =>
        {
            try
            {
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
                PresentationTraceSources.Refresh();
                try
                {
                    UpdateWindow window = new(viewModel)
                    {
                        Left = -10000,
                        Top = -10000,
                    };
                    window.Show();
                    window.UpdateLayout();

                    List<DependencyObject> visualDescendants = [];
                    CollectVisualDescendants(window, visualDescendants);
                    dataGridRowCount = visualDescendants.OfType<DataGridRow>().Count();
                    visibleRollbackButtons = visualDescendants
                        .OfType<Button>()
                        .Count(button => button.Visibility == Visibility.Visible
                            && string.Equals(button.Content as string, "Roll back to previous", StringComparison.Ordinal));
                    renderedTexts = [.. visualDescendants.OfType<TextBlock>().Select(textBlock => textBlock.Text)];

                    window.Close();
                }
                finally
                {
                    PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                }
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        Assert.True(threadException is null, $"Showing UpdateWindow threw: {threadException}");
        Assert.True(
            bindingErrors.Count == 0,
            $"WPF reported data-binding errors while showing UpdateWindow: {string.Join(Environment.NewLine, bindingErrors)}");

        Assert.True(
            dataGridRowCount >= viewModel.Plugins.Count,
            $"Expected at least {viewModel.Plugins.Count} realized DataGridRow containers (one per plugin), " +
            $"but only {dataGridRowCount} were found.");

        Assert.Contains("File Naming Manager", renderedTexts);
        Assert.Contains("WMP Tools Manager", renderedTexts);
        Assert.Contains("beta - not yet validated in live Inventor", renderedTexts);
        Assert.Contains("0.5.0", renderedTexts);
        Assert.Contains("0.6.0", renderedTexts);
        Assert.Contains(viewModel.StatusMessage, renderedTexts);
        Assert.Contains(viewModel.NotesExcerpt, renderedTexts);
        Assert.Equal(1, visibleRollbackButtons);
    }

    [Fact]
    public async Task UpdateWindowHidesTheRollbackButtonWithNothingArchived()
    {
        UpdateScenario scenario = UpdateScenario.WithAvailableUpdate();
        UpdateViewModel viewModel = new(scenario.Build());
        await viewModel.CheckAsync();

        Assert.False(viewModel.IsRollbackVisible);

        Exception? threadException = null;
        int visibleRollbackButtons = -1;

        Thread staThread = new(() =>
        {
            try
            {
                UpdateWindow window = new(viewModel)
                {
                    Left = -10000,
                    Top = -10000,
                };
                window.Show();
                window.UpdateLayout();

                List<DependencyObject> visualDescendants = [];
                CollectVisualDescendants(window, visualDescendants);
                visibleRollbackButtons = visualDescendants
                    .OfType<Button>()
                    .Count(button => button.Visibility == Visibility.Visible
                        && string.Equals(button.Content as string, "Roll back to previous", StringComparison.Ordinal));

                window.Close();
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        Assert.True(threadException is null, $"Showing UpdateWindow threw: {threadException}");
        Assert.Equal(0, visibleRollbackButtons);
    }

    [Fact]
    public async Task UpdateWindowDisablesBothLaunchingButtonsWhileAnApplyIsWaiting()
    {
        UpdateScenario scenario = UpdateScenario
            .WithAvailableUpdate()
            .WithPendingApply(RecordingProcessLauncher.LaunchedPid, "0.6.0", PendingApply.UpdateKind);
        scenario.InstallState.PreviousInstallExists = true;

        UpdateViewModel viewModel = new(scenario.Build());
        await viewModel.CheckAsync();

        Assert.True(viewModel.IsApplyPending);

        Exception? threadException = null;
        List<string> bindingErrors = [];
        BindingErrorListener listener = new(bindingErrors);
        bool? downloadEnabled = null;
        bool? rollbackEnabled = null;
        bool? checkEnabled = null;
        List<string> renderedTexts = [];

        Thread staThread = new(() =>
        {
            try
            {
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
                PresentationTraceSources.Refresh();
                try
                {
                    UpdateWindow window = new(viewModel)
                    {
                        Left = -10000,
                        Top = -10000,
                    };
                    window.Show();
                    window.UpdateLayout();

                    List<DependencyObject> visualDescendants = [];
                    CollectVisualDescendants(window, visualDescendants);
                    List<Button> buttons = [.. visualDescendants.OfType<Button>()];
                    downloadEnabled = FindButton(buttons, "Download and install")?.IsEnabled;
                    rollbackEnabled = FindButton(buttons, "Roll back to previous")?.IsEnabled;
                    checkEnabled = FindButton(buttons, "Check again")?.IsEnabled;
                    renderedTexts = [.. visualDescendants.OfType<TextBlock>().Select(textBlock => textBlock.Text)];

                    window.Close();
                }
                finally
                {
                    PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                }
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        Assert.True(threadException is null, $"Showing UpdateWindow threw: {threadException}");
        Assert.True(
            bindingErrors.Count == 0,
            $"WPF reported data-binding errors while showing UpdateWindow: {string.Join(Environment.NewLine, bindingErrors)}");

        Assert.False(downloadEnabled, "Download and install stayed enabled while an apply was waiting.");
        Assert.False(rollbackEnabled, "Roll back to previous stayed enabled while an apply was waiting.");
        Assert.True(checkEnabled, "Check again must stay enabled so the user can re-evaluate.");
        Assert.Contains(
            "An update to 0.6.0 is already waiting for Inventor to close (PowerShell process 4242). "
            + "Close Inventor to let it finish.",
            renderedTexts);
    }

    private static Button? FindButton(IEnumerable<Button> buttons, string content) =>
        buttons.FirstOrDefault(button => string.Equals(button.Content as string, content, StringComparison.Ordinal));

    private static void CollectVisualDescendants(DependencyObject root, List<DependencyObject> results)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            results.Add(child);
            CollectVisualDescendants(child, results);
        }
    }

    private sealed class BindingErrorListener(List<string> messages) : TraceListener
    {
        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                messages.Add(message);
            }
        }
    }
}
