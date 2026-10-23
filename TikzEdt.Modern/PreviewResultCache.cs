using System.IO;

namespace TikzEdt.Modern;

/// <summary>Small session cache; recorder dependencies prevent stale included files.</summary>
internal sealed class PreviewResultCache
{
    private readonly bool _requirePng;
    public PreviewResultCache(bool requirePng = true) => _requirePng = requirePng;
    private sealed record Dependency(string Path, long Length, long Modified);
    private sealed record Entry(string Key, TexPreviewResult Result, Dependency[] Dependencies);
    private readonly LinkedList<Entry> _entries = [];
    private readonly object _gate = new();
    private const int Capacity = 8;

    public TexPreviewResult? Find(string key)
    {
        lock (_gate)
        {
            var node = _entries.First;
            while (node is not null)
            {
                var next = node.Next;
                if (node.Value.Key == key)
                {
                    var entry = node.Value;
                    _entries.Remove(node);
                    if (!File.Exists(entry.Result.PdfPath) || (_requirePng && !File.Exists(entry.Result.PngPath)) ||
                        entry.Dependencies.Any(dependency => Snapshot(dependency.Path) != dependency)) return null;
                    _entries.AddFirst(entry);
                    return entry.Result;
                }
                node = next;
            }
        }
        return null;
    }

    public void Store(string key, TexPreviewResult result, string workingDirectory)
    {
        if (result.ExitCode != 0 || !File.Exists(result.PdfPath) || (_requirePng && !File.Exists(result.PngPath))) return;
        var recorder = Path.ChangeExtension(result.PdfPath, ".fls");
        if (!File.Exists(recorder)) return; // Never cache without dependency information.
        var jobDirectory = Path.GetDirectoryName(result.PdfPath)! + Path.DirectorySeparatorChar;
        try
        {
            var dependencies = File.ReadLines(recorder).Where(line => line.StartsWith("INPUT ", StringComparison.Ordinal))
                .Select(line => Path.GetFullPath(line[6..].Trim().Trim('"'), workingDirectory))
                .Where(path => !path.StartsWith(jobDirectory, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(Snapshot).ToArray();
            if (dependencies.Length == 0 || dependencies.Any(dependency => dependency is null)) return;
            lock (_gate)
            {
                for (var node = _entries.First; node is not null;)
                {
                    var next = node.Next;
                    if (node.Value.Key == key) _entries.Remove(node);
                    node = next;
                }
                _entries.AddFirst(new Entry(key, result, dependencies.Cast<Dependency>().ToArray()));
                while (_entries.Count > Capacity) _entries.RemoveLast();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Cache failures must never prevent an otherwise successful preview.
        }
    }

    private static Dependency? Snapshot(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? new Dependency(path, file.Length, file.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }
}
