# SMART MANUFACTURING EXPORTER FOR AUTODESK INVENTOR

## Complete Development Specification

Build a professional Autodesk Inventor add-in in **C#/.NET** called:

# Smart Manufacturing Exporter

The primary purpose of the add-in is to intelligently inspect an Autodesk Inventor assembly, understand its assembly structure and organisational structure, allow the user to select exactly which components should be exported, and batch-generate manufacturing files such as:

* STEP
* Sheet-metal DXF
* Drawing PDF
* Drawing DWG/DXF where appropriate

The application should be designed as a polished engineering productivity tool suitable for regular use on small and very large Autodesk Inventor assemblies.

This must NOT be implemented as a simple macro.

The architecture should support future expansion.

---

# 1. PRIMARY USER WORKFLOW

The normal workflow should be:

1. User opens an Autodesk Inventor assembly (`.iam`).
2. User clicks a ribbon command called:

**Smart Export**

3. The add-in scans the active assembly.
4. It understands:

   * Top-level parts
   * Subassemblies
   * Nested subassemblies
   * Parts inside subassemblies
   * Inventor Assembly Browser folders
   * Suppressed components
   * Content Center parts
   * Library parts
   * Purchased/OTS components
   * Fasteners
   * Duplicate occurrences
   * Sheet-metal parts
   * Associated drawings
5. A hierarchical export-selection window appears.
6. Components are automatically selected/excluded according to the active preset.
7. User reviews the selections.
8. User chooses an export destination.
9. User optionally changes STEP/DXF/PDF settings.
10. User runs Preview Export.
11. Validation occurs.
12. User clicks Export.
13. Files are generated.
14. A detailed result report is shown.

The user must always retain final control over what is exported.

---

# 2. IMPORTANT DESIGN PHILOSOPHY

This add-in should understand an Inventor assembly as an engineering structure rather than simply obtaining a flat collection of referenced files.

For example:

Machine.iam
│
├── FabricatedFrame.iam
│   ├── LeftFrame.ipt
│   ├── RightFrame.ipt
│   ├── CrossMember.ipt
│   └── Bracket.ipt
│
├── ConveyorAssembly.iam
│   ├── RollerAssembly.iam
│   │   ├── Roller.ipt
│   │   ├── Shaft.ipt
│   │   └── Bearing.ipt
│   ├── ConveyorFrame.ipt
│   └── MotorMount.ipt
│
├── OTS
│   ├── Motor.iam
│   ├── Gearbox.iam
│   └── Sensor.ipt
│
└── Fasteners
├── M8_Bolt.ipt
├── M8_Washer.ipt
└── M8_Nut.ipt

The add-in should understand the difference between:

* Real assemblies
* Real parts
* Inventor Browser folders
* Occurrences
* Unique source documents
* Purchased assemblies
* Fabricated assemblies
* Fasteners
* Content Center components
* Sheet-metal parts

---

# 3. INVENTOR RIBBON INTEGRATION

Create an Inventor Add-In with a ribbon panel called:

**Smart Export**

Commands:

### Smart Export

Opens the full export interface.

### Quick Export

Uses the user's saved/default preset.

It should still perform validation before generating files.

### Export Selected

If components are selected in the Inventor Assembly Browser, open Smart Export with those components pre-selected.

### Settings

Opens global settings, rules and classifications.

---

# 4. ACTIVE DOCUMENT VALIDATION

When Smart Export is clicked:

Check whether the active document is an Inventor AssemblyDocument.

If no assembly is active, display:

"Smart Export requires an active Inventor assembly."

Do not crash.

Also detect:

* Unsaved assemblies
* Missing references
* Unresolved references
* Read-only files
* Dirty/modified documents
* Documents requiring update

---

# 5. ASSEMBLY SCANNING

Create an AssemblyScanner service.

It should discover:

* Root assembly
* Top-level components
* Subassemblies
* Nested subassemblies
* Parts
* Browser folders
* Component occurrences
* Unique documents
* Suppressed occurrences
* Content Center parts
* Library components
* Virtual components
* Missing/unresolved components
* Sheet-metal components
* Associated drawings

The scanner must support assemblies containing hundreds or thousands of occurrences.

Avoid repeatedly traversing identical subassemblies.

Cache metadata during the current Smart Export session.

---

# 6. UNIQUE DOCUMENTS VS OCCURRENCES

The system must understand the distinction between:

**Component Occurrence**

and

**Source Document**

Example:

M8_Bolt.ipt may occur 72 times.

By default, display/export it once:

M8_Bolt.ipt | Quantity: 72

Provide:

**Show Individual Occurrences**

When enabled, individual occurrences can be displayed.

Default export behavior:

**Export each unique source document once.**

---

# 7. HIERARCHICAL ASSEMBLY TREE

The main interface must include a hierarchical assembly tree.

Example:

☑ Machine.iam

