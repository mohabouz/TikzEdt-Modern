using System.IO;
using TikzEdt.Modern;

internal static class SnippetBatchRegression
{
    private sealed class ImmediateProgress(Action<SnippetBatchProgress> report) : IProgress<SnippetBatchProgress>
    { public void Report(SnippetBatchProgress value) => report(value); }

    public static async Task RunConfigurationRegressionAsync(bool integration)
    {
        var settings = new UserSettings();
        var preamble = new PreambleSnapshot("unicode.tex", "\\usepackage{amsmath,fontspec}\n\\usepackage{polyglossia}");
        try
        {
            SnippetPreviewConfiguration.Validate("pdflatex", preamble, settings);
            throw new Exception("Incompatible compiler was accepted.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("requires LuaLaTeX or XeLaTeX")) { }
        SnippetPreviewConfiguration.Validate("lualatex", preamble, settings);
        SnippetPreviewConfiguration.Validate("xelatex", preamble, settings);
        SnippetPreviewConfiguration.Validate("pdflatex", new(null, "% \\usepackage{fontspec}\n\\usepackage{amsmath}"), settings);
        var directory = Path.GetFullPath(Path.Combine("tests/TikzEdt.RegressionTests", "batch-tests", Guid.NewGuid().ToString("N")));
        var renders = 0;
        var probes = 0;
        Task<string> Render(SnippetItem item, string engine, PreambleSnapshot snapshot, UserSettings preferences, CancellationToken token)
        { renders++; throw new Exception("Catalogue renderer should never be called for an invalid shared preamble."); }
        Task Probe(string engine, PreambleSnapshot snapshot, UserSettings preferences, CancellationToken token)
        { probes++; throw new InvalidOperationException("Shared preamble package unavailable"); }
        var batch = new SnippetPreviewBatch(directory, Render, (item, engine, snapshot, preferences) => Path.Combine(directory, item.Name + ".png"), validate: Probe);
        var items = Enumerable.Range(1, 100).Select(index => new SnippetItem { Name = index.ToString() }).ToArray();
        try { await batch.StartAsync(items, "pdflatex", new(null, null), settings, new ImmediateProgress(_ => { })); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("package unavailable")) { }
        if (probes != 1 || renders != 0 || batch.ReadCheckpoint() is not { Active: false, Results.Count: 0 })
            throw new Exception("Shared preamble failure was miscounted as snippet failures or left auto-resume enabled.");
        Console.WriteLine("Compiler/preamble compatibility, comment handling and one-time fail-fast preflight passed (zero false snippet failures).");
        if (!integration) return;
        var actual = PreambleSettings.ReadSnapshot();
        foreach (var engine in new[] { "lualatex", "xelatex" })
        {
            await SnippetPreviewConfiguration.ValidateAsync(engine, actual, settings, CancellationToken.None);
            var outcomes = await SnippetLibrary.RenderPreviewPagesAsync([
                new SnippetItem { Name = "Actual preamble rectangle", Sample = "\\begin{tikzpicture}\\draw[blue] (0,0) rectangle (2,1);\\end{tikzpicture}" },
                new SnippetItem { Name = "Actual preamble resistor", RequiredPackages = "circuitikz", Sample = "\\begin{circuitikz}\\draw (0,0) to[R] (3,0);\\end{circuitikz}" }
            ], engine, actual, settings, CancellationToken.None);
            if (outcomes.Any(outcome => outcome.Error is not null || !File.Exists(outcome.Path)))
                throw new Exception(engine + " actual-preamble rendering failed: " + outcomes.FirstOrDefault(outcome => outcome.Error is not null)?.Error);
            Console.WriteLine("Actual selected preamble, compatibility probe and cached snippet pages passed: " + engine);
        }
    }

    public static async Task RunPagesAsync()
    {
        var examples = Enumerable.Range(1, 12).Select(number => new SnippetItem
        {
            Name = "Page " + number,
            Sample = $"\\begin{{tikzpicture}}\\draw[blue,thick] (0,0) rectangle ({number},1);\\node at (0.5,0.5) {{{number}}};\\end{{tikzpicture}}"
        }).ToArray();
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var pages = await TexPreviewRenderer.RenderSnippetPagesAsync(examples, engine, new(null, null), new(), CancellationToken.None);
            if (pages.Count != 12 || pages.Select(Path.GetDirectoryName).Distinct().Count() != 1)
                throw new Exception("Not a single twelve-page document.");
            var widths = pages.Select(path => SnippetLibrary.LoadPreview(path).PixelWidth).ToArray();
            if (widths[0] < 150 || !widths.SequenceEqual(widths.Order())) throw new Exception("Page order or high-resolution rendering failed.");
            Console.WriteLine($"{engine}: twelve-page document -> twelve 360-DPI PNGs in {watch.ElapsedMilliseconds} ms; widths {widths[0]}..{widths[^1]}; {pages[0]}");
        }
        var runId = Guid.NewGuid().ToString("N");
        var good = new SnippetItem { Name = "Good page", Sample = "% " + runId + "\n\\begin{tikzpicture}\\draw[red] (0,0) circle (1);\\end{tikzpicture}" };
        var bad = new SnippetItem { Name = "Broken page", Sample = "% " + runId + "\n\\begin{tikzpicture}\\nonexistentBatchCommand;\\end{tikzpicture}" };
        var outcomes = await SnippetLibrary.RenderPreviewPagesAsync([good, bad], "pdflatex", new(null, null), new(), CancellationToken.None);
        if (outcomes[0].Error is not null || !File.Exists(outcomes[0].Path) || outcomes[1].Error is null)
            throw new Exception("Broken-page isolation did not preserve a good preview.");
        Console.WriteLine("Broken multi-page group split correctly; good preview cached and broken example reported.");
        var circuitPages = await TexPreviewRenderer.RenderSnippetPagesAsync([
            new SnippetItem { Name = "Resistor", RequiredPackages = "circuitikz", Sample = "\\begin{circuitikz}\\draw (0,0) to[R] (3,0);\\end{circuitikz}" },
            new SnippetItem { Name = "Arrow", Dependencies = "arrows.meta", Sample = "\\begin{tikzpicture}\\draw[-{Latex}] (0,0) -- (2,1);\\end{tikzpicture}" }
        ], "pdflatex", new(null, null), new(), CancellationToken.None);
        if (circuitPages.Count != 2) throw new Exception("Combined package/library preamble failed.");
        Console.WriteLine("Circuitikz + arrows.meta shared-preamble page compilation passed.");
    }

    public static async Task RunIntegrationAsync()
    {
        var runId = Guid.NewGuid().ToString("N");
        var items = new[]
        {
            new SnippetItem { Name = "batch-rectangle", Sample = "% " + runId + "\n\\begin{tikzpicture}\\draw[blue,thick] (0,0) rectangle (2,1);\\end{tikzpicture}" },
            new SnippetItem { Name = "batch-node", Sample = "% " + runId + "\n\\begin{tikzpicture}\\node {$E=mc^2$};\\end{tikzpicture}" }
        };
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        {
            var folder = Path.GetFullPath(Path.Combine("tests/TikzEdt.RegressionTests", "batch-tests", runId, engine));
            var batch = new SnippetPreviewBatch(folder, SnippetLibrary.RenderPreviewAsync, SnippetLibrary.GetCachePath, SnippetLibrary.RenderPreviewPagesAsync);
            SnippetBatchProgress? last = null;
            var progress = new ImmediateProgress(value => last = value);
            var preferences = new UserSettings();
            var preamble = new PreambleSnapshot(null, null);
            await batch.StartAsync(items, engine, preamble, preferences, progress);
            if (last is not { Completed: 2, Failed: 0 } || batch.ReadCheckpoint()!.Active)
                throw new Exception("Real-engine batch failed: " + engine + " — logs in " + SnippetLibrary.CacheDirectory);
            foreach (var item in items)
            {
                var path = SnippetLibrary.GetCachePath(item, engine, preamble, preferences);
                if (!path.StartsWith(SnippetLibrary.CacheDirectory) || new FileInfo(path).Length < 100)
                    throw new Exception("Batch did not publish the shared Temp cache image.");
            }
            var before = items.Select(item => File.GetLastWriteTimeUtc(SnippetLibrary.GetCachePath(item, engine, preamble, preferences))).ToArray();
            await batch.ResumeAsync(items, progress);
            var after = items.Select(item => File.GetLastWriteTimeUtc(SnippetLibrary.GetCachePath(item, engine, preamble, preferences))).ToArray();
            if (!before.SequenceEqual(after)) throw new Exception("Resume recompiled existing previews.");
            Console.WriteLine($"Real snippet batch + cache-only resume passed: {engine}");
        }
    }

    public static async Task RunAsync()
    {
        var directory = Path.GetFullPath(Path.Combine("tests/TikzEdt.RegressionTests", "batch-tests", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        var items = new[] { "first", "interrupted", "broken", "last" }
            .Select(name => new SnippetItem { Name = name, Sample = name }).ToArray();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<string>();
        var interrupt = true;
        var broken = true;
        string CachePath(SnippetItem item, string engine, PreambleSnapshot preamble, UserSettings settings) =>
            Path.Combine(directory, engine + "-" + item.Name + ".png");
        async Task<string> Render(SnippetItem item, string engine, PreambleSnapshot preamble, UserSettings settings, CancellationToken token)
        {
            calls.Add(item.Name);
            if (engine != "xelatex" || preamble.Content != "% captured preamble" || settings.CompilerFlags != "-synctex=0")
                throw new Exception("Saved engine, preamble or flags changed during resume.");
            if (item.Name == "interrupted" && interrupt)
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
            if (item.Name == "broken" && broken) throw new InvalidOperationException("Expected missing package failure");
            var path = CachePath(item, engine, preamble, settings);
            await File.WriteAllTextAsync(path, "cached preview", token);
            return path;
        }
        var reports = new List<SnippetBatchProgress>();
        var progress = new ImmediateProgress(value => reports.Add(value));
        var settings = new UserSettings { CompilerFlags = "-synctex=0" };
        var preamble = new PreambleSnapshot("custom.tex", "% captured preamble");
        var batch = new SnippetPreviewBatch(directory, Render, CachePath);
        var work = batch.StartAsync(items, "xelatex", preamble, settings, progress);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (!batch.ReadCheckpoint()!.Active) throw new Exception("No durable checkpoint before exit.");
        // A second instance must not overwrite the running job.
        try
        {
            await new SnippetPreviewBatch(directory, Render, CachePath).StartAsync(items, "xelatex", preamble, settings, progress);
            throw new Exception("Duplicate batch acquired the cache lease.");
        }
        catch (IOException) { }
        batch.StopForExit(); await work;
        if (!batch.ReadCheckpoint()!.Active || batch.ReadCheckpoint()!.Results.Count != 1)
            throw new Exception("Exit did not preserve auto-resume and completed progress.");
        interrupt = false;
        var reopened = new SnippetPreviewBatch(directory, Render, CachePath);
        await reopened.ResumeAsync(items, progress);
        var checkpoint = reopened.ReadCheckpoint()!;
        if (checkpoint.Active || calls.Count(name => name == "first") != 1 || calls.Count(name => name == "interrupted") != 2 ||
            calls.Last() != "last" || checkpoint.Results.Count != 4 || checkpoint.Results.Values.Count(value => value is not null) != 1)
            throw new Exception("Resume, cached success reuse or failure isolation failed.");
        if (reports.Last().Completed != 4 || reports.Last().Failed != 1) throw new Exception("Wrong final progress counters.");
        var errorLog = Path.Combine(directory, checkpoint.Results.Values.Single(value => value is not null)!);
        if (!File.ReadAllText(errorLog).Contains("Expected missing package failure")) throw new Exception("Failure log missing.");
        // A restart retries failed entries only; successful files remain cached.
        broken = false;
        await reopened.StartAsync(items, "xelatex", preamble, settings, progress);
        if (calls.Count(name => name == "broken") != 2 || calls.Count(name => name == "last") != 1 || reports.Last().Failed != 0)
            throw new Exception("Explicit rebuild did not retry only failed previews.");
        // Missing cache files must rebuild even when recorded as successful.
        File.Move(CachePath(items[0], "xelatex", preamble, settings), Path.Combine(directory, "removed-preview.png"));
        await reopened.ResumeAsync(items, progress);
        if (calls.Count(name => name == "first") != 2) throw new Exception("Missing cache file was not regenerated.");
        var pauseDirectory = Path.Combine(directory, "pause");
        entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        interrupt = true;
        var paused = new SnippetPreviewBatch(pauseDirectory, Render, (item, engine, snapshot, preferences) => Path.Combine(pauseDirectory, item.Name + ".png"));
        var pauseWork = paused.StartAsync([items[1]], "xelatex", preamble, settings, progress);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        paused.Pause(); await pauseWork;
        if (paused.ReadCheckpoint()!.Active || paused.IsRunning) throw new Exception("Manual pause should not auto-resume on launch.");
        Console.WriteLine("Snippet batch checkpoint, exit/reopen resume, cached reuse, frozen compiler context, missing-cache recovery, failure logs/retry, pause and multi-instance lease passed.");
        await RunChunkResumeAsync();
    }

    private static async Task RunChunkResumeAsync()
    {
        var directory = Path.GetFullPath(Path.Combine("tests/TikzEdt.RegressionTests", "batch-tests", Guid.NewGuid().ToString("N")));
        var items = Enumerable.Range(1, 33).Select(index => new SnippetItem { Name = index.ToString() }).ToArray();
        var groupSizes = new List<int>();
        string CachePath(SnippetItem item, string engine, PreambleSnapshot preamble, UserSettings settings) => Path.Combine(directory, item.Name + ".png");
        async Task<IReadOnlyList<SnippetRenderOutcome>> RenderGroup(IReadOnlyList<SnippetItem> group, string engine, PreambleSnapshot preamble, UserSettings settings, CancellationToken token)
        {
            groupSizes.Add(group.Count);
            var outcomes = new List<SnippetRenderOutcome>();
            foreach (var item in group)
            {
                var path = CachePath(item, engine, preamble, settings);
                await File.WriteAllTextAsync(path, "preview", token);
                outcomes.Add(new(item, path, null));
            }
            return outcomes;
        }
        Task<string> Individual(SnippetItem item, string engine, PreambleSnapshot preamble, UserSettings settings, CancellationToken token) => throw new Exception("Chunk builder called single-item renderer.");
        var batch = new SnippetPreviewBatch(directory, Individual, CachePath, RenderGroup);
        var interrupted = false;
        var progress = new ImmediateProgress(value =>
        {
            if (value.Completed == 16 && !interrupted) { interrupted = true; batch.StopForExit(); }
        });
        await batch.StartAsync(items, "pdflatex", new(null, null), new(), progress);
        if (!groupSizes.SequenceEqual([16]) || batch.ReadCheckpoint() is not { Active: true, Results.Count: 16 })
            throw new Exception("Multi-page batch did not checkpoint the first chunk on exit.");
        var reopened = new SnippetPreviewBatch(directory, Individual, CachePath, RenderGroup);
        await reopened.ResumeAsync(items, new ImmediateProgress(_ => { }));
        if (!groupSizes.SequenceEqual([16, 16, 1]) || reopened.ReadCheckpoint() is not { Active: false, Results.Count: 33 })
            throw new Exception("Chunk resume did not reuse completed pages.");
        Console.WriteLine("33-item multi-page job used 16/16/1 groups and resumed without rebuilding the first group.");
    }
}
