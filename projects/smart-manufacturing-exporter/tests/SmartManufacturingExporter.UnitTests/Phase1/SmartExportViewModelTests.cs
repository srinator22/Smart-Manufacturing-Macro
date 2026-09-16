using SmartManufacturingExporter.Application.Phase1;
using SmartManufacturingExporter.Core.Phase1;
using SmartManufacturingExporter.UI.Phase1;

namespace SmartManufacturingExporter.UnitTests.Phase1;

public sealed class SmartExportViewModelTests
{
    private const string Destination = @"C:\Exports";

    [Fact]
    public void ConstructorBuildsFullySelectedRecursivePartsTreeByDefault()
    {
        Fixture fixture = CreateFixture();

        Assert.Equal(ExportScopeMode.PartsRecursive, fixture.ViewModel.SelectedScope);
        Assert.Equal(Enum.GetValues<ExportScopeMode>(), fixture.ViewModel.ScopeOptions);
        Assert.Equal(@"C:\Models\Machine.iam", fixture.ViewModel.RootAssemblyPath);
        Assert.True(fixture.ViewModel.RootNode.IsSelected == true);
        Assert.False(fixture.ViewModel.RootNode.IsExportable);
        Assert.True(fixture.ViewModel.RootNode.IsExpanded);
        Assert.True(fixture.ViewModel.CanExport);

        SmartExportTreeNodeViewModel unsupported = FindNode(fixture.ViewModel, "Reference:1");
        Assert.False(unsupported.IsExportable);
        Assert.False(unsupported.CanSelect);
        Assert.False(unsupported.IsSelected == true);
    }

    [Fact]
    public void ParentSelectionPropagatesDownAndChildSelectionRecomputesAncestors()
    {
        Fixture fixture = CreateFixture();
        SmartExportTreeNodeViewModel subassembly = FindNode(fixture.ViewModel, "SubA:1");
        SmartExportTreeNodeViewModel alpha = FindNode(fixture.ViewModel, "Alpha:1");

        alpha.IsSelected = false;

        Assert.Null(subassembly.IsSelected);
        Assert.Null(fixture.ViewModel.RootNode.IsSelected);

        subassembly.IsSelected = false;

        Assert.All(
            subassembly.DescendantsAndSelf().Where(node => node.CanSelect),
            node => Assert.True(node.IsSelected == false));
        Assert.Null(fixture.ViewModel.RootNode.IsSelected);

        fixture.ViewModel.RootNode.IsSelected = true;

        Assert.All(
            fixture.ViewModel.RootNode.DescendantsAndSelf().Where(node => node.CanSelect),
            node => Assert.True(node.IsSelected == true));
    }

    [Theory]
    [InlineData(ExportScopeMode.TopLevelOnly, 2, 0, 0)]
    [InlineData(ExportScopeMode.PartsRecursive, 4, 0, 0)]
    [InlineData(ExportScopeMode.AssembliesOnly, 0, 3, 0)]
    [InlineData(ExportScopeMode.AssembliesAndParts, 4, 3, 0)]
    public void ChangingScopeRebuildsFullySelectedEligibleTree(
        ExportScopeMode scope,
        int exportablePartOccurrences,
        int exportableAssemblyOccurrences,
        int exportableOtherOccurrences)
    {
        Fixture fixture = CreateFixture();
        fixture.ViewModel.SelectedScope = scope == ExportScopeMode.AssembliesAndParts
            ? ExportScopeMode.TopLevelOnly
            : ExportScopeMode.AssembliesAndParts;
        fixture.ViewModel.SelectNone();

        fixture.ViewModel.SelectedScope = scope;

        SmartExportTreeNodeViewModel[] exportable = fixture.ViewModel.RootNode
            .DescendantsAndSelf()
            .Where(node => node.IsExportable)
            .ToArray();
        Assert.Equal(exportablePartOccurrences, exportable.Count(node => node.DocumentKind == ComponentDocumentKind.Part));
        Assert.Equal(exportableAssemblyOccurrences, exportable.Count(node => node.DocumentKind == ComponentDocumentKind.Assembly));
        Assert.Equal(exportableOtherOccurrences, exportable.Count(node => node.DocumentKind == ComponentDocumentKind.Other));
        Assert.All(exportable, node => Assert.True(node.IsSelected == true));
        Assert.True(fixture.ViewModel.RootNode.IsSelected == true);
    }