```
☑ FabricatedFrame.iam
    ☑ LeftFrame.ipt
    ☑ RightFrame.ipt
    ☑ CrossMember.ipt

☑ Conveyor.iam
    ☑ ConveyorFrame.ipt
    ☑ MotorMount.ipt

☐ OTS
    ☐ Motor.iam
    ☐ Gearbox.iam

☐ Fasteners
    ☐ M8_Bolt.ipt
    ☐ M8_Nut.ipt
```

Every node should support checkboxes.

---

# 8. TRI-STATE CHECKBOXES

Use tri-state selection.

Checked:

Everything below the node is selected.

Unchecked:

Nothing below is selected.

Indeterminate:

Some children are selected.

This must work for:

* Root assembly
* Subassemblies
* Browser folders
* Logical groups

Checking/unchecking a parent should propagate appropriately to its children.

---

# 9. EXPAND/COLLAPSE

Provide:

* Expand All
* Collapse All
* Expand Selected
* Collapse Selected

Remember expanded states where practical during the current session.

---

# 10. INVENTOR BROWSER FOLDER SUPPORT

This is a critical feature.

Users may organise assembly components into Inventor Assembly Browser folders such as:

* OTS
* Fasteners
* Purchased
* Hardware
* Pneumatics
* Electrical
* Standard Parts
* Reference
* Do Not Export
* Fabricated

These are Inventor Browser organisational folders, NOT Windows filesystem folders.

The add-in must inspect the Inventor Assembly Browser and associate component occurrences with their Browser folders where the API permits.

Browser folders must appear distinctly in the hierarchy.

Use different icons for:

* Assembly
* Part
* Browser folder
* Content Center
* Purchased/OTS
* Fastener
* Sheet metal
* Suppressed
* Missing/unresolved

---

# 11. BROWSER FOLDER SELECTION

Browser folders must support selection.

Example:

☐ Fasteners
☐ Bolt.ipt
☐ Nut.ipt
☐ Washer.ipt

Unchecking Fasteners deselects everything contained within it.

Right-click options:

* Select All in Folder
* Deselect All in Folder
* Invert Selection
* Exclude Folder This Export
* Always Exclude Folder
* Include Folder This Export
* Add Folder Name to Exclusion Rules
* Remove Folder Name from Exclusion Rules

---

# 12. AUTOMATIC FOLDER EXCLUSIONS

Allow configurable automatic folder exclusions.

Example defaults:

Fasteners
OTS
Purchased
Hardware
Standard Parts
Do Not Export

These MUST NOT be permanently hard-coded.

Users can configure them.

Matching methods:

* Exact
* Contains
* Starts With
* Ends With
* Wildcard

Examples:

`Fasteners`

`OTS`

`*Hardware*`

`Purchased*`

Provide optional case-insensitive matching.

---

# 13. SUBASSEMBLY INCLUSION

A major control near the top of Smart Export should be:

# Include Components Inside Subassemblies

Available modes:

### Top-Level Only

Only components directly inside the active assembly.

### Parts Recursive

Recursively discover parts inside all allowed subassemblies.

Do not separately export `.iam` files.

### Assemblies Only

Export selected `.iam` assemblies but not their children individually.

### Assemblies + Parts

Allow both assemblies and their child parts to be exported.

### Custom

User independently controls traversal/export behavior for each subassembly.

---

# 14. SUBASSEMBLY PROMPT

Provide a setting:

**When subassemblies are detected:**

* Always recursively include parts
* Never recursively include parts
* Ask me
* Use preset

If Ask Me is enabled:

"This assembly contains 8 subassemblies.

How should nested components be handled?"

Options:

* Top-level only
* Include parts recursively
* Parts only recursively
* Assemblies only
* Assemblies + parts

Checkbox:

**Remember my choice**

---

# 15. INDIVIDUAL SUBASSEMBLY OVERRIDES

Each subassembly should have independent controls.

Right-click:

* Export Assembly as STEP
* Include Child Parts
* Export Assembly + Child Parts
* Do Not Export Assembly
* Do Not Traverse Children
* Treat as Purchased / OTS
* Treat as Fabricated
* Always Exclude
* Reset to Global Rule

Example:

Machine.iam

FabricatedFrame.iam
→ export child parts

Robot.iam
→ purchased
→ do not traverse

Gearbox.iam
→ purchased
→ export gearbox as one STEP
→ do not export children

---

# 16. PURCHASED / OTS ASSEMBLIES

Implement a major classification:

# Purchased / OTS

When an assembly is classified as Purchased/OTS:

Default behavior:

* Do not recursively traverse internal components.
* Do not export child components.
* Optionally export the purchased assembly itself as one STEP.
* Visually identify it as Purchased/OTS.

This prevents something like a supplier robot assembly containing 1,000 parts from unnecessarily expanding into the export tree.

---

# 17. COMPONENT CLASSIFICATIONS

Components should optionally be classified as:

* Fabricated
* Purchased / OTS
* Fastener
* Standard Part
* Reference
* Do Not Export
* Unclassified

