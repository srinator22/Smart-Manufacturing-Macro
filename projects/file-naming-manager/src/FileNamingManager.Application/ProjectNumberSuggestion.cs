// Purpose: Suggest a project number to prefill in the naming UI, with the source of the suggestion.
// Inputs: The active assembly snapshot and the file names present in the project scope.
// Outputs: A suggested ProjectNumber and where it came from, or neither when nothing can be inferred.
// Dependencies: FileNamingManager.Core (FileNameParser) only.
// Assumptions: The root file name is the strongest signal; scope frequency is next; folder name is last resort.
// Validation source: .work/TASK.md acceptance criteria "Project number is required, prefilled from root
//   filename, then sibling numbered files, then a P124-style folder"; ProjectNumberSuggestionTests.

using System.Text.RegularExpressions;
using FileNamingManager.Core;

namespace FileNamingManager.Application;

public enum ProjectNumberSuggestionSource
{
    RootFileName,
    MostCommonInScope,
    FolderName,
}

public sealed partial record ProjectNumberSuggestion(ProjectNumber? Number, ProjectNumberSuggestionSource? Source)
{
    public static ProjectNumberSuggestion Suggest(ActiveAssemblySnapshot snapshot, IEnumerable<string> scopeFileNames)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(scopeFileNames);

        ParsedFileName rootParsed = FileNameParser.Parse(Path.GetFileName(snapshot.RootFullPath));
        if (rootParsed.Token is NamingToken rootToken)
        {
            return new ProjectNumberSuggestion(rootToken.Project, ProjectNumberSuggestionSource.RootFileName);
        }

        Dictionary<int, int> countsByProject = [];
        foreach (string fileName in scopeFileNames)
        {
            ParsedFileName parsed = FileNameParser.Parse(fileName);
            if (parsed.Token is NamingToken token)
            {
                countsByProject[token.Project.Value] = countsByProject.GetValueOrDefault(token.Project.Value) + 1;
            }
        }

        if (countsByProject.Count > 0)
        {
            int mostCommon = countsByProject
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .First()
                .Key;
            return new ProjectNumberSuggestion(new ProjectNumber(mostCommon), ProjectNumberSuggestionSource.MostCommonInScope);
        }

        for (string? current = Path.GetDirectoryName(snapshot.RootFullPath);
             !string.IsNullOrEmpty(current);
             current = Path.GetDirectoryName(current))
        {
            Match match = FolderProjectNumberRegex().Match(Path.GetFileName(current));
            if (match.Success && ProjectNumber.TryParse(match.Groups["num"].Value, out ProjectNumber folderNumber))
            {
                return new ProjectNumberSuggestion(folderNumber, ProjectNumberSuggestionSource.FolderName);
            }
        }

        return new ProjectNumberSuggestion(null, null);
    }

    [GeneratedRegex(@"^P(?<num>\d{3})\b")]
    private static partial Regex FolderProjectNumberRegex();
}
