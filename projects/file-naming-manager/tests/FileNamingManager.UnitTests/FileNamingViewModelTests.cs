// Purpose: Verify FileNamingViewModel's prefill, validation, re-planning, Apply, and Vault-plan-text
//   behaviour against real FileNamingWorkflow.Analyze/Plan/Execute calls over fake ports.
// Inputs: Synthetic ActiveAssemblySnapshot fixtures built the same way FileNamingWorkflowTests builds them.
// Outputs: Assertions on view-model properties and their change notifications.
// Dependencies: FileNamingManager.UI, FileNamingManager.Application, FileNamingManager.Core, and this
//   project's fakes.
// Assumptions: The view model re-plans against the NamingAnalysis passed at construction, never re-running
//   Analyze() itself; fixtures therefore fix the project number suggestion (from the root file name) once.
// Validation source: .work/TASK.md UI acceptance criterion; worker brief "Tests" section.

using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.UI;
using FileNamingManager.UnitTests.Fakes;

namespace FileNamingManager.UnitTests;

public class FileNamingViewModelTests
{
    private const string ProjectRoot = @"C:\WMP\P124 GRM";

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

    private static (FileNamingWorkflow Workflow, FakeInventorNamingGateway Gateway, FakeNamingFileSystem FileSystem, NamingAnalysis Analysis)
        BuildFixture(bool includeVaultManagedPart = true, bool rootDirty = false)
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string unnumberedPartPath = Path.Combine(ProjectRoot, "Bracket.ipt");
        string vaultPartPath = Path.Combine(ProjectRoot, "Widget.ipt");

        List<DocumentSnapshot> docs =
        [
            Doc(rootPath, DocumentKind.Assembly, isRoot: true, isDirty: rootDirty),
            Doc(unnumberedPartPath, DocumentKind.Part, parents: [rootPath]),
        ];
        List<string> scope = [rootPath, unnumberedPartPath];

        if (includeVaultManagedPart)
        {
            docs.Add(Doc(vaultPartPath, DocumentKind.Part, parents: [rootPath]));
            scope.Add(vaultPartPath);
        }

        gateway.Snapshot = new ActiveAssemblySnapshot(rootPath, rootDirty, false, docs);
        fileSystem.SetScope(ProjectRoot, scope);
        if (includeVaultManagedPart)
        {
            fileSystem.MarkVaultManaged(vaultPartPath);
        }

