# Inventor project catalog

Every direct child of this folder is an independent Inventor automation with its own purpose, requirements, architecture, source, and tests.

| Folder | Project | Inventor target | Delivery |
| --- | --- | --- | --- |
| `smart-manufacturing-exporter` | Smart Manufacturing Exporter | 2027 | In-process C#/.NET add-in |
| `file-naming-manager` | File Naming Manager | 2027 | In-process C#/.NET add-in |

New projects use kebab-case folder names and PascalCase namespaces. A project can be an add-in, standalone utility, iLogic rule collection, or shared engineering workflow, but it must document how it interacts with Inventor and how that interaction is verified.
