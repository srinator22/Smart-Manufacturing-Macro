// Purpose: Define the COM-free data contracts for Phase 1 scanning, planning, and export results.
// Inputs: Normalized assembly occurrence observations and explicit user export selections.
// Outputs: Immutable candidates, notices, validation issues, plans, and batch results.
// Dependencies: .NET base types only.
// Assumptions: Paths preserve their source spelling; owning workflows choose comparison semantics.
// Validation source: SmartExportWorkflowTests and SmartExportViewModelTests.

namespace SmartManufacturingExporter.Core.Phase1;

public enum ComponentDocumentKind
{
    Part,
    Assembly,
    Other,
}

public sealed record TopLevelOccurrenceSnapshot(
    string OccurrenceName,
    string? SourcePath,
    ComponentDocumentKind DocumentKind,
    bool IsSuppressed);

public sealed record ExportCandidate(string SourcePath, string DisplayName, int Quantity);

public sealed record ScanNotice(string OccurrenceName, string Reason);

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ValidationIssue(string Code, string Message, ValidationSeverity Severity);

public sealed record StepExportPlanItem(string SourcePath, string OutputPath);

public sealed record StepExportPlan(
    IReadOnlyList<StepExportPlanItem> Items,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool CanExecute => Issues.All(issue => issue.Severity != ValidationSeverity.Error);
}

public sealed record ExportItemResult(
    string SourcePath,
    string OutputPath,
    bool Succeeded,
    string? ErrorMessage);

public sealed record StepExportBatchResult(IReadOnlyList<ExportItemResult> Items)
{
    public int TotalCount => Items.Count;

    public int SucceededCount => Items.Count(item => item.Succeeded);

    public int FailedCount => Items.Count(item => !item.Succeeded);
}
