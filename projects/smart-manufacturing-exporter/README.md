# Smart Manufacturing Exporter

Smart Manufacturing Exporter is a Windows add-in for Autodesk Inventor 2027. It is designed to scan an assembly as an engineering hierarchy, explain automatic inclusion and exclusion decisions, let the user review the exact manufacturing scope, and coordinate safe STEP, sheet-metal DXF, and drawing exports.

This is the first product inside the Inventor Scripts workspace. Phase 1 provides the bounded minimum viable exporter: an Inventor add-in command, active-assembly validation, unique top-level part scanning, an explicit WPF checklist, and safe STEP export. Live Inventor acceptance remains separate from compilation and unit verification.

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

## Build and verify

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

## Installing for Inventor 2027

Build and install the Debug configuration from PowerShell:

```powershell
dotnet build InventorScripts.sln -c Debug
pwsh -NoProfile -File projects/smart-manufacturing-exporter/scripts/install-addin.ps1 -Configuration Debug
```

The installer copies the built output to `%APPDATA%\Autodesk\Inventor 2027\Addins\SmartManufacturingExporter` and writes `Autodesk.SmartManufacturingExporter.Inventor.addin` in the parent Addins directory. It does not require machine-wide registration. Close Inventor before replacing an installed build, then start Inventor 2027 and confirm Smart Manufacturing Exporter is loaded in the Add-In Manager.

## Debugging in Inventor

1. Open `InventorScripts.sln` in Visual Studio Community 2022.
2. Build the Debug x64 configuration and run the install command above.
3. Set `SmartManufacturingExporter.AddIn` as the startup project.
4. Configure the startup action to launch `C:\Program Files\Autodesk\Inventor 2027\Bin\Inventor.exe`.
5. Place breakpoints in the add-in server, command, or Inventor adapter and start debugging.

Inventor API calls and STEP translation remain on Inventor's owning STA thread. The COM-free workflow and view model can be debugged without starting Inventor.

## Uninstalling

Close Inventor, then run:

```powershell
pwsh -NoProfile -File projects/smart-manufacturing-exporter/scripts/uninstall-addin.ps1
```

The script validates and removes only this add-in's per-user manifest and binary directory. Machine-level deployment remains outside Phase 1.

## Phase 1 limitations

- Scanning is limited to unique top-level part documents. Recursive subassemblies begin in Phase 2.
- Existing STEP files are blocking conflicts and are never silently overwritten.
- STEP translation uses Inventor 2027's installed translator defaults. AP203, AP214, and AP242 option selection begins only after the exact option keys are verified.
- Browser folders, classifications, smart rules, DXF, PDF, presets, and quick export remain later phases.
- The live five-part acceptance result is not claimed until the [Phase 1 test plan](docs/PHASE1_TEST_PLAN.md) is executed in Inventor 2027.

## Development workflow

Changes use short branches and pull requests into protected `main`. `Directory.Build.props` is the version source of truth, releases use annotated `vX.Y.Z` tags, and CI runs the same `scripts/check.sh` entry point as local development.
