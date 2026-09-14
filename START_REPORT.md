# Start report

Date: 2026-09-13
Last verified: 2026-09-14
Project: Inventor Scripts workspace

## Detected

- Windows 11 x64 environment.
- Git 2.53.0 and GitHub CLI 2.90.0 authenticated as `srinator22`.
- Autodesk Inventor 2027 at `C:\Program Files\Autodesk\Inventor 2027`.
- Installed Inventor 2027 interop assembly with file version `31.0.19201.6`.
- Inventor 2027 SDK installers under `C:\Users\Public\Documents\Autodesk\Inventor 2027\SDK`.
- Inventor 2024 is also installed but explicitly outside this project's compatibility scope.
- The target GitHub repository is public, solo-owned, and initially contained only a README and MIT license.

## Installed

- .NET SDK 10.0.401, including the Windows Desktop SDK and runtime.
- gitleaks 8.30.1.
- git-cliff 2.14.1.
- Stryker.NET 5.0.0 as a repository-local .NET tool.
- Visual Studio Community 2022 17.14.40 with the Managed Desktop workload.
- Visual Studio Community 2026 18.10.0 with the Managed Desktop workload, installed side-by-side.
- Autodesk Inventor 2027 Developer Tools 19.0.0, including the Inventor 2027 C# and VB add-in templates for Visual Studio 2022.

## Wired

- Multi-project `projects/` workspace with a root `InventorScripts.sln` and a reserved `shared/` boundary.
- Smart Manufacturing Exporter as the first isolated project, with C# 14, .NET 10, x64, WPF, Core, Application, Infrastructure, InventorAdapter, UI, AddIn, UnitTests, and ArchitectureTests.
- Central NuGet package versions, committed lockfiles, deterministic builds, nullable reference types, current recommended analyzers, warnings as errors, and code style enforcement.
- The canonical gauntlet performs locked restore, formatting, Debug typecheck/build, analyzer lint, all registered tests, gitleaks history and working-tree scans, Release build, changed-file mutation testing, and changelog freshness verification.
- Windows GitHub Actions installs .NET 10.0.401 and hash-verified gitleaks/git-cliff archives before running the same gauntlet.
- Project documentation records the product requirements, module boundaries, COM threading strategy, data policy, release model, and phased backlog.
- Codex defaults to Astra orchestration, Sol implementation/review, and Terra low-stakes exploration, edits, and monitoring, capped at three concurrent subagent threads.
- `origin` is `srinator22/Smart-Manufacturing-Macro`; the source repository remains as the `template` remote.

## Missing or degraded

- Autodesk's Inventor 2027 Developer Tools installer does not recognize Visual Studio Community 2026. Visual Studio Community 2022 is therefore retained as the supported template and debugging host.
- The local v31 Inventor interop reference is conditional so CI can build without redistributing Autodesk binaries. Windows development machines with Inventor 2027 compile the real adapter and add-in server.
- The Phase 1 five-part live Inventor acceptance scenario remains unverified until the add-in is installed, loaded, and exercised against a sanitized assembly.

## Manual steps

- Build and install the Phase 1 add-in with the product-owned per-user installation script.
- Run the documented five-unique-part scenario in Inventor 2027 and record whether selecting three parts produces exactly three STEP files without changing any source document.
