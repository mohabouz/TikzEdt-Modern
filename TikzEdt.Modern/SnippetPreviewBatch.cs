using System.IO;
using System.Text.Json;

namespace TikzEdt.Modern;

internal sealed record SnippetBatchProgress(int Completed, int Total, int Failed, string Current, string Engine);

// The manifest is checkpointed after every item. A crash/exit only repeats the
// current item; the cache itself is authoritative if Temp was partially cleared.
internal sealed class SnippetPreviewBatch
{
    internal sealed class Checkpoint
    {
        public int Version { get; set; } = 1;
        public bool Active { get; set; }
        public string Engine { get; set; } = "pdflatex";
        public PreambleSnapshot Preamble { get; set; } = new(null, null);
        public UserSettings Settings { get; set; } = new();
        public Dictionary<string, string?> Results { get; set; } = [];
    }

    private readonly string _directory;
    private readonly Func<SnippetItem, string, PreambleSnapshot, UserSettings, CancellationToken, Task<string>> _render;
    private readonly Func<IReadOnlyList<SnippetItem>, string, PreambleSnapshot, UserSettings, CancellationToken, Task<IReadOnlyList<SnippetRenderOutcome>>>? _renderPages;
    private readonly Func<SnippetItem, string, PreambleSnapshot, UserSettings, string> _cachePath;
    private readonly Func<string, PreambleSnapshot, UserSettings, CancellationToken, Task>? _validate;
    private CancellationTokenSource? _cancellation;
    private volatile bool _pauseRequested;
    public Task? Work { get; private set; }
    public bool IsRunning => Work is { IsCompleted: false };
    public string ManifestPath => Path.Combine(_directory, "batch-progress.json");

    public SnippetPreviewBatch() : this(SnippetLibrary.CacheDirectory, SnippetLibrary.RenderPreviewAsync, SnippetLibrary.GetCachePath, SnippetLibrary.RenderPreviewPagesAsync, SnippetPreviewConfiguration.ValidateAsync) { }

    internal SnippetPreviewBatch(string directory,
        Func<SnippetItem, string, PreambleSnapshot, UserSettings, CancellationToken, Task<string>> render,
        Func<SnippetItem, string, PreambleSnapshot, UserSettings, string> cachePath,
        Func<IReadOnlyList<SnippetItem>, string, PreambleSnapshot, UserSettings, CancellationToken, Task<IReadOnlyList<SnippetRenderOutcome>>>? renderPages = null,
        Func<string, PreambleSnapshot, UserSettings, CancellationToken, Task>? validate = null)
    { _directory = directory; _render = render; _cachePath = cachePath; _renderPages = renderPages; _validate = validate; }

    public Checkpoint? ReadCheckpoint()
    {
        if (!File.Exists(ManifestPath)) return null;
        var state = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(ManifestPath));
        if (state is null || state.Version != 1 || state.Preamble is null || state.Settings is null || state.Results is null ||
            state.Engine is not ("pdflatex" or "lualatex" or "xelatex"))
            throw new InvalidDataException("The saved snippet build is invalid. Start a new build from the Build menu.");
        state.Settings.Validate();
        return state;
    }

    public Task StartAsync(IReadOnlyList<SnippetItem> items, string engine, PreambleSnapshot preamble,
        UserSettings settings, IProgress<SnippetBatchProgress> progress) =>
        RunAsync(items, new Checkpoint { Active = true, Engine = engine, Preamble = preamble, Settings = settings.Clone() }, progress);

    public Task ResumeAsync(IReadOnlyList<SnippetItem> items, IProgress<SnippetBatchProgress> progress)
    {
        var state = ReadCheckpoint() ?? throw new InvalidOperationException("There is no saved snippet build.");
        state.Active = true;
        return RunAsync(items, state, progress);
    }

    public void Pause() { _pauseRequested = true; _cancellation?.Cancel(); }
    // Keep Active=true on disk so the next launch resumes automatically.
    public void StopForExit() => _cancellation?.Cancel();

    private Task RunAsync(IReadOnlyList<SnippetItem> items, Checkpoint state, IProgress<SnippetBatchProgress> progress)
    {
        if (IsRunning) throw new InvalidOperationException("A snippet preview build is already running.");
        SnippetPreviewConfiguration.Validate(state.Engine, state.Preamble, state.Settings);
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        _pauseRequested = false;
        var token = _cancellation.Token;
        Directory.CreateDirectory(_directory);
        // Acquire and persist before returning to the UI: even an immediate
        // application exit must leave enough information to resume the job.
        var lease = new FileStream(Path.Combine(_directory, "batch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { Save(state); }
        catch { lease.Dispose(); throw; }
        Work = Task.Run(async () =>
        {
            // Also protects the checkpoint from a second application instance.
            using var batchLease = lease;
            var completed = 0;
            var failed = 0;
            try
            {
                if (_validate is not null && items.Any(item => !File.Exists(_cachePath(item, state.Engine, state.Preamble, state.Settings))))
                {
                    progress.Report(new(0, items.Count, 0, "Checking shared preamble…", state.Engine));
                    await _validate(state.Engine, state.Preamble, state.Settings, token).ConfigureAwait(false);
                }
                foreach (var chunk in items.Chunk(16))
                {
                    token.ThrowIfCancellationRequested();
                    var pending = new List<SnippetItem>();
                    foreach (var item in chunk)
                    {
                        var path = _cachePath(item, state.Engine, state.Preamble, state.Settings);
                        var key = Path.GetFileName(path);
                        if (File.Exists(path)) { state.Results[key] = null; completed++; }
                        else if (state.Results.TryGetValue(key, out var previousError) && previousError is not null) { completed++; failed++; }
                        else pending.Add(item);
                    }
                    Save(state);
                    progress.Report(new(completed, items.Count, failed, pending.Count == 0 ? "" : $"Compiling {pending.Count} pages…", state.Engine));
                    if (pending.Count == 0) continue;
                    if (_renderPages is not null)
                    {
                        var outcomes = await _renderPages(pending, state.Engine, state.Preamble, state.Settings, token).ConfigureAwait(false);
                        foreach (var outcome in outcomes) Record(outcome.Item, outcome.Path, outcome.Error);
                    }
                    else
                    {
                        foreach (var item in pending)
                        {
                            token.ThrowIfCancellationRequested();
                            Exception? error = null;
                            try { await _render(item, state.Engine, state.Preamble, state.Settings, token).ConfigureAwait(false); }
                            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                            catch (Exception exception) { error = exception; }
                            Record(item, _cachePath(item, state.Engine, state.Preamble, state.Settings), error);
                        }
                    }
                }
                state.Active = false;
                Save(state);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                state.Active = !_pauseRequested;
                Save(state);
                progress.Report(new(completed, items.Count, failed, _pauseRequested ? "Paused" : "Saved for next launch", state.Engine));
            }
            catch
            {
                // A global configuration failure is not 1,046 broken snippets.
                // Do not automatically repeat it at every application startup.
                state.Active = false;
                Save(state);
                throw;
            }

            void Record(SnippetItem item, string path, Exception? error)
            {
                var key = Path.GetFileName(path);
                state.Results[key] = null;
                if (error is not null)
                {
                    var errorName = key + ".error.txt";
                    File.WriteAllText(Path.Combine(_directory, errorName), item.Name + "\n" + error);
                    state.Results[key] = errorName;
                    failed++;
                }
                completed++;
                Save(state);
                progress.Report(new(completed, items.Count, failed, "", state.Engine));
            }
        });
        return Work;
    }

    private void Save(Checkpoint state)
    {
        var temporary = ManifestPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, ManifestPath, true);
    }
}
