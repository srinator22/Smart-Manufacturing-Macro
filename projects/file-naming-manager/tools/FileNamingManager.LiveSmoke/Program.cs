// Purpose: Live smoke harness for section 1 of projects/file-naming-manager/docs/TEST_PLAN.md,
//   plus a live both-directions check of the external-parent guard (F1). On a pass it stamps
//   tests/live-evidence/LIVE_EVIDENCE.json so the gate can tell when the recorded live run no
//   longer attests to the adapter, workflow and harness sources in the tree.
// Inputs: No arguments. A machine with Inventor 2027 installed and NO Inventor.exe running.
// Outputs: %TEMP%\naming-live-smoke\smoke-<stamp>.log (local, never committed), the evidence
//   stamp on pass, exit 0 (pass) / 1 (fail) / 2 (unclaimed).
// Dependencies: Autodesk.Inventor.Interop v31, FileNamingManager Core/Application/Infrastructure/InventorAdapter.
// Assumptions: Everything runs on this STA thread. Every path the harness writes lives under
//   %TEMP%\naming-live-smoke\ or the evidence stamp; the harness refuses to run if a computed
//   fixture path escapes that root. Templates are read, never written, from whatever the active
//   default Inventor project resolves. The evidence stamp records no path, host or user name.
// Validation source: docs/INVENTOR_2027_API_COMPATIBILITY.md "Live smoke fixture" rows.

#if INVENTOR_INTEROP

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileNamingManager.Application;
using FileNamingManager.Core;
using FileNamingManager.Infrastructure;
using FileNamingManager.InventorAdapter;
using IOPath = System.IO.Path;

namespace FileNamingManager.LiveSmoke;

/// <summary>
/// Every fixture path of one smoke run, old names and the names the product is expected to produce.
/// OtherRig is the extra assembly that references the spacer plate from outside the active tree; it is
/// never renamed, and it exists only to drive the external-parent guard in both directions.
/// </summary>
internal sealed record SmokePaths(
    string ProjectRoot,
    string PartsDirectory,
    string AssembliesDirectory,
    string OldMesh,
    string Bracket,
    string OldSpacer,
    string OldSub,
    string OldRoot,
    string OtherRig,
    string NewMesh,
    string NewSpacer,
    string NewSub,
    string NewRoot);

internal static class Program
{
    /// <summary>
    /// Relative to the File Naming Manager project root, forward slashes: the one file whose blob id
    /// is reported as harnessVersion, and the harness's own contribution to the source hash.
    /// </summary>
    private const string HarnessSourceRelativePath = "tools/FileNamingManager.LiveSmoke/Program.cs";

    private static readonly List<string> Failures = [];
    private static readonly JsonSerializerOptions StampJsonOptions = new() { WriteIndented = true };
    private static TextWriter logWriter = TextWriter.Null;
    private static int passedChecks;
    private static string inventorDisplayName = "unknown";

    [STAThread]
    internal static int Main()
    {
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string tempRoot = IOPath.GetFullPath(IOPath.Combine(IOPath.GetTempPath(), "naming-live-smoke"));
        Directory.CreateDirectory(tempRoot);
        string logPath = IOPath.Combine(tempRoot, $"smoke-{stamp}.log");

        using var file = new StreamWriter(logPath, false) { AutoFlush = true };
        logWriter = file;

        Write($"naming-live-smoke {stamp}");
        Write($"log: {logPath}");

        string? productRoot = LocateProductRoot();
        string sourceHash = string.Empty;
        string harnessVersion = string.Empty;
        if (productRoot is null)
        {
            Write("evidence stamp: DISABLED - the File Naming Manager project root was not found above the build output");
        }
        else
        {
            (sourceHash, harnessVersion) = ComputeSourceHash(productRoot);
            Write($"sourceHash    = {sourceHash}");
            Write($"harnessVersion= {harnessVersion}");
        }

        List<int> before = InventorPids();
        if (before.Count > 0)
        {
            Write($"UNCLAIMED: Inventor already running (pids {string.Join(",", before)})");
            Console.WriteLine("UNCLAIMED: Inventor already running");
            return 2;
        }

        Write("precondition: no Inventor.exe running - OK");

        string runRoot = IOPath.GetFullPath(IOPath.Combine(tempRoot, stamp));
        string projectRoot = IOPath.GetFullPath(IOPath.Combine(runRoot, "P901 Smoke"));
        string parts = IOPath.Combine(projectRoot, "Parts");
        string assemblies = IOPath.Combine(projectRoot, "Assemblies");

        var paths = new SmokePaths(
            projectRoot,
            parts,
            assemblies,
            IOPath.Combine(parts, "high density mesh material.ipt"),
            IOPath.Combine(parts, "901-0004 Existing Bracket.ipt"),
            IOPath.Combine(parts, "spacer plate.ipt"),
            IOPath.Combine(assemblies, "lifting frame.iam"),
            IOPath.Combine(assemblies, "smoke rig.iam"),
            IOPath.Combine(assemblies, "other rig.iam"),
            IOPath.Combine(parts, "901-0006 high density mesh material.ipt"),
            IOPath.Combine(parts, "901-0005 spacer plate.ipt"),
            IOPath.Combine(assemblies, "901-A002 lifting frame (sub-assembly).iam"),
            IOPath.Combine(assemblies, "901-A001 smoke rig (main assembly).iam"));

        foreach (string path in new[] { runRoot, projectRoot, parts, assemblies })
        {
            if (!IsInsideSandbox(path, tempRoot))
            {
                Write($"REFUSED: computed path escapes the sandbox: '{path}' (sandbox '{tempRoot}')");
                Console.WriteLine("SMOKE FAIL: computed path escaped the sandbox");
                return 1;
            }
        }

        Directory.CreateDirectory(parts);
        Directory.CreateDirectory(assemblies);
        Write($"sandbox root : {tempRoot}");
        Write($"project root : {projectRoot}");

        bool firstExited = RunFirstInventor(paths);
        if (firstExited)
        {
            RunColdReopenInventor(paths);
        }
        else
        {
            Check("cold-reopen-attempted", false, "the first Inventor did not exit, so no second instance was started");
        }

        string last = Failures.Count == 0 ? "SMOKE PASS" : $"SMOKE FAIL: {Failures[0]}";
        Write(string.Empty);
        Write($"assertions passed: {passedChecks.ToString(CultureInfo.InvariantCulture)}");
        Write($"failures: {Failures.Count.ToString(CultureInfo.InvariantCulture)}");
        foreach (string failure in Failures)
        {
            Write($"  - {failure}");
        }

        if (Failures.Count == 0 && productRoot is not null)
        {
            WriteEvidenceStamp(productRoot, sourceHash, harnessVersion);
        }

        Write(last);
        Console.WriteLine(last);
        return Failures.Count == 0 ? 0 : 1;
    }

