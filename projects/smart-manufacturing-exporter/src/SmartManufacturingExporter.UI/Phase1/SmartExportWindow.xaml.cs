using Microsoft.Win32;
using System.Windows;

namespace SmartManufacturingExporter.UI.Phase1;

public partial class SmartExportWindow : Window
{
    public SmartExportWindow(SmartExportViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private SmartExportViewModel ViewModel => (SmartExportViewModel)DataContext;

    private void SelectAll_Click(object sender, RoutedEventArgs e) => ViewModel.SelectAll();

    private void SelectNone_Click(object sender, RoutedEventArgs e) => ViewModel.SelectNone();

    private void ExpandAll_Click(object sender, RoutedEventArgs e) => ViewModel.ExpandAll();

    private void CollapseAll_Click(object sender, RoutedEventArgs e) => ViewModel.CollapseAll();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dialog = new()
        {
            Title = "Choose STEP export destination",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.DestinationDirectory = dialog.FolderName;
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e) => ViewModel.ExportSelected();
}
