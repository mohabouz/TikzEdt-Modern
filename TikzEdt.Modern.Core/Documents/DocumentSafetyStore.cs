using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TikzEdt.Modern.Core.Documents;

public sealed record DocumentCopy(string Id, string? Path, string Text, DateTime CreatedUtc, string Reason);
public sealed record DiskDocument(string Text, string Fingerprint);
public sealed class DocumentConflictException(string path) : IOException("The file changed outside TikzEdt: " + path);

/// <summary>Bounded local history and recovery drafts, separate from the user's project.</summary>
public sealed class DocumentSafetyStore
{
    public const int MaxCopyBytes = 2 * 1024 * 1024;
    private readonly string _root;
    private readonly object _gate = new();
    public DocumentSafetyStore(string root) { _root = System.IO.Path.GetFullPath(root); }
    private string HistoryDirectory(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) path = path.ToUpperInvariant();
        return System.IO.Path.Combine(_root, "history", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))));
    }
    public static string? Fingerprint(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
    public static DiskDocument Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        return new(reader.ReadToEnd(), Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private static void AtomicWrite(string path, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { var bytes = new UTF8Encoding(false).GetBytes(text); stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string Save(string path, string text, string? expectedFingerprint, bool overwriteExternalChange = false)
    {
        path = System.IO.Path.GetFullPath(path);
        lock (_gate)
        {
            var existing = File.Exists(path) ? Read(path) : null;
            var actual = existing?.Fingerprint;
            if (!overwriteExternalChange && actual != expectedFingerprint) throw new DocumentConflictException(path);
            if (existing is not null) KeepCopy(path, existing.Text, "before-save");
            KeepCopy(path, text, "save checkpoint");
            // KeepCopy must succeed before any existing source file is replaced.
            AtomicWrite(path, text);
            return Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false).GetBytes(text)));
        }
    }
    public void KeepCopy(string path, string text, string reason)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxCopyBytes) return;
        lock (_gate)
        {
            var directory = HistoryDirectory(path);
            var copies = ReadCopies(directory);
            if (copies.FirstOrDefault()?.Text == text) return;
            var copy = new DocumentCopy(Guid.NewGuid().ToString("N"), System.IO.Path.GetFullPath(path), text, DateTime.UtcNow, reason);
            AtomicWrite(System.IO.Path.Combine(directory, copy.Id + ".json"), JsonSerializer.Serialize(copy));
            // Only remove files belonging to our generated history IDs, never project files.
            foreach (var old in copies.Skip(49)) File.Delete(System.IO.Path.Combine(directory, old.Id + ".json"));
            var history = System.IO.Path.Combine(_root, "history");
            var files = Directory.EnumerateFiles(history, "*.json", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p)).Where(f => Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(f.Name), "N", out _)).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            long total = 0;
            foreach (var file in files) { total += file.Length; if (total > 100L * 1024 * 1024) file.Delete(); }
        }
    }
    public IReadOnlyList<DocumentCopy> History(string path) { lock (_gate) return ReadCopies(HistoryDirectory(path)); }
    private static DocumentCopy[] ReadCopies(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var result = new List<DocumentCopy>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(file), "N", out _)) continue;
            try
            {
                if (new FileInfo(file).Length > MaxCopyBytes * 8L) continue;
                var copy = JsonSerializer.Deserialize<DocumentCopy>(File.ReadAllText(file));
                if (copy is not null && copy.Id == System.IO.Path.GetFileNameWithoutExtension(file) && copy.Text is not null) result.Add(copy);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(c => c.CreatedUtc).ToArray();
    }
    public bool WriteRecovery(Guid session, string? path, string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxCopyBytes) return false;
        lock (_gate)
        {
            var copy = new DocumentCopy(session.ToString("N"), path is null ? null : System.IO.Path.GetFullPath(path), text, DateTime.UtcNow, "unsaved recovery");
            AtomicWrite(System.IO.Path.Combine(_root, "recovery", copy.Id + ".json"), JsonSerializer.Serialize(copy)); return true;
        }
    }
    public IReadOnlyList<DocumentCopy> Recoveries() { lock (_gate) return ReadCopies(System.IO.Path.Combine(_root, "recovery")); }
    public void RemoveRecovery(Guid session)
    { lock (_gate) { var file = System.IO.Path.Combine(_root, "recovery", session.ToString("N") + ".json"); if (File.Exists(file)) File.Delete(file); } }
}
