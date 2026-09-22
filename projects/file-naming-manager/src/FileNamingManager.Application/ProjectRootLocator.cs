// Purpose: Locate the WMP project root folder that bounds a project's numbering scope.
// Inputs: The full path of the root assembly the tool was run from.
// Outputs: The nearest ancestor folder whose name looks like a project folder, else the assembly's own folder.
// Dependencies: .NET base types only.
// Assumptions: Folder names carrying the project number start with the number itself or a "P" prefix.
// Validation source: .work/TASK.md "Number allocation" paragraph; ProjectRootLocatorTests.

using System.Text.RegularExpressions;

namespace FileNamingManager.Application;

public static partial class ProjectRootLocator
{
    public static string Locate(string rootAssemblyFullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootAssemblyFullPath);

        string? directory = Path.GetDirectoryName(rootAssemblyFullPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException(
                $"'{rootAssemblyFullPath}' has no containing directory to locate a project root from.",
                nameof(rootAssemblyFullPath));
        }

        for (string? current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            string name = Path.GetFileName(current);
            if (ProjectFolderRegex().IsMatch(name))
            {
                return current;
            }
        }

        return directory;
    }

    [GeneratedRegex(@"^P?\d{3}(\b|[^\d])")]
    private static partial Regex ProjectFolderRegex();
}
