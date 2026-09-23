// Purpose: Allocate the next free part/assembly number for a project scope and report gaps and duplicates.
// Inputs: The parsed file names of every document under a project root, and the target project number.
// Outputs: Deterministic, strictly increasing allocations; gap lists; duplicate-number groups; other-project names.
// Dependencies: FileNamingManager.Core.NamingModels only.
// Assumptions: The caller passes the full recursive project scope, not just the open assembly's documents.
//   Every .ipt/.iam/.idw/.dwg/.ipn token in the target project raises the series maximum and fills gaps -
//   a lone drawing whose model is gone or out of scope still occupies its number in the real folder tree,
//   so the maximum and gap list must see it or a later model would be handed that same number again. Only
//   Part and Assembly documents can OWN a number for duplicate detection: 124-0001 Foo.idw is the drawing
//   OF 124-0001 Foo.ipt, not a second claim on 0001, so drawings and presentations never populate the
//   duplicate map even though they count toward the maximum.
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
            // Assign the token on its own statement rather than through a pattern variable. A pattern
            // variable is only definitely assigned via the branch that introduces it, so mutating that
            // branch produces code that does not compile (CS0165); the mutation runner then falls back to
            // its safe mode for the whole project and this file silently gets no mutation coverage.
            // (A comment line must not begin with the runner's own name - it reads those as directives.)
            if (parsed.Token is null)
            {
                continue;
            }

            NamingToken token = parsed.Token.Value;

            if (token.Project.Value != project.Value)
            {
                otherProjectFileNames.Add(parsed.OriginalFileName);
                continue;
            }

            NumberSeries series = token.Number.Series;
            int value = token.Number.Value;

            // Every numbered token in the target project - part, assembly, drawing or presentation -
            // raises the series maximum and fills the gap it sits on. A lone drawing at 124-0080 with no
            // model in scope still occupies 0080 in the real folder tree; leaving it out of the maximum
            // let a later model claim 0080 again, a duplicate the tool itself created and never reported.
            observedNumbers[series].Add(value);
            if (value > initialMax[series])
            {
                initialMax[series] = value;
            }

            if (!OwnsItsNumber(parsed.Kind))
            {
                // Duplicate detection is ownership-only: 124-0001 Foo.idw is the drawing OF
                // 124-0001 Foo.ipt, not a second claim on 0001, so it never enters the duplicate map.
                continue;
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
    /// Returns the next free number in the series without reserving it. The value can exceed the series
    /// maximum when the series is exhausted; ask <see cref="IsExhausted"/> before using it.
    /// </summary>
    public int NextNumber(NumberSeries series) => cursor[series] + 1;

    /// <summary>
    /// True when every number in the series is taken, so the next allocation would fall outside the
    /// series range. A project really can reach 9999 parts or A999 assemblies, and the caller asks for a
    /// number before it can know one exists, so exhaustion is a reported outcome, never an exception.
    /// </summary>
    public bool IsExhausted(NumberSeries series) => NextNumber(series) > MaxValueOf(series);

    /// <summary>
    /// Reserves and returns the next free number in the series, or null when the series is exhausted.
    /// Deterministic and strictly increasing across successive calls to the same series on this
    /// allocator instance. The nullable return is the contract: a caller cannot ignore exhaustion.
    /// </summary>
    public ItemNumber? Allocate(NumberSeries series)
    {
        if (IsExhausted(series))
        {
            return null;
        }

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

    /// <summary>
    /// True for the document kinds that can OWN a number for duplicate detection. Drawings and
    /// presentations still raise the series maximum and fill gaps like any numbered file (they are real
    /// tokens in the project scope), but they take the number of the model they document, so they are
    /// never counted as a duplicate claim on it.
    /// </summary>
    private static bool OwnsItsNumber(DocumentKind kind) => kind is DocumentKind.Part or DocumentKind.Assembly;

    private static int MaxValueOf(NumberSeries series) =>
        series == NumberSeries.Part ? ItemNumber.PartMaxValue : ItemNumber.AssemblyMaxValue;
}
