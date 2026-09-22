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

    public bool PreviousInstallExists { get; set; }

    public string? StagedInstaller { get; set; }

    public Exception? ThrowOnWrite { get; set; }

    public List<string> CreatedDirectories { get; } = [];

    public Dictionary<string, string> WrittenFiles { get; } = [];

    public List<string> ReadCatalogPaths { get; } = [];

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
    public LaunchResult Result { get; set; } = new(true, "started");

    public List<(string FileName, string Arguments)> Launches { get; } = [];

    public LaunchResult Launch(string fileName, string arguments)
    {
        Launches.Add((fileName, arguments));
        return Result;
    }
}