    /// <summary>
    /// First hidden Inventor: build the fixture, drive the external-parent guard both ways, run the
    /// normal Analyze/Plan/Execute, verify the disk. Returns true when the process this method started
    /// is confirmed gone, which gates the cold reopen.
    /// </summary>
    private static bool RunFirstInventor(SmokePaths paths)
    {
        Inventor.Application? app = null;
        int pid = 0;
        try
        {
            (app, pid) = StartInventor("first");
            RunSmoke(app, paths);
        }
#pragma warning disable CA1031 // The harness must always reach the Inventor shutdown path.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Write($"UNHANDLED {exception.GetType().Name}: {exception.Message}");
            Write(exception.ToString());
            Failures.Add($"unhandled {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            Section("9a. shut the first Inventor down");
        }

        return Shutdown(app, pid);
    }

    /// <summary>
    /// Second hidden Inventor, started only after the first is confirmed gone, so the reopen reads the
    /// renamed assembly from disk rather than returning the still-resident in-session document.
    /// </summary>
    private static void RunColdReopenInventor(SmokePaths paths)
    {
        Section("8. cold-reopen proof in a SECOND Inventor process");
        Inventor.Application? app = null;
        int pid = 0;
        try
        {
            (app, pid) = StartInventor("second");
            ColdReopen(app, paths);
        }
#pragma warning disable CA1031 // The harness must always reach the Inventor shutdown path.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            Write($"UNHANDLED {exception.GetType().Name}: {exception.Message}");
            Write(exception.ToString());
            Failures.Add($"unhandled during cold reopen {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            Section("9b. shut the second Inventor down");
        }

        Shutdown(app, pid);
    }

    private static (Inventor.Application App, int Pid) StartInventor(string label)
    {
        List<int> before = InventorPids();
        Write($"starting the {label} Inventor through ProgID 'Inventor.Application' (pids before: [{string.Join(",", before)}])");

        Type comType = Type.GetTypeFromProgID("Inventor.Application", true)
            ?? throw new InvalidOperationException("ProgID 'Inventor.Application' did not resolve to a type.");
        object instance = Activator.CreateInstance(comType)
            ?? throw new InvalidOperationException("Activator.CreateInstance returned null for Inventor.Application.");
        var app = (Inventor.Application)instance;

        app.Visible = false;
        app.SilentOperation = true;
        Write("Visible=false, SilentOperation=true set");

        List<int> after = InventorPids();
        List<int> added = [.. after.Where(candidate => !before.Contains(candidate))];
        int pid = added.Count == 1 ? added[0] : 0;
        Write($"Inventor pids after start: [{string.Join(",", after)}]; started pid: {(pid == 0 ? "UNKNOWN" : pid.ToString(CultureInfo.InvariantCulture))}");
        inventorDisplayName = app.SoftwareVersion.DisplayName;
        Write($"Inventor SoftwareVersion.DisplayVersion = {app.SoftwareVersion.DisplayVersion}");
        Write($"Inventor SoftwareVersion.DisplayName    = {inventorDisplayName}");
        return (app, pid);
    }

    private static void RunSmoke(Inventor.Application app, SmokePaths paths)
    {
        BuildFixture(app, paths);

        Section("4. reopen the root as the ACTIVE document, with 'other rig.iam' also open");
        Inventor.Document opened = app.Documents.Open(paths.OldRoot, true);
        Write($"opened root: {opened.FullFileName}");

        Inventor.Document otherRig = app.Documents.Open(paths.OtherRig, false);
        Write($"opened invisibly: {otherRig.FullFileName}");

        string activePath = app.ActiveDocument?.FullFileName ?? "<null>";
        Write($"ActiveDocument.FullFileName = {activePath}");
        if (!string.Equals(activePath, paths.OldRoot, StringComparison.OrdinalIgnoreCase))
        {
            Write("the root is not active; calling Document.Activate() on it");
            opened.Activate();
            activePath = app.ActiveDocument?.FullFileName ?? "<null>";
            Write($"ActiveDocument.FullFileName after Activate = {activePath}");
        }
        else
        {
            Write("the root was already active; Activate() was not needed");
        }

        Check("active-document-is-root", string.Equals(activePath, paths.OldRoot, StringComparison.OrdinalIgnoreCase), activePath);
        Write($"root Dirty={opened.Dirty} IsModifiable={opened.IsModifiable} HasReferencesMissing={((Inventor.AssemblyDocument)opened).HasReferencesMissing}");
        LogOpenDocuments(app, "before phase A");

        var gateway = new InventorNamingGateway(app);
        var workflow = new FileNamingWorkflow(gateway, new PhysicalNamingFileSystem(), new SystemClock());

        PhaseAExternalParentGuard(workflow, paths);

        Section("5b. close 'other rig.iam' (Close(true)) so nothing outside the tree is open");
        otherRig.Close(true);
        LogOpenDocuments(app, "after closing other rig.iam");

        PhaseBNormalRun(app, gateway, workflow, paths);
    }

