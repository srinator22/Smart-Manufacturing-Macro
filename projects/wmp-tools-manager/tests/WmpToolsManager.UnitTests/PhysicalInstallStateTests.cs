// These run against a real temporary folder under %TEMP%, created and removed per test class. They
// never touch the real state root or the real Inventor add-ins folder.

using System.IO.Compression;
using WmpToolsManager.Application;
using WmpToolsManager.Core;
using WmpToolsManager.Infrastructure;

namespace WmpToolsManager.UnitTests;

public sealed class PhysicalInstallStateTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "wmp-tools-manager-state-" + Guid.NewGuid().ToString("N"));

    private readonly PhysicalInstallState state = new();

    public PhysicalInstallStateTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReportsAMissingInstalledStateRatherThanThrowing()
    {
        ParseResult<InstalledState> parse = state.ReadInstalled(Path.Combine(root, "installed.json"));

        Assert.False(parse.IsSuccess);
        Assert.Equal("No installed.json was found.", parse.ErrorMessage);
    }

    [Fact]
    public void ReadsAnInstalledStateWrittenAsTheInstallerWritesIt()
    {
        string path = Path.Combine(root, "installed.json");
        File.WriteAllText(path, """
            {
              "version": "0.6.0",
              "installedUtc": "2026-09-23T10:00:00Z",
              "plugins": [ { "id": "wmp-tools-manager", "maturity": "beta", "installDirectory": "WmpToolsManager", "manifestName": "x.addin" } ]
            }
            """);

        ParseResult<InstalledState> parse = state.ReadInstalled(path);

        Assert.True(parse.IsSuccess);
        Assert.Equal("0.6.0", parse.Value!.Version);
        Assert.Equal("wmp-tools-manager", Assert.Single(parse.Value.Plugins).Id);
    }

    [Fact]
    public void ReportsAMalformedInstalledState()
    {
        string path = Path.Combine(root, "installed.json");
        File.WriteAllText(path, "{ not json");

        Assert.False(state.ReadInstalled(path).IsSuccess);
    }

    [Fact]
    public void ReadsTheCatalogOutOfAPackageWithoutExtractingIt()
    {
        string zip = CreatePackage("""{ "version": "0.6.0", "plugins": [ { "id": "a", "displayName": "A", "maturity": "beta" } ] }""");

        ParseResult<ReleaseCatalog> parse = state.ReadCatalog(zip);

        Assert.True(parse.IsSuccess);
        Assert.Equal("0.6.0", parse.Value!.Version);
        Assert.Equal("A", Assert.Single(parse.Value.Plugins).DisplayName);
    }

    [Fact]
    public void ReportsAPackageWithNoCatalog()
    {
        string zip = Path.Combine(root, "empty.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("WmpToolsManager/readme.txt");
        }

        ParseResult<ReleaseCatalog> parse = state.ReadCatalog(zip);

        Assert.False(parse.IsSuccess);
        Assert.Contains("contains no catalog.json", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAPackageThatIsNotAZip()
    {
        string zip = Path.Combine(root, "broken.zip");
        File.WriteAllText(zip, "this is not a zip");

        ParseResult<ReleaseCatalog> parse = state.ReadCatalog(zip);

        Assert.False(parse.IsSuccess);
        Assert.Contains("could not be read", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAPackageThatDoesNotExist() =>
        Assert.False(state.ReadCatalog(Path.Combine(root, "absent.zip")).IsSuccess);

    [Fact]
    public void ReportsNoPreviousInstallWhenTheFolderIsAbsentOrEmpty()
    {
        string previous = Path.Combine(root, "previous");

        Assert.False(state.HasPreviousInstall(previous));

        Directory.CreateDirectory(previous);
        Assert.False(state.HasPreviousInstall(previous));

        Directory.CreateDirectory(Path.Combine(previous, "0.5.0"));
        Assert.False(state.HasPreviousInstall(previous));
    }

    [Fact]
    public void ReportsAPreviousInstallOnlyWhenAnArchivedFolderHasContent()
    {
        string archived = Path.Combine(root, "previous", "0.5.0");
        Directory.CreateDirectory(archived);
        File.WriteAllText(Path.Combine(archived, "installed.json"), "{}");

        Assert.True(state.HasPreviousInstall(Path.Combine(root, "previous")));
    }

    [Fact]
    public void FindsNoInstallerWhenNeitherPersistedNorStagedExists()
    {
        Assert.Null(state.FindInstaller(root));

        Directory.CreateDirectory(Path.Combine(root, "staging", "0.6.0"));
        Assert.Null(state.FindInstaller(root));
    }

    [Fact]
    public void FallsBackToTheMostRecentlyStagedInstallerWhenNoneIsPersisted()
    {
        string older = Path.Combine(root, "staging", "0.5.0", "Install-WmpInventorTools.ps1");
        string newer = Path.Combine(root, "staging", "0.6.0", "Install-WmpInventorTools.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(older)!);
        Directory.CreateDirectory(Path.GetDirectoryName(newer)!);
        File.WriteAllText(older, "# old");
        File.WriteAllText(newer, "# new");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(newer, state.FindInstaller(root));
    }

    [Fact]
    public void PrefersThePersistedInstallerOverANewerStagedCopy()
    {
        string persisted = Path.Combine(root, "Install-WmpInventorTools.ps1");
        string staged = Path.Combine(root, "staging", "0.6.0", "Install-WmpInventorTools.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllText(persisted, "# persisted");
        File.WriteAllText(staged, "# staged, newer on disk");
        File.SetLastWriteTimeUtc(persisted, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(staged, new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(persisted, state.FindInstaller(root));
    }

    [Fact]
    public void CreatesNestedDirectoriesAndLeavesExistingContentAlone()
    {
        string nested = Path.Combine(root, "staging", "0.6.0");
        state.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "keep.txt"), "keep");

        state.CreateDirectory(nested);

        Assert.True(File.Exists(Path.Combine(nested, "keep.txt")));
    }

    [Fact]
    public void WritesUtf8TextWithoutAByteOrderMark()
    {
        string path = Path.Combine(root, "SHA256SUMS.txt");

        state.WriteText(path, "abc  file.zip\n");

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal("abc  file.zip\n", File.ReadAllText(path));
        Assert.NotEqual(0xEF, bytes[0]);
    }

    [Fact]
    public void ReplacesAFileItWritesTwice()
    {
        string path = Path.Combine(root, "SHA256SUMS.txt");

        state.WriteText(path, "first");
        state.WriteText(path, "second");

        Assert.Equal("second", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RefusesEmptyPaths(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => state.ReadInstalled(path!));
        Assert.ThrowsAny<ArgumentException>(() => state.ReadCatalog(path!));
        Assert.ThrowsAny<ArgumentException>(() => state.HasPreviousInstall(path!));
        Assert.ThrowsAny<ArgumentException>(() => state.FindInstaller(path!));
        Assert.ThrowsAny<ArgumentException>(() => state.CreateDirectory(path!));
        Assert.ThrowsAny<ArgumentException>(() => state.WriteText(path!, "x"));
        Assert.ThrowsAny<ArgumentException>(() => state.ReadPendingApply(path!));
        Assert.ThrowsAny<ArgumentException>(() => state.WritePendingApply(
            path!,
            new PendingApply(1, "0.6.0", PendingApply.UpdateKind, DateTimeOffset.UnixEpoch)));
    }

    [Fact]
    public void RefusesNullContent()
    {
        Assert.Throws<ArgumentNullException>(() => state.WriteText(Path.Combine(root, "a.txt"), null!));
        Assert.Throws<ArgumentNullException>(
            () => state.WritePendingApply(Path.Combine(root, PendingApply.FileName), null!));
    }

    [Fact]
    public void ReportsAStateRootWithNoPendingApplyMarkerAsTheNormalCase()
    {
        ParseResult<PendingApply> parse = state.ReadPendingApply(Path.Combine(root, PendingApply.FileName));

        Assert.False(parse.IsSuccess);
        Assert.Equal(PendingApply.NoMarkerMessage, parse.ErrorMessage);
    }

    [Fact]
    public void WritesAndReadsBackTheMarkerItJustWrote()
    {
        string path = Path.Combine(root, PendingApply.FileName);
        PendingApply marker = new(4242, "0.6.0", PendingApply.UpdateKind, DateTimeOffset.UnixEpoch);

        state.WritePendingApply(path, marker);

        Assert.True(File.Exists(path));
        Assert.Equal(marker, state.ReadPendingApply(path).Value);
    }

    [Fact]
    public void OverwritesAnEarlierMarkerAndNeverDeletesOne()
    {
        string path = Path.Combine(root, PendingApply.FileName);
        state.WritePendingApply(path, new(1, "0.6.0", PendingApply.UpdateKind, DateTimeOffset.UnixEpoch));

        state.WritePendingApply(path, new(2, "0.7.0", PendingApply.RollbackKind, DateTimeOffset.UnixEpoch));

        PendingApply read = state.ReadPendingApply(path).Value!;
        Assert.Equal(2, read.Pid);
        Assert.Equal("0.7.0", read.Version);
        Assert.Equal(PendingApply.RollbackKind, read.Kind);
    }

    [Fact]
    public void CreatesTheStateRootWhenTheMarkerIsTheFirstThingWrittenThere()
    {
        string stateRoot = Path.Combine(root, "not-created-yet");
        string path = Path.Combine(stateRoot, PendingApply.FileName);

        state.WritePendingApply(path, new(7, "0.6.0", PendingApply.UpdateKind, DateTimeOffset.UnixEpoch));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void ReportsAMarkerItCannotParseRatherThanThrowing()
    {
        string path = Path.Combine(root, PendingApply.FileName);
        File.WriteAllText(path, "{ not json");

        ParseResult<PendingApply> parse = state.ReadPendingApply(path);

        Assert.False(parse.IsSuccess);
        Assert.StartsWith("pending-apply.json is not valid JSON:", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesTheMarkerWithoutAByteOrderMark()
    {
        string path = Path.Combine(root, PendingApply.FileName);

        state.WritePendingApply(path, new(1, "0.6.0", PendingApply.UpdateKind, DateTimeOffset.UnixEpoch));

        byte[] bytes = File.ReadAllBytes(path);
        Assert.NotEqual<byte[]>([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    private string CreatePackage(string catalogJson)
    {
        string zip = Path.Combine(root, "WmpInventorTools-0.6.0.zip");
        using ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create);
        using (StreamWriter writer = new(archive.CreateEntry("catalog.json").Open()))
        {
            writer.Write(catalogJson);
        }

        archive.CreateEntry("WmpToolsManager/WmpToolsManager.AddIn.dll");
        return zip;
    }
}
