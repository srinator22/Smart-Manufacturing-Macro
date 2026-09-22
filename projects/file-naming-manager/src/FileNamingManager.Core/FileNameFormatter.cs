// Purpose: Render a naming token, description, kind, and optional assembly role into a canonical file name.
// Inputs: A NamingToken, a normalized description, a DocumentKind, and an optional AssemblyRole.
// Outputs: A canonical file name string with a lower-case extension.
// Dependencies: FileNamingManager.Core.NamingModels only.
// Assumptions: The description is already whitespace-normalized; callers never pass raw, unvalidated text.
// Validation source: .work/TASK.md "Naming scheme" section; round-trip tests in FileNameFormatterTests.

namespace FileNamingManager.Core;

public static class FileNameFormatter
{
    public static string Format(NamingToken token, string description, DocumentKind kind, AssemblyRole? role)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description must not be empty.", nameof(description));
        }

        if (kind == DocumentKind.Assembly && role is null)
        {
            throw new ArgumentException("Assembly file names require a main or sub assembly role.", nameof(role));
        }

        if (kind == DocumentKind.Part && token.Number.Series == NumberSeries.Assembly)
        {
            throw new ArgumentException(
                $"Part file names require a part-series number token; got assembly-series token '{token}'.",
                nameof(token));
        }

        if (kind == DocumentKind.Assembly && token.Number.Series == NumberSeries.Part)
        {
            throw new ArgumentException(
                $"Assembly file names require an assembly-series number token; got part-series token '{token}'.",
                nameof(token));
        }

        if (role is not null && kind is not (DocumentKind.Assembly or DocumentKind.Drawing or DocumentKind.Presentation))
        {
            throw new ArgumentException(
                $"A main/sub assembly role tag is not valid for {kind} file names.",
                nameof(role));
        }

        string extension = kind switch
        {
            DocumentKind.Part => "ipt",
            DocumentKind.Assembly => "iam",
            DocumentKind.Drawing => "idw",
            DocumentKind.Presentation => "ipn",
            _ => throw new ArgumentException($"'{kind}' is not a formattable document kind.", nameof(kind)),
        };

        string tag = role switch
        {
            AssemblyRole.Main => " (main assembly)",
            AssemblyRole.Sub => " (sub-assembly)",
            null => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unrecognized assembly role."),
        };

        return $"{token} {description}{tag}.{extension}";
    }
}
