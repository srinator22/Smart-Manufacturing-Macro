// Purpose: Turn the installed version and the published version into the one statement the update
//   dialog is allowed to make.
// Inputs: The version recorded in installed.json and the version read from the release's SHA256SUMS.txt.
// Outputs: A decision that never guesses: an unreadable version on either side is Unknown, not "up to date".
// Dependencies: .NET base types only.
// Assumptions: The workspace releases as one version (ADR-0005 item 4), so a single comparison decides
//   for every plugin at once. InstalledNewer is reachable in normal use: a developer who ran
//   install-addin.ps1 from a working tree can hold a build that is ahead of the newest published release.
// Validation source: docs/decisions/0005-release-distribution-and-updater.md.

namespace WmpToolsManager.Core;

public enum UpdateDecision
{
    /// <summary>Either side could not be read, so no claim is made.</summary>
    Unknown,

    /// <summary>The installed version equals the published version.</summary>
    UpToDate,

    /// <summary>The published version is higher than the installed version.</summary>
    UpdateAvailable,

    /// <summary>The installed version is higher than the published version.</summary>
    InstalledNewer,
}

public static class UpdateDecider
{
    public static UpdateDecision Decide(SemanticVersion? installed, SemanticVersion? latest)
    {
        if (installed is not { } installedVersion || latest is not { } latestVersion)
        {
            return UpdateDecision.Unknown;
        }

        int comparison = installedVersion.CompareTo(latestVersion);
        return comparison switch
        {
            0 => UpdateDecision.UpToDate,
            < 0 => UpdateDecision.UpdateAvailable,
            _ => UpdateDecision.InstalledNewer,
        };
    }

    /// <summary>
    /// The sentence the dialog shows for a decision. The text names both versions so a screenshot of the
    /// window is enough to diagnose a wrong answer.
    /// </summary>
    public static string Describe(UpdateDecision decision, SemanticVersion? installed, SemanticVersion? latest)
    {
        string installedText = installed?.ToString() ?? "unknown";
        string latestText = latest?.ToString() ?? "unknown";

        return decision switch
        {
            UpdateDecision.UpToDate => $"WMP Inventor Tools {installedText} is the latest release.",
            UpdateDecision.UpdateAvailable => $"WMP Inventor Tools {latestText} is available. You have {installedText}.",
            UpdateDecision.InstalledNewer =>
                $"The installed build {installedText} is ahead of the latest release {latestText}; there is nothing to update to.",
            _ => $"The update state could not be determined. Installed: {installedText}. Latest: {latestText}.",
        };
    }
}
