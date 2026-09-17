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
            return new(RequiredAssemblyMessage, null, null, [], []);
        }

        if (string.IsNullOrWhiteSpace(scan.RootAssemblyPath))
        {
            return new(UnsavedAssemblyMessage, null, null, [], []);
        }

        List<ScanNotice> notices = [];
        Dictionary<string, CandidateAccumulator> candidates = new(StringComparer.OrdinalIgnoreCase)
        {
            [scan.RootAssemblyPath] = new(scan.RootAssemblyPath, ComponentDocumentKind.Assembly),
        };

        foreach (ComponentOccurrenceSnapshot occurrence in scan.Occurrences)
        {
            Accumulate(occurrence, candidates, notices);
        }

        ExportCandidate[] orderedCandidates = candidates.Values
            .Select(candidate => candidate.ToExportCandidate())
            .OrderBy(candidate => candidate.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ExportHierarchyNode hierarchy = new(
            "root",
            Path.GetFileName(scan.RootAssemblyPath),
            scan.RootAssemblyPath,
            ComponentDocumentKind.Assembly,
            1,
            scan.Occurrences
                .Select((occurrence, index) => BuildHierarchyNode(occurrence, $"root/{index}", candidates))
                .Where(node => node is not null)
                .Select(node => node!)
                .ToArray());

        return new(null, scan.RootAssemblyPath, hierarchy, orderedCandidates, notices);
    }

    public static IReadOnlyList<ExportCandidate> GetCandidatesForScope(
        Phase1StartResult session,
        ExportScopeMode scope)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!Enum.IsDefined(scope))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "The export scope is unsupported.");
        }

        if (!session.IsSuccess || session.HierarchyRoot is null)
        {
            return [];
        }

        HashSet<string> eligiblePaths = new(StringComparer.OrdinalIgnoreCase);
        switch (scope)
        {
            case ExportScopeMode.TopLevelOnly:
                AddMatching(session.HierarchyRoot.Children, ComponentDocumentKind.Part, eligiblePaths, recurse: false);
                break;
            case ExportScopeMode.PartsRecursive:
                AddMatching(session.HierarchyRoot.Children, ComponentDocumentKind.Part, eligiblePaths, recurse: true);
                break;
            case ExportScopeMode.AssembliesOnly:
                eligiblePaths.Add(session.HierarchyRoot.SourcePath!);
                AddMatching(session.HierarchyRoot.Children, ComponentDocumentKind.Assembly, eligiblePaths, recurse: true);
                break;
            case ExportScopeMode.AssembliesAndParts:
                eligiblePaths.Add(session.HierarchyRoot.SourcePath!);
                AddMatching(session.HierarchyRoot.Children, null, eligiblePaths, recurse: true);
                break;
        }

        return session.Candidates
            .Where(candidate => eligiblePaths.Contains(candidate.SourcePath))
            .ToArray();
    }

    public StepExportPlan BuildStepPlan(
        Phase1StartResult session,
        ExportScopeMode scope,
        IEnumerable<string> selectedSourcePaths,
        string destinationDirectory,
        StepExportPrecision precision)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(selectedSourcePaths);

        List<ValidationIssue> issues = [];
        ValidateStepPrecision(precision, issues);
        ValidateDestination(destinationDirectory, issues);

        IReadOnlyList<ExportCandidate> scopedCandidates;
        try
        {
            scopedCandidates = GetCandidatesForScope(session, scope);
        }
        catch (ArgumentOutOfRangeException)
        {
            scopedCandidates = [];
            issues.Add(new(
                "UnsupportedExportScope",
                $"The export scope '{scope}' is unsupported. Choose a listed scope.",
                ValidationSeverity.Error));
        }

        Dictionary<string, ExportCandidate> candidatesByPath = scopedCandidates.ToDictionary(
            candidate => candidate.SourcePath,
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (string sourcePath in selectedSourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !candidatesByPath.ContainsKey(sourcePath))
            {
                issues.Add(new(
                    "UnknownSelection",
                    $"The selected source is not available in the current export scope: '{sourcePath}'. Refresh the scan or change the scope.",
                    ValidationSeverity.Error));
                continue;
            }

            selectedPaths.Add(sourcePath);
        }

        if (selectedPaths.Count == 0)
        {
            issues.Add(new(
                "NoSelection",
                "Select at least one available document before building a STEP export plan.",
                ValidationSeverity.Error));
        }

        List<StepExportPlanItem> items = [];
        Dictionary<string, string> outputSources = new(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            foreach (ExportCandidate candidate in scopedCandidates.Where(candidate => selectedPaths.Contains(candidate.SourcePath)))
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

                gateway.ExportDocumentAsStep(item.SourcePath, item.OutputPath, plan.Precision);
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

    private static void Accumulate(
        ComponentOccurrenceSnapshot occurrence,
        Dictionary<string, CandidateAccumulator> candidates,
        List<ScanNotice> notices)
    {
        if (occurrence.IsSuppressed)
        {
            notices.Add(new(occurrence.OccurrenceName, "Suppressed"));
            return;
        }

        if (occurrence.DocumentKind is not (ComponentDocumentKind.Part or ComponentDocumentKind.Assembly))
        {
            notices.Add(new(occurrence.OccurrenceName, "Unsupported document type"));
        }
        else if (string.IsNullOrWhiteSpace(occurrence.SourcePath))
        {
            notices.Add(new(occurrence.OccurrenceName, "Document has no resolved source path"));
        }
        else if (candidates.TryGetValue(occurrence.SourcePath, out CandidateAccumulator? candidate))
        {
            candidate.Quantity++;
        }
        else
        {
            candidates.Add(occurrence.SourcePath, new(occurrence.SourcePath, occurrence.DocumentKind));
        }

        foreach (ComponentOccurrenceSnapshot child in occurrence.Children)
        {
            Accumulate(child, candidates, notices);
        }
    }

    private static ExportHierarchyNode? BuildHierarchyNode(
        ComponentOccurrenceSnapshot occurrence,
        string nodeId,
        IReadOnlyDictionary<string, CandidateAccumulator> candidates)
    {
        if (occurrence.IsSuppressed)
        {
            return null;
        }

        int quantity = occurrence.SourcePath is not null && candidates.TryGetValue(occurrence.SourcePath, out CandidateAccumulator? candidate)
            ? candidate.Quantity
            : 0;
        return new(
            nodeId,
            occurrence.OccurrenceName,
            occurrence.SourcePath,
            occurrence.DocumentKind,
            quantity,
            occurrence.Children
                .Select((child, index) => BuildHierarchyNode(child, $"{nodeId}/{index}", candidates))
                .Where(child => child is not null)
                .Select(child => child!)
                .ToArray());
    }

    private static void AddMatching(
        IEnumerable<ExportHierarchyNode> nodes,
        ComponentDocumentKind? kind,
        HashSet<string> paths,
        bool recurse)
    {
        foreach (ExportHierarchyNode node in nodes)
        {
            if (node.SourcePath is not null &&
                node.DocumentKind is ComponentDocumentKind.Part or ComponentDocumentKind.Assembly &&
                (kind is null || node.DocumentKind == kind))
            {
                paths.Add(node.SourcePath);
            }

            if (recurse)
            {
                AddMatching(node.Children, kind, paths, recurse: true);
            }
        }
    }

    private sealed class CandidateAccumulator(string sourcePath, ComponentDocumentKind documentKind)
    {
        public int Quantity { get; set; } = 1;

        public ExportCandidate ToExportCandidate() =>
            new(sourcePath, Path.GetFileName(sourcePath), Quantity, documentKind);
    }
}
