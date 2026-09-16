// Purpose: Present a deterministic, COM-free selection and export interaction for Phase 1.
// Inputs: A successful workflow session, row selections, and a destination directory.
// Outputs: Export commands, eligibility state, and actionable status text for the WPF host.
// Dependencies: Application workflow and Core Phase 1 models only.
// Assumptions: Calls are synchronous on the Inventor UI thread and the session remains immutable.
// Validation source: SmartExportViewModelTests.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using SmartManufacturingExporter.Application.Phase1;
using SmartManufacturingExporter.Core.Phase1;

namespace SmartManufacturingExporter.UI.Phase1;

public sealed class SmartExportViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<StepExportPrecision> PrecisionOptions =
        Array.AsReadOnly(
        [
            StepExportPrecision.Low,
            StepExportPrecision.Medium,
            StepExportPrecision.Highest,
        ]);

    private readonly SmartExportWorkflow workflow;
    private readonly Phase1StartResult session;
    private string destinationDirectory = string.Empty;
    private StepExportPrecision selectedStepPrecision = StepExportPrecision.Low;
    private string statusMessage = "Choose a destination, review the selected parts, then export.";

    public SmartExportViewModel(SmartExportWorkflow workflow, Phase1StartResult session)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsSuccess || string.IsNullOrWhiteSpace(session.RootAssemblyPath))
        {
            throw new ArgumentException("A successful Phase 1 session with a root assembly is required.", nameof(session));
        }

        this.workflow = workflow;
        this.session = session;
        RootAssemblyPath = session.RootAssemblyPath;
        Rows = new(session.Candidates.Select(candidate => new SmartExportRowViewModel(candidate)));
        foreach (SmartExportRowViewModel row in Rows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RootAssemblyPath { get; }

    public ObservableCollection<SmartExportRowViewModel> Rows { get; }

    public IReadOnlyList<StepExportPrecision> StepPrecisionOptions { get; } = PrecisionOptions;

    public StepExportPrecision SelectedStepPrecision
    {
        get => selectedStepPrecision;
        set
        {
            if (selectedStepPrecision == value)
            {
                return;
            }

            selectedStepPrecision = value;
            OnPropertyChanged(nameof(SelectedStepPrecision));
            OnPropertyChanged(nameof(StepPrecisionDescription));
        }
    }

    public string StepPrecisionDescription => SelectedStepPrecision switch
    {
        StepExportPrecision.Low => "Uses Inventor's standard spline-fit accuracy and smallest expected file size.",
        StepExportPrecision.Medium => "Uses finer spline-fit accuracy and can increase file size.",
        StepExportPrecision.Highest => "Uses the finest spline-fit accuracy and can produce the largest files.",
        _ => throw new InvalidOperationException($"Unsupported STEP precision: {SelectedStepPrecision}."),
    };

    public string DestinationDirectory
    {
        get => destinationDirectory;
        set
        {
            value ??= string.Empty;
            if (string.Equals(destinationDirectory, value, StringComparison.Ordinal))
            {
                return;
            }

            destinationDirectory = value;
            OnPropertyChanged(nameof(DestinationDirectory));
            OnPropertyChanged(nameof(CanExport));
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (string.Equals(statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    public bool CanExport => Rows.Any(row => row.IsSelected) && !string.IsNullOrWhiteSpace(DestinationDirectory);

    public void SelectAll()
    {
        foreach (SmartExportRowViewModel row in Rows)
        {
            row.IsSelected = true;
        }
    }

    public void SelectNone()
    {
        foreach (SmartExportRowViewModel row in Rows)
        {
            row.IsSelected = false;
        }
    }

    public void ExportSelected()
    {
        string[] selectedPaths = Rows.Where(row => row.IsSelected).Select(row => row.SourcePath).ToArray();
        StepExportPlan plan = workflow.BuildStepPlan(
            session,
            selectedPaths,
            DestinationDirectory,
            SelectedStepPrecision);
        ValidationIssue[] errors = plan.Issues
            .Where(issue => issue.Severity == ValidationSeverity.Error)
            .ToArray();
        if (errors.Length != 0)
        {
            StatusMessage = string.Join(" ", errors.Select(issue => issue.Message));
            return;
        }

        StepExportBatchResult result = workflow.ExecuteStepPlan(plan);
        string summary = $"Export complete: {result.SucceededCount} succeeded, {result.FailedCount} failed.";
        string[] failureDetails = result.Items
            .Where(item => !item.Succeeded)
            .Select(item => $"{Path.GetFileName(item.SourcePath)}: {item.ErrorMessage}")
            .ToArray();
        StatusMessage = failureDetails.Length == 0
            ? summary
            : $"{summary} {string.Join(" ", failureDetails)}";
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(SmartExportRowViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(CanExport));
        }
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
