# WmpRibbon

Provides the single shared "WMP Custom Tools" Inventor ribbon tab (internal name `WMP.CustomTools`) on the Assembly ribbon, so multiple independent WMP add-ins present their commands under one tab instead of each creating its own.

## Contract

- `WmpRibbonTab.TabDisplayName` / `WmpRibbonTab.TabInternalName` are the tab's fixed display and internal names.
- `WmpRibbonTab.EnsureTab(application, ribbonName, clientId)` returns the shared tab, creating it only if it does not already exist. Whichever add-in activates first creates the tab under its own `clientId`; every add-in that activates afterward finds the existing tab instead of creating a duplicate.
- `WmpRibbonTab.EnsurePanel(tab, displayName, internalName, clientId)` returns a panel on the shared tab by the caller's own internal name, creating it only if it does not already exist.
- `WmpRibbonTab.ContainsControl(panel, internalName)` reports whether the panel already carries a control with that internal name, by iterating `panel.CommandControls` and comparing `InternalName` (never by indexing `CommandControls.Item` on a name that may not exist - the interop XML documents no contract for that case). Callers use it to guard each `CommandControls.AddButton` call so re-ensuring a panel never adds a duplicate button.
- All three methods hold no static or cached COM state; every call re-resolves the tab, panel, or control list from the live Inventor object model.
- The whole file compiles only when `INVENTOR_INTEROP` is defined (the installed Inventor interop assembly is present), matching the pattern used by `SmartManufacturingExporter.InventorAdapter`.

## Consumers

- Smart Manufacturing Exporter (`projects/smart-manufacturing-exporter`) - adds its "Smart Export" panel to the shared tab.
- File Naming Manager (`projects/file-naming-manager`) - adds its own panel to the shared tab.
- WMP Tools Manager (`projects/wmp-tools-manager`) - adds its own "WMP Tools" panel to the shared tab.

## Rules

- Each add-in owns only its own panel on the shared tab; it never removes, renames, or reconfigures the tab itself.
- No add-in ever calls `RibbonTab.Delete` on the shared tab.
- No add-in assumes it is the tab's creator - the creator is whichever add-in's `Activate` runs first in a given Inventor session, or first after a ribbon rebuild.
- Every add-in calls `EnsureTab` and `EnsurePanel` on **every** `Activate` call, not only when `FirstTime` is true. Inventor passes `FirstTime` only on the first activation after install; when a sibling add-in is uninstalled and Inventor rebuilds its ribbon, or a user resets the ribbon, this add-in's own panel can be dropped from the tab without a matching `FirstTime` activation to recreate it. Re-ensuring on every activation is safe because `EnsureTab`/`EnsurePanel` are idempotent, and each add-in guards its own `CommandControls.AddButton` calls with `ContainsControl` so a re-ensured panel never gets a duplicate button. This rebuild-recovery behavior is live-unverified (no automated test exercises an Inventor ribbon reset).
