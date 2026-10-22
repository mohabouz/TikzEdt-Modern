# Legacy rendering architecture and modern port

## Why the legacy WYSIWYG matched Preview

The legacy editor did not redraw TikZ as approximate WPF shapes. Its preview
pane stacked three controls in `TikzEdt/Views/DocumentView.xaml`:

1. `RasterControl` draws the Cartesian or polar editing grid.
2. `TikzDisplay` rasterizes the PDF produced by TeX. It is transparent in
   WYSIWYG mode and opaque in Preview mode.
3. `PdfOverlay` draws selection/control points and changes the parse tree. It
   does not replace the TeX artwork.

All three layers share `CurrentBB` and `Resolution`. This common coordinate
system is the reason PDF pixels, grid coordinates, and editing handles align.
Fonts, math, line joins, fill rules, arrows, decorations, user styles, and
libraries are therefore always drawn by the selected TeX engine.

## Compile modes

`TEApplicationLogic/ViewModels/TEDocument.cs::Recompile` defines the modes:

- **WYSIWYG:** asynchronously parse and compile a temporary preview. Editing is
  disabled while the parser is stale. The compile writes an exact TikZ bounding
  box for layer alignment.
- **Preview:** compile the temporary preview without the editing overlay.
- **Production:** save and compile the real document. It deliberately does not
  auto-compile unsaved source changes.

`TexCompiler.AddJobExclusive` drops obsolete queued preview jobs. This matters
more than trying to complete every intermediate keystroke.

## Exact bounds and fast display

Before `\end{tikzpicture}`, the compiler injects `Consts.CodeToWriteBB`. TikZ
writes `current bounding box.south west` and `north east` to
`<jobname>_BB.txt`. Values are converted from TeX points to TikZ units and
become `CurrentBB`.

The PDF is rendered to a bitmap asynchronously by `TikzDisplayModel<T>`. It
keeps at most one pending bitmap job and rejects stale results whose PDF path no
longer matches the active document. The legacy compiler can also use a
precompiled format header, avoiding repeated preamble/package startup work.

## Modern implementation

`TexPreviewRenderer.cs` now restores the essential pipeline:

- the selected `pdflatex`, `lualatex`, or `xelatex` engine produces the pixels;
- the legacy bounding-box writer is injected into the preview compilation;
- `pdftocairo` rasterizes page one to a transparent PNG;
- raster DPI follows the WYSIWYG zoom level (300 DPI at 100%, increasing on
  demand up to a memory-safe cap) without recompiling TeX;
- the bitmap is placed in TikZ coordinates under the editor overlay;
- WPF paths, rectangles, ellipses, and nodes are invisible hit targets only;
- Preview mode reuses the same bitmap, hiding the raster and overlay;
- source edits are debounced and stale compile/raster processes are cancelled;
- Production mode does not auto-compile source edits.

This makes displayed commands correct by construction: if TeX can render a
command, it appears in WYSIWYG and Preview even when the parser cannot yet make
that command editable.

## Follow-up performance work

The next speed layer should port per-engine precompiled format management from
the legacy compiler. Formats must be cached separately for pdfLaTeX, LuaLaTeX,
and XeLaTeX and invalidated when the dynamic preamble changes. The current
exclusive/cancellable job pipeline should remain, because it prevents an old
render from replacing a newer one.
