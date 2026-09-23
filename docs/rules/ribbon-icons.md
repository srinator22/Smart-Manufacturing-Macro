# Ribbon icons

Load this before adding a ribbon command, adding a new tool with a ribbon button, or changing any
icon or tool logo. Every WMP command on Inventor's ribbon follows the same pipeline, so a new tool
looks like it belongs next to Inventor's own icons in both UI themes and at every display scale.

## The pipeline

    projects/<project>/assets/icons/<icon>-16.xaml   16-unit master, drawn for 16/20/24 px
    projects/<project>/assets/icons/<icon>-32.xaml   32-unit master, drawn for 32/40/48/64 px
            |  dotnet run --project shared/WmpIconRenderer
            v
    projects/<project>/assets/ribbon/<icon>-<dark|light>-<size>.png   (14 PNGs per icon, generated)
            |  EmbeddedResource in the AddIn project
            v
    WmpRibbon.RibbonIcons.Load  ->  Inventor theme + window DPI  ->  two PICTYPE_ICON pictures

- The masters are the source. The PNGs are generated artifacts: never edit, paint over, or
  replace them by hand. `./scripts/check.sh` re-renders every master and fails on any PNG that is
  missing, stale, or has no master.
- Sizes and file names come from `WmpRibbon.RibbonIconSelector`; the renderer and the add-ins
  both read them from there, so never hard-code a size list elsewhere.

## Style

Match Inventor 2027's own ribbon icons: flat line-art shapes, one idea per icon, no text.

| Slot | Use it for | Dark theme | Light theme |
| --- | --- | --- | --- |
| `body` | The main object (part, document, tag, ring) | `#D8D8D8` | `#5E6773` |
| `shade` | The second face of a 3D object, a ferrule, a secondary part | `#A9B2BE` | `#8A94A1` |
| `detail` | Marks drawn on top of `body` (text lines, notches) | `#5A6678` | `#E8ECF0` |
| `accent` | The one blue element that says what the command is about | `#78C4F8` | `#1C8BD6` |
| `glass` | Translucent fill inside a lens or window | 35% `accent` | 25% `accent` |
| `gold` | The action or output (arrow out, pencil, highlight) | `#F8D080` | `#E0A526` |

The values live in `shared/WmpIconRenderer/Palette.cs`. The dark values were sampled from
Inventor 2027's dark-theme icons; changing one re-renders every icon in the workspace, so a
palette change needs a preview of all icons in both themes.

- Use at most one `accent` element and one `gold` element per icon. Most of the icon is `body`.
- Every command gets its own icon. Two buttons never share one, even inside the same tool.
- Where one shape crosses another (an arrow over a cube, a lens over a tag), cut a gap out of the
  shape underneath with `CombinedGeometry GeometryCombineMode="Exclude"` about 1.5 units wider than
  the shape on top (0.75 on the 16-unit grid). Never fake a gap by painting the background color:
  the background differs between themes.
- Holes (a tag's eyelet) are also cut with `Exclude`, so they are truly transparent.
- No gradients, drop shadows, glows, outlines in black, or baked-in backgrounds.

## Drawing a master

- File names are `<icon>-16.xaml` and `<icon>-32.xaml`; `<icon>` is lowercase kebab-case and
  unique across the whole workspace. Both masters are required.
- The root element is a `DrawingGroup` in the WPF presentation namespace. Colors are written as
  palette tokens (`Brush="{{accent}}"`); an unknown token fails the render.
- Everything drawn must stay inside `0,0` to `16,16` or `0,0` to `32,32`; the renderer rejects a
  master whose drawing leaves its grid. Exclusion shapes may extend past it.
- Keep straight edges on whole or half units, and keep features at least 2 units wide on the
  32-unit grid and 1 unit on the 16-unit grid; thinner details blur or vanish at 100%.
- The 16-unit master is a simplification, not a scaled copy: drop the second text line, thin
  details, and anything under a pixel. Copy an existing master as a starting point
  (`projects/file-naming-manager/assets/icons/` has a tag with a lens and a tag with a pencil).

## Render and inspect

    dotnet build shared/WmpIconRenderer
    dotnet run --project shared/WmpIconRenderer --no-build
    dotnet run --project shared/WmpIconRenderer --no-build -- --preview <scratch>/preview.png
    dotnet run --project shared/WmpIconRenderer --no-build -- --check

Open the preview and check both theme bands before committing: every size must read on the dark
(`#3C4452`) and the light (`#F5F5F5`) ribbon, and the 16 px column must still be recognizable.
Keep the preview out of the repository.

## Wiring a new icon into an add-in

1. In the AddIn project, embed the rendered folder once:

       <EmbeddedResource Include="..\..\assets\ribbon\*.png"
                         Link="Assets\Ribbon\%(Filename)%(Extension)"
                         LogicalName="<AddInAssemblyName>.Ribbon.%(Filename)%(Extension)" />

2. In `Activate`, load the pair before creating the button and keep both objects in fields for the
   add-in's lifetime:

       (standardIcon, largeIcon) = RibbonIcons.Load(
           inventorApplication, typeof(StandardAddInServer).Assembly, "<AddInAssemblyName>", "<icon>");

3. Pass them to `AddButtonDefinition` as the standard and large icons.
4. A tool window uses the light 32 px variant as its title-bar icon, because it reads on both light
   and dark title bars: add `..\..\assets\ribbon\<icon>-light-32.png` as a `Resource` in the UI
   project and reference `Assets/<icon>-light-32.png` from the window.
5. Extend the project's architecture test that asserts every variant exists, is embedded under the
   loader's resource name, and is loaded through `RibbonIcons.Load` (see
   `RibbonIconVariantsExistAndAreEmbeddedByTheirOwningHosts` in any project).

Never convert ribbon images with `AxHost.GetIPictureDispFromPicture` or any other path that
produces a `PICTYPE_BITMAP` picture: Inventor re-converts those and loses transparency, which is
what made the first generation of WMP icons look dark and fringed. `WmpRibbon.RibbonPicture` is
the only converter.

## Live verification

The render check proves the PNGs match their masters; only Inventor proves they look right. After
changing an icon, install the Release build, restart Inventor, and look at the WMP Custom Tools tab
in the dark theme at the user's display scale, then in the light theme (Application Options,
Colors, UI Theme; a theme change applies on the next start). Record what was checked in the task
file.

## Tool logos

A tool does not get separate raster logo art. Where a README or window needs a larger image, use
the tool's rendered 64 px variant. The 1280 px `*-logo.png` files and `assets/generate-icons.ps1`
scripts in older projects are superseded and are not a pattern to copy.
