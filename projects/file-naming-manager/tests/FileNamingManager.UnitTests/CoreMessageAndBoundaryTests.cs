// Purpose: Pin the exact text Core puts in front of an engineer, and the exact numeric boundaries of
//   the two numbering series.
// Inputs: Literal file names, tokens, and scopes built from FileNameParser.
// Outputs: Assertions on finding messages, exception messages, and boundary parse results.
// Dependencies: FileNamingManager.Core.
// Assumptions: A finding whose message says nothing is as useless as a missing finding, so the message
//   itself is part of the contract, not decoration. Boundary numbers are pinned at the exact limit
//   rather than near it, because an off-by-one there silently rejects a legal file name.
// Validation source: .work/TASK.md "Naming scheme" (PPP 100-999, NNNN 0001-9999, ANNN A001-A999) and
//   "Findings the analyzer must report".

using FileNamingManager.Core;

namespace FileNamingManager.UnitTests;

public class CoreMessageAndBoundaryTests
{
    private static readonly ProjectNumber Project124 = new(124);

    [Theory]
    [InlineData(
        "101-0001-A0.ipt",
        FindingCode.RevisionSuffixed,
        "'101-0001-A0.ipt' carries a revision suffix in the file name. Revision suffixes are recognized but never generated.")]
    [InlineData(
        "P74B 32.75mm 0.85.ipt",
        FindingCode.LegacyPrefix,
        "'P74B 32.75mm 0.85.ipt' uses the legacy P-prefix numbering scheme, not the current PPP-NNNN scheme.")]
    [InlineData(
        "Part1.ipt",
        FindingCode.Unnumbered,
        "'Part1.ipt' has no project-item number token.")]
    [InlineData(
        "124-A002 Bracket.ipt",
        FindingCode.WrongSeriesForKind,
        "'124-A002 Bracket.ipt' is a Part file but carries a Assembly-series number token 'A002'.")]
    [InlineData(
        "124-A001 Full Double Stack Mechanism .iam",
        FindingCode.TaglessAssembly,
        "'124-A001 Full Double Stack Mechanism .iam' is missing its (main assembly) or (sub-assembly) tag.")]
    [InlineData(
        "124-A001 Full Double Stack Mechanism _1.iam",
        FindingCode.CopySuffix,
        "'124-A001 Full Double Stack Mechanism _1.iam' carries a copy suffix, likely left by a file-explorer copy.")]
    [InlineData(
        "124-0074  wakeup fan mesh cartirdge .ipt",
        FindingCode.MalformedWhitespace,
        "'124-0074  wakeup fan mesh cartirdge .ipt' has irregular whitespace around its description.")]
    [InlineData(
        "124-0002.ipt",
        FindingCode.NumberedWithoutDescription,
        "'124-0002.ipt' carries a number token but no description.")]
    public void ParseFindingsCarryTheirFullMessage(string fileName, FindingCode code, string expectedMessage)
    {
        ParsedFileName result = FileNameParser.Parse(fileName);

        NamingFinding finding = Assert.Single(result.Findings, f => f.Code == code);
        Assert.Equal(expectedMessage, finding.Message);
        Assert.Equal([fileName], finding.FileNames);
    }

    [Fact]
    public void ParseLeadingDotNameIsUnparseableAndNotClassifiedByItsTrailingExtension()
    {
        ParsedFileName result = FileNameParser.Parse(".ipt");

        Assert.Equal(NameState.Unparseable, result.State);
        Assert.Equal(DocumentKind.Other, result.Kind);
    }

    [Fact]
    public void ParseLegacyPrefixWithNothingAfterItLeavesTheDescriptionNull()
    {
        ParsedFileName result = FileNameParser.Parse("P74B .ipt");

        Assert.Equal(NameState.LegacyPrefix, result.State);
        Assert.Null(result.Description);
    }

