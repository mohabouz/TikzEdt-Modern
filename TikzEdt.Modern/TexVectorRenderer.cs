using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace TikzEdt.Modern;

internal sealed record TexVectorResult(string PdfPath, string SvgPath, DrawingImage? Image, Rect BoundingBox, string Log, int ExitCode, int UserSourceStartLine);

internal static class TexVectorRenderer
{
    private static readonly PreviewResultCache PdfCache = new(false);
    private static readonly SemaphoreSlim ReaderGate = new(1);
    private static readonly Dictionary<string, DrawingImage> Drawings = new(StringComparer.OrdinalIgnoreCase);
    public static async Task<TexVectorResult> RenderAsync(string source, string engine, string? directory, string? preamble,
        CancellationToken token, UserSettings? settings = null, bool useCache = true)
    {
        settings ??= SettingsStore.Current.Clone();
        var cacheable = useCache && !UserSettings.ParseFlags(settings.CompilerFlags).Any(f => f.Contains("shell-escape", StringComparison.OrdinalIgnoreCase) || f.Contains("enable-write18", StringComparison.OrdinalIgnoreCase));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", engine, settings.CompilerIdentity, directory, preamble, source))));
        var pdf = cacheable ? PdfCache.Find(key) : null;
        if (pdf is null)
        {
            pdf = await TexPreviewRenderer.RenderPdfAsync(source, engine, directory, preamble, token, settings).ConfigureAwait(false);
            if (cacheable) PdfCache.Store(key, pdf, directory ?? Path.GetDirectoryName(pdf.PdfPath)!);
        }
        token.ThrowIfCancellationRequested();
        if (pdf.ExitCode != 0 || !File.Exists(pdf.PdfPath)) return new(pdf.PdfPath, "", null, pdf.BoundingBox, pdf.Log, pdf.ExitCode, pdf.UserSourceStartLine);
        var svg = Path.Combine(Path.GetDirectoryName(pdf.PdfPath)!, "TikzEdt-figure.svg");
        if (!File.Exists(svg)) svg = await SvgClipboard.ExportAsync(pdf.PdfPath, token).ConfigureAwait(false);
        var drawing = await LoadAsync(svg, token).ConfigureAwait(false);
        return new(pdf.PdfPath, svg, drawing, pdf.BoundingBox, pdf.Log, 0, pdf.UserSourceStartLine);
    }
    public static async Task<DrawingImage> LoadAsync(string path, CancellationToken token)
    {
        await ReaderGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Drawings.TryGetValue(path, out var cached)) return cached;
            var completion = new TaskCompletionSource<DrawingImage>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    // Input is produced by pdftocairo, not arbitrary web SVG. Still
                    // prohibit external resources and DTDs before library parsing.
                    using var xml = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                    var document = XDocument.Load(xml);
                    foreach (var attribute in document.Descendants().Attributes().Where(a => a.Name.LocalName == "href"))
                        if (!attribute.Value.StartsWith('#') && !attribute.Value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("External SVG resources are not allowed in figure previews.");
                    foreach (var dimension in new[] { "width", "height" })
                    {
                        var attribute = document.Root?.Attribute(dimension);
                        if (attribute is not null && double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                            attribute.Value += "pt"; // pdftocairo unitless lengths are PDF points.
                    }
                    var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true, CultureInfo = CultureInfo.InvariantCulture,
                        ViewBoxMode = ViewBoxMode.Strict, EnsureViewboxPosition = false, EnsureViewboxSize = true };
                    using var reader = new FileSvgReader(settings);
                    using var normalized = document.CreateReader();
                    var group = reader.Read(normalized) ?? throw new InvalidDataException("SVG conversion produced no vector drawing.");
                    // DrawingImage normally trims to the ink bounds. Preserve the
                    // PDF/SVG page rectangle, otherwise TikZ coordinate overlays shift.
                    double Length(string? text)
                    {
                        if (text is null) return 0;
                        var match = System.Text.RegularExpressions.Regex.Match(text, @"^\s*([0-9.eE+-]+)\s*(pt|px)?\s*$");
                        // Cairo emits PDF points as either "pt" or unitless values.
                        // SharpVectors maps both to 96-DPI WPF units; use the same
                        // factor for our page frame or the lower/right ink is clipped.
                        return match.Success ? double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * (match.Groups[2].Value == "px" ? 1 : 96d / 72) : 0;
                    }
                    var width = Length(document.Root?.Attribute("width")?.Value); var height = Length(document.Root?.Attribute("height")?.Value);
                    if (width <= 0 || height <= 0) throw new InvalidDataException("SVG preview does not declare a valid page size.");
                    var frame = new Rect(0, 0, width, height);
                    var root = new DrawingGroup { ClipGeometry = new RectangleGeometry(frame) };
                    root.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(frame)));
                    root.Children.Add(group);
                    if (!root.CanFreeze) throw new InvalidDataException("SVG drawing cannot be shared with the UI thread.");
                    root.Freeze(); var image = new DrawingImage(root); image.Freeze();
                    token.ThrowIfCancellationRequested(); completion.TrySetResult(image);
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(token); }
                catch (Exception e) { completion.TrySetException(e); }
            }) { IsBackground = true, Name = "TikzEdt SVG vector conversion" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            var result = await completion.Task.ConfigureAwait(false);
            if (Drawings.Count >= 12) Drawings.Remove(Drawings.Keys.First());
            Drawings[path] = result; return result;
        }
        finally { ReaderGate.Release(); }
    }
}
