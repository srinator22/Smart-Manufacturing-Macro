# Architecture

## Scope

File Naming Manager is an in-process Autodesk Inventor 2027 add-in. It reads the active assembly and its project folder, classifies every file name against the WMP part-numbering scheme, allocates the next free numbers, previews a rename plan, and applies the plan through Inventor's own `SaveAs` so that references stay intact. It never renames a Vault-managed file in v1; those are planned and exported for Vault Explorer.

Inventor 2027 is the only host. The solution targets C# 14, .NET 10, WPF, x64. The installed `Autodesk.Inventor.Interop.dll` major version is 31.

## Modules and dependency direction

```text
FileNamingManager.AddIn
  -> FileNamingManager.UI
  -> FileNamingManager.InventorAdapter
  -> FileNamingManager.Infrastructure
  -> FileNamingManager.Application
       -> FileNamingManager.Core
  -> shared/WmpRibbon (ribbon tab only)

Inventor 2027 COM -> InventorAdapter -> Application ports -> Core models
Filesystem        -> Infrastructure  -> Application ports -> Core models
WPF               -> UI              -> Application workflow -> Core models
```

- `Core` owns the naming grammar: `FileNameParser`, `FileNameFormatter`, `NamingToken`, `ItemNumber`, `ProjectNumber`, `NumberAllocator`, `NamingReport` and the finding codes. It has no UI, filesystem, or Autodesk dependency, and it never parses a path, only a file name.
- `Application` owns the `FileNamingWorkflow` (Analyze, Plan, Execute) and defines the ports `IInventorNamingGateway`, `INamingFileSystem` and `IClock`. It decides which files are renameable, which are Vault-managed, which are blocked, and in what order operations run.
- `Infrastructure` implements `INamingFileSystem` over the real filesystem: project-scope enumeration excluding `OldVersions`, `_V`, `3rd Party Hardware`, `Content Center Files` and `_renamed-originals` (the archive folder itself, so an Apply's own originals never re-enter scope as duplicates on the next Analyze), `_V\<name>.v` tracker detection, moving originals, writing the manifest.
- `InventorAdapter` is the only general project that references Inventor interop. It flattens the active assembly into COM-free `DocumentSnapshot`s and executes `SaveAs`, `Save` and Part Number writes.
- `UI` is the WPF window and view model over COM-free models.
- `AddIn` owns the `ApplicationAddInServer`, the two ribbon buttons on the shared `WMP Custom Tools` tab, and composition.

`FileNamingManager.ArchitectureTests` parses project references and fails when this direction changes.

## Analyze flow

1. `AddIn` checks that a saved assembly is active and calls `FileNamingWorkflow.Analyze` on Inventor's STA thread.
2. `InventorAdapter` walks `AllReferencedDocuments` once and returns, for each unique document: path, kind, root flag, `IsModifiable`, `Dirty`, parents, and the current Part Number iProperty.
3. `Application` locates the project root (`ProjectRootLocator`), enumerates the project scope through `INamingFileSystem`, parses every file name with `Core`, and marks each document Vault-managed or unmanaged.
4. `NumberAllocator` computes gaps, duplicates and the next free number per series over the whole scope, so files outside the open assembly consume numbers; drawings and presentations raise the series maximum like any numbered file but are never reported as duplicates of their model, because they take the model's number by design rather than owning one, and a series with every number taken reports itself exhausted rather than allocating past `9999`/`A999`.
5. `ProjectNumberSuggestion` proposes a project number from the root file name, then sibling numbered files, then a `P124`-style folder. The user must confirm or enter it; nothing is planned without one.
6. `UI` shows one row per document: current name, state, Vault state, proposed name, action and reasons, plus project-level findings.

## Plan and apply flow

1. `Plan` walks every Part/Assembly row, gating each numbered-defect class behind its option (`NormalizeMalformed` for already-numbered defects, `RenameUnnumbered` for defects needing a new or reused number token) before it proposes a name and allocates a number, so a row excluded by an option never burns a number. Rows with no description (`NumberedWithoutDescription`), a duplicated number, a number series that is exhausted, or a path outside the project scope (outside the project root, or inside `OldVersions`, `_V`, `3rd Party Hardware`, `Content Center Files` or `_renamed-originals`) are never proposed; they surface as a row reason instead, and an out-of-scope row never blocks the rows the project does own. `Plan` produces `RenameOperation`s for unmanaged files, ordered leaf-first (parts and their companion drawings, sub-assemblies deepest first, root last), and `VaultRenameInstruction`s for managed files, in the same leaf-first order. Numbers are allocated to both so they cannot collide.
2. Blockers stop the whole plan and name the offending document: root dirty; root has missing references; a renamed document or a parent whose reference must be saved is not `IsModifiable`; the proposed target path already exists; two or more operations would rename to the same target; a proposed name would duplicate a file name used elsewhere in scope (Inventor's unique-filenames mode); a companion drawing is Vault-managed or read-only; a number series a row actually needed is exhausted; or a document is also referenced by another open document outside the active assembly and that reference is not a handled companion. A plan with any blocker has no executable operations.
3. `Execute` first pre-opens every companion drawing across the whole plan, before any rename runs. A drawing only has its reference to a model rewritten in memory if it is already open when that model's `SaveAs` runs, and an earlier operation's rename can affect a later operation's drawing, so opening lazily per operation would be too late; a companion that fails to open takes only its own operation out of the run.
4. Each operation then runs in isolation: `SaveAs(newPath, false)` for the model, then each companion drawing, then the Part Number write; failures are recorded per item and later operations continue. A model rename is recorded the instant it lands, before the companion and Part Number steps, so a failure in either leaves the item a partial success (`ModelRenamed` true, `Succeeded` false, the error naming the step) whose parents are still saved and whose original is still archived, rather than a renamed file nothing tracks. Distinct parents are saved once, deepest first, root last, resolved through the rename map so a parent that was itself renamed is saved under its new path rather than the pre-rename path recorded in the plan. If any parent save fails, execution stops there: every original is left in place, because an unsaved parent still references the old file name on disk, and the parent-save failures are returned instead of an archived manifest.
5. Only once every parent save succeeds is the manifest written, with every entry unarchived, before the first file is moved - so a run interrupted mid-archive still leaves a complete record of what was intended. Each successfully renamed original is then moved to `<project root>\_renamed-originals\<UTC timestamp>\`, preserving relative paths, in isolation per file so one locked original cannot strand the rest; the manifest is rewritten with each entry's outcome (`Archived` true, or `Archived` false with `Error` set) once archival finishes. An original that does not resolve to a path under the project root is refused before the move, because its relative path would walk the archive back out of `_renamed-originals`, and it is recorded as an archive failure. Nothing is deleted.
6. Part Number is written only when the current value is empty or equals the old file stem.
7. The UI's row list is plan-derived, not analysis-derived: `NamingRowViewModel.Refresh` looks each row up in the current `RenamePlan`'s operations and Vault instructions on every re-plan, so the grid always shows the action, proposed name, and (when excluded) the specific option that excluded it, for the project number and options currently selected - never a stale action left over from the original Analyze-time preview.

## Vault boundary

Vault state is read from the working folder trackers, never from the Vault server, in v1. The full procedure for renaming managed files through the Vault SDK, and why it is deferred, is in `VAULT_RENAME_DESIGN.md`.

## Threading and COM lifetime

All Inventor calls stay on Inventor's owning STA thread. Analysis of file names and allocation are pure and could run elsewhere, but the whole interaction is short and synchronous in v1. The adapter passes typed snapshots inward and does not call `Marshal.ReleaseComObject` indiscriminately.

## Verification boundaries

- Core, Application and Infrastructure: deterministic unit tests including the real P124 file names as golden cases, and mutation tests with enforced thresholds.
- UI: a render test that shows the window on an STA thread and asserts realized rows, because a view-model-only suite cannot see XAML.
- Adapter and AddIn: compile against the installed interop; live behaviour is exercised by the temp-folder smoke harness described in `TEST_PLAN.md` and never against the Vault workspace.
- Project boundaries: architecture tests.