    private static void BuildFixture(Inventor.Application app, SmokePaths paths)
    {
        Section("3. build the fixture through the Inventor API");

        string partTemplate = app.FileManager.GetTemplateFile(
            Inventor.DocumentTypeEnum.kPartDocumentObject,
            Inventor.SystemOfMeasureEnum.kDefaultSystemOfMeasure,
            Inventor.DraftingStandardEnum.kDefault_DraftingStandard,
            Type.Missing);
        string assemblyTemplate = app.FileManager.GetTemplateFile(
            Inventor.DocumentTypeEnum.kAssemblyDocumentObject,
            Inventor.SystemOfMeasureEnum.kDefaultSystemOfMeasure,
            Inventor.DraftingStandardEnum.kDefault_DraftingStandard,
            Type.Missing);
        Write($"part template (read only)     : {partTemplate}");
        Write($"assembly template (read only) : {assemblyTemplate}");

        foreach (string path in new[] { paths.OldMesh, paths.Bracket, paths.OldSpacer })
        {
            Inventor.Document part = app.Documents.Add(Inventor.DocumentTypeEnum.kPartDocumentObject, partTemplate, false);
            part.SaveAs(path, false);
            part.Close(true);
            Write($"created part: {path}");
        }

        Inventor.AssemblyDocument sub =
            (Inventor.AssemblyDocument)app.Documents.Add(Inventor.DocumentTypeEnum.kAssemblyDocumentObject, assemblyTemplate, false);
        sub.ComponentDefinition.Occurrences.Add(paths.OldSpacer, app.TransientGeometry.CreateMatrix());
        sub.SaveAs(paths.OldSub, false);
        sub.Close(true);
        Write($"created sub-assembly: {paths.OldSub} (1 occurrence: spacer plate)");

        Inventor.AssemblyDocument other =
            (Inventor.AssemblyDocument)app.Documents.Add(Inventor.DocumentTypeEnum.kAssemblyDocumentObject, assemblyTemplate, false);
        other.ComponentDefinition.Occurrences.Add(paths.OldSpacer, app.TransientGeometry.CreateMatrix());
        other.SaveAs(paths.OtherRig, false);
        other.Close(true);
        Write($"created outside assembly: {paths.OtherRig} (1 occurrence: spacer plate)");

        Inventor.AssemblyDocument root =
            (Inventor.AssemblyDocument)app.Documents.Add(Inventor.DocumentTypeEnum.kAssemblyDocumentObject, assemblyTemplate, false);
        root.ComponentDefinition.Occurrences.Add(paths.OldMesh, app.TransientGeometry.CreateMatrix());
        root.ComponentDefinition.Occurrences.Add(paths.Bracket, app.TransientGeometry.CreateMatrix());
        root.ComponentDefinition.Occurrences.Add(paths.OldSub, app.TransientGeometry.CreateMatrix());
        root.SaveAs(paths.OldRoot, false);
        root.Close(true);
        Write($"created root assembly: {paths.OldRoot} (3 occurrences)");

        LogOpenDocuments(app, "after fixture creation");
        app.Documents.CloseAll(true);
        LogOpenDocuments(app, "after CloseAll(true)");
    }

    /// <summary>
    /// Phase A: 'other rig.iam' is open and references the spacer plate from outside the active tree.
    /// The guard must surface it on the row and block the plan, and the row must not reach Operations.
    /// </summary>
    private static void PhaseAExternalParentGuard(FileNamingWorkflow workflow, SmokePaths paths)
    {
        Section("5a. PHASE A - Analyze and Plan with 'other rig.iam' open (external-parent guard ON)");

        NamingAnalysis analysis = workflow.Analyze(null);
        LogAnalysis(analysis, "phase A");
        Check("extguard-analysis-success", analysis.IsSuccess, analysis.ErrorMessage ?? string.Empty);
        if (!analysis.IsSuccess)
        {
            Write("STOP phase A: analysis failed.");
            return;
        }

        NamingAnalysisRow? spacerRow = analysis.Rows.FirstOrDefault(
            row => string.Equals(row.FullPath, paths.OldSpacer, StringComparison.OrdinalIgnoreCase));
        Check("extguard-spacer-row-present", spacerRow is not null, string.Empty);
        if (spacerRow is not null)
        {
            Write($"spacer plate.ipt ExternalParentFullPaths = [{string.Join(" | ", spacerRow.ExternalParentFullPaths)}]");
            Check(
                "extguard-spacer-external-parent-is-other-rig",
                spacerRow.ExternalParentFullPaths.Any(parent => string.Equals(parent, paths.OtherRig, StringComparison.OrdinalIgnoreCase)),
                $"got [{string.Join(" | ", spacerRow.ExternalParentFullPaths)}]");
        }

        NamingAnalysisRow? rootRow = analysis.Rows.FirstOrDefault(row => row.IsRoot);
        Write($"root ExternalParentFullPaths = [{string.Join(" | ", rootRow?.ExternalParentFullPaths ?? [])}]");
        Check(
            "extguard-root-has-no-external-parents",
            rootRow is not null && rootRow.ExternalParentFullPaths.Count == 0,
            $"got [{string.Join(" | ", rootRow?.ExternalParentFullPaths ?? [])}]");

        RenamePlan plan = workflow.Plan(analysis, new ProjectNumber(901), new RenameOptions());
        LogPlan(plan, "phase A");

        Check("extguard-plan-has-blockers", plan.Blockers.Count > 0, "expected at least one blocker while other rig.iam is open");
        string? namingBlocker = plan.Blockers.FirstOrDefault(blocker =>
            blocker.Contains("spacer plate.ipt", StringComparison.OrdinalIgnoreCase)
            && blocker.Contains("other rig.iam", StringComparison.OrdinalIgnoreCase));
        Check(
            "extguard-blocker-names-both-files",
            namingBlocker is not null,
            namingBlocker is null ? $"blockers were [{string.Join(" || ", plan.Blockers)}]" : $"verbatim: {namingBlocker}");

        Check(
            "extguard-operations-exclude-spacer",
            !plan.Operations.Any(operation => string.Equals(operation.CurrentFullPath, paths.OldSpacer, StringComparison.OrdinalIgnoreCase)),
            $"operations were [{string.Join(" | ", plan.Operations.Select(operation => IOPath.GetFileName(operation.CurrentFullPath)))}]");
        Check(
            "extguard-vault-instructions-exclude-spacer",
            !plan.VaultInstructions.Any(instruction => string.Equals(instruction.CurrentFullPath, paths.OldSpacer, StringComparison.OrdinalIgnoreCase)),
            string.Empty);
        Check("extguard-plan-cannot-execute", !plan.CanExecute, string.Empty);
    }

