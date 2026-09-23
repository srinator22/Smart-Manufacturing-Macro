// Purpose: Verify NumberAllocator's gap detection, duplicate detection, other-project exclusion, and
//   deterministic strictly-increasing allocation.
// Inputs: Synthetic parsed-file-name scopes built directly from ParsedFileName (no CAD).
// Outputs: Assertions on NextNumber, Allocate, Gaps, Duplicates, and OtherProjectFileNames.
// Dependencies: FileNamingManager.Core.
// Assumptions: None.
// Validation source: .work/TASK.md "Number allocation" paragraph and the real P124 gap/duplicate cases.

using FileNamingManager.Core;

namespace FileNamingManager.UnitTests;

public class NumberAllocatorTests
{
    private static readonly ProjectNumber Project124 = new(124);

    private static ParsedFileName Part(int project, int number) =>
        new(
            $"{project:D3}-{number:D4} Part.ipt",
            DocumentKind.Part,
            NameState.Canonical,
            new NamingToken(new ProjectNumber(project), new ItemNumber(NumberSeries.Part, number)),
            "Part",
            null,
            []);

    private static ParsedFileName Assembly(int project, int number, string fileName) =>
        new(
            fileName,
            DocumentKind.Assembly,
            NameState.Canonical,
            new NamingToken(new ProjectNumber(project), new ItemNumber(NumberSeries.Assembly, number)),
            "Assembly",
            AssemblyRole.Sub,
            []);

