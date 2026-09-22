// Purpose: Define the COM-free, IO-free ports UpdateWorkflow depends on, so the whole update decision
//   is testable without a network, a filesystem, or a process.
// Inputs: n/a (interfaces and their result records).
// Outputs: Contracts implemented by WmpToolsManager.Infrastructure.
// Dependencies: WmpToolsManager.Core only.
// Assumptions: Every port returns a result record rather than throwing. An update check runs from an
//   Inventor command callback, where an escaping exception can take Inventor down, so "the network was
//   unreachable" has to be a value the workflow can put on screen, not an exception.
// Validation source: docs/decisions/0005-release-distribution-and-updater.md item 3.

using WmpToolsManager.Core;

namespace WmpToolsManager.Application;

/// <summary>Downloaded text, or the reason it could not be fetched.</summary>
public sealed record SourceText(string? Content, string? ErrorMessage)
{
    public bool IsSuccess => ErrorMessage is null;
}

/// <summary>A downloaded file on disk, or the reason it is not there.</summary>
public sealed record SourceFile(string? FilePath, long SizeInBytes, string? ErrorMessage)
{
    public bool IsSuccess => ErrorMessage is null;
}

/// <summary>A computed digest, or the reason it could not be computed.</summary>
public sealed record HashResult(string? Sha256, string? ErrorMessage)
{
    public bool IsSuccess => ErrorMessage is null;
}

/// <summary>Whether a process was started, and what to tell the user either way.</summary>
public sealed record LaunchResult(bool Launched, string Message);

public interface IReleaseSource
{
    /// <summary>
    /// Fetches releases/latest/download/SHA256SUMS.txt. This, not the Releases API, is the version
    /// check: the download URL needs no token and is not rate-limited.
    /// </summary>
    Task<SourceText> GetLatestSumsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the Releases API payload for one tag, for its notes body alone. The API is
    /// rate-limited without a token, so a failure here is expected and must degrade to "no notes",
    /// never to a failed update check.
    /// </summary>
    Task<SourceText> GetReleasePayloadAsync(string tag, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads one asset of the latest release to <paramref name="destinationPath"/>.
    /// </summary>
    Task<SourceFile> DownloadLatestAssetAsync(
        string assetFileName,
        string destinationPath,
        CancellationToken cancellationToken);
}

public interface IInstallState
{
    /// <summary>Reads installed.json. A missing or malformed file is a failed parse, never an exception.</summary>
    ParseResult<InstalledState> ReadInstalled(string installedStatePath);

    /// <summary>Reads catalog.json out of a package zip without extracting the package.</summary>
    ParseResult<ReleaseCatalog> ReadCatalog(string packageZipPath);

    /// <summary>
    /// True when the previous root holds an install that the installer's -Rollback would actually
    /// restore, which means at least one non-empty subdirectory.
    /// </summary>
    bool HasPreviousInstall(string previousRoot);

    /// <summary>
    /// The installer script every install persists directly under the state root
    /// (&lt;stateRoot&gt;\Install-WmpInventorTools.ps1), or - for an install that predates that
    /// persisted copy - the newest copy staged by an update check under &lt;stateRoot&gt;\staging.
    /// Null when neither exists. Rollback needs this script on disk to run itself.
    /// </summary>
    string? FindInstaller(string stateRoot);

    /// <summary>Creates a directory and every missing parent. Existing content is left alone.</summary>
    void CreateDirectory(string path);

    /// <summary>Writes UTF-8 text without a byte-order mark, replacing any existing file.</summary>
    void WriteText(string path, string content);
}

public interface IHashVerifier
{
    HashResult ComputeSha256(string filePath);
}

public interface IProcessLauncher
{
    LaunchResult Launch(string fileName, string arguments);
}