Classification can originate from:

* Manual user classification
* Browser folder
* Content Center status
* Filename
* Part Number
* Description
* iProperty
* File location
* Rule
* Saved classification memory

---

# 18. CLASSIFICATION MEMORY

Implement optional persistent classification memory.

Example:

User classifies:

`Festo_DNC_40_100.iam`

as:

Purchased / OTS

Next time that component appears in another assembly, Smart Export automatically recognises it.

Classification identity should safely consider:

* Full file path
* Part Number
* File identity
* Configurable matching strategy

Avoid accidentally classifying unrelated components that happen to share a generic filename.

Allow classifications to be:

* Viewed
* Edited
* Removed
* Exported
* Imported

Store locally in a safe JSON configuration.

---

# 19. SMART RULE ENGINE

Create a reusable RuleEngine.

Rules should support:

### Filename

* Contains
* Does not contain
* Starts with
* Ends with
* Equals
* Wildcard

### Part Number

Same operators.

### Description

Same operators.

### Browser Folder

Same operators.

### Material

### File Path

### Document Type

### Quantity

### Revision

### Content Center status

### Sheet-metal status

### Suppressed status

### Classification

Rules can perform actions such as:

* Select
* Deselect
* Classify as Purchased
* Classify as Fabricated
* Classify as Fastener
* Do Not Traverse
* Export Assembly Only
* Export Child Parts

---

# 20. RULE EXAMPLES

Example:

IF Browser Folder = Fasteners
THEN Exclude

IF Browser Folder = OTS
THEN Classify Purchased

IF Content Center = True
THEN Exclude

IF Part Number starts with `FAB-`
THEN Classify Fabricated AND Select

IF Filename contains `bolt`
THEN Classify Fastener AND Exclude

IF Description contains `purchased`
THEN Classify Purchased

---

# 21. RULE PRIORITY

Implement predictable rule priority.

Suggested priority:

1. Invalid/unresolved
2. Explicit current-session manual exclusion
3. Explicit current-session manual inclusion
4. Explicit component classification
5. Parent Do Not Traverse
6. Browser folder rule
7. Purchased/OTS rule
8. Content Center rule
9. Filename/iProperty rule
10. Preset
11. Global default

Clearly document the actual implemented priority.

---

# 22. SELECTION REASON

Every component should have a Selection Reason.

Examples:

* Manually Selected
* Manually Excluded
* Selected by Preset
* Excluded by OTS Folder
* Excluded by Fasteners Folder
* Excluded as Content Center
* Excluded as Purchased
* Parent Assembly Excluded
* Filename Rule
* Part Number Rule
* Suppressed
* Missing
* Unresolved

Display this in the UI.

Tooltips should explain automatic decisions.

---

# 23. DETAILED COMPONENT TABLE

In addition to the tree, provide a detailed table.

Columns:

* Export
* Icon
* File Name
* Part Number
* Description
* Revision
* Material
* Quantity
* Type
* Classification
* Parent Assembly
* Browser Folder
* Source Path
* Suppressed
* Content Center
* Sheet Metal
* Drawing Available
* STEP Status
* Selection Reason
* Last Modified
* Output Filename

Allow:

* Sorting
* Resizing
* Reordering
* Search
* Filtering

---

# 24. TREE/TABLE SYNCHRONISATION

The hierarchical tree and detailed table must stay synchronised.

Changing selection in the tree updates the table.

Changing selection in the table updates the tree.

Provide either:

Tree | Table

tabs

or preferably:

Tree on left
Detailed component grid in centre/right.

---

# 25. SEARCH

Provide instant search.

Search fields:

* Filename
* Part Number
* Description
* Material
* Assembly
* Browser Folder
* Classification

Search should not destroy current selections.

---

# 26. FILTERS

Provide filters:

* Parts only
* Assemblies only
* Parts + Assemblies
* Selected only
* Unselected only
* Fabricated
* Purchased
* Fasteners
* Content Center
* Sheet Metal
* Suppressed
* Missing
* Unique files
* Individual occurrences
* Missing STEP
* Outdated STEP
* Up-to-date STEP
* Has drawing
* No drawing

---

# 27. SELECTION COMMANDS

Provide:

* Select All
* Select None
* Invert
* Select Visible
* Select Fabricated
* Select Parts
* Select Assemblies
* Select Sheet Metal
* Select Missing STEP
* Select Outdated STEP
* Deselect Purchased
* Deselect Fasteners
* Deselect Content Center
* Deselect Suppressed

---

# 28. COMPONENT DETAILS PANEL

Selecting a component should display:

* Thumbnail if practical
* File Name
* Full Path
* Part Number
* Description
* Revision
* Material
* Stock Number
* Project
* Designer
* Classification
* Parent Assembly
* Browser Folder
* Quantity
* Document Type
* Sheet Metal status
* Drawing status
* STEP status
* Source Modified Date
* Output path
* Generated output filename
* Selection Reason

