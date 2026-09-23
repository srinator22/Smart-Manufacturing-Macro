// Purpose: Pin filesystem enumeration and the per-row scope rule to the one excluded-folder list.
// Inputs: A real fixture tree under a unique per-test temp root: one in-scope model at the root, one under
//   each reserved folder name, one under a nested reserved folder, and one under an ordinary subfolder.
// Outputs: Assertions that EnumerateScope returns exactly the in-scope files, and that membership in the
//   returned set agrees segment by segment with NamingScopeRules.IsExcludedFolderName.
// Dependencies: FileNamingManager.Core, FileNamingManager.Infrastructure, System.IO.
// Assumptions: Tests run on Windows with a writable temp directory; each test cleans up after itself.
// Validation source: NamingScopeRules header ("neither may keep a copy of it"); docs/TEST_PLAN.md section 1.

using FileNamingManager.Core;
using FileNamingManager.Infrastructure;

namespace FileNamingManager.UnitTests;

/// <summary>
/// The enumeration that builds the scope and the rule that marks a row out of scope used to read separate
/// copies of the same folder list. Two copies drift, and a drifted pair either renames files inside
/// OldVersions or reports an in-scope file as out of scope. These tests exercise both through one fixture
/// tree so a name added to only one of them fails here.
/// </summary>
public sealed class ScopeAgreementTests : IDisposable
{
    private readonly string root;
    private readonly PhysicalNamingFileSystem fileSystem = new();
    private readonly List<string> inScopeFiles = [];
    private readonly List<string> excludedFiles = [];

    public ScopeAgreementTests()
    {
        root = Path.Combine(Path.GetTempPath(), "FileNamingManagerScope_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        inScopeFiles.Add(WriteFile("RootModel.ipt"));
        inScopeFiles.Add(WriteFile(Path.Combine("Normal", "SubModel.ipt")));

        foreach (string excluded in NamingScopeRules.ExcludedFolderNames)
        {
            excludedFiles.Add(WriteFile(Path.Combine(excluded, "Superseded.ipt")));
        }

        excludedFiles.Add(WriteFile(Path.Combine("Normal", "Deep", "OldVersions", "Superseded.ipt")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateScopeReturnsExactlyTheInScopeFiles()
    {
        IReadOnlyList<string> scope = fileSystem.EnumerateScope(root);

        Assert.Equal(
            inScopeFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            scope.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryExcludedFileSitsUnderASegmentTheScopeRuleCallsExcluded()
    {
        foreach (string excludedFile in excludedFiles)
        {
            Assert.True(
                HasExcludedSegment(excludedFile),
                $"'{excludedFile}' was kept out of the scope but no path segment is excluded by NamingScopeRules.");
        }
    }

    [Fact]
    public void NoReturnedFileSitsUnderASegmentTheScopeRuleCallsExcluded()
    {
        IReadOnlyList<string> scope = fileSystem.EnumerateScope(root);

        foreach (string returnedFile in scope)
        {
            Assert.False(
                HasExcludedSegment(returnedFile),
                $"'{returnedFile}' is in the scope but a path segment is excluded by NamingScopeRules.");
        }
    }

    /// <summary>
    /// Only the directory segments below the project root are judged. The temp root itself is not part of
    /// the project, and the file's own name is not a folder.
    /// </summary>
    private bool HasExcludedSegment(string fullPath)
    {
        string relative = Path.GetRelativePath(root, fullPath);
        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        return segments
            .Take(segments.Length - 1)
            .Any(NamingScopeRules.IsExcludedFolderName);
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
