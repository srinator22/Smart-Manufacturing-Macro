# Architecture

## Scope

Smart Manufacturing Exporter is an in-process Autodesk Inventor 2027 add-in. It reads the active assembly through the Inventor API, converts COM state into typed session models, applies deterministic rules and explicit user overrides, previews a side-effect-free export plan, and invokes format-specific exporters only after validation.

Inventor 2027 is the only initial host. The solution targets C# 14, .NET 10, WPF, and x64. The installed `Autodesk.Inventor.Interop.dll` major version is 31.

## Modules and dependency direction

```text
SmartManufacturingExporter.AddIn
  -> SmartManufacturingExporter.UI
  -> SmartManufacturingExporter.InventorAdapter
  -> SmartManufacturingExporter.Infrastructure
  -> SmartManufacturingExporter.Application
       -> SmartManufacturingExporter.Core

Inventor 2027 COM -> InventorAdapter -> Application contracts -> Core models
Filesystem/JSON  -> Infrastructure  -> Application contracts -> Core models
WPF              -> UI              -> Application workflows -> Core models
```

- `Core` owns immutable or explicitly stateful domain models, classifications, selection reasons, rule priority, naming, collision detection, validation results, and exporter contracts. It has no UI, filesystem, logging, or Autodesk dependencies.
- `Application` owns scan, preview, and export use cases. It defines ports for Inventor snapshots, settings, clocks, filesystems, logs, and exporters.
- `Infrastructure` implements local JSON settings, presets, classification memory, filesystem inspection, and CSV/JSON/TXT logs.
- `InventorAdapter` is the only general-purpose project allowed to reference Inventor interop. It scans assemblies and browser folders, reads metadata, detects document state, and invokes verified translators.
- `UI` contains WPF views and view models. It receives COM-free models and calls application workflows.
- `AddIn` owns `ApplicationAddInServer`, ribbon integration, command event lifetime, Inventor application lifetime, and dependency composition. It may reference interop only where host integration requires it.

`SmartManufacturingExporter.ArchitectureTests` parses project references and fails when this direction changes without an explicit architecture decision.

## Scan data flow

1. `AddIn` validates that the active document is an assembly and starts a session on Inventor's owning STA thread.
2. `InventorAdapter` traverses occurrences and browser nodes once, deduplicates source documents early, stops at purchased or Do Not Traverse boundaries, and caches metadata for the session.
3. The adapter releases unnecessary COM reachability by returning internal identifiers and typed snapshots instead of passing raw COM objects inward.
4. `Application` invokes Core rules to classify nodes, compute selection reasons, quantities, and tri-state state.
5. `UI` renders the hierarchy and detail grid from the same session model so selection remains synchronized.

## Export data flow

1. Preview builds an immutable export plan with target paths, formats, naming results, existing-file status, warnings, and errors.
2. Validation rejects unresolved inputs, naming collisions, illegal paths, ambiguous overwrites, and unavailable translators before any output write.
3. `Application` runs independent export operations through `IManufacturingExporter` implementations.
4. A component failure is recorded and later components continue unless cancellation or a session-wide fatal condition occurs.
5. Logs record source identity, selected preset, planned action, result, and output path without private content or credentials.

## Threading and COM lifetime

Inventor API calls remain on the Inventor STA thread unless Autodesk explicitly documents an operation as safe elsewhere. CPU-only classification, filtering, naming, and filesystem comparison may run asynchronously after COM data has been copied into internal models. Cancellation is cooperative between export operations. The code does not indiscriminately call `Marshal.ReleaseComObject`; it minimizes retained COM references and documents ownership at each adapter boundary.

## Persistence and data

User settings, presets, and classification memory are versioned JSON outside the repository under the user's local application-data area. Customer assemblies, drawings, derived manufacturing files, and production logs never enter Git. The repository may contain only sanitized, source-linked fixtures and self-hosted UI assets.

## Verification boundaries

- Core and Application behavior is covered by deterministic unit and mutation tests.
- Project dependencies are enforced by architecture tests.
- Inventor behavior is covered by version-specific integration scenarios using Inventor 2027 and documented test assemblies.
- Software verification does not imply manufacturing, field, or regulatory validation.