---

# 29. ASSEMBLY SUMMARY

Top of the window should display:

Active Assembly

Assembly Part Number

Revision

and counts:

* Total occurrences
* Unique parts
* Unique assemblies
* Subassemblies
* Fabricated
* Purchased
* Fasteners
* Content Center
* Sheet Metal
* Suppressed
* Missing
* Selected

Counts update dynamically.

---

# 30. OUTPUT DESTINATION

Allow user to browse for output folder.

Remember:

* Last folder
* Per-project folder if configured

Provide destination modes.

### Flat

Everything into one directory.

### Preserve Source Folder Structure

Recreate relevant source directory hierarchy.

### Assembly Hierarchy

Create folders based on assembly structure.

Example:

Export
/FabricatedFrame
/Conveyor
/RollerAssembly

### iProperty Structure

Create folders using properties such as:

* Project
* Category
* Stock Number

---

# 31. STEP EXPORT

Use Autodesk Inventor's supported STEP TranslatorAddIn / SaveCopyAs workflow.

Do not invent APIs or translator GUIDs.

Confirm the appropriate API details for the targeted Inventor version.

Support STEP standards available in Inventor such as:

* AP203
* AP214
* AP242 where supported

Prefer AP242 by default where supported and appropriate.

---

# 32. STEP NAMING ENGINE

Create a NamingEngine.

Options:

* Original filename
* Part Number
* Part Number + Revision
* Filename + Revision
* Part Number + Description
* Custom template

Tokens:

{FileName}
{PartNumber}
{Description}
{Revision}
{Material}
{Quantity}
{Project}
{StockNumber}
{Assembly}
{Classification}

Example:

`{PartNumber}_REV-{Revision}`

Output:

`FAB-1042_REV-C.step`

---

# 33. NAMING SAFETY

Automatically:

* Remove illegal Windows filename characters
* Trim whitespace
* Handle blank properties
* Detect duplicate filenames
* Detect path length issues where relevant

Fallback:

If Part Number is blank and required for naming, use source filename and generate a warning.

Never silently overwrite two components because they generated identical filenames.

---

# 34. EXISTING STEP DETECTION

Before export, inspect destination.

Status:

* Missing
* Up to Date
* Outdated
* Conflict
* Existing
* Ready to Overwrite

---

# 35. SMART OUTDATED STEP DETECTION

At minimum compare:

Inventor source file modification timestamp

against

STEP modification timestamp.

If source is newer:

STEP = Outdated.

Design the architecture so stronger future change detection can be added, such as:

* Stored source hash
* Export metadata
* Revision comparison

---

# 36. EXISTING FILE BEHAVIOR

Options:

* Overwrite
* Skip
* Ask
* Auto Rename
* Export only missing
* Export missing + outdated
* Overwrite only outdated
* Never overwrite automatically

---

# 37. SHEET METAL

Detect sheet-metal parts.

Optional:

**Export Flat Pattern DXF**

Sheet-metal component can generate:

* STEP
* DXF
* Both

Where supported, allow flat pattern generation.

Avoid permanently modifying the source file if possible.

Allow DXF naming templates.

---

# 38. DRAWING DETECTION

Detect associated:

* `.idw`
* `.dwg`

where practical and reliable.

Allow optional:

* PDF export
* DWG export
* DXF export

STEP/DXF/PDF selections should be independently configurable.

---

# 39. MANUFACTURING PACKAGE MODE

Provide optional:

# Manufacturing Package

For each fabricated component generate relevant outputs.

Example:

Machined part:

* STEP
* PDF drawing

Sheet metal:

* STEP
* Flat-pattern DXF
* PDF drawing

Assembly:

* STEP assembly
* PDF assembly drawing

Purchased:

* Normally excluded

Fastener:

* Excluded

---

# 40. EXPORT PROFILES / PRESETS

Allow named presets.

Example:

## Supplier STEP Pack

* Fabricated only
* Parts recursively
* Exclude OTS
* Exclude Fasteners
* Exclude Content Center
* STEP AP242
* Part Number + Revision naming
* Missing + Outdated only

## Sheet Metal Pack

* Fabricated sheet metal only
* STEP
* Flat DXF
* Drawing PDF

## Full Manufacturing Package

* Fabricated parts
* Fabricated assemblies
* STEP
* DXF where applicable
* PDF drawings

## Customer Model

* Main assemblies
* No individual manufacturing parts
* STEP only

---

# 41. PRESET CONTENT

Preset should save:

* Selection defaults
* Folder exclusions
* Classification rules
* Filename rules
* Traversal behavior
* STEP settings
* DXF settings
* PDF settings
* Naming template
* Destination mode
* Existing file behavior
* Drawing behavior

---

# 42. PREVIEW EXPORT / DRY RUN

Before export provide:

# Preview Export

This performs the complete export decision process without creating files.

Display:

Source
Output
Type
Status
Action
Reason

