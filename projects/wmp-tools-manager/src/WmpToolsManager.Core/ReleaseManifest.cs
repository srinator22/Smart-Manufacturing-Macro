// Purpose: Resolve the published release version and its digests from SHA256SUMS.txt, which is the
//   add-in's version-check source. ADR-0005 keeps the Releases API for the notes body only: the
//   redirecting releases/latest/download URL needs no token and is not rate-limited, so a user who
//   presses "Check for updates" repeatedly can never be refused by the API's 60-per-hour ceiling.
// Inputs: The text of SHA256SUMS.txt exactly as scripts/release/build-release.ps1 writes it -
//   "<lowercase hex sha-256><two spaces><file name>", one artifact per line, LF terminated.
// Outputs: The release version taken from the package file name, plus the digest of each artifact.
// Dependencies: .NET base types only. No IO, no HTTP.
// Assumptions: The package is named WmpInventorTools-<version>.zip; that name is the only place the
//   release version appears in the sums file, and build-release.ps1 derives it from
//   Directory.Build.props VersionPrefix. The installer's own Get-SumsEntry tolerates any run of
//   whitespace and a leading '*' before the file name, so this parser matches that tolerance rather
//   than assuming the exact two-space form.
// Validation source: scripts/release/build-release.ps1 (writer) and
//   scripts/release/Install-WmpInventorTools.ps1 Get-SumsEntry / Get-SumsZipName (the other reader).

using System.Globalization;

namespace WmpToolsManager.Core;

/// <summary>
/// One "digest file-name" line of SHA256SUMS.txt. <see cref="Sha256"/> is normalised to lowercase hex.
/// </summary>
public sealed record Sha256SumsEntry(string Sha256, string FileName);

/// <summary>
/// What SHA256SUMS.txt says the latest release is.
/// </summary>
public sealed record ReleaseManifest(
    SemanticVersion Version,
    string PackageFileName,
    string PackageSha256,
    string? InstallerSha256,
    IReadOnlyList<Sha256SumsEntry> Entries);

/// <summary>
/// The outcome of parsing SHA256SUMS.txt. Exactly one of the two properties is set.
/// </summary>
public sealed record ReleaseManifestParse(ReleaseManifest? Manifest, string? ErrorMessage)
{
    public bool IsSuccess => Manifest is not null;
}

public static class Sha256SumsFile
{
    /// <summary>
    /// The installer script published beside the package and covered by the same digest file.
    /// </summary>
    public const string InstallerFileName = "Install-WmpInventorTools.ps1";

    /// <summary>
    /// The digest file itself, as published by the release workflow.
    /// </summary>
    public const string FileName = "SHA256SUMS.txt";

    private const string PackagePrefix = "WmpInventorTools-";
    private const string PackageSuffix = ".zip";
    private const int Sha256HexLength = 64;

    /// <summary>
    /// Returns every line that is a digest followed by a file name. A line that is not - a blank line,
    /// a comment, a truncated digest - yields no entry. Dropping it is safe because nothing is trusted
    /// on absence: a download is verified only against a digest that was actually found for its exact
    /// file name, and <see cref="Parse"/> refuses a file with no package line at all.
    /// </summary>
    public static IReadOnlyList<Sha256SumsEntry> ParseEntries(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        List<Sha256SumsEntry> entries = [];
        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            string digest = parts[0].ToLowerInvariant();
            if (!IsSha256Hex(digest))
            {
                continue;
            }

            entries.Add(new Sha256SumsEntry(digest, parts[1].Trim().TrimStart('*')));
        }

        return entries;
    }

    /// <summary>
    /// Reads the published version and digests out of SHA256SUMS.txt.
    /// </summary>
    public static ReleaseManifestParse Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new ReleaseManifestParse(null, $"{FileName} was empty.");
        }

        IReadOnlyList<Sha256SumsEntry> entries = ParseEntries(content);
        if (entries.Count == 0)
        {
            return new ReleaseManifestParse(
                null,
                $"{FileName} contained no '<sha-256>  <file name>' lines.");
        }

        Sha256SumsEntry? package = entries.FirstOrDefault(entry =>
            entry.FileName.StartsWith(PackagePrefix, StringComparison.OrdinalIgnoreCase)
            && entry.FileName.EndsWith(PackageSuffix, StringComparison.OrdinalIgnoreCase));
        if (package is null)
        {
            return new ReleaseManifestParse(
                null,
                $"{FileName} does not list a {PackagePrefix}<version>{PackageSuffix} package.");
        }

        string versionText = package.FileName[PackagePrefix.Length..^PackageSuffix.Length];
        if (!SemanticVersion.TryParse(versionText, out SemanticVersion version))
        {
            return new ReleaseManifestParse(
                null,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{package.FileName}' does not carry a MAJOR.MINOR.PATCH version."));
        }

        string? installerSha256 = entries
            .FirstOrDefault(entry => string.Equals(entry.FileName, InstallerFileName, StringComparison.OrdinalIgnoreCase))
            ?.Sha256;

        return new ReleaseManifestParse(
            new ReleaseManifest(version, package.FileName, package.Sha256, installerSha256, entries),
            null);
    }

    /// <summary>
    /// Compares two digests as hex text, ignoring case. Returns false when either side is absent, so a
    /// missing digest can never be read as a match.
    /// </summary>
    public static bool DigestsMatch(string? expected, string? actual) =>
        !string.IsNullOrEmpty(expected)
        && !string.IsNullOrEmpty(actual)
        && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256Hex(string candidate)
    {
        if (candidate.Length != Sha256HexLength)
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (character is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }
}
