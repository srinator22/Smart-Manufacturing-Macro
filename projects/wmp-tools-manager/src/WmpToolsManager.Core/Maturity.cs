// Purpose: State a plugin's maturity in the two places a user sees it - the ribbon tooltip suffix and
//   the plugin table in the update dialog - from one definition, so the two can never disagree.
// Inputs: The "maturity" field of a plugin.json or of a catalog.json entry.
// Outputs: A tooltip suffix and a human-readable description.
// Dependencies: .NET base types only.
// Assumptions: scripts/release/build-release.ps1 already refuses any value other than "beta" or
//   "stable", so an unrecognised value reaching here means a hand-edited catalog; it is surfaced
//   verbatim rather than silently promoted to "stable".
// Validation source: scripts/release/build-release.ps1 maturity validation and its Show-Summary text.

namespace WmpToolsManager.Core;

public static class Maturity
{
    public const string Beta = "beta";
    public const string Stable = "stable";

    /// <summary>
    /// What a beta plugin's ribbon tooltip ends with. Empty for anything else, so a stable command's
    /// tooltip reads as plain prose.
    /// </summary>
    public const string BetaTooltipSuffix = " (beta)";

    public static bool IsBeta(string? maturity) =>
        string.Equals(maturity?.Trim(), Beta, StringComparison.OrdinalIgnoreCase);

    public static string TooltipSuffix(string? maturity) => IsBeta(maturity) ? BetaTooltipSuffix : string.Empty;

    /// <summary>
    /// The wording the installer's own summary table uses, so the dialog and the console agree.
    /// </summary>
    public static string Describe(string? maturity)
    {
        string trimmed = maturity?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return "unknown";
        }

        return IsBeta(trimmed) ? "beta - not yet validated in live Inventor" : trimmed;
    }
}
