// Purpose: Supply Inventor 2027's COM add-in entry point and the Assembly ribbon boundary.
// Inputs: Inventor activation lifecycle and retained button OnExecute events.
// Outputs: One Smart Export ribbon command delegating to SmartExportCommand.
// Dependencies: Inventor interop, Phase 1 adapters, workflow, and WPF host.
// Assumptions: Inventor owns activation and callback threading; firstTime controls UI creation.
// Validation source: Installed Inventor 2027 interop contracts and Autodesk C# add-in template.

#if INVENTOR_INTEROP
using System.Runtime.InteropServices;
using Inventor;
using SmartManufacturingExporter.AddIn.Phase1;
using SmartManufacturingExporter.Application.Phase1;
using SmartManufacturingExporter.Infrastructure.Phase1;
using SmartManufacturingExporter.InventorAdapter.Phase1;

namespace SmartManufacturingExporter.AddIn;

[ComVisible(true)]
[Guid(ServerGuid)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class StandardAddInServer : ApplicationAddInServer
{
    public const string ServerGuid = "A77D6A17-82A7-45C9-93C6-E6FA4EB91E73";
    public const string ClientId = "{A77D6A17-82A7-45C9-93C6-E6FA4EB91E73}";

    private const string ButtonInternalName = "SmartManufacturingExporter.SmartExportButton";
    private const string TabInternalName = "SmartManufacturingExporter.SmartExportTab";
    private const string PanelInternalName = "SmartManufacturingExporter.SmartExportPanel";

    private Inventor.Application? inventorApplication;
    private ButtonDefinition? buttonDefinition;
    private ButtonDefinitionSink_OnExecuteEventHandler? onExecuteHandler;
    private SmartExportCommand? smartExportCommand;

    public object? Automation => null;

    public void Activate(ApplicationAddInSite AddInSiteObject, bool FirstTime)
    {
        ArgumentNullException.ThrowIfNull(AddInSiteObject);

        inventorApplication = AddInSiteObject.Application;
        PhysicalPhase1FileSystem fileSystem = new();
        InventorPhase1Gateway gateway = new(inventorApplication);
        SmartExportWorkflow workflow = new(gateway, fileSystem);
        smartExportCommand = new(inventorApplication, workflow);

        buttonDefinition = inventorApplication.CommandManager.ControlDefinitions.AddButtonDefinition(
            "Smart Export",
            ButtonInternalName,
            CommandTypesEnum.kFileOperationsCmdType,
            ClientId,
            "Export selected top-level parts as STEP files.",
            "Review top-level parts and export selected files as STEP.");
        onExecuteHandler = OnSmartExportExecute;
        buttonDefinition.OnExecute += onExecuteHandler;

        if (FirstTime)
        {
            Ribbon assemblyRibbon = inventorApplication.UserInterfaceManager.Ribbons["Assembly"];
            RibbonTab tab = assemblyRibbon.RibbonTabs.Add(
                "Smart Export",
                TabInternalName,
                ClientId);
            RibbonPanel panel = tab.RibbonPanels.Add(
                "Smart Export",
                PanelInternalName,
                ClientId);
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
        inventorApplication = null;
    }

    public void ExecuteCommand(int CommandID)
    {
    }

    private void OnSmartExportExecute(NameValueMap context) => smartExportCommand?.Execute();
}
#endif