Example:

Bracket.ipt
→ FAB-1024_REV-B.step
→ STEP
→ Missing
→ EXPORT

Bolt_M8.ipt
→ No output
→ Excluded
→ Fasteners Browser Folder

Motor.iam
→ No output
→ Excluded
→ Purchased / OTS

SidePanel.ipt
→ PANEL-200_REV-C.dxf
→ DXF
→ Outdated
→ RE-EXPORT

---

# 43. VALIDATION ENGINE

Before export validate:

* Destination exists
* Destination is writable
* Source exists
* References resolved
* STEP translator available
* Drawing translator available where needed
* DXF capability available
* Naming collisions
* Blank required iProperties
* Invalid filenames
* Unsaved documents
* Read-only conditions
* Unsupported document type
* Missing drawing
* Missing flat pattern
* Excessive path length
* Existing output conflicts

Classify:

ERROR
WARNING
INFO

Blocking errors prevent export.

Warnings may continue after confirmation.

---

# 44. EXPORT PROGRESS

During export display:

Current file

Current operation

Example:

`FAB-1042.ipt`
`Exporting STEP...`

Show:

* Progress bar
* Current item / total
* Successful
* Skipped
* Warnings
* Failed

Provide:

Cancel

Cancellation should stop safely after the current operation.

---

# 45. ERROR ISOLATION

One failed export must NOT terminate the entire batch.

Example:

File 17 fails.

Record failure.

Continue with file 18.

At completion report failures.

---

# 46. EXPORT RESULTS

Display:

EXPORT COMPLETE

STEP:
38 successful
4 skipped
1 failed

DXF:
12 successful

PDF:
21 successful
2 unavailable

Buttons:

* Open Export Folder
* View Report
* Copy Failed Items
* Retry Failed
* Export Again
* Close

---

# 47. LOGGING

Generate export log.

Fields:

* Timestamp
* Inventor version
* Root assembly
* User
* Preset
* Source document
* Part Number
* Revision
* Classification
* Browser Folder
* Parent Assembly
* Output type
* Output path
* STEP standard
* Action
* Result
* Warning
* Error

Support:

* CSV
* JSON
* TXT

---

# 48. SETTINGS PERSISTENCE

Remember:

* Window size
* Window location
* Column sizes
* Column ordering
* Last destination
* Last preset
* Last traversal mode
* Last STEP standard
* Filters
* Search settings
* Naming scheme

Use safe local configuration storage, preferably JSON.

---

# 49. CLASSIFICATION DATABASE

Maintain a local classification store.

Example conceptual structure:

Component Identity
Classification
Preferred Traversal
Preferred Export Behavior
Date Classified
Source

Allow:

* Search
* Edit
* Delete classification
* Import classifications
* Export classifications
* Clear all with confirmation

---

# 50. RIGHT-CLICK COMPONENT ACTIONS

Right-click component:

* Select
* Deselect
* Select Children
* Deselect Children
* Export STEP
* Include DXF
* Include PDF
* Classify Fabricated
* Classify Purchased / OTS
* Classify Fastener
* Classify Reference
* Do Not Export
* Do Not Traverse
* Reset Classification
* Open Source File
* Open File Location
* Show Properties

---

# 51. CONTEXT MENU IN INVENTOR

Where supported, integrate with Inventor's Assembly Browser.

If user selects components directly in Inventor:

Right-click →

**Smart Export Selected**

Launch Smart Export with those components pre-selected.

---

# 52. QUICK EXPORT

Quick Export uses a configured default preset.

Example:

User opens assembly.

Clicks:

Quick Manufacturing Export

Plugin:

* Scans
* Applies rules
* Validates
* Shows concise summary
* Exports

If blocking ambiguity exists, fall back to the full Smart Export window.

---

# 53. PERFORMANCE

This tool must work efficiently with large assemblies.

Requirements:

* Deduplicate source documents early
* Cache iProperties
* Avoid opening documents unnecessarily
* Avoid scanning Purchased/OTS children when Do Not Traverse is active
* Avoid repeatedly traversing identical subassemblies
* Use efficient Inventor API collections
* Keep UI responsive where technically safe

Do not perform Inventor API calls from unsafe background threads.

Separate API access from CPU-only processing so safe operations may be asynchronous where appropriate.

---

# 54. SAFETY

Never:

* Delete source Inventor files
* Modify iProperties automatically
* Rename source files
* Move source files
* Overwrite source documents
* Save source documents without explicit need
* Delete existing output without appropriate overwrite settings

Export should use copy/translator mechanisms.

---

# 55. DIRTY DOCUMENT HANDLING

If a source document contains unsaved modifications:

Clearly indicate:

**Modified / Unsaved Changes**

Allow configurable behavior:

* Export current in-memory state where supported
* Warn
* Skip
* Require save

Never silently save the user's design.

---

# 56. UI DESIGN

Prefer WPF if compatible with the chosen Inventor/.NET architecture.

