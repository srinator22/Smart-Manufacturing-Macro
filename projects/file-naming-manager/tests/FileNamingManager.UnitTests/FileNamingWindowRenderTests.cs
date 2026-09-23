// Purpose: Guard that FileNamingWindow actually renders its report grid and controls without WPF binding
//   failures, and that the rendered visual tree carries the expected containers and text - not just that
//   Show() ran without throwing.
// Inputs: A FileNamingViewModel built over a fixture with a root, an unnumbered part, and a
//   Vault-managed part, in Apply mode.
// Outputs: A hard failure (thrown exception, collected binding-error trace, or a visual tree missing the
//   expected DataGridRow containers, include-column CheckBox containers, Select all / Select none buttons,
//   project number TextBox, or TextBlock text) if the template did not actually render the report.
// Dependencies: FileNamingManager.UI window/view-model, WPF's own data-binding trace source, and
//   System.Windows.Media.VisualTreeHelper.
// Assumptions: Runs on a dedicated STA thread with no live System.Windows.Application; nothing in the gate
//   otherwise ever calls Window.Show(), which is why SmartExportWindow's D4 defect (TwoWay binding on a
//   read-only Run.Text) went undetected there - this view model avoids that pattern entirely (plain
//   single-binding TextBlocks). WPF DataGrid virtualizes by default, but a grid with only two rows realizes
//   every row once UpdateLayout() returns, so this fixture is intentionally small.
// Validation source: .work/TASK.md UI acceptance criterion (render test asserting realized rows). Mutation
//   sanity: temporarily misspelling a column's Binding path in FileNamingWindow.xaml reproduces a WPF
//   binding-error trace and fails this test's zero-binding-error assertion; the XAML was restored
//   byte-identical afterward (see worker report for the diff).

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UI;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public sealed class FileNamingWindowRenderTests
{
    private const string ProjectRoot = @"C:\WMP\P124 GRM";

    [Fact]
    public void FileNamingWindowShowsTheReportGridWithoutBindingFailures()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string unnumberedPartPath = Path.Combine(ProjectRoot, "Bracket.ipt");
        string vaultPartPath = Path.Combine(ProjectRoot, "Widget.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                new(rootPath, DocumentKind.Assembly, true, true, false, false, [], [], null),
                new(unnumberedPartPath, DocumentKind.Part, false, true, false, false, [rootPath], [], null),
                new(vaultPartPath, DocumentKind.Part, false, true, false, false, [rootPath], [], null),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, unnumberedPartPath, vaultPartPath]);
        fileSystem.MarkVaultManaged(vaultPartPath);

        NamingAnalysis analysis = workflow.Analyze(null);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        Exception? threadException = null;
        List<string> bindingErrors = [];
        BindingErrorListener listener = new(bindingErrors);
        int dataGridRowCount = -1;
        int includeCheckBoxCount = -1;
        bool projectNumberTextBoxFound = false;
        List<string> renderedTexts = [];
        List<string> buttonContents = [];

        Thread staThread = new(() =>
        {
            try
            {
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
                PresentationTraceSources.Refresh();
                try
                {
                    FileNamingWindow window = new(viewModel)
                    {
                        Left = -10000,
                        Top = -10000,
                    };
                    window.Show();
                    window.UpdateLayout();

                    List<DependencyObject> visualDescendants = [];
                    CollectVisualDescendants(window, visualDescendants);
                    dataGridRowCount = visualDescendants.OfType<DataGridRow>().Count();
                    projectNumberTextBoxFound = visualDescendants.OfType<TextBox>().Any();
                    renderedTexts = [.. visualDescendants.OfType<TextBlock>().Select(textBlock => textBlock.Text)];

                    // Only the include column's checkboxes carry a row view model as their DataContext;
                    // the option checkboxes above the grid bind against FileNamingViewModel itself.
                    includeCheckBoxCount = visualDescendants
                        .OfType<CheckBox>()
                        .Count(checkBox => checkBox.DataContext is NamingRowViewModel);
                    buttonContents = [.. visualDescendants
                        .OfType<Button>()
                        .Select(button => button.Content as string ?? string.Empty)];

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

        Assert.True(threadException is null, $"Showing FileNamingWindow threw: {threadException}");
        Assert.True(
            bindingErrors.Count == 0,
            $"WPF reported data-binding errors while showing FileNamingWindow: {string.Join(Environment.NewLine, bindingErrors)}");

        Assert.True(
            dataGridRowCount >= viewModel.Rows.Count,
            $"Expected at least {viewModel.Rows.Count} realized DataGridRow containers (one per report row), " +
            $"but only {dataGridRowCount} were found.");
        Assert.True(projectNumberTextBoxFound, "No project number TextBox was found in the rendered visual tree.");

        Assert.True(
            includeCheckBoxCount >= viewModel.Rows.Count,
            $"Expected at least {viewModel.Rows.Count} realized include CheckBox containers (one per report row), " +
            $"but only {includeCheckBoxCount} were found.");
        Assert.Contains("Select all", buttonContents);
        Assert.Contains("Select none", buttonContents);

        NamingRowViewModel unnumberedRow = Assert.Single(viewModel.Rows, row => row.CurrentFileName == "Bracket.ipt");
        Assert.NotEmpty(unnumberedRow.ProposedFileName);
        Assert.Contains(unnumberedRow.CurrentFileName, renderedTexts);
        Assert.Contains(unnumberedRow.ProposedFileName, renderedTexts);
    }

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
