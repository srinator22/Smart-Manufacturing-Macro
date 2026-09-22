// Purpose: Model the marker one launched apply leaves behind, so a second launch can be refused while
//   the first is still waiting for Inventor to exit. The installer waits minutes or hours - as long as
//   the user keeps Inventor open - and during that window the dialog can be reopened, so without this
//   marker two or more powershell.exe processes would rewrite the Addins root at once.
// Inputs: The process id the launcher reported, the version being applied, which of the two apply
//   kinds it is, and when it started.
// Outputs: The record, its user-facing refusal sentence, and JSON round-tripping that never throws at
//   the caller.
// Dependencies: System.Text.Json only. Nothing here touches the filesystem or a process.
// Assumptions: The marker is advisory, not a lock. It is overwritten, never deleted: a finished or
//   failed installer leaves a stale file behind, and a stale file whose process is gone simply does not
//   block. Treating a pid whose process has exited as "still pending" would strand the user with a
//   dialog that refuses everything until they deleted a file by hand.
// Validation source: docs/ARCHITECTURE.md "Check, stage, apply"; the PR #15 review finding that
//   Download and install could be launched twice.

using System.Text.Json;

namespace WmpToolsManager.Core;

/// <summary>
/// One launched apply step that has not finished yet. <see cref="Pid"/> is the powershell.exe process
/// that is waiting for Inventor to exit.
/// </summary>
public sealed record PendingApply(int Pid, string Version, string Kind, DateTimeOffset LaunchedUtc)
{
    /// <summary>The <see cref="Kind"/> written when an update package is being applied.</summary>
    public const string UpdateKind = "update";

    /// <summary>The <see cref="Kind"/> written when the archived previous install is being restored.</summary>
    public const string RollbackKind = "rollback";

    public const string FileName = "pending-apply.json";

    /// <summary>
    /// The parse message for "no marker exists", which is the normal state and not a problem to report.
    /// It is a constant rather than a sentence matched by text so the reader and the writer of this
    /// distinction cannot drift apart.
    /// </summary>
    public const string NoMarkerMessage = "No pending-apply.json was found.";

    /// <summary>The version text used when the installed version could not be read.</summary>
    public const string UnknownVersion = "an unknown version";

    /// <summary>
    /// What the dialog says while this apply is still running. A rollback is worded as a rollback:
    /// telling a user who pressed Roll back that "an update" is pending would send them looking for an
    /// update they never started.
    /// </summary>
    public string WaitingMessage =>
        string.Equals(Kind, RollbackKind, StringComparison.Ordinal)
            ? $"A rollback from {Version} is already waiting for Inventor to close "
              + $"(PowerShell process {Pid}). Close Inventor to let it finish."
            : $"An update to {Version} is already waiting for Inventor to close "
              + $"(PowerShell process {Pid}). Close Inventor to let it finish.";
}

public static class PendingApplyJson
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize(PendingApply value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return JsonSerializer.Serialize(value, WriteOptions);
    }

    /// <summary>
    /// Reads the marker. Every failure is a parse result, because the caller runs inside an Inventor
    /// command callback where an escaping exception can take the host down.
    /// </summary>
    public static ParseResult<PendingApply> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ParseResult<PendingApply>(null, $"{PendingApply.FileName} was empty.");
        }

        PendingApply? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<PendingApply>(json, ReadOptions);
        }
        catch (JsonException exception)
        {
            return new ParseResult<PendingApply>(
                null,
                $"{PendingApply.FileName} is not valid JSON: {exception.Message}");
        }
        catch (NotSupportedException exception)
        {
            return new ParseResult<PendingApply>(
                null,
                $"{PendingApply.FileName} could not be read: {exception.Message}");
        }

        if (parsed is null)
        {
            return new ParseResult<PendingApply>(null, $"{PendingApply.FileName} deserialised to null.");
        }

        if (parsed.Pid <= 0)
        {
            return new ParseResult<PendingApply>(
                null,
                $"{PendingApply.FileName} records the process id {parsed.Pid}, which identifies no process.");
        }

        bool isUpdate = string.Equals(parsed.Kind, PendingApply.UpdateKind, StringComparison.Ordinal);
        bool isRollback = string.Equals(parsed.Kind, PendingApply.RollbackKind, StringComparison.Ordinal);
        if (!isUpdate && !isRollback)
        {
            return new ParseResult<PendingApply>(
                null,
                $"{PendingApply.FileName} records the kind '{parsed.Kind}', which is neither "
                + $"'{PendingApply.UpdateKind}' nor '{PendingApply.RollbackKind}'.");
        }

        // A JSON document that simply omits "version" leaves the non-nullable property null, which no
        // compiler check catches across a deserialiser boundary.
        return new ParseResult<PendingApply>(
            parsed.Version is null ? parsed with { Version = PendingApply.UnknownVersion } : parsed,
            null);
    }
}
