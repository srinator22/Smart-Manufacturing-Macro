// Purpose: A deterministic, in-memory IInventorNamingGateway test double.
// Inputs: A configurable ActiveAssemblySnapshot and per-path failure injection for RenameDocument.
// Outputs: Recorded call logs the tests assert against.
// Dependencies: FileNamingManager.Application.
// Assumptions: Single-threaded test usage only.
// Validation source: n/a - test infrastructure.

using FileNamingManager.Application;

namespace FileNamingManager.UnitTests.Fakes;

public sealed class FakeInventorNamingGateway : IInventorNamingGateway
{
    private readonly Dictionary<string, Exception> renameFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> saveFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> ensureOpenFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> setPartNumberFailures = new(StringComparer.OrdinalIgnoreCase);

    public ActiveAssemblySnapshot? Snapshot { get; set; }

    public List<(string Current, string New)> RenameCalls { get; } = [];

    public List<string> SaveCalls { get; } = [];

    public List<(string Path, string PartNumber)> SetPartNumberCalls { get; } = [];

    /// <summary>
    /// Every call this fake recorded, in order, as "EnsureOpen:&lt;path&gt;", "Rename:&lt;from&gt;-&gt;&lt;to&gt;",
    /// "Save:&lt;path&gt;", or "SetPartNumber:&lt;path&gt;=&lt;value&gt;". Kept alongside the per-call-kind lists above
    /// so ordering-sensitive tests (e.g. companion drawings opened before their model is renamed) can assert
    /// interleaving that the per-kind lists cannot express.
    /// </summary>
    public List<string> CallLog { get; } = [];

    public ActiveAssemblySnapshot? ScanActiveAssembly() => Snapshot;

    public void FailRenameFor(string currentFullPath, Exception exception) => renameFailures[currentFullPath] = exception;

    public void FailSaveFor(string fullPath, Exception exception) => saveFailures[fullPath] = exception;

    public void FailEnsureOpenFor(string fullPath, Exception exception) => ensureOpenFailures[fullPath] = exception;

    /// <summary>
    /// Fails the Part Number write for one renamed document. The path is the document's NEW full path,
    /// because Execute writes the property after the rename.
    /// </summary>
    public void FailSetPartNumberFor(string fullPath, Exception exception) => setPartNumberFailures[fullPath] = exception;

    public void EnsureDocumentOpen(string fullPath)
    {
        if (ensureOpenFailures.TryGetValue(fullPath, out Exception? exception))
        {
            throw exception;
        }

        CallLog.Add($"EnsureOpen:{fullPath}");
    }

    public void RenameDocument(string currentFullPath, string newFullPath)
    {
        if (renameFailures.TryGetValue(currentFullPath, out Exception? exception))
        {
            throw exception;
        }

        RenameCalls.Add((currentFullPath, newFullPath));
        CallLog.Add($"Rename:{currentFullPath}->{newFullPath}");
    }

    public void SaveDocument(string fullPath)
    {
        if (saveFailures.TryGetValue(fullPath, out Exception? exception))
        {
            throw exception;
        }

        SaveCalls.Add(fullPath);
        CallLog.Add($"Save:{fullPath}");
    }

    public void SetPartNumber(string fullPath, string partNumber)
    {
        if (setPartNumberFailures.TryGetValue(fullPath, out Exception? exception))
        {
            throw exception;
        }

        SetPartNumberCalls.Add((fullPath, partNumber));
        CallLog.Add($"SetPartNumber:{fullPath}={partNumber}");
    }
}
