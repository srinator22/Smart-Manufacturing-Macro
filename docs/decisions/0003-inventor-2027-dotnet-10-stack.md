# ADR 0003: Inventor 2027 and .NET 10 stack

Date: 2026-09-13
Status: accepted

## Context

The product is an in-process Autodesk Inventor manufacturing export add-in with a large WPF selection interface. The development machine has Inventor 2027 and its API v31 interop assembly installed. Autodesk documents Inventor 2027 as supporting .NET 10. The product brief requires actual API verification and forbids invented API members or translator identifiers.

The add-in handles engineering source files and manufacturing outputs, so source mutation, output collision handling, COM threading, and deterministic selection rules are safety boundaries rather than UI preferences.

## Decision

- Target Inventor 2027 only for the initial product.
- Use C# 14, .NET 10.0.401, WPF, and x64.
- Keep pure domain logic in Core, workflows and ports in Application, local system adapters in Infrastructure, Inventor COM access in InventorAdapter, presentation in UI, and host composition in AddIn.
- Reference Inventor interop only when Phase 1 API code is introduced and verify each API surface against Autodesk documentation or the installed Inventor 2027 interop assembly.
- Use central NuGet versions, committed lockfiles, built-in .NET analyzers, warnings as errors, `dotnet format`, xUnit, architecture tests, Stryker.NET, gitleaks, and git-cliff.
- Keep user data local, version JSON schemas, self-host shipped assets, and use version-stamped caches if caching is introduced.

## Rejected alternatives

- .NET Framework 4.8 and Inventor 2024: rejected because the user explicitly selected Inventor 2027 and Autodesk documents 2027 on .NET 10.
- .NET 8: rejected because Inventor 2027 supports .NET 10 and .NET 10 is the current LTS toolchain on the machine.
- WinForms: rejected because the specification calls for a dense, synchronized hierarchy/grid/details experience better suited to WPF binding and templating.
- A single add-in project: rejected because it would spread COM types through rules, UI, persistence, and tests, preventing isolated verification.
- NuGet interop packages chosen by name alone: rejected until a package is verified against the installed Inventor 2027 API; the local Autodesk assembly and official documentation are authoritative.

## Consequences

The first implementation phase must run on Windows with Inventor 2027 for integration verification. Most behavior remains testable without Inventor. Adding support for another Inventor release requires a separate compatibility decision and evidence, not a silent target-framework change.
