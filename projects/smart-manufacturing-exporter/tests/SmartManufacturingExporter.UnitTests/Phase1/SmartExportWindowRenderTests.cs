// Purpose: Guard that SmartExportWindow actually renders its hierarchy template without WPF binding
//   failures, and that the rendered visual tree carries the expected containers and text - not just
//   that Show() ran without throwing.
// Inputs: A SmartExportViewModel built over a three-level, duplicate-occurrence, mixed-kind fixture.
// Outputs: A hard failure (thrown exception, collected binding-error trace, or a visual tree missing the
//   expected TreeViewItem containers, tri-state CheckBoxes, or TextBlock text) if the template did not
//   actually render the hierarchy.
// Dependencies: SmartManufacturingExporter.UI Phase1 window/view-model, WPF's own data-binding trace
//   source, and System.Windows.Media.VisualTreeHelper.
// Assumptions: Runs on a dedicated STA thread with no live System.Windows.Application; nothing in the gate
//   otherwise ever calls Window.Show(), which is why D4 (TwoWay binding on a read-only property) went
//   undetected. VisualTreeHelper must be walked on that same STA thread, so the walk happens before
//   window.Close() and the results are carried out via captured locals, the same way threadException is.
//   WPF TreeView does not virtualize by default and this XAML sets no virtualization property, so every
//   expanded item is expected to be realized once UpdateLayout() returns.
// Validation source: Reproduces the XamlParseException recorded for defect D4 and must stay green
//   afterward. Also reproduces, and must fail against, a template deleted or a StringFormat corrupted -
//   see the mutation sanity check recorded for this file's strengthening.

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SmartManufacturingExporter.Application.Phase1;
using SmartManufacturingExporter.Core.Phase1;
using SmartManufacturingExporter.UI.Phase1;

namespace SmartManufacturingExporter.UnitTests.Phase1;

public sealed class SmartExportWindowRenderTests
{
    [Fact]
    public void SmartExportWindowShowsTheHierarchyTemplateWithoutBindingFailures()
    {
        FakeGateway gateway = new();
        FakeFileSystem fileSystem = new();
        SmartExportWorkflow workflow = new(gateway, fileSystem);
        Phase1StartResult session = workflow.Start();
        SmartExportViewModel viewModel = new(workflow, session) { DestinationDirectory = @"C:\Exports" };
        viewModel.SelectedScope = ExportScopeMode.AssembliesAndParts;
        viewModel.ExpandAll();

        Exception? threadException = null;
        List<string> bindingErrors = [];
        BindingErrorListener listener = new(bindingErrors);
        int treeViewItemCount = -1;
        int triStateCheckBoxCount = -1;
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
                    SmartExportWindow window = new(viewModel)
                    {
                        Left = -10000,
                        Top = -10000,
                        Height = 720,
                    };
                    window.Show();
                    window.UpdateLayout();

                    List<DependencyObject> visualDescendants = [];
                    CollectVisualDescendants(window, visualDescendants);
                    treeViewItemCount = visualDescendants.OfType<TreeViewItem>().Count();
                    triStateCheckBoxCount = visualDescendants.OfType<CheckBox>().Count(checkBox => checkBox.IsThreeState);
                    renderedTexts = visualDescendants.OfType<TextBlock>().Select(textBlock => textBlock.Text).ToList();

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

        Assert.True(threadException is null, $"Showing SmartExportWindow threw: {threadException}");
        Assert.True(
            bindingErrors.Count == 0,
            $"WPF reported data-binding errors while showing SmartExportWindow: {string.Join(Environment.NewLine, bindingErrors)}");

        List<SmartExportTreeNodeViewModel> fixtureNodes = viewModel.RootNode.DescendantsAndSelf().ToList();
        int expectedMinimumTreeViewItems = fixtureNodes.Count(node => node.CanSelect);
        (int Depth, SmartExportTreeNodeViewModel Node) deepest = FindDeepest(viewModel.RootNode, 0);
        SmartExportTreeNodeViewModel? duplicatedPart = fixtureNodes.FirstOrDefault(
            node => string.Equals(node.SourcePath, @"C:\Models\Beta.ipt", StringComparison.OrdinalIgnoreCase));
        Assert.True(duplicatedPart is not null, "Fixture no longer contains the duplicated Beta.ipt occurrence the render test depends on.");
        string expectedQuantityText = $"Qty {duplicatedPart!.Quantity}";

        Assert.True(
            treeViewItemCount > 0,
            "No TreeViewItem containers were generated - the hierarchy template is not rendering the tree at all.");
        Assert.True(
            treeViewItemCount >= expectedMinimumTreeViewItems,
            $"Expected at least {expectedMinimumTreeViewItems} realized TreeViewItem containers (one per selectable fixture node), " +
            $"but only {treeViewItemCount} were found - the hierarchy did not fully expand.");
        Assert.True(
            triStateCheckBoxCount > 0,
            "No tri-state CheckBox rendered - the template's IsThreeState CheckBox is not being produced.");
        Assert.Contains(viewModel.RootNode.DisplayName, renderedTexts);
        Assert.Contains(deepest.Node.DisplayName, renderedTexts);
        Assert.Contains("[Assembly]", renderedTexts);
        Assert.Contains("[Part]", renderedTexts);
        Assert.True(
            renderedTexts.Contains(expectedQuantityText),
            $"Expected a TextBlock with text \"{expectedQuantityText}\" for the duplicated part, " +
            $"but rendered texts were: {string.Join(" | ", renderedTexts)}");
    }

