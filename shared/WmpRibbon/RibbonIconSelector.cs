// Purpose: Decide which pre-rendered ribbon icon variant an add-in embeds and hands to Inventor, from
//   Inventor's active theme name and the DPI of the window the ribbon is drawn in.
// Inputs: A theme name as reported by Inventor's ThemeManager, a DPI value, an assembly resource prefix,
//   and an icon name.
// Outputs: The theme variant, the small and large pixel sizes, and the embedded resource name.
// Dependencies: None; deliberately compiled without the Inventor interop so CI can test it.
// Assumptions: Inventor resizes every ribbon image to 16 or 32 px times the window scale, so supplying
//   the smallest rendered size at or above that target avoids an upscale; a larger source is shrunk,
//   which stays sharper than stretching a smaller one. Any theme name that is not recognizably light
//   falls back to the dark variant, whose light-grey bodies are Inventor's default look.
// Validation source: live Inventor 2027 spike on 2026-09-23 (a 40 px and a 32 px source rendered at the
//   same on-screen size at 125%), recorded in .work/done for the ribbon-icon-restyle task.

namespace WmpRibbon;

public enum RibbonTheme
{
    Dark,
    Light,
}

public static class RibbonIconSelector
{
    public const int DefaultDpi = 96;

    public static IReadOnlyList<int> SmallSizes { get; } = [16, 20, 24, 32];

    public static IReadOnlyList<int> LargeSizes { get; } = [32, 40, 48, 64];

    public static IReadOnlyList<RibbonTheme> Themes { get; } = [RibbonTheme.Dark, RibbonTheme.Light];

    public static RibbonTheme ThemeFromName(string? themeName) =>
        themeName is not null && themeName.Contains("light", StringComparison.OrdinalIgnoreCase)
            ? RibbonTheme.Light
            : RibbonTheme.Dark;

    public static int SmallSize(int dpi) => Pick(SmallSizes, 16, dpi);

    public static int LargeSize(int dpi) => Pick(LargeSizes, 32, dpi);

    public static string ThemeSuffix(RibbonTheme theme) => theme switch
    {
        RibbonTheme.Dark => "dark",
        RibbonTheme.Light => "light",
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "Unknown ribbon theme."),
    };

    public static string FileName(string iconName, RibbonTheme theme, int size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iconName);
        if (!SmallSizes.Contains(size) && !LargeSizes.Contains(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "No ribbon icon is rendered at this size.");
        }

        return $"{iconName}-{ThemeSuffix(theme)}-{size}.png";
    }

    public static string ResourceName(string resourcePrefix, string iconName, RibbonTheme theme, int size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourcePrefix);
        return $"{resourcePrefix}.Ribbon.{FileName(iconName, theme, size)}";
    }

    private static int Pick(IReadOnlyList<int> sizes, int baseSize, int dpi)
    {
        // A failed DPI query reports 0; anything at or below 100% uses the unscaled size.
        if (dpi <= DefaultDpi)
        {
            return sizes[0];
        }

        int target = ((baseSize * dpi) + DefaultDpi - 1) / DefaultDpi;
        foreach (int size in sizes)
        {
            if (size >= target)
            {
                return size;
            }
        }

        return sizes[^1];
    }
}
