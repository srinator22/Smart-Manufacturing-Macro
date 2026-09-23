// Purpose: Pin the excluded-folder list and its case-insensitive membership test.
// Inputs: Single path segments, never full paths.
// Outputs: Assertions on ExcludedFolderNames and IsExcludedFolderName.
// Dependencies: FileNamingManager.Core.
// Assumptions: None.
// Validation source: docs/TEST_PLAN.md section 1 (scope); NamingScopeRules header.

using FileNamingManager.Core;

namespace FileNamingManager.UnitTests;

public class NamingScopeRulesTests
{
    [Theory]
    [InlineData("OldVersions")]
    [InlineData("_V")]
    [InlineData("3rd Party Hardware")]
    [InlineData("Content Center Files")]
    [InlineData("_renamed-originals")]
    public void EachReservedFolderNameIsExcluded(string folderName)
    {
        Assert.True(NamingScopeRules.IsExcludedFolderName(folderName));
        Assert.Contains(folderName, NamingScopeRules.ExcludedFolderNames);
    }

    /// <summary>
    /// Inventor and Vault do not agree on casing between machines, and a folder the user typed by hand
    /// rarely matches the canonical spelling. An ordinal comparison here would walk into OldVersions and
    /// offer to rename superseded files, so every casing of every reserved name has to be excluded.
    /// </summary>
    [Theory]
    [InlineData("oldversions")]
    [InlineData("OLDVERSIONS")]
    [InlineData("_v")]
    [InlineData("3RD PARTY HARDWARE")]
    [InlineData("content center files")]
    [InlineData("_RENAMED-ORIGINALS")]
    public void ReservedFolderNamesAreExcludedRegardlessOfCasing(string folderName)
    {
        Assert.True(NamingScopeRules.IsExcludedFolderName(folderName));
    }

    /// <summary>
    /// The rule matches a whole path segment, not a prefix: 'OldVersions2' is an ordinary project folder
    /// whose files must be renamed, and 'Parts' is the normal place a project keeps its models. A
    /// StartsWith or Contains test here would silently drop real work from the scope.
    /// </summary>
    [Theory]
    [InlineData("OldVersions2")]
    [InlineData("")]
    [InlineData("Parts")]
    public void OrdinaryFolderNamesAreNotExcluded(string folderName)
    {
        Assert.False(NamingScopeRules.IsExcludedFolderName(folderName));
    }

    [Fact]
    public void RenamedOriginalsArchiveFolderIsExcludedFromTheScope()
    {
        Assert.Contains(NamingScopeRules.RenamedOriginalsFolderName, NamingScopeRules.ExcludedFolderNames);
        Assert.True(NamingScopeRules.IsExcludedFolderName(NamingScopeRules.RenamedOriginalsFolderName));
    }

    [Fact]
    public void NullFolderNameThrowsNamingTheOffendingParameter()
    {
        Assert.Throws<ArgumentNullException>("folderName", () => NamingScopeRules.IsExcludedFolderName(null!));
    }

    /// <summary>
    /// The list is the single source both the filesystem enumeration and the per-row scope rule read, so
    /// its exact contents are a contract: a name added or dropped here changes what the tool renames.
    /// </summary>
    [Fact]
    public void ExcludedFolderNamesHoldsExactlyTheFiveReservedNames()
    {
        Assert.Equal(
            ["OldVersions", "_V", "3rd Party Hardware", "Content Center Files", "_renamed-originals"],
            NamingScopeRules.ExcludedFolderNames);
    }
}
