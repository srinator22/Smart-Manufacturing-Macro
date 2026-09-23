// Purpose: Bind FileNamingWorkflow's Analyze/Plan/Execute pipeline to the WPF naming window.
// Inputs: A workflow, a successful NamingAnalysis produced by an earlier Analyze() call, and the mode
//   (Analyze preview or Apply) the window was opened in.
// Outputs: Report rows/findings for display, a live RenamePlan re-computed as the project number, the
//   options, or any row's include box change, and an Apply() that executes that plan and reports the
//   outcome.
// Dependencies: FileNamingManager.Application (workflow, ports, analysis/plan models) and
//   FileNamingManager.Core (enum ToString() values, ProjectNumber parsing) only.
// Assumptions: The NamingAnalysis passed in is not re-run by this view model - re-planning calls
//   workflow.Plan(analysis, project, options) against the stored analysis, per .work/TASK.md's UI
//   acceptance criterion; Findings therefore reflect Analyze()'s original project number and do not
//   retroactively update if the operator later types a different project number. This is a documented v1
//   limitation, not an oversight. Rows are the exception: each row's ProposedFileName/Action/Reasons are
//   re-derived from the live RenamePlan on every re-plan (see NamingRowViewModel.Refresh), because a row
//   claiming an action the plan will not perform is a defect (D6), not a preview nuance. Per-row
//   exclusions are held here, keyed by full path, and passed to every Plan call through
//   RenameOptions.ExcludedPaths, so an unticked row leaves the real plan rather than only the display and
//   its include state survives a re-plan that no longer mentions it. A WPF window
//   that nobody ever showed crashed on open because Run.Text
//   binds TwoWay by default against read-only properties (see SmartExportWindowRenderTests header); this
//   view model exposes plain string/bool properties for single-binding TextBlocks, never Run.Text targets.
// Validation source: .work/TASK.md UI acceptance criterion; FileNamingViewModelTests.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using FileNamingManager.Application;
using FileNamingManager.Core;

namespace FileNamingManager.UI;

public sealed class FileNamingViewModel : INotifyPropertyChanged
{
    private readonly FileNamingWorkflow workflow;
    private readonly NamingAnalysis analysis;

    /// <summary>
    /// The operator's per-row exclusions, keyed by full path so they survive every re-plan: rows are
    /// plan-derived, and a plan that no longer mentions an excluded row cannot be the memory of it.
    /// Case-insensitive, matching how the workflow compares them and how Windows compares paths.
    /// </summary>
    private readonly HashSet<string> excludedPaths = new(StringComparer.OrdinalIgnoreCase);

    private bool suppressRowReplan;
    private string projectNumberText = string.Empty;
    private string? projectNumberError;
    private bool renameUnnumbered = true;
    private bool normalizeMalformed;
    private bool setPartNumberProperty = true;
    private RenamePlan? plan;
    private string statusMessage = "Review the report, confirm the project number, then plan a rename.";

    public FileNamingViewModel(FileNamingWorkflow workflow, NamingAnalysis initialAnalysis, bool applyMode)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(initialAnalysis);
        if (!initialAnalysis.IsSuccess)
        {
            throw new ArgumentException(
                $"Cannot build the naming window from a failed analysis: {initialAnalysis.ErrorMessage}",
                nameof(initialAnalysis));
        }

        this.workflow = workflow;
        analysis = initialAnalysis;
        Mode = applyMode ? FileNamingMode.Apply : FileNamingMode.Analyze;

        RootAssemblyPath = initialAnalysis.RootFullPath ?? string.Empty;
        ProjectRootPath = initialAnalysis.ProjectRootPath ?? string.Empty;
        Title = Mode == FileNamingMode.Apply
            ? "File Naming Manager - Apply Naming"
            : "File Naming Manager - Analyze Naming";

        ProjectNumberSuggestionText = DescribeSuggestion(initialAnalysis.SuggestionSource);

        Rows = new(initialAnalysis.Rows.Select(row => new NamingRowViewModel(row)));
        foreach (NamingRowViewModel row in Rows)
        {
            row.PropertyChanged += OnRowPropertyChanged;
        }

        Findings = [.. (initialAnalysis.Report?.ProjectFindings ?? []).Select(DescribeFinding)];

