using System.Diagnostics;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace TikzEdt.Modern;

internal sealed record TexPreviewResult(
    string PdfPath,
    string PngPath,
    Rect BoundingBox,
    double RasterDpi,
    string Log,
    int ExitCode,
    int UserSourceStartLine);

internal sealed record TexRasterResult(string PngPath, double Dpi, string Log, int ExitCode);

/// <summary>
/// Produces the picture used by both WYSIWYG and Preview modes.  The pixels are
/// rendered by TeX, while WPF only supplies the editable overlay.
/// </summary>
internal static partial class TexPreviewRenderer
{
    private const string PreviewFormatVersion = "v3";
    private const string PreviewPreamble = """
\documentclass[tikz,border=0pt]{standalone}
\usepackage{amsmath,amssymb,bm,xcolor}
\usepackage{tikz}
""";

    // TeX formats are the important part of the legacy editor's fast-preview
    // design: load the class and packages once, then compile only the changing
    // tikzpicture.  Keep one shared warm-up task per engine so rapid canvas
    // edits cannot start duplicate format builds.
    private static readonly ConcurrentDictionary<string, Task<string?>> PreviewFormats = new();
    private static readonly PreviewResultCache RecentPreviews = new();

    public static async Task<TexPreviewResult> RenderCachedAsync(
        string tikzSource, string engine, string? documentDirectory, string? customPreamble,
        double requestedRasterDpi, CancellationToken cancellationToken, UserSettings? settings = null)
    {
        settings ??= SettingsStore.Current;
        cancellationToken.ThrowIfCancellationRequested();
        // Explicit builds still use RenderAsync. Shell-escape jobs may have external side effects.
        var cacheable = !UserSettings.ParseFlags(settings.CompilerFlags).Any(flag =>
            flag.Contains("shell-escape", StringComparison.OrdinalIgnoreCase) || flag.Contains("enable-write18", StringComparison.OrdinalIgnoreCase));
        var directory = documentDirectory is not null && Directory.Exists(documentDirectory)
            ? Path.GetFullPath(documentDirectory) : null;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            engine, settings.CompilerIdentity, directory, customPreamble,
            requestedRasterDpi.ToString("R", CultureInfo.InvariantCulture), tikzSource))));
        if (cacheable && RecentPreviews.Find(key) is { } cached) return cached;
        var result = await RenderAsync(tikzSource, engine, directory, customPreamble, requestedRasterDpi, cancellationToken, settings);
        cancellationToken.ThrowIfCancellationRequested();
        if (cacheable) RecentPreviews.Store(key, result, directory ?? Path.GetDirectoryName(result.PdfPath)!);
        return result;
    }

    // This is the bounding-box writer used by the legacy TikzEdt compiler.  The
    // 0.1 TikZ-unit border prevents thick strokes from being clipped.
    private const string BoundingBoxWriter = """

\usetikzlibrary{calc}
\pgftransformreset
\node[inner sep=0pt,outer sep=0pt,minimum size=0pt,line width=0pt,text width=0pt,text height=0pt] at (current bounding box) {};
\foreach \border in {0.1}
  \useasboundingbox (current bounding box.south west)+(-\border,-\border) rectangle (current bounding box.north east)+(\border,\border);
\newwrite\metadatafile
\immediate\openout\metadatafile=\jobname_BB.txt
\path
  let
    \p1=(current bounding box.south west),
    \p2=(current bounding box.north east)
  in
  node[inner sep=0pt,outer sep=0pt,minimum size=0pt,line width=0pt,text width=0pt,text height=0pt,draw=white] at (current bounding box) {
\immediate\write\metadatafile{\p1,\p2}
};
\immediate\closeout\metadatafile
""";

    internal const double PointsPerTikzUnit = 72.27 / 2.54; // TeX pt/cm, not PDF bp/cm.
    private const double MinimumRasterDpi = 96;
    private const double MaximumRasterDpi = 4000;
    private const double MaximumRasterPixels = 20_000_000;

    // luabidi sets pagedir TRT for Arabic documents. PGF's LuaTeX driver
    // does not isolate that direction as the XeTeX bidi driver does: the PDF
    // page and picture boxes disagree, mirroring labels and clipping paths.
    // Give the figure page an LTR coordinate system; explicit language runs
    // such as \textarabic still retain their own RTL shaping.
    private const string LuaPictureDirection = @"\ifdefined\setLTR\ifdefined\pagedir\pagedir TLT\fi\setLTR\fi";

    public static async Task<TexPreviewResult> RenderAsync(
        string tikzSource,
        string engine,
        string? documentDirectory,
        string? customPreamble,
        double requestedRasterDpi,
        CancellationToken cancellationToken,
        UserSettings? settings = null)
        => await RenderCoreAsync(tikzSource, engine, documentDirectory, customPreamble, requestedRasterDpi, cancellationToken, settings, true);

    public static async Task<TexPreviewResult> RenderPdfAsync(
        string tikzSource, string engine, string? documentDirectory, string? customPreamble,
        CancellationToken cancellationToken, UserSettings? settings = null)
        => await RenderCoreAsync(tikzSource, engine, documentDirectory, customPreamble, 0, cancellationToken, settings, false);

    private static async Task<TexPreviewResult> RenderCoreAsync(
        string tikzSource, string engine, string? documentDirectory, string? customPreamble,
        double requestedRasterDpi, CancellationToken cancellationToken, UserSettings? settings, bool rasterize)
    {
        settings ??= SettingsStore.Current;
        var engineName = engine;
        engine = settings.ResolveEngine(engineName);
        var flags = UserSettings.ParseFlags(settings.CompilerFlags);
        var jobDirectory = Path.Combine(
            Path.GetTempPath(),
            "TikzEdt.Modern",
            "Preview",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDirectory);

        var jobName = "tikzedt-preview";
        var texPath = Path.Combine(jobDirectory, jobName + ".tex");
        var pdfPath = Path.Combine(jobDirectory, jobName + ".pdf");
        var bbPath = Path.Combine(jobDirectory, jobName + "_BB.txt");

        var isStandalone = IsStandalone(tikzSource);
        var fragment = isStandalone ? (Source: tikzSource, Preamble: string.Empty) : PrepareFragment(tikzSource);
        var sourceWithBoundingBox = InjectBoundingBoxWriter(fragment.Source);
        var isLuaLatex = engineName.Equals("lualatex", StringComparison.OrdinalIgnoreCase);
        var pictureDirection = isLuaLatex ? LuaPictureDirection + "\n" : string.Empty;
        var requiredPackages = Regex.Matches(tikzSource, @"(?m)^\s*%\s*!TIKZEDT\s+PACKAGE\s+([a-zA-Z0-9_.-]+)")
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (tikzSource.Contains("\\begin{circuitikz}", StringComparison.Ordinal)) requiredPackages.Add("circuitikz");
        if (Regex.IsMatch(tikzSource, @"\\chemfig\b")) requiredPackages.Add("chemfig");
        var packageLines = string.Join("\n", requiredPackages.Select(package => $"\\usepackage{{{package}}}"));
        var effectivePreamble = BuildEffectivePreamble(customPreamble + "\n" + packageLines + "\n" + fragment.Preamble);
        var formatPath = isStandalone || !settings.UsePrecompiledFormats ? null : await GetPreviewFormatAsync(engine, engineName, effectivePreamble, flags).WaitAsync(cancellationToken);
        string compilationSource;
        int userSourceStartLine;
        if (isStandalone)
        {
            // Insert inline to retain original source line numbers for errors.
            compilationSource = BeginDocumentRegex().Replace(sourceWithBoundingBox,
                match => packageLines.Replace("\n", " ", StringComparison.Ordinal) + match.Value + (isLuaLatex ? LuaPictureDirection : ""), 1);
            userSourceStartLine = 1;
        }
        else
        {
            var prefix = formatPath is not null
                ? "\\begin{document}\n"
                : effectivePreamble + "\n\\begin{document}\n";
            prefix += pictureDirection;
            compilationSource = prefix + sourceWithBoundingBox + "\n\\end{document}\n";
            userSourceStartLine = prefix.Count(character => character == '\n') + 1;
        }

        await File.WriteAllTextAsync(texPath, compilationSource, new UTF8Encoding(false), cancellationToken);

        var compile = new ProcessStartInfo
        {
            FileName = engine,
            WorkingDirectory = documentDirectory is not null && Directory.Exists(documentDirectory)
                ? documentDirectory
                : jobDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        compile.ArgumentList.Add("-interaction=nonstopmode");
        compile.ArgumentList.Add("-halt-on-error");
        compile.ArgumentList.Add("-file-line-error");
        compile.ArgumentList.Add("-recorder");
        foreach (var flag in flags) compile.ArgumentList.Add(flag);
        if (formatPath is not null)
            compile.ArgumentList.Add("-fmt=" + formatPath);
        compile.ArgumentList.Add("-output-directory=" + jobDirectory);
        compile.ArgumentList.Add(texPath);

        var (exitCode, compileLog) = await RunProcessAsync(compile, cancellationToken);
        // Some MiKTeX configurations can dump a format but fail while loading
        // it. Retry with the full preamble so preview remains functional.
        if (exitCode != 0 && formatPath is not null && !File.Exists(Path.ChangeExtension(texPath, ".log")))
        {
            var prefix = effectivePreamble + "\n\\begin{document}\n";
            prefix += pictureDirection;
            compilationSource = prefix + sourceWithBoundingBox + "\n\\end{document}\n";
            userSourceStartLine = prefix.Count(character => character == '\n') + 1;
            await File.WriteAllTextAsync(texPath, compilationSource, new UTF8Encoding(false), cancellationToken);
            compile.ArgumentList.Remove("-fmt=" + formatPath);
            (exitCode, compileLog) = await RunProcessAsync(compile, cancellationToken);
        }
        if (exitCode != 0 || !File.Exists(pdfPath))
            return new TexPreviewResult(pdfPath, string.Empty, Rect.Empty, 0, compileLog, exitCode, userSourceStartLine);

        var boundingBox = ReadBoundingBox(bbPath);
        if (!rasterize) return new TexPreviewResult(pdfPath, "", boundingBox, 0, compileLog, 0, userSourceStartLine);
        var rasterResult = await RasterizeAsync(pdfPath, boundingBox, requestedRasterDpi, cancellationToken);
        var combinedLog = compileLog + (string.IsNullOrWhiteSpace(rasterResult.Log) ? string.Empty : "\n" + rasterResult.Log);
        return new TexPreviewResult(
            pdfPath,
            rasterResult.PngPath,
            boundingBox,
            rasterResult.Dpi,
            combinedLog,
            rasterResult.ExitCode,
            userSourceStartLine);
    }

    public static async Task<TexRasterResult> RasterizeAsync(
        string pdfPath,
        Rect boundingBox,
        double requestedDpi,
        CancellationToken cancellationToken)
    {
        var jobDirectory = Path.Combine(
            Path.GetTempPath(),
            "TikzEdt.Modern",
            "Raster",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobDirectory);
        var pngBasePath = Path.Combine(jobDirectory, "tikzedt-preview");
        var pngPath = pngBasePath + ".png";
        var rasterDpi = ResolveRasterDpi(boundingBox, requestedDpi);

        var raster = new ProcessStartInfo
        {
            FileName = "pdftocairo",
            WorkingDirectory = jobDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        raster.ArgumentList.Add("-png");
        raster.ArgumentList.Add("-singlefile");
        raster.ArgumentList.Add("-transp");
        raster.ArgumentList.Add("-r");
        raster.ArgumentList.Add(rasterDpi.ToString("0.###", CultureInfo.InvariantCulture));
        raster.ArgumentList.Add("-f");
        raster.ArgumentList.Add("1");
        raster.ArgumentList.Add("-l");
        raster.ArgumentList.Add("1");
        raster.ArgumentList.Add(pdfPath);
        raster.ArgumentList.Add(pngBasePath);

        var (rasterExitCode, rasterLog) = await RunProcessAsync(raster, cancellationToken);
        if (rasterExitCode != 0 || !File.Exists(pngPath))
            return new TexRasterResult(pngPath, rasterDpi, rasterLog, rasterExitCode);

        return new TexRasterResult(pngPath, rasterDpi, rasterLog, 0);
    }

    private static Task<string?> GetPreviewFormatAsync(string engine, string engineName, string effectivePreamble, IReadOnlyList<string> flags)
    {
        // pdfTeX can safely dump this package preamble. LuaTeX and XeTeX load
        // native font/Lua state which cannot be dumped reliably, so they keep
        // using the fully supported normal compilation path.
        if (engineName != "pdflatex")
            return Task.FromResult<string?>(null);

        var preambleHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(engine + "\n" + string.Join("\n", flags) + "\n" + effectivePreamble)))[..16]
            .ToLowerInvariant();
        var cacheKey = engineName + ":" + preambleHash;
        return PreviewFormats.GetOrAdd(cacheKey,
            _ => BuildPreviewFormatAsync(engine, engineName, effectivePreamble, preambleHash, flags));
    }

    private static async Task<string?> BuildPreviewFormatAsync(
        string engine,
        string engineName,
        string effectivePreamble,
        string preambleHash,
        IReadOnlyList<string> flags)
    {
        try
        {
            var formatDirectory = Path.Combine(
                Path.GetTempPath(),
                "TikzEdt.Modern",
                "Formats",
                PreviewFormatVersion,
                engineName,
                preambleHash);
            Directory.CreateDirectory(formatDirectory);

            var jobName = $"te-{preambleHash}";
            var formatPath = Path.Combine(formatDirectory, jobName + ".fmt");
            if (File.Exists(formatPath))
                return formatPath;

            var headerPath = Path.Combine(formatDirectory, jobName + ".tex");
            await File.WriteAllTextAsync(headerPath, effectivePreamble, new UTF8Encoding(false));

            var generate = new ProcessStartInfo
            {
                FileName = engine,
                WorkingDirectory = formatDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            generate.ArgumentList.Add("-ini");
            generate.ArgumentList.Add("-interaction=nonstopmode");
            generate.ArgumentList.Add("-halt-on-error");
            foreach (var flag in flags) generate.ArgumentList.Add(flag);
            generate.ArgumentList.Add("-jobname=" + jobName);
            generate.ArgumentList.Add($"&{engineName} {Path.GetFileName(headerPath)}\\dump");

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var (exitCode, _) = await RunProcessAsync(generate, timeout.Token);
            return exitCode == 0 && File.Exists(formatPath) ? formatPath : null;
        }
        catch (Exception)
        {
            // A custom TeX installation may forbid format creation.  Rendering
            // still works through the normal full-preamble compilation path.
            return null;
        }
    }

    private static string BuildEffectivePreamble(string? customPreamble)
    {
        if (string.IsNullOrWhiteSpace(customPreamble))
            return PreviewPreamble;

        var custom = customPreamble.TrimStart('\uFEFF');
        var documentStart = BeginDocumentRegex().Match(custom);
        if (documentStart.Success)
            custom = custom[..documentStart.Index];
        custom = EndDocumentRegex().Replace(custom, string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(custom))
            return PreviewPreamble;

        var commandsOnly = TeXCommentRegex().Replace(custom, string.Empty);
        if (!DocumentClassRegex().IsMatch(commandsOnly))
            return PreviewPreamble + "\n" + custom;

        var builder = new StringBuilder(custom.TrimEnd());
        if (!TikzPackageRegex().IsMatch(commandsOnly) && !TikzDocumentClassOptionRegex().IsMatch(commandsOnly))
            builder.AppendLine().Append(@"\usepackage{tikz}");

        var usesStandaloneClass = StandaloneClassRegex().IsMatch(commandsOnly);
        var hasPreviewEnvironment = PreviewEnvironmentRegex().IsMatch(commandsOnly);
        if (!usesStandaloneClass && !hasPreviewEnvironment)
        {
            if (!PreviewPackageRegex().IsMatch(commandsOnly))
                builder.AppendLine().Append(@"\usepackage[active,tightpage]{preview}");
            builder.AppendLine().Append(@"\PreviewEnvironment{tikzpicture}");
        }

        return builder.ToString();
    }

    private static double ResolveRasterDpi(Rect boundingBox, double requestedDpi)
    {
        var dpi = Math.Clamp(requestedDpi, MinimumRasterDpi, MaximumRasterDpi);
        if (boundingBox.IsEmpty || boundingBox.Width <= 0 || boundingBox.Height <= 0)
            return dpi;

        var areaInSquareInches = boundingBox.Width / 2.54 * boundingBox.Height / 2.54;
        var memorySafeDpi = Math.Sqrt(MaximumRasterPixels / areaInSquareInches);
        return Math.Max(MinimumRasterDpi, Math.Min(dpi, memorySafeDpi));
    }

    private static async Task<(int ExitCode, string Log)> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the check and Kill.
            }
        });

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await stdout + await stderr);
    }

    private static string InjectBoundingBoxWriter(string source)
    {
        var match = EndTikzPictureRegex().Match(source);
        if (!match.Success)
            return source;
        return source.Insert(match.Index, BoundingBoxWriter);
    }

    private static (string Source, string Preamble) PrepareFragment(string source)
    {
        var picture = BeginPictureRegex().Match(source);
        if (!picture.Success) return (source, string.Empty);
        var header = source[..picture.Index];
        // Preserve offsets/newlines, ignore declarations inside comments, and leave
        // all text inside the picture (including labels and their spaces) untouched.
        var searchable = TeXCommentRegex().Replace(header, match => new string(' ', match.Length));
        var characters = header.ToCharArray();
        var declarations = new List<string>();
        foreach (Match declaration in LibraryDeclarationRegex().Matches(searchable))
        {
            declarations.Add(header.Substring(declaration.Index, declaration.Length));
            for (var index = declaration.Index; index < declaration.Index + declaration.Length; index++)
                if (characters[index] is not ('\r' or '\n')) characters[index] = ' ';
        }
        for (var index = 0; index < characters.Length; index++)
            if (characters[index] is not ('\r' or '\n' or '\t') &&
                (char.IsWhiteSpace(characters[index]) || characters[index] is '\uFEFF' or '\u200B'))
                characters[index] = ' ';
        return (new string(characters) + source[picture.Index..], string.Join("\n", declarations));
    }

    [GeneratedRegex(@"\\begin\s*\{\s*(?:tikzpicture|circuitikz)\s*\}", RegexOptions.IgnoreCase)]
    private static partial Regex BeginPictureRegex();

    [GeneratedRegex(@"(?<!\\)\\usetikzlibrary\s*\{[^{}]*\}")]
    private static partial Regex LibraryDeclarationRegex();

    private static bool IsStandalone(string source) =>
        source.Contains("\\documentclass", StringComparison.OrdinalIgnoreCase) ||
        StandaloneDirectiveRegex().IsMatch(source);

    private static Rect ReadBoundingBox(string path)
    {
        if (!File.Exists(path))
            return Rect.Empty;

        var values = Regex.Split(File.ReadLines(path).FirstOrDefault() ?? string.Empty, @"[,\s]+")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ? value[..^2] : value)
            .Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed / PointsPerTikzUnit
                : double.NaN)
            .ToArray();

        if (values.Length != 4 || values.Any(double.IsNaN))
            return Rect.Empty;
        var result = new Rect(new Point(values[0], values[1]), new Point(values[2], values[3]));
        return result.Width is > 0 and < 500 && result.Height is > 0 and < 500 ? result : Rect.Empty;
    }

    [GeneratedRegex(@"\\end\s*\{\s*(?:tikzpicture|circuitikz)\s*\}", RegexOptions.IgnoreCase)]
    private static partial Regex EndTikzPictureRegex();

    [GeneratedRegex(@"(?im)^\s*%\s*!TIKZEDT\s+STANDALONE\s*$")]
    private static partial Regex StandaloneDirectiveRegex();

    [GeneratedRegex(@"\\begin\s*\{\s*document\s*\}", RegexOptions.IgnoreCase)]
    private static partial Regex BeginDocumentRegex();

    [GeneratedRegex(@"\\end\s*\{\s*document\s*\}", RegexOptions.IgnoreCase)]
    private static partial Regex EndDocumentRegex();

    [GeneratedRegex(@"(?m)(?<!\\)%.*$")]
    private static partial Regex TeXCommentRegex();

    [GeneratedRegex(@"\\documentclass(?:\s*\[[^\]]*\])?\s*\{[^}]+\}", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentClassRegex();

    [GeneratedRegex(@"\\documentclass\s*\[[^\]]*\btikz\b[^\]]*\]\s*\{\s*standalone\s*\}", RegexOptions.IgnoreCase)]
    private static partial Regex TikzDocumentClassOptionRegex();

    [GeneratedRegex(@"\\documentclass(?:\s*\[[^\]]*\])?\s*\{\s*standalone\s*\}", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneClassRegex();

    [GeneratedRegex(@"\\usepackage(?:\s*\[[^\]]*\])?\s*\{[^}]*\btikz\b[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex TikzPackageRegex();

    [GeneratedRegex(@"\\usepackage(?:\s*\[[^\]]*\])?\s*\{[^}]*\bpreview\b[^}]*\}", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewPackageRegex();

    [GeneratedRegex(@"\\PreviewEnvironment\s*\{\s*tikzpicture\s*\}", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewEnvironmentRegex();
}
