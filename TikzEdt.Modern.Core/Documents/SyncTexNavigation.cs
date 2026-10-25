using System.Globalization;
using System.Diagnostics;
using System.Text;

namespace TikzEdt.Modern.Core.Documents;

public sealed record SyncTexBox(int Page, double X, double Y, double Width, double Height);
public sealed record SyncTexSource(string Input, int Line, int Column);
public sealed record SyncTexLineMap(int UserStartLine, int InjectionSourceLine, int InjectionLines, int SourceLines, int InjectionColumn = 1)
{
    public int Forward(int line, int column = 1) => UserStartLine + Math.Clamp(line, 1, SourceLines) - 1 + (line > InjectionSourceLine || line == InjectionSourceLine && column >= InjectionColumn ? InjectionLines : 0);
    public int? Reverse(int line)
    {
        var relative = line - UserStartLine + 1;
        if (relative >= InjectionSourceLine && relative < InjectionSourceLine + InjectionLines && (relative > InjectionSourceLine || InjectionColumn == 1)) return null;
        if (relative >= InjectionSourceLine + InjectionLines) relative -= InjectionLines;
        return relative >= 1 && relative <= SourceLines ? relative : null;
    }
}
public static class SyncTexNavigation
{
    private static double Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : 0;
    private static IEnumerable<Dictionary<string, string>> Records(string output)
    {
        var record = new Dictionary<string, string>();
        foreach (var line in output.Split('\n'))
        {
            var separator = line.IndexOf(':'); if (separator < 1) continue;
            var key = line[..separator].Trim(); var value = line[(separator + 1)..].Trim();
            if ((key == "Output" && record.ContainsKey("Output")) || (key == "Input" && record.ContainsKey("Input"))) { yield return record; record = new(); }
            record[key] = value;
        }
        if (record.Count > 0) yield return record;
    }
    public static IReadOnlyList<SyncTexBox> ParseBoxes(string output) => Records(output).Where(r => r.ContainsKey("Page") && r.ContainsKey("x") && r.ContainsKey("y"))
        .Select(r => new SyncTexBox((int)Number(r["Page"]), Number(r.GetValueOrDefault("h", r["x"])), Number(r.GetValueOrDefault("v", r["y"])), Math.Max(1, Number(r.GetValueOrDefault("W", "1"))), Math.Max(1, Number(r.GetValueOrDefault("H", "1"))))).ToArray();
    public static IReadOnlyList<SyncTexSource> ParseSources(string output) => Records(output).Where(r => r.ContainsKey("Input") && r.ContainsKey("Line"))
        .Select(r => new SyncTexSource(r["Input"], (int)Number(r["Line"]), Math.Max(1, (int)Number(r.GetValueOrDefault("Column", "1"))))).ToArray();
    public static async Task<string> QueryAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8)); var cancellation = timeout.Token;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Remove("SYNCTEX_VIEWER"); start.Environment.Remove("SYNCTEX_EDITOR");
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start }; process.Start();
        async Task<string> Drain(StreamReader reader) { var text = new StringBuilder(); var buffer = new char[2048]; int read; while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0) if (text.Length < 131072) text.Append(buffer, 0, Math.Min(read, 131072 - text.Length)); return text.ToString(); }
        var stdout = Drain(process.StandardOutput); var stderr = Drain(process.StandardError);
        try { await process.WaitForExitAsync(cancellation).ConfigureAwait(false); var result = await stdout.ConfigureAwait(false); await stderr.ConfigureAwait(false); return process.ExitCode == 0 ? result : ""; }
        finally { if (!process.HasExited) try { process.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch (OperationCanceledException) { } }
    }
    public static string PointArgument(int page, double x, double y, string path) => string.Join(":", page.ToString(CultureInfo.InvariantCulture), x.ToString("0.######", CultureInfo.InvariantCulture), y.ToString("0.######", CultureInfo.InvariantCulture), path);
}
