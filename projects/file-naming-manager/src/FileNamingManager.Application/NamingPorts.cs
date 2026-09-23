// Purpose: Define the COM-free application ports FileNamingWorkflow depends on.
// Inputs: n/a (interfaces and their supporting DTOs).
// Outputs: Contracts implemented by InventorAdapter (gateway) and Infrastructure (file system, clock).
// Dependencies: FileNamingManager.Core only.
// Assumptions: Host adapters invoke these contracts synchronously on Inventor's owning STA thread.
// Validation source: .work/TASK.md "Vault safety model" section and the Inventor 2027 compatibility matrix.

using FileNamingManager.Core;

namespace FileNamingManager.Application;

/// <summary>
/// One document of the active assembly. ParentFullPaths lists referencing documents that are part of
/// this snapshot; ExternalParentFullPaths lists referencing documents open in the session that are not,
/// for example a drawing of another assembly. Renaming a document whose external parents are not
/// handled would leave those parents pointing at a file name that no longer exists.
/// </summary>
public sealed record DocumentSnapshot(
    string FullPath,
    DocumentKind Kind,
    bool IsRoot,
    bool IsModifiable,
    bool IsDirty,
    bool IsSuppressedOnly,
    IReadOnlyList<string> ParentFullPaths,
    IReadOnlyList<string> ExternalParentFullPaths,
    string? PartNumberProperty);

public sealed record ActiveAssemblySnapshot(
    string RootFullPath,
    bool RootIsDirty,
    bool RootHasMissingReferences,
    IReadOnlyList<DocumentSnapshot> Documents);

public interface IInventorNamingGateway
{
    /// <summary>
    /// Returns null when the active document is not a saved assembly.
    /// </summary>
    ActiveAssemblySnapshot? ScanActiveAssembly();

    /// <summary>
    /// Renames a document in-session via SaveAs(newFullPath, false).
    /// </summary>
    void RenameDocument(string currentFullPath, string newFullPath);

    /// <summary>
    /// Idempotent: opens fullPath invisibly and tracks it for later close when it is not already open.
    /// A companion drawing must be open before its model's SaveAs so the drawing's reference to the model
    /// is rewritten in memory; a drawing opened only afterward would still resolve to the model's old name.
    /// </summary>
    void EnsureDocumentOpen(string fullPath);

    void SaveDocument(string fullPath);

    void SetPartNumber(string fullPath, string partNumber);
}

public interface INamingFileSystem
{
    /// <summary>
    /// Returns full paths of .ipt/.iam/.idw/.dwg/.ipn files recursively under the project root, excluding
    /// every folder named in <see cref="Core.NamingScopeRules.ExcludedFolderNames"/> at any depth. The
    /// list is not repeated here: the implementation and FileNamingWorkflow's per-row scope rule must
    /// agree on it, so it lives in one place.
    /// </summary>
    IReadOnlyList<string> EnumerateScope(string projectRoot);

    /// <summary>
    /// True when a Vault tracker file exists beside fullPath at "&lt;dir&gt;\_V\&lt;fileName&gt;.v".
    /// </summary>
    bool IsVaultManaged(string fullPath);

    /// <summary>
    /// True when fullPath exists and carries the read-only attribute. A read-only file cannot be renamed
    /// or saved over, so a plan that would touch one is blocked before any exporter writes.
    /// </summary>
    bool IsReadOnly(string fullPath);

    bool FileExists(string fullPath);

    bool DirectoryExists(string path);

    /// <summary>
    /// Moves fullPath into originalsRoot, preserving its path relative to projectRoot. Never deletes.
    /// </summary>
    void MoveToOriginals(string fullPath, string originalsRoot, string projectRoot);

    void WriteManifest(string originalsRoot, RenameManifest manifest);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// One planned archival of a renamed original. The manifest is written once before any move, with
/// Archived false and Error null on every entry, and again after the moves with each entry's outcome,
/// so a run interrupted part-way still leaves a record of exactly what was intended and what happened.
/// </summary>
public sealed record RenameManifestEntry(
    string OriginalPath,
    string ArchivedPath,
    string RenamedTo,
    bool Archived,
    string? Error);

public sealed record RenameManifest(
    DateTimeOffset Timestamp,
    string ProjectRoot,
    IReadOnlyList<RenameManifestEntry> Entries);
