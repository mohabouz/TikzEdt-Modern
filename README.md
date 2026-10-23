# TikzEdt Modern

A Windows WYSIWYG and source editor for TikZ figures, modernized from the
original [TikzEdt project](https://github.com/mkantem/tikzedt). This repository
contains only the modern .NET 10 WPF app and its required dependencies.
This is a development preview.

## Current features

- Dark editor with syntax highlighting, completion, diagnostics and code folding.
- TeX-rendered WYSIWYG, preview and production modes; pdfLaTeX, LuaLaTeX and XeLaTeX.
- Shape tools, coordinate editing, multi-selection, snapping, Cartesian/polar grids and zoom.
- Preamble selection, editor/compiler settings and SVG clipboard export.
- Categorized TikZ/CircuiTikZ snippets with high-resolution cached previews.
- Resumable multi-page snippet builds and a self-contained Windows x64 installer.
- Transform-aware grid/mouse coordinates, faint scope guides and precision-preserving movement.
- Find/replace with regex, case and whole-word controls; categorized LaTeX symbols and an RGB/hex color picker.
- PDF/SVG/PNG/JPEG/TIFF/BMP figure export and document undo/redo from the visual editor.

- Chemfig molecule editing with compact flyout palettes, stereochemical bonds and lone pairs.
- PST-Labo apparatus, editable liquid layers and attached tubing with vector previews.

Windows downloads are available in [GitHub Releases](https://github.com/mohabouz/TikzEdt-Modern/releases).

## Building on Windows

Install the .NET 10 SDK. The required parser builds automatically as a .NET 10
dependency; no old solution or .NET Framework targeting pack is required.
From PowerShell at the repository root:

```powershell
dotnet build TikzEdt.Modern.slnx -c Release
dotnet run --project TikzEdt.Modern/TikzEdt.Modern.csproj
```

For compilation and previews, install MiKTeX or TeX Live with the packages
needed by your figures. `pdftocairo` and `pdfinfo` must be on PATH. Preambles
using `fontspec` or `polyglossia` require LuaLaTeX or XeLaTeX, not pdfLaTeX.

To build the installer with Inno Setup installed:

```powershell
./tools/packaging/build-setup.ps1 -Version 0.3.0
```

See [installer documentation](installer/README.md),
[snippet caching](SNIPPET_PREVIEW_CACHE.md), and
[drawing tools](DRAWING_TOOLS.md).

## Regression checks

```powershell
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --ui-construction
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --selection-regression
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --snippet-batch-regression
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --workspace-regression
```

The original applications, old solutions and unrelated source projects are not
included. The small parser dependency is documented in
[Dependencies/README.md](Dependencies/README.md). Generated binaries, IDE state,
local caches and installer output are not tracked.

The original GPL v3 license is in [LICENSE](LICENSE).

## Editing controls

- **Ctrl+F / Ctrl+H:** open find / replace. The input buttons toggle case (`Aa`),
  whole words (`ab`; off means partial matches), and .NET regular expressions
  (`.*`). Enter/Shift+Enter navigate matches. Regex replacements support capture
  groups such as `$1`; plain-text replacements treat them literally. Replace all
  is one undo step. Long-running regexes are stopped and searches are limited to
  20,000 matches to keep the UI responsive.
- **Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z:** document history also works with canvas focus.
  Text input fields keep their own undo behavior.
- **Symbols:** the tab and collapsed-sidebar button below Snippets offer searchable
  categories. Click a glyph to insert its LaTeX command; math delimiters are added
  unless the insertion point is already in a recognized math context.
- **Color palette toolbar button:** RGB sliders, preset swatches and `#RRGGBB`
  input. Insert an inline TikZ color value or define a named color before the
  picture. It does not automatically recolor selected objects.
- **File > Export figure / Export toolbar button:** save the current compiled
  figure in vector or raster formats. Raster exports use the configured preview
  DPI; JPEG/BMP transparency is flattened onto white.

The editing grid, snapped mouse position and new drawing coordinates use the
transform at the current source coordinate/path/scope, including picture scale,
rotation and shifts supported by the parser. Scope outlines are non-interactive
guides around editable coordinate bounds, not exact TeX-measured text/clip bounds.

## Chemfig visual editing and compilation

Use **Chemfig** on the toolbar, **Tools > Chemfig molecule editor**, or the editor
context menu. A new molecule is inserted into a TikZ node. Place the source caret
inside an existing supported `\chemfig{...}` command to edit it, or double-click
its node/control marker on the canvas. Applying changes is one document undo step.

The editable structure supports atom/group labels (including basic sub/superscripts),
dragging atoms, single/double/triple/wedge bonds, branches, ring closures, and
5/6-membered ring/benzene buttons. In Draw bond mode, drag from an atom to empty
space (default-length bonds on hexagonal/60-degree directions, or Cartesian/90-degree
directions) or another atom; drawing over an existing bond changes
its type. The C button inserts a visible `C`; the separate Skeletal carbon button
removes the label for implied carbon. The separate live TeX
preview displays the exact package output, including typeset atom labels.
The Chemfig window, editing canvas and controls use the shared dark theme. Its
temporary TeX preview uses light ink on a transparent dark-backed canvas for
contrast; this display adaptation does not alter molecule source or export colors.

The 42-pixel-wide flat tool rail offers a vertical C/H/N/O/S/P list, a More atoms menu,
skeletal carbon, rings, groups and a custom-label popover. Rings and groups each have a primary icon button and
a tiny corner arrow. These flyout menus open beside the rail (also available by
right-click or Alt+Down); choosing an alternative remembers it for that button.
Atom choices update selected atoms and the atom used for new bonds; bond choices
update selected bonds and the drawing tool. All nine bond styles stay grouped in
the top toolbar beside icon-only Select, Bond, Undo, Redo and Delete actions,
with accessible names and explanatory tooltips; selecting
a bond detects its style. Solid, hashed and hollow wedge endpoints match Chemfig:
`A>B` has its wide end at A and narrow end at B; `<` reverses it.
Lone pairs are also on the top toolbar: the pair icon adds/updates the chosen
pair on selected atoms; its arrow opens angle, line/dot style and removal controls.
3–8-membered rings and benzene are in the atom palette. Grid type, orientation and
snap controls occupy a full-width bottom bar below source and dialog actions.
Each pane has a bottom zoom selector; one shared Pan toggle enables hand dragging
in either pane and one Fit button fits both panes. Wheel zoom stays anchored under the pointer; Hand, middle-drag
or Space+drag pans without recompilation or changes to molecule coordinates.

The default construction grid is a hexagonal lattice with Cartesian axes, centred
at the origin and oriented at 30° (bond directions ±30°, ±90°, ±150°, spaced 60°
apart). The Hex at 0° selector provides horizontal/±60° directions instead.
Each nearest-neighbour step is one Chemfig bond-length factor
(the package's default `atom sep` is `3em`). Square Cartesian and hidden grid modes
are also available. Snap can be disabled independently or bypassed with Alt+drag.
Existing imported geometry is not snapped simply by clicking it.

In Move/select mode, click atoms or bonds, Ctrl+click to toggle membership, and drag
empty space for rectangle selection (Ctrl adds). Drag selected atoms/bonds to move
their endpoints together. Delete/Backspace removes the selection; Ctrl+A selects
all, arrow keys move the selection, Escape cancels/clears, and Ctrl+Z/Y undo/redo.
Text fields retain normal text-editing keys. Deleting an internal atom/bond may
create fragments: they remain editable and previewable, but Apply is disabled
until they are reconnected or unwanted fragments deleted.

Generated source is simplified without changing the molecular geometry: a chain
such as `CH_3-CH=CH-CH_2-CH_3` stays a chain, parentheses are reserved for side
branches, default zero angles/unit lengths are omitted, and unmodified regular
3–8-membered rings use compact `*n(...)` syntax. Imported and generated regular
rings follow Chemfig's south-west starting vertex and counterclockwise order.
Distorted/rotated rings retain explicit geometry rather than being forced into a
different shape just to shorten the code.

Initial source import supports simple chains/branches, explicit bond angles and
lengths, named hooks and compact 3–8-membered rings. All nine Chemfig bond types
are available, including solid/hashed/hollow Cram bonds in both directions.
Selecting a bond detects its type without editing the structure; mixed selections
remain mixed until a type is deliberately chosen.

The **Pairs** tab adds/removes non-bonding pairs on selected atoms at eight angles.
New pairs default to lines. **Settings > Chemfig > Default lone-pair representation**
chooses lines or two dots; imported pairs retain their notation. Simple numeric-angle
`\charge`/`\Charge` dot-pair (`\:`) and line-pair (`\|`) syntax round-trips through
the visual editor, including inside rings.

Amber advisory warnings check common main-group valences (including explicit
hydrogens and bond orders) and first-row electron counts with lone pairs. Carbon
with five single bonds is flagged. Warnings do not block editing or applying a
structure. This is not full chemical validation: ambiguous multi-atom labels,
metals, radicals and unusual charge states require manual review. The distinction
between bond-order totals and explicit hydrogens follows standard valence-checking
practice described in the [RDKit book](https://www.rdkit.org/docs/RDKit_Book.html);
RDKit is not a dependency of this application.

Advanced Chemfig macros, schemes, arbitrary formal-charge macros, global options and styled/anchored bonds are not visually
editable yet: they are rejected without changing the source and can still be edited
as LaTeX. Chemfig is loaded automatically for compilation; install it in your TeX
distribution. Package syntax follows the [Chemfig manual](https://tug.ctan.org/macros/latex/contrib/chemfig/chemfig-en.pdf).

In **Settings > Compiler**, enable **Start with the last used compiler** to restore
the engine across sessions, or disable it and choose a fixed default. Engine changes
are saved immediately and still trigger recompilation.

TeX runs in external processes; syntax checks, compiler-log analysis, SVG loading
and export/thumbnail PNG decoding run on background workers. Stale results are
discarded, and image/vector loading and syntax analysis have bounded worker gates.
WPF visuals stay on the UI thread. No C/C++ component was added: native
code does not remove TeX process/package startup, and no measured managed-code
hotspot currently justifies the interop/deployment cost. This is a responsiveness
improvement, not a claim of a fixed compilation speed multiplier.

### Vector previews and zoom

Main WYSIWYG, Preview and Production use TeX PDF → SVG → native WPF drawing
geometry through SharpVectors. The Chemfig dialog uses the same vector path for
its typeset preview; its editable structure, grid and controls are native WPF
vector elements. Structure/Preview zoom sliders and mouse wheel zoom in Chemfig
do not recompile TeX, regenerate SVG, or request a higher-resolution bitmap.
The main zoom controls likewise scale the existing drawing without a raster job.
SVG conversion disables print-to-fit shrinking and centering so paths retain
their PDF coordinates. Bounding-box metadata and editable point units use the
exact TeX point conversion (72.27 pt/in), distinct from PDF points (72 bp/in).
Compilation/conversion happens only when source, engine, preamble or a recorded
dependency changes (or an explicit compile is requested).

Fonts from TeX are SVG glyph outlines, so label sharpness does not depend on
display DPI or installed UI fonts. The full PDF page rectangle is preserved to
keep editable coordinates aligned. Source images included by the user remain
images; the app cannot make a bitmap photograph into genuine vector geometry.
PNG/JPEG exports, clipboard compatibility images and snippet thumbnails remain
raster formats by design. Preview DPI settings now affect raster exports,
not vector preview sharpness. Marker contrast checks may sample a
tiny temporary pixel probe; this is never used as the displayed figure.

Additional checks:

```powershell
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --chemfig-regression
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --chemfig-integration
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --vector-regression
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --vector-integration
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --vector-alignment
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --chemfig-stereo-regression
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --chemfig-stereo-integration
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --chemistry-editing-regression
```

### Source editing shortcuts

**Ctrl+/** toggles `%` comments on the current line or selected lines as one undo
step. **Ctrl+Alt+Up/Down** extends a rectangular column selection for multiline
typing; **Alt+Shift+arrows** and **Alt+drag** also use AvalonEdit's native column
selection. **Escape** returns to a single caret. This is column editing, not
arbitrary-position multiple cursors.

**Ctrl+Shift+F** / **Format Code** now inserts structural line breaks around
top-level `\begin`/`\end` and drawing commands, then indents. Existing comments,
macro bodies and literal/verbatim text are preserved. With a selection, only its
whole lines are formatted; a single undo restores the original text.

### PST-Labo apparatus editor (initial implementation)

Open **Tools > PST-Labo apparatus editor** to create a laboratory TeX document.
The categorized palette includes 38 apparatus/templates and component parts:
glassware, measuring/transfer equipment, titration/heating/reflux/gas-collection/
distillation/separation assemblies, standalone burette, supports, condensers,
burners, stoppers, clamps, tube segments, pH meter, hotplate and stirring bar.
Drawings come from the installed PST-Labo package, not substitute apparatus art.

Objects are compiled to cached SVG/native WPF vectors. Move, Ctrl+click selection,
group translation, duplicate, delete and undo/redo are available. Moving cached
apparatus and navigating the canvas do not recompile TeX. Vector ink is measured
without the transparent compile frame: selection boxes, hit areas, Fit and scene
export use the actual figure bounds. Old artwork stays visible during property
compilation, and Apply waits for current bounds/artwork.

The schema covers all 39 PST-Labo-specific keys in the 2.08 source. Relevant
properties appear by default; **Show all package keys** exposes the remaining
keys with applicability/upstream warnings. Liquid layers 1-3 have independent
levels/styles/custom colours; the separating funnel exposes its two phases and
uses isolated AqueoPhase/OrganicPhase overrides. Reagent labels, contents/solids
with particle counts, attachment choices, heating/gas, titration, pipette,
distillation, tap and common line properties are editable. Mutually exclusive
attachments are changed atomically. The white drawing surface preserves colours.

**Tube** creates straight, rounded-elbow or Bezier tubing by clicking endpoints.
Endpoints snap to cardinal figure ports or attach at a picked position inside
the figure bounds; empty-space endpoints remain free/grid-snapped. Attachments
follow apparatus movement. Selected tubes have draggable endpoint/routing handles
and editable diameter/bend/endpoint offsets. Detach, deletion cleanup, connected
duplication and undo are supported. Canvas and export use the same cubic path
geometry, including rounded elbows.
**Exact scene preview** compiles the whole assembly into a zoomable/pannable vector
view. Apply creates a complete TeX document, selects its engine and uses bounded
Preview mode; existing laboratory documents apply as one undo step.

LuaLaTeX + LuaPSTricks is the tested default. The catalogue and 39 native property
variants are compilation-tested, with vector-only assembled previews. XeLaTeX is offered for
installations with a compatible PSTricks/Ghostscript driver, but the full-scene
test timed out on the development installation; it is not certified here.
pdfLaTeX cannot directly compile this PSTricks document. The initial catalog uses
opaque graphics without experimental PDF tagging/transparency extensions.
Install `pst-labo` and `luapstricks` with the TeX distribution's package manager.

Generated documents carry versioned scene metadata and a content hash so they
can be reopened in the visual editor. If the TeX source is manually modified,
visual import refuses to overwrite it. Version-1 laboratory documents migrate to
the current format. The limits are 100 apparatus objects and 200 connecting tubes.
Arbitrary existing PSTricks/PostScript import, free-form custom material macros,
arbitrary apparatus deformation and editing every private drawing helper are not
implemented. Port locations are generic cardinal points or user-picked positions,
not a model of real glass joint compatibility. `pstBigPipette` is unfinished in
the inspected upstream sources (undefined clip macro and malformed arc), so it
is not offered as working equipment. `tubeDegagementDroit` is declared but unused
upstream and is labelled accordingly. See [coverage notes](TikzEdt.Modern/PST_LABO_COVERAGE.md).
This is a diagram builder, not a chemical or apparatus-safety simulator.

References: [PST-Labo](https://ctan.org/pkg/pst-labo),
[LuaPSTricks](https://github.com/zauguin/luapstricks).

```powershell
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --labo-navigation-regression
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --labo-integration
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --labo-visual-proof
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --labo-coverage-regression
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -- --labo-coverage-integration
```
