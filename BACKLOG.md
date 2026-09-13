# Backlog

Remaining work only. Priorities P0-P3 follow `docs/procedures/audit.md`.

| Item | Priority | Reason | Status |
| ---- | -------- | ------ | ------ |
| Document and register each new Inventor automation under `projects/` | P0 | Preserves independent ownership and full-workspace verification | ongoing |
| Phase 1 API research and compatibility matrix for Inventor 2027 | P0 | Every host and translator call must be verified before implementation | next |
| Phase 1 add-in server, ribbon command, active-assembly validation, top-level unique-part scan, checklist, and safe STEP export | P0 | Establishes the first end-to-end testable product slice | open |
| Phase 1 per-user `.addin` packaging, debug profile, install, and uninstall procedure | P0 | Required to validate the add-in in Inventor 2027 | open |
| Inventor integration fixture: five unique parts, select three, export exactly three STEP files | P0 | Phase 1 acceptance test from the product specification | open |
| Phase 2 recursive hierarchy, quantities, unique documents, and tri-state selection | P1 | Core engineering structure workflow | open |
| Phase 3 Inventor Browser folders and configurable OTS/Fastener exclusions | P1 | Critical organization-aware behavior | open |
| Phase 4 persistent classifications and Do Not Traverse purchased assemblies | P1 | Performance and export-scope safety | open |
| Phase 5 deterministic rule engine, priority, and selection reasons | P1 | Makes automatic decisions predictable and explainable | open |
| Phase 6 naming, existing-file status, collision handling, progress, cancellation, and logs | P1 | Completes professional STEP export behavior | open |
| Phases 7-10 manufacturing formats, professional UI, quick export, context menu, and hardening | P2 | Later capability waves after the foundation is verified | open |
| Update `actions/checkout` after verifying a Node.js 24-native release | P2 | GitHub CI currently succeeds but warns that checkout v4 targets deprecated Node.js 20 | open |
