# Contributing

This project is operated by coding agents with human direction.
Contributions follow the same safety and verification rules.

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
