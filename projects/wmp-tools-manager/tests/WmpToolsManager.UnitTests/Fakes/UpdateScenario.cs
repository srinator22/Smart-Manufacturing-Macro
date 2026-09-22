// One place to build a workflow over the fakes, so the view-model and render suites describe the same
// published release the workflow suite does.

using WmpToolsManager.Application;
using WmpToolsManager.Core;

namespace WmpToolsManager.UnitTests.Fakes;

public sealed class UpdateScenario
{
    public const string StateRoot = @"C:\Users\Tester\AppData\Local\WMP\InventorTools";
    public const string AddinsRoot = @"C:\Users\Tester\AppData\Roaming\Autodesk\Inventor 2027\Addins";
    public const string PackageName = "WmpInventorTools-0.6.0.zip";
    public const string PackageDigest = "9f2c4a1b7d8e6f30a1b2c3d4e5f60718293a4b5c6d7e8f901a2b3c4d5e6f7081";
    public const string InstallerDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    public const string PublishedSums =
        PackageDigest + "  " + PackageName + "\n" +
        InstallerDigest + "  Install-WmpInventorTools.ps1\n";

    public const string InstalledJson = """
        {
          "version": "0.5.0",
          "installedUtc": "2026-09-20T08:00:00Z",
          "plugins": [ { "id": "file-naming-manager", "maturity": "beta", "installDirectory": "FileNamingManager", "manifestName": "a.addin" } ]
        }
        """;

    public const string CatalogJson = """
        {
          "version": "0.6.0",
          "plugins": [
            { "id": "file-naming-manager", "displayName": "File Naming Manager", "description": "Naming.", "maturity": "beta" },
            { "id": "wmp-tools-manager", "displayName": "WMP Tools Manager", "description": "Updates.", "maturity": "beta" }
          ]
        }
        """;

    public FakeReleaseSource Source { get; } = new();

    public FakeInstallState InstallState { get; } = new();

    public FakeHashVerifier HashVerifier { get; } = new();

    public RecordingProcessLauncher Launcher { get; } = new();

    /// <summary>A release 0.6.0 is published, 0.5.0 is installed, and both downloads verify.</summary>
    public static UpdateScenario WithAvailableUpdate()
    {
        UpdateScenario scenario = new();
        scenario.Source.Sums = new(PublishedSums, null);
        scenario.Source.Payload = new(
            """{ "tag_name": "v0.6.0", "body": "### Added\n- The update command." }""",
            null);
        scenario.InstallState.Installed = ReleaseJson.ParseInstalledState(InstalledJson);
        scenario.InstallState.Catalog = ReleaseJson.ParseCatalog(CatalogJson);
        scenario.HashVerifier.Results[PackageName] = new(PackageDigest, null);
        scenario.HashVerifier.Results["Install-WmpInventorTools.ps1"] = new(InstallerDigest, null);
        return scenario;
    }

    /// <summary>
    /// Marks an apply as already launched and still running, which is what a user who pressed Download
    /// and install, left Inventor open, and reopened the dialog would find.
    /// </summary>
    public UpdateScenario WithPendingApply(int pid, string version, string kind)
    {
        InstallState.Pending = new(new PendingApply(pid, version, kind, DateTimeOffset.UnixEpoch), null);
        Launcher.RunningPids.Add(pid);
        return this;
    }

    /// <summary>A marker whose process is gone, which is what a finished or failed installer leaves.</summary>
    public UpdateScenario WithStalePendingApply(int pid, string version, string kind)
    {
        InstallState.Pending = new(new PendingApply(pid, version, kind, DateTimeOffset.UnixEpoch), null);
        return this;
    }

    public UpdateWorkflow Build() =>
        new(Source, InstallState, HashVerifier, Launcher, new StagingPaths(StateRoot), AddinsRoot);
}
