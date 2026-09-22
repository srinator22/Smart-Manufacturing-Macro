// Purpose: Verify PhysicalNamingFileSystem's real file-system behavior in temp directories.
// Inputs: Real files and folders created under a unique per-test temp root.
// Outputs: Assertions on scope enumeration, exclusions, Vault detection, and move-without-overwrite.
// Dependencies: FileNamingManager.Infrastructure, System.IO, System.Text.Json.
// Assumptions: Tests run on Windows with a writable temp directory; each test cleans up after itself.
// Validation source: .work/TASK.md "Vault safety model" section; PhysicalNamingFileSystem test list.

using System.Text.Json;
using FileNamingManager.Application;
using FileNamingManager.Infrastructure;

namespace FileNamingManager.UnitTests;

public sealed class PhysicalNamingFileSystemTests : IDisposable
{
    private readonly string root;
    private readonly PhysicalNamingFileSystem fileSystem = new();

    public PhysicalNamingFileSystemTests()
    {
        root = Path.Combine(Path.GetTempPath(), "FileNamingManagerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateScopeFindsRecognizedExtensionsRecursively()
    {
        WriteFile("Part.ipt");
        WriteFile(@"Sub\Assembly.iam");
        WriteFile(@"Sub\Drawing.idw");
        WriteFile(@"Sub\Legacy.dwg");
        WriteFile(@"Sub\Presentation.ipn");
        WriteFile("Ignore.txt");

        IReadOnlyList<string> scope = fileSystem.EnumerateScope(root);

        Assert.Equal(5, scope.Count);
        Assert.Contains(scope, p => p.EndsWith("Part.ipt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(scope, p => p.EndsWith("Ignore.txt", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("OldVersions")]
    [InlineData("_V")]
    [InlineData("3rd Party Hardware")]
    [InlineData("Content Center Files")]
    [InlineData("oldversions")]
    [InlineData("_renamed-originals")]
    public void EnumerateScopeExcludesReservedFoldersAtAnyDepth(string excludedFolderName)
    {
        WriteFile("Keep.ipt");
        WriteFile(Path.Combine("Nested", excludedFolderName, "Excluded.ipt"));

        IReadOnlyList<string> scope = fileSystem.EnumerateScope(root);

        Assert.Single(scope);
        Assert.Contains(scope, p => p.EndsWith("Keep.ipt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateScopeOnMissingProjectRootReturnsEmpty()
    {
        IReadOnlyList<string> scope = fileSystem.EnumerateScope(Path.Combine(root, "DoesNotExist"));

        Assert.Empty(scope);
    }

    [Fact]
    public void IsVaultManagedTrueWhenTrackerFileExists()
    {
        string filePath = WriteFile("124-0001 Widget.ipt");
        WriteFile(@"_V\124-0001 Widget.ipt.v");

        Assert.True(fileSystem.IsVaultManaged(filePath));
    }

    [Fact]
    public void IsVaultManagedFalseWhenNoTrackerFileExists()
    {
        string filePath = WriteFile("124-0002 Gadget.ipt");

        Assert.False(fileSystem.IsVaultManaged(filePath));
    }

    [Fact]
    public void IsReadOnlyTrueForAFileCarryingTheReadOnlyAttribute()
    {
        string filePath = WriteFile("124-0003 Locked.ipt");
        File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.ReadOnly);

        try
        {
            Assert.True(fileSystem.IsReadOnly(filePath));
        }
        finally
        {
            File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public void IsReadOnlyFalseForAWritableFile()
    {
        string filePath = WriteFile("124-0004 Writable.ipt");

        Assert.False(fileSystem.IsReadOnly(filePath));
    }

    /// <summary>
    /// A path that is not on disk is nothing to protect, and File.GetAttributes would throw for it. The
    /// caller asks this question about files it found by enumerating the scope, so a missing file is a
    /// race, not malformed input: reporting "not read-only" leaves the later rename to fail loudly.
    /// </summary>
    [Fact]
    public void IsReadOnlyFalseForAFileThatDoesNotExist()
    {
        Assert.False(fileSystem.IsReadOnly(Path.Combine(root, "Gone.ipt")));
    }

    [Fact]
    public void MoveToOriginalsPreservesRelativePathUnderOriginalsRoot()
    {
        string filePath = WriteFile(@"Sub\Part.ipt");
        string originalsRoot = Path.Combine(root, "_renamed-originals", "20260923T000000Z");

        fileSystem.MoveToOriginals(filePath, originalsRoot, root);

        string expectedDestination = Path.Combine(originalsRoot, "Sub", "Part.ipt");
        Assert.True(File.Exists(expectedDestination));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void MoveToOriginalsRefusesToOverwriteAnExistingDestination()
    {
        string filePath = WriteFile("Part.ipt");
        string originalsRoot = Path.Combine(root, "_renamed-originals", "20260923T000000Z");
        Directory.CreateDirectory(originalsRoot);
        File.WriteAllText(Path.Combine(originalsRoot, "Part.ipt"), "already archived");

        IOException thrown = Assert.Throws<IOException>(() => fileSystem.MoveToOriginals(filePath, originalsRoot, root));
        Assert.Equal(
            $"Refusing to overwrite an existing archived original: '{Path.Combine(originalsRoot, "Part.ipt")}'.",
            thrown.Message);
        Assert.True(File.Exists(filePath));
    }

    /// <summary>
    /// Every entry point validates its own arguments and names the offending one. Without these guards
    /// a null path silently becomes "nothing found" or "not managed" - a quiet wrong answer about
    /// whether a file is safe to rename, which is exactly the class of failure this tool must not have.
    /// The parameter name is asserted because several downstream .NET calls throw the same exception
    /// type further in; only the parameter name proves the guard itself fired.
    /// </summary>
    [Fact]
    public void EveryEntryPointRejectsNullPathArgumentsNamingTheOffendingParameter()
    {
        RenameManifest manifest = new(DateTimeOffset.UnixEpoch, root, []);

        Assert.Throws<ArgumentNullException>("projectRoot", () => fileSystem.EnumerateScope(null!));
        Assert.Throws<ArgumentNullException>("fullPath", () => fileSystem.IsVaultManaged(null!));
        Assert.Throws<ArgumentNullException>("fullPath", () => fileSystem.IsReadOnly(null!));
        Assert.Throws<ArgumentNullException>("fullPath", () => fileSystem.MoveToOriginals(null!, root, root));
        Assert.Throws<ArgumentNullException>("originalsRoot", () => fileSystem.MoveToOriginals(root, null!, root));
        Assert.Throws<ArgumentNullException>("projectRoot", () => fileSystem.MoveToOriginals(root, root, null!));
        Assert.Throws<ArgumentNullException>("originalsRoot", () => fileSystem.WriteManifest(null!, manifest));
        Assert.Throws<ArgumentNullException>("manifest", () => fileSystem.WriteManifest(root, null!));
    }

    [Fact]
    public void WriteManifestWritesIndentedJsonWithTimestampProjectRootAndEntries()
    {
        string originalsRoot = Path.Combine(root, "_renamed-originals", "20260923T000000Z");
        RenameManifest manifest = new(
            new DateTimeOffset(2026, 9, 23, 0, 0, 0, TimeSpan.Zero),
            root,
            [
                new RenameManifestEntry(@"C:\P\Part.ipt", @"C:\P\_renamed-originals\Part.ipt", @"C:\P\124-0001 Part.ipt", true, null),
                new RenameManifestEntry(@"C:\P\Gear.ipt", @"C:\P\_renamed-originals\Gear.ipt", @"C:\P\124-0002 Gear.ipt", false, "locked"),
            ]);

        fileSystem.WriteManifest(originalsRoot, manifest);

        string manifestPath = Path.Combine(originalsRoot, "manifest.json");
        Assert.True(File.Exists(manifestPath));

        string json = File.ReadAllText(manifestPath);
        Assert.Contains("\n", json, StringComparison.Ordinal);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root2 = document.RootElement;
        Assert.Equal(root, root2.GetProperty("projectRoot").GetString());
        Assert.Equal(2, root2.GetProperty("entries").GetArrayLength());
        Assert.Equal(@"C:\P\Part.ipt", root2.GetProperty("entries")[0].GetProperty("from").GetString());
        Assert.Equal(@"C:\P\124-0001 Part.ipt", root2.GetProperty("entries")[0].GetProperty("renamedTo").GetString());

        // The manifest is the record of what actually happened to each original, so the per-entry
        // outcome has to survive serialization; a manifest that only lists intentions is not a record.
        Assert.True(root2.GetProperty("entries")[0].GetProperty("archived").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root2.GetProperty("entries")[0].GetProperty("error").ValueKind);
        Assert.False(root2.GetProperty("entries")[1].GetProperty("archived").GetBoolean());
        Assert.Equal("locked", root2.GetProperty("entries")[1].GetProperty("error").GetString());
    }

    private string WriteFile(string relativePath)
    {
        string fullPath = Path.Combine(root, relativePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, "test");
        return fullPath;
    }
}
