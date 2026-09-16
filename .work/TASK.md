# Task: phase2-hierarchy
Mode: autopilot
Branch: codex/phase2-hierarchy
Date: 2026-09-16

## Goal
Deliver the Phase 2 Smart Manufacturing Exporter slice: recursive assembly discovery, a hierarchical tree, tri-state selection, selectable recursive parts and assembly scopes, correct occurrence quantities, and unique-document STEP export.

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
- [ ] A WPF TreeView presents root, subassembly, and part nodes with tri-state selection; parent changes propagate downward and child changes recompute ancestors. -> view-model tests plus rendered inspection
- [x] Expand All and Collapse All operate on the complete tree, and changing scope rebuilds a deterministic reviewed selection. -> view-model tests
- [x] Export planning emits each selected source document once, supports `.ipt` and `.iam`, and preserves non-overwrite, per-item isolation, precision, and no-source-save guarantees. -> workflow tests and adapter inspection
- [x] README and compatibility documentation describe Phase 2 behavior and unclaimed live validation. -> README contract and inspection
- [ ] Version, generated changelog, complete gate, independent Sol review, PR CI, and exact-main CI pass. -> ship procedure

## Plan
1. Verify recursive Inventor 2027 API members and define the COM-free tree contract.
2. Implement recursive scan, aggregation, scope filtering, and unique export planning with tests.
3. Implement the tri-state WPF hierarchy and scope/expand controls with tests.
4. Render-inspect, document, version, review, verify, install, and deliver through gated CI.

## Progress log
- 2026-09-16T12:43Z - Opened the Phase 2 hierarchy task from clean green main; user requested maximum verified progress within the remaining usage window.
- 2026-09-16T12:56Z - Implemented recursive scanning, four scopes, tri-state tree selection, `.ipt`/`.iam` unique export, documentation, and version 0.4.0. Full local `scripts/check.cmd` passed with 49 tests and an 80.00% mutation score; independent review remains.
- 2026-09-17T08:25Z - Repaired reviewer-identified regression assertions. Independent Sol review passed; full local gate passed with 50 tests and an 80.00% mutation score. Live rendered inspection and delivery remain.
- 2026-09-17T08:31Z - GitHub review caught mixed-scope assembly omission before merge. Added a failing regression test, then separated each node's own document selection from its aggregate tri-state value; targeted regression passes.
- 2026-09-17T08:40Z - GitHub re-review caught quadratic `CanExport` reevaluation for parent selection. Added a failing notification-count regression and changed recursive selection to emit one completion notification per user action; focused regressions pass.

## Review verdict
PASS

- Recursive propagation now invokes one shared completion callback only after the externally requested tree change finishes, so `CanExport` is reevaluated once instead of once per changed node. Internal child updates and ancestor tri-state recomputation remain callback-free, mixed-scope document selection remains correct, and scope rebuild notifications are preserved. The two focused regressions pass; no new correctness, boundary, dependency, or test-coverage issue was found.

## Retro
<!-- filled by docs/procedures/retro.md -->
