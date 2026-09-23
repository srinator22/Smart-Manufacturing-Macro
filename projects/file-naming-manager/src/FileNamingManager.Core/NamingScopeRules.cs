// Purpose: Name the folders that never belong to a project's naming scope, in one place.
// Inputs: A folder name (a single path segment, never a full path).
// Outputs: The excluded-folder list and a case-insensitive membership test.
// Dependencies: None beyond the BCL.
// Assumptions: Inventor keeps superseded versions in OldVersions, Vault keeps its trackers in _V, Content
//   Center and 3rd Party Hardware hold files owned by someone else, and _renamed-originals is this tool's
//   own archive. Both the filesystem enumeration and the per-row scope rule must agree on this list, so
//   neither may keep a copy of it.
// Validation source: docs/TEST_PLAN.md section 1 (scope), NamingScopeRulesTests.

namespace FileNamingManager.Core;

public static class NamingScopeRules
{
    /// <summary>
    /// The folder under the project root where this tool archives originals it renamed. It is excluded
    /// from the scope by the same list, so the archive can never feed numbers back into allocation.
    /// </summary>
    public const string RenamedOriginalsFolderName = "_renamed-originals";

    public static IReadOnlyList<string> ExcludedFolderNames { get; } =
        ["OldVersions", "_V", "3rd Party Hardware", "Content Center Files", RenamedOriginalsFolderName];

    public static bool IsExcludedFolderName(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        foreach (string excluded in ExcludedFolderNames)
        {
            if (string.Equals(excluded, folderName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
