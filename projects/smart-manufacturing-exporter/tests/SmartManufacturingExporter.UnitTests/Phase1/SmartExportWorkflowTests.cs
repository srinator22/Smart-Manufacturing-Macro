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
        SmartExportWorkflow workflow = CreateWorkflow(gateway);

        Phase1StartResult result = workflow.Start();

        Assert.False(result.IsSuccess);
        Assert.Equal(SmartExportWorkflow.RequiredAssemblyMessage, result.ErrorMessage);
        Assert.Null(result.RootAssemblyPath);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.Notices);
        Assert.Equal(1, gateway.ScanCalls);
        Assert.Empty(gateway.ExportCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StartWithUnsavedAssemblyReturnsExactErrorAndDoesNotExport(string? rootAssemblyPath)
    {
        FakeGateway gateway = new()
        {
            Scan = new ActiveAssemblyScan(
                rootAssemblyPath!,
                [Part("Part:1", @"C:\Models\Part.ipt")]),
        };
        SmartExportWorkflow workflow = CreateWorkflow(gateway);

        Phase1StartResult result = workflow.Start();

        Assert.False(result.IsSuccess);
        Assert.Equal(SmartExportWorkflow.UnsavedAssemblyMessage, result.ErrorMessage);
        Assert.Null(result.RootAssemblyPath);
        Assert.Empty(result.Candidates);
        Assert.Empty(result.Notices);
        Assert.Equal(1, gateway.ScanCalls);
        Assert.Empty(gateway.ExportCalls);
    }

    [Fact]
    public void StartDeduplicatesPathsCaseInsensitivelyAndCountsQuantity()
    {
        FakeGateway gateway = WithOccurrences(
            Part("First", @"C:\Models\Bracket.ipt"),
            Part("Second", @"c:\models\BRACKET.ipt"));

        Phase1StartResult result = CreateWorkflow(gateway).Start();

        ExportCandidate candidate = Assert.Single(result.Candidates);
        Assert.Equal(@"C:\Models\Bracket.ipt", candidate.SourcePath);
        Assert.Equal("Bracket.ipt", candidate.DisplayName);
        Assert.Equal(2, candidate.Quantity);
        Assert.Equal(1, gateway.ScanCalls);
    }

    [Fact]
    public void StartReportsEveryIneligibleOccurrence()
    {
        FakeGateway gateway = WithOccurrences(
            new("Suppressed Part", @"C:\Models\Suppressed.ipt", ComponentDocumentKind.Part, true),
            new("Subassembly", @"C:\Models\Subassembly.iam", ComponentDocumentKind.Assembly, false),
            new("Unresolved Part", " ", ComponentDocumentKind.Part, false));

        Phase1StartResult result = CreateWorkflow(gateway).Start();

        Assert.Empty(result.Candidates);
        Assert.Equal(
            [
                new ScanNotice("Suppressed Part", "Suppressed"),
                new ScanNotice("Subassembly", "Not a top-level part"),
                new ScanNotice("Unresolved Part", "Part has no resolved source path"),
            ],
            result.Notices);
    }

    [Fact]
    public void StartOrdersCandidatesByDisplayNameThenSourcePathIgnoringCase()
    {
        FakeGateway gateway = WithOccurrences(
            Part("Z", @"C:\B\zeta.ipt"),
            Part("B", @"C:\B\alpha.ipt"),
            Part("A", @"C:\A\ALPHA.ipt"));

        Phase1StartResult result = CreateWorkflow(gateway).Start();

        Assert.Equal(
            [@"C:\A\ALPHA.ipt", @"C:\B\alpha.ipt", @"C:\B\zeta.ipt"],
            result.Candidates.Select(candidate => candidate.SourcePath));
    }

    [Fact]
    public void BuildStepPlanIncludesOnlySelectedCandidatesAndDoesNotExport()
    {
        FakeGateway gateway = WithOccurrences(
            Part("A", @"C:\Models\Alpha.ipt"),
            Part("B", @"C:\Models\Beta.ipt"));
        SmartExportWorkflow workflow = CreateWorkflow(gateway);
        Phase1StartResult session = workflow.Start();

        StepExportPlan plan = workflow.BuildStepPlan(session, [@"c:\models\BETA.ipt"], Destination);

        Assert.True(plan.CanExecute);
        StepExportPlanItem item = Assert.Single(plan.Items);
        Assert.Equal(@"C:\Models\Beta.ipt", item.SourcePath);
        Assert.Equal(@"C:\Exports\Beta.step", item.OutputPath);
        Assert.Empty(gateway.ExportCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildStepPlanRejectsBlankDestination(string? destination)
    {
        Fixture fixture = ValidFixture();

        StepExportPlan plan = fixture.Workflow.BuildStepPlan(fixture.Session, [fixture.SourcePath], destination!);

        AssertError(plan, "DestinationRequired");
    }

    [Fact]
    public void BuildStepPlanRejectsMissingDestination()
    {
        Fixture fixture = ValidFixture(directoryExists: false);

        StepExportPlan plan = fixture.Workflow.BuildStepPlan(fixture.Session, [fixture.SourcePath], Destination);

        AssertError(plan, "DestinationNotFound");
    }

    [Fact]
    public void BuildStepPlanRejectsUnwritableDestination()
    {
        Fixture fixture = ValidFixture(canWrite: false);

        StepExportPlan plan = fixture.Workflow.BuildStepPlan(fixture.Session, [fixture.SourcePath], Destination);

        AssertError(plan, "DestinationNotWritable");
    }

    [Fact]
    public void BuildStepPlanRejectsExistingOutput()
    {
        Fixture fixture = ValidFixture(existingFiles: [@"C:\Exports\Part.step"]);

        StepExportPlan plan = fixture.Workflow.BuildStepPlan(fixture.Session, [fixture.SourcePath], Destination);

        AssertError(plan, "OutputExists");
        Assert.Contains(@"C:\Exports\Part.step", plan.Issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStepPlanRejectsCaseInsensitiveOutputCollision()
    {
        FakeGateway gateway = WithOccurrences(
            Part("A", @"C:\A\Bracket.ipt"),
            Part("B", @"C:\B\BRACKET.ipt"));
        SmartExportWorkflow workflow = CreateWorkflow(gateway);
        Phase1StartResult session = workflow.Start();

        StepExportPlan plan = workflow.BuildStepPlan(
            session,
            [@"C:\A\Bracket.ipt", @"C:\B\BRACKET.ipt"],
            Destination);

        AssertError(plan, "OutputCollision");
    }

    [Fact]
    public void BuildStepPlanRejectsSelectionOutsideSession()
    {
        Fixture fixture = ValidFixture();

        StepExportPlan plan = fixture.Workflow.BuildStepPlan(fixture.Session, [@"C:\Models\Unknown.ipt"], Destination);

        AssertError(plan, "UnknownSelection");
        Assert.Contains(@"C:\Models\Unknown.ipt", plan.Issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildStepPlanRejectsEmptySelection()
    {
        Fixture fixture = ValidFixture();

        StepExportPlan plan = fixture.Workflow.BuildStepPlan(fixture.Session, [], Destination);

        AssertError(plan, "NoSelection");
    }

    [Fact]
    public void ExecuteStepPlanRefusesPlanWithErrors()
    {
        Fixture fixture = ValidFixture();
        StepExportPlan invalidPlan = new(
            [],
            [new ValidationIssue("Unsafe", "Unsafe plan.", ValidationSeverity.Error)]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => fixture.Workflow.ExecuteStepPlan(invalidPlan));

        Assert.Contains("validation errors", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Gateway.ExportCalls);
    }

    [Fact]
    public void ExecuteStepPlanIsolatesItemFailuresAndContinues()
    {
        FakeGateway gateway = WithOccurrences();
        gateway.ExportFailureBySource[@"C:\Models\Bad.ipt"] = new InvalidOperationException("Translator failed.");
        SmartExportWorkflow workflow = CreateWorkflow(gateway);
        StepExportPlan plan = new(
            [
                new StepExportPlanItem(@"C:\Models\Bad.ipt", @"C:\Exports\Bad.step"),
                new StepExportPlanItem(@"C:\Models\Good.ipt", @"C:\Exports\Good.step"),
            ],
            []);

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
                (@"C:\Models\Bad.ipt", @"C:\Exports\Bad.step"),
                (@"C:\Models\Good.ipt", @"C:\Exports\Good.step"),
            ],
            gateway.ExportCalls);
    }

    [Fact]
    public void ExecuteStepPlanSkipsNewOutputConflictAndContinues()
    {
        FakeGateway gateway = WithOccurrences(
            Part("A", @"C:\Models\Alpha.ipt"),
            Part("B", @"C:\Models\Beta.ipt"));
        FakeFileSystem fileSystem = new();
        SmartExportWorkflow workflow = CreateWorkflow(gateway, fileSystem);
        Phase1StartResult session = workflow.Start();
        StepExportPlan plan = workflow.BuildStepPlan(
            session,
            [@"C:\Models\Alpha.ipt", @"C:\Models\Beta.ipt"],
            Destination);
        Assert.True(plan.CanExecute);
        fileSystem.ExistingFiles.Add(@"C:\Exports\Alpha.step");

        StepExportBatchResult result = workflow.ExecuteStepPlan(plan);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.False(result.Items[0].Succeeded);
        Assert.Contains("already exists", result.Items[0].ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.Items[1].Succeeded);
        Assert.Equal(
            [(@"C:\Models\Beta.ipt", @"C:\Exports\Beta.step")],
            gateway.ExportCalls);
    }

    private static TopLevelOccurrenceSnapshot Part(string occurrenceName, string sourcePath) =>
        new(occurrenceName, sourcePath, ComponentDocumentKind.Part, false);

    private static FakeGateway WithOccurrences(params TopLevelOccurrenceSnapshot[] occurrences) =>
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

        public List<(string SourcePath, string OutputPath)> ExportCalls { get; } = [];

        public Dictionary<string, Exception> ExportFailureBySource { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ActiveAssemblyScan? ScanActiveAssembly()
        {
            ScanCalls++;
            return Scan;
        }

        public void ExportPartAsStep(string sourcePath, string outputPath)
        {
            ExportCalls.Add((sourcePath, outputPath));
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
