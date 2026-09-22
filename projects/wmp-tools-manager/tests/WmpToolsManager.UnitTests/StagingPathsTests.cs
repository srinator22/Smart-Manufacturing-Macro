// These assert path arithmetic only; nothing here creates a folder or touches the disk.

using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests;

public sealed class StagingPathsTests
{
    private const string StateRoot = @"C:\Users\Tester\AppData\Local\WMP\InventorTools";

    private static StagingPaths Paths => new(StateRoot);

    [Fact]
    public void DerivesTheInstallerSOwnDefaultStateRoot() =>
        Assert.Equal(
            @"C:\Users\Tester\AppData\Local\WMP\InventorTools",
            StagingPaths.DefaultStateRoot(@"C:\Users\Tester\AppData\Local"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnEmptyLocalApplicationData(string? localApplicationData) =>
        Assert.ThrowsAny<ArgumentException>(() => StagingPaths.DefaultStateRoot(localApplicationData!));

    [Fact]
    public void ExposesTheLayoutTheInstallerWrites()
    {
        StagingPaths paths = Paths;

        Assert.Equal(StateRoot, paths.StateRoot);
        Assert.Equal(Path.Combine(StateRoot, "staging"), paths.StagingRoot);
        Assert.Equal(Path.Combine(StateRoot, "previous"), paths.PreviousRoot);
        Assert.Equal(Path.Combine(StateRoot, "installed.json"), paths.InstalledStateFile);
    }

    [Fact]
    public void TrimsATrailingSeparatorFromTheStateRoot() =>
        Assert.Equal(StateRoot, new StagingPaths(StateRoot + @"\").StateRoot);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnEmptyStateRoot(string? stateRoot) =>
        Assert.ThrowsAny<ArgumentException>(() => new StagingPaths(stateRoot!));

    [Fact]
    public void RefusesARelativeStateRoot() =>
        Assert.Throws<ArgumentException>(() => new StagingPaths(@"WMP\InventorTools"));

    [Fact]
    public void PlacesEachVersionInItsOwnStagingFolder() =>
        Assert.Equal(
            Path.Combine(StateRoot, "staging", "0.6.0"),
            Paths.StagingDirectoryFor(new SemanticVersion(0, 6, 0)));

    [Fact]
    public void PlacesAStagedArtifactInsideItsVersionFolder() =>
        Assert.Equal(
            Path.Combine(StateRoot, "staging", "0.6.0", "WmpInventorTools-0.6.0.zip"),
            Paths.StagedFile(new SemanticVersion(0, 6, 0), "WmpInventorTools-0.6.0.zip"));

    [Theory]
    [InlineData(@"..\..\..\evil.zip")]
    [InlineData("sub/evil.zip")]
    [InlineData(@"sub\evil.zip")]
    [InlineData(@"C:\Windows\System32\evil.zip")]
    public void RefusesAStagedArtifactNameThatIsNotABareFileName(string fileName) =>
        Assert.Throws<ArgumentException>(() => Paths.StagedFile(new SemanticVersion(0, 6, 0), fileName));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnEmptyStagedArtifactName(string? fileName) =>
        Assert.ThrowsAny<ArgumentException>(() => Paths.StagedFile(new SemanticVersion(0, 6, 0), fileName!));

    [Theory]
    [InlineData(StateRoot, true)]
    [InlineData(StateRoot + @"\", true)]
    [InlineData(StateRoot + @"\staging\0.6.0\package.zip", true)]
    [InlineData(StateRoot + @"\staging\..\previous", true)]
    [InlineData(@"c:\users\tester\appdata\local\wmp\inventortools\staging", true)]
    [InlineData(@"C:\Users\Tester\AppData\Local\WMP", false)]
    [InlineData(@"C:\Users\Tester\AppData\Local\WMP\InventorToolsEvil", false)]
    [InlineData(StateRoot + @"\..\..\Roaming\Autodesk", false)]
    [InlineData(@"C:\Windows\System32", false)]
    [InlineData(@"staging\0.6.0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AnswersWhetherAPathIsUnderTheStateRoot(string? candidate, bool expected) =>
        Assert.Equal(expected, Paths.IsUnderStateRoot(candidate));

    [Fact]
    public void ReturnsTheResolvedPathForSomethingInsideTheStateRoot() =>
        Assert.Equal(
            Path.Combine(StateRoot, "previous"),
            Paths.EnsureUnderStateRoot(StateRoot + @"\staging\..\previous"));

    [Fact]
    public void RefusesAPathOutsideTheStateRootAndNamesIt()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Paths.EnsureUnderStateRoot(@"C:\Windows\System32"));

        Assert.Contains(@"C:\Windows\System32", exception.Message, StringComparison.Ordinal);
        Assert.Contains(StateRoot, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesTheRelativeStateRootItRefused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => new StagingPaths(@"WMP\InventorTools"));

        Assert.Contains(@"WMP\InventorTools", exception.Message, StringComparison.Ordinal);
        Assert.Contains("absolute path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesTheStagedArtifactItRefused()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Paths.StagedFile(new SemanticVersion(0, 6, 0), @"sub\evil.zip"));

        Assert.Contains(@"sub\evil.zip", exception.Message, StringComparison.Ordinal);
        Assert.Contains("bare file name", exception.Message, StringComparison.Ordinal);
    }
}
