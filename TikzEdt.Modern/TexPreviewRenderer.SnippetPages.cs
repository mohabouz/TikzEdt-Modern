using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TikzEdt.Modern;

internal sealed record SnippetRenderOutcome(SnippetItem Item, string Path, Exception? Error);

internal static partial class TexPreviewRenderer
{
    internal static string BuildSnippetPagesSource(IReadOnlyList<SnippetItem> items, string engine, PreambleSnapshot preamble)
    {
        if (items.Any(item => string.IsNullOrWhiteSpace(item.Sample) || IsStandalone(item.Sample)))
            throw new NotSupportedException("Empty examples and full standalone documents require individual compilation.");
        var fragments = items.Select(item => PrepareFragment(item.Sample)).ToArray();
        var packages = items.SelectMany(item => item.RequiredPackages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Concat(items.SelectMany(item => Regex.Matches(item.Sample, @"(?m)^\s*%\s*!TIKZEDT\s+PACKAGE\s+([a-zA-Z0-9_.-]+)")
                .Select(match => match.Groups[1].Value)))
            .Concat(items.Any(item => item.Sample.Contains("\\begin{circuitikz}", StringComparison.Ordinal)) ? ["circuitikz"] : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var libraries = items.SelectMany(item => item.Dependencies.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal);
        var header = BuildEffectivePreamble(preamble.Content);
        // Explicit preview environments give exactly one tightly cropped page per
        // snippet, even if an example contains several TikZ pictures.
        header = StandaloneClassRegex().Replace(header, @"\documentclass{article}");
        header = Regex.Replace(header, @"\\PreviewEnvironment\s*\{(?:tikzpicture|circuitikz)\}", "");
        var source = new StringBuilder(@"\PassOptionsToPackage{active,tightpage}{preview}").AppendLine().AppendLine(header)
            .AppendLine(@"\usepackage{tikz}").AppendLine(@"\usepackage{preview}")
            .AppendLine(@"\setlength\PreviewBorder{2pt}");
        foreach (var package in packages) source.AppendLine($"\\usepackage{{{package}}}");
        var libraryList = string.Join(",", libraries);
        if (libraryList.Length > 0) source.AppendLine($"\\usetikzlibrary{{{libraryList}}}");
        foreach (var fragment in fragments) source.AppendLine(fragment.Preamble);
        source.AppendLine(@"\begin{document}");
        foreach (var fragment in fragments)
        {
            source.AppendLine(@"\begin{preview}\begingroup");
            if (engine == "lualatex") source.AppendLine(LuaPictureDirection);
            source.AppendLine(fragment.Source).AppendLine(@"\endgroup\end{preview}");
        }
        return source.AppendLine(@"\end{document}").ToString();
    }

    public static async Task<IReadOnlyList<string>> RenderSnippetPagesAsync(IReadOnlyList<SnippetItem> items,
        string engine, PreambleSnapshot preamble, UserSettings settings, CancellationToken cancellationToken)
    {
        var source = BuildSnippetPagesSource(items, engine, preamble);
        var directory = Path.Combine(SnippetLibrary.CacheDirectory, "jobs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var texPath = Path.Combine(directory, "snippet-pages.tex");
        var pdfPath = Path.ChangeExtension(texPath, ".pdf");
        await File.WriteAllTextAsync(texPath, source, new UTF8Encoding(false), cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Min(600, Math.Max(settings.CompilerTimeoutSeconds, settings.SnippetTimeoutSeconds * items.Count))));
        var compile = CreateProcess(settings.ResolveEngine(engine));
        foreach (var flag in new[] { "-interaction=nonstopmode", "-halt-on-error", "-file-line-error" }) compile.ArgumentList.Add(flag);
        foreach (var flag in UserSettings.ParseFlags(settings.CompilerFlags)) compile.ArgumentList.Add(flag);
        compile.ArgumentList.Add("-output-directory=" + directory);
        compile.ArgumentList.Add(texPath);
        var (compileCode, compileLog) = await RunProcessAsync(compile, timeout.Token);
        if (compileCode != 0 || !File.Exists(pdfPath)) throw new InvalidOperationException(compileLog);

        var info = CreateProcess("pdfinfo");
        foreach (var argument in new[] { "-f", "1", "-l", items.Count.ToString(CultureInfo.InvariantCulture), "-box", pdfPath }) info.ArgumentList.Add(argument);
        var (infoCode, metadata) = await RunProcessAsync(info, timeout.Token);
        var pageCount = Regex.Match(metadata, @"(?m)^Pages:\s*(\d+)");
        if (infoCode != 0 || !pageCount.Success || int.Parse(pageCount.Groups[1].Value, CultureInfo.InvariantCulture) != items.Count)
            throw new InvalidOperationException("Snippet PDF page count does not match the catalogue entries. " + metadata);
        var dpi = SnippetLibrary.PreviewDpi;
        foreach (Match size in Regex.Matches(metadata, @"(?m)^Page(?:\s+\d+)?\s+size:\s*([\d.]+)\s+x\s+([\d.]+)"))
        {
            var area = double.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture) * double.Parse(size.Groups[2].Value, CultureInfo.InvariantCulture) / (72 * 72);
            if (area > 0) dpi = Math.Min(dpi, Math.Sqrt(MaximumRasterPixels / area));
        }
        var raster = CreateProcess("pdftocairo");
        foreach (var argument in new[] { "-png", "-transp", "-r", dpi.ToString("0.###", CultureInfo.InvariantCulture), pdfPath, Path.Combine(directory, "page") }) raster.ArgumentList.Add(argument);
        var (rasterCode, rasterLog) = await RunProcessAsync(raster, timeout.Token);
        var pages = Directory.GetFiles(directory, "page-*.png")
            .Select(path => (Path: path, Number: int.Parse(Path.GetFileNameWithoutExtension(path)[5..], CultureInfo.InvariantCulture)))
            .OrderBy(page => page.Number).ToArray();
        if (rasterCode != 0 || !pages.Select(page => page.Number).SequenceEqual(Enumerable.Range(1, items.Count)))
            throw new InvalidOperationException("The snippet PDF did not rasterize into the expected pages. " + rasterLog);
        return pages.Select(page => page.Path).ToArray();

        ProcessStartInfo CreateProcess(string executable) => new()
        {
            FileName = executable, WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
    }
}
