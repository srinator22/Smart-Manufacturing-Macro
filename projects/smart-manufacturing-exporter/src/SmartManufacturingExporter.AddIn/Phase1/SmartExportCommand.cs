// Purpose: Own the synchronous UI interaction launched by the Inventor ribbon command.
// Inputs: Inventor application ownership information and the composed Phase 1 workflow.
// Outputs: Exact invalid-document feedback or one modal Smart Export window.
// Dependencies: Inventor interop at the host boundary, Application workflow, and WPF UI.
// Assumptions: Execute is called by Inventor on its owning STA thread.
// Validation source: Phase 1 workflow tests and installed Inventor 2027 MainFrameHWND contract.

#if INVENTOR_INTEROP
using System.Windows;
using System.Windows.Interop;
using SmartManufacturingExporter.Application.Phase1;
using SmartManufacturingExporter.UI.Phase1;

namespace SmartManufacturingExporter.AddIn.Phase1;

internal sealed class SmartExportCommand(
    Inventor.Application inventorApplication,
    SmartExportWorkflow workflow)
{
    public void Execute()
    {
        Phase1StartResult session = workflow.Start();
        if (!session.IsSuccess)
        {
            MessageBox.Show(
                session.ErrorMessage ?? SmartExportWorkflow.RequiredAssemblyMessage,
                "Smart Export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        SmartExportViewModel viewModel = new(workflow, session);
        SmartExportWindow window = new(viewModel);
        WindowInteropHelper ownership = new(window)
        {
            Owner = new IntPtr(inventorApplication.MainFrameHWND),
        };
        _ = ownership;
        window.ShowDialog();
    }
}
#endif
