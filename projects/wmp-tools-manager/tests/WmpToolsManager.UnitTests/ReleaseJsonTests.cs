// The samples mirror what the release scripts actually write: catalog.json from
// scripts/release/build-release.ps1 and installed.json from scripts/release/Install-WmpInventorTools.ps1,
// both via PowerShell ConvertTo-Json over an ordered hashtable.

using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests;

public sealed class ReleaseJsonTests
{
    private const string Catalog = """
        {
          "version": "0.6.0",
          "releasedUtc": "2026-09-23T10:11:12Z",
          "plugins": [
            {
              "id": "file-naming-manager",
              "displayName": "File Naming Manager",
              "description": "Analyze and apply the WMP part-numbering scheme.",
              "maturity": "beta",
              "installDirectory": "FileNamingManager",
              "assembly": "FileNamingManager.AddIn.dll",
              "addinTemplate": "templates/file-naming-manager.addin.template",
              "manifestName": "Autodesk.FileNamingManager.Inventor.addin",
              "ribbonPanel": "File Naming",
              "commands": [ "Analyze Naming", "Apply Naming" ]
            },
            {
              "id": "wmp-tools-manager",
              "displayName": "WMP Tools Manager",
              "description": "Check GitHub for a newer release.",
              "maturity": "beta",
              "installDirectory": "WmpToolsManager",
              "assembly": "WmpToolsManager.AddIn.dll",
              "addinTemplate": "templates/wmp-tools-manager.addin.template",
              "manifestName": "Autodesk.WmpToolsManager.Inventor.addin",
              "ribbonPanel": "WMP Tools",
              "commands": [ "Check for updates" ]
            }
          ]
        }
        """;

    private const string Installed = """
        {
          "version": "0.5.0",
          "installedUtc": "2026-09-20T08:00:00Z",
          "plugins": [
            {
              "id": "file-naming-manager",
              "maturity": "beta",
              "installDirectory": "FileNamingManager",
              "manifestName": "Autodesk.FileNamingManager.Inventor.addin"
            }
          ]
        }
        """;

