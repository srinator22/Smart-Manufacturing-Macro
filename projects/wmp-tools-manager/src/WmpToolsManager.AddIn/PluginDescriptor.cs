// Purpose: Read this add-in's own plugin.json out of the assembly, so the ribbon tooltip's maturity
//   suffix comes from the catalog entry that shipped with the binary rather than from a literal that
//   could drift away from it.
// Inputs: The embedded resource WmpToolsManager.AddIn.plugin.json.
// Outputs: The declared maturity, and the tooltip suffix derived from it.
// Dependencies: WmpToolsManager.Core parsing and System.Reflection.
// Assumptions: A missing or malformed embedded manifest must not stop the add-in from loading - an
//   Inventor add-in that throws during Activate is simply absent from the ribbon. The suffix then
//   degrades to empty, which understates maturity rather than overstating it.
// Validation source: projects/wmp-tools-manager/plugin.json and the ArchitectureTests assertion that
//   the csproj embeds it under this logical name.

using System.IO;
using WmpToolsManager.Core;

namespace WmpToolsManager.AddIn;

public static class PluginDescriptor
{
    public const string ResourceName = "WmpToolsManager.AddIn.plugin.json";

    /// <summary>
    /// The maturity declared by the embedded plugin.json, or an empty string when it cannot be read.
    /// </summary>
    public static string Maturity => Read()?.Maturity ?? string.Empty;

    /// <summary>
    /// " (beta)" while this plugin is beta, and nothing once it is stable.
    /// </summary>
    public static string TooltipSuffix => Core.Maturity.TooltipSuffix(Maturity);

    /// <summary>
    /// Appends the maturity suffix to a command's tooltip text.
    /// </summary>
    public static string DescribeCommand(string tooltip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tooltip);
        return tooltip + TooltipSuffix;
    }

    private static PluginManifest? Read()
    {
        using Stream? stream = typeof(PluginDescriptor).Assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using StreamReader reader = new(stream);
        return ReleaseJson.ParsePluginManifest(reader.ReadToEnd()).Value;
    }
}
