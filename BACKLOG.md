# Backlog

Remaining work only. Priorities P0-P3 follow `docs/procedures/audit.md`.

| Item | Priority | Reason | Status |
| ---- | -------- | ------ | ------ |
| Document and register each new Inventor automation under `projects/` | P0 | Preserves independent ownership and full-workspace verification | ongoing |
| Inventor integration fixture: five unique parts, select three, export exactly three STEP files | P0 | Phase 1 acceptance test from the product specification | open |
| STEP precision live acceptance: export a spline-bearing part with Low and Highest and compare import fidelity, time, and file size | P0 | Confirms Inventor 2027 honors the documented translator tolerance before release claims | open |
| Phase 2 live Inventor 2027 acceptance of the three-level hierarchy, quantities, unique documents, and shared-document tri-state selection per `docs/PHASE2_TEST_PLAN.md` | P0 | Implementation is complete and merging, but live Inventor acceptance has not been run, so hierarchy and selection behavior remain unverified against real Inventor 2027 | open |
| Document the local verification prerequisites (`dotnet`, `gitleaks`, `git-cliff`, PowerShell 7) in CONTRIBUTING.md | P2 | `scripts/check.sh` cannot complete without pwsh, which is undocumented; a contributor on a machine without it sees the gate stop at the packaging test with no guidance on what to install | open |
| Mutation-test `SmartManufacturingExporter.UI` selection logic | P2 | Stryker 5.0.0 cannot analyze `net10.0-windows` WPF projects, so the tri-state selection logic that has been the source of four separate defects has no mutation coverage; either make WPF projects analyzable by Stryker or extract the selection logic into a plain non-WPF project Stryker can target | open |
| Phase 3 Inventor Browser folders and configurable OTS/Fastener exclusions | P1 | Critical organization-aware behavior | open |
| Phase 4 persistent classifications and Do Not Traverse purchased assemblies | P1 | Performance and export-scope safety | open |
| Phase 5 deterministic rule engine, priority, and selection reasons | P1 | Makes automatic decisions predictable and explainable | open |
| Phase 6 naming, existing-file status, collision handling, progress, cancellation, and logs | P1 | Completes professional STEP export behavior | open |
| Phases 7-10 manufacturing formats, professional UI, quick export, context menu, and hardening | P2 | Later capability waves after the foundation is verified | open |
| File Naming Manager: execute Vault-managed renames through the Vault SDK per `projects/file-naming-manager/docs/VAULT_RENAME_DESIGN.md` | P1 | v1 plans and exports managed renames but does not execute them; the user's stated use is renaming old projects already checked in, and every P124 file including unnumbered ones has a `_V` tracker. Requires a non-production vault or per-run human approval, and the production server must first stop rejecting large commits (error 109, 2026-09-22) | open |
| File Naming Manager: live Inventor 2027 acceptance per `projects/file-naming-manager/docs/TEST_PLAN.md` sections 2-4 | P0 | Only the temp-folder smoke harness exercises the rename path; interactive blockers and the Vault Explorer procedure are unverified | open |
| Shared `WMP Custom Tools` tab ownership when the creating add-in is uninstalled | P2 | The tab is created with the first-activating add-in's ClientId and each add-in only ensures it on FirstTime; whether Inventor removes the tab with its creator, stranding the other add-in's panel, is unverified live | open |
| Restore mutation coverage of `FileNamingManager.Core/NumberAllocator.cs` | P2 | Stryker 5.0.0 hits an unidentified mutation that fails to compile (CS0165 at 36:16) and enters safe mode, marking every mutant in that file CompileError, so the 92.68 percent Core score does not include the allocator; restructure the affected `token` declaration or exclude the mutant by comment so the file is measured | open |
| Fix `scripts/new-task.sh` direct-to-main detection | P2 | Its grep `'- Git workflow:.*direct-to-main'` matches the AGENTS.md sentence stating direct-to-main is NOT the workflow, so the script silently stays on `main` instead of branching; found 2026-09-23 when scaffolding the file-naming-manager task | open |
| Update `actions/checkout` after verifying a Node.js 24-native release | P2 | GitHub CI currently succeeds but warns that checkout v4 targets deprecated Node.js 20 | open |
