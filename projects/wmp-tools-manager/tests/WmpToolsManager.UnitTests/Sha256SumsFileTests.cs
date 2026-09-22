// The sample digest file is byte-shaped like the one scripts/release/build-release.ps1 writes:
// lowercase hex, two spaces, the file name, LF endings, trailing newline.

using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests;

public sealed class Sha256SumsFileTests
{
    private const string PackageDigest = "9f2c4a1b7d8e6f30a1b2c3d4e5f60718293a4b5c6d7e8f901a2b3c4d5e6f7081";
    private const string InstallerDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private const string Published =
        PackageDigest + "  WmpInventorTools-0.6.0.zip\n" +
        InstallerDigest + "  Install-WmpInventorTools.ps1\n";

    [Fact]
    public void ReadsTheVersionAndBothDigestsFromAPublishedDigestFile()
    {
        ReleaseManifestParse parse = Sha256SumsFile.Parse(Published);

        Assert.True(parse.IsSuccess);
        Assert.Null(parse.ErrorMessage);
        ReleaseManifest manifest = parse.Manifest!;
        Assert.Equal(new SemanticVersion(0, 6, 0), manifest.Version);
        Assert.Equal("WmpInventorTools-0.6.0.zip", manifest.PackageFileName);
        Assert.Equal(PackageDigest, manifest.PackageSha256);
        Assert.Equal(InstallerDigest, manifest.InstallerSha256);
        Assert.Equal(2, manifest.Entries.Count);
    }

    [Fact]
    public void AcceptsCrlfEndingsAndAStarPrefixedFileName()
    {
        ReleaseManifestParse parse = Sha256SumsFile.Parse(
            PackageDigest + " *WmpInventorTools-1.0.0.zip\r\n");

        Assert.True(parse.IsSuccess);
        Assert.Equal(new SemanticVersion(1, 0, 0), parse.Manifest!.Version);
        Assert.Equal("WmpInventorTools-1.0.0.zip", parse.Manifest.PackageFileName);
    }

    [Fact]
    public void NormalisesUppercaseDigestsToLowercase()
    {
        ReleaseManifestParse parse = Sha256SumsFile.Parse(
            PackageDigest.ToUpperInvariant() + "  WmpInventorTools-0.6.0.zip\n");

        Assert.True(parse.IsSuccess);
        Assert.Equal(PackageDigest, parse.Manifest!.PackageSha256);
    }

    [Fact]
    public void ReportsAMissingInstallerDigestAsAbsentRatherThanEmpty()
    {
        ReleaseManifestParse parse = Sha256SumsFile.Parse(PackageDigest + "  WmpInventorTools-0.6.0.zip\n");

        Assert.True(parse.IsSuccess);
        Assert.Null(parse.Manifest!.InstallerSha256);
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("   \n\n", "empty")]
    public void RefusesAnEmptyDigestFile(string? content, string expectedFragment)
    {
        ReleaseManifestParse parse = Sha256SumsFile.Parse(content);

        Assert.False(parse.IsSuccess);
        Assert.Contains(expectedFragment, parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFileWithNoRecognisableDigestLine()
    {
        ReleaseManifestParse parse = Sha256SumsFile.Parse("# nothing to see here\nnot-a-digest file.zip\n");

        Assert.False(parse.IsSuccess);
        Assert.Contains("no '<sha-256>  <file name>' lines", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesADigestFileThatListsNoPackage()
    {
        ReleaseManifestParse parse = Sha256SumsFile.Parse(InstallerDigest + "  Install-WmpInventorTools.ps1\n");

        Assert.False(parse.IsSuccess);
        Assert.Contains("does not list a WmpInventorTools-<version>.zip", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAPackageWhoseNameCarriesNoSemanticVersion()
    {
        ReleaseManifestParse parse = Sha256SumsFile.Parse(PackageDigest + "  WmpInventorTools-nightly.zip\n");

        Assert.False(parse.IsSuccess);
        Assert.Contains("does not carry a MAJOR.MINOR.PATCH version", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("abc  short.zip")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz  nonhex.zip")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde  tooshort.zip")]
    public void IgnoresLinesWhoseFirstFieldIsNotASha256Digest(string line) =>
        Assert.Empty(Sha256SumsFile.ParseEntries(line + "\n"));

    [Fact]
    public void IgnoresALineWithNoFileName() =>
        Assert.Empty(Sha256SumsFile.ParseEntries(PackageDigest + "\n"));

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("abc", null, false)]
    [InlineData(null, "abc", false)]
    [InlineData("", "", false)]
    [InlineData("abc", "abd", false)]
    [InlineData("abc", "abc", true)]
    [InlineData("ABC", "abc", true)]
    public void ComparesDigestsCaseInsensitivelyAndNeverMatchesAMissingOne(
        string? expected,
        string? actual,
        bool matches) =>
        Assert.Equal(matches, Sha256SumsFile.DigestsMatch(expected, actual));

    [Fact]
    public void ParseEntriesRefusesNull() =>
        Assert.Throws<ArgumentNullException>(() => Sha256SumsFile.ParseEntries(null!));

    [Fact]
    public void PicksTheZipEvenWhenAnotherArtifactSharesThePackagePrefix()
    {
        ReleaseManifestParse parse = Sha256SumsFile.Parse(
            InstallerDigest + "  WmpInventorTools-0.6.0.txt\n" +
            PackageDigest + "  WmpInventorTools-0.6.0.zip\n");

        Assert.True(parse.IsSuccess);
        Assert.Equal("WmpInventorTools-0.6.0.zip", parse.Manifest!.PackageFileName);
        Assert.Equal(PackageDigest, parse.Manifest.PackageSha256);
    }
}
