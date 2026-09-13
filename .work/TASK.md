# Task: Initialize Inventor Scripts workspace for Inventor 2027
Mode: autopilot
Branch: setup/inventor-2027
Date: 2026-09-13

## Goal
Convert the agentic template into a production-oriented Inventor 2027 monorepo for multiple independent script and add-in projects. Keep Smart Manufacturing Exporter as the first isolated project, align the workspace with the corresponding GitHub repository, install and verify the required development tools, and leave the canonical gauntlet and CI executable.

## Non-goals
- Implementing Phase 1 exporter behavior or speculative Inventor API calls.
- Supporting Inventor versions other than 2027 in the initial workspace.
- Creating speculative shared libraries before a second project needs them.
- Publishing an installer or release binary during initialization.

## Budget
- Wall-clock: one setup session
- Subagents / workflow runs: 0 for this coherent setup; later coding uses Sol, with Terra for low-stakes bounded work
- Retries per failing step: 2
- Escalate to human when: an installer requires interactive elevation, required CI or branch protection cannot be configured, or a requirement cannot be verified

## Acceptance criteria
- [x] Root documentation and ADR describe the Inventor Scripts workspace, project isolation, and Inventor 2027 scope -> judgment: cross-file review
- [x] Smart Manufacturing Exporter is self-contained under `projects/smart-manufacturing-exporter/` with its product specification, architecture, source, and tests -> filesystem and build
- [x] .NET 10/C# tooling, root solution, formatting, analyzers, tests, mutation testing, secret scanning, build, and changelog freshness are wired -> scripts/check.sh
- [x] Project-scoped Codex routing uses Astra for orchestration, Sol for implementation, and Terra for low-stakes work -> .codex/config.toml and agent configs
- [x] Local verification executes real format, typecheck/build, lint/boundary, unit-test, secret-scan, production-build, mutation, and changelog checks -> scripts/check.sh
- [x] The local repository preserves both histories and tracks the Smart-Manufacturing-Macro repository as origin -> git log and git remote -v
- [ ] The initialized commit is tagged v0.1.0 and CI/branch protection are verified for the exact pushed SHA -> scripts/ci-watch.sh and GitHub API

## Plan
1. Verify the installed Inventor 2027 and .NET requirements from Autodesk and local interop assemblies.
2. Install the .NET 10 SDK, Visual Studio tooling where possible, gitleaks, git-cliff, and Inventor developer tools.
3. Scaffold the root workspace and the first C# project boundary skeleton without implementing exporter behavior.
4. Wire the project catalog, workspace decisions, product documentation, agent routing, checks, and CI.
5. Run the full local gauntlet and obtain an independent review verdict.
6. Commit, push through the gated workflow, tag v0.1.0, and verify CI on the exact SHA.

## Progress log
- 2026-09-13: Confirmed Inventor 2027 and 2024 are installed; project scope narrowed to Inventor 2027 only.
- 2026-09-13: Confirmed Inventor 2027 interop version 31 and Autodesk .NET 10 support.
- 2026-09-13: Installed .NET 10 SDK 10.0.401, gitleaks 8.30.1, and git-cliff 2.14.1.
- 2026-09-13: Pointed origin at Smart-Manufacturing-Macro and preserved the source template as the template remote.
- 2026-09-13: Visual Studio Community 2026 installation reached an unaccepted UAC prompt; Autodesk Developer Tools correctly reported Visual Studio as its missing prerequisite.
- 2026-09-13: Reframed the root as Inventor Scripts, moved Smart Manufacturing Exporter into its own `projects/` boundary, and registered it in `InventorScripts.sln`.
- 2026-09-13: Initial independent Sol review attempt was blocked by workspace credits; a retry became available after the workspace pivot.
- 2026-09-13: Sol review failed mutation ownership, empty mutation scoring, and cross-project boundary enforcement; added project-owned mutation gates, mutable compatibility behavior, and a global reference-boundary test.
- 2026-09-13: GitHub CI reached all checks but found clone-dependent changelog output because local-only template tags matched an unanchored release-tag pattern; anchored SemVer tags and made generation use the declared workspace version.

## Review verdict

PASS - The staged monorepo setup satisfies the review gate. The canonical check completed with locked restore, zero-warning Debug and Release builds, 4 unit tests, 2 architecture tests, history and working-tree secret scans, changelog freshness, and a scored mutation run of 1 killed mutant out of 1 for 100.00%. Project-owned mutation routing and repository-wide cross-project reference enforcement address the prior findings. No blocking correctness, security, path, dependency, architecture, or documentation issues remain in the staged diff.

PASS - Follow-up review confirmed the clone-independent changelog repair. An isolated clone simulation produced byte-identical output with local template tags, without those tags as on GitHub, and after adding the annotated `v0.1.0` tag. The exact SemVer pattern excludes `template-v*`, and the generator consistently derives the release tag from `Directory.Build.props`.

## Retro
<!-- filled by docs/procedures/retro.md -->
