# Architecture

## Scope

WMP Tools Manager is an in-process Autodesk Inventor 2027 add-in whose only job is the Update command that ADR-0005 defines. It asks GitHub what the latest published release is, compares that with the version recorded by the installer, shows the release notes and the plugin table, and - on the user's click - downloads the package into `%LOCALAPPDATA%\WMP\InventorTools\staging\`, verifies its SHA-256, and hands it to the published PowerShell installer, which waits for every Inventor process to exit before applying it.

It reads no document, drives no modelling API, and writes nothing into Inventor's add-ins folder. Inventor locks its own add-in assemblies while it runs, so the only actor that may write there is a process that outlives Inventor.

Inventor 2027 is the only host. The solution targets C# 14, .NET 10, WPF, x64. The installed `Autodesk.Inventor.Interop.dll` major version is 31.

## Modules and dependency direction

```text
WmpToolsManager.AddIn
  -> WmpToolsManager.UI
  -> WmpToolsManager.Infrastructure
  -> WmpToolsManager.Application
       -> WmpToolsManager.Core
  -> shared/WmpRibbon (ribbon tab only)

GitHub HTTPS  -> Infrastructure -> Application ports -> Core models
Filesystem    -> Infrastructure -> Application ports -> Core models
powershell.exe-> Infrastructure -> Application ports -> Core models
WPF           -> UI             -> Application workflow -> Core models
```

- `Core` owns the value types and the arithmetic: `SemanticVersion` (plain and `v`-tagged forms), `Sha256SumsFile` (the release digest file, which is also the version source), `UpdateDecider`, the `catalog.json` / `installed.json` / `plugin.json` records and their parsing, `PendingApply` (the marker one launched apply leaves behind, and its wording), `StagingPaths` (every path the updater may touch, and the refusal for anything outside the state root), `ReleaseNotes` (the excerpt), `Maturity` (the tooltip suffix and the table wording), and `ApplyCommandLine` (the exact `powershell.exe` argument string). It performs no IO and opens no socket; `System.IO.Path` is used for string arithmetic only.
- `Application` owns `UpdateWorkflow` (check, stage, apply, roll back) and defines the ports `IReleaseSource`, `IInstallState`, `IHashVerifier` and `IProcessLauncher`. Every port returns a result record: an update check runs inside an Inventor command callback, where an escaping exception can take the host down, so "the network was unreachable" has to be a value the dialog can render.
- `Infrastructure` implements those ports: `GitHubReleaseSource` over `HttpClient`, `Sha256HashVerifier`, `PhysicalInstallState` (installed.json, the catalog inside the package zip, the archived previous install, the staged installer script, the pending-apply marker), and `ProcessLauncher` (which reports the started process id and answers whether a recorded id is still running).
- `UI` is the WPF window and view model over COM-free models.
- `AddIn` owns the `ApplicationAddInServer`, the one ribbon button on the shared `WMP Custom Tools` tab, and composition. It is the only project that references Inventor interop; there is no `InventorAdapter`, because nothing here touches a document.

`WmpToolsManager.ArchitectureTests` parses project references and fails when this direction changes.

## Why SHA256SUMS.txt answers the version question

ADR-0005 item 3 describes querying the Releases API. The add-in instead resolves the latest version from `releases/latest/download/SHA256SUMS.txt`:

- That URL is a redirect to the newest release's asset. It needs no token and is not rate-limited, so a user who presses Check for updates repeatedly can never be refused.
- The unauthenticated Releases API allows 60 calls per hour per IP. It is still used, for the release-notes body alone, and its failure degrades to "no release notes", never to a failed check.
- The digest file is the artifact the install must be verified against anyway, and `scripts/release/build-release.ps1` derives the package name `WmpInventorTools-<version>.zip` from `Directory.Build.props` `VersionPrefix`. Reading the version from the name of the file whose digest is about to be checked means the version compared and the bytes verified come from one document.

## Check, stage, apply

