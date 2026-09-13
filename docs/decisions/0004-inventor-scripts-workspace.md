# ADR 0004: Inventor Scripts workspace layout

Date: 2026-09-13
Status: accepted

## Context

The repository will host multiple independent Autodesk Inventor automations, not only Smart Manufacturing Exporter. A single-product root would mix product-specific requirements with shared tooling and make later tools appear to be modules of the exporter.

## Decision

- Theme the repository as Inventor Scripts and target Inventor 2027 only for now.
- Place every automation under `projects/<kebab-case-name>/` with its own README, docs, source, and tests.
- Keep one root `InventorScripts.sln` containing all registered .NET projects.
- Reserve `shared/` for stable code with at least two real consumers.
- Keep CI, pinned dependencies, agent configuration, security policy, and the canonical check at the repository root.
- Release the workspace together until evidence justifies independent project versioning.

## Rejected alternatives

- One repository per script immediately: rejected because the user requested one working folder for multiple Inventor projects and the shared quality harness would be duplicated.
- Flat source folders at the root: rejected because product ownership, requirements, and tests become ambiguous as the catalog grows.
- A general shared framework now: rejected because only one product exists, so its contract would be speculative.

## Consequences

Smart Manufacturing Exporter moves intact under `projects/smart-manufacturing-exporter/`. New projects remain isolated but inherit the same toolchain. The root check and solution must be updated whenever a new compiled project is registered.
