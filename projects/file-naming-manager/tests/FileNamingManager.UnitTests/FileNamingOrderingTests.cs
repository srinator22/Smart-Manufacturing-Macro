// Purpose: Pin the leaf-first ordering of rename operations and of the parent saves that follow them.
// Inputs: Synthetic assembly trees with known depths, including a diamond, an orphan, and a reference
//   to a document that is not part of the snapshot.
// Outputs: Assertions on the exact order of RenamePlan.Operations and RenamePlan.ParentSaveOrder.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core, and this project's fakes.
// Assumptions: Ordering is the safety property here - a parent renamed before its child leaves the child
//   unreferenced - so these tests assert whole sequences, never just membership.
// Validation source: .work/TASK.md "Vault safety model" (leaf documents first, then sub-assemblies, then
//   the root; parents saved afterwards).

using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public class FileNamingOrderingTests
{
    private const string ProjectRoot = @"C:\WMP\P124 GRM";
    private static readonly ProjectNumber Project124 = new(124);

    private static DocumentSnapshot Doc(string fullPath, DocumentKind kind, bool isRoot = false, string[]? parents = null) =>
        new(fullPath, kind, isRoot, true, false, false, parents ?? [], [], null);

    private static (FileNamingWorkflow Workflow, FakeInventorNamingGateway Gateway, FakeNamingFileSystem FileSystem)
        CreateWorkflow()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());
        return (workflow, gateway, fileSystem);
    }

    /// <summary>
    /// Parts first, then sub-assemblies, then the root; within a group the deepest document first; and
    /// only then alphabetically, so the order is fully determined and does not vary with the order
    /// Inventor happened to enumerate the assembly in. "C Part.ipt" hangs off both the root and the
    /// sub-assembly, so its depth is the deeper of the two - taking the shallower one would let it be
    /// renamed after a parent that still references it.
    /// </summary>
    [Fact]
    public void OperationsRunPartsFirstThenDeepestFirstThenAlphabetically()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string subPath = Path.Combine(ProjectRoot, "Sub.iam");
        string subSubPath = Path.Combine(ProjectRoot, "SubSub.iam");
        string partAPath = Path.Combine(ProjectRoot, "A Part.ipt");
        string partBPath = Path.Combine(ProjectRoot, "B Part.ipt");
        string partCPath = Path.Combine(ProjectRoot, "C Part.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(subPath, DocumentKind.Assembly, parents: [rootPath]),
                Doc(subSubPath, DocumentKind.Assembly, parents: [subPath]),
                Doc(partAPath, DocumentKind.Part, parents: [rootPath]),
                Doc(partBPath, DocumentKind.Part, parents: [rootPath]),
                Doc(partCPath, DocumentKind.Part, parents: [rootPath, subPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, subPath, subSubPath, partAPath, partBPath, partCPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Equal(
            [partCPath, partAPath, partBPath, subSubPath, subPath, rootPath],
            plan.Operations.Select(op => op.CurrentFullPath));
    }

    /// <summary>
    /// Parents are saved deepest first for the same reason renames run leaf first, and ties break
    /// alphabetically so the sequence is deterministic.
    /// </summary>
    [Fact]
    public void ParentSaveOrderRunsDeepestFirstThenAlphabetically()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string alphaPath = Path.Combine(ProjectRoot, "Alpha.iam");
        string betaPath = Path.Combine(ProjectRoot, "Beta.iam");
        string widgetAPath = Path.Combine(ProjectRoot, "Widget A.ipt");
        string widgetBPath = Path.Combine(ProjectRoot, "Widget B.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(alphaPath, DocumentKind.Assembly, parents: [rootPath]),
                Doc(betaPath, DocumentKind.Assembly, parents: [rootPath]),
                Doc(widgetAPath, DocumentKind.Part, parents: [alphaPath]),
                Doc(widgetBPath, DocumentKind.Part, parents: [betaPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, alphaPath, betaPath, widgetAPath, widgetBPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Equal([alphaPath, betaPath, rootPath], plan.ParentSaveOrder);
    }

    /// <summary>
    /// Depth computation has to survive the two shapes a real session produces that a clean tree does
    /// not: a document with no parent at all (referenced only by a drawing, or dragged in and not yet
    /// placed) and a document whose parent is open but outside this snapshot. Neither may throw.
    /// </summary>
    [Fact]
    public void DepthComputationHandlesAnOrphanAndAParentOutsideTheSnapshot()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        string rootPath = Path.Combine(ProjectRoot, "Root.iam");
        string orphanPath = Path.Combine(ProjectRoot, "Orphan.ipt");
        string ghostChildPath = Path.Combine(ProjectRoot, "Ghost Child.ipt");
        string ghostParentPath = Path.Combine(ProjectRoot, "Ghost.iam");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(orphanPath, DocumentKind.Part),
                Doc(ghostChildPath, DocumentKind.Part, parents: [ghostParentPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, orphanPath, ghostChildPath]);

        NamingAnalysis analysis = workflow.Analyze(Project124);
        RenamePlan plan = workflow.Plan(analysis, Project124, new RenameOptions());

        Assert.Equal([ghostChildPath, orphanPath, rootPath], plan.Operations.Select(op => op.CurrentFullPath));

        // A parent outside the snapshot is still saved: Execute isolates the attempt, and the gateway
        // reports precisely which document it could not save rather than the plan guessing.
        Assert.Equal([ghostParentPath], plan.ParentSaveOrder);
    }
}
