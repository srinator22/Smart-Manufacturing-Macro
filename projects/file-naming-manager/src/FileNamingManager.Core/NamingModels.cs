// Purpose: Define the COM-free value types and result records for the WMP file naming scheme.
// Inputs: File names (strings) and their parsed components.
// Outputs: Immutable tokens, parse results, and findings consumed by Application and Infrastructure.
// Dependencies: .NET base types only.
// Assumptions: A file name always includes its extension; descriptions never change case.
// Validation source: .work/TASK.md "Naming scheme" section, golden cases in FileNameParserTests.

using System.Globalization;

namespace FileNamingManager.Core;

public enum DocumentKind
{
    Part,
    Assembly,
    Drawing,
    Presentation,
    Other,
}

public static class DocumentKindExtensions
{
    /// <summary>
    /// Maps a file extension (with or without leading dot, any case) to a <see cref="DocumentKind"/>.
    /// </summary>
    public static DocumentKind FromExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        string trimmed = extension.TrimStart('.');
        return trimmed.ToUpperInvariant() switch
        {
            "IPT" => DocumentKind.Part,
            "IAM" => DocumentKind.Assembly,
            "IDW" or "DWG" => DocumentKind.Drawing,
            "IPN" => DocumentKind.Presentation,
            _ => DocumentKind.Other,
        };
    }
}

public enum NumberSeries
{
    Part,
    Assembly,
}

public enum AssemblyRole
{
    Main,
    Sub,
}

/// <summary>
/// Three-digit WMP project number, 100-999.
/// </summary>
public readonly record struct ProjectNumber
{
    public const int MinValue = 100;
    public const int MaxValue = 999;

    public ProjectNumber(int value)
    {
        if (value is < MinValue or > MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Project number must be between {MinValue} and {MaxValue}, inclusive. Actual value: {value}.");
        }

        Value = value;
    }

    public int Value { get; }

    public static bool TryParse(string? text, out ProjectNumber result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (text.Length != 3 || !int.TryParse(text, out int parsed))
        {
            return false;
        }

        if (parsed is < MinValue or > MaxValue)
        {
            return false;
        }

        result = new ProjectNumber(parsed);
        return true;
    }

    public override string ToString() => Value.ToString("D3", CultureInfo.InvariantCulture);
}

/// <summary>
/// A number within a numbering series: 0001-9999 for parts, A001-A999 for assemblies.
/// </summary>
public readonly record struct ItemNumber
{
    public const int PartMinValue = 1;
    public const int PartMaxValue = 9999;
    public const int AssemblyMinValue = 1;
    public const int AssemblyMaxValue = 999;

    public ItemNumber(NumberSeries series, int value)
    {
        int max = series == NumberSeries.Part ? PartMaxValue : AssemblyMaxValue;
        int min = series == NumberSeries.Part ? PartMinValue : AssemblyMinValue;
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"{series} item number must be between {min} and {max}, inclusive. Actual value: {value}.");
        }

        Series = series;
        Value = value;
    }

    public NumberSeries Series { get; }

    public int Value { get; }

    public override string ToString() => Series == NumberSeries.Part
        ? Value.ToString("D4", CultureInfo.InvariantCulture)
        : "A" + Value.ToString("D3", CultureInfo.InvariantCulture);
}

/// <summary>
/// The project-plus-item token that prefixes every canonical file name, e.g. <c>124-0002</c> or <c>124-A002</c>.
/// </summary>
public readonly record struct NamingToken(ProjectNumber Project, ItemNumber Number)
{
    public override string ToString() => $"{Project}-{Number}";
}

public enum NameState
{
    Canonical,
    UnnumberedDescription,

    /// <summary>
    /// A well-formed number token with no description at all, e.g. <c>124-0002.ipt</c>. The number is
    /// real and allocated, so the token is kept and must count toward allocation; the missing description
    /// is what makes the name unproposable, because there is nothing to carry into the new name.
    /// </summary>
    NumberedWithoutDescription,
    LegacyPrefix,
    RevisionSuffixed,
    TaglessAssembly,
    MalformedWhitespace,
    CopySuffix,
    Unparseable,
}

public enum FindingCode
{
    DuplicateNumber,
    SequenceGap,
    MalformedWhitespace,
    TaglessAssembly,
    Unnumbered,
    LegacyPrefix,
    RevisionSuffixed,
    CopySuffix,
    DuplicateFileName,
    WrongSeriesForKind,
    OtherProject,
    NumberedWithoutDescription,
}

public sealed record NamingFinding(FindingCode Code, string Message, IReadOnlyList<string> FileNames);

/// <summary>
/// The result of parsing a single file name (with extension) against the WMP naming grammar.
/// </summary>
public sealed record ParsedFileName(
    string OriginalFileName,
    DocumentKind Kind,
    NameState State,
    NamingToken? Token,
    string? Description,
    AssemblyRole? AssemblyRole,
    IReadOnlyList<NamingFinding> Findings);
