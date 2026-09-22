// The adapters' failure paths, driven for real: a file another handle holds open, a cancelled request,
// and one process that actually starts. Without these the catch blocks and the success branch are
// never executed, and a gate that never executes them cannot claim they work.

using System.Net;
using System.Net.Http;
using WmpToolsManager.Application;
using WmpToolsManager.Core;
using WmpToolsManager.Infrastructure;

namespace WmpToolsManager.UnitTests;

public sealed class InfrastructureFailurePathTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "wmp-tools-manager-failure-" + Guid.NewGuid().ToString("N"));

    public InfrastructureFailurePathTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReportsAnInstalledStateAnotherHandleHasLocked()
    {
        string path = Path.Combine(root, "installed.json");
        File.WriteAllText(path, """{ "version": "0.6.0" }""");

        using FileStream exclusive = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
        ParseResult<InstalledState> parse = new PhysicalInstallState().ReadInstalled(path);

        Assert.False(parse.IsSuccess);
        Assert.StartsWith("installed.json could not be read:", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAPackageAnotherHandleHasLocked()
    {
        string path = Path.Combine(root, "WmpInventorTools-0.6.0.zip");
        File.WriteAllBytes(path, [0x50, 0x4B, 0x03, 0x04]);

        using FileStream exclusive = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
        ParseResult<ReleaseCatalog> parse = new PhysicalInstallState().ReadCatalog(path);

        Assert.False(parse.IsSuccess);
        Assert.StartsWith("The package 'WmpInventorTools-0.6.0.zip' could not be read:", parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportsAFileAnotherHandleHasLockedRatherThanHashingIt()
    {
        string path = Path.Combine(root, "package.zip");
        File.WriteAllText(path, "content");

        using FileStream exclusive = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
        HashResult result = new Sha256HashVerifier().ComputeSha256(path);

        Assert.False(result.IsSuccess);
        Assert.StartsWith($"'{path}' could not be hashed:", result.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void StartsARealProcessAndReportsItsIdentity()
    {
        // cmd.exe /c exit starts, does nothing and returns immediately. It is the smallest real
        // process that proves the success branch actually runs, which no stub can show.
        LaunchResult result = new ProcessLauncher().Launch("cmd.exe", "/c exit");

        Assert.True(result.Launched);
        Assert.StartsWith("Started 'cmd.exe' (process ", result.Message, StringComparison.Ordinal);
        Assert.EndsWith(").", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReportsACancelledRequestAsATimeoutNamingTheCeiling()
    {
        using HttpClient client = new(new ThrowingHandler(new OperationCanceledException("cancelled")));

        SourceText text = await new GitHubReleaseSource(client).GetLatestSumsAsync(default);

        Assert.False(text.IsSuccess);
        Assert.EndsWith("did not answer within 10 seconds.", text.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LetsAnUnexpectedFailureEscapeRatherThanCallingItATransportProblem()
    {
        // The catch filter lists the transport failures this adapter knows how to describe. Anything
        // else - a bug in this process - must not be reported to the user as "GitHub is unreachable".
        using HttpClient client = new(new ThrowingHandler(new InvalidTimeZoneException("not a transport failure")));

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            () => new GitHubReleaseSource(client).GetLatestSumsAsync(default));
    }

    [Fact]
    public async Task DescribesAFailedStatusWithTheUrlItAsked()
    {
        using HttpClient client = new(new StatusHandler(HttpStatusCode.NotFound));

        SourceText text = await new GitHubReleaseSource(client).GetLatestSumsAsync(default);

        Assert.StartsWith(
            "'https://github.com/srinator22/Smart-Manufacturing-Macro/releases/latest/download/SHA256SUMS.txt' could not be fetched:",
            text.ErrorMessage,
            StringComparison.Ordinal);
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(string.Empty) });
    }
}
