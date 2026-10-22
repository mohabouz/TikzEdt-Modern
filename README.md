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

ChemFig-specific WYSIWYG editing has not been implemented in this baseline.

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
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --ui-construction
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --selection-regression
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --snippet-batch-regression
```

The original applications, old solutions and unrelated source projects are not
included. The small parser dependency is documented in
[Dependencies/README.md](Dependencies/README.md). Generated binaries, IDE state,
local caches and installer output are not tracked.

The original GPL v3 license is in [LICENSE](LICENSE).
