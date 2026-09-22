// Purpose: Allocate the next free part/assembly number for a project scope and report gaps and duplicates.
// Inputs: The parsed file names of every document under a project root, and the target project number.
// Outputs: Deterministic, strictly increasing allocations; gap lists; duplicate-number groups; other-project names.
// Dependencies: FileNamingManager.Core.NamingModels only.
// Assumptions: The caller passes the full recursive project scope, not just the open assembly's documents.
// Validation source: .work/TASK.md "Number allocation" paragraph, NumberAllocatorTests.

namespace FileNamingManager.Core;

public sealed record DuplicateNumberGroup(ItemNumber Number, IReadOnlyList<string> FileNames);

public sealed class NumberAllocator
{
    private readonly Dictionary<NumberSeries, int> initialMax = new();
    private readonly Dictionary<NumberSeries, int> cursor = new();
    private readonly Dictionary<NumberSeries, HashSet<int>> observedNumbers = new();
    private readonly Dictionary<NumberSeries, Dictionary<int, List<string>>> fileNamesByNumber = new();
    private readonly List<string> otherProjectFileNames = [];

    public NumberAllocator(IEnumerable<ParsedFileName> scope, ProjectNumber project)
    {
        ArgumentNullException.ThrowIfNull(scope);

        foreach (NumberSeries series in Enum.GetValues<NumberSeries>())
        {
            initialMax[series] = 0;
            cursor[series] = 0;
            observedNumbers[series] = [];
            fileNamesByNumber[series] = [];
        }

        foreach (ParsedFileName parsed in scope)
        {
            if (parsed.Token is not NamingToken token)
            {
                continue;
            }

            if (token.Project.Value != project.Value)
            {
                otherProjectFileNames.Add(parsed.OriginalFileName);
                continue;
            }

            NumberSeries series = token.Number.Series;
            int value = token.Number.Value;

            observedNumbers[series].Add(value);
            if (value > initialMax[series])
            {
                initialMax[series] = value;
            }

            Dictionary<int, List<string>> byNumber = fileNamesByNumber[series];
            if (!byNumber.TryGetValue(value, out List<string>? names))
            {
                names = [];
                byNumber[value] = names;
            }

            if (!names.Contains(parsed.OriginalFileName, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(parsed.OriginalFileName);
            }
        }

        foreach (NumberSeries series in Enum.GetValues<NumberSeries>())
        {
            cursor[series] = initialMax[series];
        }
    }

    public IReadOnlyList<string> OtherProjectFileNames => otherProjectFileNames;

    /// <summary>
    /// Returns the next free number in the series without reserving it.
    /// </summary>
    public int NextNumber(NumberSeries series) => cursor[series] + 1;

    /// <summary>
    /// Reserves and returns the next free number in the series. Deterministic and strictly increasing
    /// across successive calls to the same series on this allocator instance.
    /// </summary>
    public ItemNumber Allocate(NumberSeries series)
    {
        int next = NextNumber(series);
        cursor[series] = next;
        return new ItemNumber(series, next);
    }

    public IReadOnlyList<int> Gaps(NumberSeries series)
    {
        List<int> gaps = [];
        for (int candidate = 1; candidate < initialMax[series]; candidate++)
        {
            if (!observedNumbers[series].Contains(candidate))
            {
                gaps.Add(candidate);
            }
        }

        return gaps;
    }

    public IReadOnlyList<DuplicateNumberGroup> Duplicates(NumberSeries series)
    {
        List<DuplicateNumberGroup> duplicates = [];
        foreach ((int value, List<string> names) in fileNamesByNumber[series].OrderBy(pair => pair.Key))
        {
            if (names.Count > 1)
            {
                duplicates.Add(new DuplicateNumberGroup(new ItemNumber(series, value), names));
            }
        }

        return duplicates;
    }
}
