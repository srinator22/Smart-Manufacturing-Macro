# Shared Inventor components

This folder is reserved for code, schemas, fixtures, or build tooling used by at least two Inventor projects.

Do not move project-specific code here in anticipation of reuse. A shared component must define a stable contract, list its consumers, avoid leaking Inventor COM types into pure logic, and be registered in `InventorScripts.sln` when it contains .NET code.

## Components

- [WmpRibbon](WmpRibbon/README.md) - the shared "WMP Custom Tools" Inventor ribbon tab and the theme- and scale-aware ribbon icon loader. Consumers: Smart Manufacturing Exporter, File Naming Manager, WMP Tools Manager. Tests: `WmpRibbon.UnitTests`.
- [WmpIconRenderer](WmpIconRenderer/README.md) - build tooling that renders every project's ribbon icon masters to PNGs and verifies them in `scripts/check.sh`. Consumers: every project with a ribbon command. Authoring rules: [docs/rules/ribbon-icons.md](../docs/rules/ribbon-icons.md).
