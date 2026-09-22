// Purpose: Provide the single shared "WMP Custom Tools" Inventor ribbon tab used by every WMP add-in.
// Inputs: A live Inventor Application, the target ribbon name, and the calling add-in's own ClientId.
// Outputs: The existing or newly created shared RibbonTab, and per-caller RibbonPanel, COM objects.
// Dependencies: Inventor 2027 interop v31, conditionally compiled when its installed assembly exists.
// Assumptions: Called on Inventor's owning STA thread, typically from each add-in's own
//   Activate(FirstTime). The tab is shared: whichever add-in activates first creates it, passing its
//   own ClientId as the owning client; every add-in that activates afterward finds the existing tab
//   by its internal name and adds only its own panel. No caller may assume it is the creator, and no
//   caller ever deletes the shared tab - only its own panel belongs to it.
// Validation source: Installed Autodesk.Inventor.Interop.xml (RibbonTabs.Add, RibbonTabs.Item,
//   RibbonPanels.Add, RibbonPanels.Item) and INVENTOR_2027_API_COMPATIBILITY.md. The interop XML
//   documents no exception or null-return contract for Item on a missing key, so callers must treat
//   both outcomes as "absent".

#if INVENTOR_INTEROP
using System.Runtime.InteropServices;
using Inventor;

namespace WmpRibbon;

public static class WmpRibbonTab
{
    public const string TabDisplayName = "WMP Custom Tools";
    public const string TabInternalName = "WMP.CustomTools";

    public static RibbonTab EnsureTab(Inventor.Application application, string ribbonName, string clientId)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrEmpty(ribbonName);
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        Ribbon ribbon = application.UserInterfaceManager.Ribbons[ribbonName];
        return TryGetTab(ribbon, TabInternalName)
            ?? ribbon.RibbonTabs.Add(TabDisplayName, TabInternalName, clientId);
    }

    public static RibbonPanel EnsurePanel(RibbonTab tab, string displayName, string internalName, string clientId)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentException.ThrowIfNullOrEmpty(displayName);
        ArgumentException.ThrowIfNullOrEmpty(internalName);
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        return TryGetPanel(tab, internalName)
            ?? tab.RibbonPanels.Add(displayName, internalName, clientId);
    }

    private static RibbonTab? TryGetTab(Ribbon ribbon, string internalName)
    {
        try
        {
            return ribbon.RibbonTabs[internalName];
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static RibbonPanel? TryGetPanel(RibbonTab tab, string internalName)
    {
        try
        {
            return tab.RibbonPanels[internalName];
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
#endif