    /// <summary>
    /// Phase B: nothing outside the active tree is open any more, so the guard must stay silent and the
    /// whole rename must run to completion.
    /// </summary>
    private static void PhaseBNormalRun(
        Inventor.Application app,
        InventorNamingGateway gateway,
        FileNamingWorkflow workflow,
        SmokePaths paths)
    {
        Section("5c. PHASE B - Analyze with only the active tree open");

        NamingAnalysis analysis = workflow.Analyze(null);
        LogAnalysis(analysis, "phase B");

        Check("analysis-success", analysis.IsSuccess, analysis.ErrorMessage ?? string.Empty);
        Check(
            "analysis-project-root-is-P901-Smoke",
            string.Equals(analysis.ProjectRootPath, paths.ProjectRoot, StringComparison.OrdinalIgnoreCase),
            $"got '{analysis.ProjectRootPath}'");
        Check(
            "analysis-suggests-901",
            analysis.SuggestedProject is ProjectNumber suggested && suggested.Value == 901,
            $"got '{(analysis.SuggestedProject is ProjectNumber s ? s.ToString() : "<null>")}'");
        Check("analysis-has-5-rows", analysis.Rows.Count == 5, $"got {analysis.Rows.Count.ToString(CultureInfo.InvariantCulture)}");
        Check(
            "analysis-no-external-parents",
            analysis.Rows.All(row => row.ExternalParentFullPaths.Count == 0),
            string.Join(" || ", analysis.Rows.Where(row => row.ExternalParentFullPaths.Count > 0)
                .Select(row => $"{row.CurrentFileName}: {string.Join(",", row.ExternalParentFullPaths)}")));

        if (!analysis.IsSuccess)
        {
            Write("STOP: analysis failed; not planning.");
            return;
        }

        Section("6a. Plan");
        RenamePlan plan = workflow.Plan(analysis, new ProjectNumber(901), new RenameOptions());
        LogPlan(plan, "phase B");

        Check("plan-has-zero-blockers", plan.Blockers.Count == 0, string.Join(" || ", plan.Blockers));
        if (plan.Blockers.Count > 0)
        {
            Write("STOP: the plan has blockers; not executing. Blockers verbatim:");
            foreach (string blocker in plan.Blockers)
            {
                Write($"  BLOCKER: {blocker}");
            }

            return;
        }

        Check("plan-has-4-operations", plan.Operations.Count == 4, $"got {plan.Operations.Count.ToString(CultureInfo.InvariantCulture)}");

        var expectedTargets = new[]
        {
            (paths.OldSpacer, paths.NewSpacer),
            (paths.OldMesh, paths.NewMesh),
            (paths.OldSub, paths.NewSub),
            (paths.OldRoot, paths.NewRoot),
        };
        foreach ((string current, string expected) in expectedTargets)
        {
            RenameOperation? match = plan.Operations.FirstOrDefault(
                operation => string.Equals(operation.CurrentFullPath, current, StringComparison.OrdinalIgnoreCase));
            Check(
                $"plan-target-{IOPath.GetFileName(current)}",
                match is not null && string.Equals(match.NewFullPath, expected, StringComparison.OrdinalIgnoreCase),
                match is null ? "no operation for this file" : $"got '{IOPath.GetFileName(match.NewFullPath)}', expected '{IOPath.GetFileName(expected)}'");
        }

        Section("6b. Execute");
        RenameExecution execution = workflow.Execute(plan);
        Write($"Items ({execution.Items.Count.ToString(CultureInfo.InvariantCulture)}):");
        foreach (RenameItemResult item in execution.Items)
        {
            Write($"  Succeeded={item.Succeeded} ModelRenamed={item.ModelRenamed} {IOPath.GetFileName(item.CurrentFullPath)} -> {IOPath.GetFileName(item.NewFullPath)}");
            if (item.ErrorMessage is not null)
            {
                Write($"      ERROR: {item.ErrorMessage}");
            }
        }

        Write($"ParentSaveFailures ({execution.ParentSaveFailures.Count.ToString(CultureInfo.InvariantCulture)}):");
        foreach (ParentSaveFailure failure in execution.ParentSaveFailures)
        {
            Write($"  {failure.ParentFullPath}");
            Write($"      ERROR: {failure.ErrorMessage}");
        }

        Write($"ArchiveFailures ({execution.ArchiveFailures.Count.ToString(CultureInfo.InvariantCulture)}):");
        foreach (ArchiveFailure failure in execution.ArchiveFailures)
        {
            Write($"  {failure.OriginalPath}");
            Write($"      ERROR: {failure.ErrorMessage}");
        }

        Write($"OriginalsMoved = {execution.OriginalsMoved}");
        if (execution.Manifest is null)
        {
            Write("Manifest = <null>");
        }
        else
        {
            Write($"Manifest timestamp={execution.Manifest.Timestamp.ToString("O", CultureInfo.InvariantCulture)} root={execution.Manifest.ProjectRoot}");
            foreach (RenameManifestEntry entry in execution.Manifest.Entries)
            {
                Write($"  {entry.OriginalPath}");
                Write($"      archived -> {entry.ArchivedPath}");
                Write($"      renamed  -> {entry.RenamedTo}");
                Write($"      Archived={entry.Archived} Error={entry.Error ?? "<null>"}");
            }
        }

        Check(
            "execute-every-item-succeeded",
            execution.Items.All(item => item.Succeeded),
            string.Join(" || ", execution.Items.Where(item => !item.Succeeded).Select(item => item.ErrorMessage)));
        Check(
            "execute-every-item-model-renamed",
            execution.Items.All(item => item.ModelRenamed),
            string.Join(" || ", execution.Items.Where(item => !item.ModelRenamed)
                .Select(item => $"{IOPath.GetFileName(item.CurrentFullPath)}: ModelRenamed=false Error={item.ErrorMessage ?? "<null>"}")));
        Check(
            "execute-has-4-items",
            execution.Items.Count == 4,
            $"got {execution.Items.Count.ToString(CultureInfo.InvariantCulture)}");
        Check(
            "execute-no-parent-save-failures",
            execution.ParentSaveFailures.Count == 0,
            string.Join(" || ", execution.ParentSaveFailures.Select(failure => $"{IOPath.GetFileName(failure.ParentFullPath)}: {failure.ErrorMessage}")));
        Check(
            "execute-no-archive-failures",
            execution.ArchiveFailures.Count == 0,
            string.Join(" || ", execution.ArchiveFailures.Select(failure => $"{IOPath.GetFileName(failure.OriginalPath)}: {failure.ErrorMessage}")));
        Check("execute-originals-moved", execution.OriginalsMoved, string.Empty);
        Check("execute-manifest-returned", execution.Manifest is not null, string.Empty);
        Check(
            "execute-manifest-has-4-entries",
            execution.Manifest is not null && execution.Manifest.Entries.Count == 4,
            $"got {(execution.Manifest?.Entries.Count ?? -1).ToString(CultureInfo.InvariantCulture)}");
        Check(
            "execute-manifest-all-archived",
            execution.Manifest is not null && execution.Manifest.Entries.All(entry => entry.Archived && entry.Error is null),
            string.Join(" || ", execution.Manifest?.Entries.Where(entry => !entry.Archived || entry.Error is not null)
                .Select(entry => $"{IOPath.GetFileName(entry.OriginalPath)}: Archived={entry.Archived} Error={entry.Error ?? "<null>"}") ?? []));

        gateway.CloseDocumentsOpenedHere();
        LogOpenDocuments(app, "after CloseDocumentsOpenedHere()");

        VerifyDisk(paths);
    }

