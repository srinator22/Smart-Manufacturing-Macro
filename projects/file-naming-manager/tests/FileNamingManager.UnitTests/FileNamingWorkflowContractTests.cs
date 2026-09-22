// Purpose: Pin FileNamingWorkflow's argument guards, exact operator-facing messages, per-row action and
//   reason derivation, companion matching, assembly role tagging, and the Part Number property rule.
// Inputs: Synthetic ActiveAssemblySnapshot fixtures over the fake gateway, file system, and clock.
// Outputs: Assertions on thrown exceptions, blocker and reason strings, and RenameOperation contents.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core, and this project's fakes.
// Assumptions: These are contract tests - each asserts the exact string or value an operator or a caller
//   sees, because a message that says nothing useful is the same defect as a missing message.
// Validation source: .work/TASK.md "Vault safety model" and "Naming scheme" sections.

using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public class FileNamingWorkflowContractTests
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
    public void ConstructorRejectsEveryNullPort()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FakeClock clock = new();

        Assert.Throws<ArgumentNullException>(() => new FileNamingWorkflow(null!, fileSystem, clock));
        Assert.Throws<ArgumentNullException>(() => new FileNamingWorkflow(gateway, null!, clock));
        Assert.Throws<ArgumentNullException>(() => new FileNamingWorkflow(gateway, fileSystem, null!));
    }

    [Fact]
    public void PlanRejectsNullArgumentsAndAFailedAnalysis()
    {
        (FileNamingWorkflow workflow, _, _) = CreateWorkflow();
        NamingAnalysis failed = workflow.Analyze(null);

        Assert.Throws<ArgumentNullException>(() => workflow.Plan(null!, Project124, new RenameOptions()));
        Assert.Throws<ArgumentNullException>(() => workflow.Plan(failed, Project124, null!));

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(() => workflow.Plan(failed, Project124, new RenameOptions()));
        Assert.Equal(
            $"Cannot plan renames from a failed analysis: {FileNamingWorkflow.RequiredAssemblyMessage}",
            thrown.Message);
    }

    [Fact]
    public void ExecuteRejectsANullPlan()
    {
        (FileNamingWorkflow workflow, _, _) = CreateWorkflow();

        Assert.Throws<ArgumentNullException>(() => workflow.Execute(null!));
    }

    /// <summary>
    /// The confirmed project number always wins over the suggestion: the operator may type a different
    /// one after Analyze prefilled it, and silently planning against the suggestion instead would rename
    /// files into the wrong project's series.
    /// </summary>
    [Fact]
    public void AnalyzeUsesTheSuppliedProjectNumberOverTheSuggestedOne()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath]);

        NamingAnalysis analysis = workflow.Analyze(new ProjectNumber(155));

        Assert.Equal(124, analysis.SuggestedProject!.Value.Value);
        NamingAnalysisRow partRow = Assert.Single(analysis.Rows, row => row.FullPath == partPath);
        Assert.Equal("155-0001 Widget.ipt", partRow.ProposedFileName);
    }

    [Theory]
    [InlineData(true, false, true, "The root assembly has unsaved changes.")]
    [InlineData(false, true, true, "The root assembly has missing references.")]
    [InlineData(false, false, false, "'Widget.ipt' is not modifiable.")]
    public void BuildRowBlocksAndExplainsEachUnsafeCondition(
        bool rootIsDirty,
        bool rootHasMissingReferences,
        bool isModifiable,
        string expectedReason)
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            rootIsDirty,
            rootHasMissingReferences,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, isModifiable: isModifiable, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);

        NamingAnalysisRow partRow = Assert.Single(analysis.Rows, row => row.FullPath == partPath);
        Assert.Equal(RenameAction.Blocked, partRow.Action);
        Assert.Equal([expectedReason], partRow.Reasons);
    }

    [Fact]
    public void BuildRowMarksAnUnmanagedFileRenameAndAManagedFileVaultRenameWithNoReasons()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string unmanagedPath = Path.Combine(ProjectRoot, "Widget.ipt");
        string managedPath = Path.Combine(ProjectRoot, "Gadget.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(unmanagedPath, DocumentKind.Part, parents: [rootPath]),
                Doc(managedPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, unmanagedPath, managedPath]);
        fileSystem.MarkVaultManaged(managedPath);

        NamingAnalysis analysis = workflow.Analyze(Project124);

        NamingAnalysisRow unmanagedRow = Assert.Single(analysis.Rows, row => row.FullPath == unmanagedPath);
        NamingAnalysisRow managedRow = Assert.Single(analysis.Rows, row => row.FullPath == managedPath);

        Assert.Equal(RenameAction.Rename, unmanagedRow.Action);
        Assert.Empty(unmanagedRow.Reasons);
        Assert.Equal(VaultState.Unmanaged, unmanagedRow.VaultState);
        Assert.Equal(RenameAction.VaultRename, managedRow.Action);
        Assert.Empty(managedRow.Reasons);
        Assert.Equal(VaultState.Managed, managedRow.VaultState);
    }

    /// <summary>
    /// The "number but no description" reason belongs only to names that really do carry a number and no
    /// description. An unrecognized name has neither, and a name that has a description but no proposal
    /// for some other reason has already been given that other reason.
    /// </summary>
    [Fact]
    public void BuildRowGivesTheNoDescriptionReasonOnlyToNumberedNamesWithNoDescription()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string unparseablePath = Path.Combine(ProjectRoot, "124-0000.ipt");
        string canonicalPath = Path.Combine(ProjectRoot, "124-0007 Canonical Widget.ipt");
        string noDescriptionPath = Path.Combine(ProjectRoot, "124-0008.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(unparseablePath, DocumentKind.Part, parents: [rootPath]),
                Doc(canonicalPath, DocumentKind.Part, parents: [rootPath]),
                Doc(noDescriptionPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, unparseablePath, canonicalPath, noDescriptionPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);

        Assert.Equal(NameState.Unparseable, Assert.Single(analysis.Rows, r => r.FullPath == unparseablePath).Parsed.State);
        Assert.Empty(Assert.Single(analysis.Rows, r => r.FullPath == unparseablePath).Reasons);
        Assert.Empty(Assert.Single(analysis.Rows, r => r.FullPath == canonicalPath).Reasons);
        Assert.Equal(
            ["'124-0008.ipt' has a number but no description; add a description manually."],
            Assert.Single(analysis.Rows, r => r.FullPath == noDescriptionPath).Reasons);
    }

    /// <summary>
    /// The role tag written into a proposal comes from the file's own name when it already carries one,
    /// and only falls back to "main for the root, sub for everything else" when the name has no tag.
    /// A non-root assembly that calls itself the main assembly keeps that tag; it is not this tool's job
    /// to relabel it while it is only fixing the spacing.
    /// </summary>
    [Fact]
    public void ProposalKeepsAnExistingRoleTagAndTagsTheUntaggedRootAsMain()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root.iam");
        string taggedSubPath = Path.Combine(ProjectRoot, "124-A005  Rig (main assembly).iam");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(taggedSubPath, DocumentKind.Assembly, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, taggedSubPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions(NormalizeMalformed: true));

        Assert.Equal(
            "124-A001 Root (main assembly).iam",
            Path.GetFileName(Assert.Single(plan.Operations, op => op.CurrentFullPath == rootPath).NewFullPath));
        Assert.Equal(
            "124-A005 Rig (main assembly).iam",
            Path.GetFileName(Assert.Single(plan.Operations, op => op.CurrentFullPath == taggedSubPath).NewFullPath));
    }

    /// <summary>
    /// A companion drawing is one that shares the model's exact stem AND sits in the model's own folder.
    /// A same-stem drawing in another folder belongs to another model under Inventor's unique-filenames
    /// mode only by coincidence, and a different-stem drawing in the same folder is simply unrelated.
    /// </summary>
    [Fact]
    public void CompanionDrawingsRequireBothTheSameStemAndTheSameFolder()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");
        string sameFolderSameStem = Path.Combine(ProjectRoot, "Widget.idw");
        string sameFolderOtherStem = Path.Combine(ProjectRoot, "Other.idw");
        string otherFolderSameStem = Path.Combine(ProjectRoot, "Sub", "Widget.idw");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(
            ProjectRoot,
            [rootPath, partPath, sameFolderSameStem, sameFolderOtherStem, otherFolderSameStem]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        RenameOperation operation = Assert.Single(plan.Operations, op => op.CurrentFullPath == partPath);
        (string companionCurrent, string companionNew) = Assert.Single(operation.CompanionDrawings);
        Assert.Equal(sameFolderSameStem, companionCurrent);
        Assert.Equal(Path.Combine(ProjectRoot, "124-0001 Widget.idw"), companionNew);
    }

    /// <summary>
    /// A Vault-managed model still has a companion drawing; the exported plan must list it so the operator
    /// renames it too, even though the tool applies no blocker for it here - AddCompanionBlockers runs
    /// only on the unmanaged branch, because v1 never renames a managed file's drawing locally (N2).
    /// </summary>
    [Fact]
    public void VaultRenameInstructionCarriesItsCompanionDrawingsInformationally()
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
        fileSystem.MarkVaultManaged(partPath);
        fileSystem.MarkVaultManaged(drawingPath);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Empty(plan.Blockers);
        VaultRenameInstruction instruction = Assert.Single(plan.VaultInstructions, v => v.CurrentFullPath == partPath);
        (string companionCurrent, string companionNew) = Assert.Single(instruction.CompanionDrawings);
        Assert.Equal(drawingPath, companionCurrent);
        Assert.Equal(Path.Combine(ProjectRoot, "124-0001 Widget.idw"), companionNew);
    }

    /// <summary>
    /// Part Number is only overwritten when it is empty or still equals the old file stem, because any
    /// other value is a number a person chose deliberately and the tool must not silently replace it.
    /// </summary>
    [Theory]
    [InlineData(true, null, "124-0001")]
    [InlineData(true, "", "124-0001")]
    [InlineData(true, "   ", "124-0001")]
    [InlineData(true, "Widget", "124-0001")]
    [InlineData(true, "LEGACY-77", null)]
    [InlineData(false, null, null)]
    public void PartNumberIsSetOnlyWhenEmptyOrStillEqualToTheOldStem(
        bool setPartNumberProperty,
        string? currentPartNumber,
        string? expectedPartNumberToSet)
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, parents: [rootPath], partNumber: currentPartNumber),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(
            analysis,
            Project124,
            new RenameOptions(SetPartNumberProperty: setPartNumberProperty));

        RenameOperation operation = Assert.Single(plan.Operations, op => op.CurrentFullPath == partPath);
        Assert.Equal(expectedPartNumberToSet, operation.PartNumberToSet);
    }
}
