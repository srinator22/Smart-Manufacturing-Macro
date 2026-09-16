using SmartManufacturingExporter.Core.Phase1;

namespace SmartManufacturingExporter.Application.Phase1;

/// <summary>
/// Coordinates the COM-free scan, validation, planning, and explicit STEP export workflow.
/// </summary>
public sealed class SmartExportWorkflow
{
    public const string RequiredAssemblyMessage = "Smart Export requires an active Inventor assembly.";
    public const string UnsavedAssemblyMessage = "Smart Export requires the active assembly to be saved before export.";

    private const string InvalidPlanMessage = "Cannot execute a STEP export plan that contains validation errors.";
    private readonly IInventorPhase1Gateway gateway;
    private readonly IPhase1FileSystem fileSystem;

    public SmartExportWorkflow(IInventorPhase1Gateway gateway, IPhase1FileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(fileSystem);

        this.gateway = gateway;
        this.fileSystem = fileSystem;
    }

    public Phase1StartResult Start()
    {
        ActiveAssemblyScan? scan = gateway.ScanActiveAssembly();
        if (scan is null)
        {
            return new(RequiredAssemblyMessage, null, [], []);
        }

        if (string.IsNullOrWhiteSpace(scan.RootAssemblyPath))
        {
            return new(UnsavedAssemblyMessage, null, [], []);
        }

        List<ScanNotice> notices = [];
        Dictionary<string, CandidateAccumulator> candidates = new(StringComparer.OrdinalIgnoreCase);

        foreach (TopLevelOccurrenceSnapshot occurrence in scan.Occurrences)
        {
            if (occurrence.IsSuppressed)
            {
                notices.Add(new(occurrence.OccurrenceName, "Suppressed"));
                continue;
            }

            if (occurrence.DocumentKind != ComponentDocumentKind.Part)
            {
                notices.Add(new(occurrence.OccurrenceName, "Not a top-level part"));
                continue;
            }

            if (string.IsNullOrWhiteSpace(occurrence.SourcePath))
            {
                notices.Add(new(occurrence.OccurrenceName, "Part has no resolved source path"));
                continue;
            }

            if (candidates.TryGetValue(occurrence.SourcePath, out CandidateAccumulator? candidate))
            {
                candidate.Quantity++;
            }
            else
            {
                candidates.Add(occurrence.SourcePath, new(occurrence.SourcePath));
            }
        }

        ExportCandidate[] orderedCandidates = candidates.Values
            .Select(candidate => candidate.ToExportCandidate())
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(null, scan.RootAssemblyPath, orderedCandidates, notices);
    }

    public StepExportPlan BuildStepPlan(
        Phase1StartResult session,
        IEnumerable<string> selectedSourcePaths,
        string destinationDirectory,
        StepExportPrecision precision)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(selectedSourcePaths);

        List<ValidationIssue> issues = [];
        ValidateStepPrecision(precision, issues);
        ValidateDestination(destinationDirectory, issues);

        Dictionary<string, ExportCandidate> candidatesByPath = session.Candidates.ToDictionary(
            candidate => candidate.SourcePath,
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (string sourcePath in selectedSourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !candidatesByPath.ContainsKey(sourcePath))
            {
                issues.Add(new(
                    "UnknownSelection",
                    $"The selected source is not part of this scan: '{sourcePath}'. Refresh the scan and select an available part.",
                    ValidationSeverity.Error));
                continue;
            }

            selectedPaths.Add(sourcePath);
        }

        if (selectedPaths.Count == 0)
        {
            issues.Add(new(
                "NoSelection",
                "Select at least one scanned part before building a STEP export plan.",
                ValidationSeverity.Error));
        }

        List<StepExportPlanItem> items = [];
        Dictionary<string, string> outputSources = new(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            foreach (ExportCandidate candidate in session.Candidates.Where(candidate => selectedPaths.Contains(candidate.SourcePath)))
            {
                string outputPath = Path.Combine(
                    destinationDirectory,
                    $"{Path.GetFileNameWithoutExtension(candidate.SourcePath)}.step");
                items.Add(new(candidate.SourcePath, outputPath));

                if (outputSources.TryGetValue(outputPath, out string? conflictingSource))
                {
                    issues.Add(new(
                        "OutputCollision",
                        $"Selected sources '{conflictingSource}' and '{candidate.SourcePath}' both map to '{outputPath}'. Rename or deselect one source.",
                        ValidationSeverity.Error));
                }
                else
                {
                    outputSources.Add(outputPath, candidate.SourcePath);
                }

                if (fileSystem.FileExists(outputPath))
                {
                    issues.Add(new(
                        "OutputExists",
                        $"The output file already exists: '{outputPath}'. Choose another destination or remove the conflict.",
                        ValidationSeverity.Error));
                }
            }
        }

        return new(items, issues, precision);
    }

    public StepExportBatchResult ExecuteStepPlan(StepExportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanExecute)
        {
            throw new InvalidOperationException(InvalidPlanMessage);
        }

        List<ExportItemResult> results = new(plan.Items.Count);
        foreach (StepExportPlanItem item in plan.Items)
        {
            try
            {
                if (fileSystem.FileExists(item.OutputPath))
                {
                    results.Add(new(
                        item.SourcePath,
                        item.OutputPath,
                        false,
                        $"The output file already exists: '{item.OutputPath}'. Export was skipped to prevent overwrite."));
                    continue;
                }

                gateway.ExportPartAsStep(item.SourcePath, item.OutputPath, plan.Precision);
                results.Add(new(item.SourcePath, item.OutputPath, true, null));
            }
            catch (Exception exception)
            {
                results.Add(new(item.SourcePath, item.OutputPath, false, exception.Message));
            }
        }

        return new(results);
    }

    private void ValidateDestination(string destinationDirectory, List<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            issues.Add(new(
                "DestinationRequired",
                "Choose an output destination before building a STEP export plan.",
                ValidationSeverity.Error));
            return;
        }

        if (!fileSystem.DirectoryExists(destinationDirectory))
        {
            issues.Add(new(
                "DestinationNotFound",
                $"The output destination does not exist: '{destinationDirectory}'. Choose an existing folder.",
                ValidationSeverity.Error));
            return;
        }

        if (!fileSystem.CanWriteToDirectory(destinationDirectory))
        {
            issues.Add(new(
                "DestinationNotWritable",
                $"The output destination is not writable: '{destinationDirectory}'. Choose a folder with write access.",
                ValidationSeverity.Error));
        }
    }

    private static void ValidateStepPrecision(
        StepExportPrecision precision,
        List<ValidationIssue> issues)
    {
        if (!Enum.IsDefined(precision))
        {
            issues.Add(new(
                "UnsupportedStepPrecision",
                $"The STEP export precision '{precision}' is unsupported. Choose Low, Medium, or Highest.",
                ValidationSeverity.Error));
        }
    }

    private sealed class CandidateAccumulator(string sourcePath)
    {
        public int Quantity { get; set; } = 1;

        public ExportCandidate ToExportCandidate() =>
            new(sourcePath, Path.GetFileName(sourcePath), Quantity);
    }
}
