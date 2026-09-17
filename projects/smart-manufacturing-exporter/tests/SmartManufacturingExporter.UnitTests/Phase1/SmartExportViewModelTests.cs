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
        Assert.Equal(Enum.GetValues<ExportScopeMode>(), fixture.ViewModel.ScopeOptions.Select(option => option.Mode));
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
    public void ParentSelectionRaisesCanExportOncePerCompletedTreeChange()
    {
        Fixture fixture = CreateFixture();
        int canExportNotifications = 0;
        int parentSelectionNotifications = 0;
        SmartExportTreeNodeViewModel subassembly = FindNode(fixture.ViewModel, "SubA:1");
        fixture.ViewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(SmartExportViewModel.CanExport))
            {
                canExportNotifications++;
            }
        };
        subassembly.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(SmartExportTreeNodeViewModel.IsSelected))
            {
                parentSelectionNotifications++;
            }
        };

        subassembly.IsSelected = false;

        Assert.Equal(1, canExportNotifications);
        Assert.Equal(1, parentSelectionNotifications);
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
    public void MixedScopeExportsSelectedAssemblyWhenItsTreeStateIsIndeterminate()
    {
        Fixture fixture = CreateFixture();
        fixture.ViewModel.SelectedScope = ExportScopeMode.AssembliesAndParts;
        fixture.ViewModel.SelectNone();
        SmartExportTreeNodeViewModel subassembly = FindNode(fixture.ViewModel, "SubA:1");
        SmartExportTreeNodeViewModel innerAssembly = FindNode(fixture.ViewModel, "Inner:1");
        subassembly.IsSelected = true;
        innerAssembly.IsSelected = false;

        Assert.Null(subassembly.IsSelected);

        fixture.ViewModel.ExportSelected();

        Assert.Equal(
            [
                (@"C:\Models\Alpha.ipt", @"C:\Exports\Alpha.step", StepExportPrecision.Low),
                (@"C:\Models\SubA.iam", @"C:\Exports\SubA.step", StepExportPrecision.Low),
            ],
            fixture.Gateway.ExportCalls);
    }

    [Fact]
    public void DeselectingOneOccurrenceDeselectsEveryOccurrenceOfTheSameDocument()
    {
        Fixture fixture = CreateFixture();
        SmartExportTreeNodeViewModel betaOne = FindNode(fixture.ViewModel, "Beta:1");
        SmartExportTreeNodeViewModel betaTwo = FindNode(fixture.ViewModel, "Beta:2");

        betaOne.IsSelected = false;

        Assert.False(betaTwo.IsSelected == true);
        Assert.False(betaTwo.IsDocumentSelected);

        fixture.ViewModel.ExportSelected();

        Assert.DoesNotContain(
            fixture.Gateway.ExportCalls,
            call => string.Equals(call.SourcePath, @"C:\Models\Beta.ipt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectingOneOccurrenceSelectsEveryOccurrenceOfTheSameDocument()
    {
        Fixture fixture = CreateFixture();
        fixture.ViewModel.SelectNone();
        SmartExportTreeNodeViewModel betaOne = FindNode(fixture.ViewModel, "Beta:1");
        SmartExportTreeNodeViewModel betaTwo = FindNode(fixture.ViewModel, "Beta:2");

        betaTwo.IsSelected = true;

        Assert.True(betaOne.IsSelected == true);
        Assert.True(betaOne.IsDocumentSelected);

        fixture.ViewModel.ExportSelected();

        Assert.Equal(
            [(@"C:\Models\Beta.ipt", @"C:\Exports\Beta.step", StepExportPrecision.Low)],
            fixture.Gateway.ExportCalls);
    }

    [Fact]
    public void DeselectingAnOccurrenceRecomputesAncestorsInEveryBranchThatSharesTheDocument()
    {
        CrossBranchGateway gateway = new();
        FakeFileSystem fileSystem = new() { DirectoryExistsResult = true };
        SmartExportWorkflow workflow = new(gateway, fileSystem);
        Phase1StartResult session = workflow.Start();
        SmartExportViewModel viewModel = new(workflow, session) { DestinationDirectory = Destination };

        SmartExportTreeNodeViewModel left = FindNode(viewModel, "Left:1");
        SmartExportTreeNodeViewModel right = FindNode(viewModel, "Right:1");
        SmartExportTreeNodeViewModel sharedUnderLeft = viewModel.RootNode
            .DescendantsAndSelf()
            .Single(node => node.DisplayName == "Shared:1");
        SmartExportTreeNodeViewModel sharedUnderRight = viewModel.RootNode
            .DescendantsAndSelf()
            .Single(node => node.DisplayName == "Shared:2");

        sharedUnderLeft.IsSelected = false;

        Assert.False(sharedUnderRight.IsSelected == true);
        Assert.False(sharedUnderRight.IsDocumentSelected);
        Assert.True(left.IsSelected == false);
        Assert.Null(right.IsSelected);
    }

    [Fact]
    public void TogglingOneOccurrenceOfAWidelyRepeatedDocumentStaysLinearInTreeSize()
    {
        const int occurrenceCount = 200;
        const string boltPath = @"C:\Models\Bolt.ipt";

        List<ExportHierarchyNode> children = [];
        for (int i = 0; i < occurrenceCount; i++)
        {
            children.Add(new ExportHierarchyNode($"Bolt:{i}", $"Bolt:{i}", boltPath, ComponentDocumentKind.Part, 1, []));
        }

        // Two distinct parts alongside the repeated bolt so the root can become indeterminate once one
        // bolt occurrence is toggled off (all-true before, mixed after).
        children.Add(new ExportHierarchyNode("Washer:1", "Washer:1", @"C:\Models\Washer.ipt", ComponentDocumentKind.Part, 1, []));
        children.Add(new ExportHierarchyNode("Nut:1", "Nut:1", @"C:\Models\Nut.ipt", ComponentDocumentKind.Part, 1, []));

        ExportHierarchyNode root = new("Root:1", "Root:1", null, ComponentDocumentKind.Assembly, 1, children);
        HashSet<string> exportablePaths = new(StringComparer.OrdinalIgnoreCase)
        {
            boltPath,
            @"C:\Models\Washer.ipt",
            @"C:\Models\Nut.ipt",
        };

        SmartExportTreeNodeViewModel rootNode = new(root, exportablePaths);
        long before = rootNode.AggregationCount;

        rootNode.Children[0].IsSelected = false;

        long delta = rootNode.AggregationCount - before;

        // AggregationCount is incremented by (1 + Children.Count) per CalculateSelection() call, so it
        // measures element VISITS, not call entries - a per-entry-only counter cannot see this
        // regression, since the buggy call count is already linear (~2 * occurrenceCount): the defect
        // is O(occurrenceCount) calls to the root's CalculateSelection(), each walking all
        // occurrenceCount+2 children. Measured on the unmodified (regressed) algorithm: 40800 at
        // occurrenceCount=200 and 643200 at occurrenceCount=800 (ratio ~15.8, matching the ~16x
        // expected for O(n^2)). The phased fix collapses this to one root refresh plus one
        // RefreshSelfOnly per touched peer, a few hundred here. 6x occurrenceCount (1200) leaves
        // generous headroom for legitimate per-node work while still failing hard on the regression.
        // Re-baseline this bound deliberately (with evidence of the new legitimate cost), never widen
        // it just to make a regression pass.
        Assert.True(
            delta <= 6 * occurrenceCount,
            $"Expected at most {6 * occurrenceCount} aggregation element visits for {occurrenceCount} occurrences, observed {delta}.");
    }

    [Fact]
    public void TogglingAnOccurrenceNotifiesSharedAncestorsOnceWithTheFinalValue()
    {
        const string sharedPath = @"C:\Models\Shared.ipt";
        ExportHierarchyNode leftChild = new("Shared:1", "Shared:1", sharedPath, ComponentDocumentKind.Part, 1, []);
        ExportHierarchyNode left = new("Left:1", "Left:1", @"C:\Models\Left.iam", ComponentDocumentKind.Assembly, 1, [leftChild]);
        ExportHierarchyNode rightChild = new("Shared:2", "Shared:2", sharedPath, ComponentDocumentKind.Part, 1, []);
        ExportHierarchyNode right = new("Right:1", "Right:1", @"C:\Models\Right.iam", ComponentDocumentKind.Assembly, 1, [rightChild]);
        ExportHierarchyNode root = new("Root:1", "Root:1", null, ComponentDocumentKind.Assembly, 1, [left, right]);

        // Only the shared part is exportable here (as in the real AssembliesAndParts scope, container
        // assemblies with no distinct own selection state contribute nothing but their children's
        // aggregate) so each assembly's own aggregate is driven entirely by the shared occurrence
        // beneath it - reproducing the stale-peer interleaving the phased fix targets.
        HashSet<string> exportablePaths = new(StringComparer.OrdinalIgnoreCase) { sharedPath };

        SmartExportTreeNodeViewModel rootNode = new(root, exportablePaths);
        SmartExportTreeNodeViewModel sharedUnderLeft = rootNode
            .DescendantsAndSelf()
            .Single(node => node.DisplayName == "Shared:1");

        int isSelectedNotifications = 0;
        List<bool?> observedValuesAtNotificationTime = [];
        rootNode.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(SmartExportTreeNodeViewModel.IsSelected))
            {
                isSelectedNotifications++;
                observedValuesAtNotificationTime.Add(rootNode.IsSelected);
            }
        };

        sharedUnderLeft.IsSelected = false;

        Assert.Equal(1, isSelectedNotifications);
        Assert.All(observedValuesAtNotificationTime, value => Assert.Equal(rootNode.IsSelected, value));
    }

    [Fact]
    public void TogglingARepeatedSubassemblyNotifiesEveryNodeAtMostOnceWithTheFinalValue()
    {
        const string subassemblyPath = @"C:\Models\Sub.iam";
        const string partPath = @"C:\Models\X.ipt";

        ExportHierarchyNode x1 = new("X1:1", "X1:1", partPath, ComponentDocumentKind.Part, 1, []);
        ExportHierarchyNode subA = new("SubA:1", "SubA:1", subassemblyPath, ComponentDocumentKind.Assembly, 1, [x1]);
        ExportHierarchyNode left = new("Left:1", "Left:1", @"C:\Models\Left.iam", ComponentDocumentKind.Assembly, 1, [subA]);
        ExportHierarchyNode x2 = new("X2:1", "X2:1", partPath, ComponentDocumentKind.Part, 1, []);
        ExportHierarchyNode subB = new("SubB:1", "SubB:1", subassemblyPath, ComponentDocumentKind.Assembly, 1, [x2]);
        ExportHierarchyNode right = new("Right:1", "Right:1", @"C:\Models\Right.iam", ComponentDocumentKind.Assembly, 1, [subB]);
        ExportHierarchyNode root = new("Root:1", "Root:1", null, ComponentDocumentKind.Assembly, 1, [left, right]);

        // Scope AssembliesAndParts: both the repeated sub-assembly document and the repeated part
        // document underneath it are exportable - an ordinary repeated-subassembly CAD shape, not just
        // a repeated leaf part.
        HashSet<string> exportablePaths = new(StringComparer.OrdinalIgnoreCase) { subassemblyPath, partPath };

        SmartExportTreeNodeViewModel rootNode = new(root, exportablePaths);
        SmartExportTreeNodeViewModel subANode = rootNode
            .DescendantsAndSelf()
            .Single(node => node.DisplayName == "SubA:1");

        Dictionary<SmartExportTreeNodeViewModel, int> notificationCounts = [];
        Dictionary<SmartExportTreeNodeViewModel, List<bool?>> observedValues = [];
        Dictionary<SmartExportTreeNodeViewModel, bool?> valuesBeforeToggle = [];
        foreach (SmartExportTreeNodeViewModel node in rootNode.DescendantsAndSelf())
        {
            notificationCounts[node] = 0;
            observedValues[node] = [];
            valuesBeforeToggle[node] = node.IsSelected;
            node.PropertyChanged += (_, eventArgs) =>
            {
                if (eventArgs.PropertyName == nameof(SmartExportTreeNodeViewModel.IsSelected))
                {
                    notificationCounts[node]++;
                    observedValues[node].Add(node.IsSelected);
                }
            };
        }

        subANode.IsSelected = false;

        foreach (SmartExportTreeNodeViewModel node in rootNode.DescendantsAndSelf())
        {
            // Exactly once for a node whose value changed, never for one that did not. Asserting
            // only "at most one" would pass a node that silently failed to notify at all, and would
            // leave the published-value assertion below vacuous for it.
            int expectedNotifications = valuesBeforeToggle[node] == node.IsSelected ? 0 : 1;
            Assert.True(
                notificationCounts[node] == expectedNotifications,
                $"{node.DisplayName} raised {notificationCounts[node]} IsSelected notifications, expected {expectedNotifications} (was {valuesBeforeToggle[node]}, now {node.IsSelected}).");
            Assert.All(
                observedValues[node],
                value => Assert.True(
                    value == node.IsSelected,
                    $"{node.DisplayName} published {value} but its final IsSelected is {node.IsSelected}."));
        }
    }

    private sealed class CrossBranchGateway : IInventorPhase1Gateway
    {
        public ActiveAssemblyScan? ScanActiveAssembly() => new(
            @"C:\Models\Machine.iam",
            [
                new(
                    "Left:1",
                    @"C:\Models\Left.iam",
                    ComponentDocumentKind.Assembly,
                    false,
                    [new("Shared:1", @"C:\Models\Shared.ipt", ComponentDocumentKind.Part, false, [])]),
                new(
                    "Right:1",
                    @"C:\Models\Right.iam",
                    ComponentDocumentKind.Assembly,
                    false,
                    [
                        new("Shared:2", @"C:\Models\Shared.ipt", ComponentDocumentKind.Part, false, []),
                        new("Other:1", @"C:\Models\Other.ipt", ComponentDocumentKind.Part, false, []),
                    ]),
            ]);

        public void ExportDocumentAsStep(string sourcePath, string outputPath, StepExportPrecision precision)
        {
        }
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
    public void ScopeOptionsExposeEveryScopeModeWithADistinctDisplayName()
    {
        Fixture fixture = CreateFixture();

        Assert.Equal(
            Enum.GetValues<ExportScopeMode>(),
            fixture.ViewModel.ScopeOptions.Select(option => option.Mode));
        Assert.All(
            fixture.ViewModel.ScopeOptions,
            option => Assert.False(string.IsNullOrWhiteSpace(option.DisplayName)));
        Assert.Equal(
            fixture.ViewModel.ScopeOptions.Select(option => option.DisplayName).Distinct(StringComparer.Ordinal).Count(),
            fixture.ViewModel.ScopeOptions.Count);
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
