# Smart Export visual assets

These project-owned assets provide the Smart Export ribbon and window identity. They contain no Autodesk artwork or third-party logo.

| File | Purpose |
| --- | --- |
| `smart-export-logo.png` | 1280 x 1280 transparent source artwork |
| `smart-export-16.png` | Small Inventor ribbon icon |
| `smart-export-32.png` | Large Inventor ribbon and WPF window icon |

The source artwork was generated with OpenAI's built-in image generation tool on 2026-09-14 using this prompt:

> Create a clean professional square app icon for an Autodesk Inventor add-in named Smart Export. Use a transparent background and a centered symbol combining a deep cobalt-blue gear ring, an isometric blue cube, and an amber-gold export arrow pointing outward to the right. Use flat vector-like geometry, high contrast, a crisp silhouette, and minimal internal detail. It must remain readable at 16 x 16 and 32 x 32 pixels. Include no text, letters, Autodesk logo, border, shadow, or photorealism.

The 16 and 32 pixel derivatives are cropped to the visible alpha bounds, padded by 8 percent, and resized with Lanczos resampling. Regenerate derivatives from the source rather than scaling an existing small icon.
