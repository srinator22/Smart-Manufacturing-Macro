// Purpose: Host UpdateViewModel and wire the four buttons the XAML cannot bind to.
// Inputs: A constructed UpdateViewModel.
// Outputs: An update check when the window opens, and the staging, rollback and close actions.
// Dependencies: WmpToolsManager.UI.UpdateViewModel only.
// Assumptions: The window opens modally from an Inventor command callback, where an escaping exception
//   can take Inventor down, so every handler swallows nothing silently but reports on the window
//   instead of throwing. The open-time check runs only when the view model has not been checked
//   already, which keeps a test that drove the view model itself from being re-checked underneath it.
// Validation source: UpdateWindowRenderTests.

using System.Windows;

namespace WmpToolsManager.UI;

public partial class UpdateWindow : Window
{
    public UpdateWindow(UpdateViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private UpdateViewModel ViewModel => (UpdateViewModel)DataContext;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasChecked)
        {
            await RunAsync(ViewModel.CheckAsync(default)).ConfigureAwait(true);
        }
    }

    private async void CheckAgain_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(ViewModel.CheckAsync(default)).ConfigureAwait(true);

    private async void DownloadAndInstall_Click(object sender, RoutedEventArgs e) =>
        await RunAsync(ViewModel.DownloadAndStageAsync(default)).ConfigureAwait(true);

    private void Rollback_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.RollbackToPrevious();
        }
        catch (Exception exception)
        {
            Report(exception);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async Task RunAsync(Task work)
    {
        try
        {
            await work.ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            Report(exception);
        }
    }

    private void Report(Exception exception) =>
        MessageBox.Show(
            this,
            exception.Message,
            ViewModel.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
}
