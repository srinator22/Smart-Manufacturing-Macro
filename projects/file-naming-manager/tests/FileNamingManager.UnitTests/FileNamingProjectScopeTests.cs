// Purpose: Pin the project-scope boundary - a referenced document outside the project scope is never
//   renamed, and an original outside the project root is never archived out of _renamed-originals.
// Inputs: Synthetic ActiveAssemblySnapshot fixtures whose documents deliberately live in reserved
//   folders and outside the project root, plus a RenamePlan constructed directly for the Execute case.
// Outputs: Assertions on row action/reason, plan membership, blocker absence, and the fake file
//   system's recorded MoveToOriginals calls.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core, and this project's fakes.
// Assumptions: An assembly routinely references Content Center parts, 3rd Party Hardware, and parts
//   owned by another project. Those files are other people's property: the tool must neither rename
//   them nor relocate their originals, and their presence must not hold up the rows it does own.
// Validation source: PR review finding T1 (data safety) on the shipped File Naming Manager.

using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public class FileNamingProjectScopeTests
{
    private const string ProjectRoot = @"C:\WMP\P124 GRM";
    private static readonly ProjectNumber Project124 = new(124);

    private static DocumentSnapshot Doc(
        string fullPath,
        DocumentKind kind,
        bool isRoot = false,
        string[]? parents = null) =>
        new(fullPath, kind, isRoot, true, false, false, parents ?? [], [], null);

    private static (FileNamingWorkflow Workflow, FakeInventorNamingGateway Gateway, FakeNamingFileSystem FileSystem)
        CreateWorkflow()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());
        return (workflow, gateway, fileSystem);
    }

    private static string ExpectedReason(string fileName) =>
        $"'{fileName}' is outside the project scope ({ProjectRoot}, excluding OldVersions, _V, "
        + "3rd Party Hardware, Content Center Files and _renamed-originals) and is never renamed.";

    /// <summary>
    /// Each of the three real cases: a 3rd Party Hardware part, a Content Center part, and a part owned
    /// by another project entirely. Every one is unnumbered and modifiable, so nothing but the scope
    /// boundary stops the tool renaming it, and each was renameable before this guard.
    /// </summary>
    [Theory]
    [InlineData(@"C:\WMP\P124 GRM\3rd Party Hardware\M6 bolt.ipt", "M6 bolt.ipt")]
    [InlineData(@"C:\Other\Rig\shared jig.ipt", "shared jig.ipt")]
    [InlineData(@"C:\WMP\P124 GRM\Content Center Files\ANSI washer.ipt", "ANSI washer.ipt")]
    public void AReferencedFileOutsideTheProjectScopeIsNeverProposedOrPlanned(string outsidePath, string outsideName)
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string siblingPath = Path.Combine(ProjectRoot, "Bracket.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(outsidePath, DocumentKind.Part, parents: [rootPath]),
                Doc(siblingPath, DocumentKind.Part, parents: [rootPath]),
            ]);

        // The enumerated scope is the project root minus the reserved folders, exactly as
        // PhysicalNamingFileSystem produces it: none of the three outside files appear in it.
        fileSystem.SetScope(ProjectRoot, [rootPath, siblingPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);

        NamingAnalysisRow outsideRow = Assert.Single(analysis.Rows, row => row.FullPath == outsidePath);
        Assert.Null(outsideRow.ProposedFileName);
        Assert.Equal(RenameAction.None, outsideRow.Action);
        Assert.Equal([ExpectedReason(outsideName)], outsideRow.Reasons);

        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.DoesNotContain(plan.Operations, op => op.CurrentFullPath == outsidePath);
        Assert.DoesNotContain(plan.VaultInstructions, v => v.CurrentFullPath == outsidePath);

        // The outside file must not hold up the rows the project does own.
        Assert.Empty(plan.Blockers);
        RenameOperation operation = Assert.Single(plan.Operations);
        Assert.Equal(siblingPath, operation.CurrentFullPath);
        Assert.Equal("124-0001 Bracket.ipt", Path.GetFileName(operation.NewFullPath));
    }

    /// <summary>
    /// An in-scope file in an ordinary sub-folder is not caught by the boundary: the guard excludes the
    /// reserved folder names and anything outside the root, nothing else.
    /// </summary>
    [Fact]
    public void AFileInAnOrdinarySubFolderOfTheProjectRootStaysInScope()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "124-A001 Root (main assembly).iam");
        string nestedPath = Path.Combine(ProjectRoot, "Parts", "Sub", "Bracket.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(nestedPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, nestedPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Equal("124-0001 Bracket.ipt", Assert.Single(analysis.Rows, r => r.FullPath == nestedPath).ProposedFileName);
        Assert.Contains(plan.Operations, op => op.CurrentFullPath == nestedPath);
    }

    /// <summary>
    /// Defense in depth at the archival step. ComputeArchivedPath uses Path.GetRelativePath, which yields
    /// '..\..\' segments for an original outside the project root; moving by that path would drop the
    /// original somewhere outside _renamed-originals entirely. The plan is constructed directly, because
    /// this guard has to hold whatever produced the plan.
    /// </summary>
    [Fact]
    public void ExecuteRefusesToArchiveAnOriginalOutsideTheProjectRoot()
    {
        (FileNamingWorkflow workflow, _, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        const string OutsidePath = @"C:\Other\Rig\shared jig.ipt";
        const string NewOutsidePath = @"C:\Other\Rig\124-0001 shared jig.ipt";
        string insidePath = Path.Combine(ProjectRoot, "Bracket.ipt");
        string newInsidePath = Path.Combine(ProjectRoot, "124-0002 Bracket.ipt");

        RenamePlan plan = new(
            ProjectRoot,
            [
                new RenameOperation(OutsidePath, NewOutsidePath, DocumentKind.Part, [], [], null),
                new RenameOperation(insidePath, newInsidePath, DocumentKind.Part, [], [], null),
            ],
            [],
            [],
            []);

        RenameExecution execution = workflow.Execute(plan);

        ArchiveFailure failure = Assert.Single(execution.ArchiveFailures);
        Assert.Equal(OutsidePath, failure.OriginalPath);
        Assert.Equal(
            $"'{OutsidePath}' is outside the project root '{ProjectRoot}', so archiving it would move it "
            + "outside '_renamed-originals'; the rename stands and the original was left in place.",
            failure.ErrorMessage);

        Assert.DoesNotContain(fileSystem.MoveCalls, move => move.FullPath == OutsidePath);
        Assert.DoesNotContain(fileSystem.MoveCalls, move => move.OriginalsRoot.Contains("..", StringComparison.Ordinal));
        Assert.DoesNotContain(fileSystem.MoveCalls, move => move.FullPath.Contains("..", StringComparison.Ordinal));

        // The in-root original is unaffected: refusal is per-item, exactly like a failed move.
        Assert.Contains(fileSystem.MoveCalls, move => move.FullPath == insidePath);
        Assert.False(execution.OriginalsMoved);

        RenameManifestEntry outsideEntry = Assert.Single(
            execution.Manifest!.Entries,
            entry => entry.OriginalPath == OutsidePath);
        Assert.False(outsideEntry.Archived);
        Assert.Equal(failure.ErrorMessage, outsideEntry.Error);
    }
}