        projectNumberText = initialAnalysis.SuggestedProject?.ToString() ?? string.Empty;
        ValidateAndPlan();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RootAssemblyPath { get; }

    public string ProjectRootPath { get; }

    public FileNamingMode Mode { get; }

    public bool IsApplyMode => Mode == FileNamingMode.Apply;

    public string Title { get; }

    public string ProjectNumberText
    {
        get => projectNumberText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(projectNumberText, value, StringComparison.Ordinal))
            {
                return;
            }

            projectNumberText = value;
            OnPropertyChanged(nameof(ProjectNumberText));
            ValidateAndPlan();
        }
    }

    public string ProjectNumberSuggestionText { get; }

    public string? ProjectNumberError => projectNumberError;

    public bool RenameUnnumbered
    {
        get => renameUnnumbered;
        set
        {
            if (renameUnnumbered == value)
            {
                return;
            }

            renameUnnumbered = value;
            OnPropertyChanged(nameof(RenameUnnumbered));
            ValidateAndPlan();
        }
    }

    public bool NormalizeMalformed
    {
        get => normalizeMalformed;
        set
        {
            if (normalizeMalformed == value)
            {
                return;
            }

            normalizeMalformed = value;
            OnPropertyChanged(nameof(NormalizeMalformed));
            ValidateAndPlan();
        }
    }

    public static string NormalizeMalformedWarning =>
        "Vault-managed files are never renamed by this tool, even when this option is on. Normalizing " +
        "already-numbered names changes file identity for every reference and should be checked into " +
        "Vault deliberately, not left on as a routine default.";

    public bool SetPartNumberProperty
    {
        get => setPartNumberProperty;
        set
        {
            if (setPartNumberProperty == value)
            {
                return;
            }

            setPartNumberProperty = value;
            OnPropertyChanged(nameof(SetPartNumberProperty));
            ValidateAndPlan();
        }
    }

    public ObservableCollection<NamingRowViewModel> Rows { get; }

    public IReadOnlyList<string> Findings { get; }

    public RenamePlan? Plan => plan;

    public IReadOnlyList<string> Blockers => plan?.Blockers ?? [];

    public int VaultInstructionCount => plan?.VaultInstructions.Count ?? 0;

    public bool HasVaultInstructions => VaultInstructionCount > 0;

    public int OperationCount => plan?.Operations.Count ?? 0;

    public string StatusMessage
    {
        get => statusMessage;
        set
        {
            value ??= string.Empty;
            if (string.Equals(statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            statusMessage = value;
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    public bool CanApply =>
        Mode == FileNamingMode.Apply
        && Plan is not null
        && Plan.CanExecute
        && Plan.Operations.Count > 0
        && ProjectNumberError is null;

    public string ApplyDisabledReason
    {
        get
        {
            if (Mode != FileNamingMode.Apply)
            {
                return "Switch to Apply mode to enable renaming.";
            }

            if (ProjectNumberError is not null)
            {
                return ProjectNumberError;
            }

            if (Plan is not null && Plan.Blockers.Count > 0)
            {
                return string.Join(" ", Plan.Blockers);
            }

            if (Plan is null || Plan.Operations.Count == 0)
            {
                return "Nothing to rename.";
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Ticks every row's include box. Re-including is always allowed, even for a row the plan would do
    /// nothing with, because a checked box on such a row means only "not excluded".
    /// </summary>
    public void SelectAllRows() => SetAllRowsIncluded(true);

    /// <summary>
    /// Unticks every row the plan would act on. A row that cannot be toggled is left alone: excluding it
    /// would change no operation while putting "Excluded by the operator." on a row nobody excluded.
    /// </summary>
    public void SelectNoRows() => SetAllRowsIncluded(false);

    public void Apply()
    {
        if (plan is null)
        {
            throw new InvalidOperationException("There is no rename plan to apply.");
        }

        string message;
        try
        {
            message = DescribeExecution(workflow.Execute(plan));
        }
        catch (Exception exception)
        {
            // Apply runs inside an Inventor command callback. An exception that escapes it reaches the
            // user as an opaque add-in fault and can unload the add-in, taking the report window with
            // it, so every failure becomes a status message the operator can act on instead.
            message = $"Apply failed: {exception.Message} Re-open the window and run Analyze again "
                + "before retrying, because part of the plan may already have run.";
        }

        StatusMessage = message;

        plan = null;
        RefreshRows(CurrentOptions());
        RaisePlanDerivedNotifications();
    }

    public string BuildVaultPlanText()
    {
        StringBuilder builder = new();
        builder.AppendLine("WMP Vault rename plan");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Project root: {ProjectRootPath}");
        builder.AppendLine();

        IReadOnlyList<VaultRenameInstruction> instructions = plan?.VaultInstructions ?? [];
        foreach (VaultRenameInstruction instruction in instructions)
        {
            string currentName = Path.GetFileName(instruction.CurrentFullPath);
            builder.AppendLine(CultureInfo.InvariantCulture, $"{currentName} -> {instruction.ProposedFileName}");
            string parents = instruction.ParentFullPaths.Count > 0
                ? string.Join("; ", instruction.ParentFullPaths.Select(Path.GetFileName))
                : "(none)";
            builder.AppendLine(CultureInfo.InvariantCulture, $"    parents: {parents}");

            foreach ((string current, string @new) in instruction.CompanionDrawings)
            {
                builder.AppendLine(CultureInfo.InvariantCulture,
                    $"    drawing: {Path.GetFileName(current)} -> {Path.GetFileName(@new)}");
            }
        }

        // The exported plan is what the operator hands to Vault Explorer; a blocker that leaves the plan
        // incomplete has to travel with the text itself, not stay visible only in the app's own Blockers
        // panel, which the operator has already left behind by the time they are pasting this into Vault.
        IReadOnlyList<string> blockers = plan?.Blockers ?? [];
        if (blockers.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Blockers (this plan is incomplete until resolved):");
            foreach (string blocker in blockers)
            {
                builder.AppendLine(CultureInfo.InvariantCulture, $"    {blocker}");
            }
        }

        return builder.ToString();
    }

    private static string DescribeExecution(RenameExecution execution)
    {
        // The count is of models actually renamed on disk, not of fully clean items. An item whose model
        // was renamed but whose companion drawing or Part Number write failed HAS been renamed, and
        // reporting it as "Renamed 0 of 1" told the operator nothing had happened when it had.
        int renamed = execution.Items.Count(item => item.ModelRenamed);
        int partial = execution.Items.Count(item => item.ModelRenamed && !item.Succeeded);
        int total = execution.Items.Count;
        string originalsText = DescribeOriginals(execution.Manifest);
        string countText = partial > 0
            ? $"Renamed {renamed} of {total}, of which {partial} with warnings."
            : $"Renamed {renamed} of {total}.";
        string message = $"{countText} Originals: {originalsText}";

        List<string> failureDetails = [.. execution.Items
            .Where(item => !item.Succeeded)
            .Select(item => DescribeFailure(item))];
        if (failureDetails.Count > 0)
        {
            message += " " + string.Join(" ", failureDetails);
        }

        if (execution.ParentSaveFailures.Count > 0)
        {
            List<string> parentFailureDetails = [.. execution.ParentSaveFailures
                .Select(failure => $"Parent save failed: {Path.GetFileName(failure.ParentFullPath)}: {failure.ErrorMessage}")];
            message += " " + string.Join(" ", parentFailureDetails);
            message += " Originals were left in place so the assembly still opens; save the listed parents and run Apply again.";
        }

        if (execution.ArchiveFailures.Count > 0)
        {
            List<string> archiveFailureDetails = [.. execution.ArchiveFailures
                .Select(failure => $"Archive failed: {Path.GetFileName(failure.OriginalPath)}: {failure.ErrorMessage}")];
            message += " " + string.Join(" ", archiveFailureDetails);
            message += " The renames stand; the listed originals are still beside their renamed files and were not archived.";
        }

        return message;
    }

    /// <summary>
    /// A model whose companion drawing rename failed is left open in Inventor with its reference already
    /// rewritten to the model's new name (Execute pre-opens every companion so the in-memory reference
    /// updates before the model's own SaveAs runs); the failure is the drawing's own save, not a lost
    /// reference. The operator would otherwise read "Model renamed; companion drawing ... failed" and not
    /// know the fix is a plain save in Inventor rather than a re-run of Apply.
    /// </summary>
    private static string DescribeFailure(RenameItemResult item)
    {
        string detail = $"{Path.GetFileName(item.CurrentFullPath)}: {item.ErrorMessage}";
        if (item.ErrorMessage is not null &&
            item.ErrorMessage.StartsWith("Model renamed; companion drawing", StringComparison.Ordinal))
        {
            detail += " The drawing is still open with the corrected reference; save it in Inventor to finish.";
        }

        return detail;
    }

    private static string? DeriveOriginalsRoot(RenameManifest manifest)
    {
        RenameManifestEntry entry = manifest.Entries[0];
        string relative = Path.GetRelativePath(manifest.ProjectRoot, entry.OriginalPath);
        string archived = entry.ArchivedPath;
        if (archived.Length > relative.Length && archived.EndsWith(relative, StringComparison.OrdinalIgnoreCase))
        {
            return archived[..(archived.Length - relative.Length)]
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return Path.GetDirectoryName(archived);
    }

    private static string DescribeOriginals(RenameManifest? manifest)
    {
        if (manifest is null || manifest.Entries.Count == 0)
        {
            return "none";
        }

        return DeriveOriginalsRoot(manifest) ?? "none";
    }

    private static string DescribeSuggestion(ProjectNumberSuggestionSource? source) => source switch
    {
        null => string.Empty,
        ProjectNumberSuggestionSource.RootFileName => "Suggested from the root file name",
        ProjectNumberSuggestionSource.MostCommonInScope => "Suggested from the most common project number in the project folder",
        ProjectNumberSuggestionSource.FolderName => "Suggested from the project folder name",
        _ => throw new InvalidOperationException($"Unsupported project number suggestion source: {source}."),
    };

    private static string DescribeFinding(NamingFinding finding)
    {
        string files = finding.FileNames.Count > 0 ? string.Join(", ", finding.FileNames) : "(none)";
        return $"{finding.Code}: {finding.Message} Files: {files}";
    }

    private void ValidateAndPlan()
    {
        bool isValid = ProjectNumber.TryParse(projectNumberText, out ProjectNumber parsed);
        string? newError = isValid
            ? null
            : "Enter the project number as three digits between 100 and 999, for example 124.";

        if (!string.Equals(projectNumberError, newError, StringComparison.Ordinal))
        {
            projectNumberError = newError;
            OnPropertyChanged(nameof(ProjectNumberError));
        }

        RenameOptions options = CurrentOptions();
        plan = isValid ? workflow.Plan(analysis, parsed, options) : null;

        RefreshRows(options);
        RaisePlanDerivedNotifications();
    }

    /// <summary>
    /// A snapshot of the excluded set, never the live set: a RenameOptions that aliased it would let a
    /// later toggle silently change the meaning of a plan that was already computed and possibly applied.
    /// </summary>
    private RenameOptions CurrentOptions() =>
        new(RenameUnnumbered, NormalizeMalformed, SetPartNumberProperty) { ExcludedPaths = [.. excludedPaths] };

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NamingRowViewModel.IsIncluded) || sender is not NamingRowViewModel row)
        {
            return;
        }

        if (row.IsIncluded)
        {
            excludedPaths.Remove(row.FullPath);
        }
        else
        {
            excludedPaths.Add(row.FullPath);
        }

        // Refresh() never writes IsIncluded, so the re-plan this triggers cannot re-enter here.
        if (!suppressRowReplan)
        {
            ValidateAndPlan();
        }
    }

    private void SetAllRowsIncluded(bool included)
    {
        // One re-plan for the whole column, not one per row: Plan walks every row and the grid would
        // otherwise flicker through as many intermediate plans as there are rows.
        suppressRowReplan = true;
        try
        {
            foreach (NamingRowViewModel row in Rows)
            {
                if (included || row.CanToggle)
                {
                    row.IsIncluded = included;
                }
            }
        }
        finally
        {
            suppressRowReplan = false;
        }

        ValidateAndPlan();
    }

    private void RefreshRows(RenameOptions options)
    {
        foreach (NamingRowViewModel row in Rows)
        {
            row.Refresh(plan, options);
        }
    }

    private void RaisePlanDerivedNotifications()
    {
        OnPropertyChanged(nameof(Plan));
        OnPropertyChanged(nameof(Blockers));
        OnPropertyChanged(nameof(VaultInstructionCount));
        OnPropertyChanged(nameof(HasVaultInstructions));
        OnPropertyChanged(nameof(OperationCount));
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyDisabledReason));
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
