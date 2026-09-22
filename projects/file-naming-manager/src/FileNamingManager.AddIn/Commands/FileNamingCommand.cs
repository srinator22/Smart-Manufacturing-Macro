// Purpose: Own the synchronous UI interaction launched by the Analyze Naming / Apply Naming ribbon commands.
// Inputs: Inventor application ownership information, the composed workflow, and the InventorAdapter
//   gateway composed once in StandardAddInServer.Activate.
// Outputs: Exact invalid-document feedback, or one modal File Naming window, followed by closing any
//   documents this command's gateway opened during an Apply session.
// Dependencies: Inventor interop at the host boundary, Application workflow, and WPF UI.
// Assumptions: Execute is called by Inventor on its owning STA thread. An unhandled exception inside an
//   Inventor command callback can take down Inventor, so Execute never lets one escape.
// Validation source: FileNamingWorkflow tests and installed Inventor 2027 MainFrameHWND contract.

#if INVENTOR_INTEROP
using System.Windows;
using System.Windows.Interop;
using FileNamingManager.Application;
using FileNamingManager.InventorAdapter;
using FileNamingManager.UI;

namespace FileNamingManager.AddIn.Commands;

internal sealed class FileNamingCommand(
    Inventor.Application inventorApplication,
    FileNamingWorkflow workflow,
    InventorNamingGateway gateway)
{
    public void Execute(FileNamingMode mode)
    {
        try
        {
            NamingAnalysis analysis = workflow.Analyze(null);
            if (!analysis.IsSuccess)
            {
                MessageBox.Show(
                    analysis.ErrorMessage ?? FileNamingWorkflow.RequiredAssemblyMessage,
                    "File Naming Manager",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                FileNamingViewModel viewModel = new(workflow, analysis, mode == FileNamingMode.Apply);
                FileNamingWindow window = new(viewModel);
                WindowInteropHelper ownership = new(window)
                {
                    Owner = new IntPtr(inventorApplication.MainFrameHWND),
                };
                _ = ownership;
                window.ShowDialog();
            }
            finally
            {
                if (mode == FileNamingMode.Apply)
                {
                    gateway.CloseDocumentsOpenedHere();
                }
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "File Naming Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
#endif
