# Backlog

Remaining work only. Priorities P0-P3 follow `docs/procedures/audit.md`.

| Item | Priority | Reason | Status |
| ---- | -------- | ------ | ------ |
| Document and register each new Inventor automation under `projects/` | P0 | Preserves independent ownership and full-workspace verification | ongoing |
| Inventor integration fixture: five unique parts, select three, export exactly three STEP files | P0 | Phase 1 acceptance test from the product specification | open |
| STEP precision live acceptance: export a spline-bearing part with Low and Highest and compare import fidelity, time, and file size | P0 | Confirms Inventor 2027 honors the documented translator tolerance before release claims | open |
| Phase 2 live Inventor 2027 acceptance of the three-level hierarchy, quantities, unique documents, and shared-document tri-state selection per `docs/PHASE2_TEST_PLAN.md` | P0 | Implementation is complete and merging, but live Inventor acceptance has not been run, so hierarchy and selection behavior remain unverified against real Inventor 2027 | open |
| Mutation-test `SmartManufacturingExporter.UI` selection logic | P2 | Stryker 5.0.0 cannot analyze `net10.0-windows` WPF projects, so the tri-state selection logic that has been the source of four separate defects has no mutation coverage; either make WPF projects analyzable by Stryker or extract the selection logic into a plain non-WPF project Stryker can target | open |
| Phase 3 Inventor Browser folders and configurable OTS/Fastener exclusions | P1 | Critical organization-aware behavior | open |
| Phase 4 persistent classifications and Do Not Traverse purchased assemblies | P1 | Performance and export-scope safety | open |
| Phase 5 deterministic rule engine, priority, and selection reasons | P1 | Makes automatic decisions predictable and explainable | open |
| Phase 6 naming, existing-file status, collision handling, progress, cancellation, and logs | P1 | Completes professional STEP export behavior | open |
| Phases 7-10 manufacturing formats, professional UI, quick export, context menu, and hardening | P2 | Later capability waves after the foundation is verified | open |
| Update `actions/checkout` after verifying a Node.js 24-native release | P2 | GitHub CI currently succeeds but warns that checkout v4 targets deprecated Node.js 20 | open |
