// Purpose: Convert a ribbon icon bitmap into the OLE picture Inventor's ButtonDefinition expects, as an
//   icon-type picture so per-pixel alpha and anti-aliased edges survive.
// Inputs: A 32bpp ARGB System.Drawing.Bitmap owned by the caller.
// Outputs: An IPictureDisp dispatch object that owns its own HICON.
// Dependencies: oleaut32 OleCreatePictureIndirect; System.Drawing Bitmap.GetHicon.
// Assumptions: Called on Inventor's owning STA thread. The picture owns the HICON (fOwn = true), so the
//   source bitmap may be disposed once this returns.
// Validation source: Autodesk developer blog "Resolving Ribbon Icon(.bmp) Rendering Issues in Autodesk
//   Inventor Dark Theme" - AxHost.GetIPictureDispFromPicture yields PICTYPE_BITMAP, which Inventor
//   re-converts to an icon and loses transparency; PICTYPE_ICON avoids that conversion. Win32 PICTDESC
//   layout from oleaut32 documentation (x64: 4 + 4 + 16-byte union = 24 bytes).

#if INVENTOR_INTEROP
using System.Drawing;
using System.Runtime.InteropServices;

namespace WmpRibbon;

public static class RibbonPicture
{
    private const int PictypeIcon = 3;
    private static readonly Guid IPictureDispGuid = new("7BF80981-BF32-101A-8BBB-00AA00300CAB");

    public static object FromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        IntPtr iconHandle = bitmap.GetHicon();
        PictDescIcon description = new()
        {
            cbSizeofstruct = Marshal.SizeOf<PictDescIcon>(),
            picType = PictypeIcon,
            hicon = iconHandle,
        };
        Guid interfaceId = IPictureDispGuid;
        int result = NativeMethods.OleCreatePictureIndirect(ref description, ref interfaceId, true, out object? picture);
        if (result < 0 || picture is null)
        {
            _ = NativeMethods.DestroyIcon(iconHandle);
            throw new InvalidOperationException(
                $"OleCreatePictureIndirect could not create the ribbon icon picture (HRESULT 0x{result:X8}).");
        }

        return picture;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PictDescIcon
    {
        public int cbSizeofstruct;
        public int picType;
        public IntPtr hicon;
        public IntPtr unionPadding;
    }

    private static class NativeMethods
    {
        [DllImport("oleaut32.dll", PreserveSig = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int OleCreatePictureIndirect(
            ref PictDescIcon pictDesc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Bool)] bool fOwn,
            [MarshalAs(UnmanagedType.IDispatch)] out object? ppvObj);

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);
    }
}
#endif
