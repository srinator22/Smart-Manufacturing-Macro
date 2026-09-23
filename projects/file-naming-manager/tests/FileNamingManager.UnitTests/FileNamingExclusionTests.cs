// Purpose: Pin the per-row include/exclude contract on FileNamingWorkflow.Plan - an excluded path is
//   skipped before allocation, so it produces no operation, no Vault instruction, and no blocker, and it
//   never consumes a number the next row would otherwise have been given.
// Inputs: Synthetic ActiveAssemblySnapshot fixtures over the fake gateway, file system, and clock.
// Outputs: Assertions on RenamePlan.Operations, RenamePlan.VaultInstructions, and the allocated numbers.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core, and this project's fakes.
// Assumptions: RenameOptions.ExcludedPaths is compared case-insensitively, because the paths come from a
//   view model that got them from Inventor and Windows paths differ only in case.
// Validation source: docs/TEST_PLAN.md section 2 (include/exclude cases).

using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public class FileNamingExclusionTests
{
    private const string ProjectRoot = @"C:\WMP\P124 GRM";
    private static readonly ProjectNumber Project124 = new(124);

    private static DocumentSnapshot Doc(
        string fullPath,
        DocumentKind kind,
        bool isRoot = false,
        string[]? parents = null) =>
        new(fullPath, kind, isRoot, true, false, false, parents ?? [], [], null);

    private static (FileNamingWorkflow Workflow, FakeNamingFileSystem FileSystem, NamingAnalysis Analysis)
        BuildTwoUnnumberedParts(params string[] vaultManagedPaths)
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string alphaPath = Path.Combine(ProjectRoot, "Alpha Bracket.ipt");
        string bravoPath = Path.Combine(ProjectRoot, "Bravo Bracket.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(alphaPath, DocumentKind.Part, parents: [rootPath]),
                Doc(bravoPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, alphaPath, bravoPath]);
        foreach (string managed in vaultManagedPaths)
        {
            fileSystem.MarkVaultManaged(managed);
        }

        return (workflow, fileSystem, workflow.Analyze(Project124));
    }

    [Fact]
    public void DefaultRenameOptionsExcludeNothing() =>
        Assert.Empty(new RenameOptions().ExcludedPaths);

    /// <summary>
    /// The number matters as much as the operation: an excluded row that still allocated would leave the
    /// next row on 0002 and manufacture a gap at 0001 that the report would then complain about.
    /// </summary>
    [Fact]
    public void AnExcludedUnmanagedRowProducesNoOperationAndConsumesNoNumber()
    {
        (FileNamingWorkflow workflow, _, NamingAnalysis analysis) = BuildTwoUnnumberedParts();
        string alphaPath = Path.Combine(ProjectRoot, "Alpha Bracket.ipt");
        string bravoPath = Path.Combine(ProjectRoot, "Bravo Bracket.ipt");

        RenamePlan included = workflow.Plan(analysis, Project124, new RenameOptions());
        Assert.Equal(
            "124-0001 Alpha Bracket.ipt",
            Path.GetFileName(Assert.Single(included.Operations, op => op.CurrentFullPath == alphaPath).NewFullPath));
        Assert.Equal(
            "124-0002 Bravo Bracket.ipt",
            Path.GetFileName(Assert.Single(included.Operations, op => op.CurrentFullPath == bravoPath).NewFullPath));

        RenamePlan excluded = workflow.Plan(
            analysis,
            Project124,
            new RenameOptions { ExcludedPaths = [alphaPath.ToUpperInvariant()] });

        RenameOperation operation = Assert.Single(excluded.Operations);
        Assert.Equal(bravoPath, operation.CurrentFullPath);
        Assert.Equal("124-0001 Bravo Bracket.ipt", Path.GetFileName(operation.NewFullPath));
        Assert.Empty(excluded.Blockers);
    }

    [Fact]
    public void AnExcludedManagedRowProducesNoVaultInstruction()
    {
        string alphaPath = Path.Combine(ProjectRoot, "Alpha Bracket.ipt");
        string bravoPath = Path.Combine(ProjectRoot, "Bravo Bracket.ipt");
        (FileNamingWorkflow workflow, _, NamingAnalysis analysis) = BuildTwoUnnumberedParts(alphaPath, bravoPath);

        RenamePlan included = workflow.Plan(analysis, Project124, new RenameOptions());
        Assert.Equal(2, included.VaultInstructions.Count);

        RenamePlan excluded = workflow.Plan(
            analysis,
            Project124,
            new RenameOptions { ExcludedPaths = [alphaPath] });

        VaultRenameInstruction instruction = Assert.Single(excluded.VaultInstructions);
        Assert.Equal(bravoPath, instruction.CurrentFullPath);
        Assert.Equal("124-0001 Bravo Bracket.ipt", instruction.ProposedFileName);
        Assert.Empty(excluded.Operations);
        Assert.Empty(excluded.Blockers);
    }

    /// <summary>
    /// An excluded row is not going to be touched, so nothing about it can stop the run: the read-only
    /// companion drawing that would otherwise block its model's rename must stop blocking once the
    /// operator takes that model out of the plan.
    /// </summary>
    [Fact]
    public void AnExcludedRowRaisesNoBlockerOfItsOwn()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Alpha Bracket.ipt");
        string drawingPath = Path.Combine(ProjectRoot, "Alpha Bracket.idw");

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

        Assert.NotEmpty(workflow.Plan(analysis, Project124, new RenameOptions()).Blockers);

        RenamePlan excluded = workflow.Plan(
            analysis,
            Project124,
            new RenameOptions { ExcludedPaths = [partPath] });

        Assert.Empty(excluded.Blockers);
        Assert.Empty(excluded.Operations);
    }
}
