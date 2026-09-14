# Task: plugin-readme-standard
Mode: autopilot
Branch: codex/plugin-readme-standard
Date: 2026-09-14

## Goal
Make every Inventor project README a dependable operator guide, and bring Smart Manufacturing Exporter's README up to that standard before the user's first live Inventor 2027 test.

## Non-goals
- Changing Phase 1 product behavior, Inventor API integration, packaging paths, or the workspace release model.
- Creating independent per-plugin release trains without the required architecture decision.

## Budget
- Wall-clock: 45 minutes
- Subagents / workflow runs: 0
- Retries per failing step: 2
- Escalate to human when: the requested documentation standard conflicts with a project-specific installation model.

## Acceptance criteria
- [x] Smart Manufacturing Exporter's README explains what the current version does and gives complete install, verification, usage, update, uninstall, troubleshooting, limitation, and version/changelog guidance. -> judgment: README inspection
- [x] The workspace documents the required README contract for future Inventor projects. -> root README inspection
- [x] A repository check fails when any `projects/*/README.md` omits a required operator section. -> script fixture test
- [x] The installed per-user add-in manifest and assembly exist at their documented paths. -> filesystem inspection
- [x] The complete repository gate passes. -> `scripts/check.sh`

## Plan
1. Define and mechanize the project README contract.
2. Rewrite the Smart Manufacturing Exporter README around first-time installation and real Phase 1 usage.
3. Verify the live per-user installation paths and run the complete repository gate.
4. Review, version through the generated workspace changelog, and deliver through the gated pull-request workflow.

## Progress log
- 2026-09-14T11:08Z - Confirmed the Debug build succeeds with zero warnings and installed it into Inventor 2027's per-user Addins directory while Inventor was closed.
- 2026-09-14T12:21Z - Added and fixture-tested the required project README contract, expanded the exporter operator guide, confirmed the installed manifest targets Inventor 2027 and the installed DLL, and passed the complete repository gate.
- 2026-09-14T12:42Z - Repaired fenced-code parser bypasses, passed the complete repository gate again, and received an independent Sol reviewer PASS.

## Review verdict
PASS

- Both backtick and tilde fenced headings are rejected, including trailing-text pseudo-closers; targeted fixtures and an independent adversarial run pass.
- The exporter README no longer claims independent review before verdict, matches actual Phase 1 behavior and installation paths, and correctly describes workspace-wide versioning and changelog ownership.
- `scripts/check.sh` wires the contract tests into the canonical gate. No weakened checks, secrets, dependencies, or unrelated changes found.
- Residual limitation: the automated contract enforces required section structure; documentation completeness still requires reviewer inspection, which the current exporter README passes.
- Mechanical version review: PASS. `Directory.Build.props`, the product README, activation manifest, and generated project dependency lock entries consistently report `0.2.1`; no unrelated lockfile fields changed.

## Retro

- Discovery routes: the independent reviewer caught fenced-code parser bypasses before merge; the fixture tests reproduced both cases before repair. No defect escaped merge and no human-reported defect belonged to this documentation task.
- Regression guards: backtick and tilde fence cases, including invalid trailing-text pseudo-closers, are covered by `scripts/test-project-readmes.sh` and run in the canonical gate.
- Lessons: none proposed. The checks and review process caught the defects before merge, and no approved lesson was consulted.
- Delivery evidence: PR #5 merged as `18d6c0fe0e8c081920511c604ad88d3e0176ea8c`; the required `check` job passed for both the branch SHA and the exact main merge SHA.
- Remaining validation: documentation completeness beyond required section structure remains a reviewer judgment; the current exporter README passed that inspection.
