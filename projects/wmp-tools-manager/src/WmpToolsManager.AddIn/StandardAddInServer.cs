// Purpose: Supply Inventor 2027's COM add-in entry point for WMP Tools Manager; the Check for updates
//   command lives on the shared WMP Custom Tools ribbon tab, in this add-in's own WMP Tools panel.
// Inputs: Inventor activation lifecycle and the retained button's OnExecute event.
// Outputs: One ribbon command that opens the update window, composed over the real release source,
//   install state, hash verifier and process launcher.
// Dependencies: Inventor interop, Infrastructure/Application/UI, and the shared WmpRibbon tab.
// Assumptions: Inventor owns activation and callback threading; FirstTime controls UI creation. The
//   HttpClient is created once per session and disposed on Deactivate, because a new client per check
//   would leak sockets. The add-in writes nothing into the Addins folder itself: it stages under
//   %LOCALAPPDATA% and hands the apply step to a PowerShell process that outlives the host.
// Validation source: Installed Inventor 2027 interop contracts, Autodesk's C# add-in template, and
//   docs/decisions/0005-release-distribution-and-updater.md.

#if INVENTOR_INTEROP
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using Inventor;
using WmpRibbon;
using WmpToolsManager.AddIn.Commands;
using WmpToolsManager.AddIn.Interop;
using WmpToolsManager.Infrastructure;

namespace WmpToolsManager.AddIn;

[ComVisible(true)]
[Guid(ServerGuid)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class StandardAddInServer : ApplicationAddInServer
{
    public const string ServerGuid = "39625833-F960-4BDE-9EB1-F1E7F8F8013B";
    public const string ClientId = "{39625833-F960-4BDE-9EB1-F1E7F8F8013B}";

    private const string PanelDisplayName = "WMP Tools";
    private const string PanelInternalName = "WmpToolsManager.ToolsPanel";
    private const string CheckForUpdatesInternalName = "WmpToolsManager.CheckForUpdatesButton";
    private const string CheckForUpdatesDisplayName = "Check for updates";
    private const string CheckForUpdatesTooltip =
        "Check GitHub for a newer WMP Inventor Tools release, verify it, and apply it once Inventor closes.";

    private Inventor.Application? inventorApplication;
    private HttpClient? httpClient;
    private ButtonDefinition? checkForUpdatesButtonDefinition;
    private ButtonDefinitionSink_OnExecuteEventHandler? onCheckForUpdatesExecuteHandler;
    private CheckForUpdatesCommand? checkForUpdatesCommand;
    private Bitmap? standardIconBitmap;
    private Bitmap? largeIconBitmap;
    private object? standardIcon;
    private object? largeIcon;

    public object? Automation => null;

    public void Activate(ApplicationAddInSite AddInSiteObject, bool FirstTime)
    {
        ArgumentNullException.ThrowIfNull(AddInSiteObject);

        inventorApplication = AddInSiteObject.Application;
        httpClient = GitHubReleaseSource.CreateHttpClient();
        checkForUpdatesCommand = new(inventorApplication, UpdateComposition.Create(httpClient));

        standardIconBitmap = LoadRibbonIcon("tools-manager-16.png");
        largeIconBitmap = LoadRibbonIcon("tools-manager-32.png");
        standardIcon = PictureDispConverter.ToPictureDisp(standardIconBitmap);
        largeIcon = PictureDispConverter.ToPictureDisp(largeIconBitmap);

        string tooltip = PluginDescriptor.DescribeCommand(CheckForUpdatesTooltip);
        checkForUpdatesButtonDefinition = inventorApplication.CommandManager.ControlDefinitions.AddButtonDefinition(
            CheckForUpdatesDisplayName,
            CheckForUpdatesInternalName,
            CommandTypesEnum.kFileOperationsCmdType,
            ClientId,
            tooltip,
            tooltip,
            standardIcon,
            largeIcon);
        onCheckForUpdatesExecuteHandler = OnCheckForUpdatesExecute;
        checkForUpdatesButtonDefinition.OnExecute += onCheckForUpdatesExecuteHandler;

        if (FirstTime)
        {
            RibbonTab tab = WmpRibbonTab.EnsureTab(inventorApplication, "Assembly", ClientId);
            RibbonPanel panel = WmpRibbonTab.EnsurePanel(tab, PanelDisplayName, PanelInternalName, ClientId);
            panel.CommandControls.AddButton(checkForUpdatesButtonDefinition, true, true);
        }
    }

    public void Deactivate()
    {
        if (checkForUpdatesButtonDefinition is not null && onCheckForUpdatesExecuteHandler is not null)
        {
            checkForUpdatesButtonDefinition.OnExecute -= onCheckForUpdatesExecuteHandler;
        }

        onCheckForUpdatesExecuteHandler = null;
        checkForUpdatesButtonDefinition = null;
        checkForUpdatesCommand = null;
        standardIcon = null;
        largeIcon = null;
        standardIconBitmap?.Dispose();
        standardIconBitmap = null;
        largeIconBitmap?.Dispose();
        largeIconBitmap = null;
        httpClient?.Dispose();
        httpClient = null;
        inventorApplication = null;
    }

    public void ExecuteCommand(int CommandID)
    {
    }

    private void OnCheckForUpdatesExecute(NameValueMap context) => checkForUpdatesCommand?.Execute();

    private static Bitmap LoadRibbonIcon(string fileName)
    {
        string resourceName = $"WmpToolsManager.AddIn.Assets.{fileName}";
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded ribbon icon was not found: {resourceName}.");
        using Bitmap decodedBitmap = new(stream);
        return new Bitmap(decodedBitmap);
    }
}
#endif
