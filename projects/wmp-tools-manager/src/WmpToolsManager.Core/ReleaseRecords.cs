// Purpose: Model the JSON documents the updater reads - the Releases API payload used for the notes
//   body, the catalog.json inside a release package, and the installed.json the installer writes - and
//   parse them without ever throwing at the caller.
// Inputs: JSON text obtained by Infrastructure from api.github.com, from the staged package zip, and
//   from the state root.
// Outputs: Immutable records, or a parse failure carrying the reason.
// Dependencies: System.Text.Json only. No IO, no HTTP.
// Assumptions: catalog.json and installed.json are written by PowerShell's ConvertTo-Json over ordered
//   hashtables, so every key is the camelCase form of the property that reads it and case-insensitive
//   matching alone maps them - no per-property attribute restates a name the compiler already knows.
//   The GitHub payload instead uses snake_case (published_at, browser_download_url), which no
//   convention maps, so it is read element by element rather than deserialised into a record.
// Validation source: scripts/release/build-release.ps1 (catalog.json), scripts/release/Install-WmpInventorTools.ps1
//   (installed.json), and the documented GitHub "Get the latest release" response shape.

using System.Text.Json;

namespace WmpToolsManager.Core;

public sealed record ReleaseAsset(string Name, string DownloadUrl, long SizeInBytes);

/// <summary>
/// A published release as the Releases API describes it. Only <see cref="Body"/> is load-bearing: the
/// version comparison uses SHA256SUMS.txt instead, so this whole record is optional decoration.
/// </summary>
public sealed record ReleaseInfo(
    string Tag,
    string Name,
    string Body,
    DateTimeOffset? PublishedUtc,
    IReadOnlyList<ReleaseAsset> Assets);

public sealed record CatalogPlugin
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Maturity { get; init; } = string.Empty;

    public string InstallDirectory { get; init; } = string.Empty;

    public string Assembly { get; init; } = string.Empty;

    public string ManifestName { get; init; } = string.Empty;

    public string RibbonPanel { get; init; } = string.Empty;

    public IReadOnlyList<string> Commands { get; init; } = [];
}

public sealed record ReleaseCatalog
{
    public string Version { get; init; } = string.Empty;

    public string? ReleasedUtc { get; init; }

    public IReadOnlyList<CatalogPlugin> Plugins { get; init; } = [];
}

public sealed record InstalledPlugin
{
    public string Id { get; init; } = string.Empty;

    public string Maturity { get; init; } = string.Empty;

    public string InstallDirectory { get; init; } = string.Empty;

    public string ManifestName { get; init; } = string.Empty;
}

public sealed record InstalledState
{
    public string Version { get; init; } = string.Empty;

    public string? InstalledUtc { get; init; }

    public IReadOnlyList<InstalledPlugin> Plugins { get; init; } = [];
}

/// <summary>
/// This add-in's own plugin.json, embedded in the add-in assembly so the ribbon tooltip states the
/// maturity that shipped with the binary rather than whatever is on disk beside it.
/// </summary>
public sealed record PluginManifest
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Maturity { get; init; } = string.Empty;
}

/// <summary>
/// A parse outcome. <see cref="Value"/> is null exactly when <see cref="ErrorMessage"/> is set.
/// </summary>
public sealed record ParseResult<T>(T? Value, string? ErrorMessage)
    where T : class
{
    public bool IsSuccess => Value is not null;
}

public static class ReleaseJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static ParseResult<ReleaseCatalog> ParseCatalog(string? json)
    {
        ParseResult<ReleaseCatalog> parsed = Deserialize<ReleaseCatalog>(json, "catalog.json");
        if (!parsed.IsSuccess)
        {
            return parsed;
        }

        ReleaseCatalog catalog = parsed.Value!;
        return string.IsNullOrWhiteSpace(catalog.Version) || catalog.Plugins.Count == 0
            ? new ParseResult<ReleaseCatalog>(null, "catalog.json declares no version or no plugins.")
            : parsed;
    }

    public static ParseResult<InstalledState> ParseInstalledState(string? json)
    {
        ParseResult<InstalledState> parsed = Deserialize<InstalledState>(json, "installed.json");
        if (!parsed.IsSuccess)
        {
            return parsed;
        }

        return string.IsNullOrWhiteSpace(parsed.Value!.Version)
            ? new ParseResult<InstalledState>(null, "installed.json records no version.")
            : parsed;
    }

    public static ParseResult<PluginManifest> ParsePluginManifest(string? json)
    {
        ParseResult<PluginManifest> parsed = Deserialize<PluginManifest>(json, "plugin.json");
        if (!parsed.IsSuccess)
        {
            return parsed;
        }

        return string.IsNullOrWhiteSpace(parsed.Value!.Maturity)
            ? new ParseResult<PluginManifest>(null, "plugin.json declares no maturity.")
            : parsed;
    }

    /// <summary>
    /// Reads the fields of a Releases API payload this add-in uses. Every field is optional: an API
    /// response that has changed shape degrades to an empty body, never to a failed update check.
    /// </summary>
    public static ParseResult<ReleaseInfo> ParseReleasePayload(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParseResult<ReleaseInfo>(null, "The release payload was empty.");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new ParseResult<ReleaseInfo>(null, "The release payload was not a JSON object.");
            }

            List<ReleaseAsset> assets = [];
            if (root.TryGetProperty("assets", out JsonElement assetsElement)
                && assetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assetsElement.EnumerateArray())
                {
                    if (asset.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    assets.Add(new ReleaseAsset(
                        ReadString(asset, "name"),
                        ReadString(asset, "browser_download_url"),
                        ReadInt64(asset, "size")));
                }
            }

            return new ParseResult<ReleaseInfo>(
                new ReleaseInfo(
                    ReadString(root, "tag_name"),
                    ReadString(root, "name"),
                    ReadString(root, "body"),
                    ReadTimestamp(root, "published_at"),
                    assets),
                null);
        }
        catch (JsonException exception)
        {
            return new ParseResult<ReleaseInfo>(null, $"The release payload is not valid JSON: {exception.Message}");
        }
    }

    private static ParseResult<T> Deserialize<T>(string? json, string documentName)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParseResult<T>(null, $"{documentName} was empty.");
        }

        try
        {
            T? value = JsonSerializer.Deserialize<T>(json, Options);
            return value is null
                ? new ParseResult<T>(null, $"{documentName} deserialised to null.")
                : new ParseResult<T>(value, null);
        }
        catch (JsonException exception)
        {
            return new ParseResult<T>(null, $"{documentName} is not valid JSON: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            return new ParseResult<T>(null, $"{documentName} could not be read: {exception.Message}");
        }
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long ReadInt64(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out long number)
            ? number
            : 0L;

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
        && value.TryGetDateTimeOffset(out DateTimeOffset timestamp)
            ? timestamp
            : null;
}
