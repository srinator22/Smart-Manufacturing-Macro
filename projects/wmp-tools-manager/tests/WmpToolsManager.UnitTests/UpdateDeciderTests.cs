using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests;

public sealed class UpdateDeciderTests
{
    public static TheoryData<string?, string?, UpdateDecision> Matrix => new()
    {
        { "0.6.0", "0.6.0", UpdateDecision.UpToDate },
        { "0.5.0", "0.6.0", UpdateDecision.UpdateAvailable },
        { "0.6.0", "0.5.0", UpdateDecision.InstalledNewer },
        { "0.9.0", "0.10.0", UpdateDecision.UpdateAvailable },
        { "0.10.0", "0.9.0", UpdateDecision.InstalledNewer },
        { "1.0.0", "0.99.99", UpdateDecision.InstalledNewer },
        { null, "0.6.0", UpdateDecision.Unknown },
        { "0.6.0", null, UpdateDecision.Unknown },
        { null, null, UpdateDecision.Unknown },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void DecidesFromTheTwoVersionsAndNeverGuesses(string? installed, string? latest, UpdateDecision expected) =>
        Assert.Equal(expected, UpdateDecider.Decide(Parse(installed), Parse(latest)));

    [Fact]
    public void DescribesEveryDecisionNamingBothVersions()
    {
        SemanticVersion installed = new(0, 5, 0);
        SemanticVersion latest = new(0, 6, 0);

        Assert.Equal(
            "WMP Inventor Tools 0.5.0 is the latest release.",
            UpdateDecider.Describe(UpdateDecision.UpToDate, installed, installed));
        Assert.Equal(
            "WMP Inventor Tools 0.6.0 is available. You have 0.5.0.",
            UpdateDecider.Describe(UpdateDecision.UpdateAvailable, installed, latest));
        Assert.Equal(
            "The installed build 0.6.0 is ahead of the latest release 0.5.0; there is nothing to update to.",
            UpdateDecider.Describe(UpdateDecision.InstalledNewer, latest, installed));
        Assert.Equal(
            "The update state could not be determined. Installed: unknown. Latest: unknown.",
            UpdateDecider.Describe(UpdateDecision.Unknown, null, null));
    }

    private static SemanticVersion? Parse(string? text) =>
        SemanticVersion.TryParse(text, out SemanticVersion version) ? version : null;
}
