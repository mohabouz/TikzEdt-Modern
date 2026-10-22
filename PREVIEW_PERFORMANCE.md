# Preview update performance

Measured 2026-10-21 on this Windows/MiKTeX installation. These timings cover
the renderer (TeX plus rasterization), not UI presentation or debounce.
Fixture: a rectangle and a math-text node, 192 DPI; sources A, B, A, B.

| Engine | Before: repeated A/B (ms) | After: repeated A/B (ms) |
|---|---:|---:|
| pdfLaTeX | 861.5 / 764.9 | 1.4 / 0.9 |
| LuaLaTeX | 1905.2 / 1908.6 | 4.3 / 4.3 |
| XeLaTeX | 1664.2 / 1565.5 | 2.6 / 2.3 |

Fresh edited-source renders after this change remained approximately 895,
1956 and 1544 ms respectively. This is **not** a 3x improvement in fresh TeX
compilation; the large reuse improvement applies to recent states such as
undo/redo, repeated edits and background recompilation of unchanged content.

Other changes:

- Default typing debounce: 450 -> 150 ms (existing saved preferences respected).
- Editable-overlay debounce: 90 -> 30 ms.
- Automatic refresh first shows 192-DPI TeX pixels, then sharpens after idle to
  the configured zoom resolution. Explicit compilation uses requested quality.
- The last eight successful background previews are reused only while recorder
  input dependencies, settings, engine, directory, source and resolution match.
  Missing outputs and shell-escape jobs are not reused. Explicit compilation
  always invokes TeX. Cache metadata is bounded; this does not remove temp files.
- Compilation/process startup and cache validation run off the UI thread.
- Superseded zoom rasters and diagnostics cannot overwrite newer previews.

Verification commands:

```powershell
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --preview-benchmark --uncached
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --preview-benchmark
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --preview-cache-regression
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --selection-regression
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --drag-regression
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --ui-construction
```

Cache regression checks include reuse, changed `\\input` contents, DPI and flag
changes, missing output files, and explicit rebuilds. Selection checks also
exercise relative/polar group translations and one-step undo.
