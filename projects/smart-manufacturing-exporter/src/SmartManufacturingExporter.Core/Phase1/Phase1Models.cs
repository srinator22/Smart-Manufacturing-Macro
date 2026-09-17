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

public sealed record ComponentOccurrenceSnapshot(
    string OccurrenceName,
    string? SourcePath,
    ComponentDocumentKind DocumentKind,
    bool IsSuppressed,
    IReadOnlyList<ComponentOccurrenceSnapshot> Children);

public enum ExportScopeMode
{
    TopLevelOnly,
    PartsRecursive,
    AssembliesOnly,
    AssembliesAndParts,
}

public sealed record ExportCandidate(
    string SourcePath,
    string DisplayName,
    int Quantity,
    ComponentDocumentKind DocumentKind);

public sealed record ExportHierarchyNode(
    string NodeId,
    string DisplayName,
    string? SourcePath,
    ComponentDocumentKind DocumentKind,
    int Quantity,
    IReadOnlyList<ExportHierarchyNode> Children);

public sealed record ScanNotice(string OccurrenceName, string Reason);

public enum ValidationSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record ValidationIssue(string Code, string Message, ValidationSeverity Severity);

/// <summary>
/// Selects the STEP translator spline-fit tolerance. Lower tolerance values produce more accurate
/// approximations and can increase file size.
/// </summary>
public enum StepExportPrecision
{
    Low,
    Medium,
    Highest,
}

public static class StepExportPrecisionExtensions
{
    /// <summary>
    /// Returns Inventor's <c>export_fit_tolerance</c> value in centimeters.
    /// </summary>
    /// <remarks>
    /// Autodesk's
    /// <see href="https://help.autodesk.com/cloudhelp/2025/ENU/Inventor-API/files/TranslatorSettings.htm">
    /// Inventor Translator Settings reference</see> documents a range of 0.00001 cm to 0.001 cm
    /// and identifies 0.001 cm as the default. Medium is the decade step between the documented
    /// endpoints.
    /// </remarks>
    public static double GetFitToleranceCentimeters(this StepExportPrecision precision) =>
        precision switch
        {
            StepExportPrecision.Low => 0.001,
            StepExportPrecision.Medium => 0.0001,
            StepExportPrecision.Highest => 0.00001,
            _ => throw new ArgumentOutOfRangeException(
                nameof(precision),
                precision,
                "The STEP export precision must be Low, Medium, or Highest."),
        };
}

public sealed record StepExportPlanItem(string SourcePath, string OutputPath);

public sealed record StepExportPlan(
    IReadOnlyList<StepExportPlanItem> Items,
    IReadOnlyList<ValidationIssue> Issues,
    StepExportPrecision Precision)
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
