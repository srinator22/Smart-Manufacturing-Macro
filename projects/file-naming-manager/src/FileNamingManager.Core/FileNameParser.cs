// Purpose: Classify a single file name against the WMP naming grammar defined in .work/TASK.md.
// Inputs: A file name string, with extension, as it exists on disk (no path component expected).
// Outputs: A ParsedFileName describing the structural state, token, description, role, and findings.
// Dependencies: FileNamingManager.Core.NamingModels only.
// Assumptions: Descriptions never change case; whitespace irregularities are reported, never silently fixed.
// Validation source: .work/TASK.md "Naming scheme" section, golden real-world names from the P124 GRM
//   workspace surveyed 2026-09-23, pinned as golden cases in FileNameParserTests.
using System.Globalization;
using System.Text.RegularExpressions;

namespace FileNamingManager.Core;

public static partial class FileNameParser
{
    public static ParsedFileName Parse(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        int dotIndex = fileName.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex == fileName.Length - 1)
        {
            return Unparseable(fileName, DocumentKind.Other);
        }

        string stem = fileName[..dotIndex];
        string extension = fileName[(dotIndex + 1)..];
        DocumentKind kind = DocumentKindExtensions.FromExtension(extension);

        if (string.IsNullOrWhiteSpace(stem))
        {
            return Unparseable(fileName, kind);
        }

        Match revisionMatch = RevisionSuffixRegex().Match(stem);
        if (revisionMatch.Success
            && TokenFrom(revisionMatch.Groups["proj"].Value, revisionMatch.Groups["num"].Value) is NamingToken revToken)
        {
            return new ParsedFileName(
                fileName,
                kind,
                NameState.RevisionSuffixed,
                revToken,
                null,
                null,
                [new NamingFinding(
                    FindingCode.RevisionSuffixed,
                    $"'{fileName}' carries a revision suffix in the file name. Revision suffixes are recognized but never generated.",
                    [fileName])]);
        }

        Match numberedMatch = NumberedRegex().Match(stem);
        if (!numberedMatch.Success)
        {
            Match legacyMatch = LegacyPrefixRegex().Match(stem);
            if (legacyMatch.Success)
            {
                // The text after the legacy prefix is the description, and it is the only part of the
                // name worth carrying into a renamed file. Falling back to the whole stem instead would
                // embed the retired 'P74B' token inside the new description.
                string legacyDescription = NormalizeWhitespace(stem[legacyMatch.Length..]);
                return new ParsedFileName(
                    fileName,
                    kind,
                    NameState.LegacyPrefix,
                    null,
                    legacyDescription.Length == 0 ? null : legacyDescription,
                    null,
                    [new NamingFinding(
                        FindingCode.LegacyPrefix,
                        $"'{fileName}' uses the legacy P-prefix numbering scheme, not the current PPP-NNNN scheme.",
                        [fileName])]);
            }

            Match bareNumberMatch = LeadingNumberNoDescriptionRegex().Match(stem);
            if (bareNumberMatch.Success)
            {
                return TokenFrom(bareNumberMatch.Groups["proj"].Value, bareNumberMatch.Groups["num"].Value)
                    is NamingToken bareToken
                    ? NumberedWithoutDescription(fileName, kind, bareToken, null, [])
                    : Unparseable(fileName, kind);
            }

            string unnumberedDescription = NormalizeWhitespace(stem);
            return new ParsedFileName(
                fileName,
                kind,
                NameState.UnnumberedDescription,
                null,
                unnumberedDescription,
                null,
                [new NamingFinding(
                    FindingCode.Unnumbered,
                    $"'{fileName}' has no project-item number token.",
                    [fileName])]);
        }

        if (TokenFrom(numberedMatch.Groups["proj"].Value, numberedMatch.Groups["num"].Value) is not NamingToken token)
        {
            return Unparseable(fileName, kind);
        }

        string rest = numberedMatch.Groups["rest"].Value;
        NumberSeries series = token.Number.Series;

        int leadingSpaceCount = rest.Length - rest.TrimStart(' ').Length;
        string body = rest.TrimStart(' ');
        int trailingSpaceCount = body.Length - body.TrimEnd(' ').Length;
        string core = body.TrimEnd(' ');

        bool copySuffixFound = false;
        Match copyMatch = CopySuffixRegex().Match(core);
        if (copyMatch.Success)
        {
            copySuffixFound = true;
            core = copyMatch.Groups["pre"].Value.TrimEnd(' ');
        }

        AssemblyRole? role = null;
        Match tagMatch = TagRegex().Match(core);
        if (tagMatch.Success)
        {
            role = tagMatch.Groups["tag"].Value == "main assembly" ? AssemblyRole.Main : AssemblyRole.Sub;
            core = tagMatch.Groups["pre"].Value.TrimEnd(' ');
        }

        string descriptionCore = core;
        string normalizedDescription = NormalizeWhitespace(descriptionCore);

        bool whitespaceMalformed = leadingSpaceCount != 1
            || trailingSpaceCount != 0
            || !string.Equals(descriptionCore, normalizedDescription, StringComparison.Ordinal);

