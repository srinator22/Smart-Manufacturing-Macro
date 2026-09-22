using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests;

public sealed class MaturityTests
{
    [Theory]
    [InlineData("beta", true)]
    [InlineData("Beta", true)]
    [InlineData(" beta ", true)]
    [InlineData("stable", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("betas", false)]
    public void RecognisesBetaRegardlessOfCaseOrSurroundingSpace(string? maturity, bool isBeta) =>
        Assert.Equal(isBeta, Maturity.IsBeta(maturity));

    [Theory]
    [InlineData("beta", " (beta)")]
    [InlineData("BETA", " (beta)")]
    [InlineData("stable", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SuffixesOnlyABetaTooltip(string? maturity, string expected) =>
        Assert.Equal(expected, Maturity.TooltipSuffix(maturity));

    [Theory]
    [InlineData("beta", "beta - not yet validated in live Inventor")]
    [InlineData("stable", "stable")]
    [InlineData("  ", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("experimental", "experimental")]
    public void DescribesMaturityWithTheSameWordingTheInstallerPrints(string? maturity, string expected) =>
        Assert.Equal(expected, Maturity.Describe(maturity));
}

public sealed class ReleaseNotesTests
{
    [Fact]
    public void ReturnsTheWholeBodyWhenItFitsInTheExcerpt()
    {
        string excerpt = ReleaseNotes.Excerpt("First line\nSecond line", 5);

        Assert.Equal($"First line{Environment.NewLine}Second line", excerpt);
        Assert.DoesNotContain(ReleaseNotes.TruncationMarker, excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void MarksAnExcerptThatCutTheBody()
    {
        string excerpt = ReleaseNotes.Excerpt("one\ntwo\nthree\nfour", 2);

        Assert.StartsWith($"one{Environment.NewLine}two", excerpt, StringComparison.Ordinal);
        Assert.EndsWith(ReleaseNotes.TruncationMarker, excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain("three", excerpt, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalisesCarriageReturnsBeforeCounting()
    {
        string excerpt = ReleaseNotes.Excerpt("one\r\ntwo\r\nthree", 3);

        Assert.Equal($"one{Environment.NewLine}two{Environment.NewLine}three", excerpt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void ReturnsNothingForAnAbsentBody(string? body) =>
        Assert.Equal(string.Empty, ReleaseNotes.Excerpt(body));

    [Fact]
    public void RefusesAnExcerptOfFewerThanOneLine() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ReleaseNotes.Excerpt("body", 0));

    [Fact]
    public void DefaultsToTwentyLines()
    {
        string body = string.Join('\n', Enumerable.Range(1, 30).Select(line => $"line {line}"));

        string excerpt = ReleaseNotes.Excerpt(body);

        Assert.Contains("line 20", excerpt, StringComparison.Ordinal);
        Assert.DoesNotContain("line 21", excerpt, StringComparison.Ordinal);
        Assert.Equal(20, ReleaseNotes.DefaultLineCount);
    }
}