Suggested layout:

## HEADER

Assembly name
Part Number
Revision
Preset

Summary statistics

## LEFT

Assembly tree

Search

Filters

## CENTRE

Detailed component grid

## RIGHT

Selected component details / preview

## BOTTOM

Output folder

Export mode

STEP/DXF/PDF options

Preview Export

Export Selected

---

# 57. VISUAL STATUS

Use clear visual indicators.

Examples:

✓ Fabricated / Selected

○ Purchased / Excluded

🔩 Fastener

📁 Browser Folder

⚙ Assembly

📄 Part

Sheet Metal indicator

Warning indicator

Error indicator

Do not rely solely on colour.

Use text/icons/tooltips so status remains understandable for colour-blind users.

---

# 58. EXPORT SCOPE SUMMARY

Before export show:

Root:
Machine_A.iam

Scan Mode:
Parts Recursive

Occurrences:
842

Unique Files:
217

Subassemblies:
24

Fabricated:
83

Purchased:
41

Fasteners:
79

Content Center:
14

Automatically Excluded:
126

Selected:

54 Parts
6 Assemblies

Outputs:

60 STEP
18 DXF
31 PDF

Estimated actions:

42 New
15 Outdated
3 Overwrite

---

# 59. REVIEW SELECTION

Before final export provide:

**Review Selection**

User can inspect exactly what will and will not be generated.

There should be no ambiguity about export scope.

---

# 60. SMART DEFAULTS

The tool should have sensible defaults while avoiding assumptions that could cause data loss.

Suggested initial defaults:

* Unique files
* Parts recursively
* Exclude Content Center
* Browser-folder exclusions enabled
* Purchased assemblies Do Not Traverse
* STEP AP242 where supported
* Export Missing + Outdated
* Never silently overwrite conflicts
* Preview before export
* Preserve user source files

---

# 61. PROJECT-SPECIFIC SETTINGS

Consider supporting optional project-specific settings.

If different Inventor projects have different manufacturing rules, allow settings to be associated with the current project.

Global settings remain available.

Priority:

Project preset
→ User preset
→ Global default

---

# 62. FUTURE EXTENSIBILITY

Design architecture so future exporters could be added for:

* STL
* SAT
* IGES
* Parasolid where supported
* 3MF
* Additional drawing formats
* BOM CSV
* BOM Excel
* Manufacturing ZIP package

Do NOT tightly couple the application to STEP only.

Create an exporter interface.

Conceptually:

IManufacturingExporter

Implementations:

StepExporter
DxfExporter
PdfExporter

Future exporters can be added without rewriting assembly scanning.

---

# 63. FUTURE BOM SUPPORT

Architecture should permit future generation of a manufacturing BOM containing:

* Part Number
* Description
* Revision
* Material
* Quantity
* Classification
* Export filenames

Do not necessarily implement advanced BOM functionality in Phase 1.

---

# 64. FUTURE PACKAGE EXPORT

Design for a future command:

**Create Manufacturing Package**

Example output:

JOB-1042/
│
├── STEP/
├── DXF/
├── PDF/
├── BOM/
└── ExportReport.csv

Potential optional ZIP:

`JOB-1042_REV-C_MANUFACTURING.zip`

---

# 65. INVENTOR API RESEARCH REQUIREMENT

Before writing substantial implementation code, verify the actual Autodesk Inventor API for the targeted version.

Specifically investigate:

* ApplicationAddInServer
* Inventor.Application
* AssemblyDocument
* AssemblyComponentDefinition
* ComponentOccurrence
* ComponentOccurrence.SubOccurrences
* ReferencedDocuments
* Document
* PartDocument
* SheetMetalComponentDefinition
* PropertySets
* iProperties
* BrowserPane
* BrowserNode
* BrowserFolder
* UserInterfaceManager
* ControlDefinitions
* Ribbon
* RibbonTab
* RibbonPanel
* CommandControls
* TranslatorAddIn
* TranslationContext
* NameValueMap
* DataMedium
* STEP translator
* DXF export
* PDF translator
* drawing document relationships
* Content Center identification
* suppression state
* document dirty state
* thumbnail retrieval
* command events
* context-menu integration

Do not hallucinate API members.

Do not invent translator GUIDs.

Use Autodesk documentation and installed Inventor interop definitions as authoritative references.

Where behavior varies by Inventor version, isolate it behind an adapter.

---

# 66. ARCHITECTURE

Use clean architecture.

Suggested Visual Studio solution:

SmartManufacturingExporter

/InventorAddIn
AddInServer
RibbonManager
CommandHandlers

/Core
Models
Interfaces
Enums

/Scanning
AssemblyScanner
BrowserTreeScanner
DocumentResolver
OccurrenceResolver

/Metadata
PropertyReader
ClassificationService
DrawingResolver

/Rules
RuleEngine
RuleEvaluator
SelectionEngine

