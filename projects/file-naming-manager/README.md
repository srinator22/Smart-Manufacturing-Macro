# File Naming Manager

File Naming Manager `0.6.0` is a Windows add-in for Autodesk Inventor 2027. It analyzes the files of the active assembly against the WMP part-numbering scheme, allocates the next free numbers, previews a rename plan, and applies that plan through Inventor's own `SaveAs` so references stay intact.

Maturity: `beta` (`plugin.json`). Live rename behaviour is verified only by the temp-folder smoke harness described in [docs/TEST_PLAN.md](docs/TEST_PLAN.md); acceptance on a real project has not been recorded, which is why the plugin is tagged beta. Renaming Vault-managed (checked-in) files is not implemented in v1: those files are detected, planned, and exported to a Vault rename plan for manual execution in Vault Explorer. The full design and the reason execution is deferred are recorded in [docs/VAULT_RENAME_DESIGN.md](docs/VAULT_RENAME_DESIGN.md).

## What it does

- Adds Analyze Naming and Apply Naming commands to the shared WMP Custom Tools tab on the Inventor Assembly ribbon, panel File Naming.
- Requires a saved `.iam` assembly to be the active document.
- Parses every file name in scope against the WMP scheme:
  - Part: `PPP-NNNN Description.ipt`, for example `124-0002 Adaptor Plate Bottom.ipt`
  - Sub-assembly: `PPP-ANNN Description (sub-assembly).iam`, for example `124-A002 NDRM (sub-assembly).iam`
  - Main assembly: `PPP-ANNN Description (main assembly).iam`; the root of the assembly the tool is run from is always the main assembly, every other assembly is a sub-assembly
  - A drawing or presentation sharing a model's base name keeps that stem with its own extension
