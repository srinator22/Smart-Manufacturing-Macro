// Purpose: Define what an update check and a staging run return to the UI. Every failure is a field on
//   these records, never an exception, so the dialog can always render something truthful.
// Inputs: n/a (result records).
// Outputs: Contracts consumed by WmpToolsManager.UI.
// Dependencies: WmpToolsManager.Core only.
// Assumptions: Errors is a list rather than a single string because a check can fail in two
//   independent ways at once - installed.json unreadable and the network unreachable - and hiding
//   either one would send the user to fix the wrong thing.
// Validation source: docs/decisions/0005-release-distribution-and-updater.md item 3.

using WmpToolsManager.Core;

namespace WmpToolsManager.Application;

/// <summary>
/// One plugin as the dialog lists it. Before a release is staged this is what installed.json knows;
/// afterwards it is the richer record from the staged catalog.json.
/// </summary>
public sealed record PluginSummary(string Id, string DisplayName, string Description, string Maturity)
{
    /// <summary>Qualified because this record's own <see cref="Maturity"/> property shadows the type.</summary>
    public string MaturityDescription => Core.Maturity.Describe(Maturity);

    public static PluginSummary FromInstalled(InstalledPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        // installed.json records only id and maturity per plugin; the display name lives in the
        // release catalog, which is not on disk after an install. The id is shown rather than a
        // fabricated title.
        return new PluginSummary(plugin.Id, plugin.Id, string.Empty, plugin.Maturity);
    }

    public static PluginSummary FromCatalog(CatalogPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        return new PluginSummary(
            plugin.Id,
            plugin.DisplayName.Length == 0 ? plugin.Id : plugin.DisplayName,
            plugin.Description,
            plugin.Maturity);
    }
}

/// <summary>
/// The answer to "is there a new version?". <see cref="Sha256SumsContent"/> is carried so staging
/// writes the digest file it already verified against, instead of downloading it a second time and
/// racing a release published in between.
/// </summary>
public sealed record UpdateCheck(
    UpdateDecision Decision,
    SemanticVersion? InstalledVersion,
    SemanticVersion? LatestVersion,
    string? PackageFileName,
    string? Sha256SumsContent,
    string NotesExcerpt,
    IReadOnlyList<PluginSummary> Plugins,
    bool HasPreviousInstall,
    IReadOnlyList<string> Errors,
    PendingApply? PendingApply = null)
{
    /// <summary>
    /// Whether an apply this dialog already launched is still waiting for Inventor to exit. It is
    /// deliberately not folded into <see cref="CanStage"/>: staging refuses for a different reason and
    /// has to say so in different words.
    /// </summary>
    public bool HasPendingApply => PendingApply is not null;

    public bool CanStage =>
        Decision == UpdateDecision.UpdateAvailable
        && LatestVersion is not null
        && !string.IsNullOrEmpty(PackageFileName)
        && !string.IsNullOrEmpty(Sha256SumsContent);

    public string Summary => UpdateDecider.Describe(Decision, InstalledVersion, LatestVersion);
}

/// <summary>
/// The outcome of downloading and verifying a release into the state root. Nothing has been applied at
/// this point: Inventor still holds the installed assemblies.
/// </summary>
public sealed record StageResult(
    bool Verified,
    SemanticVersion? Version,
    string? PackageZipPath,
    string? Sha256SumsPath,
    string? InstallerScriptPath,
    IReadOnlyList<PluginSummary> Plugins,
    string Message,
    IReadOnlyList<string> Errors)
{
    public static StageResult Failed(string message, IReadOnlyList<string> errors) =>
        new(false, null, null, null, null, [], message, errors);
}