/Export
ExportCoordinator
StepExporter
DxfExporter
PdfExporter
NamingEngine
DestinationResolver

/Validation
ValidationEngine
ValidationResult

/Settings
SettingsService
PresetService
ClassificationStore

/Logging
ExportLogger
CsvLogger
JsonLogger

/UI
MainWindow
ViewModels
Dialogs
Converters

/Utilities

Do not place the entire program inside ribbon-button event handlers.

---

# 67. MODEL DESIGN

Create strongly typed models such as:

AssemblyNode

ComponentNode

BrowserFolderNode

ComponentMetadata

ExportSelection

ExportJob

ExportOperation

ExportResult

ExportPreset

ClassificationRule

SelectionRule

NamingTemplate

ValidationIssue

Do not pass raw Inventor COM objects throughout the entire application unnecessarily.

Create internal models where appropriate.

---

# 68. COM RESOURCE HANDLING

Inventor uses COM interop.

Handle COM references carefully.

Avoid unnecessary retention of Inventor COM objects.

Do not indiscriminately call Marshal.ReleaseComObject without understanding ownership/lifetime.

Document the chosen COM lifetime strategy.

---

# 69. ERROR HANDLING

Handle expected failure cases gracefully.

Examples:

* Inventor closes during operation
* Assembly changes during scanning
* Component deleted
* File locked
* Network drive unavailable
* Output directory disappears
* Translator fails
* Invalid drawing
* Corrupt reference
* Permission denied

Errors should be meaningful to engineers.

Avoid generic:

"Something went wrong."

Prefer:

"STEP export failed for FAB-1024.ipt because Inventor's STEP translator returned an export failure."

---

# 70. DEPLOYMENT

Produce an installable Inventor add-in.

Include:

* `.addin` manifest
* Required DLLs
* Installation directory instructions
* User-level deployment where practical
* Machine-level deployment instructions if relevant

Document supported Inventor versions.

---

# 71. README

README must include:

## Requirements

* Autodesk Inventor version
* Windows requirements
* .NET requirements
* Visual Studio requirements

## Building

Exact build instructions.

## Debugging

How to:

1. Build project
2. Register/load add-in
3. Start Inventor from Visual Studio
4. Set breakpoints
5. Debug add-in

## Installing

Step-by-step installation.

## Uninstalling

Clean removal instructions.

---

# 72. DEVELOPMENT STRATEGY

DO NOT attempt every feature simultaneously.

Implement incrementally.

Every phase must compile and be testable before moving to the next.

---

# PHASE 1 - MINIMUM VIABLE EXPORTER

Implement:

* Inventor add-in registration
* Ribbon button
* Detect active assembly
* Scan top-level parts
* Unique source documents
* Basic checklist
* Select All
* Select None
* Output folder
* STEP export
* Basic error handling

Acceptance test:

Open simple assembly with five parts.

Select three.

Export.

Exactly three STEP files should be generated.

---

# PHASE 2 - ASSEMBLY HIERARCHY

Implement:

* Subassembly discovery
* Recursive scanning
* Tree view
* Tri-state selection
* Parts recursive mode
* Assemblies mode
* Quantity counting
* Unique file handling

Test with at least three nested assembly levels.

---

# PHASE 3 - BROWSER FOLDERS

Implement:

* Inventor Browser folder discovery
* Folder hierarchy
* Folder checkboxes
* Automatic folder exclusions
* Fasteners/OTS rules

Test:

Create Fasteners and OTS Browser folders.

Put components inside.

They should automatically deselect according to preset.

---

# PHASE 4 - CLASSIFICATION

Implement:

* Fabricated
* Purchased
* Fastener
* Reference
* Do Not Export
* Do Not Traverse
* Persistent classification memory

Test Purchased assembly containing many children.

Scanner should avoid unnecessary traversal.

---

# PHASE 5 - SMART RULE ENGINE

Implement:

* Filename rules
* Browser-folder rules
* Part Number rules
* Description rules
* Content Center rules
* Classification actions
* Rule priority
* Selection Reason

---

# PHASE 6 - SMART STEP EXPORT

Implement:

* NamingEngine
* Part Number naming
* Revision naming
* Existing STEP detection
* Missing/Outdated status
* Collision detection
* STEP profiles
* AP203/AP214/AP242 as supported
* Progress
* Cancellation
* Logging

---

# PHASE 7 - MANUFACTURING EXPORTS

Implement:

* Sheet-metal detection
* Flat DXF
* Drawing detection
* PDF drawing export
* Manufacturing Package mode

---

# PHASE 8 - PROFESSIONAL UI

Implement/refine:

* Search
* Filters
* Details panel
* Thumbnail
* Export summary
* Preview Export
* Validation report
* Preset manager
* Classification manager
* Settings
* Context menus

---

# PHASE 9 - QUICK EXPORT + INVENTOR CONTEXT MENU

Implement:

* Quick Export
* Default preset
* Inventor Browser selection integration
* Smart Export Selected

