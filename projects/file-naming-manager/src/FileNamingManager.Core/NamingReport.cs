// Purpose: Aggregate a parsed project scope into per-file rows plus project-level findings.
// Inputs: The parsed file names of every document under a project root, and the target project number.
// Outputs: An immutable report of rows and DuplicateNumber, SequenceGap, and DuplicateFileName findings.
// Dependencies: FileNamingManager.Core.NumberAllocator and NamingModels only.
// Assumptions: File names are unique identifiers within a project scope (Inventor's unique-filenames mode).
// Validation source: .work/TASK.md "Findings the analyzer must report" paragraph, NamingReportTests.

namespace FileNamingManager.Core;

public sealed class NamingReport
{
    private NamingReport(IReadOnlyList<ParsedFileName> rows, IReadOnlyList<NamingFinding> projectFindings)
    {
        Rows = rows;
        ProjectFindings = projectFindings;
    }

    public IReadOnlyList<ParsedFileName> Rows { get; }

    public IReadOnlyList<NamingFinding> ProjectFindings { get; }

    public static NamingReport Build(IEnumerable<ParsedFileName> scope, ProjectNumber project)
    {
        ArgumentNullException.ThrowIfNull(scope);

        List<ParsedFileName> rows = [.. scope];
        NumberAllocator allocator = new(rows, project);
        List<NamingFinding> findings = [];

        foreach (NumberSeries series in Enum.GetValues<NumberSeries>())
        {
            foreach (DuplicateNumberGroup group in allocator.Duplicates(series))
            {
                findings.Add(new NamingFinding(
                    FindingCode.DuplicateNumber,
                    $"Number '{project}-{group.Number}' is used by {group.FileNames.Count} files.",
                    group.FileNames));
            }

            IReadOnlyList<int> gaps = allocator.Gaps(series);
            if (gaps.Count > 0)
            {
                findings.Add(new NamingFinding(
                    FindingCode.SequenceGap,
                    $"{series} series is missing number(s): {string.Join(", ", gaps)}.",
                    []));
            }
        }

        foreach (IGrouping<string, ParsedFileName> group in rows
            .GroupBy(row => row.OriginalFileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            findings.Add(new NamingFinding(
                FindingCode.DuplicateFileName,
                $"File name '{group.Key}' appears {group.Count()} times in the project scope.",
                [.. group.Select(row => row.OriginalFileName)]));
        }

        return new NamingReport(rows, findings);
    }
}
