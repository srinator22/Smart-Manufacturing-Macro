# WMP Tools Manager

WMP Tools Manager `0.6.0` is a Windows add-in for Autodesk Inventor 2027. It adds one command - Check for updates - to the shared WMP Custom Tools ribbon tab. The command compares the installed version of the WMP Inventor Tools package with the newest release published on GitHub, shows what that release contains, and on your click downloads it, verifies its SHA-256, and hands it to the published PowerShell installer, which applies it after you close Inventor.

Maturity: **beta**. The command, the verification and the rollback path are covered by automated tests, but no live Inventor session has been recorded applying an update end to end. The ribbon tooltip carries the same `(beta)` marker, read from this project's `plugin.json`.

The distribution model, and why no executable ships, is [ADR-0005](../../docs/decisions/0005-release-distribution-and-updater.md). The module layout is [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## What it does

- Adds the Check for updates command to the shared WMP Custom Tools tab on the Inventor Assembly ribbon, panel WMP Tools. The tooltip ends in ` (beta)` while this plugin's `plugin.json` declares that maturity.
- Resolves the latest published version by downloading `releases/latest/download/SHA256SUMS.txt` from `srinator22/Smart-Manufacturing-Macro`. That URL needs no token and is not rate-limited, so repeated checks are never refused. The Releases API is used only for the release-notes body, and its failure degrades to "no release notes", never to a failed check.
- Reads the installed version from `%LOCALAPPDATA%\WMP\InventorTools\installed.json`, which the installer writes, and reports one of four states: up to date, update available, the installed build is ahead of the latest release, or unknown. An unreadable version on either side is reported as unknown - never as up to date.
- Shows both version numbers, the first 20 lines of the release notes, and a table of the plugins in the release with each one's maturity. Before a release is staged the table lists what `installed.json` records; afterwards it lists what the staged release's `catalog.json` declares.
- On Download and install: creates `%LOCALAPPDATA%\WMP\InventorTools\staging\<version>\`, writes the digest file it checked against, downloads the package zip and `Install-WmpInventorTools.ps1`, and verifies both against that digest file. A mismatch is reported with the expected and actual digests, the file is left in place for inspection, and nothing is started.
- Once both artifacts verify, starts `powershell.exe` with the staged installer, `-WaitForInventor`, and the add-ins and state roots, then tells you to close Inventor. The installer waits for every Inventor process to exit, archives the current install under `previous\`, copies the new one into place, and writes manifests for any plugin in the release that is not yet installed.
- Offers Roll back to previous only when `%LOCALAPPDATA%\WMP\InventorTools\previous\` actually holds an archived install; that button runs the same installer with `-Rollback -WaitForInventor`.
- Shows every failure - network, digest, filesystem, process - as text in the window. Nothing is ever deleted, and the add-in never writes into Inventor's add-ins folder itself.

Updates are never silent. There is no startup check and no timer: every step above happens because you pressed something.

## Requirements

- Windows 11 x64
- Autodesk Inventor 2027
- The .NET 10 Desktop runtime (x64) on any machine that runs the installed add-in; the installer detects it and points at Microsoft's download rather than bundling it
- Windows PowerShell 5.1 or later, which is in-box on Windows 11, for the install and apply steps
- Network access to `github.com` and `api.github.com`

To build from source, additionally:

- .NET SDK 10.0.401, pinned by `global.json`
- Visual Studio Community 2022 17.14 with the .NET desktop development workload for Autodesk's Inventor 2027 templates and interactive debugging
- Autodesk Inventor 2027 Developer Tools 19.0.0 from `C:\Users\Public\Documents\Autodesk\Inventor 2027\SDK\developertools.msi`
- `gitleaks` 8.30.1 and `git-cliff` 2.14.1 for the full local gauntlet

## Install

The supported path installs every WMP plugin at once, from the published release. Close Inventor, then run in PowerShell:

```powershell
irm https://github.com/srinator22/Smart-Manufacturing-Macro/releases/latest/download/Install-WmpInventorTools.ps1 | iex
```

The script runs in memory, downloads the package, verifies its SHA-256, removes the Mark-of-the-Web, extracts into `%APPDATA%\Autodesk\Inventor 2027\Addins\`, and writes one `.addin` manifest per plugin. Nothing is launched from the download, so SmartScreen shows no prompt. Integrity comes from HTTPS to the pinned repository plus the published digest; there is no code-signing certificate, which ADR-0005 accepts for three internal users and requires revisiting before any wider distribution.

To install this one add-in from a working tree instead, close Inventor and run from the repository root:

```powershell
dotnet restore InventorScripts.sln --locked-mode
dotnet build InventorScripts.sln -c Release --no-restore
pwsh -NoProfile -File projects/wmp-tools-manager/scripts/install-addin.ps1 -Configuration Release
```

That installer is per-user, needs no administrator privileges, and writes only these product-owned targets:

```text
%APPDATA%\Autodesk\Inventor 2027\Addins\WmpToolsManager\
%APPDATA%\Autodesk\Inventor 2027\Addins\Autodesk.WmpToolsManager.Inventor.addin
```

A working-tree install writes no `installed.json`, so Check for updates will report the installed version as unknown and say why.

## Verify installation

Before starting Inventor, confirm these files exist:

```text
%APPDATA%\Autodesk\Inventor 2027\Addins\WmpToolsManager\WmpToolsManager.AddIn.dll
%APPDATA%\Autodesk\Inventor 2027\Addins\Autodesk.WmpToolsManager.Inventor.addin
```

Then:

1. Start Autodesk Inventor 2027.
2. Open Inventor's Add-In Manager and confirm WMP Tools Manager is loaded.
3. Open any assembly.
4. Confirm the WMP Custom Tools tab, the WMP Tools panel, and the Check for updates command appear in the Assembly ribbon. Smart Manufacturing Exporter and File Naming Manager, if installed, share the same tab with their own panels.
5. Hover the command and confirm the tooltip ends in `(beta)`.

## Use

1. Select Check for updates from the WMP Tools panel. The window opens and checks immediately.
2. Read the summary line. It names both versions, so a screenshot of the window is enough to diagnose a wrong answer.
3. If an update is available, read the release notes excerpt and the plugin table. The Maturity column repeats the wording the installer prints; a plugin marked `beta - not yet validated in live Inventor` has not been proven in a live session.
4. Select Download and install. The package and the installer script are downloaded into `%LOCALAPPDATA%\WMP\InventorTools\staging\<version>\` and both are verified against the published digests. Any problem appears under Problems, in red, and nothing is started.
5. On success the window says `Close Inventor to finish; the update applies automatically.` and a PowerShell window appears. Leave it open.
6. Close Inventor. The PowerShell window archives the current install, copies the new one into place, prints the installed version and the plugin table, and prints the rollback command.
7. Start Inventor and verify the commands as in Verify installation above.

To undo an update, reopen Check for updates and select Roll back to previous, then close Inventor again. The button is present only when an archived install exists.

## Update

This add-in updates itself along with every other WMP plugin, through the command above: the release zip is the unit of update for the whole package (ADR-0005 item 1).

If the add-in itself cannot run - it fails to load, or the window will not open - update from PowerShell instead. Close Inventor and run:

```powershell
irm https://github.com/srinator22/Smart-Manufacturing-Macro/releases/latest/download/Install-WmpInventorTools.ps1 | iex
```

To roll back without the add-in, close Inventor and run:

```powershell
& ([scriptblock]::Create((irm https://github.com/srinator22/Smart-Manufacturing-Macro/releases/latest/download/Install-WmpInventorTools.ps1))) -Rollback
```

## Uninstall

Close Inventor, then run:

```powershell
pwsh -NoProfile -File projects/wmp-tools-manager/scripts/uninstall-addin.ps1
```

The script validates and removes only this add-in's per-user manifest and binary directory. It does not remove Autodesk components, the other WMP plugins, the state root under `%LOCALAPPDATA%\WMP\InventorTools`, or any archived install. Delete the state root by hand if you want the staged downloads and the rollback archive gone; nothing else reads it.

## Troubleshooting

| Symptom | Check | Resolution |
| --- | --- | --- |
| Add-in is absent from the Add-In Manager | Confirm the two paths in Verify installation exist and the manifest points to the installed DLL. | Close Inventor, reinstall, and restart Inventor 2027. |
| WMP Custom Tools tab is missing | Confirm WMP Tools Manager is loaded and a document is open. | Restart Inventor after the first installation. |
| Installed version shows as unknown | The window names the file it expected: `%LOCALAPPDATA%\WMP\InventorTools\installed.json`. | This is expected after a working-tree install. Install from the published release once to create it. |
| The check reports the state could not be determined | Read the Problems list. A network, DNS or proxy failure names the URL and the 10 second ceiling. | Confirm access to `github.com`, then select Check again. |
| Release notes are missing but the check worked | The Releases API is rate-limited to 60 unauthenticated calls per hour per IP. | Harmless: the version comparison does not use the API. Wait and check again, or read the notes on GitHub. |
| SHA-256 mismatch | The message names the expected and actual digests and the file it kept. | Do not use that file. Select Check again to download it afresh; if it repeats, report it with both digests - the download did not match what the release published. |
| Download and install is disabled | The summary line says why: already up to date, the installed build is newer, the state is unknown, or this release is already staged. | Nothing to do unless the state is unknown, which the Problems list explains. |
| Nothing happened after closing Inventor | The PowerShell window must stay open; it is what applies the update. | Reopen the command and stage again, or run the one-line installer from the Update section. |
| Roll back to previous is not shown | `%LOCALAPPDATA%\WMP\InventorTools\previous\` holds no archived install with content. | Expected before the first update applied through this add-in. |
| Roll back says no installer was found | The rollback runs `Install-WmpInventorTools.ps1`, which every install keeps at `%LOCALAPPDATA%\WMP\InventorTools\Install-WmpInventorTools.ps1`; this is missing only if that file was deleted by hand. | Use the PowerShell rollback command in the Update section. |
| Updated DLL cannot be copied or loaded | Inventor may still hold the assembly open. | Close every Inventor process and let the waiting PowerShell window continue, or rerun the installer. |

When reporting a problem, include the Inventor 2027 display version, the two version numbers the window shows, the exact text under Problems, and the contents of `%LOCALAPPDATA%\WMP\InventorTools\installed.json`. Do not attach proprietary CAD.

## Versioning and changelog

The workspace currently uses one semantic version for all Inventor projects before `1.0`:

- [Directory.Build.props](../../Directory.Build.props) contains the authoritative `VersionPrefix`.
- [CHANGELOG.md](../../CHANGELOG.md) is generated from Conventional Commits and is never hand-edited.
- The add-in activation manifest version is mechanically checked against `VersionPrefix`.
- Annotated release tags use `vX.Y.Z`, and this add-in only recognises that exact form; a tag in any other shape is refused rather than compared.

Independent per-plugin version numbers and changelogs are not active yet. They require an architecture decision once multiple plugins genuinely need separate release schedules. Until then, this README links to the workspace changelog and states which workspace version it documents.

## Known limitations

- No live Inventor acceptance is claimed. The ribbon button appearing on the shared tab, and an apply that really replaces the installed files, are unverified by any automated test here.
- The whole package updates together. There is no way to update one plugin and hold another back; per-plugin release trains require their own ADR (ADR-0004).
- There is no startup check and no notification. You only learn a release exists by opening the command.
- Integrity rests on HTTPS to the pinned repository plus the published SHA-256. Nothing is code-signed, so a compromised repository or release would not be detected by this add-in.
- Rollback restores the most recently archived install only. It runs `Install-WmpInventorTools.ps1`, which every install keeps at `%LOCALAPPDATA%\WMP\InventorTools\Install-WmpInventorTools.ps1`. Older archives under `previous\` must be restored by hand.
- The add-in never deletes anything. Staged downloads, superseded packages and archived installs accumulate under `%LOCALAPPDATA%\WMP\InventorTools` until you remove them.
- The release-notes excerpt is the first 20 lines of the body, shown as plain text; Markdown is not rendered.
- The check needs direct HTTPS access. A proxy requiring authentication is not configured anywhere and will surface as a network failure.

## Development and verification

Run these commands from the repository root. From PowerShell or Command Prompt:

```bat
scripts\check.cmd
```

From Git Bash:

```bash
./scripts/check.sh
```

The gauntlet restores only from committed lockfiles, checks formatting and analyzers, compiles with warnings as errors, enforces product and cross-project architecture tests, runs unit tests, scans Git history and the working tree for secrets, builds Release output, runs the product-owned mutation suite when production logic changes, and verifies the generated changelog. This product's mutation suite enforces Core at 85%, Application at 85%, and Infrastructure at 70%, against measured scores of 92.05% / 92.44% / 77.46% on 2026-09-23; `scripts/mutation.sh` records why Infrastructure sits lower and which mutants remain.

Product-local checks:

```bash
bash projects/wmp-tools-manager/scripts/check.sh
bash projects/wmp-tools-manager/scripts/mutation.sh
```

Individual development commands:

```powershell
dotnet restore InventorScripts.sln --locked-mode
dotnet build InventorScripts.sln -c Release --no-restore
dotnet test InventorScripts.sln -c Release --no-build --no-restore
```

The update window is covered by a render test that shows it on an STA thread and asserts the realized plugin rows, the rendered maturity text and zero WPF binding errors. The workflow is covered by fakes for all four ports; the adapters are covered against real temporary folders, a stub `HttpMessageHandler` and one real process, so no test needs a network or a published release.

Architecture, the module dependency direction, the check/stage/apply flow, and why the digest file rather than the Releases API answers the version question are documented in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The distribution decision is [ADR-0005](../../docs/decisions/0005-release-distribution-and-updater.md).
