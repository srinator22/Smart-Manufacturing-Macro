// Purpose: Drive the update dialog through the three states ADR-0005 allows - checking, up to date,
//   update available - and expose every failure as text on the window rather than as an exception.
// Inputs: An UpdateWorkflow composed by the add-in over the real ports, or over fakes in tests.
// Outputs: Observable state for UpdateWindow: a summary sentence, the release-notes excerpt, the
//   plugin table, the error list, and which buttons are enabled or visible.
// Dependencies: WmpToolsManager.Application and WmpToolsManager.Core only.
// Assumptions: Updates are never silent: nothing here runs on its own timer and there is no startup
//   check. The window calls CheckAsync once when it opens, and every other transition is a click. An
//   apply that this dialog already launched keeps running after the window is closed, so a reopened
//   window finds it through the pending-apply marker and disables both launching buttons rather than
//   letting a second powershell.exe rewrite the add-ins root alongside the first.
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
    private PendingApply? pendingApply;

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

    public bool CanDownload =>
        !IsBusy && !IsApplyPending && check is { CanStage: true } && stage is not { Verified: true };

    /// <summary>
    /// Rollback is offered only when the state root actually holds an archived install, because the
    /// installer's -Rollback fails outright when it does not.
    /// </summary>
    public bool IsRollbackVisible => check?.HasPreviousInstall == true;

    /// <summary>
    /// Rollback stays visible but disabled while an apply is pending, so the reason is on screen in
    /// the status line instead of the button silently vanishing.
    /// </summary>
    public bool CanRollback => !IsBusy && !IsApplyPending;

    /// <summary>True while an apply this add-in launched is still waiting for Inventor to exit.</summary>
    public bool IsApplyPending => pendingApply is not null;

    /// <summary>The sentence naming the waiting process, or empty when nothing is pending.</summary>
    public string PendingApplyMessage => pendingApply?.WaitingMessage ?? string.Empty;

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
            pendingApply = result.PendingApply;

            Summary = result.Summary;
            StatusMessage = pendingApply is not null
                ? pendingApply.WaitingMessage
                : result.Decision == UpdateDecision.UpdateAvailable
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
            pendingApply = workflow.GetActivePendingApply();
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
            pendingApply = workflow.GetActivePendingApply();
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
            pendingApply = workflow.GetActivePendingApply();
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
        OnPropertyChanged(nameof(CanRollback));
    }

    private void RaiseDerivedStates()
    {
        RaiseCommandStates();
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasPlugins));
        OnPropertyChanged(nameof(IsRollbackVisible));
        OnPropertyChanged(nameof(IsUpdateStaged));
        OnPropertyChanged(nameof(IsApplyPending));
        OnPropertyChanged(nameof(PendingApplyMessage));
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
