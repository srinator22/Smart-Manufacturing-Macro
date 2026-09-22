// Purpose: Convert retained managed ribbon bitmaps into the COM picture type Inventor expects.
// Inputs: A non-null System.Drawing.Image whose lifetime is owned by the add-in server.
// Outputs: An OLE picture dispatch object suitable for Inventor command definitions.
// Dependencies: Windows Forms AxHost COM picture conversion at the AddIn host boundary.
// Assumptions: Calls occur on Inventor's owning STA thread and the source image remains alive.
// Validation source: Inventor 2027 ControlDefinitions.AddButtonDefinition interop contract.

#if INVENTOR_INTEROP
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace WmpToolsManager.AddIn.Interop;

[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Inheritance exposes AxHost's protected COM picture conversion method.")]
internal sealed class PictureDispConverter : AxHost
{
    private PictureDispConverter()
        : base(string.Empty)
    {
    }

    public static object ToPictureDisp(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return GetIPictureDispFromPicture(image)
            ?? throw new InvalidOperationException("Windows Forms could not create an OLE picture for the ribbon icon.");
    }
}
#endif
