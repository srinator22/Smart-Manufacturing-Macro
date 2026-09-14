# Phase 1 Inventor 2027 test plan

Status: Not run

This test separates build verification from live Autodesk Inventor behavior. Passing unit tests and compilation does not mark this scenario complete.

## Preconditions

- Autodesk Inventor 2027 is installed.
- Visual Studio Community 2022 17.14 and Autodesk Inventor 2027 Developer Tools 19.0.0 are installed.
- `scripts/check.sh` passes.
- Inventor is closed while the Debug add-in is installed.
- The test assembly and outputs contain no customer or production data and remain outside Git.

## Prepare the add-in

From the repository root in PowerShell:

```powershell
dotnet build InventorScripts.sln -c Debug
pwsh -NoProfile -File projects/smart-manufacturing-exporter/scripts/install-addin.ps1 -Configuration Debug
```

Start Inventor 2027. In the Add-In Manager, verify Smart Manufacturing Exporter is loaded. Do not continue if Inventor reports an add-in load error.

## Main acceptance scenario

1. Create or open a sanitized assembly containing five distinct saved `.ipt` source documents as direct top-level occurrences. Do not use subassemblies in this Phase 1 scenario.
2. Record each source document's modified timestamp and dirty state.
3. Open the Smart Export ribbon tab and select Smart Export.
4. Verify the checklist contains exactly five unique part rows and shows the expected quantity for each source document.
5. Select None, then select exactly three rows.
6. Choose a new, empty output directory outside the repository.
7. Run Export Selected.
8. Verify the result reports three successful exports and zero failures.
9. Verify the destination contains exactly three `.step` files and that each corresponds to a selected source document.
10. Verify no unselected part has an output file.
11. Verify all source document timestamps and dirty states match the values recorded before export.

## Safety and validation scenarios

- Activate a part document and invoke Smart Export. Expected: `Smart Export requires an active Inventor assembly.` and no scan or output.
- Add a duplicate occurrence of one part. Expected: one checklist row for that source with quantity 2 and one STEP output when selected.
- Suppress a top-level part occurrence. Expected: it is excluded and reported as suppressed.
- Choose a destination containing a conflicting STEP filename. Expected: export is blocked before translation and the existing file remains unchanged.
- Make an output appear after preview but before translation. Expected: that item fails without overwrite and later items continue.
- Cause one source export to fail. Expected: the failure is reported and later selected items continue.

## Evidence to record

- Inventor display version and interop assembly version.
- Add-in load status and ribbon visibility.
- Sanitized assembly path and the five source filenames.
- Selected filenames and destination path.
- Before and after source timestamps and dirty states.
- Produced STEP filenames and file count.
- Any warning, failure, Inventor stability issue, or UI ambiguity.

Change `Status` only after the scenario is run and the evidence above is retained outside Git or sanitized for the repository data policy.
