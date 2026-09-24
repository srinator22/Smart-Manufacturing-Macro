// Purpose: Load an add-in's pre-rendered ribbon icon pair for Inventor's active theme and the DPI of
//   Inventor's main window, as PICTYPE_ICON pictures ready for AddButtonDefinition.
// Inputs: A live Inventor Application, the assembly that embeds the PNGs, its resource prefix, and the
//   icon name shared by every rendered variant.
// Outputs: The standard (small) and large IPictureDisp objects.
// Dependencies: Inventor ThemeManager and MainFrameHWND, user32 DPI queries, RibbonIconSelector for
//   the variant decision, RibbonPicture for the conversion.
// Assumptions: Called from Activate on Inventor's owning STA thread. The theme and DPI are read once;
//   a theme or monitor change takes effect on the next Inventor start. A failed theme or window-DPI
//   query falls back to the dark theme and the system DPI rather than failing activation.
// Validation source: Installed Autodesk.Inventor.Interop.xml (Application.ThemeManager,
//   ThemeManager.ActiveTheme, Theme.Name, Application.MainFrameHWND); Win32 GetDpiForWindow and
//   GetDpiForSystem documentation (0 means the query failed).

#if INVENTOR_INTEROP
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WmpRibbon;

public static class RibbonIcons
{
    public static (object Standard, object Large) Load(
        Inventor.Application application,
        Assembly resourceAssembly,
        string resourcePrefix,
        string iconName)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(resourceAssembly);

        RibbonTheme theme = RibbonIconSelector.ThemeFromName(ReadThemeName(application));
        int dpi = ReadWindowDpi(application);
        object standard = LoadPicture(resourceAssembly, resourcePrefix, iconName, theme, RibbonIconSelector.SmallSize(dpi));
        object large = LoadPicture(resourceAssembly, resourcePrefix, iconName, theme, RibbonIconSelector.LargeSize(dpi));
        return (standard, large);
    }

    private static object LoadPicture(Assembly assembly, string prefix, string iconName, RibbonTheme theme, int size)
    {
        string resourceName = RibbonIconSelector.ResourceName(prefix, iconName, theme, size);
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded ribbon icon was not found: {resourceName}.");
        using Bitmap decoded = new(stream);
        if (decoded.Width != size || decoded.Height != size)
        {
            throw new InvalidOperationException(
                $"Embedded ribbon icon {resourceName} is {decoded.Width}x{decoded.Height}; expected {size}x{size}.");
        }

        using Bitmap argb = decoded.Clone(new Rectangle(0, 0, size, size), PixelFormat.Format32bppArgb);
        return RibbonPicture.FromBitmap(argb);
    }

    private static string? ReadThemeName(Inventor.Application application)
    {
        try
        {
            // The interop documents no null contract for either property, so a null is treated like
            // a failed query rather than allowed to throw out of Activate.
            return application.ThemeManager?.ActiveTheme?.Name;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static int ReadWindowDpi(Inventor.Application application)
    {
        uint dpi = 0;
        try
        {
            dpi = NativeMethods.GetDpiForWindow(new IntPtr(application.MainFrameHWND));
        }
        catch (COMException)
        {
            dpi = 0;
        }

        if (dpi == 0)
        {
            dpi = NativeMethods.GetDpiForSystem();
        }

        return dpi == 0 ? RibbonIconSelector.DefaultDpi : (int)dpi;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint GetDpiForSystem();
    }
}
#endif