    private static void LogAnalysis(NamingAnalysis analysis, string label)
    {
        Write($"--- analysis ({label}) ---");
        Write($"IsSuccess        = {analysis.IsSuccess}");
        Write($"ErrorMessage     = {analysis.ErrorMessage ?? "<null>"}");
        Write($"RootFullPath     = {analysis.RootFullPath ?? "<null>"}");
        Write($"ProjectRootPath  = {analysis.ProjectRootPath ?? "<null>"}");
        Write($"RootIsDirty      = {analysis.RootIsDirty}");
        Write($"MissingRefs      = {analysis.RootHasMissingReferences}");
        Write($"SuggestedProject = {(analysis.SuggestedProject is ProjectNumber suggested ? suggested.ToString() : "<null>")}");
        Write($"SuggestionSource = {(analysis.SuggestionSource is null ? "<null>" : analysis.SuggestionSource.ToString())}");
        Write($"Scope ({analysis.Scope.Count.ToString(CultureInfo.InvariantCulture)} files):");
        foreach (ScopeEntry entry in analysis.Scope)
        {
            Write($"  {entry.FullPath}  [{entry.Parsed.State}]");
        }

        Write($"Rows ({analysis.Rows.Count.ToString(CultureInfo.InvariantCulture)}), in snapshot order:");
        foreach (NamingAnalysisRow row in analysis.Rows)
        {
            Write($"  {row.CurrentFileName}");
            Write($"      Kind={row.Kind} IsRoot={row.IsRoot} IsModifiable={row.IsModifiable} IsDirty={row.IsDirty}");
            Write($"      State={row.Parsed.State} VaultState={row.VaultState} PartNumberProperty={row.PartNumberProperty ?? "<null>"}");
            Write($"      Proposed={row.ProposedFileName ?? "<none>"} Action={row.Action}");
            Write($"      ParentFullPaths=[{string.Join(" | ", row.ParentFullPaths)}]");
            Write($"      ExternalParentFullPaths=[{string.Join(" | ", row.ExternalParentFullPaths)}]");
            Write($"      Reasons=[{string.Join(" | ", row.Reasons)}]");
        }
    }

    private static void LogPlan(RenamePlan plan, string label)
    {
        Write($"--- plan ({label}) ---");
        Write($"ProjectRootPath = {plan.ProjectRootPath}");
        Write($"CanExecute = {plan.CanExecute}");
        Write($"Blockers ({plan.Blockers.Count.ToString(CultureInfo.InvariantCulture)}), verbatim:");
        foreach (string blocker in plan.Blockers)
        {
            Write($"  BLOCKER: {blocker}");
        }

        Write($"Operations ({plan.Operations.Count.ToString(CultureInfo.InvariantCulture)}), in execution order:");
        for (int index = 0; index < plan.Operations.Count; index++)
        {
            RenameOperation operation = plan.Operations[index];
            Write($"  [{index.ToString(CultureInfo.InvariantCulture)}] {IOPath.GetFileName(operation.CurrentFullPath)} -> {IOPath.GetFileName(operation.NewFullPath)}");
            Write($"      Kind={operation.Kind} PartNumberToSet={operation.PartNumberToSet ?? "<null>"}");
            Write($"      ParentsToSave=[{string.Join(" | ", operation.ParentsToSave.Select(IOPath.GetFileName))}]");
            Write($"      CompanionDrawings={operation.CompanionDrawings.Count.ToString(CultureInfo.InvariantCulture)}");
        }

        Write($"ParentSaveOrder ({plan.ParentSaveOrder.Count.ToString(CultureInfo.InvariantCulture)}):");
        foreach (string parent in plan.ParentSaveOrder)
        {
            Write($"  {parent}");
        }

        Write($"VaultInstructions ({plan.VaultInstructions.Count.ToString(CultureInfo.InvariantCulture)})");
    }