    [Fact]
    public void NullScopeIsRefusedByName()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => new NumberAllocator(null!, Project124));
        Assert.Equal("scope", exception.ParamName);
    }

    [Fact]
    public void GapsAndNextNumberReportRealP124GapAndNext()
    {
        List<ParsedFileName> scope = [];
        for (int i = 1; i <= 20; i++)
        {
            scope.Add(Part(124, i));
        }

        for (int i = 22; i <= 76; i++)
        {
            scope.Add(Part(124, i));
        }

        NumberAllocator allocator = new(scope, Project124);

        Assert.Equal([21], allocator.Gaps(NumberSeries.Part));
        Assert.Equal(77, allocator.NextNumber(NumberSeries.Part));
    }

    [Fact]
    public void DuplicatesReportsTwoDistinctFileNamesSharingANumber()
    {
        List<ParsedFileName> scope =
        [
            Assembly(124, 4, "124-A004 Electronics Box Triple Wake_Up (sub-assembly).iam"),
            Assembly(124, 4, "124-A004 Fridge (sub-assembly).iam"),
        ];

        NumberAllocator allocator = new(scope, Project124);

        IReadOnlyList<DuplicateNumberGroup> duplicates = allocator.Duplicates(NumberSeries.Assembly);

        DuplicateNumberGroup group = Assert.Single(duplicates);
        Assert.Equal(4, group.Number.Value);
        Assert.Equal(2, group.FileNames.Count);
        Assert.Contains("124-A004 Electronics Box Triple Wake_Up (sub-assembly).iam", group.FileNames);
        Assert.Contains("124-A004 Fridge (sub-assembly).iam", group.FileNames);
    }

    [Fact]
    public void DuplicatesSameFileNameTwiceIsNotCountedAsDuplicateNumber()
    {
        List<ParsedFileName> scope =
        [
            Part(124, 5),
            Part(124, 5),
        ];

        NumberAllocator allocator = new(scope, Project124);

        Assert.Empty(allocator.Duplicates(NumberSeries.Part));
    }

    [Fact]
    public void OtherProjectFileNamesExcludedFromAllocationAndListedSeparately()
    {
        List<ParsedFileName> scope =
        [
            Part(124, 5),
            Part(101, 9999),
        ];

        NumberAllocator allocator = new(scope, Project124);

        Assert.Equal(6, allocator.NextNumber(NumberSeries.Part));
        Assert.Contains("101-9999 Part.ipt", allocator.OtherProjectFileNames);
    }

    [Fact]
    public void AllocateIsDeterministicAndStrictlyIncreasingAcrossCalls()
    {
        NumberAllocator allocator = new([Part(124, 5)], Project124);

        ItemNumber? first = allocator.Allocate(NumberSeries.Part);
        ItemNumber? second = allocator.Allocate(NumberSeries.Part);
        ItemNumber? third = allocator.Allocate(NumberSeries.Part);

        Assert.Equal(6, first!.Value.Value);
        Assert.Equal(7, second!.Value.Value);
        Assert.Equal(8, third!.Value.Value);
    }

    /// <summary>
    /// A project that has used every number in a series is a real, reachable state, and the allocator is
    /// asked for a number before anything knows whether one exists. Allocating past the series maximum
    /// used to throw out of ItemNumber's constructor and escape Analyze, so the window never opened.
    /// </summary>
    [Fact]
    public void AnExhaustedPartSeriesAllocatesNothingAndReportsItselfExhausted()
    {
        NumberAllocator allocator = new([FileNameParser.Parse("124-9999 Last.ipt")], Project124);

        Assert.True(allocator.IsExhausted(NumberSeries.Part));
        Assert.False(allocator.IsExhausted(NumberSeries.Assembly));
        Assert.Null(allocator.Allocate(NumberSeries.Part));
    }

    [Fact]
    public void AnExhaustedAssemblySeriesAllocatesNothingAndReportsItselfExhausted()
    {
        NumberAllocator allocator = new([FileNameParser.Parse("124-A999 Last (sub-assembly).iam")], Project124);

        Assert.True(allocator.IsExhausted(NumberSeries.Assembly));
        Assert.False(allocator.IsExhausted(NumberSeries.Part));
        Assert.Null(allocator.Allocate(NumberSeries.Assembly));
    }

    /// <summary>
    /// The boundary itself: the series maximum is a legal number to hand out, and only the allocation
    /// after it is refused. An off-by-one here would silently retire the last number of every project.
    /// </summary>
    [Fact]
    public void TheSeriesMaximumIsStillAllocatableAndExhaustsTheSeries()
    {
        NumberAllocator allocator = new([FileNameParser.Parse("124-9998 Penultimate.ipt")], Project124);

        Assert.False(allocator.IsExhausted(NumberSeries.Part));
        Assert.Equal(9999, allocator.Allocate(NumberSeries.Part)!.Value.Value);
        Assert.True(allocator.IsExhausted(NumberSeries.Part));
        Assert.Null(allocator.Allocate(NumberSeries.Part));
    }

    [Fact]
    public void EmptyScopeNextNumberIsOneNoGapsOrDuplicates()
    {
        NumberAllocator allocator = new([], Project124);

        Assert.Equal(1, allocator.NextNumber(NumberSeries.Part));
        Assert.Equal(1, allocator.NextNumber(NumberSeries.Assembly));
        Assert.Empty(allocator.Gaps(NumberSeries.Part));
        Assert.Empty(allocator.Duplicates(NumberSeries.Part));
        Assert.Empty(allocator.OtherProjectFileNames);
    }

    [Fact]
    public void ScopeIgnoresEntriesWithoutATokenSuchAsUnnumberedOrLegacy()
    {
        ParsedFileName unnumbered = new("Part1.ipt", DocumentKind.Part, NameState.UnnumberedDescription, null, "Part1", null, []);

        NumberAllocator allocator = new([unnumbered], Project124);

        Assert.Equal(1, allocator.NextNumber(NumberSeries.Part));
        Assert.Empty(allocator.OtherProjectFileNames);
    }

    /// <summary>
    /// A drawing carries its model's number by design - '124-0001 Foo.idw' is the drawing OF
    /// '124-0001 Foo.ipt', not a second claim on 0001. Counting it as a second owner reported a false
    /// duplicate, which then stopped the model itself being normalized.
    /// </summary>
    [Fact]
    public void ACompanionDrawingSharingItsModelsNumberIsNotADuplicate()
    {
        NumberAllocator allocator = new(
            [FileNameParser.Parse("124-0001 Foo.ipt"), FileNameParser.Parse("124-0001 Foo.idw")],
            Project124);

        Assert.Empty(allocator.Duplicates(NumberSeries.Part));
    }

    /// <summary>
    /// Every numbered token in the project scope - including a drawing or presentation whose model is
    /// gone or out of scope - occupies its number in the real folder tree, so it must raise the series
    /// maximum and fill the gap it sits on. It still cannot OWN the number for duplicate purposes: a lone
    /// drawing is not a second claim on anything, because there is nothing else claiming it.
    /// </summary>
    [Fact]
    public void ALoneDrawingRaisesTheMaximumAndIsNotADuplicate()
    {
        NumberAllocator allocator = new(
            [FileNameParser.Parse("124-0001 A.ipt"), FileNameParser.Parse("124-0080 X.idw")],
            Project124);

        Assert.Equal(81, allocator.NextNumber(NumberSeries.Part));
        Assert.Empty(allocator.Duplicates(NumberSeries.Part));
        IReadOnlyList<int> gaps = allocator.Gaps(NumberSeries.Part);
        for (int candidate = 2; candidate <= 79; candidate++)
        {
            Assert.Contains(candidate, gaps);
        }

        Assert.DoesNotContain(80, gaps);
    }

    /// <summary>
    /// The complement of the companion-drawing rule: two models really do both claim the number, and
    /// that duplicate must still be reported.
    /// </summary>
    [Fact]
    public void TwoPartsSharingOneNumberStillReportTheDuplicate()
    {
        NumberAllocator allocator = new(
            [FileNameParser.Parse("124-0001 Foo.ipt"), FileNameParser.Parse("124-0001 Bar.ipt")],
            Project124);

        DuplicateNumberGroup group = Assert.Single(allocator.Duplicates(NumberSeries.Part));
        Assert.Equal(1, group.Number.Value);
        Assert.Equal(["124-0001 Foo.ipt", "124-0001 Bar.ipt"], group.FileNames);
    }

    /// <summary>
    /// A file named only with its number and no description holds a real, allocated number. If the parser
    /// dropped its token the allocator would hand that same number out again (review verdict R3), so this
    /// case is pinned end to end from the real file name rather than from a hand-built ParsedFileName.
    /// </summary>
    [Fact]
    public void NextNumberAccountsForANumberedFileThatCarriesNoDescription()
    {
        NumberAllocator allocator = new(
            [FileNameParser.Parse("124-0001 Widget.ipt"), FileNameParser.Parse("124-0002.ipt")],
            Project124);

        Assert.Equal(3, allocator.NextNumber(NumberSeries.Part));
        Assert.Empty(allocator.Gaps(NumberSeries.Part));
    }
}
