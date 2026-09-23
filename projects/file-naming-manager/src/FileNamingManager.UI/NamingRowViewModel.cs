// Purpose: Present one project-scope file for the report DataGrid, with identity columns fixed at
//   analysis time but ProposedFileName/Action/Reasons re-derived from whichever RenamePlan is current.
// Inputs: The NamingAnalysisRow captured at analysis time (identity/state only), plus a RenamePlan and
//   RenameOptions supplied on every re-plan via Refresh().
// Outputs: Read-only display strings plus the two-way IsIncluded flag; ProposedFileName/Action/Reasons/
//   HasBlocker/CanToggle raise PropertyChanged so the DataGrid updates in place without reopening the
//   window.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core (for the enum ToString() values).
// Assumptions: A row must never claim an action the current plan will not perform - showing VaultRename
//   or a proposed name for a defect class the operator has left unchecked is the same class of bug as a
//   checkbox that does nothing (D6). Refresh() therefore looks the row's FullPath up in the current
//   plan's Operations/VaultInstructions rather than trusting the Analyze-time preview action. When the
//   analysis-time preview had a proposal but the current plan excludes it, the exclusion is attributed to
//   the specific option gate (NormalizeMalformed for MalformedWhitespace/TaglessAssembly/RevisionSuffixed;
//   RenameUnnumbered for UnnumberedDescription/LegacyPrefix/CopySuffix), mirroring FileNamingWorkflow.Plan.
//   IsIncluded is the operator's own per-row gate and defaults to true for every row; a row the plan would
//   do nothing with is disabled through CanToggle rather than started unticked, because "unticked" has to
//   keep meaning "the operator took this out" and nothing else. A row the plan BLOCKS on is not such a
//   row: Plan leaves it out of Operations and VaultInstructions, so it reads Action None while being the
//   one row whose exclusion clears the blocker. RenamePlan.BlockedPaths names it, Refresh() turns that
//   into HasBlocker, and CanToggle keeps its box live (review finding P1).
// Validation source: .work/TASK.md UI acceptance criterion; FileNamingWorkflow.Plan's gating switch;
//   FileNamingViewModelTests.

using System.ComponentModel;
using System.IO;
using FileNamingManager.Application;
using FileNamingManager.Core;

namespace FileNamingManager.UI;

public sealed class NamingRowViewModel : INotifyPropertyChanged
{
    private const string InvalidProjectReason = "Enter a valid project number (100-999).";

    /// <summary>
    /// The row reason for a file the operator took out of the plan themselves. Deliberately distinct from
    /// the option-gate reasons: "you unticked this row" and "an option is off" are different facts, and an
    /// operator who sees the wrong one goes looking for an option that is not the cause.
    /// </summary>
    private const string OperatorExcludedReason = "Excluded by the operator.";

    private readonly NamingAnalysisRow row;
    private string proposedFileName = string.Empty;
    private string action = nameof(RenameAction.None);
    private string reasons;
    private bool isIncluded = true;
    private bool hasBlocker;

