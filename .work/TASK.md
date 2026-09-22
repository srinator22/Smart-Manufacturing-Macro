# Task: live-evidence-gate
Mode: autopilot
Branch: task/live-evidence-gate
Date: 2026-09-23

## Goal
Commit the File Naming Manager live smoke harness to the repository and make live evidence freshness a gate: a passing run records a sanitized stamp with a content hash of the Inventor-facing sources, and the project check fails when those sources change without a new recorded run. This mechanizes the second pending lesson from the file-naming-manager retro (live evidence went stale against a changed adapter until a reviewer refused it).

## Non-goals
- Running Inventor in CI. CI only verifies the stamp against the sources; the run itself happens on a machine with Inventor 2027.
- Committing smoke logs (they carry local user paths) or any fixture CAD.
- Changing product behaviour.

## Budget
- Wall-clock: 2 hours
- Subagents / workflow runs: 3
- Retries per failing step: 2
- Escalate to human when: the harness cannot be made to compile without the interop, or the hash cannot be made identical between a Windows Git Bash checkout and the CI runner.

## Acceptance criteria
- [x] The harness under projects/file-naming-manager/tools/FileNamingManager.LiveSmoke builds in the solution with 0 warnings with and without the installed interop; without it, Main prints UNCLAIMED and exits 2. -> solution build locally and on CI Evidence: builds with the installed interop and with `-p:InventorInteropPath=<missing>`; the no-interop exe prints UNCLAIMED and exits 2; registered in the solution
- [x] The harness contains no user-specific path; logs go to the temp folder, the stamp to tests/live-evidence/LIVE_EVIDENCE.json with recordedUtc, inventorDisplayName, sourceHash, assertionsPassed, assertionsFailed, result, harnessVersion; no paths or user names. -> inspection and an architecture test Evidence: stamp recorded 2026-09-22T20:53:12Z, Inventor 2027 build 310192060, 52 assertions, no paths; `LiveEvidenceStampCarriesNoLocalPaths` green
- [x] scripts/check-live-evidence.sh computes sourceHash as SHA-256 over the sorted git blob ids of the InventorAdapter sources, FileNamingWorkflow.cs and the harness Program.cs, compares with the stamp, fails with an actionable message, and is wired into the project check. -> fails on a deliberately edited adapter line and passes after a new run Evidence: C# and bash hashes identical (a6d4551b...); a one-character adapter comment edit produced LIVE EVIDENCE STALE and exit 1, reverted byte-identical
- [x] scripts/run-live-smoke.sh builds, refuses to run while Inventor.exe runs, runs under a timeout, and writes the stamp only on SMOKE PASS. -> executed once on this tree to produce the committed stamp Evidence: executed once; stamp committed
- [x] README, TEST_PLAN section 1 and ARCHITECTURE describe the gate; the PENDING lesson reflects the mechanization. -> inspection Evidence: inspected; check-project-readmes OK
- [ ] Gate green, review PASS, PR CI and exact-main CI green. -> ship procedure

## Plan
1. Move and guard the harness, add the two scripts, wire the project check, add the architecture test (WP1).
2. Run the smoke once to record the stamp; register the csproj; docs.
3. Review, gate, ship, retro.

## Progress log
- 2026-09-22T18:40Z - Opened from clean main after PR #12. Harness verified portable (temp root via GetTempPath, templates via FileManager.GetTemplateFile); its logs contain user paths and stay local. WP1 dispatched with the full spec inline.

- 2026-09-22T20:58Z - WP1 landed: harness relocated and guarded, stamp recorded, check wired into the project gate and proven to fail closed. Full gate: check: OK (255 + 9 + 6 + 55 tests, packaging OK for both add-ins, live-evidence OK, no leaks, mutation skipped with no production C# change). Independent review in progress. docs/decisions/0005 is untracked on purpose and ships with the next task.

- 2026-09-22T21:07Z - Review PASS; two minor items closed; committing.

## Review verdict
PASS (2026-09-23, independent reviewer, fresh context)

- All five acceptance criteria confirmed by inspection, including hash determinism between this checkout and the CI runner (`.gitattributes` pins LF; zero CR bytes in the hashed files; ordinal sort on both sides) and arithmetic corroboration of the stamp: 43 assertion sites minus 3 failure-only plus 12 loop iterations equals the recorded 52.
- Minor items closed before commit: a missing stamp field now prints LIVE EVIDENCE MALFORMED instead of a bare exit; ARCHITECTURE states exactly which sources the stamp covers and records the harness as the sanctioned interop use outside src.
- Recorded for the retro: Infrastructure and other Application files are exercised by the smoke but not hashed (matches the criterion as written); the checker script has no dedicated test script unlike the repo's other checkers; the kernel line about interop staying in InventorAdapter and AddIn should mention the harness at the next kernel edit.

## Retro
<!-- filled by docs/procedures/retro.md -->
