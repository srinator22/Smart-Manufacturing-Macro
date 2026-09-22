// Purpose: Pin what Execute does when the model's own rename succeeds but a later step of the same
//   operation fails - the companion drawing rename, or the Part Number property write.
// Inputs: RenamePlans constructed directly, with failures injected into the fake gateway.
// Outputs: Assertions on RenameItemResult.ModelRenamed/Succeeded/ErrorMessage, which parents were
//   saved, and exactly which originals reached the manifest and the archive.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core, and this project's fakes.
// Assumptions: A rename that has already happened on disk is a fact, not something a later failure can
//   take back. Reporting the item as a plain failure left the model renamed but untracked: its parent
//   was never saved (so the assembly still pointed at the old name) and its original was never archived.
// Validation source: PR review finding T2 on the shipped File Naming Manager.

using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public class FileNamingPartialRenameTests
{
    private const string ProjectRoot = @"C:\P";
    private const string PartPath = @"C:\P\Widget.ipt";
    private const string NewPartPath = @"C:\P\124-0001 Widget.ipt";
    private const string ParentPath = @"C:\P\Root.iam";

    private static (FileNamingWorkflow Workflow, FakeInventorNamingGateway Gateway, FakeNamingFileSystem FileSystem)
        CreateWorkflow()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());
        return (workflow, gateway, fileSystem);
    }

    [Fact]
    public void AFailedPartNumberWriteLeavesTheModelRenameTrackedAsAPartialSuccess()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        RenameOperation operation = new(PartPath, NewPartPath, DocumentKind.Part, [ParentPath], [], "124-0001");
        RenamePlan plan = new(ProjectRoot, [operation], [], [], [ParentPath]);

        gateway.FailSetPartNumberFor(NewPartPath, new InvalidOperationException("the property is read-only"));

        RenameExecution execution = workflow.Execute(plan);

        RenameItemResult item = Assert.Single(execution.Items);
        Assert.True(item.ModelRenamed);
        Assert.False(item.Succeeded);
        Assert.Equal(
            "Model renamed; Part Number write failed: the property is read-only",
            item.ErrorMessage);

        Assert.Contains(ParentPath, gateway.SaveCalls);
        Assert.Contains(execution.Manifest!.Entries, entry => entry.OriginalPath == PartPath);
        Assert.Contains(fileSystem.MoveCalls, move => move.FullPath == PartPath);
    }

    [Fact]
    public void AFailedCompanionRenameLeavesTheModelRenameTrackedAndTheCompanionUnarchived()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        const string DrawingPath = @"C:\P\Widget.idw";
        const string NewDrawingPath = @"C:\P\124-0001 Widget.idw";

        RenameOperation operation = new(
            PartPath,
            NewPartPath,
            DocumentKind.Part,
            [ParentPath],
            [(DrawingPath, NewDrawingPath)],
            null);
        RenamePlan plan = new(ProjectRoot, [operation], [], [], [ParentPath]);

        gateway.FailRenameFor(DrawingPath, new InvalidOperationException("the drawing is checked out"));

        RenameExecution execution = workflow.Execute(plan);

        RenameItemResult item = Assert.Single(execution.Items);
        Assert.True(item.ModelRenamed);
        Assert.False(item.Succeeded);
        Assert.Equal(
            "Model renamed; companion drawing 'Widget.idw' failed: the drawing is checked out",
            item.ErrorMessage);

        Assert.Contains(ParentPath, gateway.SaveCalls);
        Assert.Contains(execution.Manifest!.Entries, entry => entry.OriginalPath == PartPath);
        Assert.Contains(fileSystem.MoveCalls, move => move.FullPath == PartPath);

        // The drawing was never renamed, so its original is not an original of anything: archiving it
        // would move a live file out from under the drawing that still carries that name.
        Assert.DoesNotContain(execution.Manifest.Entries, entry => entry.OriginalPath == DrawingPath);
        Assert.DoesNotContain(fileSystem.MoveCalls, move => move.FullPath == DrawingPath);
    }

    /// <summary>
    /// Companions are recorded one at a time, as each rename lands. A drawing that was renamed before a
    /// later companion failed is on disk under its new name, so its original must still be archived.
    /// </summary>
    [Fact]
    public void CompanionsRenamedBeforeTheFailureAreStillRecorded()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        const string FirstDrawing = @"C:\P\Widget.idw";
        const string NewFirstDrawing = @"C:\P\124-0001 Widget.idw";
        const string SecondDrawing = @"C:\P\Widget.dwg";
        const string NewSecondDrawing = @"C:\P\124-0001 Widget.dwg";

        RenameOperation operation = new(
            PartPath,
            NewPartPath,
            DocumentKind.Part,
            [ParentPath],
            [(FirstDrawing, NewFirstDrawing), (SecondDrawing, NewSecondDrawing)],
            null);
        RenamePlan plan = new(ProjectRoot, [operation], [], [], [ParentPath]);

        gateway.FailRenameFor(SecondDrawing, new InvalidOperationException("the drawing is checked out"));

        RenameExecution execution = workflow.Execute(plan);

        RenameItemResult item = Assert.Single(execution.Items);
        Assert.True(item.ModelRenamed);
        Assert.False(item.Succeeded);
        Assert.Equal(
            "Model renamed; companion drawing 'Widget.dwg' failed: the drawing is checked out",
            item.ErrorMessage);

        Assert.Contains(execution.Manifest!.Entries, entry => entry.OriginalPath == FirstDrawing);
        Assert.DoesNotContain(execution.Manifest.Entries, entry => entry.OriginalPath == SecondDrawing);
        Assert.Contains(fileSystem.MoveCalls, move => move.FullPath == FirstDrawing);
        Assert.DoesNotContain(fileSystem.MoveCalls, move => move.FullPath == SecondDrawing);
    }

    /// <summary>
    /// A Part Number write is not attempted once a companion has already failed: the operation is
    /// already partial, and the message must name the first thing that went wrong, not the last.
    /// </summary>
    [Fact]
    public void ThePartNumberWriteIsSkippedOnceACompanionHasFailed()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _) = CreateWorkflow();

        const string DrawingPath = @"C:\P\Widget.idw";
        const string NewDrawingPath = @"C:\P\124-0001 Widget.idw";

        RenameOperation operation = new(
            PartPath,
            NewPartPath,
            DocumentKind.Part,
            [],
            [(DrawingPath, NewDrawingPath)],
            "124-0001");
        RenamePlan plan = new(ProjectRoot, [operation], [], [], []);

        gateway.FailRenameFor(DrawingPath, new InvalidOperationException("the drawing is checked out"));

        RenameExecution execution = workflow.Execute(plan);

        Assert.Contains("companion drawing", Assert.Single(execution.Items).ErrorMessage);
        Assert.Empty(gateway.SetPartNumberCalls);
    }

    /// <summary>
    /// The complement: when the model's own rename never happened, nothing about it is tracked. A failed
    /// model rename and a pre-open failure both leave ModelRenamed false, so no parent is saved on its
    /// behalf and no original of it is archived.
    /// </summary>
    [Fact]
    public void AModelWhoseOwnRenameFailedIsNotTrackedAsRenamed()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, FakeNamingFileSystem fileSystem) = CreateWorkflow();

        RenameOperation operation = new(PartPath, NewPartPath, DocumentKind.Part, [ParentPath], [], null);
        RenamePlan plan = new(ProjectRoot, [operation], [], [], [ParentPath]);

        gateway.FailRenameFor(PartPath, new InvalidOperationException("locked by another process"));

        RenameExecution execution = workflow.Execute(plan);

        RenameItemResult item = Assert.Single(execution.Items);
        Assert.False(item.ModelRenamed);
        Assert.False(item.Succeeded);
        Assert.Equal("locked by another process", item.ErrorMessage);
        Assert.Empty(gateway.SaveCalls);
        Assert.Empty(fileSystem.MoveCalls);
    }

    [Fact]
    public void AnOperationSkippedByAPreOpenFailureIsNotTrackedAsRenamed()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _) = CreateWorkflow();

        const string DrawingPath = @"C:\P\Widget.idw";
        const string NewDrawingPath = @"C:\P\124-0001 Widget.idw";

        RenameOperation operation = new(
            PartPath,
            NewPartPath,
            DocumentKind.Part,
            [ParentPath],
            [(DrawingPath, NewDrawingPath)],
            null);
        RenamePlan plan = new(ProjectRoot, [operation], [], [], [ParentPath]);

        gateway.FailEnsureOpenFor(DrawingPath, new InvalidOperationException("the drawing is checked out"));

        RenameExecution execution = workflow.Execute(plan);

        RenameItemResult item = Assert.Single(execution.Items);
        Assert.False(item.ModelRenamed);
        Assert.False(item.Succeeded);
    }

    [Fact]
    public void AFullySuccessfulOperationIsBothRenamedAndSucceeded()
    {
        (FileNamingWorkflow workflow, _, _) = CreateWorkflow();

        RenameOperation operation = new(PartPath, NewPartPath, DocumentKind.Part, [ParentPath], [], "124-0001");
        RenamePlan plan = new(ProjectRoot, [operation], [], [], [ParentPath]);

        RenameItemResult item = Assert.Single(workflow.Execute(plan).Items);

        Assert.True(item.ModelRenamed);
        Assert.True(item.Succeeded);
        Assert.Null(item.ErrorMessage);
    }
}
