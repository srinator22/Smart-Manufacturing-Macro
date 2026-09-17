using SmartManufacturingExporter.Application.Phase1;
using SmartManufacturingExporter.Core.Phase1;

namespace SmartManufacturingExporter.UnitTests.Phase1;

public sealed class SmartExportWorkflowTests
{
    private const string AssemblyPath = @"C:\Models\Machine.iam";
    private const string Destination = @"C:\Exports";

    [Fact]
    public void StartWithoutActiveAssemblyReturnsExactErrorAndDoesNotExport()
    {
        FakeGateway gateway = new() { Scan = null };

        Phase1StartResult result = CreateWorkflow(gateway).Start();

        Assert.False(result.IsSuccess);
        Assert.Equal(SmartExportWorkflow.RequiredAssemblyMessage, result.ErrorMessage);
        Assert.Null(result.RootAssemblyPath);
        Assert.Null(result.HierarchyRoot);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.Notices);
        Assert.Equal(1, gateway.ScanCalls);
        Assert.Empty(gateway.ExportCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StartWithUnsavedAssemblyReturnsExactError(string? rootAssemblyPath)
    {
        FakeGateway gateway = new()
        {
            Scan = new ActiveAssemblyScan(rootAssemblyPath!, [Part("Part:1", @"C:\Models\Part.ipt")]),
        };

        Phase1StartResult result = CreateWorkflow(gateway).Start();

        Assert.False(result.IsSuccess);
        Assert.Equal(SmartExportWorkflow.UnsavedAssemblyMessage, result.ErrorMessage);
        Assert.Null(result.RootAssemblyPath);
        Assert.Null(result.HierarchyRoot);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.Notices);
        Assert.Equal(1, gateway.ScanCalls);
        Assert.Empty(gateway.ExportCalls);
    }

    [Fact]
    public void StartBuildsThreeLevelHierarchyAndGlobalCaseInsensitiveQuantities()
    {
        FakeGateway gateway = WithOccurrences(
            Part("Bracket:1", @"C:\Models\Bracket.ipt"),
            Assembly(
                "Frame:1",
                @"C:\Models\Frame.iam",
                Part("Bracket:2", @"c:\models\BRACKET.ipt"),
                Assembly(
                    "Nested:1",
                    @"C:\Models\Nested.iam",
                    Part("Pin:1", @"C:\Models\Pin.ipt"))));

        Phase1StartResult result = CreateWorkflow(gateway).Start();

        ExportHierarchyNode root = Assert.IsType<ExportHierarchyNode>(result.HierarchyRoot);
        Assert.Equal("root/1/1/0", root.Children[1].Children[1].Children[0].NodeId);
        Assert.Equal(2, root.Children[0].Quantity);
        Assert.Equal(2, root.Children[1].Children[0].Quantity);
        ExportCandidate bracket = Assert.Single(
            result.Candidates,
            candidate => candidate.SourcePath.Equals(@"C:\Models\Bracket.ipt", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, bracket.Quantity);
        Assert.Equal(ComponentDocumentKind.Part, bracket.DocumentKind);
        Assert.Contains(result.Candidates, candidate => candidate.SourcePath == AssemblyPath && candidate.Quantity == 1);
    }

    [Fact]
    public void StartOmitsSuppressedSubtreeAndReportsIneligibleDocuments()
    {
        FakeGateway gateway = WithOccurrences(
            new("Suppressed", @"C:\Models\Hidden.iam", ComponentDocumentKind.Assembly, true,
                [Part("Hidden child", @"C:\Models\Hidden.ipt")]),
            new("Unresolved", " ", ComponentDocumentKind.Part, false, []),
            new("Reference", @"C:\Models\Reference.dwg", ComponentDocumentKind.Other, false, []));

        Phase1StartResult result = CreateWorkflow(gateway).Start();

        ExportHierarchyNode root = Assert.IsType<ExportHierarchyNode>(result.HierarchyRoot);
        Assert.Equal(["Unresolved", "Reference"], root.Children.Select(node => node.DisplayName));
        Assert.DoesNotContain(result.Candidates, candidate => candidate.SourcePath.Contains("Hidden", StringComparison.Ordinal));
        Assert.Equal(
            [
                new ScanNotice("Suppressed", "Suppressed"),
                new ScanNotice("Unresolved", "Document has no resolved source path"),
                new ScanNotice("Reference", "Unsupported document type"),
            ],
            result.Notices);
    }

    [Theory]
    [InlineData(ExportScopeMode.TopLevelOnly, "Top.ipt")]
    [InlineData(ExportScopeMode.PartsRecursive, "Deep.ipt,Top.ipt")]
    [InlineData(ExportScopeMode.AssembliesOnly, "Machine.iam,Sub.iam")]
    [InlineData(ExportScopeMode.AssembliesAndParts, "Deep.ipt,Machine.iam,Sub.iam,Top.ipt")]
    public void GetCandidatesForScopeReturnsExpectedUniqueDocuments(ExportScopeMode scope, string expectedNames)
    {
        SmartExportWorkflow workflow = CreateWorkflow(WithOccurrences(
            Part("Top", @"C:\Models\Top.ipt"),
            Assembly("Sub", @"C:\Models\Sub.iam", Part("Deep", @"C:\Models\Deep.ipt"))));
        Phase1StartResult session = workflow.Start();

        IReadOnlyList<ExportCandidate> candidates = SmartExportWorkflow.GetCandidatesForScope(session, scope);

        Assert.Equal(
            expectedNames.Split(','),
            candidates.Select(candidate => candidate.DisplayName));
    }

    [Fact]
    public void GetCandidatesForScopeRejectsInvalidEnum()
    {
        SmartExportWorkflow workflow = CreateWorkflow(WithOccurrences());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => SmartExportWorkflow.GetCandidatesForScope(workflow.Start(), (ExportScopeMode)99));
    }

