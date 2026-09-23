// Purpose: Supply Inventor 2027's COM add-in entry point; the Analyze Naming and Apply Naming commands
//   live on the shared WMP Custom Tools ribbon tab rather than a File Naming Manager-owned tab - this
//   add-in owns only its own panel on that shared tab.
// Inputs: Inventor activation lifecycle and retained button OnExecute events.
// Outputs: Two File Naming ribbon commands delegating to FileNamingCommand.
// Dependencies: Inventor interop, Infrastructure/InventorAdapter/Application/UI, and the shared WmpRibbon tab.
// Assumptions: Inventor owns activation and callback threading. The tab, panel and buttons are ensured on
//   every Activate, not only when FirstTime is true, so a ribbon rebuild or another add-in's uninstall cannot
//   strand this panel; FirstTime is therefore unused.
// Validation source: Installed Inventor 2027 interop contracts and Autodesk C# add-in template.

#if INVENTOR_INTEROP
using System.Runtime.InteropServices;
using FileNamingManager.AddIn.Commands;
using FileNamingManager.Application;
using FileNamingManager.Infrastructure;
using FileNamingManager.InventorAdapter;
using FileNamingManager.UI;
using Inventor;
using WmpRibbon;

namespace FileNamingManager.AddIn;

[ComVisible(true)]
[Guid(ServerGuid)]
[ClassInterface(ClassInterfaceType.None)]
public sealed class StandardAddInServer : ApplicationAddInServer
{
    public const string ServerGuid = "BB7F1BFF-D45E-440A-897B-F70E6A4ADE67";
    public const string ClientId = "{BB7F1BFF-D45E-440A-897B-F70E6A4ADE67}";

    private const string PanelInternalName = "FileNamingManager.FileNamingPanel";
    private const string AnalyzeButtonInternalName = "FileNamingManager.AnalyzeNamingButton";
    private const string ApplyButtonInternalName = "FileNamingManager.ApplyNamingButton";

    private Inventor.Application? inventorApplication;
    private ButtonDefinition? analyzeButtonDefinition;
    private ButtonDefinition? applyButtonDefinition;
    private ButtonDefinitionSink_OnExecuteEventHandler? onAnalyzeExecuteHandler;
    private ButtonDefinitionSink_OnExecuteEventHandler? onApplyExecuteHandler;
    private FileNamingCommand? fileNamingCommand;
    private object? analyzeStandardIcon;
    private object? analyzeLargeIcon;
    private object? applyStandardIcon;
    private object? applyLargeIcon;

    public object? Automation => null;

    public void Activate(ApplicationAddInSite AddInSiteObject, bool FirstTime)
    {
        ArgumentNullException.ThrowIfNull(AddInSiteObject);

        inventorApplication = AddInSiteObject.Application;
        PhysicalNamingFileSystem fileSystem = new();
        SystemClock clock = new();
        InventorNamingGateway gateway = new(inventorApplication);
        FileNamingWorkflow workflow = new(gateway, fileSystem, clock);
        fileNamingCommand = new(inventorApplication, workflow, gateway);

        (analyzeStandardIcon, analyzeLargeIcon) = RibbonIcons.Load(inventorApplication, typeof(StandardAddInServer).Assembly, "FileNamingManager.AddIn", "analyze-naming");
        (applyStandardIcon, applyLargeIcon) = RibbonIcons.Load(inventorApplication, typeof(StandardAddInServer).Assembly, "FileNamingManager.AddIn", "apply-naming");

        analyzeButtonDefinition = inventorApplication.CommandManager.ControlDefinitions.AddButtonDefinition(
            "Analyze Naming",
            AnalyzeButtonInternalName,
            CommandTypesEnum.kFileOperationsCmdType,
            ClientId,
            "Analyze the active assembly's file names against the WMP numbering scheme.",
            "Analyze the active assembly's file names against the WMP numbering scheme.",
            analyzeStandardIcon,
            analyzeLargeIcon);
        onAnalyzeExecuteHandler = OnAnalyzeNamingExecute;
        analyzeButtonDefinition.OnExecute += onAnalyzeExecuteHandler;

        applyButtonDefinition = inventorApplication.CommandManager.ControlDefinitions.AddButtonDefinition(
            "Apply Naming",
            ApplyButtonInternalName,
            CommandTypesEnum.kFileOperationsCmdType,
            ClientId,
            "Preview and apply WMP file numbering to unnumbered files in the active assembly.",
            "Preview and apply WMP file numbering to unnumbered files in the active assembly.",
            applyStandardIcon,
            applyLargeIcon);
        onApplyExecuteHandler = OnApplyNamingExecute;
        applyButtonDefinition.OnExecute += onApplyExecuteHandler;

        RibbonTab tab = WmpRibbonTab.EnsureTab(inventorApplication, "Assembly", ClientId);
        RibbonPanel panel = WmpRibbonTab.EnsurePanel(tab, "File Naming", PanelInternalName, ClientId);
        if (!WmpRibbonTab.ContainsControl(panel, AnalyzeButtonInternalName))
        {
            panel.CommandControls.AddButton(analyzeButtonDefinition, true, true);
        }

        if (!WmpRibbonTab.ContainsControl(panel, ApplyButtonInternalName))
        {
            panel.CommandControls.AddButton(applyButtonDefinition, true, true);
        }
    }

    public void Deactivate()
    {
        if (analyzeButtonDefinition is not null && onAnalyzeExecuteHandler is not null)
        {
            analyzeButtonDefinition.OnExecute -= onAnalyzeExecuteHandler;
        }

        if (applyButtonDefinition is not null && onApplyExecuteHandler is not null)
        {
            applyButtonDefinition.OnExecute -= onApplyExecuteHandler;
        }

        onAnalyzeExecuteHandler = null;
        onApplyExecuteHandler = null;
        analyzeButtonDefinition = null;
        applyButtonDefinition = null;
        fileNamingCommand = null;
        analyzeStandardIcon = null;
        analyzeLargeIcon = null;
        applyStandardIcon = null;
        applyLargeIcon = null;
        inventorApplication = null;
    }

    public void ExecuteCommand(int CommandID)
    {
    }

    private void OnAnalyzeNamingExecute(NameValueMap context) => fileNamingCommand?.Execute(FileNamingMode.Analyze);

    private void OnApplyNamingExecute(NameValueMap context) => fileNamingCommand?.Execute(FileNamingMode.Apply);
}
#endif
