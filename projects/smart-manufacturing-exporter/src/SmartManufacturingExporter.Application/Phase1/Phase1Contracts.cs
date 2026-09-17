// Purpose: Define the Phase 1 application ports and active-assembly session boundary.
// Inputs: COM-free occurrence snapshots, filesystem queries, and explicit STEP source/output settings.
// Outputs: A validated start session and adapter operations required by SmartExportWorkflow.
// Dependencies: Core Phase 1 models only.
// Assumptions: Host adapters invoke these contracts synchronously on their owning thread.
// Validation source: SmartExportWorkflowTests and the Inventor 2027 compatibility matrix.

using SmartManufacturingExporter.Core.Phase1;

namespace SmartManufacturingExporter.Application.Phase1;

public sealed record ActiveAssemblyScan(
    string RootAssemblyPath,
    IReadOnlyList<ComponentOccurrenceSnapshot> Occurrences);

public interface IInventorPhase1Gateway
{
    ActiveAssemblyScan? ScanActiveAssembly();

    void ExportDocumentAsStep(
        string sourcePath,
        string outputPath,
        StepExportPrecision precision);
}

public interface IPhase1FileSystem
{
    bool DirectoryExists(string path);

    bool CanWriteToDirectory(string path);

    bool FileExists(string path);
}

public sealed record Phase1StartResult(
    string? ErrorMessage,
    string? RootAssemblyPath,
    ExportHierarchyNode? HierarchyRoot,
    IReadOnlyList<ExportCandidate> Candidates,
    IReadOnlyList<ScanNotice> Notices)
{
    public bool IsSuccess => ErrorMessage is null;
}