        List<NamingFinding> findings = [];
        bool wrongSeries =
            (kind == DocumentKind.Part && series == NumberSeries.Assembly) ||
            (kind == DocumentKind.Assembly && series == NumberSeries.Part);
        if (wrongSeries)
        {
            findings.Add(new NamingFinding(
                FindingCode.WrongSeriesForKind,
                $"'{fileName}' is a {kind} file but carries a {series}-series number token '{token.Number}'.",
                [fileName]));
        }

        if (normalizedDescription.Length == 0)
        {
            // Trailing spaces or a bare role tag after the number leave nothing to describe the file.
            // This is NumberedWithoutDescription, not MalformedWhitespace: no amount of respacing
            // produces a canonical name, because there is no description text to respace.
            return NumberedWithoutDescription(fileName, kind, token, role, findings);
        }

        bool isTaglessAssembly = kind == DocumentKind.Assembly && role is null;
        if (isTaglessAssembly)
        {
            findings.Add(new NamingFinding(
                FindingCode.TaglessAssembly,
                $"'{fileName}' is missing its (main assembly) or (sub-assembly) tag.",
                [fileName]));
        }

        if (copySuffixFound)
        {
            findings.Add(new NamingFinding(
                FindingCode.CopySuffix,
                $"'{fileName}' carries a copy suffix, likely left by a file-explorer copy.",
                [fileName]));
        }

        NameState state;
        if (copySuffixFound)
        {
            // A copy suffix is the more specific, more actionable defect: it points at a stray
            // duplicate file rather than a workflow gap, so it takes precedence over a co-occurring
            // missing assembly tag. See FileNameParserTests for the pinned example.
            state = NameState.CopySuffix;
        }
        else if (isTaglessAssembly)
        {
            state = NameState.TaglessAssembly;
        }
        else if (whitespaceMalformed)
        {
            state = NameState.MalformedWhitespace;
            findings.Add(new NamingFinding(
                FindingCode.MalformedWhitespace,
                $"'{fileName}' has irregular whitespace around its description.",
                [fileName]));
        }
        else
        {
            state = NameState.Canonical;
        }

        return new ParsedFileName(fileName, kind, state, token, normalizedDescription, role, findings);
    }

    private static ParsedFileName Unparseable(string fileName, DocumentKind kind) =>
        new(fileName, kind, NameState.Unparseable, null, null, null, []);

    /// <summary>
    /// A real, allocated number with no description text. The token is kept so NumberAllocator counts
    /// the number as taken; the null description is what stops the workflow proposing a new name.
    /// </summary>
    private static ParsedFileName NumberedWithoutDescription(
        string fileName,
        DocumentKind kind,
        NamingToken token,
        AssemblyRole? role,
        List<NamingFinding> findings)
    {
        findings.Add(new NamingFinding(
            FindingCode.NumberedWithoutDescription,
            $"'{fileName}' carries a number token but no description.",
            [fileName]));

        return new ParsedFileName(fileName, kind, NameState.NumberedWithoutDescription, token, null, role, findings);
    }

    /// <summary>
    /// The number regexes accept four digits, which includes <c>0000</c> and <c>A000</c>; those are
    /// outside both series' legal ranges. A file name is untrusted input read off disk, so an
    /// out-of-range number is reported as an unrecognized name, never thrown out of the parser.
    /// </summary>
    private static NamingToken? TokenFrom(string projectText, string numberText)
    {
        if (!ProjectNumber.TryParse(projectText, out ProjectNumber project))
        {
            return null;
        }

        NumberSeries series = numberText[0] == 'A' ? NumberSeries.Assembly : NumberSeries.Part;
        int value = series == NumberSeries.Assembly
            ? int.Parse(numberText[1..], CultureInfo.InvariantCulture)
            : int.Parse(numberText, CultureInfo.InvariantCulture);
        int min = series == NumberSeries.Part ? ItemNumber.PartMinValue : ItemNumber.AssemblyMinValue;
        int max = series == NumberSeries.Part ? ItemNumber.PartMaxValue : ItemNumber.AssemblyMaxValue;
        if (value < min || value > max)
        {
            return null;
        }

        return new NamingToken(project, new ItemNumber(series, value));
    }

    private static string NormalizeWhitespace(string value) =>
        CollapseWhitespaceRegex().Replace(value.Trim(), " ");

    [GeneratedRegex(@"^(?<proj>\d{3})-(?<num>\d{4})-(?<rev>[A-Za-z]\d+)$")]
    private static partial Regex RevisionSuffixRegex();

    [GeneratedRegex(@"^(?<proj>\d{3})-(?<num>\d{4}|A\d{3})(?<rest>\s.*)$")]
    private static partial Regex NumberedRegex();

    [GeneratedRegex(@"^(?<proj>\d{3})-(?<num>\d{4}|A\d{3})$")]
    private static partial Regex LeadingNumberNoDescriptionRegex();

    [GeneratedRegex(@"^P\d{2}[A-Za-z]?\s")]
    private static partial Regex LegacyPrefixRegex();

    [GeneratedRegex(@"^(?<pre>.*?)(?<sp>\s*)_(?<digits>\d+)$")]
    private static partial Regex CopySuffixRegex();

    [GeneratedRegex(@"^(?<pre>.*?)(?<sp>\s*)\((?<tag>main assembly|sub-assembly)\)$")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex CollapseWhitespaceRegex();
}
