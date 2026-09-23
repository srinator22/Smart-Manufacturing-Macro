# Inventor 2027 API compatibility - File Naming Manager

## Scope and evidence standard

This matrix covers Autodesk Inventor 2027 only. Every member below was checked
on 2026-09-23 by exact-name lookup in the installed interop XML at
`C:\Program Files\Autodesk\Inventor 2027\Bin\Public Assemblies\Autodesk.Inventor.Interop.xml`
(interop assembly version `31.0.19201.6`). Presence in the interop demonstrates
that the member exists with that signature; it does not demonstrate live-host
behaviour. Live behaviour is claimed only where the "Live smoke" column says so,
and only for the temp-folder fixture described in `TEST_PLAN.md`.

## Scan

| Concern | Verified member or signature | Evidence |
| --- | --- | --- |
| Active document | `Application.ActiveDocument`, `Application.ActiveDocumentType` | Interop XML |
| Root identity | `Document.FullFileName`, `Document.FullDocumentName`, `Document.DocumentType` | Interop XML |
| Root state | `Document.Dirty`, `Document.HasReferencesMissing`, `Document.IsModifiable` | Interop XML |
| Unique referenced documents | `Document.AllReferencedDocuments` (recursive, unique), `DocumentsEnumerator.Count`, `DocumentsEnumerator.Item` | Interop XML |
| Reference status per child | `Document.ReferencedDocumentDescriptors`, `DocumentDescriptor.FullDocumentName`, `DocumentDescriptor.ReferenceMissing`, `DocumentDescriptor.ReferencedDocumentType`, `DocumentDescriptor.ReferencedDocument` | Interop XML |
| Parents of a document | `Document.ReferencingDocuments` | Interop XML |
| Occurrence walk (hierarchy depth, suppression) | `ComponentOccurrence.SubOccurrences`, `ComponentOccurrence.Suppressed`, `ComponentOccurrence.ReferencedDocumentDescriptor` | Interop XML |
| Occurrence collections | `AssemblyDocument.ComponentDefinition`, `AssemblyComponentDefinition.Occurrences`, `ComponentOccurrences.Count`, `ComponentOccurrences.Item`, `ComponentOccurrencesEnumerator.Count`, `ComponentOccurrencesEnumerator.Item` | Interop XML |
| Document kinds | `DrawingDocument`, `PresentationDocument` types | Interop XML |
| Part Number iProperty | `Document.PropertySets`, `PropertySets.Item`, `PropertySet.Item`, `Property.Name`, `Property.Value` | Interop XML |
| Project paths for scope exclusion | `Application.DesignProjectManager`, `DesignProjectManager.ActiveDesignProject`, `DesignProject.WorkspacePath`, `DesignProject.LibraryPaths`, `DesignProject.ContentCenterPath`, `ProjectPath.Path` | Interop XML |

## Rename and save

| Concern | Verified member or signature | Evidence |
| --- | --- | --- |
| Rename in session | `Document.SaveAs(string FileName, bool SaveCopyAs)`; the tool passes `SaveCopyAs = false` so the open document takes the new name and in-session parents follow | Interop XML |
| Persist parents | `Document.Save`, `Document.Save2(bool SaveDependents, object DocumentsToSave)` | Interop XML |
| Companion drawing handling | `Documents.Open(string, bool Visible)`, `Document.Close(bool SkipSave)` | Interop XML |
| Property write | `Property.Value` setter | Interop XML |

`Document.SaveAs` does not remove the previous file from disk. The original is
moved by the tool, never deleted; see `VAULT_RENAME_DESIGN.md`.

## Ribbon

| Concern | Verified member or signature | Evidence |
| --- | --- | --- |
| Shared tab lookup and creation | `UserInterfaceManager.Ribbons`, `Ribbon.RibbonTabs`, `RibbonTabs.Item(object)`, `RibbonTabs.Add(string, string, string, string, bool, bool)` | Interop XML, shared through `shared/WmpRibbon` |
| Panel and command | `RibbonPanels.Item(object)`, `RibbonPanels.Add(string, string, string, string, bool)`, `CommandControls.AddButton(ButtonDefinition, bool, bool, string, bool)`, `ControlDefinitions.AddButtonDefinition(...)` | Interop XML |
| Duplicate-button guard | `RibbonPanel.CommandControls`, `CommandControl.InternalName` (enumerated, never indexed by a name that may not exist) | Interop XML, shared through `shared/WmpRibbon` |
| Window ownership | `Application.MainFrameHWND` | Interop XML |

The shared tab and this add-in's own panel are re-ensured on every `Activate` call, not
only when `FirstTime` is true, because Inventor rebuilds the ribbon and can drop a
stranded panel (a sibling add-in uninstalled, or a user ribbon reset) without passing
`FirstTime` again. Each button is added only when `WmpRibbonTab.ContainsControl` reports
the panel does not already carry it, so a re-ensured panel never gets a duplicate. This
re-ensure-on-every-activation and rebuild-recovery behavior is live-unverified.

## Live smoke fixture (automation only, never the Vault workspace)

| Concern | Verified member or signature | Evidence |
| --- | --- | --- |
| Start and stop a hidden host | ProgID `Inventor.Application` (CLSID `{B6B5DC40-96E3-11d2-B774-0060B0F159EF}`, `Inventor.exe /Automation`), `Application.Visible`, `Application.SilentOperation`, `Application.Quit` | Registry and interop XML |
| Create fixture documents | `Documents.Add(DocumentTypeEnum, string TemplateFileName, bool CreateVisible)`, `FileManager.GetTemplateFile(DocumentTypeEnum, SystemOfMeasureEnum, DraftingStandardEnum, object)` | Interop XML |
| Place occurrences | `ComponentOccurrences.Add(string FileName, Matrix)`, `TransientGeometry.CreateMatrix`, `Application.TransientGeometry` | Interop XML |

## Known imprecision

`DocumentDescriptor.FullDocumentName` can carry a model-state or member suffix
in angle brackets, so the suppressed-only map keyed on it may miss a match
against `Document.FullFileName`. `IsSuppressedOnly` is informational in v1 and
gates no rename decision; the workflow reads it only for display.

## Not used, and why

| Member | Reason |
| --- | --- |
| `FileManager.MoveFile`, `FileManager.DeleteFile` | A filesystem move or delete behind Inventor's back breaks references; the tool renames through `SaveAs` and moves originals only after every parent is saved. |
| `ReferencedFileDescriptor.PutLogicalFileNameUsingFull` | Not needed while the renamed child is open in the same session; `SaveAs` updates in-session parents. Kept as the fallback for the Vault-managed task. |
| Any Vault SDK member | v1 does not call Vault. The design for the managed case is in `VAULT_RENAME_DESIGN.md`. |

## Deferred live-host verification

- Behaviour of `SaveAs(..., false)` on a document referenced by a parent that is not open in the session (the tool opens parents through the active assembly, so this should not occur, but it is unverified).
- Interaction with the Inventor Vault add-in when a checked-out parent is saved after a child rename; expected to be the ordinary new-file check-in flow.
- Ribbon tab persistence across Inventor restarts when three add-ins share one tab.
- Panel and button recovery across an Inventor ribbon rebuild (sibling add-in uninstall or ribbon reset).
- Rename of a model whose companion drawing is checked in to Vault.
