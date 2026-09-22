# Smart Manufacturing Exporter

Smart Manufacturing Exporter `0.5.0` is a Windows add-in for Autodesk Inventor 2027. It presents a recursive assembly tree and exports an explicit selection of unique part and assembly documents as STEP files with a chosen spline-fit precision.

Phases 1 and 2 are ready for live Inventor testing. Compilation, automated tests, and packaging are verified by the repository gate; live three-level hierarchy and assembly-export acceptance remain unclaimed until exercised in Inventor 2027.

## What it does

- Adds a Smart Export command to the shared WMP Custom Tools tab on the Inventor Assembly ribbon.
- Requires a saved assembly to be the active document.
- Recursively scans saved Inventor part and assembly occurrences.
- Excludes suppressed occurrences and their descendants, unresolved documents, and unsupported document types.
- Preserves occurrence hierarchy while deduplicating export source paths case-insensitively and showing total quantity. Selection is per source document: every occurrence of the same document shares one checkbox state, so checking or unchecking any occurrence applies to all of them and the document exports exactly once.
- Provides Top Level Only, Parts Recursive, Assemblies Only, and Assemblies And Parts scopes.
- Uses a tri-state tree: parent changes propagate downward and child changes update ancestors.
- Selects all eligible documents by default and provides Select All, Select None, Expand All, and Collapse All controls.
- Provides Low, Medium, and Highest STEP spline-fit precision presets.
- Exports each checked `.ipt` or `.iam` source document once through Inventor 2027's installed STEP translator.
- Rejects unwritable destinations and existing output conflicts.
- Atomically finalizes new `.step` files without overwriting another file.
- Reports per-item success and failure while continuing with later selected items.

The add-in never silently saves, renames, moves, or updates source Inventor documents.

## Target and safety boundary

- Autodesk Inventor 2027 only
- C# 14 and .NET 10, x64
- WPF presentation layer
- Source Inventor documents are never silently saved, renamed, moved, or modified
- Conflicting output files are never silently overwritten
- Inventor API behavior is implemented only after verification against Autodesk documentation or the installed Inventor 2027 interop assembly

The verified Phase 1 API surface and remaining live-host uncertainties are recorded in [docs/INVENTOR_2027_API_COMPATIBILITY.md](docs/INVENTOR_2027_API_COMPATIBILITY.md).

The complete development requirements are in [docs/PRODUCT_SPEC.md](docs/PRODUCT_SPEC.md). Product architecture is in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and workspace decisions are in [../../docs/decisions](../../docs/decisions).

## Solution structure

- `SmartManufacturingExporter.Core` - typed domain models and pure selection, naming, validation, and rule logic
- `SmartManufacturingExporter.Application` - use cases and ports defined toward the core
- `SmartManufacturingExporter.Infrastructure` - local JSON settings, logs, filesystems, and other non-Inventor adapters
- `SmartManufacturingExporter.InventorAdapter` - Inventor 2027 COM and translator integration
- `SmartManufacturingExporter.UI` - WPF views and view models over COM-free application contracts
- `SmartManufacturingExporter.AddIn` - add-in server, ribbon commands, and composition root
- `SmartManufacturingExporter.UnitTests` - deterministic tests for COM-free behavior
- `SmartManufacturingExporter.ArchitectureTests` - mechanical enforcement of project dependency direction

## Requirements

- Windows 11 x64
- Autodesk Inventor 2027
- .NET SDK 10.0.401, pinned by `global.json`
- Visual Studio Community 2022 17.14 with the .NET desktop development workload for Autodesk's Inventor 2027 templates and interactive debugging
- Autodesk Inventor 2027 Developer Tools 19.0.0 from `C:\Users\Public\Documents\Autodesk\Inventor 2027\SDK\developertools.msi`
- Visual Studio Community 2026 may coexist, but Autodesk's Inventor 2027 Developer Tools installer does not detect it as a replacement for Visual Studio 2022
- `gitleaks` 8.30.1 and `git-cliff` 2.14.1 for the full local gauntlet

## Development and verification

Run these commands from the repository root. From PowerShell or Command Prompt:

```bat
scripts\check.cmd
```

From Git Bash:

```bash
./scripts/check.sh
```

The gauntlet restores only from committed lockfiles, checks formatting and analyzers, compiles with warnings as errors, enforces product and cross-project architecture tests, runs unit tests, scans Git history and the working tree for secrets, builds Release output, runs the product-owned mutation suite when production logic changes, and verifies the generated changelog.

Individual development commands:

```powershell
dotnet restore InventorScripts.sln --locked-mode
dotnet build InventorScripts.sln -c Release --no-restore
dotnet test InventorScripts.sln -c Release --no-build --no-restore
```

## Install

Close Inventor before replacing add-in files. From the repository root in PowerShell, restore, build, and install the Release configuration:

```powershell
dotnet restore InventorScripts.sln --locked-mode
dotnet build InventorScripts.sln -c Release --no-restore
pwsh -NoProfile -File projects/smart-manufacturing-exporter/scripts/install-addin.ps1 -Configuration Release
```

The installer is per-user and does not require administrator privileges. It writes only these product-owned targets:

```text
%APPDATA%\Autodesk\Inventor 2027\Addins\SmartManufacturingExporter\
%APPDATA%\Autodesk\Inventor 2027\Addins\Autodesk.SmartManufacturingExporter.Inventor.addin
```

For an active debugging session, build and install `Debug` instead of `Release`.

## Verify installation

Before starting Inventor, confirm these files exist:

```text
%APPDATA%\Autodesk\Inventor 2027\Addins\SmartManufacturingExporter\SmartManufacturingExporter.AddIn.dll
%APPDATA%\Autodesk\Inventor 2027\Addins\Autodesk.SmartManufacturingExporter.Inventor.addin
```

Then:

1. Start Autodesk Inventor 2027.
2. Open Inventor's Add-In Manager and confirm Smart Manufacturing Exporter is loaded.
3. Open a saved `.iam` assembly.
4. Confirm the WMP Custom Tools tab and Smart Export command appear in the Assembly ribbon.

If Inventor reports a load error or the command does not appear, stop and use the troubleshooting table below before testing exports.

## Use

For the first test, use sanitized CAD files outside the Git repository and choose a new empty output folder.

1. Open a saved Inventor `.iam` assembly and make it the active document.
2. Open the Smart Export ribbon tab and select Smart Export.
3. Choose an inclusion scope and review the recursive assembly tree. Quantity is the total number of occurrences that reference the same source document, and checking or unchecking any one occurrence checks or unchecks every occurrence of that document.
4. Expand or collapse the hierarchy as needed. Use Select None and then check only the documents you want, or leave all eligible documents selected. A shaded checkbox means only part of that branch is selected.
5. Choose STEP precision. Low preserves Inventor's documented default; Medium and Highest use progressively tighter spline-fit tolerances.
6. Select Browse and choose an existing writable destination directory.
7. Select Export STEP.
8. Read the status message at the bottom of the window. It reports the successful and failed export counts and identifies individual failures.
9. Confirm the destination contains one `<source-filename>.step` file for each successful unique selected source document.

Inclusion scopes:

| Scope | Eligible documents |
| --- | --- |
| Top Level Only | Saved `.ipt` parts directly under the active assembly |
| Parts Recursive | Saved `.ipt` parts at every scanned depth |
| Assemblies Only | The active `.iam` root and saved descendant `.iam` assemblies |
| Assemblies And Parts | The active root and every saved descendant `.iam` and `.ipt` document |

STEP precision presets:

| Preset | Translator tolerance | Practical use |
| --- | --- | --- |
| Low | `0.001 cm` (`0.01 mm`) | Inventor's documented default and the smallest typical file size |
| Medium | `0.0001 cm` (`0.001 mm`) | Finer spline approximation when downstream inspection needs it |
| Highest | `0.00001 cm` (`0.0001 mm`) | Tightest documented tolerance, with potentially larger files and longer translation time |

This setting controls the STEP translator's spline-fit tolerance. It is not an STL-style mesh resolution control, and it does not make every kind of exact analytic geometry more accurate. Use Low unless a downstream system or inspection comparison demonstrates that a tighter spline approximation is needed.

Expected safety behavior:

- A part document or no active document shows `Smart Export requires an active Inventor assembly.`
- An unsaved active assembly is rejected before scanning.
- An existing destination filename blocks the plan instead of being overwritten.
- A conflict that appears during export causes that item to fail without replacing the conflicting file.
- A failure for one selected document does not prevent later selected documents from being attempted.

The formal five-part first-test procedure and evidence checklist are in [docs/PHASE1_TEST_PLAN.md](docs/PHASE1_TEST_PLAN.md). Recursive hierarchy, scope, tri-state, and `.iam` export checks are in [docs/PHASE2_TEST_PLAN.md](docs/PHASE2_TEST_PLAN.md).

## Debugging in Inventor