    // Walks the STA thread's visual tree (the logical tree is not enough: HierarchicalDataTemplate
    // content is realized as visuals) collecting every descendant so the test can assert on what the
    // template actually produced, not merely that no exception or binding error occurred.
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

    // Finds the deepest node in the fixture tree by walking SmartExportTreeNodeViewModel.Children,
    // so the expected "deepest part" assertion tracks the fixture instead of a hardcoded node name.
    private static (int Depth, SmartExportTreeNodeViewModel Node) FindDeepest(SmartExportTreeNodeViewModel node, int depth)
    {
        (int Depth, SmartExportTreeNodeViewModel Node) deepest = (depth, node);
        foreach (SmartExportTreeNodeViewModel child in node.Children)
        {
            (int Depth, SmartExportTreeNodeViewModel Node) candidate = FindDeepest(child, depth + 1);
            if (candidate.Depth > deepest.Depth)
            {
                deepest = candidate;
            }
        }

        return deepest;
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

    private sealed class FakeGateway : IInventorPhase1Gateway
    {
        public ActiveAssemblyScan? ScanActiveAssembly() => new(
            @"C:\Models\Machine.iam",
            [
                new(
                    "SubA:1",
                    @"C:\Models\SubA.iam",
                    ComponentDocumentKind.Assembly,
                    false,
                    [
                        new(
                            "Inner:1",
                            @"C:\Models\Inner.iam",
                            ComponentDocumentKind.Assembly,
                            false,
                            [new("Gamma:1", @"C:\Models\Gamma.ipt", ComponentDocumentKind.Part, false, [])]),
                        new("Beta:1", @"C:\Models\Beta.ipt", ComponentDocumentKind.Part, false, []),
                        new("Beta:2", @"C:\Models\Beta.ipt", ComponentDocumentKind.Part, false, []),
                    ]),
                new("Reference:1", null, ComponentDocumentKind.Other, false, []),
            ]);

        public void ExportDocumentAsStep(
            string sourcePath,
            string outputPath,
            StepExportPrecision precision)
        {
        }
    }

    private sealed class FakeFileSystem : IPhase1FileSystem
    {
        public bool DirectoryExists(string path) => true;

        public bool CanWriteToDirectory(string path) => true;

        public bool FileExists(string path) => false;
    }
}