1. `CheckForUpdateAsync` reads `installed.json` from the state root, downloads `SHA256SUMS.txt`, parses the package name into a version, and decides: `UpToDate`, `UpdateAvailable`, `InstalledNewer`, or `Unknown`. An unreadable version on either side is `Unknown`, never "up to date". It then fetches the release payload for the notes excerpt and lists the installed plugins with their maturity.
2. `StageUpdateAsync` creates `staging\<version>\`, writes the digest file it already read, downloads the package and the installer script, and verifies both against that digest file. A mismatch is reported with both digests and the file is left where it is - nothing is deleted, so a rejected download can be inspected. The staged package's `catalog.json` is then read without extracting the package, which is what fills the dialog's plugin table.
3. `LaunchApply` starts `powershell.exe -NoProfile -ExecutionPolicy Bypass -File <staged installer> -ZipPath ... -Sha256SumsPath ... -WaitForInventor -AddinsRoot ... -StateRoot ...` and returns. The process is deliberately not awaited: it waits for Inventor to exit, which cannot happen while the call is on the stack. Its console window is left visible, because once Inventor has closed it is the only progress and error report the user gets. The started process id is then written to `<state root>\pending-apply.json`.
4. `RollbackToPrevious` runs the same installer with `-Rollback -WaitForInventor`. It is offered only when `<state root>\previous\` holds an archived install with content, mirroring the installer's own `Get-NewestPreviousInstall`, because `-Rollback` fails outright when it does not. The script it runs is the copy every install keeps at `%LOCALAPPDATA%\WMP\InventorTools\Install-WmpInventorTools.ps1`; only an install made before that copy was introduced falls back to the newest copy an update check staged, and when neither exists the dialog says so and points at the one-line PowerShell command in the README instead of failing silently.

Nothing here runs on a timer. There is no startup check and no silent update: every transition above happens because the user pressed something.

## One apply at a time

The launched installer waits for every Inventor process to exit, which can take as long as the user keeps Inventor open. The dialog can be closed and reopened in that window, so without a guard a second Download and install - or a Roll back - would start a second `powershell.exe` rewriting the add-ins root alongside the first.

Every successful launch therefore writes `<state root>\pending-apply.json`: the started process id, the version, whether it is an `update` or a `rollback`, and the launch timestamp. `GetActivePendingApply` reads that file and returns the record only when `IProcessLauncher.IsProcessRunning` says the id is still alive. While one is active, `StageUpdateAsync`, `LaunchApply` and `RollbackToPrevious` all refuse with the sentence naming the version and the process, and the dialog disables Download and install and Roll back while leaving Check again enabled so the state can be re-evaluated.

The marker is advisory, not a lock, and it is consistent with this project's rule that nothing is ever deleted:

- A finished or failed installer leaves its marker behind. Its process is gone, so the marker is stale, nothing is blocked, and the next launch overwrites the file.
- A marker that cannot be parsed never blocks either. It is reported under Problems with its path, because refusing every action over a file the user cannot be expected to find would be worse than the concurrent apply the guard exists to prevent.
- A process id this session cannot open - `HasExited` raises `Win32Exception` for a protected process, which a recycled id can land on - is treated as not running, for the same reason.
- A launch whose marker could not be written still succeeded, so it is reported as started with the sentence saying the guard is not in place, never as a failed launch.

This is not cross-machine mutual exclusion and does not claim to be. It closes the one window the review found: this dialog starting a second apply on top of its own.

## Path safety

`StagingPaths` owns `pending-apply.json` alongside `installed.json`, `staging\` and `previous\`. It is constructed from an absolute state root and refuses a relative one. Every composed path is re-checked against that root before it is used, and an artifact name taken from the downloaded digest file is required to be a bare file name - a name carrying a separator, a rooted path, or `..` is refused rather than resolved. The add-ins root is passed to the installer as an argument and is never written to by this process.

## Threading and COM lifetime

The ribbon callback runs on Inventor's owning STA thread. The command opens one modal window and returns; the window's own work is asynchronous but stays on the dispatcher thread. The single `HttpClient` is created on activation and disposed on deactivation, so a repeated check does not exhaust sockets. Inventor interop types exist only in `AddIn`.

## Verification boundaries

- Core, Application and Infrastructure: deterministic unit tests with fake ports and, for the adapters, real temporary folders, a stub `HttpMessageHandler`, and one process that actually starts; plus mutation tests with enforced thresholds (`scripts/mutation.sh` records the measured scores and the residual equivalent mutants).
- UI: a render test that shows the window on an STA thread and asserts realized `DataGridRow` containers, the rendered plugin and maturity text, and zero WPF binding errors, because a view-model-only suite cannot see XAML.
- AddIn: compiles against the installed interop. Its live behaviour - the ribbon button appearing on the shared tab, and an apply that really replaces the installed files - is not claimed by any automated test here.
- Project boundaries, the ClientId, the activation manifest version and the plugin catalog entry: architecture tests.
