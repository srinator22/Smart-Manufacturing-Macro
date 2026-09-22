// Purpose: Read and write the updater's on-disk state - installed.json, the catalog inside a package
//   zip, the archived previous install, the staged installer script, and the pending-apply marker one
//   launched installer leaves behind while it waits for Inventor to exit.
// Inputs: Absolute paths the Application layer already checked against the state root.
// Outputs: Parse results, booleans, and created folders. Nothing is ever deleted here.
// Dependencies: System.IO and System.IO.Compression.
// Assumptions: catalog.json sits at the root of the package zip, because build-release.ps1 compresses
//   the staged folder's contents rather than the folder. Compress-Archive has historically written
//   backslash separators into entry names, so the lookup does not assume a separator style.
// Validation source: scripts/release/build-release.ps1 (zip layout) and
//   scripts/release/Install-WmpInventorTools.ps1 (installed.json, previous/, staging/).

using System.IO.Compression;
using System.Text;
using WmpToolsManager.Application;
using WmpToolsManager.Core;

namespace WmpToolsManager.Infrastructure;

public sealed class PhysicalInstallState : IInstallState
{
    public const string CatalogFileName = "catalog.json";

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public ParseResult<InstalledState> ReadInstalled(string installedStatePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installedStatePath);

        if (!File.Exists(installedStatePath))
        {
            return new ParseResult<InstalledState>(null, "No installed.json was found.");
        }

        try
        {
            return ReleaseJson.ParseInstalledState(File.ReadAllText(installedStatePath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ParseResult<InstalledState>(null, $"installed.json could not be read: {exception.Message}");
        }
    }

    public ParseResult<ReleaseCatalog> ReadCatalog(string packageZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageZipPath);

        try
        {
            using ZipArchive archive = ZipFile.OpenRead(packageZipPath);
            ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(candidate =>
                string.Equals(candidate.FullName, CatalogFileName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return new ParseResult<ReleaseCatalog>(
                    null,
                    $"The package '{Path.GetFileName(packageZipPath)}' contains no {CatalogFileName}.");
            }

            using StreamReader reader = new(entry.Open());
            return ReleaseJson.ParseCatalog(reader.ReadToEnd());
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new ParseResult<ReleaseCatalog>(
                null,
                $"The package '{Path.GetFileName(packageZipPath)}' could not be read: {exception.Message}");
        }
    }

    public bool HasPreviousInstall(string previousRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(previousRoot);

        try
        {
            // Mirrors the installer's Get-NewestPreviousInstall: an empty archive folder is not a
            // rollback target, so the dialog must not offer one.
            return Directory.Exists(previousRoot)
                && Directory.EnumerateDirectories(previousRoot)
                    .Any(directory => Directory.EnumerateFileSystemEntries(directory).Any());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public string? FindInstaller(string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);

        try
        {
            // Every install since ADR-0005's rollback fix persists its own copy directly under the
            // state root, so that copy is always current for whatever is actually installed. Only an
            // install made before that fix has to fall back to the staged copy a later update check
            // downloaded, which may not match what is currently installed.
            string persisted = Path.Combine(stateRoot, Sha256SumsFile.InstallerFileName);
            if (File.Exists(persisted))
            {
                return persisted;
            }

            string stagingRoot = Path.Combine(stateRoot, StagingPaths.StagingFolderName);
            if (!Directory.Exists(stagingRoot))
            {
                return null;
            }

            return Directory
                .EnumerateFiles(stagingRoot, Sha256SumsFile.InstallerFileName, SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void CreateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(path);
    }

    public void WriteText(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        File.WriteAllText(path, content, Utf8WithoutBom);
    }

    public ParseResult<PendingApply> ReadPendingApply(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            return new ParseResult<PendingApply>(null, PendingApply.NoMarkerMessage);
        }

        try
        {
            return PendingApplyJson.Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ParseResult<PendingApply>(
                null,
                $"{PendingApply.FileName} could not be read: {exception.Message}");
        }
    }

    public void WritePendingApply(string path, PendingApply value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(value);

        // A working-tree install has no state root yet, and the marker is the first thing written
        // there. Creating the folder leaves any existing content alone.
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, PendingApplyJson.Serialize(value), Utf8WithoutBom);
    }
}