    private static void VerifyDisk(SmokePaths paths)
    {
        Section("7. verify on disk");

        string[] partFiles = [.. Directory.EnumerateFiles(paths.PartsDirectory).Select(IOPath.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)];
        string[] assemblyFiles = [.. Directory.EnumerateFiles(paths.AssembliesDirectory).Select(IOPath.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase)];
        Write($"Parts\\ files      : {string.Join(" | ", partFiles)}");
        Write($"Assemblies\\ files : {string.Join(" | ", assemblyFiles)}");
        Write($"Parts\\ subfolders      : {string.Join(" | ", Directory.EnumerateDirectories(paths.PartsDirectory).Select(IOPath.GetFileName))}");
        Write($"Assemblies\\ subfolders : {string.Join(" | ", Directory.EnumerateDirectories(paths.AssembliesDirectory).Select(IOPath.GetFileName))}");

        foreach (string expected in new[] { paths.NewMesh, paths.NewSpacer, paths.NewRoot, paths.NewSub })
        {
            Check($"disk-exists-{IOPath.GetFileName(expected)}", File.Exists(expected), expected);
        }

        Check("disk-901-0004-untouched", File.Exists(paths.Bracket), paths.Bracket);

        foreach (string gone in new[] { paths.OldMesh, paths.OldSpacer, paths.OldSub, paths.OldRoot })
        {
            Check($"disk-old-name-gone-{IOPath.GetFileName(gone)}", !File.Exists(gone), gone);
        }

        var expectedPartFiles = new[]
        {
            IOPath.GetFileName(paths.Bracket),
            IOPath.GetFileName(paths.NewSpacer),
            IOPath.GetFileName(paths.NewMesh),
        }.Order(StringComparer.OrdinalIgnoreCase).ToArray();

        // 'other rig.iam' is never renamed: it is outside the active tree, so it stays exactly as built.
        var expectedAssemblyFiles = new[]
        {
            IOPath.GetFileName(paths.NewRoot),
            IOPath.GetFileName(paths.NewSub),
            IOPath.GetFileName(paths.OtherRig),
        }.Order(StringComparer.OrdinalIgnoreCase).ToArray();

        Check(
            "disk-parts-folder-exact",
            partFiles.SequenceEqual(expectedPartFiles, StringComparer.OrdinalIgnoreCase),
            $"got [{string.Join(" | ", partFiles)}] expected [{string.Join(" | ", expectedPartFiles)}]");
        Check(
            "disk-assemblies-folder-exact",
            assemblyFiles.SequenceEqual(expectedAssemblyFiles, StringComparer.OrdinalIgnoreCase),
            $"got [{string.Join(" | ", assemblyFiles)}] expected [{string.Join(" | ", expectedAssemblyFiles)}]");

        string originalsRoot = IOPath.Combine(paths.ProjectRoot, "_renamed-originals");
        Write($"_renamed-originals exists = {Directory.Exists(originalsRoot)}");
        if (!Directory.Exists(originalsRoot))
        {
            Check("originals-folder-exists", false, originalsRoot);
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(originalsRoot, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            Write($"  {IOPath.GetRelativePath(originalsRoot, entry)}");
        }

        string[] stampFolders = [.. Directory.EnumerateDirectories(originalsRoot)];
        Check("originals-single-stamp-folder", stampFolders.Length == 1, $"got {stampFolders.Length.ToString(CultureInfo.InvariantCulture)}");
        if (stampFolders.Length != 1)
        {
            return;
        }

        string archive = stampFolders[0];
        foreach (string relative in new[]
        {
            IOPath.Combine("Parts", "high density mesh material.ipt"),
            IOPath.Combine("Parts", "spacer plate.ipt"),
            IOPath.Combine("Assemblies", "lifting frame.iam"),
            IOPath.Combine("Assemblies", "smoke rig.iam"),
        })
        {
            Check($"originals-archived-{relative}", File.Exists(IOPath.Combine(archive, relative)), IOPath.Combine(archive, relative));
        }

        string manifestPath = IOPath.Combine(archive, "manifest.json");
        Check("originals-manifest-json", File.Exists(manifestPath), manifestPath);
        if (!File.Exists(manifestPath))
        {
            return;
        }

        string manifestJson = File.ReadAllText(manifestPath);
        Write("manifest.json:");
        Write(manifestJson);

        int archivedTrue = manifestJson.Split("\"archived\": true", StringSplitOptions.None).Length - 1;
        int archivedFalse = manifestJson.Split("\"archived\": false", StringSplitOptions.None).Length - 1;
        Check(
            "manifest-json-4-archived-true",
            archivedTrue == 4 && archivedFalse == 0,
            $"archived:true={archivedTrue.ToString(CultureInfo.InvariantCulture)} archived:false={archivedFalse.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void ColdReopen(Inventor.Application app, SmokePaths paths)
    {
        if (!File.Exists(paths.NewRoot))
        {
            Check("cold-reopen-root-exists", false, $"'{paths.NewRoot}' does not exist");
            return;
        }

        LogOpenDocuments(app, "in the fresh process before Open");
        var reopened = (Inventor.AssemblyDocument)app.Documents.Open(paths.NewRoot, true);
        Write($"cold-reopened: {reopened.FullFileName}");
        Write($"HasReferencesMissing = {reopened.HasReferencesMissing}");
        Check("cold-reopen-no-missing-references", !reopened.HasReferencesMissing, string.Empty);

        var referenced = new List<string>();
        Inventor.DocumentsEnumerator enumerator = reopened.AllReferencedDocuments;
        for (int index = 1; index <= enumerator.Count; index++)
        {
            Inventor.Document child = enumerator[index];
            referenced.Add(child.FullFileName);
            Write($"  referenced: {child.FullFileName}");
            Write($"      Part Number iProperty = {ReadPartNumber(child)}");
        }

        Write($"  root Part Number iProperty = {ReadPartNumber((Inventor.Document)reopened)}");

        var expected = new HashSet<string>(
            new[] { paths.NewMesh, paths.NewSpacer, paths.NewSub, paths.Bracket },
            StringComparer.OrdinalIgnoreCase);
        var actual = new HashSet<string>(referenced, StringComparer.OrdinalIgnoreCase);
        Write($"missing from actual : {string.Join(" | ", expected.Except(actual, StringComparer.OrdinalIgnoreCase))}");
        Write($"unexpected in actual: {string.Join(" | ", actual.Except(expected, StringComparer.OrdinalIgnoreCase))}");
        Check("cold-reopen-reference-set-matches", actual.SetEquals(expected), string.Empty);

        Check(
            "cold-reopen-part-number-901-0005-spacer",
            string.Equals(ReadPartNumberAt(app, paths.NewSpacer), "901-0005", StringComparison.Ordinal),
            $"got '{ReadPartNumberAt(app, paths.NewSpacer)}'");
        Check(
            "cold-reopen-part-number-901-0006-mesh",
            string.Equals(ReadPartNumberAt(app, paths.NewMesh), "901-0006", StringComparison.Ordinal),
            $"got '{ReadPartNumberAt(app, paths.NewMesh)}'");
    }

    private static string ReadPartNumberAt(Inventor.Application app, string fullPath)
    {
        Inventor.Documents documents = app.Documents;
        for (int index = 1; index <= documents.Count; index++)
        {
            Inventor.Document document = documents[index];
            string name;
            try
            {
                name = document.FullFileName;
            }
            catch (COMException)
            {
                continue;
            }

            if (string.Equals(name, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return ReadPartNumber(document);
            }
        }

        return "<not open>";
    }

    private static string ReadPartNumber(Inventor.Document document)
    {
        try
        {
            return document.PropertySets["Design Tracking Properties"]["Part Number"].Value as string ?? "<null>";
        }
        catch (COMException exception)
        {
            return $"<COMException 0x{exception.HResult:X8}>";
        }
    }

    private static void LogOpenDocuments(Inventor.Application app, string label)
    {
        Inventor.Documents documents = app.Documents;
        Write($"open documents {label}: {documents.Count.ToString(CultureInfo.InvariantCulture)}");
        for (int index = 1; index <= documents.Count; index++)
        {
            Inventor.Document document = documents[index];
            string name;
            try
            {
                name = document.FullFileName;
            }
            catch (COMException exception)
            {
                name = $"<COMException 0x{exception.HResult:X8}>";
            }

            Write($"  [{index.ToString(CultureInfo.InvariantCulture)}] {name}");
        }
    }

    /// <summary>
    /// Quits the given Inventor and waits up to 60 s, then kills ONLY the pid this harness started.
    /// Returns true when that pid is confirmed gone.
    /// </summary>
    private static bool Shutdown(Inventor.Application? app, int startedPid)
    {
        if (app is null)
        {
            Write("no Inventor application object to quit");
            return startedPid != 0 && !InventorPids().Contains(startedPid);
        }

        try
        {
            app.Quit();
            Write("Application.Quit() returned");
        }
        catch (COMException exception)
        {
            Write($"Application.Quit() threw COMException 0x{exception.HResult:X8}: {exception.Message}");
        }

        try
        {
            Marshal.FinalReleaseComObject(app);
        }
        catch (ArgumentException exception)
        {
            Write($"FinalReleaseComObject threw: {exception.Message}");
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();

        if (startedPid == 0)
        {
            Write("started pid is UNKNOWN; not killing any process");
            Write($"Inventor pids still present: [{string.Join(",", InventorPids())}]");
            Failures.Add("started Inventor pid could not be identified");
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (!InventorPids().Contains(startedPid))
            {
                Write($"Inventor pid {startedPid.ToString(CultureInfo.InvariantCulture)} exited cleanly after {stopwatch.Elapsed.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)} s");
                return true;
            }

            Thread.Sleep(500);
        }

        Write($"Inventor pid {startedPid.ToString(CultureInfo.InvariantCulture)} still alive after 60 s; killing ONLY that pid");
        try
        {
            using Process process = Process.GetProcessById(startedPid);
            process.Kill(true);
            process.WaitForExit(30000);
            Write($"killed pid {startedPid.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (ArgumentException)
        {
            Write($"pid {startedPid.ToString(CultureInfo.InvariantCulture)} was already gone");
        }
        catch (InvalidOperationException exception)
        {
            Write($"could not kill pid {startedPid.ToString(CultureInfo.InvariantCulture)}: {exception.Message}");
        }

        Failures.Add($"Inventor pid {startedPid.ToString(CultureInfo.InvariantCulture)} did not exit within 60 s of Quit()");
        return !InventorPids().Contains(startedPid);
    }

    private static List<int> InventorPids()
    {
        List<int> pids = [];
        foreach (Process process in Process.GetProcessesByName("Inventor"))
        {
            pids.Add(process.Id);
            process.Dispose();
        }

        return pids;
    }

    private static bool IsInsideSandbox(string candidate, string sandboxRoot)
    {
        string full = IOPath.GetFullPath(candidate);
        if (full.Contains("Vault", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string prefix = sandboxRoot.TrimEnd(IOPath.DirectorySeparatorChar) + IOPath.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(full.TrimEnd(IOPath.DirectorySeparatorChar), sandboxRoot.TrimEnd(IOPath.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Walks up from the build output to the folder that holds docs/VAULT_RENAME_DESIGN.md, the same
    /// anchor the architecture tests use, so the stamp lands in the checkout the harness was built from.
    /// </summary>
    private static string? LocateProductRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(IOPath.Combine(current.FullName, "docs", "VAULT_RENAME_DESIGN.md")))
            {
                return current.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// The sources whose behaviour a live run attests to, relative to the project root with forward
    /// slashes: everything the adapter compiles, the Execute path in the workflow, and this harness.
    /// Kept byte-identical to the set in scripts/check-live-evidence.sh.
    /// </summary>
    private static List<string> HashSourceFiles(string productRoot)
    {
        var relative = new List<string>();
        string adapterRoot = IOPath.Combine(productRoot, "src", "FileNamingManager.InventorAdapter");
        foreach (string file in Directory.EnumerateFiles(adapterRoot, "*.cs", SearchOption.AllDirectories))
        {
            string[] parts = IOPath.GetRelativePath(productRoot, file)
                .Split(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);
            if (parts.Contains("bin", StringComparer.OrdinalIgnoreCase) || parts.Contains("obj", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            relative.Add(string.Join('/', parts));
        }

        relative.Add("src/FileNamingManager.Application/FileNamingWorkflow.cs");
        relative.Add(HarnessSourceRelativePath);
        return relative;
    }

    /// <summary>
    /// Returns (sourceHash, harnessVersion). sourceHash is SHA-256 over the ordinally sorted
    /// "&lt;relativePath&gt;:&lt;blobId&gt;" lines joined by LF; harnessVersion is this file's blob id.
    /// Blob ids are computed the way Git computes them so a plain `git hash-object` cross-checks them.
    /// </summary>
    private static (string SourceHash, string HarnessVersion) ComputeSourceHash(string productRoot)
    {
        List<string> relatives = HashSourceFiles(productRoot);
        var lines = new List<string>(relatives.Count);
        string harnessVersion = string.Empty;

        foreach (string relative in relatives)
        {
            string full = IOPath.Combine(productRoot, relative.Replace('/', IOPath.DirectorySeparatorChar));
            string blob = GitBlobId(full);
            if (string.Equals(relative, HarnessSourceRelativePath, StringComparison.Ordinal))
            {
                harnessVersion = blob;
            }

            lines.Add($"{relative}:{blob}");
        }

        lines.Sort(StringComparer.Ordinal);
        byte[] joined = Encoding.UTF8.GetBytes(string.Join('\n', lines));
        return (Convert.ToHexStringLower(SHA256.HashData(joined)), harnessVersion);
    }

    /// <summary>
    /// Git's blob id: SHA-1 over "blob &lt;byteLength&gt;\0" plus the content Git would store. The
    /// repository's .gitattributes declares `* text=auto eol=lf`, so the stored content is the file
    /// with CRLF pairs collapsed to LF.
    /// </summary>
    private static string GitBlobId(string fullPath)
    {
        byte[] stored = NormalizeToLf(File.ReadAllBytes(fullPath));
        byte[] header = Encoding.ASCII.GetBytes($"blob {stored.Length.ToString(CultureInfo.InvariantCulture)}\0");
        byte[] buffer = new byte[header.Length + stored.Length];
        header.CopyTo(buffer, 0);
        stored.CopyTo(buffer, header.Length);

#pragma warning disable CA5350 // Git blob ids are SHA-1 by definition; this is an identity check, not a security boundary.
        return Convert.ToHexStringLower(SHA1.HashData(buffer));
#pragma warning restore CA5350
    }

    private static byte[] NormalizeToLf(byte[] content)
    {
        var stored = new List<byte>(content.Length);
        for (int index = 0; index < content.Length; index++)
        {
            if (content[index] == (byte)'\r' && index + 1 < content.Length && content[index + 1] == (byte)'\n')
            {
                continue;
            }

            stored.Add(content[index]);
        }

        return [.. stored];
    }

    /// <summary>
    /// Records that a live run passed against exactly these sources. Deliberately carries no path, host
    /// or user name: the stamp is committed, the log that does name paths stays under %TEMP%.
    /// </summary>
    private static void WriteEvidenceStamp(string productRoot, string sourceHash, string harnessVersion)
    {
        string directory = IOPath.Combine(productRoot, "tests", "live-evidence");
        Directory.CreateDirectory(directory);

        var stamp = new JsonObject
        {
            ["recordedUtc"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["inventorDisplayName"] = inventorDisplayName,
            ["sourceHash"] = sourceHash,
            ["assertionsPassed"] = passedChecks,
            ["assertionsFailed"] = Failures.Count,
            ["result"] = "PASS",
            ["harnessVersion"] = harnessVersion,
        };

        // Written with LF explicitly: .gitattributes normalizes this file to LF, and Utf8JsonWriter
        // indents with Environment.NewLine, which would leave the checkout permanently dirty.
        string json = stamp.ToJsonString(StampJsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(IOPath.Combine(directory, "LIVE_EVIDENCE.json"), json + "\n");
        Write("evidence stamp written: tests/live-evidence/LIVE_EVIDENCE.json");
        Write(json);
    }

    private static void Check(string name, bool ok, string detail)
    {
        string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" -- {detail}";
        Write($"  {(ok ? "PASS" : "FAIL")} {name}{suffix}");
        if (ok)
        {
            passedChecks++;
        }
        else
        {
            Failures.Add($"{name}{suffix}");
        }
    }

    private static void Section(string title)
    {
        Write(string.Empty);
        Write($"=== {title} ===");
    }

    private static void Write(string line)
    {
        logWriter.WriteLine(line);
        Console.WriteLine(line);
    }
}

#else

namespace FileNamingManager.LiveSmoke;

/// <summary>
/// Compiled when Autodesk.Inventor.Interop is absent, which is every CI runner. The harness is a
/// developer tool that only ever runs on a workstation with Inventor 2027; on any other machine it
/// must still compile, and it must say clearly that live behaviour is unclaimed rather than pass.
/// </summary>
internal static class Program
{
    internal static int Main()
    {
        Console.WriteLine("UNCLAIMED: Autodesk.Inventor.Interop not installed");
        return 2;
    }
}

#endif
