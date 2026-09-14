# Task: phase1-mvp
Mode: autopilot
Branch: codex/phase1-mvp
Date: 2026-09-14

## Goal
Deliver the first testable Smart Manufacturing Exporter slice for Autodesk Inventor 2027: a loadable add-in with an Assembly ribbon command, safe active-document validation, a COM-free selection workflow over unique top-level part documents, and explicit STEP export to a user-selected folder.

## Non-goals
- Recursive subassembly traversal, Browser folders, classification memory, rules, quick export, drawing/PDF/DXF export, or production-scale performance tuning.
- Silent source-document saves or changes, automatic overwrite, cloud services, and Inventor versions other than 2027.
- Claiming the five-part Inventor acceptance scenario passes until it is run interactively in Inventor 2027.
- Implementing the Smart Render Pack, Part Number & Filename Manager, or 3D Print Production Manager beyond recording the user-supplied future concepts.

## Budget
- Wall-clock: 2 hours
- Subagents / workflow runs: 6
- Retries per failing step: 2
- Escalate to human when: Inventor must be driven interactively for the final five-part host acceptance test or a verified API contract cannot be implemented safely.

## Acceptance criteria
- [x] Inventor 2027 compatibility evidence records interop version 31.0.19201.6, verified Phase 1 members, and the installed STEP translator identity and supported source types. -> judgment: compare against installed Inventor 2027 interop XML/assembly and translator add-in manifest
- [x] The add-in implements `ApplicationAddInServer`, retains command-event lifetime, and creates one Smart Export command in the Assembly ribbon without placing workflow logic in the event handler. -> architecture tests plus Release build
- [x] Invoking Smart Export without an active assembly returns the explicit message `Smart Export requires an active Inventor assembly.` without scanning or exporting. -> unit tests
- [x] A top-level scan converts Inventor state to COM-free snapshots, skips suppressed and non-part occurrences with explicit reasons, deduplicates part source paths case-insensitively, and counts duplicate quantities. -> unit tests
- [x] The Phase 1 WPF checklist supports Select All, Select None, output-folder selection, and exporting only checked unique parts. -> view-model unit tests plus judgment: rendered UI inspection
- [x] Export planning is side-effect free, rejects missing/unwritable destinations and existing output conflicts before translation, and never saves or changes source documents. -> unit tests
- [x] The Inventor adapter resolves the installed STEP translator `{90AF7F40-0C01-11D5-8E83-0010B541CD80}` and uses `TranslatorAddIn.SaveCopyAs` for selected part documents only. -> adapter contract tests where COM-free plus installed API evidence
- [x] A per-user Inventor 2027 `.addin` manifest and reversible install/uninstall scripts are documented and validated without requiring machine-wide deployment. -> script tests or dry-run validation plus documentation inspection
- [x] The complete solution restores locked, builds with zero warnings, passes all tests and architecture gates, and passes `scripts/check.sh`. -> `scripts/check.sh`
- [x] Interactive acceptance steps document the five-unique-part, select-three, exactly-three-STEP scenario and leave its result unclaimed until run in Inventor 2027. -> documentation inspection
- [x] The future idea log records the three supplied Inventor 2027 product concepts, first milestones, major risks, and likely reuse without scaffolding speculative projects. -> `IDEAS.md`

## Plan
1. Record a focused Inventor 2027 API compatibility matrix from installed authoritative artifacts.
2. Define COM-free Phase 1 models, ports, workflow, export-plan safety rules, and failing unit tests.
3. Implement the Inventor adapter and add-in composition/ribbon boundary against verified interop contracts.
4. Implement the bounded WPF checklist and selection/export view model.
5. Add the per-user manifest, install/uninstall scripts, and exact build/debug/integration-test instructions.
6. Run the focused tests, full canonical gate, rendered UI inspection, and independent reviewer gate.
7. Commit, push, open a pull request, monitor exact-SHA CI, merge only when green, and monitor main CI.

## Progress log
- 2026-09-14T00:25Z - Verified Inventor 2027 interop assembly 31.0.19201.6, `ApplicationAddInServer`, ribbon APIs, `TranslatorAddIn.SaveCopyAs`, and installed STEP translator manifest GUID and source support.
- 2026-09-14T00:25Z - Opened `codex/phase1-mvp` from clean synchronized main and recorded autopilot acceptance criteria.
- 2026-09-14T00:31Z - Added the installed-artifact compatibility matrix; COM-free workflow tests pass 21/21, including execution-time overwrite-race protection and per-item failure isolation.
- 2026-09-14T00:42Z - Logged Smart Render Pack, Part Number & Filename Manager, and 3D Print Production Manager as future ideas with bounded first milestones and reuse gates.
- 2026-09-14T10:27Z - Rendered and inspected the WPF checklist, repaired collapsed file/source columns, and passed the canonical gate: 29 unit plus 3 architecture tests, packaging, secret scans, zero-warning builds, and 100.00% mutation score.
- 2026-09-14T10:44Z - Independent review found and the implementation repaired effective directory permission detection, atomic non-overwriting STEP finalization, and activation-manifest version drift; reviewer verdict is PASS.
- 2026-09-14T10:46Z - Versioned the Phase 1 feature wave as 0.2.0 and passed the final canonical gate: 31 unit plus 4 architecture tests, packaging, secret scans, zero-warning Debug and Release builds, changelog freshness, and 100.00% mutation score.

## Review verdict

PASS

The three blocking findings are repaired:

- Effective `FILE_ADD_FILE` access is checked without creating a probe file; `C:\Windows\System32` now returns false for the current user.
- STEP output is translated to a unique same-directory temporary `.step`, finalized with non-overwriting `File.Move`, and cleanup preserves primary failures.
- An architecture test enforces manifest version equality with `VersionPrefix` plus `.0`.

Focused verification passed: 27 unit tests, the manifest-version architecture test, Release build with 0 warnings and 0 errors, formatting, and `git diff --check`. Live Inventor 2027 acceptance remains explicitly unclaimed as required.

## Retro
<!-- filled by docs/procedures/retro.md -->
