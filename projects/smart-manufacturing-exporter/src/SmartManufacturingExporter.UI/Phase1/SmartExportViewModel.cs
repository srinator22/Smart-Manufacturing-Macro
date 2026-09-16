// Purpose: Present a deterministic, COM-free hierarchy selection and export interaction.
// Inputs: A successful workflow session, tree selections, scope, and a destination directory.
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
    private ExportScopeMode selectedScope = ExportScopeMode.PartsRecursive;
    private StepExportPrecision selectedStepPrecision = StepExportPrecision.Low;
    private string statusMessage = "Choose a destination, review the selected documents, then export.";

    public SmartExportViewModel(SmartExportWorkflow workflow, Phase1StartResult session)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(session);
        if (!session.IsSuccess || string.IsNullOrWhiteSpace(session.RootAssemblyPath) || session.HierarchyRoot is null)
        {
            throw new ArgumentException("A successful Phase 1 session with a root assembly is required.", nameof(session));
        }

        this.workflow = workflow;
        this.session = session;
        RootAssemblyPath = session.RootAssemblyPath;
        RebuildTree();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RootAssemblyPath { get; }

    public ObservableCollection<SmartExportTreeNodeViewModel> RootNodes { get; } = [];

    public SmartExportTreeNodeViewModel RootNode { get; private set; } = null!;

    public IReadOnlyList<ExportScopeMode> ScopeOptions { get; } = Enum.GetValues<ExportScopeMode>();

    public ExportScopeMode SelectedScope
    {
        get => selectedScope;
        set
        {
            if (selectedScope == value)
            {
                return;
            }

            selectedScope = value;
            RebuildTree();
            OnPropertyChanged(nameof(SelectedScope));
            OnPropertyChanged(nameof(CanExport));
        }
    }

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

    public bool CanExport => SelectedSourcePaths().Length != 0 && !string.IsNullOrWhiteSpace(DestinationDirectory);

    public void SelectAll()
    {
        RootNode.IsSelected = true;
    }

    public void SelectNone()
    {
        RootNode.IsSelected = false;
    }

    public void ExpandAll() => RootNode.SetExpandedRecursively(true);

    public void CollapseAll() => RootNode.SetExpandedRecursively(false);

    public void ExportSelected()
    {
        StepExportPlan plan = workflow.BuildStepPlan(
            session,
            SelectedScope,
            SelectedSourcePaths(),
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

    private string[] SelectedSourcePaths() => RootNode
        .DescendantsAndSelf()
        .Where(node => node.IsDocumentSelected && !string.IsNullOrWhiteSpace(node.SourcePath))
        .Select(node => node.SourcePath!)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private void RebuildTree()
    {
        IReadOnlyList<ExportCandidate> candidates = SmartExportWorkflow.GetCandidatesForScope(session, selectedScope);
        HashSet<string> exportablePaths = candidates
            .Select(candidate => candidate.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RootNode = new(session.HierarchyRoot!, exportablePaths);
        RootNode.IsExpanded = true;
        foreach (SmartExportTreeNodeViewModel node in RootNode.DescendantsAndSelf())
        {
            node.PropertyChanged += OnNodePropertyChanged;
        }

        RootNodes.Clear();
        RootNodes.Add(RootNode);
        OnPropertyChanged(nameof(RootNode));
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(SmartExportTreeNodeViewModel.IsSelected)
            or nameof(SmartExportTreeNodeViewModel.IsDocumentSelected))
        {
            OnPropertyChanged(nameof(CanExport));
        }
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
