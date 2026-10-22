using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace TikzEdt.Modern;

internal sealed class SnippetItem : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public string Code { get; init; } = "";
    public string Sample { get; init; } = "";
    public string Dependencies { get; init; } = "";
    public string RequiredPackages { get; init; } = "";
    private BitmapSource? _preview;
    public BitmapSource? Preview { get => _preview; set { _preview = value; Changed(); } }
    private string _state = "Waiting for preview";
    public string State { get => _state; set { _state = value; Changed(); } }
    public string? Error { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal static class SnippetLibrary
{
    private const string CacheVersion = "2-360dpi";
    public const double PreviewDpi = 360;
    public static string CacheDirectory { get; } = Path.Combine(Path.GetTempPath(), "TikzEdt", "Modern", "SnippetPreviews");
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> CacheLocks = new();

    public static IReadOnlyList<SnippetItem> Load(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "TheSnippets.xml");
        var document = XDocument.Load(path);
        var legacy = document.Descendants().Where(element => element.Name.LocalName == "SnippetsTable")
            .Select(element => new SnippetItem
            {
                Id = Value(element, "ID"),
                Name = Value(element, "Name"),
                Category = Value(element, "Category"),
                Description = Value(element, "Description"),
                Code = Value(element, "SnippetCode"),
                Sample = Value(element, "SampleCode"),
                Dependencies = Value(element, "Dependencies")
            });
        var cataloguePath = Path.Combine(Path.GetDirectoryName(path)!, "CircuitikzCatalog.json");
        var circuits = File.Exists(cataloguePath)
            ? JsonSerializer.Deserialize<CircuitCatalogue>(File.ReadAllText(cataloguePath))?.Entries ?? []
            : [];
        return legacy.Concat(circuits).OrderBy(item => item.Category).ThenBy(item => item.Name).ToArray();
    }

    private static string Value(XElement row, string name) => row.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() ?? "";

    public static string GetCachePath(SnippetItem item, string engine, PreambleSnapshot preamble, UserSettings settings)
    {
        var key = string.Join("\n", CacheVersion, engine, settings.CompilerIdentity,
            preamble.CacheIdentity, item.Sample, item.Dependencies, item.RequiredPackages);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(CacheDirectory, hash + ".png");
    }

    public static BitmapSource LoadPreview(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static async Task<string> RenderPreviewAsync(SnippetItem item, string engine,
        PreambleSnapshot preamble, UserSettings settings, CancellationToken cancellationToken)
    {
        SnippetPreviewConfiguration.Validate(engine, preamble, settings);
        var path = GetCachePath(item, engine, preamble, settings);
        var cacheLock = CacheLocks.GetOrAdd(path, _ => new SemaphoreSlim(1));
        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            return await RenderPreviewCoreAsync(item, engine, preamble, settings, cancellationToken);
        }
        finally { cacheLock.Release(); }
    }

    private static async Task<string> RenderPreviewCoreAsync(SnippetItem item, string engine,
        PreambleSnapshot preamble, UserSettings settings, CancellationToken cancellationToken)
    {
        var path = GetCachePath(item, engine, preamble, settings);
        if (File.Exists(path)) return path;
        if (string.IsNullOrWhiteSpace(item.Sample)) throw new InvalidOperationException("This snippet has no preview example.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.SnippetTimeoutSeconds));
        var libraries = string.IsNullOrWhiteSpace(item.Dependencies) ? "" : $"\\usetikzlibrary{{{item.Dependencies}}}\n";
        var source = string.Join("\n", item.RequiredPackages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(package => "% !TIKZEDT PACKAGE " + package)) + "\n" + item.Sample;
        var result = await TexPreviewRenderer.RenderAsync(source, engine, null,
            (preamble.Content ?? "") + "\n" + libraries, PreviewDpi, timeout.Token, settings);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.ExitCode != 0 || !File.Exists(result.PngPath)) throw new InvalidOperationException(result.Log);
        PublishPreview(result.PngPath, path);
        return path;
    }

    private static void PublishPreview(string source, string destination)
    {
        Directory.CreateDirectory(CacheDirectory);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.Copy(source, temporary, true);
        File.Move(temporary, destination, true);
    }

    public static async Task<IReadOnlyList<SnippetRenderOutcome>> RenderPreviewPagesAsync(IReadOnlyList<SnippetItem> items,
        string engine, PreambleSnapshot preamble, UserSettings settings, CancellationToken token)
    {
        var groups = items.GroupBy(item => GetCachePath(item, engine, preamble, settings)).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var acquired = new List<SemaphoreSlim>();
        var outcomes = new Dictionary<string, Exception?>();
        try
        {
            // Same locks as individual previews, acquired in stable order.
            foreach (var group in groups)
            {
                var gate = CacheLocks.GetOrAdd(group.Key, _ => new SemaphoreSlim(1));
                await gate.WaitAsync(token);
                acquired.Add(gate);
            }
            var missing = groups.Where(group => !File.Exists(group.Key)).Select(group => group.First()).ToArray();
            await RenderGroupAsync(missing);
            return items.Select(item =>
            {
                var path = GetCachePath(item, engine, preamble, settings);
                return new SnippetRenderOutcome(item, path, outcomes.GetValueOrDefault(path));
            }).ToArray();
        }
        finally { foreach (var gate in acquired) gate.Release(); }

        async Task RenderGroupAsync(IReadOnlyList<SnippetItem> group)
        {
            token.ThrowIfCancellationRequested();
            if (group.Count == 0) return;
            try
            {
                if (group.Count == 1)
                    await RenderPreviewCoreAsync(group[0], engine, preamble, settings, token);
                else
                {
                    var pages = await TexPreviewRenderer.RenderSnippetPagesAsync(group, engine, preamble, settings, token);
                    // No image is assigned until the complete page count is validated.
                    for (var index = 0; index < group.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        PublishPreview(pages[index], GetCachePath(group[index], engine, preamble, settings));
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                if (group.Count == 1) outcomes[GetCachePath(group[0], engine, preamble, settings)] = exception;
                else
                {
                    // A bad example or conflicting library cannot poison good pages.
                    var middle = group.Count / 2;
                    await RenderGroupAsync(group.Take(middle).ToArray());
                    await RenderGroupAsync(group.Skip(middle).ToArray());
                }
            }
        }
    }

    private sealed class CircuitCatalogue
    {
        public List<SnippetItem> Entries { get; set; } = [];
    }
}
