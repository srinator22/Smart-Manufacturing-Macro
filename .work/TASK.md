# Task: phase2-hierarchy
Mode: autopilot
Branch: codex/phase2-hierarchy
Date: 2026-09-16

## Goal
Deliver the Phase 2 Smart Manufacturing Exporter slice: recursive assembly discovery, a hierarchical tree, tri-state selection, selectable recursive parts and assembly scopes, correct occurrence quantities, and unique-document STEP export. Land the slice with the three review-identified defects in the same surface repaired.

## Non-goals
- Inventor Browser folders, automatic OTS/Fastener exclusions, persistent classifications, rules, naming policies, progress/cancellation, DXF/PDF, and Inventor versions other than 2027.
- Claiming live three-level Inventor acceptance before the updated add-in is exercised interactively.

## Budget
- Wall-clock: 2 hours
- Subagents / workflow runs: 4
- Retries per failing step: 2
- Escalate to human when: an Inventor 2027 recursive API contract cannot be verified or the remaining usage budget cannot support a safe merge.

## Acceptance criteria
- [x] InventorAdapter recursively snapshots unsuppressed occurrences through at least three assembly levels and returns only COM-free models. -> installed API evidence, unit contracts, and Release build
- [x] The Core/Application hierarchy preserves distinct occurrences while aggregating unique source documents case-insensitively with correct total quantities. -> unit tests
- [x] Export scope supports top-level parts, recursive parts, assemblies only, and assemblies plus parts without silently including unsupported nodes. -> unit tests
- [x] A WPF TreeView presents root, subassembly, and part nodes with tri-state selection; parent changes propagate downward and child changes recompute ancestors. -> view-model tests plus rendered inspection of `.work/jobs/phase2-ui.png`. The new gate test proves the window opens and raises no binding error; it does not by itself prove the template rendered, so the rendered evidence carries that half of the criterion
- [x] Expand All and Collapse All operate on the complete tree, and changing scope rebuilds a deterministic reviewed selection. -> view-model tests
- [x] Export planning emits each selected source document once, supports `.ipt` and `.iam`, and preserves non-overwrite, per-item isolation, precision, and no-source-save guarantees. -> workflow tests and adapter inspection
- [x] README and compatibility documentation describe Phase 2 behavior and unclaimed live validation. -> README contract and inspection
- [x] D1: the Phase 1 flat row view model, dead since the tree replaced it, no longer ships in the UI assembly. -> archived to `.archive/`, Debug build green
- [x] D2: the scope selector derives its items from `ExportScopeMode` so a new scope cannot reach the enum, the tests, and the plan while staying invisible in the window. -> failing-first regression plus view-model tests
- [x] D3: mutation testing covers Application as well as Core with enforced thresholds instead of `--break-at 0`. -> Core 80.00% at threshold 75, Application 85.11% at threshold 80; UI cannot be analyzed by Stryker 5.0.0 and is a documented BACKLOG gap, not a silent drop
- [x] D4: the Smart Export window opens. `Run.Text` binds TwoWay by default and was bound to the read-only `DocumentKind` and `Quantity`, so `Show()` threw `XamlParseException` and the add-in would have crashed on first use. -> failing-first render test that shows the window and fails on any WPF binding error
- [x] D5: occurrences of one source document share a single selection state, so no checkbox can appear to exclude a document that another occurrence still exports. -> three failing-first regressions plus rendered confirmation
- [ ] Generated changelog, complete gate, independent review, PR CI, and exact-main CI pass. -> ship procedure. Version stays 0.4.0: it is unreleased (no `v0.4.0` tag), so these fixes fold into its section

## Plan
1. Verify recursive Inventor 2027 API members and define the COM-free tree contract.
2. Implement recursive scan, aggregation, scope filtering, and unique export planning with tests.
3. Implement the tri-state WPF hierarchy and scope/expand controls with tests.
4. Repair D1, D2, and D3 with a failing check before each fix.
5. Render-inspect, document, review, verify, install, and deliver through gated CI.

