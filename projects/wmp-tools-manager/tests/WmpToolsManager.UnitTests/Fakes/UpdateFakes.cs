// In-memory stand-ins for the four update ports. They complete synchronously so a test never races a
// wall clock, and they record what the workflow asked for so the assertions can check the contract
// rather than the implementation.

using WmpToolsManager.Application;
using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests.Fakes;

public sealed class FakeReleaseSource : IReleaseSource
{
    public SourceText Sums { get; set; } = new(null, "not configured");

    public SourceText Payload { get; set; } = new("{}", null);

    public Dictionary<string, SourceFile> Downloads { get; } = [];

    public List<string> RequestedAssets { get; } = [];

    public List<string> RequestedTags { get; } = [];

    public Task<SourceText> GetLatestSumsAsync(CancellationToken cancellationToken) => Task.FromResult(Sums);

    public Task<SourceText> GetReleasePayloadAsync(string tag, CancellationToken cancellationToken)
    {
        RequestedTags.Add(tag);
        return Task.FromResult(Payload);
    }

    public Task<SourceFile> DownloadLatestAssetAsync(
        string assetFileName,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        RequestedAssets.Add(assetFileName);
        return Task.FromResult(Downloads.TryGetValue(assetFileName, out SourceFile? file)
            ? file
            : new SourceFile(destinationPath, 1, null));
    }
}

public sealed class FakeInstallState : IInstallState
{
    public ParseResult<InstalledState> Installed { get; set; } =
        new(null, "No installed.json was found.");

    public ParseResult<ReleaseCatalog> Catalog { get; set; } =
        new(null, "catalog.json was empty.");

    /// <summary>Nothing is pending by default, which is the state a fresh state root is in.</summary>
    public ParseResult<PendingApply> Pending { get; set; } =
        new(null, PendingApply.NoMarkerMessage);

    public bool PreviousInstallExists { get; set; }

    public string? StagedInstaller { get; set; }

    public Exception? ThrowOnWrite { get; set; }

    /// <summary>Separate from <see cref="ThrowOnWrite"/> so a staging failure and a marker failure can
    /// be provoked independently.</summary>
    public Exception? ThrowOnWritePendingApply { get; set; }

    public List<string> CreatedDirectories { get; } = [];

    public Dictionary<string, string> WrittenFiles { get; } = [];

    public List<string> ReadCatalogPaths { get; } = [];

    public List<string> ReadPendingApplyPaths { get; } = [];

    public List<(string Path, PendingApply Value)> WrittenPendingApplies { get; } = [];

    public ParseResult<InstalledState> ReadInstalled(string installedStatePath) => Installed;

    public ParseResult<ReleaseCatalog> ReadCatalog(string packageZipPath)
    {
        ReadCatalogPaths.Add(packageZipPath);
        return Catalog;
    }

    public bool HasPreviousInstall(string previousRoot) => PreviousInstallExists;

    public string? FindInstaller(string stateRoot) => StagedInstaller;

    public void CreateDirectory(string path)
    {
        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        CreatedDirectories.Add(path);
    }

    public void WriteText(string path, string content)
    {
        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        WrittenFiles[path] = content;
    }

    public ParseResult<PendingApply> ReadPendingApply(string path)
    {
        ReadPendingApplyPaths.Add(path);
        return Pending;
    }

    public void WritePendingApply(string path, PendingApply value)
    {
        if (ThrowOnWritePendingApply is not null)
        {
            throw ThrowOnWritePendingApply;
        }

        WrittenPendingApplies.Add((path, value));

        // The real adapter reads back what it wrote, so this one does too: a workflow that launched an
        // apply must find its own marker on the next read, which is the whole guard under test.
        Pending = new(value, null);
    }
}

public sealed class FakeHashVerifier : IHashVerifier
{
    public Dictionary<string, HashResult> Results { get; } = [];

    public HashResult Fallback { get; set; } = new("0", null);

    public HashResult ComputeSha256(string filePath) =>
        Results.TryGetValue(Path.GetFileName(filePath), out HashResult? result) ? result : Fallback;
}

public sealed class RecordingProcessLauncher : IProcessLauncher
{
    public const int LaunchedPid = 4242;

    /// <summary>The default carries a process id, because the real launcher always reports one for a
    /// process Windows actually started.</summary>
    public LaunchResult Result { get; set; } = new(true, "started", LaunchedPid);

    public List<(string FileName, string Arguments)> Launches { get; } = [];

    /// <summary>The pids this fake calls alive. Empty means every marker on disk is stale.</summary>
    public HashSet<int> RunningPids { get; } = [];

    public List<int> ProbedPids { get; } = [];

    public LaunchResult Launch(string fileName, string arguments)
    {
        Launches.Add((fileName, arguments));
        return Result;
    }

    public bool IsProcessRunning(int pid)
    {
        ProbedPids.Add(pid);
        return RunningPids.Contains(pid);
    }
}
