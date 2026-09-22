// Purpose: Verify ProjectRootLocator walks up to the nearest project-numbered ancestor folder.
// Inputs: Synthetic Windows-style paths, including the real P124 GRM folder shape from TASK.md.
// Outputs: Assertions on the located project root path.
// Dependencies: FileNamingManager.Application.
// Assumptions: Paths are Windows-style; tests run on Windows (the product's only supported platform).
// Validation source: .work/TASK.md "Number allocation" paragraph, ProjectRootLocator test example.

using FileNamingManager.Application;

namespace FileNamingManager.UnitTests;

public class ProjectRootLocatorTests
{
    [Fact]
    public void LocateWalksUpToPNumberedFolder()
    {
        string rootAssembly = @"C:\WMP\Adult Release\P124 GRM\GRM Double Stack\Assemblies\x.iam";

        string result = ProjectRootLocator.Locate(rootAssembly);

        Assert.Equal(@"C:\WMP\Adult Release\P124 GRM", result);
    }

    [Fact]
    public void LocateAcceptsBareDigitFolderWithoutPPrefix()
    {
        string rootAssembly = @"C:\WMP\Adult Release\124 GRM\Assemblies\x.iam";

        string result = ProjectRootLocator.Locate(rootAssembly);

        Assert.Equal(@"C:\WMP\Adult Release\124 GRM", result);
    }

    [Fact]
    public void LocateFallsBackToAssemblyFolderWhenNoAncestorMatches()
    {
        string rootAssembly = @"C:\WMP\Scratch\Loose Files\x.iam";

        string result = ProjectRootLocator.Locate(rootAssembly);

        Assert.Equal(@"C:\WMP\Scratch\Loose Files", result);
    }

    [Fact]
    public void LocateNullOrWhitespacePathThrows()
    {
        Assert.Throws<ArgumentNullException>(() => ProjectRootLocator.Locate(null!));
        Assert.Throws<ArgumentException>(() => ProjectRootLocator.Locate(""));
        Assert.Throws<ArgumentException>(() => ProjectRootLocator.Locate("   "));
    }

    [Fact]
    public void LocateAPathWithNoContainingDirectoryThrowsNamingThatPath()
    {
        ArgumentException thrown = Assert.Throws<ArgumentException>(() => ProjectRootLocator.Locate("Assembly1.iam"));

        Assert.StartsWith(
            "'Assembly1.iam' has no containing directory to locate a project root from.",
            thrown.Message,
            StringComparison.Ordinal);
    }
}
