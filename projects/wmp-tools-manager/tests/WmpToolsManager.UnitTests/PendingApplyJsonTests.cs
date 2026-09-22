// These assert the marker record and its JSON alone; nothing here touches the disk or a process. The
// marker is the only thing standing between one waiting installer and a second one started on top of
// it, so a document this parser accepted when it should not have would silently disable the guard.

using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests;

public sealed class PendingApplyJsonTests
{
    private static readonly DateTimeOffset Launched = new(2026, 9, 23, 10, 30, 0, TimeSpan.Zero);

    [Fact]
    public void RoundTripsAMarkerThroughTheFormItIsWrittenIn()
    {
        string json = PendingApplyJson.Serialize(
            new PendingApply(4242, "0.6.0", PendingApply.UpdateKind, Launched));

        Assert.Contains("\"pid\": 4242", json, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"0.6.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"update\"", json, StringComparison.Ordinal);

        ParseResult<PendingApply> parse = PendingApplyJson.Parse(json);

        Assert.True(parse.IsSuccess);
        Assert.Equal(new PendingApply(4242, "0.6.0", PendingApply.UpdateKind, Launched), parse.Value);
    }

    [Fact]
    public void RoundTripsARollbackMarker()
    {
        ParseResult<PendingApply> parse = PendingApplyJson.Parse(
            PendingApplyJson.Serialize(new PendingApply(7, "0.6.0", PendingApply.RollbackKind, Launched)));

        Assert.True(parse.IsSuccess);
        Assert.Equal(PendingApply.RollbackKind, parse.Value!.Kind);
    }

    [Fact]
    public void RefusesToSerialiseNothing() =>
        Assert.Throws<ArgumentNullException>(() => PendingApplyJson.Serialize(null!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReportsAnEmptyMarker(string? json)
    {
        ParseResult<PendingApply> parse = PendingApplyJson.Parse(json);

        Assert.False(parse.IsSuccess);
        Assert.Equal("pending-apply.json was empty.", parse.ErrorMessage);
    }

    [Fact]
    public void ReportsAMarkerThatIsNotJson()
    {
        ParseResult<PendingApply> parse = PendingApplyJson.Parse("not json at all");

        Assert.False(parse.IsSuccess);
        Assert.StartsWith("pending-apply.json is not valid JSON:", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAMarkerThatDeserialisedToNothing()
    {
        ParseResult<PendingApply> parse = PendingApplyJson.Parse("null");

        Assert.False(parse.IsSuccess);
        Assert.Equal("pending-apply.json deserialised to null.", parse.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ReportsAProcessIdThatIdentifiesNothing(int pid)
    {
        ParseResult<PendingApply> parse = PendingApplyJson.Parse(
            $$"""{ "pid": {{pid}}, "version": "0.6.0", "kind": "update", "launchedUtc": "2026-09-23T10:30:00Z" }""");

        Assert.False(parse.IsSuccess);
        Assert.Equal(
            $"pending-apply.json records the process id {pid}, which identifies no process.",
            parse.ErrorMessage);
    }

    [Fact]
    public void ReportsAMarkerWithNoProcessIdAtAll()
    {
        ParseResult<PendingApply> parse = PendingApplyJson.Parse("""{ "version": "0.6.0", "kind": "update" }""");

        Assert.False(parse.IsSuccess);
        Assert.Contains("identifies no process", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Update")]
    [InlineData("install")]
    public void ReportsAKindItDoesNotRecognise(string kind)
    {
        ParseResult<PendingApply> parse = PendingApplyJson.Parse(
            $$"""{ "pid": 4242, "version": "0.6.0", "kind": "{{kind}}" }""");

        Assert.False(parse.IsSuccess);
        Assert.Equal(
            $"pending-apply.json records the kind '{kind}', which is neither 'update' nor 'rollback'.",
            parse.ErrorMessage);
    }

    [Fact]
    public void SubstitutesAnUnknownVersionRatherThanCarryingANullAcrossTheDeserialiser()
    {
        ParseResult<PendingApply> parse = PendingApplyJson.Parse("""{ "pid": 4242, "kind": "rollback" }""");

        Assert.True(parse.IsSuccess);
        Assert.Equal("an unknown version", parse.Value!.Version);
    }

    [Fact]
    public void ReadsTheKeysCaseInsensitivelyBecausePowerShellMayWriteThem()
    {
        ParseResult<PendingApply> parse = PendingApplyJson.Parse(
            """{ "Pid": 11, "Version": "0.7.0", "Kind": "update", "LaunchedUtc": "2026-09-23T10:30:00Z" }""");

        Assert.True(parse.IsSuccess);
        Assert.Equal(11, parse.Value!.Pid);
        Assert.Equal("0.7.0", parse.Value.Version);
    }

    [Fact]
    public void WordsAnUpdateAndARollbackDifferentlySoTheUserKnowsWhatIsWaiting()
    {
        Assert.Equal(
            "An update to 0.6.0 is already waiting for Inventor to close (PowerShell process 4242). "
            + "Close Inventor to let it finish.",
            new PendingApply(4242, "0.6.0", PendingApply.UpdateKind, Launched).WaitingMessage);

        Assert.Equal(
            "A rollback from 0.6.0 is already waiting for Inventor to close (PowerShell process 4242). "
            + "Close Inventor to let it finish.",
            new PendingApply(4242, "0.6.0", PendingApply.RollbackKind, Launched).WaitingMessage);
    }

    [Fact]
    public void PlacesTheMarkerBesideInstalledJsonInTheStateRoot()
    {
        StagingPaths paths = new(@"C:\Users\Tester\AppData\Local\WMP\InventorTools");

        Assert.Equal(
            Path.Combine(@"C:\Users\Tester\AppData\Local\WMP\InventorTools", "pending-apply.json"),
            paths.PendingApplyFile);
        Assert.True(paths.IsUnderStateRoot(paths.PendingApplyFile));
    }
}
