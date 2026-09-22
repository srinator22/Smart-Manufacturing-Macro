// Every guard is asserted by the parameter it names, not just by the exception type. A guard that is
// simply deleted usually still produces some ArgumentException from further down the call - from
// ZipFile.OpenRead or Path.IsPathRooted - so asserting only the type would let the deletion pass.

using System.Net.Http;
using WmpToolsManager.Core;
using WmpToolsManager.Infrastructure;

namespace WmpToolsManager.UnitTests;

public sealed class ArgumentGuardTests
{
    private static readonly PhysicalInstallState State = new();

    public static TheoryData<string?> EmptyPaths => new() { null, string.Empty, "   " };

    [Theory]
    [MemberData(nameof(EmptyPaths))]
    public void PhysicalInstallStateNamesTheParameterItRefused(string? path)
    {
        Assert.Equal("installedStatePath", Refused(() => _ = State.ReadInstalled(path!)));
        Assert.Equal("packageZipPath", Refused(() => _ = State.ReadCatalog(path!)));
        Assert.Equal("previousRoot", Refused(() => _ = State.HasPreviousInstall(path!)));
        Assert.Equal("stateRoot", Refused(() => _ = State.FindInstaller(path!)));
        Assert.Equal("path", Refused(() => State.CreateDirectory(path!)));
        Assert.Equal("path", Refused(() => State.WriteText(path!, "content")));
    }

    [Theory]
    [MemberData(nameof(EmptyPaths))]
    public void HashVerifierNamesTheParameterItRefused(string? path) =>
        Assert.Equal("filePath", Refused(() => _ = new Sha256HashVerifier().ComputeSha256(path!)));

    [Theory]
    [MemberData(nameof(EmptyPaths))]
    public void ProcessLauncherNamesTheParameterItRefused(string? fileName) =>
        Assert.Equal("fileName", Refused(() => _ = new ProcessLauncher().Launch(fileName!, "-NoProfile")));

    [Theory]
    [MemberData(nameof(EmptyPaths))]
    public async Task ReleaseSourceNamesTheParameterItRefused(string? value)
    {
        using HttpClient client = GitHubReleaseSource.CreateHttpClient();
        GitHubReleaseSource source = new(client);

        ArgumentException tag = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => source.GetReleasePayloadAsync(value!, default));
        Assert.Equal("tag", tag.ParamName);

        ArgumentException asset = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => source.DownloadLatestAssetAsync(value!, @"C:\unused.zip", default));
        Assert.Equal("assetFileName", asset.ParamName);

        ArgumentException destination = await Assert.ThrowsAnyAsync<ArgumentException>(
            () => source.DownloadLatestAssetAsync("SHA256SUMS.txt", value!, default));
        Assert.Equal("destinationPath", destination.ParamName);
    }

    [Theory]
    [MemberData(nameof(EmptyPaths))]
    public void StagingPathsNamesTheParameterItRefused(string? value)
    {
        Assert.Equal("stateRoot", Refused(() => _ = new StagingPaths(value!)));
        Assert.Equal("localApplicationData", Refused(() => _ = StagingPaths.DefaultStateRoot(value!)));
        Assert.Equal(
            "fileName",
            Refused(() => _ = new StagingPaths(@"C:\state").StagedFile(new SemanticVersion(0, 6, 0), value!)));
    }

    [Fact]
    public void ReleaseManifestParsingRefusesNullContentByName() =>
        Assert.Equal("content", Refused(() => _ = Sha256SumsFile.ParseEntries(null!)));

    private static string? Refused(Action action) => Assert.ThrowsAny<ArgumentException>(action).ParamName;
}