    [Fact]
    public void BuildStepPlanDeduplicatesSelectionsAndIncludesAssembly()
    {
        FakeGateway gateway = WithOccurrences(
            Assembly("Sub", @"C:\Models\Sub.iam", Part("Deep", @"C:\Models\Deep.ipt")));
        SmartExportWorkflow workflow = CreateWorkflow(gateway);

        StepExportPlan plan = workflow.BuildStepPlan(
            workflow.Start(),
            ExportScopeMode.AssembliesAndParts,
            [@"C:\Models\Sub.iam", @"c:\models\SUB.iam"],
            Destination,
            StepExportPrecision.Medium);

        Assert.True(plan.CanExecute);
        StepExportPlanItem item = Assert.Single(plan.Items);
        Assert.Equal(@"C:\Models\Sub.iam", item.SourcePath);
        Assert.Equal(@"C:\Exports\Sub.step", item.OutputPath);
        Assert.Empty(gateway.ExportCalls);
    }

    [Fact]
    public void BuildStepPlanRejectsSelectionOutsideScope()
    {
        Fixture fixture = ValidFixture();

        StepExportPlan plan = fixture.Workflow.BuildStepPlan(
            fixture.Session,
            ExportScopeMode.AssembliesOnly,
            [fixture.SourcePath],
            Destination,
            StepExportPrecision.Low);

        AssertError(plan, "UnknownSelection");
        Assert.Contains(fixture.SourcePath, plan.Issues[0].Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Gateway.ExportCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildStepPlanRejectsBlankDestination(string? destination)
    {
        Fixture fixture = ValidFixture();

        StepExportPlan plan = fixture.Workflow.BuildStepPlan(
            fixture.Session,
            ExportScopeMode.TopLevelOnly,
            [fixture.SourcePath],
            destination!,
            StepExportPrecision.Low);

        AssertError(plan, "DestinationRequired");
    }

    [Fact]
    public void BuildStepPlanRejectsDestinationAndOutputSafetyFailures()
    {
        Fixture missing = ValidFixture(directoryExists: false);
        AssertError(missing.Workflow.BuildStepPlan(
            missing.Session, ExportScopeMode.TopLevelOnly, [missing.SourcePath], Destination, StepExportPrecision.Low),
            "DestinationNotFound");

        Fixture unwritable = ValidFixture(canWrite: false);
        AssertError(unwritable.Workflow.BuildStepPlan(
            unwritable.Session, ExportScopeMode.TopLevelOnly, [unwritable.SourcePath], Destination, StepExportPrecision.Low),
            "DestinationNotWritable");

        Fixture existing = ValidFixture(existingFiles: [@"C:\Exports\Part.step"]);
        StepExportPlan existingPlan = existing.Workflow.BuildStepPlan(
            existing.Session, ExportScopeMode.TopLevelOnly, [existing.SourcePath], Destination, StepExportPrecision.Low);
        AssertError(existingPlan, "OutputExists");
        Assert.Contains(@"C:\Exports\Part.step", existingPlan.Issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStepPlanRejectsCollisionEmptySelectionAndInvalidEnums()
    {
        FakeGateway gateway = WithOccurrences(
            Part("A", @"C:\A\Bracket.ipt"),
            Part("B", @"C:\B\BRACKET.ipt"));
        SmartExportWorkflow workflow = CreateWorkflow(gateway);
        Phase1StartResult session = workflow.Start();

        AssertError(workflow.BuildStepPlan(
            session, ExportScopeMode.TopLevelOnly,
            [@"C:\A\Bracket.ipt", @"C:\B\BRACKET.ipt"], Destination, StepExportPrecision.Low),
            "OutputCollision");
        AssertError(workflow.BuildStepPlan(
            session, ExportScopeMode.TopLevelOnly, [], Destination, StepExportPrecision.Low),
            "NoSelection");
        AssertError(workflow.BuildStepPlan(
            session, (ExportScopeMode)99, [@"C:\A\Bracket.ipt"], Destination, StepExportPrecision.Low),
            "UnsupportedExportScope");
        AssertError(workflow.BuildStepPlan(
            session, ExportScopeMode.TopLevelOnly, [@"C:\A\Bracket.ipt"], Destination, (StepExportPrecision)99),
            "UnsupportedStepPrecision");
        Assert.Empty(gateway.ExportCalls);
    }

    [Fact]
    public void ExecuteStepPlanRefusesInvalidPlan()
    {
        Fixture fixture = ValidFixture();
        StepExportPlan plan = new(
            [],
            [new ValidationIssue("Unsafe", "Unsafe plan.", ValidationSeverity.Error)],
            StepExportPrecision.Low);

        Assert.Throws<InvalidOperationException>(() => fixture.Workflow.ExecuteStepPlan(plan));
        Assert.Empty(fixture.Gateway.ExportCalls);
    }

    [Fact]
    public void ExecuteStepPlanExportsOnlySelectedDocumentsWithRequestedPrecision()
    {
        FakeGateway gateway = WithOccurrences(
            Part("A", @"C:\Models\Alpha.ipt"),
            Part("B", @"C:\Models\Beta.ipt"),
            Part("C", @"C:\Models\Gamma.ipt"));
        SmartExportWorkflow workflow = CreateWorkflow(gateway);
        Phase1StartResult session = workflow.Start();
        StepExportPlan plan = workflow.BuildStepPlan(
            session,
            ExportScopeMode.TopLevelOnly,
            [@"C:\Models\Alpha.ipt", @"C:\Models\Gamma.ipt"],
            Destination,
            StepExportPrecision.Highest);

        StepExportBatchResult result = workflow.ExecuteStepPlan(plan);

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(
            [
                (@"C:\Models\Alpha.ipt", @"C:\Exports\Alpha.step", StepExportPrecision.Highest),
                (@"C:\Models\Gamma.ipt", @"C:\Exports\Gamma.step", StepExportPrecision.Highest),
            ],
            gateway.ExportCalls);
        Assert.DoesNotContain(
            gateway.ExportCalls,
            call => string.Equals(call.SourcePath, @"C:\Models\Beta.ipt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExecuteStepPlanIsolatesFailuresAndUsesDocumentGateway()
    {
        FakeGateway gateway = WithOccurrences();
        gateway.ExportFailureBySource[@"C:\Models\Bad.iam"] = new InvalidOperationException("Translator failed.");
        SmartExportWorkflow workflow = CreateWorkflow(gateway);
        StepExportPlan plan = new(
            [
                new(@"C:\Models\Bad.iam", @"C:\Exports\Bad.step"),
                new(@"C:\Models\Good.ipt", @"C:\Exports\Good.step"),
            ],
            [],
            StepExportPrecision.Highest);

        StepExportBatchResult result = workflow.ExecuteStepPlan(plan);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.False(result.Items[0].Succeeded);
        Assert.Equal("Translator failed.", result.Items[0].ErrorMessage);
        Assert.True(result.Items[1].Succeeded);
        Assert.Null(result.Items[1].ErrorMessage);
        Assert.Equal(
            [
                (@"C:\Models\Bad.iam", @"C:\Exports\Bad.step", StepExportPrecision.Highest),
                (@"C:\Models\Good.ipt", @"C:\Exports\Good.step", StepExportPrecision.Highest),
            ],
            gateway.ExportCalls);
    }

    [Fact]
    public void ExecuteStepPlanSkipsOutputCreatedAfterPlanningAndContinues()
    {
        FakeGateway gateway = WithOccurrences();
        FakeFileSystem fileSystem = new();
        SmartExportWorkflow workflow = CreateWorkflow(gateway, fileSystem);
        StepExportPlan plan = new(
            [
                new(@"C:\Models\Alpha.ipt", @"C:\Exports\Alpha.step"),
                new(@"C:\Models\Beta.ipt", @"C:\Exports\Beta.step"),
            ],
            [],
            StepExportPrecision.Medium);
        fileSystem.ExistingFiles.Add(@"C:\Exports\Alpha.step");

        StepExportBatchResult result = workflow.ExecuteStepPlan(plan);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.False(result.Items[0].Succeeded);
        Assert.Contains("already exists", result.Items[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Items[1].Succeeded);
        Assert.Null(result.Items[1].ErrorMessage);
        Assert.Single(gateway.ExportCalls);
        Assert.Equal(
            [(@"C:\Models\Beta.ipt", @"C:\Exports\Beta.step", StepExportPrecision.Medium)],
            gateway.ExportCalls);
    }

    private static ComponentOccurrenceSnapshot Part(string name, string sourcePath) =>
        new(name, sourcePath, ComponentDocumentKind.Part, false, []);

    private static ComponentOccurrenceSnapshot Assembly(
        string name,
        string sourcePath,
        params ComponentOccurrenceSnapshot[] children) =>
        new(name, sourcePath, ComponentDocumentKind.Assembly, false, children);

    private static FakeGateway WithOccurrences(params ComponentOccurrenceSnapshot[] occurrences) =>
        new() { Scan = new ActiveAssemblyScan(AssemblyPath, occurrences) };

    private static SmartExportWorkflow CreateWorkflow(FakeGateway gateway, FakeFileSystem? fileSystem = null) =>
        new(gateway, fileSystem ?? new FakeFileSystem());

    private static Fixture ValidFixture(
        bool directoryExists = true,
        bool canWrite = true,
        IEnumerable<string>? existingFiles = null)
    {
        const string sourcePath = @"C:\Models\Part.ipt";
        FakeGateway gateway = WithOccurrences(Part("Part:1", sourcePath));
        FakeFileSystem fileSystem = new()
        {
            DirectoryExistsResult = directoryExists,
            CanWriteResult = canWrite,
        };
        fileSystem.ExistingFiles.UnionWith(existingFiles ?? []);
        SmartExportWorkflow workflow = CreateWorkflow(gateway, fileSystem);
        return new(workflow, workflow.Start(), gateway, sourcePath);
    }

    private static void AssertError(StepExportPlan plan, string code)
    {
        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Issues, issue => issue.Code == code && issue.Severity == ValidationSeverity.Error);
    }

    private sealed record Fixture(
        SmartExportWorkflow Workflow,
        Phase1StartResult Session,
        FakeGateway Gateway,
        string SourcePath);

    private sealed class FakeGateway : IInventorPhase1Gateway
    {
        public ActiveAssemblyScan? Scan { get; init; }

        public int ScanCalls { get; private set; }

        public List<(string SourcePath, string OutputPath, StepExportPrecision Precision)> ExportCalls { get; } = [];

        public Dictionary<string, Exception> ExportFailureBySource { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ActiveAssemblyScan? ScanActiveAssembly()
        {
            ScanCalls++;
            return Scan;
        }

        public void ExportDocumentAsStep(string sourcePath, string outputPath, StepExportPrecision precision)
        {
            ExportCalls.Add((sourcePath, outputPath, precision));
            if (ExportFailureBySource.TryGetValue(sourcePath, out Exception? exception))
            {
                throw exception;
            }
        }
    }

    private sealed class FakeFileSystem : IPhase1FileSystem
    {
        public bool DirectoryExistsResult { get; init; } = true;

        public bool CanWriteResult { get; init; } = true;

        public HashSet<string> ExistingFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => DirectoryExistsResult;

        public bool CanWriteToDirectory(string path) => CanWriteResult;

        public bool FileExists(string path) => ExistingFiles.Contains(path);
    }
}
