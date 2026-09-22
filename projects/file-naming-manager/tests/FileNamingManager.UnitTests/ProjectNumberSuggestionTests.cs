// Purpose: Verify ProjectNumberSuggestion's precedence: root file name, then scope frequency, then folder.
// Inputs: Synthetic ActiveAssemblySnapshot root paths and scope file name lists.
// Outputs: Assertions on the suggested ProjectNumber and its source.
// Dependencies: FileNamingManager.Application, FileNamingManager.Core.
// Assumptions: None.
// Validation source: .work/TASK.md acceptance criteria "Project number is required, prefilled from root
//   filename, then sibling numbered files, then a P124-style folder".

using FileNamingManager.Application;

namespace FileNamingManager.UnitTests;

public class ProjectNumberSuggestionTests
{
    private static ActiveAssemblySnapshot Snapshot(string rootFullPath) => new(rootFullPath, false, false, []);

    [Fact]
    public void SuggestPrefersRootFileNameToken()
    {
        ActiveAssemblySnapshot snapshot = Snapshot(@"C:\WMP\P124 GRM\124-A001 Full Double Stack Mechanism (main assembly).iam");

        ProjectNumberSuggestion suggestion = ProjectNumberSuggestion.Suggest(snapshot, ["101-0001 Other.ipt"]);

        Assert.Equal(124, suggestion.Number!.Value.Value);
        Assert.Equal(ProjectNumberSuggestionSource.RootFileName, suggestion.Source);
    }

    [Fact]
    public void SuggestFallsBackToMostCommonProjectInScope()
    {
        ActiveAssemblySnapshot snapshot = Snapshot(@"C:\WMP\P124 GRM\Assembly1.iam");

        ProjectNumberSuggestion suggestion = ProjectNumberSuggestion.Suggest(
            snapshot,
            ["124-0001 A.ipt", "124-0002 B.ipt", "101-0001 C.ipt"]);

        Assert.Equal(124, suggestion.Number!.Value.Value);
        Assert.Equal(ProjectNumberSuggestionSource.MostCommonInScope, suggestion.Source);
    }

    [Fact]
    public void SuggestFallsBackToPStyleFolderName()
    {
        ActiveAssemblySnapshot snapshot = Snapshot(@"C:\WMP\Adult Release\P124 GRM\Assembly1.iam");

        ProjectNumberSuggestion suggestion = ProjectNumberSuggestion.Suggest(snapshot, ["Assembly1.iam"]);

        Assert.Equal(124, suggestion.Number!.Value.Value);
        Assert.Equal(ProjectNumberSuggestionSource.FolderName, suggestion.Source);
    }

    [Fact]
    public void SuggestReturnsNullWhenNothingCanBeInferred()
    {
        ActiveAssemblySnapshot snapshot = Snapshot(@"C:\WMP\Scratch\Assembly1.iam");

        ProjectNumberSuggestion suggestion = ProjectNumberSuggestion.Suggest(snapshot, ["Assembly1.iam"]);

        Assert.Null(suggestion.Number);
        Assert.Null(suggestion.Source);
    }

    /// <summary>
    /// Two projects equally represented in the folder is a genuine ambiguity, so the tie breaks on the
    /// lowest number rather than on enumeration order: the operator must see the same prefill every run.
    /// </summary>
    [Fact]
    public void SuggestBreaksAScopeFrequencyTieOnTheLowestProjectNumber()
    {
        ActiveAssemblySnapshot snapshot = Snapshot(@"C:\WMP\Scratch\Assembly1.iam");

        ProjectNumberSuggestion suggestion = ProjectNumberSuggestion.Suggest(
            snapshot,
            ["155-0001 B.ipt", "124-0001 A.ipt"]);

        Assert.Equal(124, suggestion.Number!.Value.Value);
        Assert.Equal(ProjectNumberSuggestionSource.MostCommonInScope, suggestion.Source);
    }

    [Fact]
    public void SuggestRejectsNullArguments()
    {
        ActiveAssemblySnapshot snapshot = Snapshot(@"C:\WMP\Scratch\Assembly1.iam");

        Assert.Throws<ArgumentNullException>(() => ProjectNumberSuggestion.Suggest(null!, ["Assembly1.iam"]));
        Assert.Throws<ArgumentNullException>(() => ProjectNumberSuggestion.Suggest(snapshot, null!));
    }
}
