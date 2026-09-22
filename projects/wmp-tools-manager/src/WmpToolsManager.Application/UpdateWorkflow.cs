// Purpose: Run the three steps ADR-0005 defines for an in-Inventor update - check, stage, apply - plus
//   rollback, keeping every side effect inside the state root and every failure inside a result record.
// Inputs: The four ports, the state root paths, and the Inventor per-user add-ins root.
// Outputs: UpdateCheck, StageResult, and LaunchResult. Nothing is copied into the add-ins root here:
//   Inventor locks its own add-in assemblies while it runs, so only the launched installer, which waits
//   for Inventor to exit, may write there.
// Dependencies: WmpToolsManager.Core and this project's ports. No IO, no HTTP, no process API.
// Assumptions: Updates are never silent (ADR-0005 item 3): every method here runs because the user
//   pressed something. Nothing is ever deleted - a download that fails verification is left where it
//   is and named in the error, so it can be inspected rather than silently replaced. The launched
//   installer waits for Inventor to exit, which can take hours, and the dialog can be reopened in the
//   meantime, so every launch records a pending-apply marker and every entry point refuses while that
//   marker's process is still alive. The marker is advisory, not a lock: a stale one - the installer
//   finished, or failed, or Windows was restarted - does not block anything and is simply overwritten
//   by the next launch.
// Validation source: docs/decisions/0005-release-distribution-and-updater.md;
//   scripts/release/Install-WmpInventorTools.ps1 (the script this workflow launches).

using WmpToolsManager.Core;

namespace WmpToolsManager.Application;

public sealed class UpdateWorkflow
{
    private readonly IReleaseSource releaseSource;
    private readonly IInstallState installState;
    private readonly IHashVerifier hashVerifier;
    private readonly IProcessLauncher processLauncher;
    private readonly StagingPaths paths;
    private readonly string addinsRoot;
    private readonly int notesLineCount;

    public UpdateWorkflow(
        IReleaseSource releaseSource,
        IInstallState installState,
        IHashVerifier hashVerifier,
        IProcessLauncher processLauncher,
        StagingPaths paths,
        string addinsRoot,
        int notesLineCount = ReleaseNotes.DefaultLineCount)
    {
        ArgumentNullException.ThrowIfNull(releaseSource);
        ArgumentNullException.ThrowIfNull(installState);
        ArgumentNullException.ThrowIfNull(hashVerifier);
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(addinsRoot);
        ArgumentOutOfRangeException.ThrowIfLessThan(notesLineCount, 1);

        this.releaseSource = releaseSource;
        this.installState = installState;
        this.hashVerifier = hashVerifier;
        this.processLauncher = processLauncher;
        this.paths = paths;
        this.addinsRoot = addinsRoot;
        this.notesLineCount = notesLineCount;
    }

    public StagingPaths Paths => paths;

    public string AddinsRoot => addinsRoot;

    /// <summary>
    /// Compares the installed version with the published one and collects everything the dialog shows.
    /// </summary>
    public async Task<UpdateCheck> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        List<string> errors = [];

        PendingApply? pending = ReadActivePendingApply(out string? pendingProblem);
        if (pendingProblem is not null)
        {
            errors.Add(pendingProblem);
        }

        ParseResult<InstalledState> installed = installState.ReadInstalled(paths.InstalledStateFile);
        SemanticVersion? installedVersion = null;
        IReadOnlyList<PluginSummary> plugins = [];
        if (installed.IsSuccess)
        {
            InstalledState state = installed.Value!;
            plugins = [.. state.Plugins.Select(PluginSummary.FromInstalled)];
            if (SemanticVersion.TryParse(state.Version, out SemanticVersion parsedInstalled))
            {
                installedVersion = parsedInstalled;
            }
            else
            {
                errors.Add($"installed.json records the version '{state.Version}', which is not MAJOR.MINOR.PATCH.");
            }
        }
        else
        {
            errors.Add(
                $"{installed.ErrorMessage} Expected it at '{paths.InstalledStateFile}'. " +
                "This add-in was probably installed from a working tree rather than a release.");
        }

