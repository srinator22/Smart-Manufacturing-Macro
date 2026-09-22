// Purpose: Fetch what the updater needs from GitHub without a token - the release digest file that
//   answers "what is the latest version", the release payload that carries the notes body, and the
//   release assets themselves.
// Inputs: An HttpClient the caller owns, and the pinned repository.
// Outputs: SourceText / SourceFile results. Network, DNS, TLS, timeout and HTTP status failures are
//   returned as text, never thrown, because this runs behind an Inventor command callback.
// Dependencies: System.Net.Http and the Application ports.
// Assumptions: releases/latest/download/<asset> is a redirect that needs no token and is not
//   rate-limited, which is why it - not the API - answers the version question. api.github.com is
//   rate-limited to 60 calls per hour per IP unauthenticated; the notes call is the only user of it and
//   its failure degrades to "no notes". GitHub rejects API requests with no User-Agent.
// Validation source: docs/decisions/0005-release-distribution-and-updater.md items 1 and 3;
//   scripts/release/Install-WmpInventorTools.ps1, which downloads from the same base URL.

using System.Globalization;
using WmpToolsManager.Application;
using WmpToolsManager.Core;

namespace WmpToolsManager.Infrastructure;

public sealed class GitHubReleaseSource : IReleaseSource
{
    /// <summary>The repository ADR-0005 pins the distribution to.</summary>
    public const string DefaultRepository = "srinator22/Smart-Manufacturing-Macro";

    /// <summary>The User-Agent GitHub requires; it identifies this tool in the repository's traffic.</summary>
    public const string UserAgent = "WmpInventorTools";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient httpClient;
    private readonly string repository;

    /// <summary>
    /// The HttpClient is not owned here: the add-in creates one for the life of the session and
    /// disposes it on Deactivate, so a repeated update check does not exhaust sockets.
    /// </summary>
    public GitHubReleaseSource(HttpClient httpClient, string repository = DefaultRepository)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        this.httpClient = httpClient;
        this.repository = repository;
    }

    /// <summary>
    /// Builds the client this source expects: the required User-Agent and the 10 second ceiling that
    /// keeps a stalled network from holding an Inventor command open.
    /// </summary>
    public static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            Timeout = DefaultTimeout,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    public Task<SourceText> GetLatestSumsAsync(CancellationToken cancellationToken) =>
        GetTextAsync(LatestAssetUri(Sha256SumsFile.FileName), cancellationToken);

    public Task<SourceText> GetReleasePayloadAsync(string tag, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        return GetTextAsync(
            new Uri(string.Create(
                CultureInfo.InvariantCulture,
                $"https://api.github.com/repos/{repository}/releases/tags/{Uri.EscapeDataString(tag)}")),
            cancellationToken);
    }

    public async Task<SourceFile> DownloadLatestAssetAsync(
        string assetFileName,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        Uri uri = LatestAssetUri(assetFileName);
        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (FileStream file = new(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            return new SourceFile(destinationPath, new FileInfo(destinationPath).Length, null);
        }
        catch (Exception exception) when (IsExpectedTransportFailure(exception))
        {
            return new SourceFile(null, 0, Describe(uri, exception));
        }
    }

    private static bool IsExpectedTransportFailure(Exception exception) =>
        exception is HttpRequestException
            or TaskCanceledException
            or OperationCanceledException
            or IOException
            or UnauthorizedAccessException;

    private static string Describe(Uri uri, Exception exception) => exception switch
    {
        TaskCanceledException or OperationCanceledException =>
            $"'{uri}' did not answer within {DefaultTimeout.TotalSeconds:0} seconds.",
        _ => $"'{uri}' could not be fetched: {exception.Message}",
    };

    private Uri LatestAssetUri(string assetFileName) => new(string.Create(
        CultureInfo.InvariantCulture,
        $"https://github.com/{repository}/releases/latest/download/{Uri.EscapeDataString(assetFileName)}"));

    private async Task<SourceText> GetTextAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new SourceText(content, null);
        }
        catch (Exception exception) when (IsExpectedTransportFailure(exception))
        {
            return new SourceText(null, Describe(uri, exception));
        }
    }
}
