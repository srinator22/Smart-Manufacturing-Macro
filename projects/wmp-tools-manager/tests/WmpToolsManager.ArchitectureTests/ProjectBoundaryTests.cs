// Purpose: Enforce the WMP Tools Manager project dependency direction and host-boundary rules
//   mechanically, and pin the identities that only show up at install time - the ClientId, the
//   activation manifest version, and the plugin catalog entry the release packer reads.
// Inputs: The csproj files under this project's src folder, its packaging template, its plugin.json,
//   Directory.Build.props, and the on-disk repository layout.
// Outputs: Failing tests when a reference crosses a boundary, interop leaks past the add-in host, a
//   second ribbon tab appears, the manifest version drifts from VersionPrefix, or plugin.json stops
//   describing what this project actually ships.
// Dependencies: System.Text.Json, System.Xml.Linq and the repository layout only; no product project
//   references, so a boundary violation cannot hide behind a compile error here.
// Assumptions: Tests run from the build output directory beneath the repository, so ancestors are
//   searched for InventorScripts.sln and for this project's plugin.json.
// Validation source: projects/wmp-tools-manager/docs/ARCHITECTURE.md "Modules and dependency
//   direction"; scripts/release/build-release.ps1 required plugin.json fields.

using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WmpToolsManager.ArchitectureTests;

public sealed class ProjectBoundaryTests
{
    private const string AddInProjectName = "WmpToolsManager.AddIn";
    private const string ManifestTemplateName = "Autodesk.WmpToolsManager.Inventor.addin.template";