        bool hasPrevious = installState.HasPreviousInstall(paths.PreviousRoot);

        SourceText sums = await releaseSource.GetLatestSumsAsync(cancellationToken).ConfigureAwait(false);
        if (!sums.IsSuccess)
        {
            errors.Add(sums.ErrorMessage!);
            return new UpdateCheck(
                UpdateDecision.Unknown, installedVersion, null, null, null, string.Empty, plugins, hasPrevious, errors, pending);
        }

        ReleaseManifestParse manifest = Sha256SumsFile.Parse(sums.Content);
        if (!manifest.IsSuccess)
        {
            errors.Add(manifest.ErrorMessage!);
            return new UpdateCheck(
                UpdateDecision.Unknown, installedVersion, null, null, null, string.Empty, plugins, hasPrevious, errors, pending);
        }

        ReleaseManifest release = manifest.Manifest!;
        UpdateDecision decision = UpdateDecider.Decide(installedVersion, release.Version);
        string notes = await ReadNotesAsync(release.Version, errors, cancellationToken).ConfigureAwait(false);

        return new UpdateCheck(
            decision,
            installedVersion,
            release.Version,
            release.PackageFileName,
            sums.Content,
            notes,
            plugins,
            hasPrevious,
            errors,
            pending);
    }

    /// <summary>
    /// The apply this add-in launched that is still waiting for Inventor to exit, or null. A marker
    /// whose process has gone - the installer finished, or failed, or Windows was restarted - is not
    /// pending, and the file is left on disk for the next launch to overwrite rather than deleted.
    /// </summary>
    public PendingApply? GetActivePendingApply() => ReadActivePendingApply(out _);

    /// <summary>
    /// Downloads the package and the installer into the state root and verifies both against the
    /// digest file the check already read. Nothing outside the state root is written.
    /// </summary>
    public async Task<StageResult> StageUpdateAsync(UpdateCheck check, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(check);

        PendingApply? pending = GetActivePendingApply();
        if (pending is not null)
        {
            // The sentence is the whole answer, so it is not repeated under Problems: a refusal the
            // user can resolve by closing Inventor is guidance, not a fault to report.
            return StageResult.Failed(pending.WaitingMessage, []);
        }

        if (!check.CanStage)
        {
            return StageResult.Failed(
                "There is no update to download.",
                [check.Summary]);
        }

        SemanticVersion version = check.LatestVersion!.Value;
        ReleaseManifestParse manifest = Sha256SumsFile.Parse(check.Sha256SumsContent);
        if (!manifest.IsSuccess)
        {
            return StageResult.Failed("The release digest file could not be read.", [manifest.ErrorMessage!]);
        }

        ReleaseManifest release = manifest.Manifest!;
        string stagingDirectory = paths.StagingDirectoryFor(version);
        string sumsPath = paths.StagedFile(version, Sha256SumsFile.FileName);
        string zipPath = paths.StagedFile(version, release.PackageFileName);
        string installerPath = paths.StagedFile(version, Sha256SumsFile.InstallerFileName);

        List<string> errors = [];
        try
        {
            installState.CreateDirectory(stagingDirectory);
            installState.WriteText(sumsPath, check.Sha256SumsContent!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return StageResult.Failed(
                $"The staging folder '{stagingDirectory}' could not be prepared.",
                [exception.Message]);
        }

        SourceFile package = await releaseSource
            .DownloadLatestAssetAsync(release.PackageFileName, zipPath, cancellationToken)
            .ConfigureAwait(false);
        if (!package.IsSuccess)
        {
            errors.Add(package.ErrorMessage!);
            return StageResult.Failed($"Downloading {release.PackageFileName} failed.", errors);
        }

        string? packageError = Verify(zipPath, release.PackageSha256, release.PackageFileName);
        if (packageError is not null)
        {
            errors.Add(packageError);
            return StageResult.Failed("The downloaded package failed verification and was not applied.", errors);
        }

        SourceFile installer = await releaseSource
            .DownloadLatestAssetAsync(Sha256SumsFile.InstallerFileName, installerPath, cancellationToken)
            .ConfigureAwait(false);
        if (!installer.IsSuccess)
        {
            errors.Add(installer.ErrorMessage!);
            return StageResult.Failed($"Downloading {Sha256SumsFile.InstallerFileName} failed.", errors);
        }

        string? installerError = Verify(installerPath, release.InstallerSha256, Sha256SumsFile.InstallerFileName);
        if (installerError is not null)
        {
            errors.Add(installerError);
            return StageResult.Failed("The downloaded installer failed verification and was not run.", errors);
        }

        ParseResult<ReleaseCatalog> catalog = installState.ReadCatalog(zipPath);
        IReadOnlyList<PluginSummary> plugins = [];
        if (catalog.IsSuccess)
        {
            plugins = [.. catalog.Value!.Plugins.Select(PluginSummary.FromCatalog)];
        }
        else
        {
            // The package is verified, so it is safe to apply even though its plugin table cannot be
            // listed. The user is told what they will not see rather than shown an empty table.
            errors.Add(catalog.ErrorMessage!);
        }

        return new StageResult(
            true,
            version,
            zipPath,
            sumsPath,
            installerPath,
            plugins,
            $"WMP Inventor Tools {version} is downloaded and verified.",
            errors);
    }

    /// <summary>
    /// Hands the verified package to the installer, which waits for every Inventor process to exit and
    /// then applies it.
    /// </summary>
    public LaunchResult LaunchApply(StageResult stage)
    {
        ArgumentNullException.ThrowIfNull(stage);

        PendingApply? pending = GetActivePendingApply();
        if (pending is not null)
        {
            return new LaunchResult(false, pending.WaitingMessage);
        }

        if (!stage.Verified
            || stage.PackageZipPath is null
            || stage.Sha256SumsPath is null
            || stage.InstallerScriptPath is null)
        {
            return new LaunchResult(false, "Nothing verified is staged, so no update was started.");
        }

        string arguments = ApplyCommandLine.BuildApplyArguments(
            stage.InstallerScriptPath,
            stage.PackageZipPath,
            stage.Sha256SumsPath,
            addinsRoot,
            paths.StateRoot);

        LaunchResult launch = processLauncher.Launch(ApplyCommandLine.ExecutableFileName, arguments);
        if (!launch.Launched)
        {
            return launch;
        }

        string recorded = RecordPendingApply(
            launch.ProcessId,
            stage.Version?.ToString() ?? PendingApply.UnknownVersion,
            PendingApply.UpdateKind);

        return new LaunchResult(
            true,
            "Close Inventor to finish; the update applies automatically. "
            + "Leave the PowerShell window open until it reports the installed version."
            + recorded,
            launch.ProcessId);
    }

    /// <summary>
    /// Restores the archived install. The installer keeps, never deletes, what it replaces, so this
    /// only swaps folders back.
    /// </summary>
    public LaunchResult RollbackToPrevious()
    {
        PendingApply? pending = GetActivePendingApply();
        if (pending is not null)
        {
            return new LaunchResult(false, pending.WaitingMessage);
        }

        if (!installState.HasPreviousInstall(paths.PreviousRoot))
        {
            return new LaunchResult(false, $"There is no archived install under '{paths.PreviousRoot}'.");
        }

        string? installerPath = installState.FindInstaller(paths.StateRoot);
        if (installerPath is null)
        {
            return new LaunchResult(
                false,
                $"No copy of {Sha256SumsFile.InstallerFileName} was found under '{paths.StateRoot}'. "
                + "Roll back from PowerShell with the command in this project's README instead.");
        }

        string arguments = ApplyCommandLine.BuildRollbackArguments(installerPath, addinsRoot, paths.StateRoot);
        LaunchResult launch = processLauncher.Launch(ApplyCommandLine.ExecutableFileName, arguments);
        if (!launch.Launched)
        {
            return launch;
        }

        string recorded = RecordPendingApply(launch.ProcessId, ReadInstalledVersionText(), PendingApply.RollbackKind);

        return new LaunchResult(
            true,
            "Close Inventor to finish; the previous version is restored automatically. "
            + "Leave the PowerShell window open until it reports the restored version."
            + recorded,
            launch.ProcessId);
    }

    /// <summary>
    /// Reads the marker and reports both what is pending and anything wrong with the file. A marker
    /// that cannot be parsed never blocks: refusing every action because of a file the user cannot be
    /// expected to find would be worse than the concurrent apply this guard exists to prevent.
    /// </summary>
    private PendingApply? ReadActivePendingApply(out string? problem)
    {
        problem = null;

        ParseResult<PendingApply> parse = installState.ReadPendingApply(paths.PendingApplyFile);
        if (!parse.IsSuccess)
        {
            if (!string.Equals(parse.ErrorMessage, PendingApply.NoMarkerMessage, StringComparison.Ordinal))
            {
                problem =
                    $"{parse.ErrorMessage} It is at '{paths.PendingApplyFile}' and is ignored, so an "
                    + "update can still be started; the next launch replaces it.";
            }

            return null;
        }

        PendingApply pending = parse.Value!;
        return processLauncher.IsProcessRunning(pending.Pid) ? pending : null;
    }

    /// <summary>
    /// Records the launched apply. The process is already running by this point, so a marker that
    /// cannot be written is reported rather than treated as a failed launch - and the sentence says
    /// what the user loses, which is the guard against a second launch.
    /// </summary>
    private string RecordPendingApply(int? processId, string version, string kind)
    {
        if (processId is not int pid)
        {
            return " The apply was started but its process id is unknown, so a second attempt "
                + "cannot be refused; do not start another until this one finishes.";
        }

        try
        {
            installState.WritePendingApply(
                paths.PendingApplyFile,
                new PendingApply(pid, version, kind, DateTimeOffset.UtcNow));
            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $" '{paths.PendingApplyFile}' could not be written ({exception.Message}), so a "
                + "second attempt cannot be refused; do not start another until this one finishes.";
        }
    }

    /// <summary>The installed version as the marker records it, for a rollback's own wording.</summary>
    private string ReadInstalledVersionText()
    {
        ParseResult<InstalledState> installed = installState.ReadInstalled(paths.InstalledStateFile);
        return installed.IsSuccess && installed.Value!.Version.Length > 0
            ? installed.Value.Version
            : PendingApply.UnknownVersion;
    }

    private async Task<string> ReadNotesAsync(
        SemanticVersion version,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        SourceText payload = await releaseSource
            .GetReleasePayloadAsync(version.ToTag(), cancellationToken)
            .ConfigureAwait(false);
        if (!payload.IsSuccess)
        {
            errors.Add($"Release notes are unavailable: {payload.ErrorMessage}");
            return string.Empty;
        }

        ParseResult<ReleaseInfo> release = ReleaseJson.ParseReleasePayload(payload.Content);
        if (!release.IsSuccess)
        {
            errors.Add($"Release notes are unavailable: {release.ErrorMessage}");
            return string.Empty;
        }

        return ReleaseNotes.Excerpt(release.Value!.Body, notesLineCount);
    }

    private string? Verify(string filePath, string? expectedSha256, string fileName)
    {
        if (string.IsNullOrEmpty(expectedSha256))
        {
            return $"{Sha256SumsFile.FileName} carries no digest for '{fileName}', so it cannot be verified.";
        }

        HashResult actual = hashVerifier.ComputeSha256(filePath);
        if (!actual.IsSuccess)
        {
            return actual.ErrorMessage;
        }

        return Sha256SumsFile.DigestsMatch(expectedSha256, actual.Sha256)
            ? null
            : $"SHA-256 mismatch for '{fileName}': expected {expectedSha256}, got {actual.Sha256}. "
              + $"The file is left at '{filePath}'; do not use it.";
    }
}
