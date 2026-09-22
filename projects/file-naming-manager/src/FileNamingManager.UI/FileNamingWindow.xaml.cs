// Purpose: Host FileNamingViewModel and wire the two side-effecting buttons the XAML cannot bind to.
// Inputs: A constructed FileNamingViewModel.
// Outputs: Apply() invocation and a written Vault rename plan text file next to the root assembly.
// Dependencies: FileNamingManager.UI.FileNamingViewModel only.
// Assumptions: Writing the plan file is this code-behind's responsibility, not the view model's, per
//   .work/TASK.md's UI acceptance criterion; the view model only builds the text. Refuses to overwrite an
//   existing plan file by appending a UTC timestamp to the file name instead.
// Validation source: .work/TASK.md UI acceptance criterion; FileNamingWindowRenderTests.

using System.Globalization;
using System.IO;
using System.Windows;

namespace FileNamingManager.UI;

public partial class FileNamingWindow : Window
{
    public FileNamingWindow(FileNamingViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
    }

    private FileNamingViewModel ViewModel => (FileNamingViewModel)DataContext;

    private void Apply_Click(object sender, RoutedEventArgs e) => ViewModel.Apply();

    private void ExportVaultPlan_Click(object sender, RoutedEventArgs e)
    {
        string text = ViewModel.BuildVaultPlanText();
        string rootAssemblyFolder = Path.GetDirectoryName(ViewModel.RootAssemblyPath) ?? string.Empty;
        string rootStem = Path.GetFileNameWithoutExtension(ViewModel.RootAssemblyPath);

        string filePath = Path.Combine(rootAssemblyFolder, $"{rootStem} vault rename plan.txt");
        if (File.Exists(filePath))
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmss'Z'", CultureInfo.InvariantCulture);
            filePath = Path.Combine(rootAssemblyFolder, $"{rootStem} vault rename plan {timestamp}.txt");
        }

        // This handler runs inside a modal dialog hosted by an Inventor command callback. An exception
        // escaping here tears the dialog down; the operator must instead see why the export failed.
        try
        {
            File.WriteAllText(filePath, text);
            ViewModel.StatusMessage = $"Vault plan exported to '{filePath}'.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ViewModel.StatusMessage = $"Vault plan export failed: {exception.Message} Choose a writable folder or copy the plan text from a new export after fixing the folder.";
        }
    }
}
