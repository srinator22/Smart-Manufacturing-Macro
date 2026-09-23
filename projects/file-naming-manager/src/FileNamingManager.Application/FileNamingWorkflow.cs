// Purpose: Coordinate the COM-free scan, analysis, rename planning, and rename execution workflow.
// Inputs: An Inventor naming gateway, a naming-aware file system, and a clock.
// Outputs: NamingAnalysis previews, executable RenamePlans, and RenameExecution results.
// Dependencies: FileNamingManager.Core (parsing, allocation, formatting) and this project's ports.
// Assumptions: Analyze's proposals are a preview only; Plan recomputes proposals against the confirmed
//   project number and RenameOptions, since a user may change the project number after Analyze runs.
// Validation source: .work/TASK.md "Application" and "Vault safety model" sections; FileNamingWorkflowTests.

using System.Globalization;
using FileNamingManager.Core;

namespace FileNamingManager.Application;

public sealed class FileNamingWorkflow
{
    public const string RequiredAssemblyMessage = "File Naming Manager requires an active, saved Inventor assembly.";

    private static readonly char[] PathSeparators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    private readonly IInventorNamingGateway gateway;
    private readonly INamingFileSystem fileSystem;
    private readonly IClock clock;

    public FileNamingWorkflow(IInventorNamingGateway gateway, INamingFileSystem fileSystem, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(clock);

        this.gateway = gateway;
        this.fileSystem = fileSystem;
        this.clock = clock;
    }

    public NamingAnalysis Analyze(ProjectNumber? projectNumber)
    {
        ActiveAssemblySnapshot? scan = gateway.ScanActiveAssembly();
        if (scan is null)
        {
            return new NamingAnalysis(RequiredAssemblyMessage, null, null, false, false, null, null, null, [], []);
        }

        string projectRoot = ProjectRootLocator.Locate(scan.RootFullPath);
        IReadOnlyList<string> scopePaths = fileSystem.EnumerateScope(projectRoot);
        List<ScopeEntry> scope = [.. scopePaths.Select(path => new ScopeEntry(path, FileNameParser.Parse(Path.GetFileName(path))))];

        ProjectNumberSuggestion suggestion = ProjectNumberSuggestion.Suggest(
            scan,
            scope.Select(entry => Path.GetFileName(entry.FullPath)));
        ProjectNumber? effectiveProject = projectNumber ?? suggestion.Number;

        NamingReport? report = effectiveProject is ProjectNumber reportProject
            ? NamingReport.Build(scope.Select(entry => entry.Parsed), reportProject)
            : null;

        NumberAllocator? allocator = effectiveProject is ProjectNumber allocatorProject
            ? new NumberAllocator(scope.Select(entry => entry.Parsed), allocatorProject)
            : null;

        List<NamingAnalysisRow> rows =
            [.. scan.Documents.Select(document => BuildRow(document, scan, fileSystem, allocator, effectiveProject, projectRoot))];

        return new NamingAnalysis(
            null,
            scan.RootFullPath,
            projectRoot,
            scan.RootIsDirty,
            scan.RootHasMissingReferences,
            suggestion.Number,
            suggestion.Source,
            report,
            rows,
            scope);
    }

    public RenamePlan Plan(NamingAnalysis analysis, ProjectNumber project, RenameOptions options)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(options);
        if (!analysis.IsSuccess)
        {
            throw new InvalidOperationException($"Cannot plan renames from a failed analysis: {analysis.ErrorMessage}");
        }

