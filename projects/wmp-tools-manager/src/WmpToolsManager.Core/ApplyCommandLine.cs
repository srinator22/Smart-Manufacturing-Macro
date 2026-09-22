// Purpose: Build the exact command line that hands the apply and rollback steps to the published
//   PowerShell installer. ADR-0005 forbids the add-in from copying anything into Inventor's Addins
//   folder itself: Inventor locks its own add-in assemblies while it runs, so the only correct actor is
//   an out-of-process script that waits for Inventor to exit.
// Inputs: Absolute paths already validated against the state root, plus the add-ins root.
// Outputs: A -File argument string for powershell.exe. No process is started here.
// Dependencies: .NET base types only. No IO, no process creation.
// Assumptions: powershell.exe parses -File arguments with its own quoting rules, under which an
//   embedded double quote cannot be passed reliably. A path containing one is refused rather than
//   escaped, because a mis-escaped path would silently target the wrong folder. Windows forbids '"' in
//   a path, so this refusal is unreachable for a real path and exists to keep it that way.
// Validation source: scripts/release/Install-WmpInventorTools.ps1 parameter block
//   (-ZipPath, -Sha256SumsPath, -AddinsRoot, -StateRoot, -Rollback, -WaitForInventor).

namespace WmpToolsManager.Core;

public static class ApplyCommandLine
{
    /// <summary>
    /// Windows PowerShell 5.1, which every Windows 11 machine has. The installer declares 5.1 as its
    /// floor, so the in-box host is used rather than depending on pwsh being installed.
    /// </summary>
    public const string ExecutableFileName = "powershell.exe";

    /// <summary>The installer script name, as published beside the package and listed in SHA256SUMS.txt.</summary>
    public const string InstallerFileName = Sha256SumsFile.InstallerFileName;

    private const string HostArguments = "-NoProfile -ExecutionPolicy Bypass -File";

    /// <summary>
    /// Applies a staged release: verify the zip again, wait for every Inventor process to exit, archive
    /// the current install, and copy the new one into place.
    /// </summary>
    public static string BuildApplyArguments(
        string installerScriptPath,
        string zipPath,
        string sha256SumsPath,
        string addinsRoot,
        string stateRoot) =>
        string.Concat(
            HostArguments,
            Quote(installerScriptPath, nameof(installerScriptPath)),
            " -ZipPath",
            Quote(zipPath, nameof(zipPath)),
            " -Sha256SumsPath",
            Quote(sha256SumsPath, nameof(sha256SumsPath)),
            " -WaitForInventor -AddinsRoot",
            Quote(addinsRoot, nameof(addinsRoot)),
            " -StateRoot",
            Quote(stateRoot, nameof(stateRoot)));

    /// <summary>
    /// Restores the most recently archived install. The installer archives rather than deletes, so this
    /// is a folder swap and the replaced install is itself kept.
    /// </summary>
    public static string BuildRollbackArguments(string installerScriptPath, string addinsRoot, string stateRoot) =>
        string.Concat(
            HostArguments,
            Quote(installerScriptPath, nameof(installerScriptPath)),
            " -Rollback -WaitForInventor -AddinsRoot",
            Quote(addinsRoot, nameof(addinsRoot)),
            " -StateRoot",
            Quote(stateRoot, nameof(stateRoot)));

    private static string Quote(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A path passed to {ExecutableFileName} must not contain a double quote: '{value}'.",
                parameterName);
        }

        return $" \"{value}\"";
    }
}
