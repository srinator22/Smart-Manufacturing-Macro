# Task: release-and-updater
Mode: autopilot
Branch: task/release-and-updater
Date: 2026-09-23

## Goal
Make installing and updating the WMP Inventor add-ins a one-step, no-executable process for non-developer users, per ADR-0005: a release workflow that publishes a zip of every plugin plus SHA256SUMS and an in-memory PowerShell installer on each `v*` tag; a data-driven plugin catalog (`plugin.json` per project with a maturity tag) so new and updated plugins flow through packaging, install and update without code changes; and a third add-in, WMP Tools Manager, that offers Check for updates on the shared WMP Custom Tools tab, stages a verified download, and applies it through the same PowerShell script after Inventor exits, keeping the previous install for rollback. Workspace version becomes 0.6.0 and `v0.6.0` is the first published release (human approval given in session on 2026-09-23).

## Non-goals
- Code signing, silent or startup-triggered updates, per-plugin release trains (ADR-0004), running Inventor in CI, bundling the .NET runtime, any Vault interaction.

## Budget
- Wall-clock: one session
- Subagents / workflow runs: 8
- Retries per failing step: 2
- Escalate to human when: the release workflow needs a permission beyond `contents: write` on the release job, or an update path would require deleting user data.

## Design references
- docs/decisions/0005-release-distribution-and-updater.md (decision)
- scripts/release/build-release.ps1, Install-WmpInventorTools.ps1, test-release.sh, .github/workflows/release.yml (built before this branch opened; verified locally: package OK, install OK, tampered digest exit 5, idempotent, rollback OK)
- projects/*/plugin.json (schema: id, displayName, description, maturity, addinProject, assembly, addinTemplate, installDirectory, ribbonPanel, commands, homepage); both existing plugins are beta because no live acceptance is recorded

## Acceptance criteria
- [x] Every project under projects/ has a valid plugin.json and a catalog check asserts the schema and maturity values. -> build-release.ps1 refuses an add-in project without plugin.json, invalid or duplicate ids, folders and manifest names; test-build-release.sh (7 cases) and test-release.sh derive the plugin set from projects/*/plugin.json
- [x] build-release.ps1 produces the zip, catalog.json, SHA256SUMS and installer from the catalog alone; test-release.sh asserts contents, hash refusal, idempotence and rollback and runs in the root gate (step 9b). -> gate: release-test OK (20 cases)
- [x] The installer never closes the caller's window: under `irm | iex` a failure reports and returns without `exit`; from a file it exits with the documented code. -> test-release.sh in-memory failure, in-memory success, in-memory rollback, file-mode exit 2 and 5
- [x] WMP Tools Manager builds with 0 warnings, its dialog renders with realized content (render test), version comparison and release parsing are unit-tested with malformed inputs, staging paths are refused outside the state root, the apply command is built exactly, packaging test passes, plugin.json present (beta), and its button sits on the shared tab with a beta-aware tooltip. -> 342 unit + 12 architecture tests, packaging OK, mutation 92.57 / 92.47 / 79.31 %
- [x] release.yml is least-privilege (job-level contents: write only), pinned like ci.yml, verifies tag equals VersionPrefix, runs the full gate before packaging, and composes notes from the changelog plus a plugins table with maturity. -> reviewer inspection PASS; release run https://github.com/srinator22/Smart-Manufacturing-Macro/actions/runs/35798325491 (success)
- [x] Root README quick install, CONTRIBUTING prerequisites, ship.md step 8 updated; each project README states its maturity. -> project-readmes OK (3), reviewer PASS
- [x] Version 0.6.0 in Directory.Build.props and every X.manifest; review PASS; gate green; PR CI and main CI green. -> PR #15 CI success for 5fd2ebad076d0cd74ed1d15c6c58a487ea915108; merged as 8e6e4abb3e667b087240b6f51750f05cf6f1a65d; main CI success for that SHA
- [x] `v0.6.0` tagged on the merge commit; the release workflow publishes zip, SHA256SUMS and installer; the one-liner installs into an isolated Addins root on this machine and installed.json lists three beta plugins. -> https://github.com/srinator22/Smart-Manufacturing-Macro/releases/tag/v0.6.0 (assets: WmpInventorTools-0.6.0.zip 405769 B, SHA256SUMS.txt, Install-WmpInventorTools.ps1; zip sha256 2aba092aac87e668d02e78b7d465a2ae2d3af6393f9cbf837146282d7cfd5765); one-liner verified 2026-09-22T23:42Z under Windows PowerShell 5.1 into isolated roots: installed.json version 0.6.0 with 3 plugins all beta, three manifests written with absolute assembly paths, installer persisted, console stayed open (AFTER printed)

## Plan
1. Add-in (WmpToolsManager) and installer exit fix in parallel; register projects; catalog check.
2. Gate, review, ship.
3. Tag v0.6.0, watch the release workflow, verify the published one-liner into a temp root, record evidence, retro.

