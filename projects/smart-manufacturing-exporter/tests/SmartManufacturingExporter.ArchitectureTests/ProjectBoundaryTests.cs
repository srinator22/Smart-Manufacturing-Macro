using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace SmartManufacturingExporter.ArchitectureTests;

public sealed class ProjectBoundaryTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedProjectReferences =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["SmartManufacturingExporter.Core"] = [],
            ["SmartManufacturingExporter.Application"] = ["SmartManufacturingExporter.Core"],
            ["SmartManufacturingExporter.Infrastructure"] =
                ["SmartManufacturingExporter.Application", "SmartManufacturingExporter.Core"],
            ["SmartManufacturingExporter.InventorAdapter"] =
                ["SmartManufacturingExporter.Application", "SmartManufacturingExporter.Core"],
            ["SmartManufacturingExporter.UI"] =
                ["SmartManufacturingExporter.Application", "SmartManufacturingExporter.Core"],
            ["SmartManufacturingExporter.AddIn"] =
            [
                "SmartManufacturingExporter.Application",
                "SmartManufacturingExporter.Infrastructure",
                "SmartManufacturingExporter.InventorAdapter",
                "SmartManufacturingExporter.UI",
            ],
        };

    [Fact]
    public void ProductionProjectReferencesFollowDocumentedDependencyDirection()
    {
        var projectRoot = FindProjectRoot();

        foreach (var (projectName, expectedReferences) in AllowedProjectReferences)
        {
            var projectPath = Path.Combine(projectRoot, "src", projectName, $"{projectName}.csproj");
            var document = XDocument.Load(projectPath);
            var actualReferences = document
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
        var repositoryRoot = FindRepositoryRoot();
        var projectFiles = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "projects"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "shared"),
                "*.csproj",
                SearchOption.AllDirectories));

        foreach (var projectFile in projectFiles)
        {
            var sourceOwner = GetProjectOwner(repositoryRoot, projectFile);
            var document = XDocument.Load(projectFile);

            foreach (var reference in document.Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value
                    ?? throw new InvalidDataException($"ProjectReference in {projectFile} has no Include attribute.");
                var referencedPath = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(projectFile)!, include));
                var referencedOwner = GetProjectOwner(repositoryRoot, referencedPath);

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
            Path.Combine(sourceRoot, "SmartManufacturingExporter.AddIn"),
            Path.Combine(sourceRoot, "SmartManufacturingExporter.InventorAdapter"),
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
    public void ActivationManifestVersionMatchesVersionPrefix()
    {
        string repositoryRoot = FindRepositoryRoot();
        XDocument buildProperties = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"));
        string versionPrefix = buildProperties
            .Descendants("VersionPrefix")
            .Single()
            .Value
            .Trim();

        string manifestPath = Path.Combine(
            FindProjectRoot(),
            "src",
            "SmartManufacturingExporter.AddIn",
            "SmartManufacturingExporter.AddIn.X.manifest");
        XDocument manifest = XDocument.Load(manifestPath);
        XElement identity = manifest
            .Descendants()
            .Single(element => element.Name.LocalName == "assemblyIdentity");
        string? manifestVersion = identity.Attribute("version")?.Value;

        Assert.Equal($"{versionPrefix}.0", manifestVersion);
    }

    private static string FindProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null
            && !File.Exists(Path.Combine(current.FullName, "docs", "PRODUCT_SPEC.md")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException(
                "Could not find the Smart Manufacturing Exporter project root from the test output directory.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null && !File.Exists(Path.Combine(current.FullName, "InventorScripts.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not find InventorScripts.sln from the test output directory.");
    }

    private static string? GetProjectOwner(string repositoryRoot, string path)
    {
        var relativeParts = Path.GetRelativePath(repositoryRoot, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return relativeParts.Length >= 2 && relativeParts[0] == "projects"
            ? relativeParts[1]
            : null;
    }
}
