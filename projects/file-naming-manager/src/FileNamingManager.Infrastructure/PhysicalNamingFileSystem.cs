// Purpose: Provide the real, conservative file system operations FileNamingWorkflow needs.
// Inputs: Project root, file, and originals-archive paths.
// Outputs: Scope listings, Vault-tracker and existence checks, archived originals, and a JSON manifest.
// Dependencies: System.IO, System.Text.Json, and FileNamingManager.Core.NamingScopeRules.
// Assumptions: Runs on Windows; MoveToOriginals never overwrites; nothing here ever deletes a file. The
//   excluded-folder list is NamingScopeRules' alone, so enumeration and the per-row scope rule cannot drift.
// Validation source: .work/TASK.md "Vault safety model" section; PhysicalNamingFileSystemTests temp-directory
//   cases; ScopeAgreementTests.

using System.Text.Json;
using System.Text.Json.Serialization;
using FileNamingManager.Application;
using FileNamingManager.Core;

namespace FileNamingManager.Infrastructure;

public sealed class PhysicalNamingFileSystem : INamingFileSystem
{
    private static readonly string[] ScopeExtensions = [".ipt", ".iam", ".idw", ".dwg", ".ipn"];
    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<string> EnumerateScope(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        if (!Directory.Exists(projectRoot))
        {
            return [];
        }

        List<string> results = [];
        EnumerateDirectory(projectRoot, results);
        return results;
    }

    public bool IsVaultManaged(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        string? directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        string tracker = Path.Combine(directory, "_V", Path.GetFileName(fullPath) + ".v");
        return File.Exists(tracker);
    }

    /// <summary>
    /// A path that is not on disk is nothing to protect, and File.GetAttributes would throw for it. The
    /// caller asks about files it found by enumerating the scope, so a missing one is a race rather than
    /// malformed input; reporting "not read-only" leaves the later rename to fail loudly and precisely.
    /// </summary>
    public bool IsReadOnly(string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

        if (!File.Exists(fullPath))
        {
            return false;
        }

        return (File.GetAttributes(fullPath) & FileAttributes.ReadOnly) == FileAttributes.ReadOnly;
    }

    public bool FileExists(string fullPath) => File.Exists(fullPath);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void MoveToOriginals(string fullPath, string originalsRoot, string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        string relative = Path.GetRelativePath(projectRoot, fullPath);
        string destination = Path.Combine(originalsRoot, relative);

        if (File.Exists(destination))
        {
            throw new IOException($"Refusing to overwrite an existing archived original: '{destination}'.");
        }

        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Move(fullPath, destination);
    }

    public void WriteManifest(string originalsRoot, RenameManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalsRoot);
        ArgumentNullException.ThrowIfNull(manifest);

        Directory.CreateDirectory(originalsRoot);

        ManifestDto dto = new(
            manifest.Timestamp,
            manifest.ProjectRoot,
            [.. manifest.Entries.Select(entry => new ManifestEntryDto(
                entry.OriginalPath,
                entry.ArchivedPath,
                entry.RenamedTo,
                entry.Archived,
                entry.Error))]);

        string json = JsonSerializer.Serialize(dto, ManifestJsonOptions);
        File.WriteAllText(Path.Combine(originalsRoot, "manifest.json"), json);
    }

    private static void EnumerateDirectory(string directory, List<string> results)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string extension = Path.GetExtension(file);
            if (ScopeExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                results.Add(file);
            }
        }

        foreach (string subDirectory in Directory.EnumerateDirectories(directory))
        {
            string name = Path.GetFileName(subDirectory);
            if (NamingScopeRules.IsExcludedFolderName(name))
            {
                continue;
            }

            EnumerateDirectory(subDirectory, results);
        }
    }

    private sealed record ManifestEntryDto(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string To,
        [property: JsonPropertyName("renamedTo")] string RenamedTo,
        [property: JsonPropertyName("archived")] bool Archived,
        [property: JsonPropertyName("error")] string? Error);

    private sealed record ManifestDto(
        [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
        [property: JsonPropertyName("projectRoot")] string ProjectRoot,
        [property: JsonPropertyName("entries")] IReadOnlyList<ManifestEntryDto> Entries);
}
