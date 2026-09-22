// Purpose: Parse and order the workspace release version, in both the plain form written into
//   catalog.json / installed.json ("0.6.0") and the annotated Git tag form ADR-0005 publishes ("v0.6.0").
// Inputs: Strings from a release tag, a SHA256SUMS package file name, catalog.json, or installed.json.
// Outputs: An ordered, immutable three-part version, or a refusal - never a guessed value.
// Dependencies: .NET base types only.
// Assumptions: The workspace releases as one semantic version before 1.0 (AGENTS.md Project decisions),
//   so there is no pre-release or build-metadata suffix to model. A tag that carries one is rejected
//   rather than silently truncated, because an unrecognised tag must not be compared as if it were known.
// Validation source: docs/decisions/0005-release-distribution-and-updater.md, Directory.Build.props
//   VersionPrefix, and scripts/release/build-release.ps1 (which names the package WmpInventorTools-<version>.zip).

using System.Globalization;

namespace WmpToolsManager.Core;

/// <summary>
/// A three-part semantic version. Comparison is numeric per component, so 0.10.0 sorts above 0.9.0.
/// </summary>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch)
    : IComparable<SemanticVersion>, IComparable
{
    /// <summary>
    /// The prefix an annotated release tag carries. Only the lowercase form is accepted, because it is
    /// the only form the release workflow produces.
    /// </summary>
    public const string TagPrefix = "v";

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Parses "MAJOR.MINOR.PATCH". Anything else - an empty string, whitespace, a leading "v", a
    /// two- or four-part version, a negative or non-numeric component - is refused.
    /// </summary>
    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        string[] parts = text.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!TryParseComponent(parts[0], out int major)
            || !TryParseComponent(parts[1], out int minor)
            || !TryParseComponent(parts[2], out int patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch);
        return true;
    }

    /// <summary>
    /// Parses an annotated release tag such as "v0.6.0". A tag without the prefix, or with anything
    /// after the patch component, is refused.
    /// </summary>
    public static bool TryParseTag(string? tag, out SemanticVersion version)
    {
        version = default;
        if (tag is null || !tag.StartsWith(TagPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return TryParse(tag[TagPrefix.Length..], out version);
    }

    /// <summary>
    /// The annotated tag that publishes this version.
    /// </summary>
    public string ToTag() => TagPrefix + ToString();

    public int CompareTo(SemanticVersion other)
    {
        int major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        int minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        SemanticVersion other => CompareTo(other),
        _ => throw new ArgumentException($"Cannot compare a {nameof(SemanticVersion)} with {obj.GetType()}.", nameof(obj)),
    };

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Major}.{Minor}.{Patch}");

    private static bool TryParseComponent(string part, out int value)
    {
        value = 0;

        // int.TryParse would accept "+1", " 1" and culture-specific group separators. A version
        // component is exactly a run of ASCII digits, so the shape is checked before parsing.
        if (part.Length is 0 or > 9)
        {
            return false;
        }

        foreach (char character in part)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
