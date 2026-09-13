# Workspace architecture

## Scope

Inventor Scripts is a monorepo for independent Autodesk Inventor 2027 automations. The root owns the toolchain, verification policy, CI, cross-project decisions, and project catalog. Each folder under `projects/` owns one product's requirements, architecture, implementation, and tests.

## Dependency direction

```text
InventorScripts.sln
  -> projects/<product>/src
  -> projects/<product>/tests
  -> shared/<component> only when two products consume it

Inventor COM -> product host adapter -> product application contracts -> pure product domain
```

- Projects do not reference another project's implementation.
- Cross-project reuse enters `shared/` only after two concrete consumers establish a stable contract.
- Autodesk COM types remain at each product's host edge. Pure rules, data transformations, naming, and validation remain testable without Inventor.
- The root solution contains every .NET project so one build detects broken cross-project assumptions.
- Non-.NET scripts provide `projects/<name>/scripts/check.sh`; the canonical root check discovers and runs each one.
- Every project or shared component with production C# provides its own `scripts/mutation.sh`; changed-file routing invokes the owning mutation suite and rejects empty or unscored runs.

## Project contract

Each `projects/<name>/` folder includes a README with purpose, Inventor version, delivery form, setup, and verification commands. Non-trivial projects also include `docs/` for requirements and architecture, `src/` for production artifacts, and `tests/` for automated and documented Inventor integration scenarios.

The current Smart Manufacturing Exporter architecture is documented in [its project folder](../projects/smart-manufacturing-exporter/docs/ARCHITECTURE.md).

## Version and data boundaries

The initial workspace releases together from the root `VersionPrefix` and annotated Git tags. If projects later need independent release trains, that change requires an ADR and per-project changelogs before version sources diverge.

Only source code, self-hosted UI assets, schemas, and small sanitized fixtures belong in Git. Autodesk binaries, customer CAD, outputs, user settings, local classifications, and production logs remain outside the repository.
