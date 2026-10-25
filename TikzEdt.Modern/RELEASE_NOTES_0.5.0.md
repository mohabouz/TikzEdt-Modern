# TikzEdt Modern 0.5.0 Preview

Windows x64 development preview. This release includes the editor workflow upgrades
and the calculated-coordinate/plot control-marker fix.

## Downloads

- **Setup-win-x64.exe**: per-user installer, Start-menu shortcuts and uninstaller.
- **Portable-win-x64.zip**: extract the entire ZIP into a writable folder and run
  `TikzEdt.Modern.exe`.
- Both include the .NET runtime. A separate .NET installation is not required.
- The installer is unsigned; Windows may display a SmartScreen warning.
- TeX Live or MiKTeX, the required scientific packages, and Poppler's `pdftocairo`
  and `pdfinfo` are separate prerequisites. SyncTeX is needed for source navigation.
- Save your work and close the running app before installing an update. Keep
  portable files together; do not copy only the executable.

## New authoring and safety features

- Read-only Toolchain Doctor for engines, converters and package discovery.
- Atomic saves, external-change conflict protection, local history and recovery.
- Searchable command/file palette (Ctrl+K).
- Global/project JSON snippets with linked fields, Tab navigation and completion.
- Cancellable vector math hover using the selected engine and preamble.
- Symbol search by meaning, fuzzy search, favorites, recents and package hints.
- Background package/library-aware completion and macro/style/coordinate indexing.
- Conservative semantic warnings with explicit, undoable quick fixes (Ctrl+.).
- SyncTeX source-to-preview navigation (Ctrl+Shift+J) and Alt+click back to source.
- Validated PNG/JPEG/BMP/TIFF paste/drop and managed relative image assets.

## Fixes

- Function plots no longer make unrelated WYSIWYG markers disappear.
- Constant calculated Cartesian/polar endpoints are editable without flattening
  their formulas. Repeated movements combine translations instead of growing
  deeply nested expressions.
- Correct source offsets are retained around opaque/dynamic plot coordinates.
- Dark list templates keep the command palette and history lists readable.
- Completion handles multiword option values and linked user snippets.

## Verification and current limits

Regression coverage includes save/recovery, snippets, completion, diagnostics,
selection, snapping, transforms, formatting, Chemfig, PST-Labo and snippet caching.
Real rendering, image imports and SyncTeX were checked with pdfLaTeX, LuaLaTeX and
XeLaTeX, including the reported grid/function plot and translated calculated axes.

This is a development preview, not complete TeX semantic interpretation. Dynamic
macros, arbitrary TikZ calc expressions and generated function-plot samples are
rendered by TeX; they do not receive guessed independent draggable handles.
SyncTeX navigation currently targets displayed page 1. Recovery/history copies
have a 2 MB per-copy limit. Image assets support raster formats, not SVG/PDF import.
See [editor workflows](EDITOR_WORKFLOWS.md) for details and shortcuts.