        NumberAllocator allocator = new(analysis.Scope.Select(entry => entry.Parsed), project);
        HashSet<string> excludedPaths = new(options.ExcludedPaths, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, NamingAnalysisRow> rowsByPath = analysis.Rows.ToDictionary(row => row.FullPath, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> depths = ComputeDepths(analysis.Rows);

        List<string> blockers = [];
        string rootFileName = Path.GetFileName(analysis.RootFullPath ?? string.Empty);
        if (analysis.RootIsDirty)
        {
            blockers.Add($"'{rootFileName}' has unsaved changes and cannot be planned.");
        }

        if (analysis.RootHasMissingReferences)
        {
            blockers.Add($"'{rootFileName}' has missing references and cannot be planned.");
        }

        List<RenameOperation> operations = [];
        List<VaultRenameInstruction> vaultInstructions = [];
        HashSet<NumberSeries> exhaustedSeriesNeeded = [];
        Dictionary<string, List<string>> targetOwners = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> proposedNameOwners = new(StringComparer.OrdinalIgnoreCase);
        ILookup<string, string> scopePathsByFileName = analysis.Scope.ToLookup(
            entry => Path.GetFileName(entry.FullPath),
            entry => entry.FullPath,
            StringComparer.OrdinalIgnoreCase);

        foreach (NamingAnalysisRow row in analysis.Rows)
        {
            if (row.Kind is not (DocumentKind.Part or DocumentKind.Assembly))
            {
                // Drawings and presentations are renamed only as companions of their model, below.
                continue;
            }

            if (IsOutsideProjectScope(row.FullPath, analysis.ProjectRootPath))
            {
                // A Content Center part, a 3rd Party Hardware part, or a part owned by another project.
                // Silently skipped rather than blocked: its presence is normal and must not stop the
                // rows this project does own. BuildRow carries the reason onto the row itself.
                continue;
            }

            if (excludedPaths.Contains(row.FullPath))
            {
                // The operator cleared this row's include box. Skipped here, before any proposal is
                // built, for the same reason as the option gate below: building a proposal allocates the
                // next free number, so an excluded row that got that far would burn a number the next
                // row then skips. An excluded row also contributes no blocker of its own - nothing about
                // a file the plan will not touch can stop the rows it will.
                continue;
            }

            // The gate is evaluated BEFORE any proposal is built, because building one allocates the
            // next free number. Allocating and then discarding the row burns that number, so the next
            // row that really is renamed skips it and the tool manufactures a gap it then reports.
            // NormalizeMalformed gates already-numbered defects (the token stays, only formatting changes);
            // RenameUnnumbered gates defects that require allocating or reusing a number token.
            bool gatedOff = row.Parsed.State switch
            {
                NameState.MalformedWhitespace or NameState.TaglessAssembly or NameState.RevisionSuffixed
                    => !options.NormalizeMalformed,
                NameState.UnnumberedDescription or NameState.LegacyPrefix or NameState.CopySuffix
                    => !options.RenameUnnumbered,
                _ => false,
            };

            if (gatedOff)
            {
                continue;
            }

            string? proposed = TryProposeFileName(
                allocator, row.Parsed, row.Kind, row.IsRoot, project, out NumberSeries? exhaustedSeries);
            if (exhaustedSeries is NumberSeries ranOut)
            {
                exhaustedSeriesNeeded.Add(ranOut);
            }

            if (proposed is null)
            {
                continue;
            }

            // A Vault-managed file is never renamed locally regardless of its own IsModifiable - it goes
            // to the exported Vault plan either way, so a checked-in defect must not block the whole plan
            // (N1). Local IsModifiable only matters for the branch that actually calls SaveAs.
            if (row.VaultState != VaultState.Managed && !row.IsModifiable)
            {
                blockers.Add($"'{row.CurrentFileName}' is not modifiable and cannot be renamed.");
            }

            // Same N1 reasoning as the IsModifiable check above, applied to the parent side: a Vault-managed
            // row is never saved as anyone's reference target (ComputeParentSaveOrder is built from
            // Operations only, and a Vault-managed row never becomes an Operation), so a non-modifiable
            // parent of a Vault-managed row is never actually written to and must not block the plan (B2).
            if (row.VaultState != VaultState.Managed)
            {
                foreach (string parentPath in row.ParentFullPaths)
                {
                    if (rowsByPath.TryGetValue(parentPath, out NamingAnalysisRow? parentRow) && !parentRow.IsModifiable)
                    {
                        blockers.Add($"'{parentRow.CurrentFileName}' is not modifiable, so its reference to '{row.CurrentFileName}' cannot be saved.");
                    }
                }
            }

            string targetDirectory = Path.GetDirectoryName(row.FullPath) ?? string.Empty;
            string targetPath = Path.Combine(targetDirectory, proposed);

            if (!targetOwners.TryGetValue(targetPath, out List<string>? owners))
            {
                owners = [];
                targetOwners[targetPath] = owners;
            }

            owners.Add(row.CurrentFileName);

            if (!string.Equals(targetPath, row.FullPath, StringComparison.OrdinalIgnoreCase) && fileSystem.FileExists(targetPath))
            {
                blockers.Add($"Target file already exists for '{row.CurrentFileName}': '{targetPath}'.");
            }

            // The project runs in Inventor's unique-filenames mode, where a file NAME - not a path - is
            // the identity Inventor resolves references by. A proposal that repeats a name already used
            // in another folder is unresolvable there, and neither the same-target guard above nor the
            // FileExists guard sees it, because both compare full paths.
            string? nameCollision = FindFileNameCollision(
                proposed,
                row.FullPath,
                scopePathsByFileName,
                proposedNameOwners);
            if (nameCollision is not null)
            {
                blockers.Add(
                    $"'{proposed}' would duplicate a file name already used at '{nameCollision}'; "
                    + "Inventor's unique-filenames mode cannot resolve it.");
            }

            proposedNameOwners.TryAdd(proposed, targetPath);

            List<(string Current, string New)> companionDrawings = FindCompanionDrawings(row, proposed, analysis.Scope);
            string? partNumberToSet = DeterminePartNumberToSet(row, proposed, options);

            // An unhandled external parent blocks the whole plan (Blockers is non-empty, so CanExecute is
            // false either way), but the row itself must not appear as something this plan would still
            // do - Operations and VaultInstructions are both "what would run/export if this executed",
            // and this row cannot safely do either while an outside reference to it is unaccounted for.
            if (AddExternalParentBlockers(blockers, row, companionDrawings))
            {
                continue;
            }

            if (row.VaultState == VaultState.Managed)
            {
                vaultInstructions.Add(new VaultRenameInstruction(
                    row.FullPath, proposed, row.Kind, row.ParentFullPaths, companionDrawings));
                continue;
            }

            // A companion drawing is renamed and archived together with its model, so a companion the
            // tool must not touch takes its model out of the executable plan with it: renaming the model
            // alone would leave the untouched drawing resolving to an archived original.
            if (AddCompanionBlockers(blockers, row, companionDrawings))
            {
                continue;
            }

            operations.Add(new RenameOperation(
                row.FullPath,
                targetPath,
                row.Kind,
                row.ParentFullPaths,
                companionDrawings,
                partNumberToSet));
        }

        // Only a series something actually asked for is a blocker: a project that has filled its part
        // series and has nothing left to rename is finished, not blocked. Enumerated in declaration
        // order so the blocker list stays deterministic whatever order the rows came in.
        foreach (NumberSeries series in Enum.GetValues<NumberSeries>())
        {
            if (exhaustedSeriesNeeded.Contains(series))
            {
                blockers.Add(DescribeExhaustedSeries(series, project));
            }
        }

        foreach (List<string> owners in targetOwners.Values)
        {
            if (owners.Count > 1)
            {
                blockers.Add(
                    $"Two or more documents would rename to the same target: {string.Join(", ", owners.Distinct(StringComparer.OrdinalIgnoreCase))}.");
            }
        }

        List<RenameOperation> orderedOperations = OrderLeafFirst(operations, op => op.CurrentFullPath, rowsByPath, depths);
        List<VaultRenameInstruction> orderedVaultInstructions =
            OrderLeafFirst(vaultInstructions, instruction => instruction.CurrentFullPath, rowsByPath, depths);
        List<string> parentSaveOrder = ComputeParentSaveOrder(orderedOperations, depths);

        return new RenamePlan(analysis.ProjectRootPath ?? string.Empty, orderedOperations, blockers, orderedVaultInstructions, parentSaveOrder);
    }

    public RenameExecution Execute(RenamePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanExecute)
        {
            throw new InvalidOperationException("Cannot execute a rename plan that has blockers.");
        }

        List<RenameItemResult> results = [];
        List<RenameManifestEntry> manifestEntries = [];
        HashSet<string> parentsOfSuccesses = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> renamedPaths = new(StringComparer.OrdinalIgnoreCase);
        string originalsRoot = Path.Combine(
            plan.ProjectRootPath,
            "_renamed-originals",
            clock.UtcNow.ToString("yyyyMMddTHHmmss'Z'", CultureInfo.InvariantCulture));

        Dictionary<string, string> preOpenFailures = PreOpenCompanionDrawings(plan);

        foreach (RenameOperation operation in plan.Operations)
        {
            if (preOpenFailures.TryGetValue(operation.CurrentFullPath, out string? preOpenError))
            {
                results.Add(new RenameItemResult(operation.CurrentFullPath, operation.NewFullPath, false, false, preOpenError));
                continue;
            }

            try
            {
                gateway.RenameDocument(operation.CurrentFullPath, operation.NewFullPath);
            }
            catch (Exception exception)
            {
                results.Add(new RenameItemResult(operation.CurrentFullPath, operation.NewFullPath, false, false, exception.Message));
                continue;
            }

            // The model's SaveAs has landed on disk. Record it NOW - before the companion renames and
            // the Part Number write, either of which can throw. Recording only after those steps left a
            // renamed model untracked: its parent was never saved, so the assembly still referenced the
            // old name, and its original was never archived (review finding T2).
            renamedPaths[operation.CurrentFullPath] = operation.NewFullPath;
            manifestEntries.Add(new RenameManifestEntry(
                operation.CurrentFullPath,
                ComputeArchivedPath(operation.CurrentFullPath, originalsRoot, plan.ProjectRootPath),
                operation.NewFullPath,
                false,
                null));
            foreach (string parent in operation.ParentsToSave)
            {
                parentsOfSuccesses.Add(parent);
            }

            // A companion is recorded as each rename lands, for the same reason: one that is already
            // renamed must still be archived even when a later companion fails.
            string? partialError = null;
            foreach ((string current, string @new) in operation.CompanionDrawings)
            {
                try
                {
                    gateway.RenameDocument(current, @new);
                }
                catch (Exception exception)
                {
                    partialError =
                        $"Model renamed; companion drawing '{Path.GetFileName(current)}' failed: {exception.Message}";
                    break;
                }

                manifestEntries.Add(new RenameManifestEntry(
                    current,
                    ComputeArchivedPath(current, originalsRoot, plan.ProjectRootPath),
                    @new,
                    false,
                    null));
            }

            if (partialError is null && operation.PartNumberToSet is not null)
            {
                try
                {
                    gateway.SetPartNumber(operation.NewFullPath, operation.PartNumberToSet);
                }
                catch (Exception exception)
                {
                    partialError = $"Model renamed; Part Number write failed: {exception.Message}";
                }
            }

            results.Add(new RenameItemResult(
                operation.CurrentFullPath,
                operation.NewFullPath,
                partialError is null,
                true,
                partialError));
        }

        List<ParentSaveFailure> parentSaveFailures = [];
        foreach (string parent in plan.ParentSaveOrder)
        {
            if (!parentsOfSuccesses.Contains(parent))
            {
                continue;
            }

            // ParentSaveOrder holds pre-rename paths. A parent that was itself renamed is no longer open
            // under that name, so saving it by the old path fails with "not open in this session" (D7,
            // observed live on 2026-09-23); a parent whose own rename failed is still open under its old
            // path and must be saved there.
            string parentToSave = renamedPaths.GetValueOrDefault(parent, parent);

            try
            {
                gateway.SaveDocument(parentToSave);
            }
            catch (Exception exception)
            {
                parentSaveFailures.Add(new ParentSaveFailure(parentToSave, exception.Message));
            }
        }

        if (parentSaveFailures.Count > 0)
        {
            // An unsaved parent still references the old file name on disk, so moving the originals away
            // would strand it; leave every original in place and surface exactly which parent to retry.
            return new RenameExecution(results, null, parentSaveFailures, [], false);
        }

        if (manifestEntries.Count == 0)
        {
            return new RenameExecution(results, null, [], [], true);
        }

        // The manifest is written BEFORE the first move, with every planned entry unarchived, so a run
        // interrupted mid-archive still leaves a complete record of which originals were moved aside.
        fileSystem.WriteManifest(originalsRoot, new RenameManifest(clock.UtcNow, plan.ProjectRootPath, manifestEntries));

        List<ArchiveFailure> archiveFailures = [];
        List<RenameManifestEntry> archivedEntries = [];
        foreach (RenameManifestEntry entry in manifestEntries)
        {
            // Defense in depth behind Plan's scope guard: ComputeArchivedPath is Path.Combine over
            // Path.GetRelativePath, which answers '..\..\x' for an original outside the project root and
            // would walk the move straight back out of _renamed-originals. Refuse before moving.
            if (!TryGetPathUnderRoot(entry.OriginalPath, plan.ProjectRootPath, out string _))
            {
                string escapeError =
                    $"'{entry.OriginalPath}' is outside the project root '{plan.ProjectRootPath}', so archiving "
                    + "it would move it outside '_renamed-originals'; the rename stands and the original was "
                    + "left in place.";
                archiveFailures.Add(new ArchiveFailure(entry.OriginalPath, escapeError));
                archivedEntries.Add(entry with { Error = escapeError });
                continue;
            }

            try
            {
                fileSystem.MoveToOriginals(entry.OriginalPath, originalsRoot, plan.ProjectRootPath);
                archivedEntries.Add(entry with { Archived = true });
            }
            catch (Exception exception)
            {
                // The rename itself already succeeded; this original is simply still beside its renamed
                // file. Isolating each move keeps one locked original from stranding all the others.
                archiveFailures.Add(new ArchiveFailure(entry.OriginalPath, exception.Message));
                archivedEntries.Add(entry with { Error = exception.Message });
            }
        }

        RenameManifest manifest = new(clock.UtcNow, plan.ProjectRootPath, archivedEntries);
        fileSystem.WriteManifest(originalsRoot, manifest);

        return new RenameExecution(results, manifest, [], archiveFailures, archiveFailures.Count == 0);
    }

