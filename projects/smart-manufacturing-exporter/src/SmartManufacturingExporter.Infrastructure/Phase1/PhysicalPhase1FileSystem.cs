// Purpose: Provide conservative, side-effect-free physical filesystem observations for Phase 1 planning.
// Inputs: User-selected directory and planned output paths.
// Outputs: Existence and coarse writability observations through IPhase1FileSystem.
// Dependencies: System.IO, Microsoft.Win32.SafeHandles, and the Windows CreateFile API.
// Assumptions: The product runs on Windows; filesystem races remain possible and are handled at export time.
// Validation source: SmartExportWorkflow destination and overwrite safety tests.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SmartManufacturingExporter.Application.Phase1;

namespace SmartManufacturingExporter.Infrastructure.Phase1;

public sealed partial class PhysicalPhase1FileSystem : IPhase1FileSystem
{
    private const uint FileAddFile = 0x00000002;
    private const uint FileFlagBackupSemantics = 0x02000000;

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool CanWriteToDirectory(string path)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            using SafeFileHandle directoryHandle = CreateFile(
                path,
                FileAddFile,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagBackupSemantics,
                IntPtr.Zero);
            // Opening the directory with FILE_ADD_FILE checks effective access without creating a probe file.
            return !directoryHandle.IsInvalid;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool FileExists(string path) => File.Exists(path);

    [LibraryImport(
        "kernel32.dll",
        EntryPoint = "CreateFileW",
        SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
}
