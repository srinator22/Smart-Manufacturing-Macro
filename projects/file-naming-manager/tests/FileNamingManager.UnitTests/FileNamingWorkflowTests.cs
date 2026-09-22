// Purpose: Verify FileNamingWorkflow.Analyze/Plan/Execute against the Vault safety model in TASK.md.
// Inputs: Synthetic ActiveAssemblySnapshot fixtures and fake gateway/file-system/clock test doubles.
// Outputs: Assertions on analysis rows, plan ordering, blockers, Vault routing, and execution isolation.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core, and this project's fakes.
// Assumptions: Tests drive the real Analyze->Plan pipeline where realistic, and construct a RenamePlan
//   directly for Execute-isolation tests, since Execute's contract only depends on the plan shape.
// Validation source: .work/TASK.md "Vault safety model" section; the WP1 test list in the worker brief.

using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public class FileNamingWorkflowTests
{
    private const string ProjectRoot = @"C:\WMP\P124 GRM";
    private static readonly ProjectNumber Project124 = new(124);

    private static DocumentSnapshot Doc(
        string fullPath,
        DocumentKind kind,
        bool isRoot = false,
        bool isModifiable = true,
        bool isDirty = false,
        string[]? parents = null,
        string[]? externalParents = null,
        string? partNumber = null) =>
        new(fullPath, kind, isRoot, isModifiable, isDirty, false, parents ?? [], externalParents ?? [], partNumber);

    private static (FileNamingWorkflow Workflow, FakeInventorNamingGateway Gateway, FakeNamingFileSystem FileSystem)
        CreateWorkflow()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());
        return (workflow, gateway, fileSystem);
    }

    [Fact]
    public void AnalyzeWithNoActiveAssemblyReturnsRequiredAssemblyMessage()
    {
        (FileNamingWorkflow workflow, _, _) = CreateWorkflow();

        NamingAnalysis analysis = workflow.Analyze(null);

        Assert.False(analysis.IsSuccess);
        Assert.Equal(FileNamingWorkflow.RequiredAssemblyMessage, analysis.ErrorMessage);
        Assert.False(analysis.RootIsDirty);
        Assert.False(analysis.RootHasMissingReferences);
        Assert.Empty(analysis.Rows);
        Assert.Empty(analysis.Scope);
    }

    [Fact]
    public void PlanOrdersOperationsPartsThenSubAssemblyThenRoot()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string subPath = Path.Combine(ProjectRoot, "Sub.iam");
        string partPath = Path.Combine(ProjectRoot, "Part.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(subPath, DocumentKind.Assembly, parents: [rootPath]),
                Doc(partPath, DocumentKind.Part, parents: [subPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, subPath, partPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Empty(plan.Blockers);
        Assert.Equal(3, plan.Operations.Count);
        Assert.Equal(partPath, plan.Operations[0].CurrentFullPath);
        Assert.Equal(subPath, plan.Operations[1].CurrentFullPath);
        Assert.Equal(rootPath, plan.Operations[2].CurrentFullPath);
        Assert.Equal([subPath, rootPath], plan.ParentSaveOrder);
    }

    [Fact]
    public void PlanOrdersDeeperSubAssemblyBeforeShallowerSubAssembly()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string outerPath = Path.Combine(ProjectRoot, "Outer.iam");
        string innerPath = Path.Combine(ProjectRoot, "Inner.iam");
        string partPath = Path.Combine(ProjectRoot, "Part.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(outerPath, DocumentKind.Assembly, parents: [rootPath]),
                Doc(innerPath, DocumentKind.Assembly, parents: [outerPath]),
                Doc(partPath, DocumentKind.Part, parents: [innerPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, outerPath, innerPath, partPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Equal(
            [partPath, innerPath, outerPath, rootPath],
            plan.Operations.Select(op => op.CurrentFullPath));
    }

    [Fact]
    public void PlanBlocksOnDirtyRootNamingIt()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        gateway.Snapshot = new ActiveAssemblySnapshot(rootPath, true, false, [Doc(rootPath, DocumentKind.Assembly, isRoot: true)]);
        fileSystem.SetScope(ProjectRoot, [rootPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Blockers, b => b.Contains("Root.iam", StringComparison.Ordinal) && b.Contains("unsaved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanBlocksOnRootMissingReferencesNamingIt()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        gateway.Snapshot = new ActiveAssemblySnapshot(rootPath, false, true, [Doc(rootPath, DocumentKind.Assembly, isRoot: true)]);
        fileSystem.SetScope(ProjectRoot, [rootPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Blockers, b => b.Contains("Root.iam", StringComparison.Ordinal) && b.Contains("missing references", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanBlocksOnNonModifiableDocumentNamingIt()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string partPath = Path.Combine(ProjectRoot, "Part.ipt");
        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, isModifiable: false, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Blockers, b => b.Contains("Part.ipt", StringComparison.Ordinal) && b.Contains("not modifiable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanBlocksOnNonModifiableParentNamingIt()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string partPath = Path.Combine(ProjectRoot, "Part.ipt");
        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true, isModifiable: false),
                Doc(partPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Blockers, b => b.Contains("Root.iam", StringComparison.Ordinal) && b.Contains("not modifiable", StringComparison.OrdinalIgnoreCase));

        // The parent blocker has to name both documents: the operator needs to know which parent to
        // unlock and which child rename it is holding up, not just that something is not modifiable.
        Assert.Contains(
            "'Root.iam' is not modifiable, so its reference to 'Part.ipt' cannot be saved.",
            plan.Blockers);
    }

    /// <summary>
    /// A Vault-managed row is never saved as anyone's reference target (ComputeParentSaveOrder is built
    /// from Operations only, and a Vault-managed row never becomes an Operation), so a non-modifiable
    /// Vault-managed parent must never block the plan on the child's behalf (B2). The unmanaged sibling's
    /// legitimate rename must go through untouched.
    /// </summary>
    [Fact]
    public void PlanDoesNotBlockOnANonModifiableVaultManagedParent()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string managedSubPath = Path.Combine(ProjectRoot, "124-A002 Sub Assembly (sub-assembly).iam");
        string managedPartPath = Path.Combine(ProjectRoot, "gasket cutting template.ipt");
        string unmanagedPartPath = Path.Combine(ProjectRoot, "widget mount bracket.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(managedSubPath, DocumentKind.Assembly, isModifiable: false, parents: [rootPath]),
                Doc(managedPartPath, DocumentKind.Part, parents: [managedSubPath]),
                Doc(unmanagedPartPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, managedSubPath, managedPartPath, unmanagedPartPath]);
        fileSystem.MarkVaultManaged(managedSubPath);
        fileSystem.MarkVaultManaged(managedPartPath);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions(RenameUnnumbered: true));

        Assert.Empty(plan.Blockers);
        RenameOperation operation = Assert.Single(plan.Operations);
        Assert.Equal(unmanagedPartPath, operation.CurrentFullPath);
        VaultRenameInstruction instruction = Assert.Single(plan.VaultInstructions);
        Assert.Equal(managedPartPath, instruction.CurrentFullPath);
    }

    [Fact]
    public void PlanBlocksWhenTargetFileAlreadyExists()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");
        string collidingTarget = Path.Combine(ProjectRoot, "124-0001 Widget.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath]);
        fileSystem.MarkFileExists(collidingTarget);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Blockers, b => b.Contains(collidingTarget, StringComparison.Ordinal));
    }

    [Fact]
    public void PlanBlocksWhenTwoOperationsTargetTheSamePath()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string part1Path = Path.Combine(ProjectRoot, "124-0005  Widget .ipt");
        string part2Path = Path.Combine(ProjectRoot, "124-0005 Widget  .ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(part1Path, DocumentKind.Part, parents: [rootPath]),
                Doc(part2Path, DocumentKind.Part, parents: [rootPath]),
            ]);

        // part2Path is deliberately left out of the enumerated project scope (unlike
        // DuplicateNumbersAreReportedAndNeverReproposed, where both duplicates are in scope and the
        // duplicate-number safeguard abstains for both). The open assembly still carries it as a document,
        // so Plan still proposes a rename for it from the same, scope-blind token; the duplicate-number
        // safeguard cannot see it and would not catch this collision, so the same-target guard remains the
        // backstop that must still block it.
        fileSystem.SetScope(ProjectRoot, [rootPath, part1Path]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions(NormalizeMalformed: true));

        Assert.False(plan.CanExecute);
        Assert.Contains(
            "Two or more documents would rename to the same target: "
                + "124-0005  Widget .ipt, 124-0005 Widget  .ipt.",
            plan.Blockers);
    }

    [Fact]
    public void PlanRoutesVaultManagedFilesToVaultInstructionsNotOperations()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string managedPartPath = Path.Combine(ProjectRoot, "ManagedWidget.ipt");
        string unmanagedPartPath = Path.Combine(ProjectRoot, "UnmanagedWidget.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(managedPartPath, DocumentKind.Part, parents: [rootPath]),
                Doc(unmanagedPartPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, managedPartPath, unmanagedPartPath]);
        fileSystem.MarkVaultManaged(managedPartPath);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Contains(plan.VaultInstructions, v => v.CurrentFullPath == managedPartPath);
        Assert.DoesNotContain(plan.Operations, op => op.CurrentFullPath == managedPartPath);
        Assert.Contains(plan.Operations, op => op.CurrentFullPath == unmanagedPartPath);
        Assert.DoesNotContain(plan.VaultInstructions, v => v.CurrentFullPath == unmanagedPartPath);
    }

    [Fact]
    public void DuplicateNumbersAreReportedAndNeverReproposed()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Full Double Stack Mechanism (main assembly).iam");
        string taglessPath = Path.Combine(ProjectRoot, "124-A001 Full Double Stack Mechanism .iam");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(taglessPath, DocumentKind.Assembly, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, taglessPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);

        NamingAnalysisRow taglessRow = Assert.Single(analysis.Rows, row => row.FullPath == taglessPath);
        Assert.Null(taglessRow.ProposedFileName);
        Assert.Equal(RenameAction.None, taglessRow.Action);
        Assert.Equal(
            ["'124-A001' is used by more than one file; resolve the duplicate manually."],
            taglessRow.Reasons);

        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.DoesNotContain(plan.Operations, op => op.CurrentFullPath == taglessPath);
        Assert.DoesNotContain(plan.VaultInstructions, v => v.CurrentFullPath == taglessPath);

        Assert.NotNull(analysis.Report);
        Assert.Contains(
            analysis.Report!.ProjectFindings,
            finding => finding.Code == FindingCode.DuplicateNumber
                && finding.FileNames.Contains(Path.GetFileName(rootPath))
                && finding.FileNames.Contains(Path.GetFileName(taglessPath)));
    }

    [Fact]
    public void PlanIncludesCompanionDrawingSharingTheModelsCurrentStem()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");
        string drawingPath = Path.Combine(ProjectRoot, "Widget.idw");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath, drawingPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        RenameOperation partOperation = Assert.Single(plan.Operations, op => op.CurrentFullPath == partPath);
        (string current, string @new) = Assert.Single(partOperation.CompanionDrawings);
        Assert.Equal(drawingPath, current);
        Assert.Equal(
            Path.GetFileNameWithoutExtension(partOperation.NewFullPath) + ".idw",
            Path.GetFileName(@new));
    }

    /// <summary>
    /// Every companion drawing of every operation is opened in a single pre-open phase before the first
    /// rename, not lazily per operation. A drawing opened only when its own model's turn comes would, by
    /// then, have had an earlier operation rename a model it also references, and that earlier model's
    /// original is archived at the end of Execute; the late-opened drawing would resolve to the moved file.
    /// </summary>
    [Fact]
    public void ExecuteOpensEveryCompanionDrawingBeforeTheFirstRename()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _) = CreateWorkflow();

        const string Part1Path = @"C:\P\Part1.ipt";
        const string NewPart1Path = @"C:\P\124-0001 Part1.ipt";
        const string Drawing1Path = @"C:\P\Part1.idw";
        const string NewDrawing1Path = @"C:\P\124-0001 Part1.idw";
        const string Part2Path = @"C:\P\Part2.ipt";
        const string NewPart2Path = @"C:\P\124-0002 Part2.ipt";
        const string Drawing2Path = @"C:\P\Part2.idw";
        const string NewDrawing2Path = @"C:\P\124-0002 Part2.idw";

        RenameOperation operation1 = new(
            Part1Path,
            NewPart1Path,
            DocumentKind.Part,
            [],
            [(Drawing1Path, NewDrawing1Path)],
            null);
        RenameOperation operation2 = new(
            Part2Path,
            NewPart2Path,
            DocumentKind.Part,
            [],
            [(Drawing2Path, NewDrawing2Path)],
            null);
        RenamePlan plan = new(@"C:\P", [operation1, operation2], [], [], []);

        workflow.Execute(plan);

        string log = string.Join(", ", gateway.CallLog);
        int lastEnsureOpenIndex = gateway.CallLog.FindLastIndex(entry => entry.StartsWith("EnsureOpen:", StringComparison.Ordinal));
        int firstRenameIndex = gateway.CallLog.FindIndex(entry => entry.StartsWith("Rename:", StringComparison.Ordinal));

        Assert.Contains($"EnsureOpen:{Drawing1Path}", gateway.CallLog);
        Assert.Contains($"EnsureOpen:{Drawing2Path}", gateway.CallLog);
        Assert.True(firstRenameIndex >= 0, $"Expected at least one Rename call. CallLog: {log}");
        Assert.True(
            lastEnsureOpenIndex < firstRenameIndex,
            $"Every companion drawing must be opened before the first rename. CallLog: {log}");

        int renameModel1Index = gateway.CallLog.IndexOf($"Rename:{Part1Path}->{NewPart1Path}");
        int renameDrawing1Index = gateway.CallLog.IndexOf($"Rename:{Drawing1Path}->{NewDrawing1Path}");
        Assert.True(renameModel1Index >= 0, $"Expected a Rename call for the model. CallLog: {log}");
        Assert.True(renameModel1Index < renameDrawing1Index, "The model must be renamed before its companion drawing.");
    }

    /// <summary>
    /// The pre-open phase stays per-item isolated: a companion that cannot be opened fails only its own
    /// operation, and never aborts Execute or the other operations.
    /// </summary>
    [Fact]
    public void ExecuteFailsOnlyTheOperationWhoseCompanionCannotBeOpened()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _) = CreateWorkflow();

        RenameOperation operation1 = new(
            @"C:\P\Part1.ipt",
            @"C:\P\124-0001 Part1.ipt",
            DocumentKind.Part,
            [],
            [(@"C:\P\Part1.idw", @"C:\P\124-0001 Part1.idw")],
            null);
        RenameOperation operation2 = new(
            @"C:\P\Part2.ipt",
            @"C:\P\124-0002 Part2.ipt",
            DocumentKind.Part,
            [],
            [],
            null);
        RenamePlan plan = new(@"C:\P", [operation1, operation2], [], [], []);

        gateway.FailEnsureOpenFor(@"C:\P\Part1.idw", new InvalidOperationException("drawing is checked out"));

        RenameExecution execution = workflow.Execute(plan);

        Assert.False(execution.Items[0].Succeeded);
        Assert.Contains("drawing is checked out", execution.Items[0].ErrorMessage);
        Assert.True(execution.Items[1].Succeeded);
        Assert.DoesNotContain(gateway.RenameCalls, call => call.Current == @"C:\P\Part1.ipt");
        Assert.Contains(gateway.RenameCalls, call => call.Current == @"C:\P\Part2.ipt");
    }

    [Fact]
    public void ExecuteIsolatesFailuresSavesParentsOfSuccessesAndManifestsOnlySuccesses()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        RenameOperation op1 = new(@"C:\P\Part1.ipt", @"C:\P\124-0001 Part1.ipt", DocumentKind.Part, [@"C:\P\Sub1.iam"], [], null);
        RenameOperation op2 = new(@"C:\P\Part2.ipt", @"C:\P\124-0002 Part2.ipt", DocumentKind.Part, [@"C:\P\Sub2.iam"], [], null);
        RenameOperation op3 = new(@"C:\P\Part3.ipt", @"C:\P\124-0003 Part3.ipt", DocumentKind.Part, [@"C:\P\Sub3.iam"], [], null);
        RenamePlan plan = new(
            @"C:\P",
            [op1, op2, op3],
            [],
            [],
            [@"C:\P\Sub1.iam", @"C:\P\Sub2.iam", @"C:\P\Sub3.iam"]);

        gateway.FailRenameFor(op2.CurrentFullPath, new InvalidOperationException("boom"));

        RenameExecution execution = workflow.Execute(plan);

        Assert.Equal(3, execution.Items.Count);
        Assert.True(execution.Items[0].Succeeded);
        Assert.False(execution.Items[1].Succeeded);
        Assert.Contains("boom", execution.Items[1].ErrorMessage);
        Assert.True(execution.Items[2].Succeeded);

        Assert.Contains(@"C:\P\Sub1.iam", gateway.SaveCalls);
        Assert.Contains(@"C:\P\Sub3.iam", gateway.SaveCalls);
        Assert.DoesNotContain(@"C:\P\Sub2.iam", gateway.SaveCalls);

        Assert.NotNull(execution.Manifest);
        Assert.Equal(2, execution.Manifest!.Entries.Count);
        Assert.Contains(execution.Manifest.Entries, e => e.OriginalPath == op1.CurrentFullPath);
        Assert.Contains(execution.Manifest.Entries, e => e.OriginalPath == op3.CurrentFullPath);
        Assert.DoesNotContain(execution.Manifest.Entries, e => e.OriginalPath == op2.CurrentFullPath);

        Assert.Contains(fileSystem.MoveCalls, m => m.FullPath == op1.CurrentFullPath);
        Assert.Contains(fileSystem.MoveCalls, m => m.FullPath == op3.CurrentFullPath);
        Assert.DoesNotContain(fileSystem.MoveCalls, m => m.FullPath == op2.CurrentFullPath);
    }

    [Fact]
    public void ExecuteSurfacesParentSaveFailures()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        RenameOperation op1 = new(@"C:\P\Part1.ipt", @"C:\P\124-0001 Part1.ipt", DocumentKind.Part, [@"C:\P\Sub1.iam"], [], null);
        RenamePlan plan = new(@"C:\P", [op1], [], [], [@"C:\P\Sub1.iam"]);

        gateway.FailSaveFor(@"C:\P\Sub1.iam", new InvalidOperationException("parent locked"));

        RenameExecution execution = workflow.Execute(plan);

        ParentSaveFailure failure = Assert.Single(execution.ParentSaveFailures);
        Assert.Equal(@"C:\P\Sub1.iam", failure.ParentFullPath);
        Assert.Contains("parent locked", failure.ErrorMessage);
        Assert.False(execution.OriginalsMoved);
        Assert.Null(execution.Manifest);
        Assert.Empty(fileSystem.MoveCalls);
    }

    [Fact]
    public void ExecuteMovesOriginalsOnlyWhenEveryParentSaved()
    {
        (FileNamingWorkflow workflow, _, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        RenameOperation op1 = new(@"C:\P\Part1.ipt", @"C:\P\124-0001 Part1.ipt", DocumentKind.Part, [@"C:\P\Sub1.iam"], [], null);
        RenamePlan plan = new(@"C:\P", [op1], [], [], [@"C:\P\Sub1.iam"]);

        RenameExecution execution = workflow.Execute(plan);

        Assert.True(execution.OriginalsMoved);
        Assert.NotNull(execution.Manifest);
        Assert.Empty(execution.ParentSaveFailures);
        Assert.Contains(fileSystem.MoveCalls, m => m.FullPath == op1.CurrentFullPath);
    }

    [Fact]
    public void ExecuteThrowsWhenPlanHasBlockers()
    {
        (FileNamingWorkflow workflow, _, _) = CreateWorkflow();
        RenamePlan plan = new(@"C:\P", [], ["some blocker"], [], []);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => workflow.Execute(plan));
        Assert.Equal("Cannot execute a rename plan that has blockers.", thrown.Message);
    }

    [Fact]
    public void ExecuteSetsThePartNumberPropertyOnlyWhenTheOperationCarriesOne()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _) = CreateWorkflow();

        RenameOperation withNumber = new(@"C:\P\Part1.ipt", @"C:\P\124-0001 Part1.ipt", DocumentKind.Part, [], [], "124-0001");
        RenameOperation withoutNumber = new(@"C:\P\Part2.ipt", @"C:\P\124-0002 Part2.ipt", DocumentKind.Part, [], [], null);
        RenamePlan plan = new(@"C:\P", [withNumber, withoutNumber], [], [], []);

        workflow.Execute(plan);

        (string path, string partNumber) = Assert.Single(gateway.SetPartNumberCalls);
        Assert.Equal(@"C:\P\124-0001 Part1.ipt", path);
        Assert.Equal("124-0001", partNumber);
    }

    /// <summary>
    /// The archive folder is stamped with the UTC instant the run started, so two runs on the same day
    /// never collide and the manifest can be matched to a session after the fact.
    /// </summary>
    [Fact]
    public void ExecuteArchivesUnderAUtcTimestampedFolderAndManifestsCompanionDrawingsToo()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FakeClock clock = new() { UtcNow = new DateTimeOffset(2026, 9, 23, 14, 5, 6, TimeSpan.Zero) };
        FileNamingWorkflow workflow = new(gateway, fileSystem, clock);

        RenameOperation operation = new(
            @"C:\P\Sub\Widget.ipt",
            @"C:\P\Sub\124-0001 Widget.ipt",
            DocumentKind.Part,
            [],
            [(@"C:\P\Sub\Widget.idw", @"C:\P\Sub\124-0001 Widget.idw")],
            null);
        RenamePlan plan = new(@"C:\P", [operation], [], [], []);

        RenameExecution execution = workflow.Execute(plan);

        const string ExpectedOriginalsRoot = @"C:\P\_renamed-originals\20260923T140506Z";
        Assert.All(fileSystem.MoveCalls, move => Assert.Equal(ExpectedOriginalsRoot, move.OriginalsRoot));

        RenameManifestEntry drawingEntry = Assert.Single(
            execution.Manifest!.Entries,
            entry => entry.OriginalPath == @"C:\P\Sub\Widget.idw");
        Assert.Equal(Path.Combine(ExpectedOriginalsRoot, @"Sub\Widget.idw"), drawingEntry.ArchivedPath);
        Assert.Equal(@"C:\P\Sub\124-0001 Widget.idw", drawingEntry.RenamedTo);
        Assert.True(drawingEntry.Archived);

        RenameManifestEntry plannedDrawingEntry = Assert.Single(
            fileSystem.WrittenManifests[0].Entries,
            entry => entry.OriginalPath == @"C:\P\Sub\Widget.idw");
        Assert.False(plannedDrawingEntry.Archived);
    }

    /// <summary>
    /// When every rename fails there is nothing to archive, so there is no manifest and nothing was left
    /// half-moved: OriginalsMoved is still true, because no original needed moving.
    /// </summary>
    [Fact]
    public void ExecuteWithNothingToArchiveWritesNoManifestAndReportsOriginalsMoved()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        RenameOperation operation = new(@"C:\P\Part1.ipt", @"C:\P\124-0001 Part1.ipt", DocumentKind.Part, [], [], null);
        RenamePlan plan = new(@"C:\P", [operation], [], [], []);
        gateway.FailRenameFor(operation.CurrentFullPath, new InvalidOperationException("boom"));

        RenameExecution execution = workflow.Execute(plan);

        Assert.Null(execution.Manifest);
        Assert.True(execution.OriginalsMoved);
        Assert.Empty(execution.ArchiveFailures);
        Assert.Empty(fileSystem.WrittenManifests);
        Assert.Empty(fileSystem.MoveCalls);
    }

    /// <summary>
    /// Once one companion of an operation cannot be opened, that operation is already lost, so the
    /// remaining companions of that same operation are left closed rather than opened for nothing.
    /// </summary>
    [Fact]
    public void ExecuteStopsPreOpeningAnOperationsCompanionsAfterTheFirstFailure()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _) = CreateWorkflow();

        RenameOperation operation = new(
            @"C:\P\Widget.ipt",
            @"C:\P\124-0001 Widget.ipt",
            DocumentKind.Part,
            [],
            [(@"C:\P\Widget.idw", @"C:\P\124-0001 Widget.idw"), (@"C:\P\Widget.dwg", @"C:\P\124-0001 Widget.dwg")],
            null);
        RenamePlan plan = new(@"C:\P", [operation], [], [], []);

        gateway.FailEnsureOpenFor(@"C:\P\Widget.idw", new InvalidOperationException("drawing is checked out"));

        workflow.Execute(plan);

        Assert.DoesNotContain(@"EnsureOpen:C:\P\Widget.dwg", gateway.CallLog);
    }

    [Fact]
    public void FakeFileSystemExposesNoDeleteApi()
    {
        IEnumerable<string> memberNames = typeof(INamingFileSystem).GetMethods()
            .Select(m => m.Name)
            .Concat(typeof(FakeNamingFileSystem).GetMethods().Select(m => m.Name));

        Assert.DoesNotContain(memberNames, name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Pins the exact proposed file name for every recognized state, not just whether a proposal exists,
    /// because a gate that lets the right rows through while proposing the wrong name is the same defect
    /// class as a gate that lets the wrong rows through. The expected names encode what the parser really
    /// hands the formatter: a reused token keeps its number, an unnumbered or legacy name allocates the
    /// next free part number (0001 here - the only other scope file is the A001 root assembly), and a
    /// state that parses with no description at all (RevisionSuffixed, whose stem is only a number plus a
    /// revision) never gets a proposal, because falling back to the stem would embed the old token in the
    /// new description.
    /// </summary>
    [Theory]
    // MalformedWhitespace is gated by NormalizeMalformed; the token is reused, only the spacing changes.
    [InlineData("124-0074  wakeup fan mesh cartirdge .ipt", DocumentKind.Part, false, true, null)]
    [InlineData("124-0074  wakeup fan mesh cartirdge .ipt", DocumentKind.Part, true, true, "124-0074 wakeup fan mesh cartirdge.ipt")]
    // TaglessAssembly is gated by NormalizeMalformed; a non-root assembly gains the (sub-assembly) tag.
    [InlineData("124-A005 Bracket Mount.iam", DocumentKind.Assembly, false, true, null)]
    [InlineData("124-A005 Bracket Mount.iam", DocumentKind.Assembly, true, true, "124-A005 Bracket Mount (sub-assembly).iam")]
    // RevisionSuffixed parses to a token with a null description, so it never gets a proposal at all.
    [InlineData("101-0001-A0.ipt", DocumentKind.Part, false, true, null)]
    [InlineData("101-0001-A0.ipt", DocumentKind.Part, true, true, null)]
    // UnnumberedDescription is gated by RenameUnnumbered; the whole stem is the description.
    [InlineData("gasket cutting template.ipt", DocumentKind.Part, true, false, null)]
    [InlineData("gasket cutting template.ipt", DocumentKind.Part, true, true, "124-0001 gasket cutting template.ipt")]
    // LegacyPrefix is gated by RenameUnnumbered; the description is the text after the legacy P-prefix.
    [InlineData("P74B 32.75mm 0.85.ipt", DocumentKind.Part, true, false, null)]
    [InlineData("P74B 32.75mm 0.85.ipt", DocumentKind.Part, true, true, "124-0001 32.75mm 0.85.ipt")]
    // CopySuffix is gated by RenameUnnumbered; the token is reused and the copy suffix dropped.
    [InlineData("124-0005 Widget_1.ipt", DocumentKind.Part, true, false, null)]
    [InlineData("124-0005 Widget_1.ipt", DocumentKind.Part, true, true, "124-0005 Widget.ipt")]
    public void RenameOptionsGateStatesAsDocumented(
        string fileName,
        DocumentKind kind,
        bool normalizeMalformed,
        bool renameUnnumbered,
        string? expectedProposedFileName)
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string targetPath = Path.Combine(ProjectRoot, fileName);

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(targetPath, kind, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, targetPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions(renameUnnumbered, normalizeMalformed));

        Assert.Empty(plan.Blockers);

        RenameOperation? operation = plan.Operations.FirstOrDefault(op => op.CurrentFullPath == targetPath);
        string? actualProposedFileName = operation is not null
            ? Path.GetFileName(operation.NewFullPath)
            : plan.VaultInstructions.FirstOrDefault(v => v.CurrentFullPath == targetPath)?.ProposedFileName;

        Assert.Equal(expectedProposedFileName, actualProposedFileName);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void CanonicalAndNumberedWithoutDescriptionNamesNeverPropose(bool normalizeMalformed, bool renameUnnumbered)
    {
        // '124-0002.ipt' parses as NumberedWithoutDescription, not Unparseable - it is a real number token
        // with no description text to carry into a proposal, so it must never be proposed either.
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string canonicalPath = Path.Combine(ProjectRoot, "124-0001 Canonical Widget.ipt");
        string unparseablePath = Path.Combine(ProjectRoot, "124-0002.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(canonicalPath, DocumentKind.Part, parents: [rootPath]),
                Doc(unparseablePath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, canonicalPath, unparseablePath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions(renameUnnumbered, normalizeMalformed));

        Assert.DoesNotContain(plan.Operations, op => op.CurrentFullPath == canonicalPath);
        Assert.DoesNotContain(plan.Operations, op => op.CurrentFullPath == unparseablePath);
        Assert.DoesNotContain(plan.VaultInstructions, v => v.CurrentFullPath == canonicalPath);
        Assert.DoesNotContain(plan.VaultInstructions, v => v.CurrentFullPath == unparseablePath);
    }
}
