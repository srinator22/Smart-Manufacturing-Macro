// Purpose: Compose the update workflow over the real ports and resolve the two Windows roots it works
//   against, outside any file that references Inventor interop - Inventor declares its own
//   Environment and Path types, which would otherwise collide with the base-class-library ones here.
// Inputs: An HttpClient owned by the caller.
// Outputs: A configured UpdateWorkflow, plus the add-ins root and the state root it was built with.
// Dependencies: WmpToolsManager.Application, .Core and .Infrastructure.
// Assumptions: Both roots must equal the installer's own defaults, because the installer is the
//   process that applies what this add-in stages; a mismatch would stage into one folder and install
//   from another.
// Validation source: scripts/release/Install-WmpInventorTools.ps1 -AddinsRoot and -StateRoot defaults.

using System.IO;
using System.Net.Http;
using WmpToolsManager.Application;
using WmpToolsManager.Core;
using WmpToolsManager.Infrastructure;

namespace WmpToolsManager.AddIn;

public static class UpdateComposition
{
    /// <summary>The per-user Inventor 2027 add-ins folder, relative to %APPDATA%.</summary>
    public const string AddinsRootRelativePath = @"Autodesk\Inventor 2027\Addins";

    public static string DefaultAddinsRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AddinsRootRelativePath);

    public static StagingPaths DefaultPaths() => new(StagingPaths.DefaultStateRoot(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

    public static UpdateWorkflow Create(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        return new UpdateWorkflow(
            new GitHubReleaseSource(httpClient),
            new PhysicalInstallState(),
            new Sha256HashVerifier(),
            new ProcessLauncher(),
            DefaultPaths(),
            DefaultAddinsRoot());
    }
}
