// Purpose: Enforce the File Naming Manager project dependency direction and host-boundary rules mechanically.
// Inputs: The csproj files under this project's src folder, the shared ribbon project, and Directory.Build.props.
// Outputs: Failing tests when a reference crosses a boundary, interop leaks past the host adapters, or the
//   activation manifest version drifts from VersionPrefix.
// Dependencies: System.Xml.Linq and the on-disk repository layout only; no product project references.
// Assumptions: Tests run from the build output directory beneath the repository, so ancestors are searched
//   for InventorScripts.sln and this project's docs/ARCHITECTURE.md.
// Validation source: projects/file-naming-manager/docs/ARCHITECTURE.md "Modules and dependency direction".

using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FileNamingManager.ArchitectureTests;

public sealed class ProjectBoundaryTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["FileNamingManager.Core"] = [],
            ["FileNamingManager.Application"] = ["FileNamingManager.Core"],
            ["FileNamingManager.Infrastructure"] = ["FileNamingManager.Application", "FileNamingManager.Core"],
            ["FileNamingManager.InventorAdapter"] = ["FileNamingManager.Application", "FileNamingManager.Core"],
            ["FileNamingManager.UI"] = ["FileNamingManager.Application", "FileNamingManager.Core"],
            ["FileNamingManager.AddIn"] =
            [
                "FileNamingManager.Application",
                "FileNamingManager.Infrastructure",
                "FileNamingManager.InventorAdapter",
                "FileNamingManager.UI",
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
    public void InventorInteropUsageStaysInsideHostAdapters()
    {
        string sourceRoot = Path.Combine(FindProjectRoot(), "src");
        string[] allowedDirectories =
        [
            Path.Combine(sourceRoot, "FileNamingManager.AddIn"),
            Path.Combine(sourceRoot, "FileNamingManager.InventorAdapter"),
        ];

        string[] violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("bin", StringComparer.OrdinalIgnoreCase))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("obj", StringComparer.OrdinalIgnoreCase))
            .Where(path => !allowedDirectories.Any(directory => path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)))
            .Where(path => Regex.IsMatch(
                File.ReadAllText(path),
                @"(?:using\s+Inventor(?:\.|\s*;)|\bInventor\.)",
                RegexOptions.CultureInvariant))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Inventor interop usage is restricted to AddIn and InventorAdapter. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void OnlyTheSharedRibbonComponentCreatesRibbonTabs()
    {
        // Both WMP add-ins share one "WMP Custom Tools" tab. A second RibbonTabs.Add anywhere in a
        // product's source would silently reintroduce a per-add-in tab; the only permitted call site is
        // shared/WmpRibbon, so this scans this project's src for the call.
        string sourceRoot = Path.Combine(FindProjectRoot(), "src");
        string[] violations = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("bin", StringComparer.OrdinalIgnoreCase))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("obj", StringComparer.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("RibbonTabs.Add", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(sourceRoot, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Ribbon tabs are created only by shared/WmpRibbon. Violations: {string.Join(", ", violations)}");
    }

    [Fact]
    public void VaultSdkIsNotReferencedInVersionOne()
    {
        // The Vault-managed rename path is designed but deliberately not implemented in v1
        // (docs/VAULT_RENAME_DESIGN.md). A reference to the Vault client SDK appearing anywhere in this
        // project is therefore a scope change that must arrive with its own task, not by accident.
        string projectRoot = FindProjectRoot();
        string[] offendingProjects = Directory
            .EnumerateFiles(Path.Combine(projectRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("Autodesk.Connectivity", StringComparison.OrdinalIgnoreCase)
                || File.ReadAllText(path).Contains("Vault Client", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(projectRoot, path))
            .ToArray();

        Assert.True(offendingProjects.Length == 0, $"Vault SDK referenced by: {string.Join(", ", offendingProjects)}");
    }

    [Fact]
    public void ActivationManifestVersionMatchesVersionPrefix()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument buildProperties = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        string versionPrefix = buildProperties.Descendants("VersionPrefix").Single().Value.Trim();

        string manifestPath = Path.Combine(FindProjectRoot(), "src", "FileNamingManager.AddIn", "FileNamingManager.AddIn.X.manifest");
        XDocument manifest = XDocument.Load(manifestPath);
        XElement identity = manifest.Descendants().Single(element => element.Name.LocalName == "assemblyIdentity");

        Assert.Equal($"{versionPrefix}.0", identity.Attribute("version")?.Value);
    }

    [Fact]
    public void AddInManifestTemplateAndServerShareOneClientId()
    {
        string projectRoot = FindProjectRoot();
        string template = File.ReadAllText(Path.Combine(
            projectRoot,
            "packaging",
            "Autodesk.FileNamingManager.Inventor.addin.template"));
        string server = File.ReadAllText(Path.Combine(projectRoot, "src", "FileNamingManager.AddIn", "StandardAddInServer.cs"));
        string activationManifest = File.ReadAllText(Path.Combine(
            projectRoot,
            "src",
            "FileNamingManager.AddIn",
            "FileNamingManager.AddIn.X.manifest"));

        Match templateId = Regex.Match(template, @"<ClassId>\{([0-9A-Fa-f-]{36})\}</ClassId>");
        Assert.True(templateId.Success, "The .addin template has no ClassId.");
        string guid = templateId.Groups[1].Value;

        Assert.Contains($"<ClientId>{{{guid}}}</ClientId>", template, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(guid, server, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(guid, activationManifest, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VisualAssetsExistAndAreEmbeddedByTheirOwningHosts()
    {
        string projectRoot = FindProjectRoot();
        string assetsRoot = Path.Combine(projectRoot, "assets");

        Assert.True(File.Exists(Path.Combine(assetsRoot, "file-naming-16.png")));
        Assert.True(File.Exists(Path.Combine(assetsRoot, "file-naming-32.png")));

        XDocument uiProject = XDocument.Load(Path.Combine(projectRoot, "src", "FileNamingManager.UI", "FileNamingManager.UI.csproj"));
        string[] uiResources = uiProject
            .Descendants("Resource")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        Assert.Contains(@"..\..\assets\file-naming-32.png", uiResources);

        XDocument addInProject = XDocument.Load(Path.Combine(projectRoot, "src", "FileNamingManager.AddIn", "FileNamingManager.AddIn.csproj"));
        string[] addInResources = addInProject
            .Descendants("EmbeddedResource")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        Assert.Contains(@"..\..\assets\file-naming-16.png", addInResources);
        Assert.Contains(@"..\..\assets\file-naming-32.png", addInResources);
    }

    private static string FindProjectRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null && !File.Exists(Path.Combine(current.FullName, "docs", "VAULT_RENAME_DESIGN.md")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not find the File Naming Manager project root from the test output directory.");
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