        NamingAnalysis analysis = workflow.Analyze(null);
        return (workflow, gateway, fileSystem, analysis);
    }

    /// <summary>
    /// A dedicated fixture carrying one MalformedWhitespace part (already-numbered, irregular spacing) so
    /// tests can toggle NormalizeMalformed and observe the row react. Not vault-managed, so the excluded
    /// row resolves to a plain Rename once the option is on.
    /// </summary>
    private static (FileNamingWorkflow Workflow, NamingAnalysis Analysis) BuildMalformedWhitespaceFixture()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string malformedPartPath = Path.Combine(ProjectRoot, "124-0002  Bad Part.ipt");

        List<DocumentSnapshot> docs =
        [
            Doc(rootPath, DocumentKind.Assembly, isRoot: true),
            Doc(malformedPartPath, DocumentKind.Part, parents: [rootPath]),
        ];
        List<string> scope = [rootPath, malformedPartPath];

        gateway.Snapshot = new ActiveAssemblySnapshot(rootPath, false, false, docs);
        fileSystem.SetScope(ProjectRoot, scope);

        NamingAnalysis analysis = workflow.Analyze(null);
        return (workflow, analysis);
    }

    private static NamingRowViewModel FindRowByState(FileNamingViewModel viewModel, NameState state)
    {
        foreach (NamingRowViewModel row in viewModel.Rows)
        {
            if (row.State == state.ToString())
            {
                return row;
            }
        }

        throw new InvalidOperationException($"No row found with State '{state}'.");
    }

    [Fact]
    public void RowsReflectTheCurrentPlanNotTheAnalysisPreview()
    {
        (FileNamingWorkflow workflow, NamingAnalysis analysis) = BuildMalformedWhitespaceFixture();
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        NamingRowViewModel row = FindRowByState(viewModel, NameState.MalformedWhitespace);

        Assert.Equal("None", row.Action);
        Assert.Equal(string.Empty, row.ProposedFileName);
        Assert.Contains(
            "Excluded: 'Normalize malformed numbered names' is off.",
            row.Reasons,
            StringComparison.Ordinal);

        viewModel.NormalizeMalformed = true;

        Assert.True(
            row.Action is "Rename" or "VaultRename",
            $"Expected Rename or VaultRename once NormalizeMalformed is on, got '{row.Action}'.");
        Assert.NotEqual(string.Empty, row.ProposedFileName);
        Assert.DoesNotContain("Excluded:", row.Reasons, StringComparison.Ordinal);
    }

    /// <summary>
    /// A row excluded by the external-parent guard (AddExternalParentBlockers) ends up in neither
    /// Operations nor VaultInstructions, so it must not sit blank in the grid: it needs its own reason,
    /// because the plan-level Blockers text is not something the row can be assumed to surface (Residual 1).
    /// </summary>
    [Fact]
    public void RowsShowTheExternalParentReasonWhenExcludedByTheExternalParentGuard()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Bracket.ipt");
        string externalParentPath = Path.Combine(ProjectRoot, "OtherAssembly.iam");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(partPath, DocumentKind.Part, parents: [rootPath], externalParents: [externalParentPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath]);

        NamingAnalysis analysis = workflow.Analyze(null);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        NamingRowViewModel row = Assert.Single(viewModel.Rows, r => r.CurrentFileName == "Bracket.ipt");

        Assert.Equal("None", row.Action);
        Assert.Equal(string.Empty, row.ProposedFileName);
        Assert.Contains(
            "Blocked: referenced by 'OtherAssembly.iam' outside the active assembly.",
            row.Reasons,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RowsShowNoActionWhenTheProjectNumberIsInvalid()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture();
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        viewModel.ProjectNumberText = "12";

        Assert.NotEmpty(viewModel.Rows);
        Assert.All(viewModel.Rows, row =>
        {
            Assert.Equal("None", row.Action);
            Assert.Equal("Enter a valid project number (100-999).", row.Reasons);
            Assert.Equal(string.Empty, row.ProposedFileName);
        });
    }

    [Fact]
    public void ConstructorThrowsForFailedAnalysis()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());
        NamingAnalysis failedAnalysis = workflow.Analyze(null);

        Assert.False(failedAnalysis.IsSuccess);
        Assert.Throws<ArgumentException>(() => new FileNamingViewModel(workflow, failedAnalysis, applyMode: false));
    }

    [Fact]
    public void PrefillsProjectNumberFromRootFileNameSuggestion()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture();

        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        Assert.Equal("124", viewModel.ProjectNumberText);
        Assert.Equal("Suggested from the root file name", viewModel.ProjectNumberSuggestionText);
        Assert.Null(viewModel.ProjectNumberError);
    }

    [Fact]
    public void InvalidProjectNumberGivesErrorAndNullPlanAndDisablesApplyWithReason()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture();
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        viewModel.ProjectNumberText = "12";

        Assert.NotNull(viewModel.ProjectNumberError);
        Assert.Null(viewModel.Plan);
        Assert.False(viewModel.CanApply);
        Assert.Equal(viewModel.ProjectNumberError, viewModel.ApplyDisabledReason);
    }

    [Fact]
    public void ValidProjectNumberPlansAndExposesCounts()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture(includeVaultManagedPart: true);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        Assert.NotNull(viewModel.Plan);
        Assert.Equal(1, viewModel.OperationCount);
        Assert.Equal(1, viewModel.VaultInstructionCount);
        Assert.True(viewModel.HasVaultInstructions);
        Assert.True(viewModel.CanApply);
        Assert.Equal(string.Empty, viewModel.ApplyDisabledReason);
    }

    [Fact]
    public void TogglingRenameUnnumberedReplansOperationCount()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        Assert.Equal(1, viewModel.OperationCount);

        viewModel.RenameUnnumbered = false;

        Assert.Equal(0, viewModel.OperationCount);
        Assert.False(viewModel.CanApply);
        Assert.Equal("Nothing to rename.", viewModel.ApplyDisabledReason);
    }

    [Fact]
    public void BlockersSurfaceInApplyDisabledReasonVerbatim()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture(includeVaultManagedPart: false, rootDirty: true);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        Assert.NotEmpty(viewModel.Blockers);
        Assert.Equal(string.Join(" ", viewModel.Blockers), viewModel.ApplyDisabledReason);
        Assert.Contains("unsaved", viewModel.ApplyDisabledReason, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void AnalyzeModeNeverAllowsApply()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: false);

        Assert.Equal(FileNamingMode.Analyze, viewModel.Mode);
        Assert.False(viewModel.IsApplyMode);
        Assert.NotNull(viewModel.Plan);
        Assert.True(viewModel.Plan!.Operations.Count > 0);
        Assert.False(viewModel.CanApply);
        Assert.Equal("Switch to Apply mode to enable renaming.", viewModel.ApplyDisabledReason);
    }

    [Fact]
    public void ApplyCallsExecuteAndFormatsStatusMessageThenDisablesApply()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _, NamingAnalysis analysis) =
            BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        Assert.True(viewModel.CanApply);

        viewModel.Apply();

        Assert.Single(gateway.RenameCalls);
        Assert.StartsWith("Renamed 1 of 1. Originals:", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("_renamed-originals", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Null(viewModel.Plan);
        Assert.False(viewModel.CanApply);
        Assert.Equal("Nothing to rename.", viewModel.ApplyDisabledReason);
    }

    [Fact]
    public void ApplyReportsPerItemFailures()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _, NamingAnalysis analysis) =
            BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        string partPath = Path.Combine(ProjectRoot, "Bracket.ipt");
        gateway.FailRenameFor(partPath, new InvalidOperationException("locked by another process"));

        viewModel.Apply();

        Assert.StartsWith("Renamed 0 of 1. Originals: none", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("locked by another process", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model that was renamed but whose Part Number write failed is not a failed item: the file on
    /// disk has its new name and its parent was saved. Counting it as "Renamed 0 of 1" told the operator
    /// nothing had happened when in fact the rename had, so the count says renamed and warns separately.
    /// </summary>
    [Fact]
    public void ApplyCountsPartiallySucceededItemsSeparatelyInTheStatusMessage()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _, NamingAnalysis analysis) =
            BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        string newPartPath = Path.Combine(ProjectRoot, "124-0001 Bracket.ipt");
        gateway.FailSetPartNumberFor(newPartPath, new InvalidOperationException("the property is read-only"));

        viewModel.Apply();

        Assert.StartsWith(
            "Renamed 1 of 1, of which 1 with warnings. Originals:",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Model renamed; Part Number write failed: the property is read-only",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
        Assert.Contains("_renamed-originals", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// Execute pre-opens every companion drawing before any rename runs, so a model whose own rename
    /// succeeded leaves its drawing open in Inventor with the reference already rewritten to the model's
    /// new name - the drawing's own save is what failed, not the reference. The status text has to tell
    /// the operator that, or they will re-run Apply on a model that already moved.
    /// </summary>
    [Fact]
    public void ApplyTellsTheOperatorToSaveTheDrawingWhenACompanionDrawingRenameFails()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Bracket.ipt");
        string drawingPath = Path.Combine(ProjectRoot, "Bracket.idw");

        List<DocumentSnapshot> docs =
        [
            Doc(rootPath, DocumentKind.Assembly, isRoot: true),
            Doc(partPath, DocumentKind.Part, parents: [rootPath]),
        ];

        gateway.Snapshot = new ActiveAssemblySnapshot(rootPath, false, false, docs);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath, drawingPath]);
        gateway.FailRenameFor(drawingPath, new InvalidOperationException("the drawing is checked out"));

        NamingAnalysis analysis = workflow.Analyze(null);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        viewModel.Apply();

        Assert.Contains(
            "Model renamed; companion drawing 'Bracket.idw' failed: the drawing is checked out "
                + "The drawing is still open with the corrected reference; save it in Inventor to finish.",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyReportsParentSaveFailuresAndLeavesOriginalsInPlace()
    {
        (FileNamingWorkflow workflow, FakeInventorNamingGateway gateway, _, NamingAnalysis analysis) =
            BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        gateway.FailSaveFor(rootPath, new InvalidOperationException("checked out to someone else"));

        viewModel.Apply();

        Assert.Contains(
            "Parent save failed: 124-A001 GRM (main assembly).iam: checked out to someone else",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "Originals were left in place so the assembly still opens; save the listed parents and run Apply again.",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Apply runs inside an Inventor command callback. An exception that escapes it surfaces to the user
    /// as an opaque add-in fault and can unload the add-in, so every failure has to become a status
    /// message instead (review verdict R2).
    /// </summary>
    [Fact]
    public void ApplyCatchesAnExceptionFromExecuteAndReportsItInTheStatusMessage()
    {
        (FileNamingWorkflow workflow, _, FakeNamingFileSystem fileSystem, NamingAnalysis analysis) =
            BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        fileSystem.FailWriteManifest(new UnauthorizedAccessException("the archive folder is read-only"));

        viewModel.Apply();

        Assert.Contains("the archive folder is read-only", viewModel.StatusMessage, StringComparison.Ordinal);
        Assert.Null(viewModel.Plan);
        Assert.False(viewModel.CanApply);
    }

    [Fact]
    public void ApplyReportsArchiveFailuresAndSaysTheRenamesStand()
    {
        (FileNamingWorkflow workflow, _, FakeNamingFileSystem fileSystem, NamingAnalysis analysis) =
            BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        string partPath = Path.Combine(ProjectRoot, "Bracket.ipt");
        fileSystem.FailMoveFor(partPath, new IOException("the original is locked"));

        viewModel.Apply();

        Assert.Contains(
            "Archive failed: Bracket.ipt: the original is locked",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
        Assert.Contains(
            "The renames stand; the listed originals are still beside their renamed files and were not archived.",
            viewModel.StatusMessage,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildVaultPlanTextListsInstructionsLeafFirstWithParents()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string subPath = Path.Combine(ProjectRoot, "SubAssembly.iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");

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
        fileSystem.MarkVaultManaged(subPath);
        fileSystem.MarkVaultManaged(partPath);

        NamingAnalysis analysis = workflow.Analyze(null);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        string text = viewModel.BuildVaultPlanText();

        Assert.Contains($"Project root: {ProjectRoot}", text, StringComparison.Ordinal);

        int partIndex = text.IndexOf("Widget.ipt ->", StringComparison.Ordinal);
        int subIndex = text.IndexOf("SubAssembly.iam ->", StringComparison.Ordinal);
        Assert.True(partIndex >= 0, $"Expected the part instruction line in: {text}");
        Assert.True(subIndex >= 0, $"Expected the sub-assembly instruction line in: {text}");
        Assert.True(partIndex < subIndex, "Expected the leaf part instruction before its parent sub-assembly.");

        Assert.Contains("parents: SubAssembly.iam", text, StringComparison.Ordinal);
        Assert.Contains("parents: 124-A001 GRM (main assembly).iam", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exported Vault plan is the artifact an operator hands to Vault Explorer; if the plan is
    /// incomplete because of a blocker, that has to travel with it, not stay visible only in the app's own
    /// Blockers panel (Residual 2).
    /// </summary>
    [Fact]
    public void BuildVaultPlanTextAppendsBlockersSectionWhenPlanHasBlockers()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string partPath = Path.Combine(ProjectRoot, "Widget.ipt");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            true, // RootIsDirty guarantees a plan-level blocker.
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true, isDirty: true),
                Doc(partPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, partPath]);
        fileSystem.MarkVaultManaged(partPath);

        NamingAnalysis analysis = workflow.Analyze(null);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        string text = viewModel.BuildVaultPlanText();

        Assert.NotEmpty(viewModel.Blockers);
        Assert.Contains("Blockers (this plan is incomplete until resolved):", text, StringComparison.Ordinal);
        Assert.All(viewModel.Blockers, blocker => Assert.Contains(blocker, text, StringComparison.Ordinal));

        int instructionIndex = text.IndexOf("Widget.ipt ->", StringComparison.Ordinal);
        int blockersHeaderIndex = text.IndexOf("Blockers (this plan is incomplete until resolved):", StringComparison.Ordinal);
        Assert.True(instructionIndex >= 0, $"Expected the part instruction line in: {text}");
        Assert.True(instructionIndex < blockersHeaderIndex, "Expected the blockers section after the instructions.");
    }

    /// <summary>
    /// Rows are plan-derived, so an excluded row has to go through a real re-plan: the operation really
    /// disappears from the plan the Apply button would execute, not just from the row's own display.
    /// </summary>
    [Fact]
    public void ExcludingARowDropsItsOperationAndTogglingBackRestoresIt()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        NamingRowViewModel row = Assert.Single(viewModel.Rows, r => r.CurrentFileName == "Bracket.ipt");

        Assert.True(row.IsIncluded);
        Assert.True(row.CanToggle);
        Assert.Equal(1, viewModel.OperationCount);

        row.IsIncluded = false;

        Assert.Equal(0, viewModel.OperationCount);
        Assert.Equal("None", row.Action);
        Assert.Equal(string.Empty, row.ProposedFileName);
        Assert.Equal("Excluded by the operator.", row.Reasons);
        Assert.True(row.CanToggle, "An excluded row must stay toggleable, or the operator cannot undo the exclusion.");
        Assert.False(viewModel.CanApply);

        row.IsIncluded = true;

        Assert.Equal(1, viewModel.OperationCount);
        Assert.Equal("Rename", row.Action);
        Assert.Equal("124-0001 Bracket.ipt", row.ProposedFileName);
        Assert.Equal(string.Empty, row.Reasons);
        Assert.True(viewModel.CanApply);
    }

    /// <summary>
    /// A blocked row is in neither Operations nor VaultInstructions, so its Action is None - but unticking
    /// it is exactly what clears its blocker and lets the unaffected rows run. A checkbox disabled on
    /// "Action is None" took that away and left the operator with a plan they could not unblock from the
    /// window at all (review finding P1).
    /// </summary>
    [Fact]
    public void ABlockedRowStaysToggleableAndExcludingItClearsTheBlockerAndEnablesApply()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
        string blockedPartPath = Path.Combine(ProjectRoot, "Bracket.ipt");
        string cleanPartPath = Path.Combine(ProjectRoot, "Widget.ipt");
        string externalParentPath = Path.Combine(ProjectRoot, "OtherAssembly.iam");

        gateway.Snapshot = new ActiveAssemblySnapshot(
            rootPath,
            false,
            false,
            [
                Doc(rootPath, DocumentKind.Assembly, isRoot: true),
                Doc(blockedPartPath, DocumentKind.Part, parents: [rootPath], externalParents: [externalParentPath]),
                Doc(cleanPartPath, DocumentKind.Part, parents: [rootPath]),
            ]);
        fileSystem.SetScope(ProjectRoot, [rootPath, blockedPartPath, cleanPartPath]);

        NamingAnalysis analysis = workflow.Analyze(null);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        NamingRowViewModel blockedRow = Assert.Single(viewModel.Rows, r => r.CurrentFileName == "Bracket.ipt");

        Assert.Equal("None", blockedRow.Action);
        Assert.True(blockedRow.HasBlocker);
        Assert.True(
            blockedRow.CanToggle,
            "A row the plan blocked must stay toggleable: excluding it is the only way to clear its blocker.");
        Assert.NotEmpty(viewModel.Blockers);
        Assert.False(viewModel.CanApply);

        blockedRow.IsIncluded = false;

        Assert.Empty(viewModel.Blockers);
        Assert.False(blockedRow.HasBlocker);
        Assert.True(blockedRow.CanToggle, "An excluded row must stay toggleable so the operator can undo it.");
        Assert.Equal(1, viewModel.OperationCount);
        Assert.True(viewModel.CanApply);

        blockedRow.IsIncluded = true;

        Assert.NotEmpty(viewModel.Blockers);
        Assert.True(blockedRow.HasBlocker);
        Assert.True(blockedRow.CanToggle);
        Assert.False(viewModel.CanApply);
    }

    /// <summary>
    /// A canonical row has nothing to include or exclude, so its checkbox must be disabled rather than
    /// offering the operator a toggle that changes nothing.
    /// </summary>
    [Fact]
    public void RowsWithNoActionCannotBeToggled()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture(includeVaultManagedPart: false);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        NamingRowViewModel rootRow =
            Assert.Single(viewModel.Rows, r => r.CurrentFileName == "124-A001 GRM (main assembly).iam");

        Assert.Equal("None", rootRow.Action);
        Assert.True(rootRow.IsIncluded);
        Assert.False(rootRow.HasBlocker);
        Assert.False(rootRow.CanToggle);
    }

    [Fact]
    public void ExcludingAVaultManagedRowDropsItFromTheExportedVaultPlan()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture(includeVaultManagedPart: true);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        NamingRowViewModel row = Assert.Single(viewModel.Rows, r => r.CurrentFileName == "Widget.ipt");

        Assert.Equal(1, viewModel.VaultInstructionCount);
        Assert.Contains("Widget.ipt ->", viewModel.BuildVaultPlanText(), StringComparison.Ordinal);

        row.IsIncluded = false;

        Assert.Equal(0, viewModel.VaultInstructionCount);
        Assert.False(viewModel.HasVaultInstructions);
        Assert.DoesNotContain("Widget.ipt ->", viewModel.BuildVaultPlanText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SelectNoRowsExcludesEveryActionableRowAndSelectAllRowsRestoresThem()
    {
        (FileNamingWorkflow workflow, _, _, NamingAnalysis analysis) = BuildFixture(includeVaultManagedPart: true);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);
        NamingRowViewModel rootRow =
            Assert.Single(viewModel.Rows, r => r.CurrentFileName == "124-A001 GRM (main assembly).iam");

        viewModel.SelectNoRows();

        Assert.Equal(0, viewModel.OperationCount);
        Assert.Equal(0, viewModel.VaultInstructionCount);
        Assert.True(rootRow.IsIncluded, "A row that cannot be toggled must not be excluded by Select none.");

        viewModel.SelectAllRows();

        Assert.Equal(1, viewModel.OperationCount);
        Assert.Equal(1, viewModel.VaultInstructionCount);
        Assert.All(viewModel.Rows, row => Assert.True(row.IsIncluded));
    }

    [Fact]
    public void BuildVaultPlanTextListsCompanionDrawingsIndentedUnderTheirInstruction()
    {
        FakeInventorNamingGateway gateway = new();
        FakeNamingFileSystem fileSystem = new();
        FileNamingWorkflow workflow = new(gateway, fileSystem, new FakeClock());

        string rootPath = Path.Combine(ProjectRoot, "124-A001 GRM (main assembly).iam");
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

        NamingAnalysis analysis = workflow.Analyze(null);
        FileNamingViewModel viewModel = new(workflow, analysis, applyMode: true);

        string text = viewModel.BuildVaultPlanText();

        int instructionIndex = text.IndexOf("Widget.ipt ->", StringComparison.Ordinal);
        int drawingIndex = text.IndexOf("drawing: Widget.idw -> 124-0001 Widget.idw", StringComparison.Ordinal);
        Assert.True(instructionIndex >= 0, $"Expected the part instruction line in: {text}");
        Assert.True(drawingIndex >= 0, $"Expected the companion drawing line in: {text}");
        Assert.True(instructionIndex < drawingIndex, "Expected the drawing line indented under its instruction.");
    }
}