1. Open `InventorScripts.sln` in Visual Studio Community 2022.
2. Build the Debug x64 configuration and run the install command above.
3. Set `SmartManufacturingExporter.AddIn` as the startup project.
4. Configure the startup action to launch `C:\Program Files\Autodesk\Inventor 2027\Bin\Inventor.exe`.
5. Place breakpoints in the add-in server, command, or Inventor adapter and start debugging.

Inventor API calls and STEP translation remain on Inventor's owning STA thread. The COM-free workflow and view model can be debugged without starting Inventor.

## Update

1. Close Inventor 2027.
2. Pull or check out the desired tested revision.
3. Run the locked restore and build commands from the Install section.
4. Run `install-addin.ps1` again with the same configuration.
5. Restart Inventor and verify the add-in and command before exporting.

The installer replaces only this add-in's per-user binary directory and manifest. It does not modify machine-wide registration or another Inventor add-in.

## Uninstall

Close Inventor, then run:

```powershell
pwsh -NoProfile -File projects/smart-manufacturing-exporter/scripts/uninstall-addin.ps1
```

The script validates and removes only this add-in's per-user manifest and binary directory. It does not remove Autodesk components, source code, or exported files.

## Troubleshooting

| Symptom | Check | Resolution |
| --- | --- | --- |
| Add-in is absent from the Add-In Manager | Confirm the two paths in Verify installation exist and the manifest points to the installed DLL. | Close Inventor, rebuild, rerun the installer, and restart Inventor 2027. |
| Inventor reports an add-in load error | Confirm this is Inventor 2027 and the `net10.0-windows` x64 build completed without errors. | Run `scripts\check.cmd`, reinstall the matching Release or Debug output, and retain the exact load message if it persists. |
| WMP Custom Tools tab is missing | Confirm Smart Manufacturing Exporter is loaded and a saved assembly is active. | Activate a `.iam` document. Restart Inventor after the first installation if needed. |
| The tree is empty or missing expected content | The chosen scope may exclude that document kind or depth; suppressed branches are intentionally skipped. | Check the scope, suppression state, and whether each source document is saved and resolved. |
| Destination is rejected | The folder must already exist and grant permission to add files. | Create or choose a writable folder owned by the current user. |
| Export is blocked by an existing file | Phase 1 never overwrites an existing `.step`. | Choose a new empty destination or manually move the old output after confirming it is safe to do so. |
| One item reports a STEP translator failure | Confirm Autodesk's STEP translator is installed and available in Inventor 2027. | Retry with one sanitized part or assembly and retain the complete error message for diagnosis. |
| Highest precision takes longer or produces a larger file | A tighter spline-fit tolerance can increase translation work and STEP size. | Retry with Medium or Low unless the downstream workflow requires Highest. |
| Updated DLL cannot be copied or loaded | Inventor may still hold the assembly open. | Close every Inventor process, rerun the installer, and restart Inventor. |

When reporting a problem, include the Inventor 2027 display version, add-in version, active document type, exact status or load message, and whether the same part exports through Inventor's built-in STEP command. Do not attach proprietary CAD.

## Versioning and changelog

The workspace currently uses one semantic version for all Inventor projects before `1.0`:

- [Directory.Build.props](../../Directory.Build.props) contains the authoritative `VersionPrefix`.
- [CHANGELOG.md](../../CHANGELOG.md) is generated from Conventional Commits and is never hand-edited.
- The add-in activation manifest version is mechanically checked against `VersionPrefix`.
- Annotated release tags use `vX.Y.Z`.

Independent per-plugin version numbers and changelogs are not active yet. They require an architecture decision once multiple plugins genuinely need separate release schedules. Until then, every plugin README links to the workspace changelog and states which workspace version it documents.

## Known limitations

- The checklist does not yet display detailed exclusion notices.
- Existing STEP files are blocking conflicts and are never silently overwritten.
- STEP application protocol selection is not exposed yet; the installed Inventor 2027 translator's protocol default remains in effect.
- Precision presets control spline-fit tolerance only, not tessellation or every exact analytic surface.
- Browser folders, classifications, smart rules, DXF, PDF, presets, and quick export remain later phases.
- The live five-part and three-level hierarchy acceptance results are not claimed until exercised in Inventor 2027; the first procedure is in the [Phase 1 test plan](docs/PHASE1_TEST_PLAN.md).
- A one-click installer and GitHub Release package are not published before live acceptance passes.

### Development workflow

Changes use short branches and pull requests into protected `main`. `Directory.Build.props` is the version source of truth, releases use annotated `vX.Y.Z` tags, and CI runs the same `scripts/check.sh` entry point as local development.
