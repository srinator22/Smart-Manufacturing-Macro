# Contributing

This project is operated by coding agents with human direction.
Contributions follow the same safety and verification rules.

## Local prerequisites

- .NET SDK 10.0.401, pinned by `global.json`. Install the exact version;
  the restore runs `--locked-mode` and a different SDK fails the gate.
- `gitleaks` 8.30.1 on PATH for the secret scan in `./scripts/check.sh`.
- `git-cliff` 2.14.1 on PATH for changelog generation and the changelog
  freshness check.
- PowerShell 7 (`pwsh`) on PATH. The per-project packaging tests and
  `scripts/release/test-release.sh` invoke it; without it those steps stop
  silently partway through the gate.
- Autodesk Inventor 2027 with its Developer Tools 19.0.0, required only to
  build and debug the add-in host projects and to run live acceptance.
  Core, Application, Infrastructure, and UI build and test without it.
- Windows 11 x64. `./scripts/check.sh` runs under Git Bash;
  `scripts\check.cmd` is the native entry point.

- Read AGENTS.md first. The kernel block is hash-pinned: editing it
  requires human approval plus `scripts/kernel-hash.sh --update` in the
  same commit, or CI fails.
- `./scripts/check.sh` must be green (native Windows:
  `scripts\check.cmd`). CI runs the exact same script.
- Conventional Commits; the body carries the why. No attribution
  trailers. Plain hyphens only - no em or en dashes anywhere.
- Keep the budgets: AGENTS.md <= 180 lines, rules files <= 40 lines,
  <= 8 skills.
- Removing anything means moving it to `.archive/` with a one-line
  reason, never deleting.
- Do not invent Inventor APIs, translator identifiers, threading safety,
  or supported export options. Cite Autodesk documentation or the
  installed Inventor 2027 interop surface in the task record.
- Keep Autodesk COM types inside InventorAdapter or the AddIn host edge.
  Unit-test pure rules and workflows without requiring Inventor.
- Keep each automation under `projects/<kebab-case-name>/` with its own
  README, requirements, source, and tests. Register compiled projects in
  `InventorScripts.sln` and in the root project catalog.
- Add code to `shared/` only when at least two projects consume a stable
  contract.
