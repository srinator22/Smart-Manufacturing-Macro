// Purpose: Pin the FileNamingWorkflow.Plan guards added after the first independent review - companion
//   drawing safety, gate-before-allocate, cross-folder file-name collisions, and external parents.
// Inputs: Synthetic ActiveAssemblySnapshot fixtures over the fake gateway and fake file system.
// Outputs: Assertions on plan blockers, operation membership, and allocated numbers.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core, and this project's fakes.
// Assumptions: A document can appear in the open assembly without appearing in the enumerated project
//   scope (Inventor resolves references across project paths, and the scope enumeration skips reserved
//   folders), which is the only way a reused token can collide by name across folders.
// Validation source: .work/TASK.md "Review verdict" items R1 and R4 and the non-blocking findings on
//   cross-folder name collisions and parents outside the snapshot.

using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public class FileNamingPlanGuardTests
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
    public void PlanBlocksAModelWhoseCompanionDrawingIsVaultManaged()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
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
        fileSystem.MarkVaultManaged(drawingPath);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Contains(
            "'Widget.idw' is Vault-managed; rename it in Vault Explorer before renaming 'Widget.ipt'.",
            plan.Blockers);
        Assert.DoesNotContain(plan.Operations, op => op.CurrentFullPath == partPath);
    }

    [Fact]
    public void PlanBlocksAModelWhoseCompanionDrawingIsReadOnly()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
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
        fileSystem.MarkReadOnly(drawingPath);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Contains(
            "'Widget.idw' is read-only and cannot be renamed alongside 'Widget.ipt'.",
            plan.Blockers);
        Assert.DoesNotContain(plan.Operations, op => op.CurrentFullPath == partPath);
    }

    [Fact]
    public void PlanStillRenamesAModelWhoseCompanionDrawingIsNeitherManagedNorReadOnly()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
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

        Assert.Empty(plan.Blockers);
        Assert.Contains(plan.Operations, op => op.CurrentFullPath == partPath);
    }

    /// <summary>
    /// A row the options gate will discard must never reach the allocator. Allocating first and gating
    /// afterwards burns the number the discarded row would have taken, so the next row that really does
    /// get renamed skips it, and the tool manufactures exactly the sequence gap it reports (R4).
    /// The malformed row here carries a token from a different project, so it cannot reuse that token
    /// and would allocate a fresh 124-series part number if it were not gated off first.
    /// </summary>
    [Fact]
    public void PlanEvaluatesTheOptionGateBeforeAllocatingANumber()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string alphaPath = Path.Combine(ProjectRoot, "124-0001 Alpha.ipt");
        string betaPath = Path.Combine(ProjectRoot, "124-0002 Beta.ipt");
        string gammaPath = Path.Combine(ProjectRoot, "124-0003 Gamma.ipt");
        string malformedPath = Path.Combine(ProjectRoot, "901-0009  Odd Part .ipt");
        string unnumberedPath = Path.Combine(ProjectRoot, "Unnumbered.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(malformedPath, DocumentKind.Part, parents: [rootPath]),
                Doc(unnumberedPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, alphaPath, betaPath, gammaPath, malformedPath, unnumberedPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(
            analysis,
            Project124,
            new RenameOptions(RenameUnnumbered: true, NormalizeMalformed: false));

        RenameOperation operation = Assert.Single(plan.Operations);
        Assert.Equal(unnumberedPath, operation.CurrentFullPath);
        Assert.Equal("124-0004 Unnumbered.ipt", Path.GetFileName(operation.NewFullPath));

        // The resulting name set must contain no manufactured gap: 0001-0004 with nothing skipped.
        List<ParsedFileName> afterRename =
        [
            .. new[] { rootPath, alphaPath, betaPath, gammaPath, malformedPath }
                .Select(path => FileNameParser.Parse(Path.GetFileName(path))),
            FileNameParser.Parse(Path.GetFileName(operation.NewFullPath)),
        ];
        NamingReport report = NamingReport.Build(afterRename, Project124);
        Assert.DoesNotContain(report.ProjectFindings, finding => finding.Code == FindingCode.SequenceGap);
    }

    /// <summary>
    /// The project uses Inventor's unique-filenames mode, so a proposed name that already exists anywhere
    /// in scope is unresolvable even when the folders differ. The colliding document is deliberately in
    /// the open assembly but outside the enumerated scope: that is the only way its token survives the
    /// duplicate-number safeguard and gets reused into a name another folder already owns.
    /// </summary>
    [Fact]
    public void PlanBlocksAProposedNameThatAnotherFolderInScopeAlreadyUses()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string malformedPath = Path.Combine(ProjectRoot, "A", "124-0009  Widget .ipt");
        string otherFolderPath = Path.Combine(ProjectRoot, "B", "124-0009 Widget.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(malformedPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, otherFolderPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions(NormalizeMalformed: true));

        Assert.Contains(
            $"'124-0009 Widget.ipt' would duplicate a file name already used at '{otherFolderPath}'; "
                + "Inventor's unique-filenames mode cannot resolve it.",
            plan.Blockers);
        Assert.False(plan.CanExecute);
    }

    [Fact]
    public void PlanBlocksTwoProposalsInDifferentFoldersThatShareOneFileName()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string firstPath = Path.Combine(ProjectRoot, "A", "124-0009  Widget .ipt");
        string secondPath = Path.Combine(ProjectRoot, "B", "124-0009 Widget  .ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(firstPath, DocumentKind.Part, parents: [rootPath]),
                Doc(secondPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions(NormalizeMalformed: true));

        string firstProposedPath = Path.Combine(ProjectRoot, "A", "124-0009 Widget.ipt");
        Assert.Contains(
            $"'124-0009 Widget.ipt' would duplicate a file name already used at '{firstProposedPath}'; "
                + "Inventor's unique-filenames mode cannot resolve it.",
            plan.Blockers);
        Assert.False(plan.CanExecute);
    }

    /// <summary>
    /// A file whose name is only a number holds that number for real. It gets no proposal (there is no
    /// description to carry over), but its number must still be visible to allocation, or the next
    /// allocated part number lands on top of it. Here the highest part number in scope is 0002, held by
    /// the description-less file, so the unnumbered part must take 0003 and not 0002.
    /// </summary>
    [Fact]
    public void PlanSkipsANumberedFileWithNoDescriptionWithoutRemintingItsNumber()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string alphaPath = Path.Combine(ProjectRoot, "124-0001 Alpha.ipt");
        string noDescriptionPath = Path.Combine(ProjectRoot, "124-0002.ipt");
        string unnumberedPath = Path.Combine(ProjectRoot, "Unnumbered.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(noDescriptionPath, DocumentKind.Part, parents: [rootPath]),
                Doc(unnumberedPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, alphaPath, noDescriptionPath, unnumberedPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);

        NamingAnalysisRow noDescriptionRow = Assert.Single(analysis.Rows, row => row.FullPath == noDescriptionPath);
        Assert.Equal(NameState.NumberedWithoutDescription, noDescriptionRow.Parsed.State);
        Assert.Equal(RenameAction.None, noDescriptionRow.Action);
        Assert.Null(noDescriptionRow.ProposedFileName);
        Assert.Contains(
            "'124-0002.ipt' has a number but no description; add a description manually.",
            noDescriptionRow.Reasons);

        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        RenameOperation operation = Assert.Single(plan.Operations);
        Assert.Equal(unnumberedPath, operation.CurrentFullPath);
        Assert.Equal("124-0003 Unnumbered.ipt", Path.GetFileName(operation.NewFullPath));
        Assert.Empty(plan.Blockers);
    }

    [Fact]
    public void PlanBlocksARenameOfAFileReferencedFromOutsideTheActiveAssembly()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");
        string outsidePath = Path.Combine(ProjectRoot, "Other", "Other Rig.iam");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, parents: [rootPath], externalParents: [outsidePath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Contains(
            "'Widget.ipt' is also referenced by 'Other Rig.iam' which is not part of the active assembly; "
                + "close or include it first.",
            plan.Blockers);
        Assert.False(plan.CanExecute);
    }

    /// <summary>
    /// The root of the snapshot can itself be renamed - the user may have activated a sub-assembly while
    /// its own parent assembly is also open - and the root's referencing documents are by construction
    /// outside the snapshot (F1). Renaming the root without this guard would leave that outside parent
    /// pointing at a file name that no longer exists.
    /// </summary>
    [Fact]
    public void PlanBlocksARenameOfTheRootWhenItIsReferencedFromOutsideTheActiveAssembly()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "SubAssembly.iam");
        string outsidePath = Path.Combine(ProjectRoot, "Other", "Parent Assembly.iam");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true, externalParents: [outsidePath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Contains(
            "'SubAssembly.iam' is also referenced by 'Parent Assembly.iam' which is not part of the active assembly; "
                + "close or include it first.",
            plan.Blockers);
        Assert.Empty(plan.Operations);
    }

    /// <summary>
    /// A companion drawing referencing its own model is an external referencing document in Inventor's
    /// terms - a drawing is not part of the assembly's referenced-document set - but the plan renames it
    /// alongside its model, so it is not an unhandled outside reference and must not block.
    /// </summary>
    [Fact]
    public void PlanDoesNotTreatACompanionDrawingAsAnUnhandledExternalParent()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");
        string drawingPath = Path.Combine(ProjectRoot, "Widget.idw");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, parents: [rootPath], externalParents: [drawingPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath, drawingPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Empty(plan.Blockers);
        RenameOperation operation = Assert.Single(plan.Operations, op => op.CurrentFullPath == partPath);
        Assert.Single(operation.CompanionDrawings);
    }

    /// <summary>
    /// The primary mixed case: a checked-out assembly with new unmanaged parts alongside older files that
    /// are Vault-managed and, because they are checked in rather than out, not IsModifiable. The tool
    /// never renames a managed file locally, so that row's own modifiability must not block the plan or
    /// the row's action (N1); only the branch that actually calls SaveAs cares about IsModifiable.
    /// </summary>
    [Fact]
    public void ManagedNotModifiableRowGoesToVaultInstructionsWhileUnmanagedSiblingIsOperated()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string managedPath = Path.Combine(ProjectRoot, "ManagedPart.ipt");
        string unmanagedPath = Path.Combine(ProjectRoot, "UnmanagedPart.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(managedPath, DocumentKind.Part, isModifiable: false, parents: [rootPath]),
                Doc(unmanagedPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, managedPath, unmanagedPath]);
        fileSystem.MarkVaultManaged(managedPath);

        NamingAnalysis analysis = workflow.Analyze(Project124);

        NamingAnalysisRow managedRow = Assert.Single(analysis.Rows, row => row.FullPath == managedPath);
        Assert.Equal(RenameAction.VaultRename, managedRow.Action);
        Assert.Empty(managedRow.Reasons);

        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Empty(plan.Blockers);
        RenameOperation operation = Assert.Single(plan.Operations);
        Assert.Equal(unmanagedPath, operation.CurrentFullPath);
        VaultRenameInstruction instruction = Assert.Single(plan.VaultInstructions);
        Assert.Equal(managedPath, instruction.CurrentFullPath);
        Assert.True(plan.CanExecute);
    }
}
