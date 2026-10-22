# Snippet preview batch builds

Use **Build → Compile all snippet previews** to build the entire catalogue using
the selected compiler, current preamble and compiler flags. The job compiles
groups of up to 16 missing previews into one multi-page PDF, then converts all
pages in one PNG conversion process. Normal editing remains available.
Each snippet gets its own tightly cropped page, and page count/order is checked
before any PNG is assigned to its cache key. Failed groups are recursively
split, isolating broken snippets; full standalone examples use individual builds.

Previews now render at 360 DPI (formerly 120), with larger sidebar cards and a
larger selected-preview area. Very large pages have a raster memory limit.
The cache version includes this resolution upgrade, so old low-resolution
images are regenerated rather than enlarged.

Previews used by both the sidebar and the snippet browser are stored in:

```
%TEMP%\TikzEdt\Modern\SnippetPreviews
```

The Build menu can open this folder, pause the job, or resume a saved job.
The toolbar shows progress, engine, current snippet and failed count.

`batch-progress.json` stores the engine, preamble snapshot, compiler settings,
and completed/failed preview keys. It is atomically updated after each item.
Closing the application leaves the job marked active, so the next launch
automatically resumes it with its original settings. An interrupted group is
retried; any pages already published to cache are reused. Manually paused jobs resume only when
requested from the menu. A file lease prevents two instances from overwriting
the same running job.

Failures do not abort the batch. Full errors are stored as `*.png.error.txt`
in the cache folder. Choosing **Compile all snippet previews** again reuses
successful previews and retries failures. Changed snippet source, engine,
compiler flags or preamble produce different cache keys.

The cache is temporary: Windows or cleanup utilities can remove it. Missing
images are regenerated on resume; if the entire folder is removed, start a new
batch. No document files are changed.

## Shared-preamble failures

Before rendering missing previews, a batch checks the shared preamble once.
An invalid compiler, missing package/font, or preamble error stops the job with
one configuration message, rather than counting the entire catalogue as failed.
In particular, `fontspec`, `polyglossia` and `unicode-math` cannot use pdfLaTeX.
Select LuaLaTeX or XeLaTeX and start **Compile all snippet previews** again.
**Resume** retains the engine/preamble snapshot from the saved job; its menu
entry identifies the saved engine. Existing previews for other contexts are kept.

The separate snippet browser also checks the preamble first and renders groups
of up to 16 pages. Category thumbnails share cached tasks, do not show a compile
indicator for disk-cache hits, and ignore failures from superseded contexts.

Regression checks:

```
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --snippet-batch-regression
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --snippet-batch-integration
dotnet run --project .codex-smoke/PreambleSmoke.csproj -- --snippet-pages
```

The integration check builds two examples with each of pdfLaTeX, LuaLaTeX and
XeLaTeX, then verifies cache-only resume without changing image timestamps.
The page regression builds twelve-page documents with all three engines,
verifies numeric page ordering and image resolution, tests mixed package/library
loading, and isolates a broken example. The checkpoint regression also checks
a 33-item job split into 16/16/1 groups across an exit and resume.
