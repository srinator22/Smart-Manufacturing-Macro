// Purpose: Own every path the updater is allowed to touch, and refuse anything outside the state root.
//   ADR-0005 puts staging under %LOCALAPPDATA%\WMP\InventorTools precisely so the add-in never writes
//   into Inventor's Addins folder itself - the PowerShell apply step does that, after Inventor exits.
// Inputs: An absolute state root (the caller resolves %LOCALAPPDATA%; this type never reads the
//   environment) and file names taken from SHA256SUMS.txt.
// Outputs: Absolute paths under the state root, or an exception naming the path that escaped it.
// Dependencies: System.IO.Path for string arithmetic only. Nothing here touches the filesystem.
// Assumptions: A file name that arrives from a downloaded digest file is untrusted input. It can contain
//   a separator or "..", so every composed path is re-checked against the state root rather than trusted
//   because it was built by concatenation.
// Validation source: scripts/release/Install-WmpInventorTools.ps1 -StateRoot default and its
//   staging/previous/installed.json layout.

namespace WmpToolsManager.Core;

public sealed class StagingPaths
{
    /// <summary>The state root's path relative to %LOCALAPPDATA%, as the installer's default computes it.</summary>
    public const string StateRootRelativePath = @"WMP\InventorTools";

    public const string StagingFolderName = "staging";
    public const string PreviousFolderName = "previous";
    public const string InstalledStateFileName = "installed.json";

    public StagingPaths(string stateRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stateRoot);
        if (!Path.IsPathRooted(stateRoot))
        {
            throw new ArgumentException($"The state root must be an absolute path: '{stateRoot}'.", nameof(stateRoot));
        }

        StateRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stateRoot));
        StagingRoot = Path.Combine(StateRoot, StagingFolderName);
        PreviousRoot = Path.Combine(StateRoot, PreviousFolderName);
        InstalledStateFile = Path.Combine(StateRoot, InstalledStateFileName);
    }

    public string StateRoot { get; }

    public string StagingRoot { get; }

    public string PreviousRoot { get; }

    public string InstalledStateFile { get; }

    /// <summary>
    /// The installer's own default state root, given the caller's resolved %LOCALAPPDATA%.
    /// </summary>
    public static string DefaultStateRoot(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        return Path.Combine(localApplicationData, StateRootRelativePath);
    }

    /// <summary>
    /// The folder that holds one staged release. Versions are separated so a retry of an older version
    /// never mixes with a newer download.
    /// </summary>
    public string StagingDirectoryFor(SemanticVersion version) =>
        EnsureUnderStateRoot(Path.Combine(StagingRoot, version.ToString()));

    /// <summary>
    /// Places one downloaded artifact inside a staged release. The file name is treated as untrusted:
    /// a name carrying a separator or "…\..\" is refused instead of resolved.
    /// </summary>
    public string StagedFile(SemanticVersion version, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.AsSpan().IndexOfAny('\\', '/') >= 0 || Path.IsPathRooted(fileName))
        {
            throw new ArgumentException(
                $"A staged artifact name must be a bare file name: '{fileName}'.",
                nameof(fileName));
        }

        return EnsureUnderStateRoot(Path.Combine(StagingDirectoryFor(version), fileName));
    }

    /// <summary>
    /// True when the candidate resolves to the state root itself or to something inside it.
    /// </summary>
    public bool IsUnderStateRoot(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathRooted(candidate))
        {
            return false;
        }

        string resolved = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (string.Equals(resolved, StateRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = StateRoot + Path.DirectorySeparatorChar;
        return resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the resolved path, or throws naming the path that escaped the state root. Callers use
    /// this immediately before writing, so a path that escapes is never handed to the filesystem.
    /// </summary>
    public string EnsureUnderStateRoot(string candidate)
    {
        if (!IsUnderStateRoot(candidate))
        {
            throw new ArgumentException(
                $"The updater refuses to use '{candidate}': it is not under the state root '{StateRoot}'.",
                nameof(candidate));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
    }
}
