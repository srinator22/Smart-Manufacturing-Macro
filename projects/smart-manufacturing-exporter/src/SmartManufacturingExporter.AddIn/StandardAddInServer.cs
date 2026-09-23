// Purpose: Supply Inventor 2027's COM add-in entry point; the Smart Export command lives on the
//   shared WMP Custom Tools ribbon tab rather than an exporter-owned tab.
// Inputs: Inventor activation lifecycle and retained button OnExecute events.
// Outputs: One Smart Export ribbon command delegating to SmartExportCommand.
// Dependencies: Inventor interop, Phase 1 adapters, workflow, WPF host, and the shared WmpRibbon tab.
// Assumptions: Inventor owns activation and callback threading. The tab, panel and buttons are ensured on
//   every Activate, not only when FirstTime is true, so a ribbon rebuild or another add-in's uninstall cannot
//   strand this panel; FirstTime is therefore unused.
// Validation source: Installed Inventor 2027 interop contracts and Autodesk C# add-in template.

#if INVENTOR_INTEROP
using System.Runtime.InteropServices;
using Inventor;
using SmartManufacturingExporter.AddIn.Phase1;
using SmartManufacturingExporter.Application.Phase1;
using SmartManufacturingExporter.Infrastructure.Phase1;
using SmartManufacturingExporter.InventorAdapter.Phase1;
using WmpRibbon;

namespace SmartManufacturingExporter.AddIn;

[ComVisible(true)]
[Guid(ServerGuid)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class StandardAddInServer : ApplicationAddInServer
{
    public const string ServerGuid = "A77D6A17-82A7-45C9-93C6-E6FA4EB91E73";
    public const string ClientId = "{A77D6A17-82A7-45C9-93C6-E6FA4EB91E73}";

    private const string ButtonInternalName = "SmartManufacturingExporter.SmartExportButton";
    private const string PanelInternalName = "SmartManufacturingExporter.SmartExportPanel";

    private Inventor.Application? inventorApplication;
    private ButtonDefinition? buttonDefinition;
    private ButtonDefinitionSink_OnExecuteEventHandler? onExecuteHandler;
    private SmartExportCommand? smartExportCommand;
    private object? standardIcon;
    private object? largeIcon;

    public object? Automation => null;

    public void Activate(ApplicationAddInSite AddInSiteObject, bool FirstTime)
    {
        ArgumentNullException.ThrowIfNull(AddInSiteObject);

        inventorApplication = AddInSiteObject.Application;
        PhysicalPhase1FileSystem fileSystem = new();
        InventorPhase1Gateway gateway = new(inventorApplication);
        SmartExportWorkflow workflow = new(gateway, fileSystem);
        smartExportCommand = new(inventorApplication, workflow);

        (standardIcon, largeIcon) = RibbonIcons.Load(inventorApplication, typeof(StandardAddInServer).Assembly, "SmartManufacturingExporter.AddIn", "smart-export");

        buttonDefinition = inventorApplication.CommandManager.ControlDefinitions.AddButtonDefinition(
            "Smart Export",
            ButtonInternalName,
            CommandTypesEnum.kFileOperationsCmdType,
            ClientId,
            "Export selected top-level parts as STEP files.",
            "Review top-level parts and export selected files as STEP.",
            standardIcon,
            largeIcon);
        onExecuteHandler = OnSmartExportExecute;
        buttonDefinition.OnExecute += onExecuteHandler;

        RibbonTab tab = WmpRibbonTab.EnsureTab(inventorApplication, "Assembly", ClientId);
        RibbonPanel panel = WmpRibbonTab.EnsurePanel(tab, "Smart Export", PanelInternalName, ClientId);
        if (!WmpRibbonTab.ContainsControl(panel, ButtonInternalName))
        {
            panel.CommandControls.AddButton(buttonDefinition, true, true);
        }
    }

    public void Deactivate()
    {
        if (buttonDefinition is not null && onExecuteHandler is not null)
        {
            buttonDefinition.OnExecute -= onExecuteHandler;
        }

        onExecuteHandler = null;
        buttonDefinition = null;
        smartExportCommand = null;
        standardIcon = null;
        largeIcon = null;
        inventorApplication = null;
    }

    public void ExecuteCommand(int CommandID)
    {
    }

    private void OnSmartExportExecute(NameValueMap context) => smartExportCommand?.Execute();
}
#endif
