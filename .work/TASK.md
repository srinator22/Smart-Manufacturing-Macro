# Task: file-naming-followups
Mode: autopilot
Branch: task/file-naming-followups
Date: 2026-09-23

## Goal
Close every open File Naming Manager item that does not touch Vault, plus two workspace defects found on the way: per-row include and exclude in the grid so the operator controls exactly which files a run renames; one source of truth for the scope exclusion list with a test pinning the enumeration and the row rule to it; restore mutation coverage of NumberAllocator (Stryker safe mode); make all three add-ins ensure the shared tab and their panel on every Activate rather than only on FirstTime so uninstalling one cannot strand another's panel; fix scripts/new-task.sh direct-to-main detection; add the checker test script for check-live-evidence.sh.

## Non-goals
- Any Vault SDK call or Vault-managed rename execution (the Vault server is being resized; docs/VAULT_RENAME_DESIGN.md stays the plan of record).
- Live acceptance in Inventor (separate P0 backlog rows).
- Behaviour changes beyond per-row include/exclude.

## Budget
- Wall-clock: one session
- Subagents / workflow runs: 6
- Retries per failing step: 2
- Escalate to human when: the NumberAllocator restructure would change any allocation result pinned by a golden test, or a follow-up needs Vault.

## Acceptance criteria
- [ ] NamingRowViewModel.IsIncluded (two-way, default true for actionable rows, disabled for Action None); FileNamingViewModel passes excluded FullPaths into RenameOptions.ExcludedPaths (case-insensitive) and re-plans on every toggle; excluded rows get Action None with reason "Excluded by the operator." and never allocate a number or block others; the Vault plan export honours the same exclusions; Select all / Select none buttons. -> unit tests (excluded unmanaged row: no operation, no number consumed; excluded managed row: no Vault instruction; toggle back re-plans), render test asserts the checkbox column, screenshot inspected
- [ ] Core NamingScopeRules.ExcludedFolderNames and IsExcludedFolderName are the only source of the exclusion list; PhysicalNamingFileSystem and FileNamingWorkflow use it; a test asserts enumeration and row rule agree on a fixture tree containing every excluded folder; NamingPorts.cs references the constant. -> tests
- [ ] Core mutation run shows no Safe Mode line and includes NumberAllocator; threshold re-measured and set by the standing rule; no golden allocation value changed. -> mutation.sh output recorded here
- [ ] All three StandardAddInServer.Activate methods ensure the tab and panel and re-add missing buttons on every activation without duplicating controls (CommandControls checked by internal name first); compatibility matrices record the behaviour as live-unverified. -> compile, inspection, architecture tests unchanged
- [ ] scripts/new-task.sh matches only a Project decisions line that records direct-to-main as the choice; scripts/test-new-task.sh covers the current AGENTS.md line and a synthetic direct-to-main line and is wired into scripts/check.sh. -> gate
- [ ] projects/file-naming-manager/scripts/test-check-live-evidence.sh covers missing stamp, non-PASS, malformed, stale hash, fresh; wired into the project check.sh. -> gate
- [ ] README Use section and TEST_PLAN section 2 updated; BACKLOG rows closed. -> inspection
- [ ] Review PASS, gate green, PR CI and main CI green. -> ship procedure

## Plan
1. Workers in parallel on disjoint files: W1 include/exclude (Application + UI + tests + docs); W2 NamingScopeRules + NumberAllocator safe mode + mutation re-measure (Core + Infrastructure + Application workflow line + tests); W3 shared-tab Activate in three add-ins + matrices; W4 workspace scripts (new-task.sh + test, test-check-live-evidence.sh, check.sh wiring).
2. Advisor: gate, render and inspect the window, review, ship, retro.