    [Fact]
    public void SelectNoneAndSelectAllUpdateCompleteTreeAndCanExport()
    {
        Fixture fixture = CreateFixture();

        fixture.ViewModel.SelectNone();

        Assert.All(
            fixture.ViewModel.RootNode.DescendantsAndSelf().Where(node => node.CanSelect),
            node => Assert.True(node.IsSelected == false));
        Assert.False(fixture.ViewModel.CanExport);

        fixture.ViewModel.SelectAll();

        Assert.All(
            fixture.ViewModel.RootNode.DescendantsAndSelf().Where(node => node.CanSelect),
            node => Assert.True(node.IsSelected == true));
        Assert.True(fixture.ViewModel.CanExport);
    }

    [Fact]
    public void ExpandAllAndCollapseAllUpdateCompleteTree()
    {
        Fixture fixture = CreateFixture();

        fixture.ViewModel.ExpandAll();

        Assert.All(fixture.ViewModel.RootNode.DescendantsAndSelf(), node => Assert.True(node.IsExpanded));

        fixture.ViewModel.CollapseAll();

        Assert.All(fixture.ViewModel.RootNode.DescendantsAndSelf(), node => Assert.False(node.IsExpanded));
    }

    [Fact]
    public void ExportSelectedExportsDuplicateOccurrencePathOnlyOnce()
    {
        Fixture fixture = CreateFixture();
        fixture.ViewModel.SelectNone();
        SmartExportTreeNodeViewModel[] betaOccurrences = fixture.ViewModel.RootNode
            .DescendantsAndSelf()
            .Where(node => node.DisplayName.StartsWith("Beta:", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, betaOccurrences.Length);
        betaOccurrences[0].IsSelected = true;
        betaOccurrences[1].IsSelected = true;

        fixture.ViewModel.ExportSelected();

        Assert.Equal(
            [(@"C:\Models\Beta.ipt", @"C:\Exports\Beta.step", StepExportPrecision.Low)],
            fixture.Gateway.ExportCalls);
        Assert.Equal("Export complete: 1 succeeded, 0 failed.", fixture.ViewModel.StatusMessage);
    }

    [Fact]
    public void ConstructorExposesStablePrecisionOptionsAndDefaultsToLow()
    {
        Fixture fixture = CreateFixture();

        Assert.Same(fixture.ViewModel.StepPrecisionOptions, fixture.ViewModel.StepPrecisionOptions);
        Assert.Equal(
            [StepExportPrecision.Low, StepExportPrecision.Medium, StepExportPrecision.Highest],
            fixture.ViewModel.StepPrecisionOptions);
        Assert.Equal(StepExportPrecision.Low, fixture.ViewModel.SelectedStepPrecision);
        Assert.Contains("spline-fit", fixture.ViewModel.StepPrecisionDescription, StringComparison.Ordinal);
        Assert.Contains("file size", fixture.ViewModel.StepPrecisionDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectingPrecisionUpdatesDescriptionAndPassesPrecisionToEveryExport()
    {
        Fixture fixture = CreateFixture();
        fixture.ViewModel.SelectNone();
        FindNode(fixture.ViewModel, "Alpha:1").IsSelected = true;
        List<string?> changedProperties = [];
        fixture.ViewModel.PropertyChanged += (_, eventArgs) => changedProperties.Add(eventArgs.PropertyName);

        fixture.ViewModel.SelectedStepPrecision = StepExportPrecision.Highest;
        fixture.ViewModel.ExportSelected();

        Assert.Contains(nameof(SmartExportViewModel.SelectedStepPrecision), changedProperties);
        Assert.Contains(nameof(SmartExportViewModel.StepPrecisionDescription), changedProperties);
        Assert.Contains("finest", fixture.ViewModel.StepPrecisionDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [(@"C:\Models\Alpha.ipt", @"C:\Exports\Alpha.step", StepExportPrecision.Highest)],
            fixture.Gateway.ExportCalls);
    }

    [Fact]
    public void InvalidDestinationReportsValidationAndDoesNotExport()
    {
        Fixture fixture = CreateFixture(directoryExists: false);

        fixture.ViewModel.ExportSelected();

        Assert.Empty(fixture.Gateway.ExportCalls);
        Assert.Contains("does not exist", fixture.ViewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportSelectedSummarizesPerItemFailure()
    {
        Fixture fixture = CreateFixture();
        fixture.ViewModel.SelectNone();
        FindNode(fixture.ViewModel, "Alpha:1").IsSelected = true;
        fixture.Gateway.Failures[@"C:\Models\Alpha.ipt"] = new InvalidOperationException("Translator failed.");

        fixture.ViewModel.ExportSelected();

        Assert.Equal("Export complete: 0 succeeded, 1 failed. Alpha.ipt: Translator failed.", fixture.ViewModel.StatusMessage);
    }

    private static SmartExportTreeNodeViewModel FindNode(SmartExportViewModel viewModel, string displayName) =>
        viewModel.RootNode.DescendantsAndSelf().Single(node => node.DisplayName == displayName);

    private static Fixture CreateFixture(bool directoryExists = true)
    {
        FakeGateway gateway = new();
        FakeFileSystem fileSystem = new() { DirectoryExistsResult = directoryExists };
        SmartExportWorkflow workflow = new(gateway, fileSystem);
        Phase1StartResult session = workflow.Start();
        SmartExportViewModel viewModel = new(workflow, session) { DestinationDirectory = Destination };
        return new(viewModel, gateway);
    }

    private sealed record Fixture(SmartExportViewModel ViewModel, FakeGateway Gateway);

    private sealed class FakeGateway : IInventorPhase1Gateway
    {
        public List<(string SourcePath, string OutputPath, StepExportPrecision Precision)> ExportCalls { get; } = [];

        public Dictionary<string, Exception> Failures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ActiveAssemblyScan? ScanActiveAssembly() => new(
            @"C:\Models\Machine.iam",
            [
                new(
                    "SubA:1",
                    @"C:\Models\SubA.iam",
                    ComponentDocumentKind.Assembly,
                    false,
                    [
                        new("Alpha:1", @"C:\Models\Alpha.ipt", ComponentDocumentKind.Part, false, []),
                        new(
                            "Inner:1",
                            @"C:\Models\Inner.iam",
                            ComponentDocumentKind.Assembly,
                            false,
                            [new("Gamma:1", @"C:\Models\Gamma.ipt", ComponentDocumentKind.Part, false, [])]),
                    ]),
                new("Beta:1", @"C:\Models\Beta.ipt", ComponentDocumentKind.Part, false, []),
                new("Beta:2", @"c:\models\BETA.IPT", ComponentDocumentKind.Part, false, []),
                new("Reference:1", null, ComponentDocumentKind.Other, false, []),
            ]);

        public void ExportDocumentAsStep(
            string sourcePath,
            string outputPath,
            StepExportPrecision precision)
        {
            ExportCalls.Add((sourcePath, outputPath, precision));
            if (Failures.TryGetValue(sourcePath, out Exception? failure))
            {
                throw failure;
            }
        }
    }

    private sealed class FakeFileSystem : IPhase1FileSystem
    {
        public bool DirectoryExistsResult { get; init; } = true;

        public bool DirectoryExists(string path) => DirectoryExistsResult;

        public bool CanWriteToDirectory(string path) => true;

        public bool FileExists(string path) => false;
    }
}