    /// <summary>
    /// Opens every companion drawing of every operation before the first rename. A drawing only has its
    /// reference to a model rewritten in memory if it is already open when that model is renamed with
    /// SaveAs, and an earlier operation may rename a model that a later operation's drawing also
    /// references, so opening lazily per operation is too late. Returns, per operation, the message of
    /// the first companion that could not be opened, so that operation alone is skipped.
    /// </summary>
    private Dictionary<string, string> PreOpenCompanionDrawings(RenamePlan plan)
    {
        Dictionary<string, string> failures = new(StringComparer.OrdinalIgnoreCase);
        foreach (RenameOperation operation in plan.Operations)
        {
            foreach ((string companionPath, string _) in operation.CompanionDrawings)
            {
                try
                {
                    gateway.EnsureDocumentOpen(companionPath);
                }
                catch (Exception exception)
                {
                    failures[operation.CurrentFullPath] = exception.Message;
                    break;
                }
            }
        }

        return failures;
    }

    private static string ComputeArchivedPath(string originalFullPath, string originalsRoot, string projectRoot) =>
        Path.Combine(originalsRoot, Path.GetRelativePath(projectRoot, originalFullPath));

    /// <summary>
    /// True when the scope enumeration would never reach <paramref name="fullPath"/>: it sits outside
    /// <paramref name="projectRoot"/> altogether, or inside one of the folders
    /// <see cref="NamingScopeRules"/> reserves. An assembly routinely references documents from those
    /// folders and from outside the project root altogether - Content Center parts, 3rd Party Hardware, a
    /// part owned by another project - and those files belong to someone else: the tool must neither
    /// rename them nor relocate their originals. An empty root means the caller could not determine one,
    /// and nothing is excluded then.
    /// </summary>
    private static bool IsOutsideProjectScope(string fullPath, string? projectRoot)
    {
        if (string.IsNullOrEmpty(projectRoot))
        {
            return false;
        }

        if (!TryGetPathUnderRoot(fullPath, projectRoot, out string relative))
        {
            return true;
        }

        string[] segments = relative.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (NamingScopeRules.IsExcludedFolderName(segments[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves <paramref name="fullPath"/> to a path relative to <paramref name="projectRoot"/>, and
    /// returns false when the result escapes the root. Path.GetRelativePath happily answers '..\..\x' for
    /// a path outside the root, and Path.Combine then walks back out of the archive folder, so every
    /// caller that builds a path under the root has to reject a rooted result and any '..' segment.
    /// </summary>
    private static bool TryGetPathUnderRoot(string fullPath, string projectRoot, out string relative)
    {
        relative = Path.GetRelativePath(projectRoot, fullPath);
        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        foreach (string segment in relative.Split(PathSeparators))
        {
            if (segment == "..")
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the path of the first file that already owns this proposed file name - a scope file in
    /// any folder other than the row being renamed, or an earlier proposal in this plan - or null when
    /// the name is free. Comparison is by file name only and case-insensitive, matching how Inventor's
    /// unique-filenames mode resolves references.
    /// </summary>
    private static string? FindFileNameCollision(
        string proposedFileName,
        string rowFullPath,
        ILookup<string, string> scopePathsByFileName,
        Dictionary<string, string> proposedNameOwners)
    {
        foreach (string scopePath in scopePathsByFileName[proposedFileName])
        {
            if (!string.Equals(scopePath, rowFullPath, StringComparison.OrdinalIgnoreCase))
            {
                return scopePath;
            }
        }

        return proposedNameOwners.TryGetValue(proposedFileName, out string? owner) ? owner : null;
    }

    /// <summary>
    /// Blocks on referencing documents that are open in the session but outside the active assembly and
    /// outside this rename's companion drawings. A companion is renamed alongside its model, so it is a
    /// handled reference; anything else would be left pointing at a name that no longer exists. Applies
    /// to the root row exactly as to any other: the root's own referencing documents are, by definition,
    /// all outside the snapshot (F1), for example the parent assembly the user activated this one from.
    /// Returns true when any blocker was added, so the caller can keep the row out of both Operations and
    /// VaultInstructions rather than listing an action the outstanding reference makes unsafe.
    /// </summary>
    private static bool AddExternalParentBlockers(
        List<string> blockers,
        NamingAnalysisRow row,
        List<(string Current, string New)> companionDrawings)
    {
        bool blocked = false;
        foreach (string externalParent in row.ExternalParentFullPaths)
        {
            if (companionDrawings.Any(companion =>
                string.Equals(companion.Current, externalParent, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            blockers.Add(
                $"'{row.CurrentFileName}' is also referenced by '{Path.GetFileName(externalParent)}' "
                + "which is not part of the active assembly; close or include it first.");
            blocked = true;
        }

        return blocked;
    }

    /// <summary>
    /// Returns true when any companion drawing must not be renamed by this tool. A Vault-managed drawing
    /// belongs to Vault Explorer's Rename command, which preserves history and the drawing's tracker; a
    /// read-only drawing cannot be saved over at all.
    /// </summary>
    private bool AddCompanionBlockers(
        List<string> blockers,
        NamingAnalysisRow row,
        List<(string Current, string New)> companionDrawings)
    {
        bool blocked = false;
        foreach ((string companionPath, string _) in companionDrawings)
        {
            string companionName = Path.GetFileName(companionPath);

            if (fileSystem.IsVaultManaged(companionPath))
            {
                blockers.Add(
                    $"'{companionName}' is Vault-managed; rename it in Vault Explorer "
                    + $"before renaming '{row.CurrentFileName}'.");
                blocked = true;
            }

            if (fileSystem.IsReadOnly(companionPath))
            {
                blockers.Add($"'{companionName}' is read-only and cannot be renamed alongside '{row.CurrentFileName}'.");
                blocked = true;
            }
        }

        return blocked;
    }

    private static string? DeterminePartNumberToSet(NamingAnalysisRow row, string proposedFileName, RenameOptions options)
    {
        if (!options.SetPartNumberProperty)
        {
            return null;
        }

        bool propertyIsEmpty = string.IsNullOrWhiteSpace(row.PartNumberProperty);
        bool propertyMatchesOldStem = row.PartNumberProperty == Path.GetFileNameWithoutExtension(row.CurrentFileName);
        if (!propertyIsEmpty && !propertyMatchesOldStem)
        {
            return null;
        }

        ParsedFileName reparsedTarget = FileNameParser.Parse(proposedFileName);
        return reparsedTarget.Token?.ToString();
    }

    private static List<(string Current, string New)> FindCompanionDrawings(
        NamingAnalysisRow row,
        string proposedFileName,
        IReadOnlyList<ScopeEntry> scope)
    {
        string directory = Path.GetDirectoryName(row.FullPath) ?? string.Empty;
        string currentStem = Path.GetFileNameWithoutExtension(row.CurrentFileName);
        string newStem = Path.GetFileNameWithoutExtension(proposedFileName);

        List<(string Current, string New)> companions = [];
        foreach (ScopeEntry entry in scope)
        {
            if (entry.Parsed.Kind is not (DocumentKind.Drawing or DocumentKind.Presentation))
            {
                continue;
            }

            string entryDirectory = Path.GetDirectoryName(entry.FullPath) ?? string.Empty;
            if (!string.Equals(entryDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string entryStem = Path.GetFileNameWithoutExtension(entry.FullPath);
            if (!string.Equals(entryStem, currentStem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string newPath = Path.Combine(directory, newStem + Path.GetExtension(entry.FullPath));
            companions.Add((entry.FullPath, newPath));
        }

        return companions;
    }

    /// <summary>
    /// Returns the canonical name this row would take, or null when it must not be proposed.
    /// <paramref name="exhaustedSeries"/> is set only when the row genuinely needed a fresh number and
    /// the series had none left, so callers can tell "nothing to do" apart from "nothing available".
    /// </summary>
    private static string? TryProposeFileName(
        NumberAllocator allocator,
        ParsedFileName parsed,
        DocumentKind kind,
        bool isRoot,
        ProjectNumber project,
        out NumberSeries? exhaustedSeries)
    {
        exhaustedSeries = null;

        if (parsed.State is NameState.Canonical or NameState.Unparseable || kind is not (DocumentKind.Part or DocumentKind.Assembly))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(parsed.Description))
        {
            // There is nothing to name the file after. Falling back to the stem would carry the old,
            // retired token into the new description, producing names like '124-0004 101-0001-A0.ipt'.
            return null;
        }

        NumberSeries requiredSeries = kind == DocumentKind.Part ? NumberSeries.Part : NumberSeries.Assembly;
        bool tokenBelongsToProject = parsed.Token is NamingToken candidate && candidate.Project.Value == project.Value;

        if (tokenBelongsToProject && IsDuplicatedNumber(allocator, parsed.Token!.Value))
        {
            // The number is already shared by two or more distinct files in scope; reusing it here would
            // keep the duplicate rather than resolve it. Abstain and let BuildRow surface the reason.
            return null;
        }

        bool hasReusableToken = tokenBelongsToProject && parsed.Token!.Value.Number.Series == requiredSeries;

        NamingToken token;
        if (hasReusableToken)
        {
            token = parsed.Token!.Value;
        }
        else
        {
            if (allocator.Allocate(requiredSeries) is not ItemNumber allocated)
            {
                // Every number in the series is taken. Abstain and tell the caller which series ran out,
                // so it can say so instead of throwing out of Analyze and never opening the window.
                exhaustedSeries = requiredSeries;
                return null;
            }

            token = new NamingToken(project, allocated);
        }

        string description = parsed.Description!;

        AssemblyRole? role = kind == DocumentKind.Assembly
            ? parsed.AssemblyRole ?? (isRoot ? AssemblyRole.Main : AssemblyRole.Sub)
            : null;

        return FileNameFormatter.Format(token, description, kind, role);
    }

    /// <summary>
    /// True when two or more distinct file names in the allocator's scope already carry this token's
    /// number. Reusing such a number on rename would perpetuate the duplicate rather than resolve it.
    /// </summary>
    private static bool IsDuplicatedNumber(NumberAllocator allocator, NamingToken token) =>
        allocator.Duplicates(token.Number.Series).Any(group => group.Number.Value == token.Number.Value);

    /// <summary>
    /// The one operator-facing sentence for a file the project does not own. It names the boundary in
    /// full, because "outside the project scope" on its own does not tell an engineer which folder rule
    /// put their file there.
    /// </summary>
    private static string DescribeOutOfScope(string fileName, string projectRoot)
    {
        IReadOnlyList<string> excludedFolders = NamingScopeRules.ExcludedFolderNames;
        return $"'{fileName}' is outside the project scope ({projectRoot}, excluding "
            + $"{string.Join(", ", excludedFolders.Take(excludedFolders.Count - 1))} and {excludedFolders[^1]}) "
            + "and is never renamed.";
    }

    /// <summary>
    /// The one operator-facing sentence for an exhausted series, shared by the row reason and the plan
    /// blocker so the two can never drift apart. The maximum comes from ItemNumber rather than a literal.
    /// </summary>
    private static string DescribeExhaustedSeries(NumberSeries series, ProjectNumber project)
    {
        int max = series == NumberSeries.Part ? ItemNumber.PartMaxValue : ItemNumber.AssemblyMaxValue;
        string seriesName = series == NumberSeries.Part ? "part" : "assembly";
        return $"The {seriesName} number series for project {project} is exhausted "
            + $"({new ItemNumber(series, max)}); no number can be allocated.";
    }

    private static NamingAnalysisRow BuildRow(
        DocumentSnapshot document,
        ActiveAssemblySnapshot scan,
        INamingFileSystem fileSystem,
        NumberAllocator? allocator,
        ProjectNumber? project,
        string? projectRoot)
    {
        string currentFileName = Path.GetFileName(document.FullPath);
        ParsedFileName parsed = FileNameParser.Parse(currentFileName);
        VaultState vaultState = fileSystem.IsVaultManaged(document.FullPath) ? VaultState.Managed : VaultState.Unmanaged;

        if (IsOutsideProjectScope(document.FullPath, projectRoot))
        {
            // Not this project's file to rename, and not this project's number to spend on it. Returning
            // before TryProposeFileName also keeps the allocator untouched, so an out-of-scope reference
            // never burns a number that an in-scope row would then skip.
            return new NamingAnalysisRow(
                document.FullPath,
                currentFileName,
                document.Kind,
                document.IsRoot,
                document.IsModifiable,
                document.IsDirty,
                document.ParentFullPaths,
                document.ExternalParentFullPaths,
                document.PartNumberProperty,
                parsed,
                vaultState,
                null,
                RenameAction.None,
                [DescribeOutOfScope(currentFileName, projectRoot!)]);
        }

        NumberSeries? exhaustedSeries = null;
        string? proposedFileName = allocator is not null && project is ProjectNumber confirmedProject
            ? TryProposeFileName(allocator, parsed, document.Kind, document.IsRoot, confirmedProject, out exhaustedSeries)
            : null;

        List<string> reasons = [];
        RenameAction action;
        if (proposedFileName is null)
        {
            action = RenameAction.None;
            if (exhaustedSeries is NumberSeries ranOut && project is ProjectNumber exhaustedProject)
            {
                reasons.Add(DescribeExhaustedSeries(ranOut, exhaustedProject));
            }

            if (allocator is not null
                && project is ProjectNumber duplicateCheckProject
                && parsed.Token is NamingToken candidateToken
                && candidateToken.Project.Value == duplicateCheckProject.Value
                && IsDuplicatedNumber(allocator, candidateToken))
            {
                reasons.Add($"'{candidateToken}' is used by more than one file; resolve the duplicate manually.");
            }

            if (parsed.State is not (NameState.Canonical or NameState.Unparseable)
                && string.IsNullOrWhiteSpace(parsed.Description))
            {
                reasons.Add($"'{currentFileName}' has a number but no description; add a description manually.");
            }
        }
        else if (scan.RootIsDirty
            || scan.RootHasMissingReferences
            || (vaultState != VaultState.Managed && !document.IsModifiable))
        {
            // A Vault-managed row's own IsModifiable never blocks it (N1): it is never renamed locally
            // either way, so a checked-in defect belongs on the exported Vault plan, not on this row.
            action = RenameAction.Blocked;
            if (scan.RootIsDirty)
            {
                reasons.Add("The root assembly has unsaved changes.");
            }

            if (scan.RootHasMissingReferences)
            {
                reasons.Add("The root assembly has missing references.");
            }

            if (vaultState != VaultState.Managed && !document.IsModifiable)
            {
                reasons.Add($"'{currentFileName}' is not modifiable.");
            }
        }
        else
        {
            action = vaultState == VaultState.Managed ? RenameAction.VaultRename : RenameAction.Rename;
        }

        return new NamingAnalysisRow(
            document.FullPath,
            currentFileName,
            document.Kind,
            document.IsRoot,
            document.IsModifiable,
            document.IsDirty,
            document.ParentFullPaths,
            document.ExternalParentFullPaths,
            document.PartNumberProperty,
            parsed,
            vaultState,
            proposedFileName,
            action,
            reasons);
    }

    private static Dictionary<string, int> ComputeDepths(IReadOnlyList<NamingAnalysisRow> rows)
    {
        Dictionary<string, NamingAnalysisRow> byPath = rows.ToDictionary(row => row.FullPath, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int> depths = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> visiting = new(StringComparer.OrdinalIgnoreCase);

        int DepthOf(string path)
        {
            if (depths.TryGetValue(path, out int cached))
            {
                return cached;
            }

            if (!byPath.TryGetValue(path, out NamingAnalysisRow? row))
            {
                return 0;
            }

            if (row.IsRoot)
            {
                depths[path] = 0;
                return 0;
            }

            if (!visiting.Add(path))
            {
                // Defensive: a reference cycle should never occur in a valid assembly tree.
                return 1;
            }

            int depth = row.ParentFullPaths.Count == 0 ? 1 : 1 + row.ParentFullPaths.Max(DepthOf);
            visiting.Remove(path);
            depths[path] = depth;
            return depth;
        }

        foreach (NamingAnalysisRow row in rows)
        {
            DepthOf(row.FullPath);
        }

        return depths;
    }

    private static List<T> OrderLeafFirst<T>(
        List<T> items,
        Func<T, string> pathOf,
        Dictionary<string, NamingAnalysisRow> rowsByPath,
        Dictionary<string, int> depths)
    {
        int GroupOf(T item)
        {
            NamingAnalysisRow row = rowsByPath[pathOf(item)];
            if (row.Kind == DocumentKind.Part)
            {
                return 0;
            }

            return row.IsRoot ? 2 : 1;
        }

        return [.. items
            .OrderBy(GroupOf)
            .ThenByDescending(item => depths.GetValueOrDefault(pathOf(item)))
            .ThenBy(pathOf, StringComparer.OrdinalIgnoreCase)];
    }

    private static List<string> ComputeParentSaveOrder(IReadOnlyList<RenameOperation> operations, Dictionary<string, int> depths)
    {
        HashSet<string> distinctParents = new(StringComparer.OrdinalIgnoreCase);
        foreach (RenameOperation operation in operations)
        {
            foreach (string parent in operation.ParentsToSave)
            {
                distinctParents.Add(parent);
            }
        }

        return [.. distinctParents
            .OrderByDescending(path => depths.GetValueOrDefault(path))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)];
    }
}