## Progress log
- 2026-09-16T12:43Z - Opened the Phase 2 hierarchy task from clean green main; user requested maximum verified progress within the remaining usage window.
- 2026-09-16T12:56Z - Implemented recursive scanning, four scopes, tri-state tree selection, `.ipt`/`.iam` unique export, documentation, and version 0.4.0. Full local `scripts/check.cmd` passed with 49 tests and an 80.00% mutation score; independent review remains.
- 2026-09-17T08:25Z - Repaired reviewer-identified regression assertions. Independent Sol review passed; full local gate passed with 50 tests and an 80.00% mutation score. Live rendered inspection and delivery remain.
- 2026-09-17T08:31Z - GitHub review caught mixed-scope assembly omission before merge. Added a failing regression test, then separated each node's own document selection from its aggregate tri-state value; targeted regression passes.
- 2026-09-17T08:40Z - GitHub re-review caught quadratic `CanExport` reevaluation for parent selection. Added a failing notification-count regression and changed recursive selection to emit one completion notification per user action; focused regressions pass.
- 2026-09-17T08:48Z - Final GitHub re-review identified intermediate ancestor aggregation still scaling quadratically for wide trees. Suppressed refresh while a parent batch is active and retained the single final recomputation.
- 2026-09-17T09:26Z - Session audit found the local gate could not complete: `projects/smart-manufacturing-exporter/scripts/check.sh` hard-fails without pwsh, which was absent from this machine. Installed PowerShell 7.6.6; the packaging test now reports `packaging: OK`.
- 2026-09-17T09:30Z - Audit also found three defects in the Phase 2 surface: a dead flat row view model, a hardcoded scope combo box that cannot drift-check against `ExportScopeMode`, and mutation testing scoped to Core alone with `--break-at 0`. Opened D1-D3 and archived the dead view model.
- 2026-09-17T09:33Z - Porting the UI render harness to the Phase 2 tree exposed D4, a release blocker: `SmartExportWindow.Show()` threw `XamlParseException` because `Run.Text` binds TwoWay by default against the read-only `DocumentKind` and `Quantity`. The add-in would have crashed the first time a user opened Smart Export. 48 unit tests, an independent review and three GitHub review rounds all missed it because nothing in the gate ever rendered the window. Replaced the `Run` composition with single-binding `TextBlock`s and added a gate test that shows the window on an STA thread and fails on any WPF data-binding trace.
- 2026-09-17T09:45Z - First successful render exposed D5: the tree selects per occurrence while export plans per unique source document, so clearing one occurrence of a repeated document silently exported it anyway through its sibling. Human chose shared per-document selection. Occurrences of one document now share a single selection state, proven by three failing-first regressions and re-render.
- 2026-09-17T09:50Z - Local gate is runnable again end to end on this machine after the pwsh install; rendered evidence captured at `.work/jobs/phase2-ui.png`.
- 2026-09-17T10:40Z - Independent review returned FAIL on F1 (quadratic peer refresh, third recurrence of this class) and F2 (transient tri-state plus duplicate ancestor notification). Root cause was interleaving own-state writes with aggregate recomputation. Replaced it with three strict phases: apply own-state, settle aggregates bottom-up, then settle affected ancestors deepest-first over a merged ancestor set. `isApplyingSelection` became structurally unnecessary and was removed. The re-review later showed the once-only notification claim was still false for repeated sub-assemblies; see the 11:40Z entry.
- 2026-09-17T10:40Z - The first work-bound metric was wrong: counting `CalculateSelection` entries is ~2N whether or not the bug is present, because the cost is element visits inside each call. Corrected it to count `1 + Children.Count` per call. Measured on the unfixed algorithm: 40,800 visits at N=200 and 643,200 at N=800, a 15.76x ratio for a 4x size increase, so quadratic. After the fix: 403 at N=200, asserted under a 6N bound. This is the guard that was missing through all three recurrences.
- 2026-09-17T10:55Z - F3 addressed: the render test asserted only the absence of failures, so deleting the `HierarchicalDataTemplate` left it green. It now asserts realized `TreeViewItem` containers (at least one per selectable node, derived from the view model), tri-state checkboxes, and the rendered text including `[Assembly]`, `[Part]` and the duplicated part's `Qty`, so a wrong `StringFormat` fails even though it raises no binding error. Proven by mutation: commenting out the template makes the test fail, and the XAML was restored byte-identical.
- 2026-09-17T11:00Z - F4 addressed: `mutation.sh` now requires positive evidence of a scored run (`The final mutation score is`) instead of matching Stryker's failure wording, which would pass an unscored run if that wording changed. Re-rendered `.work/jobs/phase2-ui.png` after the algorithm change; output is unchanged.
- 2026-09-17T11:10Z - Full gate re-run AFTER the `mutation.sh` change: 54 unit and 5 architecture tests pass, packaging OK, no leaks, Core 80.00% at threshold 75 and Application 85.11% at threshold 80, both through the new positive score check. Only the generated changelog date is stale, which ship step 5 regenerates.
- 2026-09-17T12:10Z - R1 and R2 repaired: the unordered peer pass and the ancestor-only settle were merged into one `SettlePeersAndAncestors` set, built chains-first so ancestor-closure holds, then settled deepest-first. A new test builds the repeated sub-assembly shape, subscribes to every node rather than the root, and was observed red ("SubB:1 raised 2 IsSelected notifications") before the fix. Third independent review returned PASS. Full gate after the change: 55 unit and 5 architecture tests, packaging OK, no leaks, Core 80.00% at threshold 75, Application 85.11% at threshold 80, aggregation delta still 403 at N=200 under the 6N bound.
- 2026-09-17T11:40Z - Independent re-review confirmed F1, F3 and F4 fixed and the final selection state correct in every constructed case, but returned FAIL on R1: the Phase 2 peer pass is unordered, so a touched peer that is an ancestor of another touched peer is recomputed against its stale descendant. With two occurrences of one sub-assembly under `AssembliesAndParts`, clearing one makes the other publish an indeterminate value and then its final value - two notifications, the first wrong. R2: the existing once-only guard observes only the root and uses non-exportable intermediate assemblies, so it cannot fail for R1. Remediation: settle peers and all collected ancestors as one deepest-first set, and guard it with a test that repeats an assembly document with selectable children and checks every node, not just the root.