---

# PHASE 10 - HARDENING

Test:

* 10 components
* 100 components
* 1,000+ occurrences
* Deep nesting
* Duplicate components
* Content Center
* OTS
* Browser folders
* Suppressed components
* Missing references
* Network locations
* Dirty documents
* Naming collisions
* Existing STEP files
* Large purchased assemblies

Profile performance.

Fix memory leaks.

Verify Inventor stability.

---

# 73. TESTING

Create unit tests for code that does not require Inventor where practical.

Especially test:

* NamingEngine
* RuleEngine
* Classification matching
* Selection priority
* Destination generation
* Existing file comparison
* Preset serialization
* Filename sanitisation

Separate Inventor-dependent integration tests.

---

# 74. TEST ASSEMBLIES

Create/document test scenarios.

### Test A

Simple assembly:
5 unique parts.

### Test B

Duplicates:
One bolt used 20 times.

Expected:
one unique source entry, quantity 20.

### Test C

Nested assembly:
three levels deep.

### Test D

Browser folders:

Fasteners
OTS
Fabricated

### Test E

Purchased assembly:
large child tree.

Expected:
Do Not Traverse.

### Test F

Sheet metal.

Expected:
STEP + optional DXF.

### Test G

Existing STEP.

Test:

Missing
Up-to-date
Outdated

### Test H

Naming collision.

Two components with same Part Number.

Must warn before export.

---

# 75. USER EXPERIENCE GOAL

The desired experience is:

User opens a large machine assembly.

Clicks:

**Smart Export**

Within a short period, the add-in presents:

Machine_A.iam

✓ Fabricated Frame
✓ FAB-001
✓ FAB-002
✓ FAB-003

✓ Conveyor
✓ CON-101
✓ CON-102

○ OTS
○ Motor
○ Gearbox

○ Fasteners
○ Bolts
○ Nuts

○ Robot
Purchased / Do Not Traverse

The user immediately understands what will be exported.

They can expand any assembly and change individual selections.

The tool reports:

"41 manufacturing files require export.

32 STEP files are missing.
6 STEP files are outdated.
3 sheet-metal DXFs are outdated.

76 OTS/Fastener files were automatically excluded."

User clicks:

**Preview Export**

Reviews the output.

Clicks:

**Export**

Receives a clean manufacturing package.

That is the target experience.

---

# 76. CRITICAL PRODUCT REQUIREMENTS

The following are NON-NEGOTIABLE:

1. Never silently modify source Inventor files.

2. Never silently overwrite conflicting output files.

3. Understand assembly/subassembly hierarchy.

4. Understand Inventor Browser folders where the API permits.

5. Allow Browser folders such as OTS and Fasteners to be excluded.

6. Allow individual parts within subassemblies to be selected.

7. Give the user control over whether subassemblies are traversed.

8. Purchased/OTS assemblies must support Do Not Traverse.

9. Duplicate occurrences should normally export once.

10. Every automatic exclusion should have an understandable reason.

11. Manual user overrides must be possible.

12. Naming collisions must be detected before export.

13. One failed component must not terminate the complete export.

14. Large assemblies must be handled efficiently.

15. Do not invent Autodesk Inventor API functionality.

---

# 77. DELIVERABLES

Produce:

1. Complete Visual Studio solution.
2. Compilable C# source.
3. `.addin` manifest.
4. WPF/WinForms UI.
5. README.
6. Build instructions.
7. Installation instructions.
8. Debugging instructions.
9. Architecture documentation.
10. User guide.
11. Example presets.
12. Example classification rules.
13. Example exclusion rules.
14. Unit tests.
15. Integration-test plan.
16. Known limitations.
17. Supported Inventor-version matrix.
18. Release build instructions.

---

# 78. CODING AGENT INSTRUCTIONS

Do not respond by dumping thousands of lines of speculative code immediately.

First:

1. Analyse the specification.
2. Determine the installed/target Autodesk Inventor version.
3. Determine the correct .NET target.
4. Verify the relevant Inventor APIs.
5. Propose the solution architecture.
6. Identify API uncertainties.
7. Identify features that depend on version-specific Inventor behavior.
8. Create the Visual Studio solution structure.
9. Implement Phase 1.
10. Compile.
11. Fix all compilation errors.
12. Explain how Phase 1 should be tested in Inventor.
13. Only after the foundation works, proceed sequentially through later phases.

When an Autodesk Inventor API call is uncertain:

DO NOT GUESS.

Consult the appropriate Autodesk documentation or inspect the referenced Inventor interop library.

Keep Inventor-specific implementation behind clearly defined interfaces where practical.

At the end of every development phase provide:

* What was implemented
* Files added/changed
* Architecture changes
* Known limitations
* How to build
* How to test
* What should be implemented next

The objective is not merely to produce code.

The objective is to produce a reliable, maintainable, professional Autodesk Inventor manufacturing-export add-in suitable for repeated real engineering use.
