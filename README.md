# Inventor Scripts

Inventor Scripts is a Windows workspace for building and maintaining independent Autodesk Inventor automations. It keeps add-ins, engineering utilities, and script-based tools in separate project folders while sharing one pinned Inventor 2027 development environment and one verification entry point.

The workspace currently targets Autodesk Inventor 2027 only. Support for another Inventor release requires its own compatibility evidence and architecture decision.

Future product concepts are recorded in [IDEAS.md](IDEAS.md). Logged ideas are not active projects and do not receive code or shared abstractions until they are promoted through a scoped task.

## Project catalog

| Project | Kind | Status |
| --- | --- | --- |
| [Smart Manufacturing Exporter](projects/smart-manufacturing-exporter/README.md) | C#/.NET WPF add-in | Phase 1 MVP implemented; live Inventor acceptance pending |

## Quick install

Close Inventor, open Windows PowerShell, and run:

```powershell
irm https://github.com/srinator22/Smart-Manufacturing-Macro/releases/latest/download/Install-WmpInventorTools.ps1 | iex
```

The command downloads the latest `WmpInventorTools-<version>.zip` and its
`SHA256SUMS.txt`, verifies the SHA-256 digest, clears the downloaded-file
mark, and installs every plugin in the release under
`%APPDATA%\Autodesk\Inventor 2027\Addins\`. It writes one `.addin` manifest
per plugin pointing at the files it just copied, records what it installed
in `%LOCALAPPDATA%\WMP\InventorTools\installed.json`, and keeps the install
it replaced in `%LOCALAPPDATA%\WMP\InventorTools\previous\`. Nothing is
deleted and no executable is downloaded or launched. Re-running the command
is safe; it reinstalls the same version over itself. On failure the window
stays open with the reason and nothing is changed.

Requirements:

- Autodesk Inventor 2027, closed while the installer runs
- .NET 10 Desktop runtime x64 - the installer stops and prints
  <https://dotnet.microsoft.com/download/dotnet/10.0> when it is missing

### Update

Use the **Check for updates** command on the **WMP Custom Tools** ribbon
tab (WMP Tools Manager). It downloads the latest release, verifies its
SHA-256 digest, and applies it after Inventor closes; the replaced install
is archived for rollback. Re-running the Quick install command above does
the same from outside Inventor.

### Roll back

```powershell
& ([scriptblock]::Create((irm https://github.com/srinator22/Smart-Manufacturing-Macro/releases/latest/download/Install-WmpInventorTools.ps1))) -Rollback
```

This restores the most recently archived install from
`%LOCALAPPDATA%\WMP\InventorTools\previous\` and archives the install it
replaced. Inventor must be closed.

### What "beta" means

A plugin marked `beta` builds, passes the full workspace gate, and has no
recorded live acceptance run inside Autodesk Inventor. Treat its output as
unverified: check exported files and renamed documents before relying on
them, and keep a backup of any assembly you point it at. Every plugin in
the current release is `beta`; each project's `plugin.json` carries the
maturity and each project README states it.

## Workspace layout

```text
projects/
  <project-name>/
    README.md       project purpose, setup, and commands
    docs/           project-specific requirements and architecture
    src/            production code or Inventor scripts
    tests/          deterministic and integration tests
shared/             code used by at least two Inventor projects
docs/               workspace-wide architecture, rules, and decisions
scripts/            repository checks and maintenance commands
InventorScripts.sln all registered .NET projects
```

Each project owns its product requirements and host integration details. Shared configuration at the root provides C# 14, .NET 10.0.401, analyzers, package locking, secret scanning, changelog generation, and CI. Autodesk binaries, customer CAD, generated manufacturing files, and user configuration never enter Git.

## Requirements

- Windows 11 x64
- Autodesk Inventor 2027
- .NET SDK 10.0.401, pinned by `global.json`
- Visual Studio Community 2022 17.14 with .NET desktop development for Autodesk's Inventor 2027 Developer Tools and interactive debugging
- Autodesk Inventor 2027 Developer Tools 19.0.0 for the matching add-in templates
- Visual Studio Community 2026 may remain installed side-by-side, but the Inventor 2027 Developer Tools installer does not detect it as a substitute for Visual Studio 2022
- `gitleaks` 8.30.1 and `git-cliff` 2.14.1 for the full local check

## Build and verify everything

From PowerShell or Command Prompt:

```bat
scripts\check.cmd
```

From Git Bash:

```bash
./scripts/check.sh
```

Useful direct commands:

```powershell
dotnet restore InventorScripts.sln --locked-mode
dotnet build InventorScripts.sln -c Release --no-restore
dotnet test InventorScripts.sln -c Release --no-build --no-restore
```

## Project README contract

Every folder directly under `projects/` owns a present-tense operator README. It must let a new user install, verify, use, update, troubleshoot, and remove that project without reading source code. The repository gate requires these exact sections:

- `## What it does`
- `## Requirements`
- `## Install`
- `## Verify installation`
- `## Use`
- `## Update`
- `## Uninstall`
- `## Troubleshooting`
- `## Versioning and changelog`
- `## Known limitations`
- `## Development and verification`

The workspace currently uses one semantic version from `Directory.Build.props` and one generated [CHANGELOG.md](CHANGELOG.md). Each project README states the workspace version it documents and links to that changelog. Independent plugin versions or changelogs require an architecture decision when separate release schedules become necessary.

## Adding another Inventor project

1. Create `projects/<kebab-case-name>/` with its own operator README, docs, source, and tests.
2. Record its Inventor host version and safety boundary before implementation.
3. Add every .NET project to `InventorScripts.sln`, grouped below that product's solution folder.
4. Keep Autodesk COM references at the host adapter edge and keep domain logic COM-free.
5. For production C#, add `projects/<name>/scripts/mutation.sh`; the root gate requires and runs it when that project's code changes.
6. For non-.NET automation, add `projects/<name>/scripts/check.sh`; the root gate discovers and runs it.
7. Add the project to the catalog above and run the full root check.

Use `shared/` only after the same stable capability has two real consumers. This avoids coupling otherwise independent Inventor tools through speculative abstractions.
