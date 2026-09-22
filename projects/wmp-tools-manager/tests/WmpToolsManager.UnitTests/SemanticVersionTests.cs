using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("0.6.0", 0, 6, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("10.20.30", 10, 20, 30)]
    [InlineData("0.0.0", 0, 0, 0)]
    public void ParsesAThreePartVersion(string text, int major, int minor, int patch)
    {
        Assert.True(SemanticVersion.TryParse(text, out SemanticVersion version));
        Assert.Equal(new SemanticVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.")]
    [InlineData(".1.2")]
    [InlineData("1.2.x")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.3-rc.1")]
    [InlineData("1.2.3+build")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.3 ")]
    [InlineData("+1.2.3")]
    [InlineData("-1.2.3")]
    [InlineData("1,2,3")]
    [InlineData("1.2.3456789012")]
    public void RefusesAnythingThatIsNotThreeNumericComponents(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out SemanticVersion version));
        Assert.Equal(default, version);
    }

    [Fact]
    public void ParsesTheAnnotatedTagFormTheReleaseWorkflowPublishes()
    {
        Assert.True(SemanticVersion.TryParseTag("v0.6.0", out SemanticVersion version));
        Assert.Equal(new SemanticVersion(0, 6, 0), version);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0.6.0")]
    [InlineData("V0.6.0")]
    [InlineData("version0.6.0")]
    [InlineData("v0.6")]
    [InlineData("v")]
    public void RefusesATagThatIsNotLowercaseVFollowedByThreeComponents(string? tag) =>
        Assert.False(SemanticVersion.TryParseTag(tag, out _));

    [Fact]
    public void OrdersNumericallyNotAlphabetically()
    {
        SemanticVersion nine = new(0, 9, 0);
        SemanticVersion ten = new(0, 10, 0);

        Assert.True(nine < ten);
        Assert.True(ten > nine);
        Assert.True(nine <= new SemanticVersion(0, 9, 0));
        Assert.True(nine >= new SemanticVersion(0, 9, 0));
        Assert.Equal(-1, Math.Sign(nine.CompareTo(ten)));
        Assert.Equal(1, Math.Sign(ten.CompareTo(nine)));
        Assert.Equal(0, nine.CompareTo(new SemanticVersion(0, 9, 0)));
    }

    [Fact]
    public void ComparesEachComponentInOrder()
    {
        Assert.True(new SemanticVersion(1, 0, 0) > new SemanticVersion(0, 99, 99));
        Assert.True(new SemanticVersion(0, 6, 1) > new SemanticVersion(0, 6, 0));
    }

    [Fact]
    public void ComparesAgainstAnUntypedValue()
    {
        SemanticVersion version = new(0, 6, 0);

        Assert.Equal(1, version.CompareTo(null));
        Assert.Equal(0, version.CompareTo((object)new SemanticVersion(0, 6, 0)));
        Assert.Throws<ArgumentException>(() => version.CompareTo("0.6.0"));
    }

    [Fact]
    public void RoundTripsThroughItsTextAndTagForms()
    {
        SemanticVersion version = new(0, 6, 0);

        Assert.Equal("0.6.0", version.ToString());
        Assert.Equal("v0.6.0", version.ToTag());
        Assert.True(SemanticVersion.TryParseTag(version.ToTag(), out SemanticVersion roundTripped));
        Assert.Equal(version, roundTripped);
    }

    [Fact]
    public void OrdersAVersionAsNeitherBelowNorAboveItself()
    {
        SemanticVersion version = new(0, 6, 0);
        SemanticVersion same = new(0, 6, 0);

        Assert.False(version < same);
        Assert.False(version > same);
        Assert.True(version <= same);
        Assert.True(version >= same);
    }

    [Fact]
    public void AcceptsTheLongestComponentItSupports()
    {
        Assert.True(SemanticVersion.TryParse("1.2.123456789", out SemanticVersion version));
        Assert.Equal(123456789, version.Patch);
    }

    [Fact]
    public void NamesBothTypesWhenComparedWithSomethingElse()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new SemanticVersion(0, 6, 0).CompareTo("0.6.0"));

        Assert.Contains("SemanticVersion", exception.Message, StringComparison.Ordinal);
        Assert.Contains("System.String", exception.Message, StringComparison.Ordinal);
        Assert.Equal("obj", exception.ParamName);
    }
}
