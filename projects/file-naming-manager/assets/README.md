# File Naming Manager visual assets

These project-owned assets provide the File Naming Manager ribbon and window identity. They contain no Autodesk artwork or third-party logo.

| File | Purpose |
| --- | --- |
| `file-naming-logo.png` | 1280 x 1280 transparent source artwork |
| `file-naming-16.png` | Small Inventor ribbon icon |
| `file-naming-32.png` | Large Inventor ribbon and WPF window icon |

The source artwork was generated programmatically on 2026-09-23 with `generate-icons.ps1` (Windows PowerShell 5.1, `System.Drawing`, no installs or external downloads). The design is a filing tag silhouette in deep cobalt blue (#1F4E9C) with a punch hole near the pointed tip, and three short amber-gold (#F2A93B) horizontal bars on the tag face suggesting a numbered index strip - matching the tag/label plus ordering motif specified for this add-in. It uses flat vector-like geometry, high contrast, a crisp silhouette, and minimal internal detail, with no text, letters, Autodesk logo, border, shadow, or photorealism, consistent with the Smart Export icon set.

The 16 and 32 pixel derivatives are cropped to the visible alpha bounds, padded by 8 percent, and resized with `System.Drawing`'s `HighQualityBicubic` interpolation mode. `System.Drawing` has no Lanczos resampler, so bicubic is the closest available high-quality option; regenerate derivatives from the source rather than scaling an existing small icon.