## Progress log
- 2026-09-22T23:35Z - Branch opened from main 8e6e4ab (later fast-forwarded to 25c31a4 after the release retro merged). Advisor wrote Core NamingScopeRules first; four workers dispatched on disjoint files: W1 include/exclude (Application, UI, tests, README, TEST_PLAN), W2 scope single source + NumberAllocator restructure + mutation re-measure, W3 tab and panel ensured on every Activate in all three add-ins + ContainsControl in shared WmpRibbon, W4 new-task.sh detection + test, test-check-live-evidence.sh, gate wiring.
- 2026-09-22T23:58Z - All four workers reported green in isolation: W1 282 unit tests incl. 4 exclusion + 4 view-model + render assertions, screenshot .work/jobs/naming-ui-include.png inspected (enabled boxes on actionable rows, disabled on Action None rows, Select all / Select none present); W2 Core mutation 92.92 % with no Safe Mode line and NumberAllocator in the table (32 killed), thresholds unchanged, golden values unchanged; W3 sln build 0 warnings, architecture tests 6 + 9 + 12; W4 new-task-test OK, live-evidence-test OK. Advisor added the NumberAllocator null-guard test W2 flagged. FileNamingWorkflow.cs changed, so the live smoke is being re-run to refresh the evidence stamp before the gate.
- 2026-09-23T00:02Z - Live smoke PASS (52 assertions, Inventor 2027 build 310192060, sourceHash c8e5e93a...); stamp refreshed. Gate 1: check: OK - new-task-test OK, project-readmes OK (3), tests 283 + 9 (naming), 55 + 6 (exporter), 342 + 12 (tools manager), packaging OK x3, live-evidence-test OK, live-evidence OK, build-release-test OK, release-test OK, no leaks, mutation naming 93.33 / 94.86 / 97.67 % (no Safe Mode, NumberAllocator measured), exporter 80.00 / 85.11 %, tools manager 92.57 / 92.47 / 79.31 %. Recorded measurements updated in mutation.sh and README.
- 2026-09-23T00:15Z - Round 1 findings fixed by the advisor: four BACKLOG rows closed with reasons and the shared-tab row reworded; three headers state that the tab, panel and buttons are ensured on every Activate and FirstTime is unused; NamingScopeRules.RenamedOriginalsFolderName is the single literal (used by the archive path and its message) with a test pinning it to the exclusion list; ExcludedPaths comment names the part/assembly restriction; README step 5 states that an excluded assembly is still saved when a child beneath it is renamed; mutation.sh fails on any Safe Mode line. Unit tests 284 green; format and analyzers clean. FileNamingWorkflow.cs changed again, so the live smoke is re-run before the gate.
- 2026-09-23T00:30Z - Gate 2 on the committed tree: check: OK - new-task-test OK, project-readmes OK (3), tests 284 + 9, 55 + 6, 342 + 12, packaging OK x3, live-evidence-test OK, live-evidence OK (sourceHash 22552ef5...), build-release-test OK, release-test OK, no leaks, mutation naming 93.31 / 94.85 / 97.67 % (no Safe Mode), exporter 80.00 / 85.11 %, tools manager 92.57 / 92.47 / 79.31 %. Reviewer round 2 PASS. Recorded measurements aligned to this run.

## Review verdict
<!-- written ONLY by the independent reviewer -->

Round 1 (2026-09-23T00:10Z, reviewer agent, Opus, read-only): FAIL - criterion 7 unmet (four BACKLOG rows this change implements were still open; the shared-tab row's reason was falsified by the change); three StandardAddInServer headers still claimed FirstTime controls UI creation; a second `_renamed-originals` literal in FileNamingWorkflow.cs with no test pinning it to NamingScopeRules; two nits (ExcludedPaths contract comment silent on drawing rows; README overstated that an excluded assembly leaves the plan entirely although it is still saved when a child beneath it is renamed); one observation (mutation.sh did not assert the absence of a Safe Mode line). Criteria 1 to 6 verified met on the evidence read.

Round 2 (2026-09-23T00:25Z, same reviewer): PASS - all round 1 items verified closed (BACKLOG rows done with reasons, headers accurate, RenamedOriginalsFolderName the single literal used by construction, contract comment and README clause added, mutation.sh fails on a Safe Mode line); no gate, test or threshold deleted or loosened; refreshed stamp covers the re-edited workflow. Explicitly outstanding at verdict time: gate 2 result and PR/main CI.

## Retro
<!-- filled by docs/procedures/retro.md -->
