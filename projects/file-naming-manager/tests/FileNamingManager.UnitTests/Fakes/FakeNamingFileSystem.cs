// Purpose: A deterministic, in-memory INamingFileSystem test double that intentionally has no delete API.
// Inputs: Configurable scope listings, Vault-managed paths, and existing-file paths.
// Outputs: Recorded MoveToOriginals/WriteManifest calls the tests assert against.
// Dependencies: FileNamingManager.Application.
// Assumptions: Single-threaded test usage only. There is deliberately no Delete method on this type:
//   Execute-isolation tests assert nothing-deleted by reflecting over this fake's public members.
// Validation source: n/a - test infrastructure.

using FileNamingManager.Application;

namespace FileNamingManager.UnitTests.Fakes;

public sealed class FakeNamingFileSystem : INamingFileSystem
{
    private readonly Dictionary<string, List<string>> scopeByRoot = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> vaultManagedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> existingFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> readOnlyPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> existingDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> moveFailures = new(StringComparer.OrdinalIgnoreCase);
    private Exception? writeManifestFailure;

    public List<(string FullPath, string OriginalsRoot, string ProjectRoot)> MoveCalls { get; } = [];

    public List<RenameManifest> WrittenManifests { get; } = [];

    public void SetScope(string projectRoot, IEnumerable<string> files) => scopeByRoot[projectRoot] = [.. files];

    public void MarkVaultManaged(string fullPath) => vaultManagedPaths.Add(fullPath);

    public void MarkFileExists(string fullPath) => existingFiles.Add(fullPath);

    public void MarkReadOnly(string fullPath) => readOnlyPaths.Add(fullPath);

    public void MarkDirectoryExists(string path) => existingDirectories.Add(path);

    /// <summary>
    /// Makes MoveToOriginals throw for one original path, so archival-isolation tests can fail exactly
    /// one move in the middle of a run without failing the moves either side of it.
    /// </summary>
    public void FailMoveFor(string fullPath, Exception exception) => moveFailures[fullPath] = exception;

    public void FailWriteManifest(Exception exception) => writeManifestFailure = exception;

    public IReadOnlyList<string> EnumerateScope(string projectRoot) =>
        scopeByRoot.TryGetValue(projectRoot, out List<string>? files) ? files : [];

    public bool IsVaultManaged(string fullPath) => vaultManagedPaths.Contains(fullPath);

    public bool IsReadOnly(string fullPath) => readOnlyPaths.Contains(fullPath);

    public bool FileExists(string fullPath) => existingFiles.Contains(fullPath);

    public bool DirectoryExists(string path) => existingDirectories.Contains(path);

    public void MoveToOriginals(string fullPath, string originalsRoot, string projectRoot)
    {
        if (moveFailures.TryGetValue(fullPath, out Exception? exception))
        {
            throw exception;
        }

        MoveCalls.Add((fullPath, originalsRoot, projectRoot));
    }

    public void WriteManifest(string originalsRoot, RenameManifest manifest)
    {
        if (writeManifestFailure is not null)
        {
            throw writeManifestFailure;
        }

        WrittenManifests.Add(manifest);
    }
}
