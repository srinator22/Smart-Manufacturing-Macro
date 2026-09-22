// Purpose: Pin FileNameParser's classification of real WMP P124 GRM file names to golden values.
// Inputs: Literal file name strings, including the exact real-world names from .work/TASK.md.
// Outputs: Assertions against ParsedFileName state, token, description, role, and findings.
// Dependencies: FileNamingManager.Core.
// Assumptions: None; these are pure, deterministic unit tests.
// Validation source: .work/TASK.md "Naming scheme" and "Findings the analyzer must report" sections,
//   taken from the live P124 GRM workspace surveyed 2026-09-23.

using FileNamingManager.Core;

namespace FileNamingManager.UnitTests;

public class FileNameParserTests
{
    [Fact]
    public void ParseCanonicalPartReturnsCanonicalWithTokenAndDescription()
    {
        ParsedFileName result = FileNameParser.Parse("124-0002 Adaptor Plate Bottom.ipt");

        Assert.Equal(NameState.Canonical, result.State);
        Assert.Equal(DocumentKind.Part, result.Kind);
        Assert.Equal("124-0002", result.Token.ToString());
        Assert.Equal("Adaptor Plate Bottom", result.Description);
        Assert.Null(result.AssemblyRole);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ParseCanonicalSubAssemblyReturnsCanonicalWithSubRole()
    {
        ParsedFileName result = FileNameParser.Parse("124-A002 NDRM (sub-assembly).iam");

        Assert.Equal(NameState.Canonical, result.State);
        Assert.Equal(DocumentKind.Assembly, result.Kind);
        Assert.Equal("124-A002", result.Token.ToString());
        Assert.Equal("NDRM", result.Description);
        Assert.Equal(AssemblyRole.Sub, result.AssemblyRole);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ParseCanonicalMainAssemblyReturnsCanonicalWithMainRole()
    {
        ParsedFileName result = FileNameParser.Parse("124-A001 Full Double Stack Mechanism (main assembly).iam");

        Assert.Equal(NameState.Canonical, result.State);
        Assert.Equal(DocumentKind.Assembly, result.Kind);
        Assert.Equal("124-A001", result.Token.ToString());
        Assert.Equal("Full Double Stack Mechanism", result.Description);
        Assert.Equal(AssemblyRole.Main, result.AssemblyRole);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void ParseMalformedWhitespaceNormalizesDescriptionButFlagsState()
    {
        ParsedFileName result = FileNameParser.Parse("124-0074  wakeup fan mesh cartirdge .ipt");

        Assert.Equal(NameState.MalformedWhitespace, result.State);
        Assert.Equal("124-0074", result.Token.ToString());
        Assert.Equal("wakeup fan mesh cartirdge", result.Description);
        Assert.Contains(result.Findings, f => f.Code == FindingCode.MalformedWhitespace);
    }

    [Fact]
    public void ParseTaglessAssemblyReturnsTaglessAssemblyState()
    {
        ParsedFileName result = FileNameParser.Parse("124-A001 Full Double Stack Mechanism .iam");

        Assert.Equal(NameState.TaglessAssembly, result.State);
        Assert.Equal("124-A001", result.Token.ToString());
        Assert.Equal("Full Double Stack Mechanism", result.Description);
        Assert.Null(result.AssemblyRole);
        Assert.Contains(result.Findings, f => f.Code == FindingCode.TaglessAssembly);
    }

    /// <summary>
    /// "124-A001 Full Double Stack Mechanism _1.iam" is simultaneously tagless (no main/sub tag) and
    /// carries a copy suffix. TASK.md allows either state as long as it is deterministic and documented:
    /// this parser chooses CopySuffix, because a stray Explorer-copy artifact is the more specific and
    /// more actionable defect - it points at a duplicate file to delete or rename, not just a missing tag.
    /// The TaglessAssembly finding is still reported alongside it so nothing is lost.
    /// </summary>
    [Fact]
    public void ParseTaglessAssemblyWithCopySuffixChoosesCopySuffixState()
    {
        ParsedFileName result = FileNameParser.Parse("124-A001 Full Double Stack Mechanism _1.iam");

        Assert.Equal(NameState.CopySuffix, result.State);
        Assert.Equal("124-A001", result.Token.ToString());
        Assert.Equal("Full Double Stack Mechanism", result.Description);
        Assert.Null(result.AssemblyRole);
        Assert.Contains(result.Findings, f => f.Code == FindingCode.CopySuffix);
        Assert.Contains(result.Findings, f => f.Code == FindingCode.TaglessAssembly);
    }

    [Theory]
    [InlineData("Part1.ipt", "Part1")]
    [InlineData("gasket cutting template.ipt", "gasket cutting template")]
    public void ParseUnnumberedDescriptionReturnsUnnumberedWithNoToken(string fileName, string expectedDescription)
    {
        ParsedFileName result = FileNameParser.Parse(fileName);

        Assert.Equal(NameState.UnnumberedDescription, result.State);
        Assert.Null(result.Token);
        Assert.Equal(expectedDescription, result.Description);
        Assert.Contains(result.Findings, f => f.Code == FindingCode.Unnumbered);
    }

    [Fact]
    public void ParseLegacyPrefixReturnsLegacyPrefixState()
    {
        ParsedFileName result = FileNameParser.Parse("P74B 32.75mm 0.85.ipt");

        Assert.Equal(NameState.LegacyPrefix, result.State);
        Assert.Null(result.Token);
        Assert.Contains(result.Findings, f => f.Code == FindingCode.LegacyPrefix);
    }

    [Fact]
    public void ParseRevisionSuffixedReturnsTokenWithoutDescription()
    {
        ParsedFileName result = FileNameParser.Parse("101-0001-A0.ipt");

        Assert.Equal(NameState.RevisionSuffixed, result.State);
        Assert.Equal("101-0001", result.Token.ToString());
        Assert.Null(result.Description);
        Assert.Contains(result.Findings, f => f.Code == FindingCode.RevisionSuffixed);
    }

    [Fact]
    public void ParseDecimalPointsInDescriptionAreLegalAndCanonical()
    {
        ParsedFileName result = FileNameParser.Parse("124-0022 Hex Shaft 0.75 Amplitude Offset with Clamp.ipt");

        Assert.Equal(NameState.Canonical, result.State);
        Assert.Equal("124-0022", result.Token.ToString());
        Assert.Equal("Hex Shaft 0.75 Amplitude Offset with Clamp", result.Description);
    }

    [Theory]
    [InlineData("124-A002 Bracket.ipt")]
    [InlineData("124-0002 Bracket.iam")]
    public void ParseWrongSeriesForKindReportsFinding(string fileName)
    {
        ParsedFileName result = FileNameParser.Parse(fileName);

        Assert.Contains(result.Findings, f => f.Code == FindingCode.WrongSeriesForKind);
    }

    [Theory]
    [InlineData("124-0002 Adaptor Plate Bottom.ipt")]
    [InlineData("124-A002 NDRM (sub-assembly).iam")]
    [InlineData("124-A001 Full Double Stack Mechanism (main assembly).iam")]
    [InlineData("124-0022 Hex Shaft 0.75 Amplitude Offset with Clamp.ipt")]
    public void FormatOfParseRoundTripsForCanonicalNames(string fileName)
    {
        ParsedFileName parsed = FileNameParser.Parse(fileName);

        Assert.Equal(NameState.Canonical, parsed.State);
        string formatted = FileNameFormatter.Format(
            parsed.Token!.Value,
            parsed.Description!,
            parsed.Kind,
            parsed.AssemblyRole);

        Assert.Equal(fileName, formatted);
    }

    [Fact]
    public void ParseNullOrWhitespaceFileNameThrows()
    {
        Assert.Throws<ArgumentException>(() => FileNameParser.Parse(""));
        Assert.Throws<ArgumentException>(() => FileNameParser.Parse("   "));
    }

    [Theory]
    [InlineData("noextension")]
    [InlineData(".ipt")]
    [InlineData("124-0002.")]
    public void ParseMissingOrEmptyExtensionOrStemReturnsUnparseable(string fileName)
    {
        ParsedFileName result = FileNameParser.Parse(fileName);

        Assert.Equal(NameState.Unparseable, result.State);
        Assert.Null(result.Token);
    }

    /// <summary>
    /// A name that is only a number, with no description, still carries a real, allocated number. Parsing
    /// it as Unparseable dropped the token, which made the number invisible to NumberAllocator and let
    /// Apply mint that same number again for another file - a duplicate the analyzer could never report
    /// (review verdict R3). The token is therefore kept and the description stays null, which is what
    /// stops the workflow proposing a name for it.
    /// </summary>
    [Theory]
    [InlineData("124-0002.ipt", "124-0002", DocumentKind.Part, null)]
    [InlineData("124-0002  .ipt", "124-0002", DocumentKind.Part, null)]
    [InlineData("124-A002.iam", "124-A002", DocumentKind.Assembly, null)]
    [InlineData("124-A002 (sub-assembly).iam", "124-A002", DocumentKind.Assembly, AssemblyRole.Sub)]
    public void ParseNumberedWithNoDescriptionKeepsTheTokenAndLeavesTheDescriptionNull(
        string fileName,
        string expectedToken,
        DocumentKind expectedKind,
        AssemblyRole? expectedRole)
    {
        ParsedFileName result = FileNameParser.Parse(fileName);

        Assert.Equal(NameState.NumberedWithoutDescription, result.State);
        Assert.Equal(expectedKind, result.Kind);
        Assert.Equal(expectedToken, result.Token.ToString());
        Assert.Null(result.Description);
        Assert.Equal(expectedRole, result.AssemblyRole);
        Assert.Contains(result.Findings, f => f.Code == FindingCode.NumberedWithoutDescription);
    }

    /// <summary>
    /// The number regexes accept three and four digits generically, so they also match numbers outside
    /// the legal series ranges. A file name is untrusted input read off disk: an out-of-range number is
    /// an unrecognized name, never an exception out of the parser and into Analyze.
    /// </summary>
    [Theory]
    [InlineData("124-0000.ipt")]
    [InlineData("124-A000.iam")]
    [InlineData("124-0000 Widget.ipt")]
    [InlineData("099-0001 Widget.ipt")]
    public void ParseOutOfRangeNumberReturnsUnparseableRatherThanThrowing(string fileName)
    {
        ParsedFileName result = FileNameParser.Parse(fileName);

        Assert.Equal(NameState.Unparseable, result.State);
        Assert.Null(result.Token);
    }

    [Fact]
    public void ParseDrawingSharingAssemblyStemMirrorsRoleTagWithoutWrongSeriesFinding()
    {
        ParsedFileName result = FileNameParser.Parse("124-A001 Full Double Stack Mechanism (main assembly).idw");

        Assert.Equal(DocumentKind.Drawing, result.Kind);
        Assert.Equal(NameState.Canonical, result.State);
        Assert.Equal(AssemblyRole.Main, result.AssemblyRole);
        Assert.DoesNotContain(result.Findings, f => f.Code == FindingCode.WrongSeriesForKind);
    }
}
