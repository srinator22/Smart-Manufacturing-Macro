// Purpose: The theme palette that fills the double-brace slots in every ribbon icon master.
// Inputs: A ribbon theme and a slot name.
// Outputs: A XAML color literal, or null for a slot that does not exist.
// Dependencies: WmpRibbon.RibbonTheme.
// Assumptions: Dark values follow Inventor 2027's dark-theme icons (light-grey bodies, sky-blue and
//   soft-gold accents); light values keep the same accents at higher saturation and darken the bodies
//   so they read on the white light-theme ribbon. glass is translucent accent (#AARRGGBB).
// Validation source: colors sampled from Inventor 2027 dark-theme ribbon screenshots on 2026-09-23;
//   the ribbon backgrounds are sampled from the same screenshots and used only by --preview.

using System.Windows.Media;
using WmpRibbon;

namespace WmpIconRenderer;

internal static class Palette
{
    private static readonly Dictionary<string, string> Dark = new(StringComparer.Ordinal)
    {
        ["body"] = "#FFD8D8D8",
        ["shade"] = "#FFA9B2BE",
        ["detail"] = "#FF5A6678",
        ["accent"] = "#FF78C4F8",
        ["glass"] = "#5978C4F8",
        ["gold"] = "#FFF8D080",
    };

    private static readonly Dictionary<string, string> Light = new(StringComparer.Ordinal)
    {
        ["body"] = "#FF5E6773",
        ["shade"] = "#FF8A94A1",
        ["detail"] = "#FFE8ECF0",
        ["accent"] = "#FF1C8BD6",
        ["glass"] = "#401C8BD6",
        ["gold"] = "#FFE0A526",
    };

    public static string? Color(RibbonTheme theme, string slot) =>
        (theme == RibbonTheme.Light ? Light : Dark).GetValueOrDefault(slot);

    public static Color RibbonBackground(RibbonTheme theme) =>
        theme == RibbonTheme.Light ? System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5) : System.Windows.Media.Color.FromRgb(0x3C, 0x44, 0x52);
}
