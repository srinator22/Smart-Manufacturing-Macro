// Purpose: Start the PowerShell apply step out of process. Inventor locks its own add-in assemblies
//   while it runs, so the update can only be applied by a process that outlives the host.
// Inputs: An executable name and an argument string built by Core's ApplyCommandLine.
// Outputs: Whether the process started, and the message shown either way. The process is deliberately
//   not awaited: it waits for Inventor to exit, which cannot happen while this call is on the stack.
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
                : new LaunchResult(true, $"Started '{fileName}' (process {process.Id}).");
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return new LaunchResult(false, $"'{fileName}' could not be started: {exception.Message}");
        }
    }
}
