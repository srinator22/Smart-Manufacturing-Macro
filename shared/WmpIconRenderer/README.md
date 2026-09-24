# WmpIconRenderer

Build tooling that turns every project's ribbon icon masters into the PNGs its add-in embeds, and
proves in `scripts/check.sh` that the committed PNGs still match their masters.

## Contract

- Input: `projects/<project>/assets/icons/<icon>-16.xaml` and `<icon>-32.xaml`, `DrawingGroup`
  masters whose colors are palette tokens such as `{{accent}}`.
- Output: `projects/<project>/assets/ribbon/<icon>-<dark|light>-<size>.png` for every size in
  `WmpRibbon.RibbonIconSelector` (16, 20 and 24 px from the 16-unit master; 32, 40, 48 and 64 px
  from the 32-unit master).
- `dotnet run --project shared/WmpIconRenderer` renders every PNG.
- `-- --check` re-renders in memory and exits 1 if a PNG is missing, differs in any pixel, or has
  no master. `scripts/check.sh` runs it after the test step.
- `-- --preview <path>` writes a contact sheet of every icon at every size on the dark and light
  ribbon backgrounds, for review; keep it out of the repository.
- A master with an unknown token, a missing 16- or 32-unit twin, a name that is not lowercase
  kebab-case, a name used by two projects, a drawing outside its grid, or a drawing that fills
  less than 28 by 26 of 32 units (14 by 13 of 16) fails with exit code 1.

## Assumptions

- `RenderTargetBitmap` rasterizes in software, so the pinned .NET SDK renders the same pixels on a
  developer machine and the CI runner; the check compares pixels exactly.
- The theme palette is in `Palette.cs`. Authoring rules are in
  [docs/rules/ribbon-icons.md](../../docs/rules/ribbon-icons.md).
