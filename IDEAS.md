# Inventor scripts idea log

This log preserves future Autodesk Inventor 2027 product ideas without turning them into active roadmap commitments. An idea receives a folder under `projects/` only when it is promoted into a scoped task with acceptance criteria, API evidence, and an owner.

## Idea index

| ID | Product idea | Status | Suggested first milestone | Likely shared capabilities |
| --- | --- | --- | --- | --- |
| IDEA-001 | Smart Render Pack | Logged | Headless Blender proof | STEP transfer, component identity, naming, job progress |
| IDEA-002 | Part Number & Filename Manager | Logged | Read-only part-number auditor | Component identity, metadata, conflict validation |
| IDEA-003 | 3D Print Production Manager | Logged | Read-only print dashboard | Assembly scanning, quantities, identity, STEP freshness |

## IDEA-001 - Smart Render Pack

### Product goal

Generate high-quality images for selected Inventor parts, components, subassemblies, or assemblies while treating Blender as a managed headless rendering backend. Normal users never need to open or configure Blender.

### Core experience

- Select Inventor content and choose Smart Render.
- Pick a preset, views, background, ground, shadows, lighting, quality, resolution, and destination.
- Export temporary geometry plus explicit Inventor orientation and units metadata.
- Run a pinned portable Blender worker invisibly, with GPU detection and CPU fallback.
- Show cancellable per-stage progress and an isolated result for every requested image.
- Save deterministic PNG or JPEG outputs using safe naming tokens.

### Suggested first milestone

Prove a pinned Blender runtime can run headlessly against one controlled test model, construct a deterministic studio scene, render one image, report structured progress, and pass an image sanity check. This milestone does not integrate with Inventor or silently download software.

### Important boundaries and risks

- Installing the managed render engine requires an explicit user action and verified official download hashes.
- Blender, STEP import, and any importer licenses must permit the planned distribution model.
- STEP import quality, units, hierarchy, colors, normals, and tessellation require measured compatibility evidence.
- The managed runtime must not alter a user's separate Blender installation or preferences.
- Inventor COM work stays on Inventor's owning STA thread; background work receives only files and typed job data.
- Large batches require bounded disk use, cleanup, cancellation, crash recovery, and GPU/CPU diagnostics.

### Reuse candidates

- Smart Manufacturing Exporter's future component selection, safe STEP export, naming, validation, and progress contracts.
- Part Number & Filename Manager identity and naming metadata.

## IDEA-002 - Part Number & Filename Manager

### Product goal

Make engineering identity fast and safe by auditing, allocating, editing, and eventually applying project-aware Part Numbers, descriptions, and filenames across Inventor parts, assemblies, and associated drawings.

### Core experience

- Scan unique documents and show current Part Number, description, filename, revision, type, and where-used state.
- Detect missing, invalid, duplicate, or mismatched identities without changing source files.
- Allocate the next project-aware number using an explicit gap/reuse policy and a concurrency-safe registry.
- Preview every iProperty, filename, drawing, and reference change before applying it.
- Apply renames as a validated transaction with collision checks, dependency analysis, post-change verification, history, and recovery.
- Provide quick assignment for one component and efficient bulk assignment for new designs.

### Suggested first milestone

Build a read-only auditor for unique `.ipt`, `.iam`, `.idw`, and `.dwg` documents. Report missing and duplicate Part Numbers, invalid formats, filename mismatches, and associated drawings. Do not allocate numbers, edit properties, or rename files in this milestone.

### Important boundaries and risks

- Inventor references and drawing links must never be broken by a filesystem-only rename.
- Number allocation requires a clear source of truth, locking, reservation expiry, and multi-user concurrency behavior.
- Every write path needs preview, conflict detection, transaction journaling, rollback guidance, and post-rename validation.
- Part Number, filename, file identity, and component occurrence are distinct concepts and must remain distinct in the model.
- Automatic quick fixes are limited to changes proven safe; ambiguous changes require explicit review.

### Reuse candidates

- Canonical component identity and metadata contracts for every other Inventor script.
- Shared naming sanitization and collision validation after a second real consumer exists.

## IDEA-003 - 3D Print Production Manager

### Product goal

Show what still needs to be printed for an Inventor assembly and whether existing physical prints still match the current design. Derive requirements from the assembly instead of maintaining a separate spreadsheet.

### Core experience

- Scan components classified as 3D Printed, deduplicate source documents, and calculate physical quantities from occurrences.
- Track material, color, spares, queued, printing, successful, failed, remaining, and reprint-required quantities.
- Create human-readable print jobs containing current STEP files and a simple manifest for use in Bambu Studio.
- Associate printed quantities with a geometry signature so design changes do not silently count obsolete prints as current.
- Support partial completion, requeue, manual acceptance of existing prints after change, build readiness, and concise CSV output.
- Preserve project history in a versioned, backed-up local store without expanding into ERP, slicer, or printer-control software.

### Suggested first milestone

Build a read-only dashboard for one active assembly. Identify manually classified 3D-printed components, show unique documents and correct occurrence quantities, and provide search/filtering. No production quantity database or print-job mutation exists in this milestone.

### Important boundaries and risks

- Required assembly quantity and physical printed stock are separate records.
- Printed quantity is meaningful only with its geometry signature and build context.
- Geometry fingerprinting must be deterministic, versioned, and tested against real geometry changes.
- Persistent production history requires migration, backup, locking, and corruption recovery.
- Changed material or color behavior must remain explicit and configurable.
- V1 does not replace Bambu Studio, control printers, manage filament inventory, or become an ERP system.

### Reuse candidates

- Smart Manufacturing Exporter's assembly scanning, unique-document model, selection, validation, and STEP freshness/export services.
- Part Number & Filename Manager component identity and metadata.

## Suggested sequencing

1. Complete and validate the Smart Manufacturing Exporter foundation now in progress.
2. Promote the Part Number & Filename Manager read-only auditor because stable identity benefits both remaining ideas.
3. Promote the 3D Print Production Manager read-only dashboard after recursive scanning and quantity behavior are proven.
4. Run the Smart Render headless Blender proof independently before coupling an external runtime to Inventor.

Shared code moves into `shared/` only after two implemented products use the same stable contract. Similar planned features alone do not justify a shared abstraction.

## Promotion checklist

Before changing an idea from Logged to Planned:

- Confirm the user problem and first-phase acceptance scenario.
- Verify the required Inventor 2027 API surface and any external runtime behavior.
- Record data ownership, source-file safety, installation, and recovery boundaries.
- Decide what is genuinely reusable from an implemented project.
- Create `projects/<kebab-case-name>/` with its own requirements, architecture, tests, and backlog.
- Keep unverified later phases out of the first implementation task.
