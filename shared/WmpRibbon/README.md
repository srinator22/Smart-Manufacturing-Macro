# WmpRibbon

Provides the single shared "WMP Custom Tools" Inventor ribbon tab (internal name `WMP.CustomTools`) on the Assembly ribbon, so multiple independent WMP add-ins present their commands under one tab instead of each creating its own.

## Contract

- `WmpRibbonTab.TabDisplayName` / `WmpRibbonTab.TabInternalName` are the tab's fixed display and internal names.
- `WmpRibbonTab.EnsureTab(application, ribbonName, clientId)` returns the shared tab, creating it only if it does not already exist. Whichever add-in activates first creates the tab under its own `clientId`; every add-in that activates afterward finds the existing tab instead of creating a duplicate.
- `WmpRibbonTab.EnsurePanel(tab, displayName, internalName, clientId)` returns a panel on the shared tab by the caller's own internal name, creating it only if it does not already exist.
- Both methods hold no static or cached COM state; every call re-resolves the tab or panel from the live Inventor object model.
- The whole file compiles only when `INVENTOR_INTEROP` is defined (the installed Inventor interop assembly is present), matching the pattern used by `SmartManufacturingExporter.InventorAdapter`.

## Consumers

- Smart Manufacturing Exporter (`projects/smart-manufacturing-exporter`) - adds its "Smart Export" panel to the shared tab.
- File Naming Manager (`projects/file-naming-manager`) - adds its own panel to the shared tab.

## Rules

- Each add-in owns only its own panel on the shared tab; it never removes, renames, or reconfigures the tab itself.
- No add-in ever calls `RibbonTab.Delete` on the shared tab.
- No add-in assumes it is the tab's creator - the creator is whichever add-in's `Activate(FirstTime)` runs first in a given Inventor session.
