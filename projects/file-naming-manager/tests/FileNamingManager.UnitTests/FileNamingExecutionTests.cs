// Purpose: Pin FileNamingWorkflow.Execute's post-rename behaviour - parent saves resolved through the
//   rename map, and per-item isolated archival around a manifest written before and after the moves.
// Inputs: RenamePlans constructed directly, plus the fake gateway and fake file system test doubles.
// Outputs: Assertions on which paths are saved, which originals are archived, and what the manifest holds.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core, and this project's fakes.
// Assumptions: Execute's contract depends only on the plan's shape, so these tests build plans directly
//   rather than driving Analyze->Plan; RenamePlan.ParentSaveOrder deliberately holds PRE-rename paths,
//   exactly as FileNamingWorkflow.Plan produces them, because that is the input that exposed D7 live.
// Validation source: .work/TASK.md progress log 2026-09-23T03:45Z (D7) and the review verdict item R2.

using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public class FileNamingExecutionTests
{
    private const string ProjectRoot = @"C:\P";
    private const string PartPath = @"C:\P\Widget.ipt";
    private const string NewPartPath = @"C:\P\124-0001 Widget.ipt";
    private const string SubPath = @"C:\P\Sub.iam";
    private const string NewSubPath = @"C:\P\124-A002 Sub (sub-assembly).iam";
    private const string RootPath = @"C:\P\Root.iam";
    private const string NewRootPath = @"C:\P\124-A001 Root (main assembly).iam";

    private static (FileNamingWorkflow Workflow, FakeInventorNamingGateway Gateway, FakeNamingFileSystem FileSystem)
        CreateWorkflow()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());
        return (workflow, gateway, fileSystem);
    }

    /// <summary>
    /// Three-level plan in which the sub-assembly and the root are themselves renamed. ParentSaveOrder
    /// holds their PRE-rename paths, but after a successful SaveAs neither document is open under that
    /// name any more, so saving by the old path throws "not open in this session" (D7, seen live on
    /// 2026-09-23). Each parent must be resolved through the map of renames that actually succeeded.
    /// </summary>
    private static RenamePlan BuildThreeLevelPlan() =>
        new(
            ProjectRoot,
            [
                new RenameOperation(PartPath, NewPartPath, DocumentKind.Part, [SubPath], [], null),
                new RenameOperation(SubPath, NewSubPath, DocumentKind.Assembly, [RootPath], [], null),
                new RenameOperation(RootPath, NewRootPath, DocumentKind.Assembly, [], [], null),
            ],
            [],
            [],
            [SubPath, RootPath]);

    [Fact]
    public void ExecuteSavesRenamedParentsUnderTheirNewPaths()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _) = CreateWorkflow();

        RenameExecution execution = workflow.Execute(BuildThreeLevelPlan());

        Assert.All(execution.Items, item => Assert.True(item.Succeeded, item.ErrorMessage));
        Assert.Empty(execution.ParentSaveFailures);
        Assert.Equal([NewSubPath, NewRootPath], gateway.SaveCalls);
        Assert.DoesNotContain(SubPath, gateway.SaveCalls);
        Assert.DoesNotContain(RootPath, gateway.SaveCalls);
        Assert.True(execution.OriginalsMoved);
    }

    [Fact]
    public void ExecuteSavesAParentThatFailedItsOwnRenameUnderItsOldPath()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _) = CreateWorkflow();

        gateway.FailRenameFor(SubPath, new InvalidOperationException("sub-assembly is checked out"));

        RenameExecution execution = workflow.Execute(BuildThreeLevelPlan());

        RenameItemResult subResult = Assert.Single(execution.Items, item => item.CurrentFullPath == SubPath);
        Assert.False(subResult.Succeeded);

        // The part's rename succeeded, so its parent - the sub-assembly, still open under its OLD name
        // because its own rename failed - must be saved at that old path.
        Assert.Contains(SubPath, gateway.SaveCalls);
        Assert.DoesNotContain(NewSubPath, gateway.SaveCalls);
        Assert.Empty(execution.ParentSaveFailures);
    }

    /// <summary>
    /// Archival is per-item isolated and the manifest is written before the first move, not after the
    /// last one. A mid-loop failure used to leave the originals half archived with no manifest at all and
    /// let the exception escape into the Inventor command callback (review verdict R2). The manifest is
    /// written twice: once up front with every planned entry, once afterwards carrying each outcome.
    /// </summary>
    [Fact]
    public void ExecuteIsolatesEachArchiveMoveAndRecordsTheOutcomeInTheManifest()
    {
        (FileNamingWorkflow workflow, _, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        RenameOperation op1 = new(@"C:\P\Part1.ipt", @"C:\P\124-0001 Part1.ipt", DocumentKind.Part, [], [], null);
        RenameOperation op2 = new(@"C:\P\Part2.ipt", @"C:\P\124-0002 Part2.ipt", DocumentKind.Part, [], [], null);
        RenameOperation op3 = new(@"C:\P\Part3.ipt", @"C:\P\124-0003 Part3.ipt", DocumentKind.Part, [], [], null);
        RenamePlan plan = new(ProjectRoot, [op1, op2, op3], [], [], []);

        fileSystem.FailMoveFor(op2.CurrentFullPath, new IOException("the original is locked by another process"));

        RenameExecution execution = workflow.Execute(plan);

        Assert.Contains(fileSystem.MoveCalls, move => move.FullPath == op1.CurrentFullPath);
        Assert.DoesNotContain(fileSystem.MoveCalls, move => move.FullPath == op2.CurrentFullPath);
        Assert.Contains(fileSystem.MoveCalls, move => move.FullPath == op3.CurrentFullPath);

        ArchiveFailure failure = Assert.Single(execution.ArchiveFailures);
        Assert.Equal(op2.CurrentFullPath, failure.OriginalPath);
        Assert.Contains("locked by another process", failure.ErrorMessage);
        Assert.False(execution.OriginalsMoved);

        Assert.NotNull(execution.Manifest);
        Assert.Equal(3, execution.Manifest!.Entries.Count);
        Assert.Equal([true, false, true], execution.Manifest.Entries.Select(entry => entry.Archived));
        Assert.Null(execution.Manifest.Entries[0].Error);
        Assert.Contains("locked by another process", execution.Manifest.Entries[1].Error);

        Assert.Equal(2, fileSystem.WrittenManifests.Count);
        Assert.Equal(3, fileSystem.WrittenManifests[0].Entries.Count);
        Assert.All(fileSystem.WrittenManifests[0].Entries, entry => Assert.False(entry.Archived));
        Assert.All(fileSystem.WrittenManifests[0].Entries, entry => Assert.Null(entry.Error));
    }

    [Fact]
    public void ExecuteReportsOriginalsMovedOnlyWhenEveryPlannedMoveSucceeded()
    {
        (FileNamingWorkflow workflow, _, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        RenameOperation op1 = new(@"C:\P\Part1.ipt", @"C:\P\124-0001 Part1.ipt", DocumentKind.Part, [], [], null);
        RenamePlan plan = new(ProjectRoot, [op1], [], [], []);

        RenameExecution execution = workflow.Execute(plan);

        Assert.True(execution.OriginalsMoved);
        Assert.Empty(execution.ArchiveFailures);
        Assert.All(execution.Manifest!.Entries, entry => Assert.True(entry.Archived));
    }
}
