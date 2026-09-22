# WMP Tools Manager visual assets

These project-owned assets provide the WMP Tools Manager ribbon and window identity. They contain no Autodesk artwork or third-party logo.

| File | Purpose |
| --- | --- |
| `tools-manager-logo.png` | 1280 x 1280 transparent source artwork |
| `tools-manager-16.png` | Small Inventor ribbon icon |
| `tools-manager-32.png` | Large Inventor ribbon and WPF window icon |

The source artwork was generated programmatically on 2026-09-23 with `generate-icons.ps1` (Windows PowerShell 5.1, `System.Drawing`, no installs or external downloads). The design is a circular arrow in deep cobalt blue (#1F4E9C) - a thick ring built from a 290-degree donut arc - with an amber-gold (#F2A93B) triangular arrowhead accent at its leading tip, matching the update/refresh motif for this add-in's one command, "Check for updates". It uses flat vector-like geometry, high contrast, a crisp silhouette, and minimal internal detail, with no text, letters, Autodesk logo, border, shadow, or photorealism, consistent with the Smart Export and File Naming Manager icon sets.

The 16 and 32 pixel derivatives are cropped to the visible alpha bounds, padded by 8 percent, and resized with `System.Drawing`'s `HighQualityBicubic` interpolation mode. `System.Drawing` has no Lanczos resampler, so bicubic is the closest available high-quality option; regenerate derivatives from the source rather than scaling an existing small icon.