## Review verdict
PASS (2026-09-17, independent reviewer, third pass, fresh context)

Two earlier passes returned FAIL and are kept here because they are the record of what this change cost.

- Pass 1 FAIL: F1 quadratic aggregation (third recurrence of that class on this file), F2 transient tri-state plus duplicate ancestor notification, F3 a render guard that could not fail if the template were deleted, F4 an unscored-run sentinel matching failure wording, F5 the archive reason owed to the commit body.
- Pass 2 FAIL: F1, F3 and F4 confirmed fixed, but R1 found the F2 class surviving for repeated sub-assemblies (the touched-peer pass was unordered, and a peer can be the ancestor of another peer), and R2 found the once-only guard could not fail for it because it observed only the root over non-exportable intermediates.
- Pass 3 PASS: R1 closed by deriving the algorithm rather than reading its comments. `SettlePeersAndAncestors` walks every ancestor chain before adding the touched peers, so `CollectAncestorChain`'s early exit never stops at a set that is not ancestor-closed; the merged set provably covers every node whose aggregate can change; and depth-descending is a valid topological order because a node's aggregate reads only its own state and its depth+1 children. Verified against the R1 shape, a peer-ancestor pair separated by a non-exportable intermediate, reconverging chains, a document repeated at two depths, three nested same-document assemblies, root `SelectAll`/`SelectNone`, and a scope rebuild. The reviewer independently re-derived the 403-visit figure from the code. F1, F3 and F4 re-confirmed intact; no test, gate or threshold deleted, skipped or loosened; no new dependencies; no secrets.

Non-blocking items raised in pass 3 and closed before commit: the `<= 1` notification assertion was vacuous for a node that never notified, so it now asserts exactly one notification for a node whose value changed and none for a node whose value did not; and two comments claiming every node is "notified exactly once" now state the real guarantee. Remaining: the `.archive` move carries its kernel rule 11 reason in the commit body, and the UI mutation gap is recorded in BACKLOG.md.

## Retro
<!-- filled by docs/procedures/retro.md -->
