# Smart Manufacturing Exporter

Smart Manufacturing Exporter is a Windows add-in for Autodesk Inventor 2027. It is designed to scan an assembly as an engineering hierarchy, explain automatic inclusion and exclusion decisions, let the user review the exact manufacturing scope, and coordinate safe STEP, sheet-metal DXF, and drawing exports.

This is the first product inside the Inventor Scripts workspace. It currently contains an initialized .NET 10 architecture skeleton. Phase 1 add-in behavior is the next milestone; no DLL in this setup commit is presented as a working Inventor add-in.

## Target and safety boundary

- Autodesk Inventor 2027 only
- C# 14 and .NET 10, x64
- WPF presentation layer
- Source Inventor documents are never silently saved, renamed, moved, or modified
- Conflicting output files are never silently overwritten
- Inventor API behavior is implemented only after verification against Autodesk documentation or the installed Inventor 2027 interop assembly

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
- Visual Studio Community 2026 or later with the .NET desktop development workload for interactive WPF development and debugging
- Autodesk Inventor 2027 Developer Tools from `C:\Users\Public\Documents\Autodesk\Inventor 2027\SDK\developertools.msi` when using Autodesk's Visual Studio templates and SDK samples
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

## Debugging in Inventor

Phase 1 adds the `ApplicationAddInServer` implementation and `.addin` manifest. Once those exist, configure the AddIn project to start `C:\Program Files\Autodesk\Inventor 2027\Bin\Inventor.exe`, place the manifest in the documented per-user Inventor 2027 Addins directory, build Debug x64, and start debugging from Visual Studio. Exact steps and paths will be verified against the resulting Phase 1 artifact before they are called complete.

## Installing and uninstalling

No installable add-in exists at the foundation stage. Phase 1 will produce a per-user package and manifest, plus exact install and clean uninstall steps. Machine-level installation is deferred until there is a demonstrated multi-user requirement because it requires administrator privileges.

## Development workflow

Changes use short branches and pull requests into protected `main`. `Directory.Build.props` is the version source of truth, releases use annotated `vX.Y.Z` tags, and CI runs the same `scripts/check.sh` entry point as local development.
