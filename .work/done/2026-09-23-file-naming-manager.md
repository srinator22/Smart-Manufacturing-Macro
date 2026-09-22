# Task: file-naming-manager
Mode: autopilot
Branch: task/file-naming-manager
Date: 2026-09-23

## Goal
Add a second Inventor 2027 add-in, File Naming Manager, that analyzes and applies the WMP part-numbering scheme to the files of the active assembly, and consolidate both add-ins onto one shared "WMP Custom Tools" ribbon tab. The user saves new parts and assemblies with a plain description as the filename; the tool asks for the project number, allocates the next free number per series, renames the files through Inventor so references stay intact, flags duplicates, gaps and malformed names, and never writes a document that is not checked out to the user.

## Non-goals
- Executing renames of Vault-managed (checked-in) files. v1 detects them, plans them, and exports a Vault rename plan; execution through the Vault SDK is designed and recorded as the next task because it cannot be verified without a non-production vault, and the production server is currently rejecting large commits (error 109, 2026-09-22 incident).
- Changing the numbering scheme. The tool implements the live P124 scheme verbatim.
- Revision suffixes in filenames, iProperty schemes beyond Part Number, drawings or presentations that do not share a model's base name, Content Center or third-party hardware numbering, Inventor versions other than 2027.
- Pointing any automated run at the user's Vault workspace.

## Budget
- Wall-clock: overnight session
- Subagents / workflow runs: 12
- Retries per failing step: 2
- Escalate to human when: an Inventor 2027 API contract needed for a safe rename cannot be verified from the installed interop, or a rename path would require writing to a document that is not writable.

## Naming scheme (single source of truth for this task)
Canonical forms, taken from the live P124 GRM workspace on 2026-09-23:
- Part: `PPP-NNNN Description.ipt` e.g. `124-0002 Adaptor Plate Bottom.ipt`
- Sub-assembly: `PPP-ANNN Description (sub-assembly).iam` e.g. `124-A002 NDRM (sub-assembly).iam`
- Main assembly: `PPP-ANNN Description (main assembly).iam`; the root of the assembly the tool is run from is the main assembly, every other assembly is a sub-assembly
- Drawing or presentation sharing a model's base name: same stem, own extension
- PPP: three digits, 100-999. NNNN: 0001-9999. ANNN: A001-A999. One space between number and description. Description: the user's text with whitespace collapsed to single spaces and trimmed; case is never changed.

Recognized but never generated (analysis states): legacy `PNN Description.ipt`, revision-suffixed `101-0001-A0`, tagless assembly `124-A001 Desc .iam`, copy suffixes `Desc_1.iam`.

Number allocation: next = highest observed in the project scope + 1, per series (parts, assemblies). The project scope is every `.ipt`, `.iam`, `.idw`, `.dwg`, `.ipn` under the project root folder, recursively. The project root is the nearest ancestor of the root assembly whose folder name starts with `P` plus three digits or with three digits, else the root assembly's own folder. Excluded from scope: `OldVersions`, `_V`, `3rd Party Hardware`, `Content Center Files`. Only files whose parsed project equals the target project count toward allocation.

