// Purpose: Define the analysis, planning, and execution result contracts for the naming workflow.
// Inputs: n/a (immutable result and request records used across FileNamingWorkflow).
// Outputs: Report-plus-row analyses, executable rename plans, and per-item execution results.
// Dependencies: FileNamingManager.Core only.
// Assumptions: A row without a ProposedFileName never appears in Operations or VaultInstructions.
// Validation source: .work/TASK.md "Vault safety model" and "Application" sections; FileNamingWorkflowTests.

using FileNamingManager.Core;

namespace FileNamingManager.Application;

public enum VaultState
{
    Unmanaged,
    Managed,
}

public enum RenameAction
{
    None,
    Rename,
    VaultRename,
    Blocked,
}

public sealed record NamingAnalysisRow(
    string FullPath,
    string CurrentFileName,
    DocumentKind Kind,
    bool IsRoot,
    bool IsModifiable,
    bool IsDirty,
    IReadOnlyList<string> ParentFullPaths,
    IReadOnlyList<string> ExternalParentFullPaths,
    string? PartNumberProperty,
    ParsedFileName Parsed,
    VaultState VaultState,
    string? ProposedFileName,
    RenameAction Action,
    IReadOnlyList<string> Reasons);

/// <summary>
/// One project-scope file, paired with its parse result, kept so Plan can allocate numbers and find
/// companion drawings without re-touching the file system.
/// </summary>
public sealed record ScopeEntry(string FullPath, ParsedFileName Parsed);

public sealed record NamingAnalysis(
    string? ErrorMessage,
    string? RootFullPath,
    string? ProjectRootPath,
    bool RootIsDirty,
    bool RootHasMissingReferences,
    ProjectNumber? SuggestedProject,
    ProjectNumberSuggestionSource? SuggestionSource,
    NamingReport? Report,
    IReadOnlyList<NamingAnalysisRow> Rows,
    IReadOnlyList<ScopeEntry> Scope)
{
    public bool IsSuccess => ErrorMessage is null;
}

public sealed record RenameOptions(
    bool RenameUnnumbered = true,
    bool NormalizeMalformed = false,
    bool SetPartNumberProperty = true);

public sealed record RenameOperation(
    string CurrentFullPath,
    string NewFullPath,
    DocumentKind Kind,
    IReadOnlyList<string> ParentsToSave,
    IReadOnlyList<(string Current, string New)> CompanionDrawings,
    string? PartNumberToSet);

/// <summary>
/// CompanionDrawings is informational only: v1 never renames a Vault-managed file's companion drawing,
/// so it carries no blocker for this row (see AddCompanionBlockers, which runs only for the unmanaged
/// branch). It tells the operator, in the exported plan, which drawings to also rename in Vault Explorer.
/// </summary>
public sealed record VaultRenameInstruction(
    string CurrentFullPath,
    string ProposedFileName,
    DocumentKind Kind,
    IReadOnlyList<string> ParentFullPaths,
    IReadOnlyList<(string Current, string New)> CompanionDrawings);

public sealed record RenamePlan(
    string ProjectRootPath,
    IReadOnlyList<RenameOperation> Operations,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<VaultRenameInstruction> VaultInstructions,
    IReadOnlyList<string> ParentSaveOrder)
{
    public bool CanExecute => Blockers.Count == 0;
}

public sealed record RenameItemResult(string CurrentFullPath, string NewFullPath, bool Succeeded, string? ErrorMessage);

/// <summary>
/// An unsaved parent still references the old file name on disk after a failed save, so moving the
/// renamed originals aside would strand it; ParentSaveFailures tells the caller exactly which parent
/// to save and retry, and OriginalsMoved is false whenever any parent save fails.
/// </summary>
public sealed record ParentSaveFailure(string ParentFullPath, string ErrorMessage);

/// <summary>
/// One original that was renamed successfully but could not be moved into _renamed-originals. The
/// rename itself stands; the original is simply still sitting beside its renamed file. Archival is
/// per-item isolated, so one failure never stops the remaining originals being archived.
/// </summary>
public sealed record ArchiveFailure(string OriginalPath, string ErrorMessage);

public sealed record RenameExecution(
    IReadOnlyList<RenameItemResult> Items,
    RenameManifest? Manifest,
    IReadOnlyList<ParentSaveFailure> ParentSaveFailures,
    IReadOnlyList<ArchiveFailure> ArchiveFailures,
    bool OriginalsMoved);