- Reports these analyzer findings when present: duplicate number, sequence gap, malformed whitespace, tagless assembly, unnumbered file, numbered file with no description, legacy prefix (`PNN Description.ipt`), revision-suffixed name, copy-suffixed name, and duplicate file names anywhere in scope.
- Allocates the next number per series (parts, assemblies) as the highest number observed in the project folder tree plus one, scanning every `.ipt`, `.iam`, `.idw`, `.dwg` and `.ipn` under the project root recursively, excluding `OldVersions`, `_V`, `3rd Party Hardware`, `Content Center Files` and `_renamed-originals`. Only files whose parsed project matches the target project count toward allocation, so files outside the open assembly still consume numbers; drawings and presentations raise the series maximum like any numbered file but are never reported as duplicates of their model.
- Prompts for the project number, prefilled from the root file name, then sibling numbered files, then a `P124`-style folder name, and validates it as 100-999 before anything is planned.
- Applies renames safely:
  - Renames go through Inventor's `Document.SaveAs`, never a filesystem move.
  - Documents are renamed leaf-first (parts and companion drawings, then sub-assemblies deepest first, then the root), and parents are saved afterwards so the reference change persists.
  - Originals are moved, never deleted, to `_renamed-originals\<UTC timestamp>\` with a `manifest.json` recording every move.
  - Vault-managed files are only ever planned, never renamed, by v1.
  - The plan is blocked, and the offending document named, on any of: the root assembly is unsaved; the root assembly has missing references; a document the plan would rename, or a parent whose reference must be saved, is not modifiable; the proposed target path already exists on disk; two or more operations would rename to the same target; a proposed file name would duplicate a file name already used anywhere else in scope, which Inventor's unique-filenames mode cannot resolve; a companion drawing is Vault-managed or read-only; or a document is also referenced by another open document outside the active assembly.
  - Numbered names with no description, or numbers duplicated across files, are never proposed; the row instead carries a reason to resolve manually.
  - A referenced file outside the project scope - outside the project root, or inside `OldVersions`, `_V`, `3rd Party Hardware`, `Content Center Files` or `_renamed-originals` - is never renamed and its original is never archived, and it never blocks the files the project does own.
  - A number series with every number used (`9999` for parts, `A999` for assemblies) is reported as exhausted on the row and, when a row actually needed a number from it, as a plan blocker.
  - The grid always shows what Apply will do right now for the current project number and options; a row excluded by an option names that option (for example "Excluded: 'Rename unnumbered files' is off.").

The add-in never silently saves, renames, moves, or deletes a document that is not part of the confirmed plan.

## Requirements

- Windows 11 x64
- Autodesk Inventor 2027
- .NET SDK 10.0.401, pinned by `global.json`
- Visual Studio Community 2022 17.14 with the .NET desktop development workload for Autodesk's Inventor 2027 templates and interactive debugging
- Autodesk Inventor 2027 Developer Tools 19.0.0 from `C:\Users\Public\Documents\Autodesk\Inventor 2027\SDK\developertools.msi`
- Visual Studio Community 2026 may coexist, but Autodesk's Inventor 2027 Developer Tools installer does not detect it as a replacement for Visual Studio 2022
- `gitleaks` 8.30.1 and `git-cliff` 2.14.1 for the full local gauntlet

## Install

End users install every WMP plugin, including this one, with the one-line command in the [workspace README](../../README.md#quick-install); the steps below are the developer path from a local build.

Close Inventor before replacing add-in files. From the repository root in PowerShell, restore, build, and install the Release configuration:

```powershell
dotnet restore InventorScripts.sln --locked-mode
dotnet build InventorScripts.sln -c Release --no-restore
pwsh -NoProfile -File projects/file-naming-manager/scripts/install-addin.ps1 -Configuration Release
```

The installer is per-user and does not require administrator privileges. It writes only these product-owned targets:

```text
%APPDATA%\Autodesk\Inventor 2027\Addins\FileNamingManager\
%APPDATA%\Autodesk\Inventor 2027\Addins\Autodesk.FileNamingManager.Inventor.addin
```

For an active debugging session, build and install `Debug` instead of `Release`.

## Verify installation

Before starting Inventor, confirm these files exist:

```text
%APPDATA%\Autodesk\Inventor 2027\Addins\FileNamingManager\FileNamingManager.AddIn.dll
%APPDATA%\Autodesk\Inventor 2027\Addins\Autodesk.FileNamingManager.Inventor.addin
```

Then:

1. Start Autodesk Inventor 2027.
2. Open Inventor's Add-In Manager and confirm File Naming Manager is loaded.
3. Open a saved `.iam` assembly.
4. Confirm the WMP Custom Tools tab, File Naming panel, and the Analyze Naming and Apply Naming commands appear in the Assembly ribbon. Smart Manufacturing Exporter, if installed, shares the same tab.

If Inventor reports a load error or the commands do not appear, stop and use the troubleshooting table below before renaming anything.

## Use

For the first test, use sanitized CAD files outside the Git repository and outside the Vault workspace, matching [docs/TEST_PLAN.md](docs/TEST_PLAN.md).

1. Open a saved Inventor `.iam` assembly and make it the active document.
2. Select Analyze Naming from the File Naming panel. The window shows one row per document in the assembly (include, current name, state, Vault state, proposed name) plus the project-level findings: duplicate numbers, sequence gaps, malformed whitespace, tagless assemblies, unnumbered files, numbered files with no description, legacy prefixes, and duplicate file names.
3. Confirm the Project number box: it is prefilled from the root file name, then sibling numbered files, then a `P124`-style folder name, and must be 100-999. Enter or correct it before planning a rename.
4. Choose options as needed: Rename unnumbered files (on by default), Normalize malformed numbered names (off by default; read the Vault warning before enabling it), Set Part Number iProperty (on by default), and Export Vault plan.
5. Untick the Include box on any row you do not want in this run. The plan is recomputed on every tick, so an excluded row leaves the plan entirely: it consumes no number (the next row takes the number it would otherwise have skipped), raises no blocker of its own, shows action None with the reason "Excluded by the operator.", and, if it is Vault-managed, is left out of the exported Vault plan. Select all and Select none set the whole column at once. The box is disabled on a row the plan would do nothing with, and Select none leaves those rows alone.
6. Switch to Apply mode to see the rename plan added to the report. The Apply button stays disabled and shows a reason until the plan has no blockers.
7. Select Apply. Read the status report: it names any document that could not be renamed and lists the originals moved to `_renamed-originals\<UTC timestamp>\`. The manifest is written before any file is moved, with every planned entry unarchived, then rewritten with each file's outcome once archival runs, so recovery is always possible from `manifest.json` even if a run is interrupted. If a parent save fails, the report names the parent and every original is left in place, because an unsaved parent still references the old file name on disk; save the named parent in Inventor and run Apply again. If an original cannot be moved into `_renamed-originals` after its rename succeeded, the report names it as an archive failure: the rename stands and the file is simply left beside its renamed file, unarchived. A model whose own rename succeeded but whose companion drawing or Part Number write then failed is counted as renamed with a warning ("Renamed 3 of 4, of which 1 with warnings."), because the rename really did happen: its parent is saved and its original archived, and the message names the step that failed.
8. Confirm the assembly still opens with no missing references and that Part Number iProperties equal the new number tokens where the option was enabled.

### Vault

File Naming Manager reads Vault state from the working folder only; it never queries the Vault server in v1.

- A file with no `_V\<name>.v` tracker beside it is unmanaged. It is renamed in-session like any other file.
- A file with a tracker is Vault-managed. The tracker says nothing about who has it checked out; v1 cannot tell. A managed file is never renamed locally: it appears in the report as Vault-managed with its planned name, and it is written to the exported Vault rename plan for you to apply yourself in Vault Explorer's Rename command, which preserves history. A managed companion drawing beside an unmanaged model blocks that model's rename, with the drawing named, because renaming the model would break the drawing's reference.
- If the plan would need to save a parent that Inventor reports as not modifiable (typically a managed file that is not checked out to you), the whole plan is blocked with that document named, because a parent that cannot be saved would keep referencing the old file name.

Select Export Vault plan to write the plan as a text file next to the root assembly. The step-by-step Vault Explorer procedure, including how to verify the rename afterward, is in [docs/TEST_PLAN.md](docs/TEST_PLAN.md) section 4. The full design for automating Vault-managed renames in a later task is in [docs/VAULT_RENAME_DESIGN.md](docs/VAULT_RENAME_DESIGN.md).

## Update

1. Close Inventor 2027.
2. Pull or check out the desired tested revision.
3. Run the locked restore and build commands from the Install section.
4. Run `install-addin.ps1` again with the same configuration.
5. Restart Inventor and verify the add-in and commands before renaming anything.

The installer replaces only this add-in's per-user binary directory and manifest. It does not modify machine-wide registration or another Inventor add-in.

## Uninstall

Close Inventor, then run:

```powershell
pwsh -NoProfile -File projects/file-naming-manager/scripts/uninstall-addin.ps1
```

The script validates and removes only this add-in's per-user manifest and binary directory. It does not remove Autodesk components, source code, or renamed files.

## Troubleshooting

| Symptom | Check | Resolution |
| --- | --- | --- |
| Add-in is absent from the Add-In Manager | Confirm the two paths in Verify installation exist and the manifest points to the installed DLL. | Close Inventor, rebuild, rerun the installer, and restart Inventor 2027. |
| WMP Custom Tools tab is missing | Confirm File Naming Manager is loaded and a saved `.iam` assembly is active. | Activate a saved assembly. Restart Inventor after the first installation if needed. |
| Apply is disabled | Read the reason shown next to the button. | Typical causes: the root assembly is unsaved, a reference is missing, a document the plan touches is not modifiable, or the project number has not been entered or is out of range. Fix the named cause and re-run Analyze Naming. |
| A file shows Vault-managed but I expected it to rename | The working folder has a `_V\<name>.v` tracker beside that file. | This is by design in v1: Vault-managed files are planned, not renamed. Use the exported Vault plan and Vault Explorer's Rename command, per the Vault section above. |
| Apply is blocked because a drawing is Vault-managed or read-only | A companion drawing sharing the model's base name is Vault-managed, or carries the read-only attribute, so it cannot be renamed alongside the model. | Rename the Vault-managed drawing in Vault Explorer first, or clear the read-only attribute, then re-run Analyze Naming. |
| Apply is blocked because a file is referenced by another open document | Another document open in the same Inventor session, outside the active assembly, references the file and is not a handled companion drawing. | Close or include the other open document, then re-run Analyze Naming. |
| The number sequence skipped a value | Numbers are allocated over the whole project folder tree, not just the open assembly. | Check for a file elsewhere in the project scope that already holds that number; this is expected, not an error. |
| I need to recover an original file | Renamed originals are moved, never deleted, to `_renamed-originals\<UTC timestamp>\` under the project root, with a `manifest.json` listing every move. Each entry's `Archived` field is true once the move succeeded; a failed move leaves `Archived` false and records the failure in `Error`, with the original still beside its renamed file. | Move the file back to the path recorded in the manifest. No data was deleted, so recovery is a plain file move; an entry with `Archived` false needs no move at all, since the original never left its folder. |
| Inventor reports an add-in load error | Confirm this is Inventor 2027 and the `net10.0-windows` x64 build completed without errors. | Run `scripts\check.cmd`, reinstall the matching Release or Debug output, and retain the exact load message if it persists. |
| Updated DLL cannot be copied or loaded | Inventor may still hold the assembly open. | Close every Inventor process, rerun the installer, and restart Inventor. |

When reporting a problem, include the Inventor 2027 display version, add-in version, active document type, the exact status or blocker message, and whether the affected file is Vault-managed. Do not attach proprietary CAD.

## Versioning and changelog

The workspace currently uses one semantic version for all Inventor projects before `1.0`:

- [Directory.Build.props](../../Directory.Build.props) contains the authoritative `VersionPrefix`.
- [CHANGELOG.md](../../CHANGELOG.md) is generated from Conventional Commits and is never hand-edited.
- The add-in activation manifest version is mechanically checked against `VersionPrefix`.
- Annotated release tags use `vX.Y.Z`.

Independent per-plugin version numbers and changelogs are not active yet. They require an architecture decision once multiple plugins genuinely need separate release schedules. Until then, this README links to the workspace changelog and states which workspace version it documents.

## Known limitations

- Renaming Vault-managed (checked-in) files is designed but not executed by v1; see [docs/VAULT_RENAME_DESIGN.md](docs/VAULT_RENAME_DESIGN.md).
- Only drawings and presentations that share a model's exact base name are handled. A drawing with a different name that views a renamed model is not detected and will lose its reference when the original is moved to `_renamed-originals`; move the original back from the manifest to recover, then rename the drawing's reference in Inventor.
- Only documents referenced by the active assembly are considered parents. Another assembly open in the same session that references a renamed part is reported as a blocker; an assembly that is not open and references the part is not detected and will need its reference repaired when next opened.
- Description text case is never changed.
- Revision suffixes are never generated or parsed into new names.
- The Part Number iProperty is the only iProperty this tool writes.
- Vault state is read from the working-folder tracker only; the tracker says nothing about who has the file checked out, and checkout state cannot be detected in v1.
- No live Inventor acceptance is claimed beyond the temp-folder smoke harness in [docs/TEST_PLAN.md](docs/TEST_PLAN.md); the interactive and Vault procedures in that plan are unclaimed until run and recorded.

## Development and verification

Run these commands from the repository root. From PowerShell or Command Prompt:

```bat
scripts\check.cmd
```

From Git Bash:

```bash
./scripts/check.sh
```

The gauntlet restores only from committed lockfiles, checks formatting and analyzers, compiles with warnings as errors, enforces product and cross-project architecture tests, runs unit tests, scans Git history and the working tree for secrets, builds Release output, runs the product-owned mutation suite when production logic changes, and verifies the generated changelog. The mutation suite enforces Core at 85%, Application at 85%, and Infrastructure at 90%, against measured scores of 93.33% / 94.86% / 97.67% on 2026-09-23.

Individual development commands:

```powershell
dotnet restore InventorScripts.sln --locked-mode
dotnet build InventorScripts.sln -c Release --no-restore
dotnet test InventorScripts.sln -c Release --no-build --no-restore
```

Live Inventor behaviour is gated by a recorded evidence stamp. `tools/FileNamingManager.LiveSmoke` drives a hidden Inventor 2027 through the full analyze/plan/apply path in `%TEMP%`, and on a pass writes `tests/live-evidence/LIVE_EVIDENCE.json` holding a hash of the InventorAdapter sources, the workflow Execute path and the harness itself. `scripts/check-live-evidence.sh`, which `scripts/check.sh` runs, recomputes that hash from the working tree and fails when it differs, so a change to any of those sources cannot merge while the newest live run predates it. The stamp records no path, host or user name; the run log stays local under `%TEMP%\naming-live-smoke\`. To refresh it, close every Inventor window and run `bash projects/file-naming-manager/scripts/run-live-smoke.sh` on a machine with Inventor 2027, then commit the updated stamp. Without the interop assembly, as on CI, the harness compiles and reports `UNCLAIMED: Autodesk.Inventor.Interop not installed`.

Architecture, the module dependency direction, and the analyze/plan/apply flow are documented in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The verified Inventor 2027 API surface is in [docs/INVENTOR_2027_API_COMPATIBILITY.md](docs/INVENTOR_2027_API_COMPATIBILITY.md). The live test procedure, including the automated smoke harness and the manual Vault Explorer steps, is in [docs/TEST_PLAN.md](docs/TEST_PLAN.md).