    [Fact]
    public void ReadsAPublishedCatalog()
    {
        ParseResult<ReleaseCatalog> parse = ReleaseJson.ParseCatalog(Catalog);

        Assert.True(parse.IsSuccess);
        ReleaseCatalog catalog = parse.Value!;
        Assert.Equal("0.6.0", catalog.Version);
        Assert.Equal("2026-09-23T10:11:12Z", catalog.ReleasedUtc);
        Assert.Equal(2, catalog.Plugins.Count);
        Assert.Equal("WMP Tools Manager", catalog.Plugins[1].DisplayName);
        Assert.Equal("beta", catalog.Plugins[1].Maturity);
        Assert.Equal("WmpToolsManager", catalog.Plugins[1].InstallDirectory);
        Assert.Equal("WmpToolsManager.AddIn.dll", catalog.Plugins[1].Assembly);
        Assert.Equal("Autodesk.WmpToolsManager.Inventor.addin", catalog.Plugins[1].ManifestName);
        Assert.Equal("WMP Tools", catalog.Plugins[1].RibbonPanel);
        Assert.Equal("Check for updates", Assert.Single(catalog.Plugins[1].Commands));
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("{ not json", "not valid JSON")]
    [InlineData("""{ "version": "0.6.0", "plugins": [] }""", "no version or no plugins")]
    [InlineData("""{ "plugins": [ { "id": "a" } ] }""", "no version or no plugins")]
    public void RefusesAMalformedCatalog(string? json, string expectedFragment)
    {
        ParseResult<ReleaseCatalog> parse = ReleaseJson.ParseCatalog(json);

        Assert.False(parse.IsSuccess);
        Assert.Contains(expectedFragment, parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheInstalledStateTheInstallerWrote()
    {
        ParseResult<InstalledState> parse = ReleaseJson.ParseInstalledState(Installed);

        Assert.True(parse.IsSuccess);
        InstalledState state = parse.Value!;
        Assert.Equal("0.5.0", state.Version);
        Assert.Equal("2026-09-20T08:00:00Z", state.InstalledUtc);
        InstalledPlugin plugin = Assert.Single(state.Plugins);
        Assert.Equal("file-naming-manager", plugin.Id);
        Assert.Equal("beta", plugin.Maturity);
        Assert.Equal("FileNamingManager", plugin.InstallDirectory);
        Assert.Equal("Autodesk.FileNamingManager.Inventor.addin", plugin.ManifestName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("""{ "installedUtc": "2026-09-20T08:00:00Z" }""")]
    public void RefusesAnInstalledStateWithNoUsableVersion(string? json) =>
        Assert.False(ReleaseJson.ParseInstalledState(json).IsSuccess);

    [Fact]
    public void ReadsThisPluginsOwnManifest()
    {
        ParseResult<PluginManifest> parse = ReleaseJson.ParsePluginManifest(
            """{ "id": "wmp-tools-manager", "displayName": "WMP Tools Manager", "description": "d", "maturity": "beta" }""");

        Assert.True(parse.IsSuccess);
        Assert.Equal("wmp-tools-manager", parse.Value!.Id);
        Assert.Equal("WMP Tools Manager", parse.Value.DisplayName);
        Assert.Equal("beta", parse.Value.Maturity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{ "id": "wmp-tools-manager" }""")]
    public void RefusesAPluginManifestThatDeclaresNoMaturity(string? json) =>
        Assert.False(ReleaseJson.ParsePluginManifest(json).IsSuccess);

    [Fact]
    public void ReadsTheFieldsItUsesFromAReleasePayload()
    {
        ParseResult<ReleaseInfo> parse = ReleaseJson.ParseReleasePayload("""
            {
              "tag_name": "v0.6.0",
              "name": "WMP Inventor Tools 0.6.0",
              "body": "### Added\n- The update command.",
              "published_at": "2026-09-23T10:11:12Z",
              "assets": [
                { "name": "WmpInventorTools-0.6.0.zip", "browser_download_url": "https://example.invalid/a.zip", "size": 1234 },
                "not an object"
              ]
            }
            """);

        Assert.True(parse.IsSuccess);
        ReleaseInfo release = parse.Value!;
        Assert.Equal("v0.6.0", release.Tag);
        Assert.Equal("WMP Inventor Tools 0.6.0", release.Name);
        Assert.Contains("The update command.", release.Body, StringComparison.Ordinal);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 11, 12, TimeSpan.Zero), release.PublishedUtc);
        ReleaseAsset asset = Assert.Single(release.Assets);
        Assert.Equal("WmpInventorTools-0.6.0.zip", asset.Name);
        Assert.Equal("https://example.invalid/a.zip", asset.DownloadUrl);
        Assert.Equal(1234L, asset.SizeInBytes);
    }

    [Fact]
    public void DegradesToEmptyFieldsWhenAReleasePayloadOmitsThem()
    {
        ParseResult<ReleaseInfo> parse = ReleaseJson.ParseReleasePayload("{}");

        Assert.True(parse.IsSuccess);
        Assert.Equal(string.Empty, parse.Value!.Body);
        Assert.Equal(string.Empty, parse.Value.Tag);
        Assert.Null(parse.Value.PublishedUtc);
        Assert.Empty(parse.Value.Assets);
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("[]", "not a JSON object")]
    [InlineData("{ broken", "not valid JSON")]
    public void RefusesAnUnusableReleasePayload(string? json, string expectedFragment)
    {
        ParseResult<ReleaseInfo> parse = ReleaseJson.ParseReleasePayload(json);

        Assert.False(parse.IsSuccess);
        Assert.Contains(expectedFragment, parse.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresAnAssetsFieldThatIsNotAnArray()
    {
        ParseResult<ReleaseInfo> parse = ReleaseJson.ParseReleasePayload("""{ "assets": 7 }""");

        Assert.True(parse.IsSuccess);
        Assert.Empty(parse.Value!.Assets);
    }

    [Theory]
    [InlineData("catalog.json")]
    [InlineData("installed.json")]
    [InlineData("plugin.json")]
    public void NamesTheDocumentItCouldNotRead(string documentName)
    {
        string? error = documentName switch
        {
            "catalog.json" => ReleaseJson.ParseCatalog("{ broken").ErrorMessage,
            "installed.json" => ReleaseJson.ParseInstalledState("{ broken").ErrorMessage,
            _ => ReleaseJson.ParsePluginManifest("{ broken").ErrorMessage,
        };

        Assert.StartsWith(documentName, error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("catalog.json")]
    [InlineData("installed.json")]
    [InlineData("plugin.json")]
    public void ReportsADocumentThatDeserialisesToNull(string documentName)
    {
        string? error = documentName switch
        {
            "catalog.json" => ReleaseJson.ParseCatalog("null").ErrorMessage,
            "installed.json" => ReleaseJson.ParseInstalledState("null").ErrorMessage,
            _ => ReleaseJson.ParsePluginManifest("null").ErrorMessage,
        };

        Assert.Equal($"{documentName} deserialised to null.", error);
    }

    [Fact]
    public void MapsCamelCaseKeysWithoutPerPropertyAttributes()
    {
        ParseResult<InstalledState> parse = ReleaseJson.ParseInstalledState(
            """{ "version": "0.6.0", "installedUtc": "2026-09-23T10:00:00Z", "plugins": [ { "id": "a", "installDirectory": "A", "manifestName": "a.addin", "maturity": "beta" } ] }""");

        Assert.True(parse.IsSuccess);
        Assert.Equal("2026-09-23T10:00:00Z", parse.Value!.InstalledUtc);
        InstalledPlugin plugin = Assert.Single(parse.Value.Plugins);
        Assert.Equal("A", plugin.InstallDirectory);
        Assert.Equal("a.addin", plugin.ManifestName);
    }

    [Fact]
    public void LeavesUnstatedCatalogFieldsEmptyRatherThanNull()
    {
        ParseResult<ReleaseCatalog> parse = ReleaseJson.ParseCatalog(
            """{ "version": "0.6.0", "plugins": [ { "id": "a" } ] }""");

        Assert.True(parse.IsSuccess);
        CatalogPlugin plugin = Assert.Single(parse.Value!.Plugins);
        Assert.Equal(string.Empty, plugin.DisplayName);
        Assert.Equal(string.Empty, plugin.Description);
        Assert.Equal(string.Empty, plugin.Maturity);
        Assert.Equal(string.Empty, plugin.InstallDirectory);
        Assert.Equal(string.Empty, plugin.Assembly);
        Assert.Equal(string.Empty, plugin.ManifestName);
        Assert.Equal(string.Empty, plugin.RibbonPanel);
        Assert.Empty(plugin.Commands);
        Assert.Null(parse.Value.ReleasedUtc);
    }

    [Fact]
    public void IgnoresReleasePayloadFieldsOfTheWrongJsonKind()
    {
        ParseResult<ReleaseInfo> parse = ReleaseJson.ParseReleasePayload("""
            {
              "tag_name": 6,
              "name": null,
              "body": true,
              "published_at": "not a date",
              "assets": [ { "name": [], "browser_download_url": 7, "size": "big" } ]
            }
            """);

        Assert.True(parse.IsSuccess);
        ReleaseInfo release = parse.Value!;
        Assert.Equal(string.Empty, release.Tag);
        Assert.Equal(string.Empty, release.Name);
        Assert.Equal(string.Empty, release.Body);
        Assert.Null(release.PublishedUtc);
        ReleaseAsset asset = Assert.Single(release.Assets);
        Assert.Equal(string.Empty, asset.Name);
        Assert.Equal(string.Empty, asset.DownloadUrl);
        Assert.Equal(0L, asset.SizeInBytes);
    }
}