    public NamingRowViewModel(NamingAnalysisRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        this.row = row;

        FullPath = row.FullPath;
        CurrentFileName = row.CurrentFileName;
        Kind = row.Kind.ToString();
        State = row.Parsed.State.ToString();
        VaultState = row.VaultState.ToString();
        IsRoot = row.IsRoot;
        reasons = string.Join("; ", row.Reasons);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// The identity FileNamingViewModel keys its excluded-path set by, so the include state survives a
    /// re-plan rather than being rebuilt from a plan that no longer mentions the row.
    /// </summary>
    public string FullPath { get; }

    public string CurrentFileName { get; }

    public string Kind { get; }

    public string State { get; }

    public string VaultState { get; }

    public bool IsRoot { get; }

    public string ProposedFileName
    {
        get => proposedFileName;
        private set => SetField(ref proposedFileName, value, nameof(ProposedFileName));
    }

    public string Action
    {
        get => action;
        private set
        {
            if (SetField(ref action, value, nameof(Action)))
            {
                OnPropertyChanged(nameof(CanToggle));
            }
        }
    }

    /// <summary>
    /// Two-way bound to the grid's include checkbox. FileNamingViewModel listens for this change and
    /// re-plans, so an unticked row really leaves the plan rather than only its own display.
    /// </summary>
    public bool IsIncluded
    {
        get => isIncluded;
        set
        {
            if (isIncluded == value)
            {
                return;
            }

            isIncluded = value;
            OnPropertyChanged(nameof(IsIncluded));
            OnPropertyChanged(nameof(CanToggle));
        }
    }

    /// <summary>
    /// True when the current plan raised a blocker against this row's own path and therefore left it out
    /// of both Operations and VaultInstructions (RenamePlan.BlockedPaths). Such a row shows Action None
    /// while being the one row whose exclusion clears that blocker, which is why it feeds CanToggle.
    /// </summary>
    public bool HasBlocker
    {
        get => hasBlocker;
        private set
        {
            if (hasBlocker == value)
            {
                return;
            }

            hasBlocker = value;
            OnPropertyChanged(nameof(HasBlocker));
            OnPropertyChanged(nameof(CanToggle));
        }
    }

    /// <summary>
    /// False only for a row excluding which would change nothing: out of scope, a drawing following its
    /// model, or a correctly named file with no blocker. True whenever the exclusion would change the
    /// plan - the row is already excluded, the plan acts on it, or the plan blocks on it. An
    /// already-excluded row always stays toggleable: excluding a row sets its Action to None, so a plain
    /// "Action is None" test would disable the very checkbox the operator needs to undo the exclusion
    /// with. A blocked row is the same trap seen from the other side: the plan drops it from Operations
    /// and VaultInstructions, so "Action is None" disabled the only control that clears its blocker and
    /// left the operator unable to run the rows the blocker does not touch (review finding P1).
    /// </summary>
    public bool CanToggle =>
        !isIncluded
        || hasBlocker
        || !string.Equals(action, nameof(RenameAction.None), StringComparison.Ordinal);

    public string Reasons
    {
        get => reasons;
        private set => SetField(ref reasons, value, nameof(Reasons));
    }

    /// <summary>
    /// Re-derives ProposedFileName/Action/Reasons from the plan that would execute right now. Called by
    /// FileNamingViewModel after every re-plan (project number change, option change, or Apply clearing
    /// the plan) so the grid never shows an action the plan will not perform.
    /// </summary>
    public void Refresh(RenamePlan? plan, RenameOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (plan is null)
        {
            HasBlocker = false;
            ProposedFileName = string.Empty;
            Action = nameof(RenameAction.None);
            Reasons = InvalidProjectReason;
            return;
        }

        if (!isIncluded)
        {
            // Plan skipped this row before it built a proposal, so it appears in neither Operations nor
            // VaultInstructions and raises no blocker of its own - it can never be in BlockedPaths, and a
            // stale flag would outlive the plan that set it. Say why here rather than fall through to the
            // option-gate and external-parent reasons below, none of which is the reason this row is
            // doing nothing.
            HasBlocker = false;
            ProposedFileName = string.Empty;
            Action = nameof(RenameAction.None);
            Reasons = string.Join("; ", row.Reasons.Append(OperatorExcludedReason));
            return;
        }

        HasBlocker = plan.BlockedPaths.Any(PathsMatch);

        foreach (RenameOperation operation in plan.Operations)
        {
            if (PathsMatch(operation.CurrentFullPath))
            {
                ProposedFileName = Path.GetFileName(operation.NewFullPath);
                Action = nameof(RenameAction.Rename);
                Reasons = string.Join("; ", row.Reasons);
                return;
            }
        }

        foreach (VaultRenameInstruction instruction in plan.VaultInstructions)
        {
            if (PathsMatch(instruction.CurrentFullPath))
            {
                ProposedFileName = instruction.ProposedFileName;
                Action = nameof(RenameAction.VaultRename);
                Reasons = string.Join("; ", row.Reasons);
                return;
            }
        }

        ProposedFileName = string.Empty;
        Action = nameof(RenameAction.None);

        List<string> reasonList = [.. row.Reasons];
        if (row.ProposedFileName is not null)
        {
            string? exclusion = DescribeExclusion(row.Parsed.State, options);
            if (exclusion is not null)
            {
                reasonList.Add(exclusion);
            }
        }

        // A row excluded by the plan's external-parent guard (AddExternalParentBlockers) lands in neither
        // Operations nor VaultInstructions, and the plan-level Blockers text is not something this row can
        // rely on the caller having shown - Analyze mode never surfaces it. The row needs its own reason.
        // Only a row the analysis proposed a name for can be blocked this way; a canonical row that merely
        // has an external parent was never going to be renamed and must not read "Blocked".
        foreach (string externalParent in row.ProposedFileName is null ? [] : row.ExternalParentFullPaths)
        {
            reasonList.Add($"Blocked: referenced by '{Path.GetFileName(externalParent)}' outside the active assembly.");
        }

        Reasons = string.Join("; ", reasonList);
    }

    private bool PathsMatch(string candidateFullPath) =>
        string.Equals(candidateFullPath, row.FullPath, StringComparison.OrdinalIgnoreCase);

    // Mirrors FileNamingWorkflow.Plan's gating switch: NormalizeMalformed gates already-numbered defects,
    // RenameUnnumbered gates defects that require allocating or reusing a number token. Guarded by "when
    // the option is actually off" so a row excluded for another reason (e.g. the project number changed
    // and its token now collides) never claims an option is off that is in fact on.
    private static string? DescribeExclusion(NameState state, RenameOptions options) => state switch
    {
        NameState.MalformedWhitespace or NameState.TaglessAssembly or NameState.RevisionSuffixed
            when !options.NormalizeMalformed =>
            "Excluded: 'Normalize malformed numbered names' is off.",
        NameState.UnnumberedDescription or NameState.LegacyPrefix or NameState.CopySuffix
            when !options.RenameUnnumbered =>
            "Excluded: 'Rename unnumbered files' is off.",
        _ => null,
    };

    private bool SetField(ref string field, string value, string propertyName)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
