namespace TikzEdt.Modern.Core.Documents;

public static class PaletteSearch
{
    public static int Score(string query, string text)
    {
        var total = 0; text = text.ToUpperInvariant();
        foreach (var term in query.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (text.StartsWith(term, StringComparison.Ordinal)) { total += 200; continue; }
            var index = text.IndexOf(term, StringComparison.Ordinal);
            if (index >= 0) { total += 100 - Math.Min(80, index); continue; }
            var cursor = 0; var gap = 0; var previous = -1;
            foreach (var character in term)
            {
                var found = text.IndexOf(character, cursor);
                if (found < 0) return -1;
                gap += previous < 0 ? found : found - previous - 1; previous = found; cursor = found + 1;
            }
            total += Math.Max(1, 40 - gap);
        }
        return total;
    }
    public static IReadOnlyList<string> FindFiles(string root, CancellationToken token)
    {
        var files = new List<string>(); var pending = new Queue<(string Path, int Depth)>(); pending.Enqueue((Path.GetFullPath(root), 0)); var visited = 0;
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "artifacts", "node_modules", "output", "build", "dist", "target" };
        while (pending.TryDequeue(out var directory) && visited < 5000)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory.Path))
                {
                    token.ThrowIfCancellationRequested(); if (++visited > 5000) break;
                    var name = Path.GetFileName(entry); var attributes = File.GetAttributes(entry);
                    if (name.StartsWith('.') || (attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0) { if (directory.Depth < 8 && !ignored.Contains(name)) pending.Enqueue((entry, directory.Depth + 1)); }
                    else if (Path.GetExtension(entry).ToLowerInvariant() is ".tex" or ".tikz" or ".sty" or ".cls") files.Add(entry);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return files;
    }
}
