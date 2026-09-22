// Purpose: Reduce a release body to the excerpt the update dialog shows, without ever presenting a
//   truncated text as if it were complete.
// Inputs: The "body" field of a Releases API payload, which may be absent, empty, or thousands of lines.
// Outputs: At most the requested number of lines, with an explicit marker when anything was cut.
// Dependencies: .NET base types only.
// Assumptions: The body is CommonMark with either LF or CRLF line endings; it is displayed as plain
//   text, never rendered, so no markup is interpreted and nothing in it can act on the user.
// Validation source: docs/decisions/0005-release-distribution-and-updater.md item 3 ("shows the
//   changelog excerpt").

namespace WmpToolsManager.Core;

public static class ReleaseNotes
{
    public const int DefaultLineCount = 20;

    /// <summary>The line appended when the body was longer than the excerpt.</summary>
    public const string TruncationMarker = "... (truncated; see the full release notes on GitHub)";

    /// <summary>
    /// Returns the first <paramref name="maxLines"/> lines of the body. An absent or blank body yields
    /// an empty string, which the dialog shows as "No release notes were published."
    /// </summary>
    public static string Excerpt(string? body, int maxLines = DefaultLineCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);

        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        string[] lines = body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd('\n')
            .Split('\n');

        if (lines.Length <= maxLines)
        {
            return string.Join(Environment.NewLine, lines).Trim();
        }

        return string.Join(Environment.NewLine, lines.Take(maxLines)).TrimEnd()
            + Environment.NewLine
            + TruncationMarker;
    }
}
