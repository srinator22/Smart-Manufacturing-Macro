// Purpose: Drive the update dialog through the three states ADR-0005 allows - checking, up to date,
//   update available - and expose every failure as text on the window rather than as an exception.
// Inputs: An UpdateWorkflow composed by the add-in over the real ports, or over fakes in tests.
// Outputs: Observable state for UpdateWindow: a summary sentence, the release-notes excerpt, the
//   plugin table, the error list, and which buttons are enabled or visible.
// Dependencies: WmpToolsManager.Application and WmpToolsManager.Core only.
// Assumptions: Updates are never silent: nothing here runs on its own timer and there is no startup
//   check. The window calls CheckAsync once when it opens, and every other transition is a click.
//   Properties are plain strings and bools bound single-direction from TextBlocks - never Run.Text,
//   whose default TwoWay binding crashes a window on open against a read-only property.
// Validation source: docs/decisions/0005-release-distribution-and-updater.md item 3;
//   UpdateViewModelTests and UpdateWindowRenderTests.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WmpToolsManager.Application;
using WmpToolsManager.Core;

namespace WmpToolsManager.UI;

public sealed class UpdateViewModel : INotifyPropertyChanged
{
    public const string CheckingMessage = "Checking GitHub for a newer release...";
    public const string StagedMessage =
        "Close Inventor to finish; the update applies automatically.";

    private readonly UpdateWorkflow workflow;

    private bool isBusy;
    private bool hasChecked;
    private string statusMessage = CheckingMessage;
    private string summary = string.Empty;
    private string notesExcerpt = string.Empty;
    private string installedVersionText = "unknown";
    private string latestVersionText = "unknown";
    private UpdateCheck? check;
    private StageResult? stage;

    public UpdateViewModel(UpdateWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        this.workflow = workflow;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// An instance property, not a static one: WPF resolves a binding path against the DataContext
    /// instance, so a static member here would silently produce a binding error at runtime.
    /// </summary>
    public string Title { get; } = "WMP Tools Manager - Updates";

    public ObservableCollection<PluginRowViewModel> Plugins { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    public string StatusMessage
    {
        get => statusMessage;
        private set => Set(ref statusMessage, value);
    }

    public string Summary
    {
        get => summary;
        private set => Set(ref summary, value);
    }

    public string NotesExcerpt
    {
        get => notesExcerpt;
        private set => Set(ref notesExcerpt, value);
    }

    public string InstalledVersionText
    {
        get => installedVersionText;
        private set => Set(ref installedVersionText, value);
    }

    public string LatestVersionText
    {
        get => latestVersionText;
        private set => Set(ref latestVersionText, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (Set(ref isBusy, value))
            {
                RaiseCommandStates();
            }
        }
    }

    /// <summary>
    /// True once a check has run. The window checks on open only when this is false, so a view model a
    /// test has already driven is not re-checked behind the test's back.
    /// </summary>
    public bool HasChecked
    {
        get => hasChecked;
        private set => Set(ref hasChecked, value);
    }

    public bool HasNotes => NotesExcerpt.Length > 0;

    public bool HasErrors => Errors.Count > 0;

    public bool HasPlugins => Plugins.Count > 0;

    public bool CanCheck => !IsBusy;

    public bool CanDownload => !IsBusy && check is { CanStage: true } && stage is not { Verified: true };

    /// <summary>
    /// Rollback is offered only when the state root actually holds an archived install, because the
    /// installer's -Rollback fails outright when it does not.
    /// </summary>
    public bool IsRollbackVisible => check?.HasPreviousInstall == true;

    public bool IsUpdateStaged => stage is { Verified: true };

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = CheckingMessage;
        try
        {
            UpdateCheck result = await workflow.CheckForUpdateAsync(cancellationToken).ConfigureAwait(true);
            check = result;
            stage = null;

            Summary = result.Summary;
            StatusMessage = result.Decision == UpdateDecision.UpdateAvailable
                ? "Select Download and install to stage this release."
                : result.Summary;
            NotesExcerpt = result.NotesExcerpt;
            InstalledVersionText = result.InstalledVersion?.ToString() ?? "unknown";
            LatestVersionText = result.LatestVersion?.ToString() ?? "unknown";
            Replace(Plugins, result.Plugins);
            Replace(Errors, result.Errors);
        }
        finally
        {
            HasChecked = true;
            IsBusy = false;
            RaiseDerivedStates();
        }
    }

    /// <summary>
    /// Downloads and verifies the release, then starts the out-of-process apply step. Both happen on
    /// one click because a verified package the user then has to launch separately is a step that adds
    /// nothing but a way to forget it.
    /// </summary>
    public async Task DownloadAndStageAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy || check is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Downloading and verifying the release...";
        try
        {
            StageResult result = await workflow.StageUpdateAsync(check, cancellationToken).ConfigureAwait(true);
            stage = result;
            Replace(Errors, result.Errors);
            if (result.Plugins.Count > 0)
            {
                Replace(Plugins, result.Plugins);
            }

            if (!result.Verified)
            {
                StatusMessage = result.Message;
                return;
            }

            LaunchResult launch = workflow.LaunchApply(result);
            StatusMessage = launch.Launched
                ? StagedMessage + " " + launch.Message
                : result.Message + " " + launch.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseDerivedStates();
        }
    }

    public void RollbackToPrevious()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            LaunchResult launch = workflow.RollbackToPrevious();
            StatusMessage = launch.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseDerivedStates();
        }
    }

    private static void Replace(ObservableCollection<string> target, IReadOnlyList<string> values)
    {
        target.Clear();
        foreach (string value in values)
        {
            target.Add(value);
        }
    }

    private static void Replace(ObservableCollection<PluginRowViewModel> target, IReadOnlyList<PluginSummary> values)
    {
        target.Clear();
        foreach (PluginSummary value in values)
        {
            target.Add(new PluginRowViewModel(value));
        }
    }

    private void RaiseCommandStates()
    {
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanDownload));
    }

    private void RaiseDerivedStates()
    {
        RaiseCommandStates();
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasPlugins));
        OnPropertyChanged(nameof(IsRollbackVisible));
        OnPropertyChanged(nameof(IsUpdateStaged));
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
