# Inventor 2027 API compatibility - Phase 1

## Scope and evidence standard

This matrix covers Autodesk Inventor 2027 only. It makes no compatibility
claim for any other Inventor version. Assembly and add-in manifest entries
below are locally verified installed-artifact facts. They do not demonstrate
live-host behavior.

| Area | Locally verified fact | Authoritative local source |
| --- | --- | --- |
| Interop assembly | `Autodesk.Inventor.Interop.dll` has assembly version `31.0.19201.6`. | `C:\Program Files\Autodesk\Inventor 2027\Bin\Public Assemblies\Autodesk.Inventor.Interop.dll` |
| XML API reference | The member signatures in this matrix are present in the installed interop XML. | `C:\Program Files\Autodesk\Inventor 2027\Bin\Public Assemblies\Autodesk.Inventor.Interop.xml` |
| STEP translator | The installed STEP translator has ClassId and ClientId `{90AF7F40-0C01-11D5-8E83-0010B541CD80}`. It supports `SaveCopyAs` from `.ipt` and `.iam`, and advertises `.stp`, `.ste`, `.step`, and `.stpz`. | `C:\Program Files\Autodesk\Inventor 2027\Bin\Addins\autodesk.translatorstep.inventor.addin` |
| Developer Tools | Autodesk Inventor 2027 Developer Tools version `19.0.0` is installed. The installed C# add-in template includes a generated server that implements `Inventor.ApplicationAddInServer`. | `C:\Users\srira\Documents\Visual Studio 2022\Templates\ProjectTemplates\VCSInventorAddInTemplate2027.zip` and Windows uninstall registry entry `Autodesk Inventor 2027 Developer Tools` |

## Phase 1 interop matrix

| Concern | Verified member or signature | Evidence type |
| --- | --- | --- |
| Add-in server | `ApplicationAddInServer.Activate(ApplicationAddInSite, bool)` | Installed interop XML |
| Add-in server | `ApplicationAddInServer.Deactivate()` | Installed interop XML |
| Add-in server | `ApplicationAddInServer.ExecuteCommand(int)` | Installed interop XML |
| Add-in server | `ApplicationAddInServer.Automation` | Installed interop XML |
| Active document | `Application.ActiveDocument`, `Application.ActiveDocumentType` | Installed interop XML |
| Assembly root | `AssemblyDocument.ComponentDefinition` | Installed interop XML |
| Occurrences | `AssemblyComponentDefinition.Occurrences` | Installed interop XML |
| Occurrences | `ComponentOccurrences.Count`, `ComponentOccurrences.Item(int)` | Installed interop XML and interop assembly metadata |
| Occurrence state | `ComponentOccurrence.Suppressed`, `ComponentOccurrence.DefinitionDocumentType`, `ComponentOccurrence.Definition` | Installed interop XML |
| Part document | `PartComponentDefinition.Document` | Installed interop XML |
| Part document | `PartDocument.FullFileName`, `PartDocument.Dirty`, `PartDocument.Close(bool)` | Installed interop XML |
| Command definition | `ControlDefinitions.AddButtonDefinition(string, string, CommandTypesEnum, object, string, string, object, object, ButtonDisplayEnum)` | Installed interop XML |
| Ribbon | `RibbonTabs.Add(string, string, string, string, bool, bool)` | Installed interop XML |
| Ribbon | `RibbonPanels.Add(string, string, string, string, bool)` | Installed interop XML |
| Command placement | `CommandControls.AddButton(ButtonDefinition, bool, bool, string, bool)` | Installed interop XML |
| Translator | `TranslatorAddIn.HasSaveCopyAsOptions` | Installed interop XML |
| Translator | `TranslatorAddIn.SaveCopyAs(object, TranslationContext, NameValueMap, DataMedium)` | Installed interop XML |
| Document open | `Documents.Open(string, bool)` | Installed interop XML |
| Add-in lookup | `ApplicationAddIns.ItemById(string)` | Installed interop XML and interop assembly metadata |
| Translation context | `TransientObjects.CreateTranslationContext()` | Installed interop XML and interop assembly metadata |
| Translation options | `TransientObjects.CreateNameValueMap()` | Installed interop XML and interop assembly metadata |
| Translation target | `TransientObjects.CreateDataMedium()` | Installed interop XML and interop assembly metadata |

## Phase 1 boundary

- All Inventor COM calls stay on Inventor's owning STA thread.
- The implementation does not use indiscriminate `Marshal.ReleaseComObject` calls.
- Export planning does not call source `Save`, `SaveAs`, or `Update`.
- The exporter uses the installed STEP translator identity above and its `SaveCopyAs` contract for selected part documents only.

## Deferred live-host verification

The following items are deliberately unverified by installed assemblies and
manifests. They remain deferred until exercised in a live Inventor 2027 host.

- Exact STEP option keys and AP242 option mapping.
- Interactive ribbon internal IDs and persistence behavior.
- Source close and open behavior in a live host.
- The final five-part acceptance scenario, including selection of three unique parts and production of exactly three STEP files.