    private static readonly string[] RequiredCatalogFields =
    [
        "id", "displayName", "description", "maturity", "addinProject",
        "assembly", "addinTemplate", "installDirectory", "ribbonPanel", "commands", "homepage",
    ];

    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["WmpToolsManager.Core"] = [],
            ["WmpToolsManager.Application"] = ["WmpToolsManager.Core"],
            ["WmpToolsManager.Infrastructure"] = ["WmpToolsManager.Application", "WmpToolsManager.Core"],
            ["WmpToolsManager.UI"] = ["WmpToolsManager.Application", "WmpToolsManager.Core"],
            [AddInProjectName] =
            [
                "WmpToolsManager.Application",
                "WmpToolsManager.Infrastructure",
                "WmpToolsManager.UI",
                "WmpRibbon",
            ],
        };

    [Fact]
    public void ProductionProjectReferencesFollowDocumentedDependencyDirection()
    {
        string projectRoot = FindProjectRoot();

        foreach ((string projectName, string[] expectedReferences) in AllowedProjectReferences)
        {
            string projectPath = Path.Combine(projectRoot, "src", projectName, $"{projectName}.csproj");
            XDocument document = XDocument.Load(projectPath);
            string[] actualReferences = document
                .Descendants("ProjectReference")
                .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")?.Value))
                .Where(reference => reference is not null)
                .Cast<string>()
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
        }
    }

    [Fact]
    public void EverySourceProjectIsCoveredByTheDependencyRule()
    {
        string sourceRoot = Path.Combine(FindProjectRoot(), "src");
        string[] actualProjects = Directory
            .EnumerateFiles(sourceRoot, "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedProjectReferences.Keys.Order(StringComparer.Ordinal), actualProjects);
    }

    [Fact]
    public void ProjectsDoNotReferenceAnotherProjectsImplementation()
    {
        string repositoryRoot = FindRepositoryRoot();
        IEnumerable<string> projectFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "projects"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(repositoryRoot, "shared"), "*.csproj", SearchOption.AllDirectories));

        foreach (string projectFile in projectFiles)
        {
            string? sourceOwner = GetProjectOwner(repositoryRoot, projectFile);
            XDocument document = XDocument.Load(projectFile);

            foreach (XElement reference in document.Descendants("ProjectReference"))
            {
                string include = reference.Attribute("Include")?.Value
                    ?? throw new InvalidDataException($"ProjectReference in {projectFile} has no Include attribute.");
                string referencedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, include));
                string? referencedOwner = GetProjectOwner(repositoryRoot, referencedPath);

                Assert.True(
                    referencedOwner is null || sourceOwner == referencedOwner,
                    $"{projectFile} crosses the project boundary into {referencedPath}.");
            }
        }
    }

    [Fact]
    public void InventorInteropUsageStaysInsideTheAddInHost()
    {
        // This product has no InventorAdapter: it reads no document and drives no modelling API. The
        // add-in host is the only place Inventor types may appear, and a leak inward would mean the
        // update workflow had started depending on the CAD session.
        string sourceRoot = Path.Combine(FindProjectRoot(), "src");
        string allowedDirectory = Path.Combine(sourceRoot, AddInProjectName);

        string[] violations = SourceFiles(sourceRoot)
            .Where(path => !path.StartsWith(allowedDirectory, StringComparison.OrdinalIgnoreCase))
            .Where(path => Regex.IsMatch(
                File.ReadAllText(path),
                @"(?:using\s+Inventor(?:\.|\s*;)|\bInventor\.)",
                RegexOptions.CultureInvariant))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Inventor interop usage is restricted to {AddInProjectName}. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void OnlyTheSharedRibbonComponentCreatesRibbonTabs()
    {
        // All three WMP add-ins share one "WMP Custom Tools" tab. A second RibbonTabs.Add anywhere in a
        // product's source would silently reintroduce a per-add-in tab; the only permitted call site is
        // shared/WmpRibbon, so this scans this project's src for the call.
        string sourceRoot = Path.Combine(FindProjectRoot(), "src");
        string[] violations = SourceFiles(sourceRoot)
            .Where(path => File.ReadAllText(path).Contains("RibbonTabs.Add", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Ribbon tabs are created only by shared/WmpRibbon. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void TheAddInAddsOnlyItsOwnPanelToTheSharedTab()
    {
        string server = File.ReadAllText(Path.Combine(FindProjectRoot(), "src", AddInProjectName, "StandardAddInServer.cs"));

        Assert.Contains("WmpRibbonTab.EnsureTab(", server, StringComparison.Ordinal);
        Assert.Contains("WmpRibbonTab.EnsurePanel(", server, StringComparison.Ordinal);
        Assert.Contains("WmpToolsManager.ToolsPanel", server, StringComparison.Ordinal);
    }

    [Fact]
    public void ActivationManifestVersionMatchesVersionPrefix()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument buildProperties = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        string versionPrefix = buildProperties.Descendants("VersionPrefix").Single().Value.Trim();

        string manifestPath = Path.Combine(
            FindProjectRoot(), "src", AddInProjectName, $"{AddInProjectName}.X.manifest");
        XDocument manifest = XDocument.Load(manifestPath);
        XElement identity = manifest.Descendants().Single(element => element.Name.LocalName == "assemblyIdentity");

        Assert.Equal($"{versionPrefix}.0", identity.Attribute("version")?.Value);
    }

    [Fact]
    public void AddInManifestTemplateAndServerShareOneClientId()
    {
        string projectRoot = FindProjectRoot();
        string template = File.ReadAllText(Path.Combine(projectRoot, "packaging", ManifestTemplateName));
        string server = File.ReadAllText(Path.Combine(projectRoot, "src", AddInProjectName, "StandardAddInServer.cs"));
        string activationManifest = File.ReadAllText(Path.Combine(
            projectRoot, "src", AddInProjectName, $"{AddInProjectName}.X.manifest"));

        Match templateId = Regex.Match(template, @"<ClassId>\{([0-9A-Fa-f-]{36})\}</ClassId>");
        Assert.True(templateId.Success, "The .addin template has no ClassId.");
        string guid = templateId.Groups[1].Value;

        Assert.Contains($"<ClientId>{{{guid}}}</ClientId>", template, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(guid, server, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(guid, activationManifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThisAddInsClientIdIsNotSharedWithAnotherProject()
    {
        string repositoryRoot = FindRepositoryRoot();
        string template = File.ReadAllText(Path.Combine(FindProjectRoot(), "packaging", ManifestTemplateName));
        string guid = Regex.Match(template, @"<ClassId>\{([0-9A-Fa-f-]{36})\}</ClassId>").Groups[1].Value;

        string[] otherTemplates = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "projects"), "*.addin.template", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), ManifestTemplateName, StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains(guid, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            otherTemplates.Length == 0,
            $"The ClientId {guid} is also claimed by: {string.Join(", ", otherTemplates)}");
    }

    [Fact]
    public void RibbonIconVariantsExistAndAreEmbeddedByTheirOwningHosts()
    {
        // The add-in picks a theme and scale-specific PNG at activation (WmpRibbon.RibbonIcons), so every
        // dark and light variant must exist and be embedded under the resource name the loader builds.
        string projectRoot = FindProjectRoot();
        string ribbonRoot = Path.Combine(projectRoot, "assets", "ribbon");
        foreach (string icon in new[] { "check-updates" })
        {
            foreach (string theme in new[] { "dark", "light" })
            {
                foreach (int size in new[] { 16, 20, 24, 32, 40, 48, 64 })
                {
                    string png = Path.Combine(ribbonRoot, $"{icon}-{theme}-{size}.png");
                    Assert.True(File.Exists(png), $"Missing rendered ribbon icon {png}; run dotnet run --project shared/WmpIconRenderer.");
                }
            }
        }

        XDocument uiProject = XDocument.Load(Path.Combine(projectRoot, "src", "WmpToolsManager.UI", "WmpToolsManager.UI.csproj"));
        string[] uiResources = Includes(uiProject, "Resource");
        Assert.Contains(@"..\..\assets\ribbon\check-updates-light-32.png", uiResources);
        string window = File.ReadAllText(Path.Combine(projectRoot, "src", "WmpToolsManager.UI", "UpdateWindow.xaml"));
        Assert.Contains("Assets/check-updates-light-32.png", window, StringComparison.Ordinal);

        XDocument addInProject = XDocument.Load(Path.Combine(projectRoot, "src", AddInProjectName, $"{AddInProjectName}.csproj"));
        XElement ribbonResources = addInProject
            .Descendants("EmbeddedResource")
            .Single(element => element.Attribute("Include")?.Value == @"..\..\assets\ribbon\*.png");
        Assert.Equal("WmpToolsManager.AddIn.Ribbon.%(Filename)%(Extension)", ribbonResources.Attribute("LogicalName")?.Value);

        string server = File.ReadAllText(Path.Combine(projectRoot, "src", AddInProjectName, "StandardAddInServer.cs"));
        Assert.DoesNotContain("PictureDispConverter", server, StringComparison.Ordinal);
        foreach (string icon in new[] { "check-updates" })
        {
            Assert.Contains($"RibbonIcons.Load(inventorApplication, typeof(StandardAddInServer).Assembly, \"WmpToolsManager.AddIn\", \"{icon}\")", server, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheAddInEmbedsItsOwnCatalogEntrySoTheTooltipCannotDriftFromIt()
    {
        string projectRoot = FindProjectRoot();
        XDocument addInProject = XDocument.Load(Path.Combine(projectRoot, "src", AddInProjectName, $"{AddInProjectName}.csproj"));

        XElement embedded = addInProject
            .Descendants("EmbeddedResource")
            .Single(element => element.Attribute("Include")?.Value == @"..\..\plugin.json");

        Assert.Equal("WmpToolsManager.AddIn.plugin.json", embedded.Attribute("LogicalName")?.Value);
    }

    [Fact]
    public void PluginCatalogDescribesWhatThisProjectActuallyShips()
    {
        string projectRoot = FindProjectRoot();
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(projectRoot, "plugin.json")));
        JsonElement plugin = document.RootElement;

        foreach (string field in RequiredCatalogFields)
        {
            Assert.True(plugin.TryGetProperty(field, out _), $"plugin.json is missing the required field '{field}'.");
        }

        Assert.Equal("wmp-tools-manager", plugin.GetProperty("id").GetString());
        Assert.Equal("beta", plugin.GetProperty("maturity").GetString());
        Assert.Equal("WmpToolsManager.AddIn.dll", plugin.GetProperty("assembly").GetString());
        Assert.Equal("WmpToolsManager", plugin.GetProperty("installDirectory").GetString());
        Assert.Equal("WMP Tools", plugin.GetProperty("ribbonPanel").GetString());

        string addInProject = Relative(plugin.GetProperty("addinProject").GetString());
        string template = Relative(plugin.GetProperty("addinTemplate").GetString());
        Assert.True(File.Exists(Path.Combine(projectRoot, addInProject)), $"plugin.json points at a missing '{addInProject}'.");
        Assert.True(File.Exists(Path.Combine(projectRoot, template)), $"plugin.json points at a missing '{template}'.");
        Assert.Equal(ManifestTemplateName, Path.GetFileName(template));

        string command = Assert.Single(plugin.GetProperty("commands").EnumerateArray()).GetString()!;
        Assert.Equal("Check for updates", command);
        Assert.Contains(
            $"\"{command}\"",
            File.ReadAllText(Path.Combine(projectRoot, "src", AddInProjectName, "StandardAddInServer.cs")),
            StringComparison.Ordinal);
    }

    private static string Relative(string? value) => (value ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);

    private static string[] Includes(XDocument project, string elementName) => project
        .Descendants(elementName)
        .Select(element => element.Attribute("Include")?.Value)
        .Where(value => value is not null)
        .Cast<string>()
        .ToArray();

    private static IEnumerable<string> SourceFiles(string sourceRoot) => Directory
        .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("bin", StringComparer.OrdinalIgnoreCase))
        .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("obj", StringComparer.OrdinalIgnoreCase));

    private static string FindProjectRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null
            && !(File.Exists(Path.Combine(current.FullName, "plugin.json"))
                && Directory.Exists(Path.Combine(current.FullName, "src"))))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the WMP Tools Manager project root from the test output directory.");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InventorScripts.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not find InventorScripts.sln from the test output directory.");
    }

    private static string? GetProjectOwner(string repositoryRoot, string path)
    {
        string[] relativeParts = Path.GetRelativePath(repositoryRoot, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return relativeParts.Length >= 2 && relativeParts[0] == "projects"
            ? relativeParts[1]
            : null;
    }
}
