# TikzEdt Modern 0.5.1 Preview

Windows x64 bug-fix release for WYSIWYG control-marker alignment.

## Fixes

- Markers now respect TikZ's `x=...` and `y=...` coordinate units, including vector-valued units.
- Coordinate-unit changes in nested scopes replace the inherited units correctly.
- Explicit `cm`, `mm`, and other supported dimensions bypass coordinate units while retaining canvas scaling and rotation.
- Dragging, grid snapping, and mouse coordinates use the corrected coordinate mapping.

## Downloads

- **Setup-win-x64.exe**: per-user Windows installer.
- **Portable-win-x64.zip**: extract the entire archive and run `TikzEdt.Modern.exe`.
- Both include the .NET runtime. TeX Live or MiKTeX and Poppler remain separate prerequisites.
- Save your work and close the app before installing an update.
- The installer is unsigned.

## Verification

Coordinate-unit regressions cover nested overrides and shifts, mixed explicit and unitless coordinates, vector units, rotation, relative and polar coordinates, nodes, dragging, and snapping. Vector-border alignment was checked against actual pdfLaTeX, LuaLaTeX, and XeLaTeX output. Existing calculated-coordinate and Windows workspace regressions also pass.

This release contains the Windows fix only; the separate native Linux port is not included.
