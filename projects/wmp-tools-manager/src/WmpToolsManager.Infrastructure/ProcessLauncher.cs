// Purpose: Start the PowerShell apply step out of process. Inventor locks its own add-in assemblies
//   while it runs, so the update can only be applied by a process that outlives the host.
// Inputs: An executable name and an argument string built by Core's ApplyCommandLine.
// Outputs: Whether the process started, the id it started under, and the message shown either way. The
//   process is deliberately not awaited: it waits for Inventor to exit, which cannot happen while this
//   call is on the stack. The id is what lets the workflow tell "still applying" from "finished".
// Dependencies: System.Diagnostics.Process.
// Assumptions: CreateNoWindow is false and UseShellExecute is false on purpose - the console window is
//   the only progress and error report the user gets once Inventor has closed, so hiding it would hide
//   a failed update.
// Validation source: docs/decisions/0005-release-distribution-and-updater.md item 3.

using System.ComponentModel;
using System.Diagnostics;
using WmpToolsManager.Application;

namespace WmpToolsManager.Infrastructure;

public sealed class ProcessLauncher : IProcessLauncher
{
    public LaunchResult Launch(string fileName, string arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo startInfo = new(fileName)
        {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        try
        {
            using Process? process = Process.Start(startInfo);
            return process is null
                ? new LaunchResult(false, $"Windows did not start '{fileName}'.")
                : new LaunchResult(true, $"Started '{fileName}' (process {process.Id}).", process.Id);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return new LaunchResult(false, $"'{fileName}' could not be started: {exception.Message}");
        }
    }

    public bool IsProcessRunning(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // GetProcessById raises ArgumentException for an id that names nothing, which is the
            // ordinary answer for a marker left behind by an installer that already finished.
            // A process this session cannot open - HasExited raises Win32Exception "Access is denied"
            // for a protected system process, which a recycled pid can land on - is likewise not the
            // powershell.exe this add-in started, and must not block the user out of the dialog.
            return false;
        }
    }
}
