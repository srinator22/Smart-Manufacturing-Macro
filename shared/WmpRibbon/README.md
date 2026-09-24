# WmpRibbon

Provides the single shared "WMP Custom Tools" Inventor ribbon tab (internal name `WMP.CustomTools`) on the Assembly ribbon, so multiple independent WMP add-ins present their commands under one tab instead of each creating its own.

## Contract

- `WmpRibbonTab.TabDisplayName` / `WmpRibbonTab.TabInternalName` are the tab's fixed display and internal names.
- `WmpRibbonTab.EnsureTab(application, ribbonName, clientId)` returns the shared tab, creating it only if it does not already exist. Whichever add-in activates first creates the tab under its own `clientId`; every add-in that activates afterward finds the existing tab instead of creating a duplicate.
- `WmpRibbonTab.EnsurePanel(tab, displayName, internalName, clientId)` returns a panel on the shared tab by the caller's own internal name, creating it only if it does not already exist.
- `WmpRibbonTab.ContainsControl(panel, internalName)` reports whether the panel already carries a control with that internal name, by iterating `panel.CommandControls` and comparing `InternalName` (never by indexing `CommandControls.Item` on a name that may not exist - the interop XML documents no contract for that case). Callers use it to guard each `CommandControls.AddButton` call so re-ensuring a panel never adds a duplicate button.
- All three methods hold no static or cached COM state; every call re-resolves the tab, panel, or control list from the live Inventor object model.
- `WmpRibbonTab.cs` compiles only when `INVENTOR_INTEROP` is defined (the installed Inventor interop assembly is present), matching the pattern used by `SmartManufacturingExporter.InventorAdapter`.

## Ribbon icons

- `RibbonIcons.Load(application, resourceAssembly, resourcePrefix, iconName)` returns the standard and large pictures for one command. It reads `ThemeManager.ActiveTheme.Name` and the DPI of Inventor's main window once, picks the matching embedded PNG `<resourcePrefix>.Ribbon.<iconName>-<dark|light>-<size>.png`, checks its pixel size, and converts it through `RibbonPicture`. A failed theme query falls back to dark and a failed window-DPI query to the system DPI; a missing or wrongly sized resource throws, naming the resource.
- `RibbonPicture.FromBitmap(bitmap)` creates a `PICTYPE_ICON` OLE picture that owns its HICON, so transparency and anti-aliasing survive in both themes. It is the only supported ribbon image converter.
- `RibbonIconSelector` is the pure decision: `ThemeFromName` (a name containing "light" is light, anything else dark), `SmallSize`/`LargeSize` (the smallest rendered size at or above 16 or 32 px times the DPI scale, capped at 32 or 64), and `FileName`/`ResourceName`. It compiles without the interop so `WmpRibbon.UnitTests` runs on CI, and `shared/WmpIconRenderer` uses the same sizes and names.
- `RibbonPicture.cs` and `RibbonIcons.cs` compile only when `INVENTOR_INTEROP` is defined.
- Authoring rules for masters, palette, and wiring: [docs/rules/ribbon-icons.md](../../docs/rules/ribbon-icons.md).

## Consumers

- Smart Manufacturing Exporter (`projects/smart-manufacturing-exporter`) - adds its "Smart Export" panel to the shared tab.
- File Naming Manager (`projects/file-naming-manager`) - adds its own panel to the shared tab.
- WMP Tools Manager (`projects/wmp-tools-manager`) - adds its own "WMP Tools" panel to the shared tab.

## Rules

- Each add-in owns only its own panel on the shared tab; it never removes, renames, or reconfigures the tab itself.
- No add-in ever calls `RibbonTab.Delete` on the shared tab.
- No add-in assumes it is the tab's creator - the creator is whichever add-in's `Activate` runs first in a given Inventor session, or first after a ribbon rebuild.
- Every add-in calls `EnsureTab` and `EnsurePanel` on **every** `Activate` call, not only when `FirstTime` is true. Inventor passes `FirstTime` only on the first activation after install; when a sibling add-in is uninstalled and Inventor rebuilds its ribbon, or a user resets the ribbon, this add-in's own panel can be dropped from the tab without a matching `FirstTime` activation to recreate it. Re-ensuring on every activation is safe because `EnsureTab`/`EnsurePanel` are idempotent, and each add-in guards its own `CommandControls.AddButton` calls with `ContainsControl` so a re-ensured panel never gets a duplicate button. This rebuild-recovery behavior is live-unverified (no automated test exercises an Inventor ribbon reset).
