// Covers the three remaining adapters. The HTTP source is driven through a stub message handler, so
// the suite exercises URL construction, status handling and the download path without a network.

using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using WmpToolsManager.Application;
using WmpToolsManager.Core;
using WmpToolsManager.Infrastructure;

namespace WmpToolsManager.UnitTests;

public sealed class Sha256HashVerifierTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "wmp-tools-manager-hash-" + Guid.NewGuid().ToString("N"));

    public Sha256HashVerifierTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ComputesTheSameLowercaseHexGetFileHashPublishes()
    {
        string path = Path.Combine(root, "package.zip");
        byte[] content = Encoding.UTF8.GetBytes("WMP Inventor Tools");
        File.WriteAllBytes(path, content);
        string expected = Convert.ToHexStringLower(SHA256.HashData(content));

        HashResult result = new Sha256HashVerifier().ComputeSha256(path);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Sha256);
        Assert.Equal(result.Sha256, result.Sha256!.ToLowerInvariant());
    }

    [Fact]
    public void HashesAnEmptyFileRatherThanRefusingIt()
    {
        string path = Path.Combine(root, "empty.zip");
        File.WriteAllBytes(path, []);

        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            new Sha256HashVerifier().ComputeSha256(path).Sha256);
    }

    [Fact]
    public void ReportsAFileItCannotRead()
    {
        HashResult result = new Sha256HashVerifier().ComputeSha256(Path.Combine(root, "absent.zip"));

        Assert.False(result.IsSuccess);
        Assert.Null(result.Sha256);
        Assert.Contains("could not be hashed", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RefusesAnEmptyPath(string? path) =>
        Assert.ThrowsAny<ArgumentException>(() => new Sha256HashVerifier().ComputeSha256(path!));
}

public sealed class ProcessLauncherTests
{
    [Fact]
    public void ReportsAnExecutableWindowsCannotStart()
    {
        LaunchResult result = new ProcessLauncher().Launch(
            "wmp-tools-manager-no-such-executable.exe",
            "-NoProfile");

        Assert.False(result.Launched);
        Assert.Contains("could not be started", result.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void RefusesAnEmptyExecutableName(string? fileName) =>
        Assert.ThrowsAny<ArgumentException>(() => new ProcessLauncher().Launch(fileName!, ""));

    [Fact]
    public void RefusesNullArguments() =>
        Assert.Throws<ArgumentNullException>(() => new ProcessLauncher().Launch("cmd.exe", null!));
}

public sealed class GitHubReleaseSourceTests
{
    private const string Repository = "srinator22/Smart-Manufacturing-Macro";

    [Fact]
    public void BuildsTheClientWithTheUserAgentAndTimeoutGitHubNeeds()
    {
        using HttpClient client = GitHubReleaseSource.CreateHttpClient();

        Assert.Equal(TimeSpan.FromSeconds(10), client.Timeout);
        Assert.Equal(GitHubReleaseSource.DefaultTimeout, client.Timeout);
        Assert.Equal("WmpInventorTools", client.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Fact]
    public async Task FetchesTheDigestFileFromTheLatestDownloadRedirectNotTheApi()
    {
        StubHandler handler = new(new StubResponse(HttpStatusCode.OK, "digest  file.zip"));
        using HttpClient client = new(handler);

        SourceText text = await new GitHubReleaseSource(client, Repository).GetLatestSumsAsync(default);

        Assert.True(text.IsSuccess);
        Assert.Equal("digest  file.zip", text.Content);
        Assert.Equal(
            $"https://github.com/{Repository}/releases/latest/download/SHA256SUMS.txt",
            Assert.Single(handler.RequestedUris).ToString());
    }

    [Fact]
    public async Task FetchesTheReleasePayloadForOneTagFromTheApi()
    {
        StubHandler handler = new(new StubResponse(HttpStatusCode.OK, "{}"));
        using HttpClient client = new(handler);

        SourceText text = await new GitHubReleaseSource(client, Repository).GetReleasePayloadAsync("v0.6.0", default);

        Assert.True(text.IsSuccess);
        Assert.Equal(
            $"https://api.github.com/repos/{Repository}/releases/tags/v0.6.0",
            Assert.Single(handler.RequestedUris).ToString());
    }

    [Fact]
    public async Task ReportsANonSuccessStatusAsText()
    {
        StubHandler handler = new(new StubResponse(HttpStatusCode.Forbidden, "rate limited"));
        using HttpClient client = new(handler);

        SourceText text = await new GitHubReleaseSource(client, Repository).GetReleasePayloadAsync("v0.6.0", default);

        Assert.False(text.IsSuccess);
        Assert.Null(text.Content);
        Assert.Contains("could not be fetched", text.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsATimeoutNamingTheCeiling()
    {
        StubHandler handler = new(new StubResponse(HttpStatusCode.OK, "", new TaskCanceledException("timed out")));
        using HttpClient client = new(handler);

        SourceText text = await new GitHubReleaseSource(client, Repository).GetLatestSumsAsync(default);

        Assert.False(text.IsSuccess);
        Assert.Contains("did not answer within 10 seconds", text.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesADownloadedAssetToTheDestinationAndReportsItsSize()
    {
        string directory = Path.Combine(Path.GetTempPath(), "wmp-tools-manager-dl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string destination = Path.Combine(directory, "WmpInventorTools-0.6.0.zip");
            StubHandler handler = new(new StubResponse(HttpStatusCode.OK, "package bytes"));
            using HttpClient client = new(handler);

            SourceFile file = await new GitHubReleaseSource(client, Repository)
                .DownloadLatestAssetAsync("WmpInventorTools-0.6.0.zip", destination, default);

            Assert.True(file.IsSuccess);
            Assert.Equal(destination, file.FilePath);
            Assert.Equal(13L, file.SizeInBytes);
            Assert.Equal("package bytes", File.ReadAllText(destination));
            Assert.Equal(
                $"https://github.com/{Repository}/releases/latest/download/WmpInventorTools-0.6.0.zip",
                Assert.Single(handler.RequestedUris).ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReportsADownloadThatCannotBeWritten()
    {
        StubHandler handler = new(new StubResponse(HttpStatusCode.OK, "package bytes"));
        using HttpClient client = new(handler);

        SourceFile file = await new GitHubReleaseSource(client, Repository).DownloadLatestAssetAsync(
            "WmpInventorTools-0.6.0.zip",
            Path.Combine(Path.GetTempPath(), "wmp-no-such-folder-" + Guid.NewGuid().ToString("N"), "a.zip"),
            default);

        Assert.False(file.IsSuccess);
        Assert.Equal(0L, file.SizeInBytes);
        Assert.Contains("could not be fetched", file.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsATransportFailureForAFailedDownload()
    {
        StubHandler handler = new(new StubResponse(HttpStatusCode.NotFound, ""));
        using HttpClient client = new(handler);

        SourceFile file = await new GitHubReleaseSource(client, Repository).DownloadLatestAssetAsync(
            "Install-WmpInventorTools.ps1",
            Path.Combine(Path.GetTempPath(), "wmp-unused-" + Guid.NewGuid().ToString("N") + ".ps1"),
            default);

        Assert.False(file.IsSuccess);
    }

    [Fact]
    public async Task RefusesAnIncompleteComposition()
    {
        using HttpClient client = new(new StubHandler(new StubResponse(HttpStatusCode.OK, "")));

        Assert.Throws<ArgumentNullException>(() => new GitHubReleaseSource(null!));
        Assert.ThrowsAny<ArgumentException>(() => new GitHubReleaseSource(client, " "));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new GitHubReleaseSource(client).GetReleasePayloadAsync(" ", default));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new GitHubReleaseSource(client).DownloadLatestAssetAsync(" ", @"C:\unused.zip", default));
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => new GitHubReleaseSource(client).DownloadLatestAssetAsync("a", " ", default));
    }

    private sealed record StubResponse(HttpStatusCode Status, string Content, Exception? Throw = null);

    private sealed class StubHandler(StubResponse response) : HttpMessageHandler
    {
        public List<Uri> RequestedUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestedUris.Add(request.RequestUri!);
            if (response.Throw is not null)
            {
                return Task.FromException<HttpResponseMessage>(response.Throw);
            }

            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Content),
            });
        }
    }
}
