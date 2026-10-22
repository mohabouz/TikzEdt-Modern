# Drawing workspace

All drawing tools live in the top icon toolbar in WYSIWYG mode. Icons are vector
paths, so they do not depend on an installed symbol font and scale with DPI.

| Tool | Gesture |
|---|---|
| Select / Edit | Click or drag a selection box; Ctrl+click adds/removes. Drag X markers to change coordinates. Selected groups share a translation; Delete removes selected objects. |
| Node | Click to insert a text node. |
| Path / Rectangle / Ellipse | Drag; ellipse uses the dragged bounding box. |
| Bézier | Click start, control 1, control 2, end. |
| Smooth curve | Click through at least three points; Enter or double-click finishes. |
| Grid | Click two opposite corners. The bottom grid-step setting determines spacing. |
| Arc / Pie | Click center, start, end direction. The first radius is preserved; the sweep is counterclockwise. Pie closes to its center. |

New multi-click drawings use the current snapping settings. Escape cancels an
unfinished drawing without changing source. Completed drawings insert ordinary
TikZ commands in one undo operation and use the same TeX preview pipeline.
Bézier coordinates inside `controls ... and ...` use small circular handles;
curve endpoints and other anchors retain thin X markers. Both remain fixed in
on-screen size while zooming and use the existing selection/drag behavior.

The Files sidebar browses folders and opens TeX/TikZ sources on double-click,
with the existing save/discard prompt protecting the current document.
The Snippets sidebar has counted accordion categories (including Circuitikz
subcategories). Opening a category closes the previous category. Each element
has a small preview rectangle; visible entries compile on demand with at most
two jobs at once and then use the disk cache. Search filters the categories
and their entries. It also previews the selected snippet,
and inserts code or an example. First previews compile with progress feedback;
subsequent visits use the disk cache. The full browser remains under Browse.
Use the thin chevron on the sidebar's right edge, the top folder icon, or Ctrl+B
to collapse/expand it. Drag its edge to resize; crossing 120 pixels collapses it
to a 30-pixel vertical Files/Snippets rail. Click either vertical tab to reopen
that panel. The previous expanded width is restored, and the chevron changes
direction. Dragging a collapsed edge right also expands it once sufficiently
wide. Cancelled resize gestures restore the starting state. View > Files /
Snippets also reopens the panel.

## Folding

The editor right-click menu includes Undo/Redo, Cut/Copy/Paste/Delete, Select All,
Format Code, and Fold/Unfold All. Format Code (Ctrl+Shift+F) formats selected
lines, or the full document if nothing is selected. It changes indentation only,
respects tabs/spaces preferences, leaves verbatim bodies and line endings intact,
and is one undo operation.

Use the editor gutter to fold individual multiline environments, scopes,
brace groups/loops, or drawing commands. View > Fold all / Unfold all and the
top toolbar buttons operate on all regions. Keyboard shortcuts are
Ctrl+Shift+[ and Ctrl+Shift+]. Source edits refresh folding after a short
debounce; folding never changes document text. Commented commands, escaped
braces, incomplete regions and single-line constructs are not folded.
