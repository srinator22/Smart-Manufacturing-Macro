# Shared Inventor components

This folder is reserved for code, schemas, fixtures, or build tooling used by at least two Inventor projects.

Do not move project-specific code here in anticipation of reuse. A shared component must define a stable contract, list its consumers, avoid leaking Inventor COM types into pure logic, and be registered in `InventorScripts.sln` when it contains .NET code.

## Components

- [WmpRibbon](WmpRibbon/README.md) - the shared "WMP Custom Tools" Inventor ribbon tab. Consumers: Smart Manufacturing Exporter, File Naming Manager.