Findings the analyzer must report, each present in the real P124 folder: duplicate number (`124-A001` on three files, `124-A004` on two), sequence gap (`0021`), malformed whitespace (`124-0074  wakeup fan mesh cartirdge .ipt`), tagless assembly (`124-A001 Full Double Stack Mechanism .iam`), unnumbered (`Part1.ipt`, `gasket cutting template.ipt`), legacy prefix (`P74B 32.75mm 0.85.ipt`), and duplicate filenames anywhere in scope (the project uses Inventor's unique-filenames mode).

## Vault safety model (single source of truth for this task)
- A working-folder file is Vault-managed when a tracker `_V\<filename>.v` exists beside it; otherwise it is unmanaged (typically a new file not yet checked in).
- Unmanaged files are renamed in-session through `Document.SaveAs(newPath, false)`, leaf documents first, then sub-assemblies, then the root; parents are saved afterwards so their references persist. Originals are moved, never deleted, to `<project root>\_renamed-originals\<UTC timestamp>\` preserving relative paths, with a `manifest.json` of every move.
- Managed files are never renamed by v1. They appear in the preview as Vault-managed with the planned target name and are written to an exported Vault rename plan (leaf-first order, with the parent files whose references Vault must update) for execution in Vault Explorer's Rename command, which preserves history.
- Any document the plan would modify (a renamed file or a parent whose reference changes) must report `IsModifiable`; otherwise the plan is blocked with the document named. The plan is also blocked if the root assembly has unsaved changes or missing references.
- Setting the Part Number iProperty to the number token happens only when the current Part Number is empty or equals the old file stem.

## Acceptance criteria
- [x] Core parses every canonical and recognized legacy form and classifies each real P124 finding above; formatting round-trips; whitespace normalization never changes case. -> FileNamingManager.UnitTests (golden cases use file names only, no CAD)
- [x] Number allocation returns max+1 per series over a scope that includes files outside the open assembly, ignores excluded folders and other projects, and reports gaps and duplicates. -> unit tests
- [x] Project number is required, prefilled from root filename, then sibling numbered files, then a `P124`-style folder, and validated to 100-999. -> unit tests plus view-model tests
- [x] Rename planning orders leaf-first, blocks on unwritable, dirty or reference-missing documents with the offending document named, keeps Vault-managed files out of execution, and includes same-stem drawings. -> unit tests Evidence: FileNamingWorkflowTests incl. companion Vault/read-only blockers, external-parent blocker, unique-filenames collision blocker
- [x] Execution is per-item isolated, moves originals to `_renamed-originals` with a manifest, and never deletes. -> unit tests with a fake gateway and a temp filesystem Evidence: manifest written before moves and rewritten with per-entry outcomes; parent-save failures hold originals in place; archive failures isolated
- [x] A WPF window shows the report grid, project number box, mode (Analyze or Apply), Vault export, and an Apply button disabled with a reason until the plan is valid; it opens without binding errors and renders its rows. -> `FileNamingWindowRenderTests` (proven by a broken-binding mutation) and `.work/jobs/file-naming-ui.png`; the first render exposed D6 (rows showed the analysis preview, not the current plan), fixed before ship
- [x] Both add-ins place their commands on one `WMP Custom Tools` tab of the Assembly ribbon through `shared/WmpRibbon`, and the exporter no longer creates its own tab. -> architecture tests, compile with installed interop, documented in both READMEs Evidence: `OnlyTheSharedRibbonComponentCreatesRibbonTabs` in both architecture suites; exporter and naming AddIn compile against the installed interop
- [x] Every rename-path Inventor API member used is verified against the installed interop XML and recorded in the project's compatibility matrix. -> docs/INVENTOR_2027_API_COMPATIBILITY.md
- [x] Packaging installs and uninstalls the second add-in in an isolated Addins root, and the activation manifest version matches VersionPrefix. -> gate step 9 reports `packaging: OK` for both projects; `ActivationManifestVersionMatchesVersionPrefix` and `AddInManifestTemplateAndServerShareOneClientId` green
- [x] Mutation testing covers Core and Application with enforced thresholds. -> scripts/mutation.sh Evidence: Core 92.68 at 85, Application 94.32 at 85, Infrastructure 97.87 at 90 (2026-09-23)
- [x] README with all required sections, ARCHITECTURE, compatibility matrix, live TEST_PLAN including the Vault procedure, catalog row, BACKLOG updated. -> check-project-readmes.sh reports OK for 2 projects; VAULT_RENAME_DESIGN.md added as the record of the deferred managed-rename procedure
- [x] A live smoke test against generated fixtures in a temp folder (never the Vault workspace), run only if no Inventor process exists, renames a two-level assembly and reopens it with resolved references; if it cannot run, that is recorded as unclaimed. -> `.work/jobs/naming-live-smoke/smoke-20260922T174443Z.log` SMOKE PASS on the final build in Inventor 2027 build 310192060: external-parent blocker fired live with a second open assembly and stayed silent after it closed; four renames, four originals archived, cold reopen in a fresh process with HasReferencesMissing false and Part Numbers 901-A001, 901-A002, 901-0005, 901-0006
- [x] Version bumped to 0.5.0, changelog regenerated, independent review PASS, gate green, PR CI and exact-main CI green. -> `check: OK` locally; PR #11 CI terminal-success for e346fe3; merged as 2a79f0a; main CI terminal-success for 2a79f0a

## Plan
1. Core, Application, Infrastructure and their tests (WP1) in parallel with the shared ribbon and exporter migration (WP2) and the icon (WP6).
2. WPF UI and render test (WP3), then InventorAdapter, AddIn, packaging, manifest (WP4).
3. Register projects in the solution, architecture tests, mutation script, docs (WP5).
4. Live smoke harness in temp (WP7), review, gate, ship, retro.

## Progress log
- 2026-09-23T00:05Z - Opened from clean main. Surveyed the live Vault workspace: P124 GRM uses `124-NNNN Description.ipt` and `124-ANNN Description (main assembly|sub-assembly).iam`; found the real duplicate, gap, whitespace and tagless cases the analyzer must report. Read the 2026-09-22 Vault incident notes: the server is rejecting large commits, so no automated run touches the Vault workspace and Vault-managed renames are planned, not executed, in v1.
- 2026-09-23T00:06Z - `scripts/new-task.sh` stayed on main because its direct-to-main grep matches the AGENTS.md sentence that says direct-to-main is not the workflow; branched manually and logged the defect.

- 2026-09-23T01:40Z - WP1 (Core, Application, Infrastructure) landed with 109 tests, WP2 (shared `WmpRibbon` tab, exporter migrated), WP5a (README, packaging scripts) and WP6 (icon) landed; version bumped to 0.5.0. Advisor review of the Application found three defects in the advisor's own spec, queued as WP1b with failing-first tests: (1) companion drawings are renamed after their model, but only a drawing already open in the session has its reference rewritten by the model's SaveAs - an unopened drawing resolves to the original, which is moved away later; the gateway gains EnsureDocumentOpen and the workflow pre-opens companions. (2) A failed parent save is swallowed in Execute and originals are still moved, which would strand the unsaved parent's on-disk reference; parent-save failures become part of RenameExecution and originals stay in place when any parent save fails. (3) TryProposeFileName reuses a file's token even when that number is duplicated in scope, so the real duplicate `124-A001` copies would keep a duplicate number; duplicated tokens now yield no proposal and a reason. Also decided: NormalizeMalformed gates MalformedWhitespace, TaglessAssembly and RevisionSuffixed; RenameUnnumbered gates UnnumberedDescription, LegacyPrefix and CopySuffix.

- 2026-09-23T02:30Z - WP3 (WPF window, 120 tests, render test proven by a broken-binding mutation) and WP4a (Inventor adapter, 0 warnings against the installed interop, EnsureDocumentOpen included) landed. Solution now registers WmpRibbon and every naming project except the AddIn. WP1b (the four Application defects, failing-first) and WP4b (AddIn host on the shared tab) dispatched in parallel.

- 2026-09-23T03:20Z - WP1b landed (141 tests; four defects fixed failing-first), WP4b landed (AddIn on the shared tab, 7 architecture tests). Full solution: 0 warnings, 208 tests, locked restore clean. Rendered the window: every real P124 case classified correctly, but a row showed VaultRename for a malformed name while the normalize option was off, because rows came from the analysis preview rather than the plan. Opened D6: rows now derive action, proposed name and exclusion reason from the current plan. Mutation measurement, live temp-folder smoke run and independent review dispatched.

- 2026-09-23T03:45Z - Live smoke in a hidden Inventor 2027 (build 310192060) against a generated temp fixture: all four SaveAs renames succeeded, references were rewritten in session, Part Number iProperties set, the pre-numbered 901-0004 untouched. It then FAILED on D7: Execute saves parents by their pre-rename paths, so parents that were themselves renamed are not found and the saves fail; D2 correctly held the originals, leaving both name sets on disk and no manifest. Fix: resolve each parent through the rename map before saving. No unit test could have seen this; the live run is the only check that does. Also corrected TEST_PLAN: Inventor enumerates AllReferencedDocuments depth-first, so the sub-assembly's part takes 0005.

- 2026-09-23T04:20Z - D6 landed: rows derive action, proposed name and exclusion reason from the current plan (143 tests); re-render confirms the 0074 row now shows None with the exclusion reason. Independent review returned FAIL (R1-R5, recorded below); together with D7 these are dispatched as WP1c with failing-first tests, plus the non-blocking items 6, 7, 11 and 12 and a mutant-killing pass on FileNamingWorkflow (Application measured 62.10%). Single-tab rule mechanized in both architecture test suites; README Vault sentence corrected.

- 2026-09-23T05:10Z - WP1c landed: R1 fixed (companion drawings block on Vault-managed or read-only, named, before any rename in that operation); R2 fixed (manifest written before the first move with every entry unarchived, per-file archival isolated, manifest rewritten with each entry's Archived/Error outcome); R3 fixed (a numbered name with no description now parses as NumberedWithoutDescription and its token counts toward allocation, instead of Unparseable hiding it); R4 fixed (the option gate is evaluated before a number is allocated, so a gated-off row no longer burns a number); D7 fixed (parents are saved under their post-rename path, resolved through the rename map). 221 tests. Mutation re-measured: Core 92.68% (threshold 85), Application 94.32% (threshold 85), Infrastructure 97.87% (threshold 90); all above threshold. README, ARCHITECTURE and TEST_PLAN reconciled with the shipped code.

- 2026-09-23T05:40Z - Live smoke re-run after D7: SMOKE PASS. Four renames, zero parent-save or archive failures, manifest with four archived entries, and a genuinely cold reopen in a second hidden Inventor (0 documents open before Open) showing HasReferencesMissing false and the exact expected reference set with Part Numbers 901-A001, 901-A002, 901-0005, 901-0006; 901-0004 untouched. Both Inventor processes quit cleanly; nothing outside %TEMP% written, templates read from the active project read-only. Re-review and full gate in progress.

- 2026-09-23T06:30Z - WP1d landed (226 naming tests): root external parents read from ReferencingDocuments and rows with external parents skipped entirely; `_renamed-originals` excluded from scope; modifiability blocker limited to unmanaged rows; Vault instructions carry companion drawings; dead code removed. Full gate on the final tree: 295 tests, both packaging tests OK, no leaks, Core 92.68 at 85, Application 94.89 at 85, Infrastructure 97.92 at 90, exporter 80.00 at 75 and 85.11 at 80; only the changelog stamp stale before commit. Stryker safe-mode on NumberAllocator recorded in BACKLOG. Third review pass in progress.

- 2026-09-22T16:50Z - WP1e landed (229 naming tests): parent-modifiability blockers limited to unmanaged rows, external-parent exclusions carry a visible row reason, exported Vault plan lists blockers, port comment fixed. Smoke re-run on the final adapter build (with a second open assembly to exercise the external-parent path live), fourth review pass, and full gate dispatched in parallel. Timestamps from this entry on are the machine's UTC clock.

- 2026-09-22T16:53Z - Final smoke on the WP1e build: SMOKE PASS (log smoke-20260922T165242Z). Phase A with `other rig.iam` open: spacer plate row carried the external parent, the plan blocked naming both files, and the row left Operations and VaultInstructions. Phase B after closing it: zero blockers, four renames, zero parent-save or archive failures, manifest four archived entries, cold reopen in a second Inventor clean. Both Inventor processes quit; nothing outside %TEMP% written.

- 2026-09-22T16:55Z - Fourth review pass PASS. Full gate on the WP1e tree: 298 tests, both packaging tests OK, no leaks, all five mutation runs above threshold, changelog stamp the only stale item before commit. Proceeding to commit and ship.

- 2026-09-22T17:13Z - Pushed 4730970, PR #11 opened, CI terminal-success for that SHA. Merge blocked by four unresolved GitHub review threads, all confirmed legitimate: T1 referenced files outside the project scope can be proposed and their originals archived outside the root via `..` relative paths; T2 a model rename that succeeds before a companion or Part Number failure loses its bookkeeping; T3 same-stem companion drawings count as duplicate numbers; T4 an exhausted series throws out of Analyze. Dispatched WP1f with failing-first tests; smoke re-run and re-review follow before the threads are answered.

- 2026-09-22T17:45Z - WP1f landed (255 naming tests; Core 92.92, Application 94.89, Infrastructure 97.92): out-of-scope referenced files never proposed and archival refuses paths that escape the root; model rename bookkeeping recorded before companions and Part Number, with partial results carrying ModelRenamed; drawings no longer own numbers; exhausted series reported instead of thrown. Smoke re-run on this tree: SMOKE PASS (smoke-20260922T174443Z), 52 assertions, both phases, cold reopen clean. Fifth review and full gate in progress; the four GitHub threads are answered after they land.

## Review verdict
PASS (2026-09-23, independent reviewer, fifth pass, fresh context, WP1g tree)

Five review passes plus one GitHub review round were needed. Every FAIL was on a real defect and every defect was fixed with a failing test first:
- Pass 1 (R1-R5): companion drawings bypassed the Vault and modifiability guards; archival not per-item isolated with the manifest written last; a number without a description invisible to allocation; allocation before the option gate; README claims ahead of evidence.
- Pass 2 (F1, F2): the root snapshot's external parents were never read; the tool's own `_renamed-originals` folder re-entered the scanned scope.
- Pass 3 (B1, B2): the live smoke evidence predated the shipped adapter; the parent-modifiability blocker fired for Vault-managed rows.
- Pass 4: PASS on the pre-PR tree.
- GitHub review (T1-T4): referenced files outside the project scope could be renamed and archived outside the root; a model rename that succeeded before a companion or Part Number failure lost its bookkeeping; companion drawings counted as duplicate numbers; an exhausted series threw out of Analyze.
- Pass 5: T1, T2, T4 verified closed; the T3 fix had over-reached by dropping drawings from the series maximum (a lone `124-0080 X.idw` would have let 0080 be reissued) - repaired so drawings raise the maximum but are never duplicates of their model, with tests asserting the TASK.md rule rather than the implementation. PASS.

Found by rendering and by running rather than by review: D6 (grid showed the analysis preview) and D7 (parents saved by pre-rename paths). Live evidence: `smoke-20260922T174443Z.log` SMOKE PASS on the WP1f build, accepted for WP1g because the change is Core-pure and the fixture contains no drawing.

Not verified by the reviewer and carried by the advisor's gate runs: 255 / 8 / 6 / 55 tests, 0 warnings, format and analyzers clean, mutation thresholds 85 / 85 / 90, packaging OK for both add-ins, no leaks.

## Retro

Defects by discovery route (docs/procedures/retro.md step 1):

- Route (a), caught pre-merge by a check or review: R1-R5 (pass 1), F1-F2 (pass 2), B1-B2 (pass 3), T1-T4 (GitHub review), and the T3 over-reach (pass 5). Sixteen findings, every one a real defect, every one repaired with a failing test first. The gate worked; nothing recorded per step 2, with the exception below.
- Route (d), self-noticed and self-corrected: D1-D5 from the advisor's audit and review of WP1, D6 found by rendering the window, D7 found only by the live Inventor run. Nothing recorded per step 2, with the exception below.
- Route (b) escaped past merge: none. Route (c) reported by the human: none.

Two proposals were appended to docs/lessons/PENDING.md (2026-09-22T18:19Z) because the route classification hides the signal:

- Most route-(a) findings traced to the advisor's own port contracts and Plan rules, written before code and never reviewed on their own. A spec review before the first worker dispatch would have caught the companion, scope and external-parent gaps five review passes earlier.
- D7 was visible only to a live run; later the cited live evidence went stale against a changed adapter until a reviewer refused it; and the T3 repair went beyond its finding with tests that pinned the new behaviour instead of the spec rule. Live-evidence freshness and spec-quoting tests are the candidate mechanizations.

Regression tests verified present and observed failing before their fix: FileNamingProjectScopeTests, FileNamingPartialRenameTests, FileNamingPlanGuardTests, FileNamingExecutionTests, FileNamingWorkflowContractTests, the D1-D4 and D5-D7 tests in FileNamingWorkflowTests and FileNamingViewModelTests, ALoneDrawingRaisesTheMaximumAndIsNotADuplicate, and the render test proven by a broken-binding mutation.

docs/lessons/INDEX.md has no approved lessons, so no counter was incremented (step 6).

Delivery evidence: local `scripts/check.sh` `check: OK` (255 + 8 + 6 + 55 tests, both packaging tests, no leaks, mutation Core 92.92 / Application 94.89 / Infrastructure 97.92 against 85 / 85 / 90); PR #11 CI terminal-success for e346fe32db7a570f0aedc8decdf1e7b4ce6222bf; merged as 2a79f0ab649e201fbd1c395b19f4edbf1ac16617; main CI terminal-success for that SHA. Live: SMOKE PASS smoke-20260922T174443Z in Inventor 2027 build 310192060 against generated fixtures in %TEMP%.

Not claimed: interactive acceptance on real projects (TEST_PLAN sections 2-3), the Vault Explorer procedure (section 4), execution of Vault-managed renames, and live acceptance of the exporter. `scripts/new-task.sh` still stays on main when scaffolding (BACKLOG).
