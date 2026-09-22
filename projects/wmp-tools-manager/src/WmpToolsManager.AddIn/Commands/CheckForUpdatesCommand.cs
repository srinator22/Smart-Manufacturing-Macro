// Purpose: Own the synchronous UI interaction launched by the Check for updates ribbon command.
// Inputs: Inventor application ownership information and the composed update workflow.
// Outputs: One modal update window, owned by Inventor's main frame.
// Dependencies: Inventor interop at the host boundary, the Application workflow, and the WPF UI.
// Assumptions: Execute is called by Inventor on its owning STA thread. An unhandled exception inside an
//   Inventor command callback can take the host down, so Execute never lets one escape. The command
//   itself performs no network or file access; everything it does is inside the window.
// Validation source: UpdateWindowRenderTests and UpdateWorkflowTests.

#if INVENTOR_INTEROP
using System.Windows;
using System.Windows.Interop;
using WmpToolsManager.Application;
using WmpToolsManager.UI;

namespace WmpToolsManager.AddIn.Commands;

internal sealed class CheckForUpdatesCommand(Inventor.Application inventorApplication, UpdateWorkflow workflow)
{
    public const string DialogTitle = "WMP Tools Manager";

    public void Execute()
    {
        try
        {
            UpdateViewModel viewModel = new(workflow);
            UpdateWindow window = new(viewModel);
            WindowInteropHelper ownership = new(window)
            {
                Owner = new IntPtr(inventorApplication.MainFrameHWND),
            };
            _ = ownership;
            window.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, DialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
#endif
