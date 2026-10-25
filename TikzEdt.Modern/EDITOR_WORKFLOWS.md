# Editor workflow upgrades

The A1–A6, B1, B2, B4 and B6 first implementations are available in the Windows app.
These are independent implementations, not ports of Texpile code or completion data.

## Safety and discovery

- **Tools → Toolchain Doctor** (also in Compiler settings) probes configured engines,
  converters, SyncTeX, the package locator and scientific packages. Checks time out
  and can be cancelled. Nothing is installed and PATH is never modified.
- Saving uses a flushed temporary file and replacement, with before-save and saved
  checkpoints in `%LocalAppData%/TikzEdt/Modern/DocumentSafety`.
  External changes trigger a warning and explicit overwrite confirmation.
- **File → Version history / Recover unsaved documents / Reload from disk** restores
  into the editor. History restore is undoable; recovery does not silently overwrite
  the original file. Dirty drafts are checkpointed every two seconds. The latest
  two seconds of edits can be lost on a crash. Recovery/history copies are limited
  to 2 MB each, history to 50 copies per file and 100 MB overall. Larger source files
  can still be saved. Recovery copies are cleared after explicit save/discard, not
  silently aged out. This local history is not a substitute for backups or Git.
- **Ctrl+K** opens the searchable command/file palette. File scanning is bounded and
  skips hidden directories, links and generated/dependency folders. Actions retain
  their existing dirty-document confirmations and undo behavior.

## Authoring

- **Edit → Edit global/project snippets** edits validated JSON. Global snippets live
  at `%LocalAppData%/TikzEdt/Modern/snippets.json`; project overrides at
  `.tikzedt/snippets.json` alongside the active document. Literal prefix+Tab,
  Ctrl+Space and the insertion picker all support snippets.
- Snippets support `${1:default}`, `$1`, `${1}`, `$0`, `${SELECTION}` and
  `${TM_SELECTED_TEXT}`. Tab/Shift+Tab visit numbered fields; repeated fields mirror
  edits. Escape finishes. Insertion is one undo step. Contexts are `any`, `math`,
  `text`. Regex auto-expansion, nested placeholders, transforms and choice fields
  are intentionally not supported; validation rejects unsupported forms.
- Hover over recognized math for a delayed, cancellable vector TeX preview using
  the selected engine and current preamble. Diagnostics take precedence. Toggle
  automatic math hover in Editor settings, or use **Preview math at caret** in the
  palette. Hover compilations disable shell escape and have a 30-second limit.
- The symbol browser searches commands, meanings and approximate spellings.
  Right-click to favorite; Recent keeps the last 30 insertions. Hover shows known
  package requirements. Favorites/recents persist separately from projects.
- Ctrl+Space uses a background, non-executing source/preamble index: loaded
  packages/libraries, local macros, styles, named coordinates and literal values
  for common options. Scientific entries cover a curated TikZ/PGF, CircuitikZ,
  Chemfig and PST-Labo subset, including the existing apparatus/property catalog.
  Command help identifies package/library requirements. It is not a full CTAN
  database or a TeX macro interpreter; dynamically constructed definitions and
  arbitrary included files are not indexed.
- Semantic warnings currently cover known missing libraries/packages and closed
  stroke-option enums. Unknown custom commands, styles and anchors are not errors.
  **Ctrl+.** or the context menu offers explicit fixes. Each fix is one undo step,
  refuses stale source, and modifies the current document's preamble—not external
  files or the installed TeX distribution. Indirect package loads can still require
  human review. Existing syntax/compiler diagnostics remain active.

## Navigation and images

The WYSIWYG overlay isolates calculated/plot coordinate syntax so an unevaluable
function sample cannot hide unrelated markers. Constant arithmetic and a bounded
subset of PGF math provide editable calculated Cartesian/polar coordinates;
dragging adds a translation while retaining the formula. Repeated translations
are combined. Dynamic macros, arbitrary TikZ calc expressions and generated plot
samples remain TeX-rendered rather than receiving guessed draggable positions.

- **Ctrl+Shift+J / context menu → Show in preview** runs SyncTeX forward navigation.
  **Alt+click** the figure navigates back to source. Compile with `-synctex=1` first.
  Wrapper/preamble and generated bounding-box lines are mapped separately. Vector
  page dimensions map PDF points onto the canvas in WYSIWYG and bounded modes.
  Navigation is currently for displayed page 1; TeX may map a whole label/box rather
  than a specific path. Included-file locations are reported, never opened without
  a user action. Stale previews and generated metadata are not navigable source.
- **Tools/context menu → Import image asset**, single-image file drop, or bitmap
  paste in the source editor imports PNG/JPEG/BMP/TIFF. Save the document first.
  Imports normalize to PNG under a regular `assets` subfolder with unique names;
  originals are untouched. Limits: 20 MB encoded data, 25 million pixels. SVG/PDF
  import and rich HTML/Word/table paste are not part of this implementation.
- Source paste/drop inserts `\includegraphics[width=4cm]{assets/...}`; canvas import
  inserts a positioned TikZ node. A missing graphicx declaration is added to the
  preamble in the same undo step. Edit the width in source. Undo retains the copied
  image so that undo cannot erase a potentially reused file. If the document changes
  during an import, the asset is retained but no stale source edit is applied.

## Verification

Run the regression executable with one flag at a time:

```powershell
dotnet run --project tests/TikzEdt.RegressionTests/TikzEdt.RegressionTests.csproj -c Release -- --toolchain-regression
```

Additional deterministic flags: `--document-safety-regression`, `--palette-regression`,
`--user-snippets-regression`, `--math-hover-regression`, `--symbol-regression`,
`--intelligence-regression`, `--semantic-regression`, `--synctex-regression`,
`--image-assets-regression`. The existing UI/workspace, formatter, Chemfig, PST-Labo
and snippet batch regression suites also pass with these changes.
`--editor-workflow-ui` renders the new dialogs for contrast/layout checks and tests
the symbol panel and PDF-point-to-canvas transform without opening native windows.

Installed-tool checks: `--toolchain-installed`, `--math-hover-integration`,
`--synctex-integration`, `--image-assets-integration`. The latter three exercise
pdfLaTeX, LuaLaTeX and XeLaTeX; they require working local TeX/conversion tools.