    /// <summary>
    /// Part numbers run to 9999 and assembly numbers to A999, so the four-digit part form has to be read
    /// as a whole and both series' upper limits accepted exactly.
    /// </summary>
    [Theory]
    [InlineData("124-1002 Widget.ipt", "124-1002")]
    [InlineData("124-9999 Widget.ipt", "124-9999")]
    [InlineData("124-A999 Rig (sub-assembly).iam", "124-A999")]
    public void ParseAcceptsTheFullRangeOfBothSeries(string fileName, string expectedToken)
    {
        ParsedFileName result = FileNameParser.Parse(fileName);

        Assert.Equal(NameState.Canonical, result.State);
        Assert.Equal(expectedToken, result.Token.ToString());
    }

    [Fact]
    public void FromExtensionRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentKindExtensions.FromExtension(null!));
    }

    [Fact]
    public void ProjectNumberRendersAsThreeDigitsAndRejectsOutOfRangeValuesByName()
    {
        Assert.Equal("124", Project124.ToString());

        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectNumber(1000));
        Assert.StartsWith(
            "Project number must be between 100 and 999, inclusive. Actual value: 1000.",
            thrown.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ItemNumberRejectsOutOfRangeValuesByName()
    {
        ArgumentOutOfRangeException thrown =
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemNumber(NumberSeries.Part, 0));
        Assert.StartsWith(
            "Part item number must be between 1 and 9999, inclusive. Actual value: 0.",
            thrown.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", DocumentKind.Part, NumberSeries.Part, null, "description", "Description must not be empty.")]
    [InlineData("Rig", DocumentKind.Assembly, NumberSeries.Assembly, null, "role", "Assembly file names require a main or sub assembly role.")]
    [InlineData("Widget", DocumentKind.Part, NumberSeries.Assembly, null, "token", "Part file names require a part-series number token; got assembly-series token '124-A001'.")]
    [InlineData("Rig", DocumentKind.Assembly, NumberSeries.Part, AssemblyRole.Sub, "token", "Assembly file names require an assembly-series number token; got part-series token '124-0001'.")]
    public void FormatRejectsIllegalCombinationsAndSaysWhich(
        string description,
        DocumentKind kind,
        NumberSeries series,
        AssemblyRole? role,
        string expectedParameterName,
        string expectedMessage)
    {
        NamingToken token = new(Project124, new ItemNumber(series, 1));

        ArgumentException thrown = Assert.Throws<ArgumentException>(
            expectedParameterName,
            () => FileNameFormatter.Format(token, description, kind, role));
        Assert.StartsWith(expectedMessage, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportRejectsANullScope()
    {
        Assert.Throws<ArgumentNullException>(() => NamingReport.Build(null!, Project124));
    }

    [Fact]
    public void ReportFindingsCarryTheirFullMessage()
    {
        string[] fileNames =
        [
            "124-0001 Alpha.ipt",
            "124-0001 Beta.ipt",
            "124-0004 Delta.ipt",
            "124-0004 Delta.ipt",
        ];

        NamingReport report = NamingReport.Build(fileNames.Select(FileNameParser.Parse), Project124);

        Assert.Equal(
            "Number '124-0001' is used by 2 files.",
            Assert.Single(report.ProjectFindings, f => f.Code == FindingCode.DuplicateNumber).Message);
        Assert.Equal(
            "Part series is missing number(s): 2, 3.",
            Assert.Single(report.ProjectFindings, f => f.Code == FindingCode.SequenceGap).Message);
        Assert.Equal(
            "File name '124-0004 Delta.ipt' appears 2 times in the project scope.",
            Assert.Single(report.ProjectFindings, f => f.Code == FindingCode.DuplicateFileName).Message);
    }

    [Fact]
    public void DuplicateGroupsComeBackInAscendingNumberOrder()
    {
        string[] fileNames =
        [
            "124-0005 Echo.ipt",
            "124-0005 Echo Copy.ipt",
            "124-0002 Bravo.ipt",
            "124-0002 Bravo Copy.ipt",
        ];

        NumberAllocator allocator = new(fileNames.Select(FileNameParser.Parse), Project124);

        Assert.Equal(
            [2, 5],
            allocator.Duplicates(NumberSeries.Part).Select(group => group.Number.Value));
    }
}
