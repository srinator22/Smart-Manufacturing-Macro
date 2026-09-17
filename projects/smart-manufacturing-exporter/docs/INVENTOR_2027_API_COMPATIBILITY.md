# Inventor 2027 API compatibility - Phases 1 and 2

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

## Interop matrix

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
| Recursive occurrences | `ComponentOccurrence.SubOccurrences` returns `ComponentOccurrencesEnumerator`; its `Count` and `Item(int)` expose children and it is empty for parts. | Installed interop XML and interop assembly metadata |
| Part document | `PartComponentDefinition.Document` | Installed interop XML |
| Part document | `PartDocument.FullFileName`, `PartDocument.Dirty`, `PartDocument.Close(bool)` | Installed interop XML |
| Generic document | `ComponentDefinition.Document`, `Document.FullFileName`, `Document.DocumentType`, `Document.Close(bool)` | Installed interop XML |
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
| STEP spline-fit tolerance | `export_fit_tolerance` is a `Double` in centimeters with a documented range of `0.00001` to `0.001`; the documented default is `0.001`. | [Autodesk Inventor Translator Settings](https://help.autodesk.com/cloudhelp/2025/ENU/Inventor-API/files/TranslatorSettings.htm) |
| Translation target | `TransientObjects.CreateDataMedium()` | Installed interop XML and interop assembly metadata |

## Safety boundary

- All Inventor COM calls stay on Inventor's owning STA thread.
- The implementation does not use indiscriminate `Marshal.ReleaseComObject` calls.
- Export planning does not call source `Save`, `SaveAs`, or `Update`.
- Suppressed occurrences and their descendants are not traversed.
- The exporter uses the installed STEP translator identity above and its `SaveCopyAs` contract for selected `.ipt` and `.iam` documents.
- A source document is opened invisibly only when it is not already open and is closed with `Close(true)` only when the exporter opened it.
- Low, Medium, and Highest map to `export_fit_tolerance` values `0.001`, `0.0001`, and `0.00001` centimeters respectively. Lower values increase spline approximation accuracy and can increase file size.

## Deferred live-host verification

The following items are deliberately unverified by installed assemblies and
manifests. They remain deferred until exercised in a live Inventor 2027 host.

- Live acceptance of the Low, Medium, and Highest `export_fit_tolerance` values in Inventor 2027.
- AP242 option mapping.
- Interactive ribbon internal IDs and persistence behavior.
- Source close and open behavior in a live host.
- Recursive traversal through three live assembly levels, including unresolved and suppressed occurrences.
- Live `.iam` STEP translation and preservation of an already-open active assembly.
- The final five-part acceptance scenario, including selection of three unique parts and production of exactly three STEP files.
