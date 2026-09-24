# Task: release-interop-guard
Mode: autopilot
Branch: task/release-interop-guard
Date: 2026-09-24

## Goal
Make it impossible to publish an add-in that does not load. The v0.6.0 release assets were packaged by the release workflow on a GitHub-hosted runner without Inventor: Autodesk.Inventor.Interop.dll was absent, INVENTOR_INTEROP was undefined, every `#if INVENTOR_INTEROP` block including StandardAddInServer compiled out, and the three shipped DLLs load in Inventor as Unloaded with no ribbon command. The packager now refuses any add-in assembly without the interop reference and the StandardAddInServer type before anything under dist/ is written; the workflow drafts the release with notes only; a publish script run on a machine with Inventor 2027 at the tag builds, guards, uploads and undrafts. ADR-0005 is amended; the P0 BACKLOG row closes; the release-and-updater retro record is corrected (one defect escaped past merge and was found live by the human).

## Non-goals
- Vendoring Autodesk.Inventor.Interop.dll into the repository (Autodesk redistributable, public repo).
- A self-hosted runner.
- Repairing the v0.6.0 assets themselves: that is a separate human decision (replace with --clobber from the tag build, or supersede with the next release).

## Budget
- Wall-clock: one session
- Subagents / workflow runs: 3
- Retries per failing step: 2
- Escalate to human when: the guard cannot distinguish an interop-less DLL under PowerShell 5.1, or publishing requires a permission the developer's gh login lacks.

## Acceptance criteria
- [ ] build-release.ps1 refuses a package when any add-in assembly lacks an Autodesk.Inventor.Interop reference or a StandardAddInServer type, before dist/ is written, under both PowerShell 7 and 5.1. -> test-build-release.sh negative case (failing-first against the previous script)
- [ ] release.yml publishes no DLL assets: draft release with notes, --verify-tag, job-level contents: write only, prints the publish command. -> inspection, YAML parse, next tag run
- [ ] publish-release.ps1 fails closed (untagged HEAD, dirty tree, missing interop, missing draft release) and -WhatIf lists the uploads without touching GitHub. -> dry run recorded here
- [ ] test-release.sh stays green with the interop (real add-ins pass the guard) and without it (real build refused, installer cases run over a stub add-in built in the temp folder). -> local run with the interop; CI run for the pushed SHA
- [ ] ADR-0005 amendment, ship.md step 8, BACKLOG row removed, release-and-updater archived retro corrected, pending lesson proposed. -> inspection, reviewer PASS
- [ ] Review PASS, gate green, PR CI and main CI green. -> ship procedure

## Plan
1. Worker G1 implements the guard, workflow, publish script, tests and docs in a worktree.
2. Advisor: gate, review, rebase after the ribbon retro lands, record, ship, retro.

## Progress log
- 2026-09-24T10:30Z - Defect confirmed on this machine: published DLLs 6656 / 7168 / 9728 bytes with no StandardAddInServer string; local interop builds 22 to 42 KB with it. Rebuilt the v0.6.0 tag with the interop in worktree C:\dev\WMP\wt-v060 (DLLs 12.8 to 14.8 KB, StandardAddInServer present, installer hash identical to the published one); package installs into isolated roots. Replacement of the public assets awaits the human's decision.
- 2026-09-24T10:45Z - G1 (Opus) delivered: guard with PS7 metadata reader and PS5.1 byte scan (failing-first: previous packager accepted the interop-less fixture), draft-only workflow, publish-release.ps1 with -WhatIf (dry run on this machine: interop found, guard OK for 3 plugins, would upload zip 502345 B, SHA256SUMS 188 B, installer 33383 B; refused for untagged HEAD and dirty tree), test-release.sh interop-aware. Gate in the worktree: check: OK (build-release-test 8 cases, release-test OK, no leaks, mutation skipped).
- 2026-09-24T11:20Z - Reviewer round 1 FAIL (see verdict); G2 (Opus) dispatched to fix: -AllowUntagged and -SkipBuild only with -DryRun, -ReplacePublishedAssets as the documented repair path, test-publish-release.sh over a temp clone with a fake gh shim, 5.1 guard path asserted, BACKLOG row for repairing v0.6.0, nits.
- 2026-09-24T12:05Z - G2 delivered: -AllowUntagged and -SkipBuild only with -DryRun/-WhatIf, -ReplacePublishedAssets as the documented repair switch (refuses unless HEAD at tag and clean; prints overwrite list with published digests), test-publish-release.sh (9 cases over a temp clone behind a fake gh shim, wired into check.sh 9b), 5.1 byte-scan guard path asserted alongside pwsh, BACKLOG P0 row for repairing or superseding v0.6.0, nits. Failing-first shown for the -AllowUntagged and -SkipBuild real runs against the previous script. Branch rebased onto main c55fee2 (ribbon retro). Inventor is running on this machine, so the installer cases and the full gate wait until it is closed; nothing is pushed before that.

## Review verdict
<!-- written ONLY by the independent reviewer -->

Round 1 (2026-09-24T11:20Z, reviewer agent, Opus, read-only): FAIL - 1 blocker (publish-release.ps1 -AllowUntagged let a real publish overwrite live assets from a non-tag commit, and an already-published release was clobbered after only a warning), 4 should-fix (-SkipBuild on a real publish packages unseen gitignored output; no test exercises the publish refusals; the PowerShell 5.1 byte-scan guard branch never runs under the gate; the live v0.6.0 damage had no BACKLOG row after the P0 row was removed), 1 note (the interop-absent branch of test-release.sh first runs in PR CI), 2 nits. The guard itself, release.yml and the stub-based CI path were verified sound.

## Retro
<!-- filled by docs/procedures/retro.md -->