## Progress log
- 2026-09-22T21:30Z - Opened from main a08ef2a. Release tooling, plugin.json files, workflow, root README, CONTRIBUTING and ship.md were produced ahead of the branch (verified green locally); icon assets for the third add-in exist. Version bumped to 0.6.0; test-release.sh wired into the root gate as step 9b. Workers A1 (add-in, Opus) and A2 (installer exit handling, Sonnet) dispatched.
- 2026-09-22T22:25Z - A2 done: Stop-Install throws under iex, exits from a file; two test cases added. Advisor made the failure line truthful for mid-install failures ($script:AddinsRootTouched). A1 done: projects/wmp-tools-manager (7 projects, GUID 39625833-F960-4BDE-9EB1-F1E7F8F8013B), registered in the sln. A3 done: installer persisted to the state root on every install; add-in prefers it for rollback. build-release.ps1 refuses an add-in project without plugin.json. Backlog row for CONTRIBUTING prerequisites closed.
- 2026-09-22T22:21Z - Gate 1: all steps green except changelog staleness from the version bump (expected before the commit). Tests 290 + 12 (tools manager), 55 + 6 (exporter), 255 + 9 (naming); packaging OK x3; live-evidence OK; release-test OK (10 cases); no leaks; mutation 92.05 / 92.44 / 77.46 % over thresholds 85 / 85 / 70.
- 2026-09-22T22:45Z - Reviewer round 1: FAIL (1 blocker: success `exit 0` closes the console under iex; 3 should-fix: untruthful part-way message, two-plugin pinning in test-release.sh, no catalog uniqueness check; 3 nits). All fixed failing-first by B1/B2 and the advisor; -DistRoot added so the catalog test never touches dist/. Gate 2: check: OK (tests 290+12, 55+6, 255+9; packaging OK x3; live-evidence OK; build-release-test OK; release-test OK 16 cases; no leaks; mutation 92.05/92.44/77.46 %).
- 2026-09-22T23:20Z - PR #15 opened at 77b81e3; CI success (push + pull_request). Codex left 4 threads (catalog path traversal P1, dropped plugins left installed P1, concurrent apply launches P1, x86 runtime accepted P2), all valid; fixed failing-first by C1/C2 plus the advisor's addinTemplate derivation. Gate 3: check: OK (tests 342+12, 55+6, 255+9; packaging OK x3; live-evidence OK; build-release-test OK 7 cases; release-test OK 20 cases; no leaks; mutation 92.57/92.47/79.31 %).

## Review verdict
<!-- written ONLY by the independent reviewer -->

Round 1 (2026-09-22T22:40Z, reviewer agent, Opus, read-only): FAIL - 1 blocker (success `exit 0` closes the console under `irm | iex`), 3 should-fix (part-way failure message claimed an archive that did not exist; test-release.sh pinned two plugins instead of the requirement; no catalog uniqueness check in build-release.ps1), 3 nits (README "nothing is changed"; rollback stranded a newer-release-only plugin; stale mutation figure).

Round 2 (2026-09-22T23:05Z, same reviewer): PASS - all seven findings verified fixed in the working tree, each with a test that fails without the fix; no regression (file mode still exits 2 and 5), no test or gate removed, no new dependency, no secrets. Non-blocking: step 9b comment in check.sh described one script (fixed by the advisor after the verdict); rollback moves paths named by installed.json in the user's own %LOCALAPPDATA%, archived not deleted.

## Retro

Confirmed-good call worth a pending lesson (step 4, not yet human-validated, so not proposed): running an independent adversarial review before the first push caught a blocker (console closing on success) that 16 green test cases and the full gate could not see, because no test executed the success path under Invoke-Expression. The gate now has that test.

Delivery evidence: local scripts/check.sh check: OK three times (last: tests 342 + 12, 55 + 6, 255 + 9; packaging OK x3; live-evidence OK; build-release-test OK; release-test OK 20 cases; no leaks; mutation 92.57 / 92.47 / 79.31 %); PR #15 CI success for 5fd2ebad076d0cd74ed1d15c6c58a487ea915108; merged as 8e6e4abb3e667b087240b6f51750f05cf6f1a65d; main CI success for that SHA; release run https://github.com/srinator22/Smart-Manufacturing-Macro/actions/runs/35798325491 (success) published https://github.com/srinator22/Smart-Manufacturing-Macro/releases/tag/v0.6.0 (assets: WmpInventorTools-0.6.0.zip 405769 B, SHA256SUMS.txt, Install-WmpInventorTools.ps1; zip sha256 2aba092aac87e668d02e78b7d465a2ae2d3af6393f9cbf837146282d7cfd5765); one-liner verified 2026-09-22T23:42Z under Windows PowerShell 5.1 into isolated roots: installed.json version 0.6.0 with 3 plugins all beta, three manifests written with absolute assembly paths, installer persisted, console stayed open (AFTER printed).

Residual gaps carried to BACKLOG: no live Inventor session has yet applied an update through the WMP Tools Manager dialog (all three plugins stay beta); the release workflow's notes reuse the whole 0.6.0 changelog section because 0.4.0 and 0.5.0 were never tagged.
