using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace TikzEdt.Modern;

internal enum EditorDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

internal sealed record EditorDiagnostic(
    int Offset,
    int Length,
    int Line,
    int Column,
    string Message,
    string Source,
    EditorDiagnosticSeverity Severity = EditorDiagnosticSeverity.Error)
{
    public string Location => Line > 0 ? $"Ln {Line}, Col {Math.Max(1, Column)}" : "Document";
    public string SeverityGlyph => Severity switch
    {
        EditorDiagnosticSeverity.Error => "●",
        EditorDiagnosticSeverity.Warning => "▲",
        _ => "●"
    };
}

/// <summary>
/// Draws diagnostic squiggles with AvalonEdit's public rendering API. The
/// segment collection follows document edits, so markers do not jump to stale
/// offsets while the user is typing.
/// </summary>
internal sealed class DiagnosticMarkerRenderer : IBackgroundRenderer
{
    private readonly TextDocument _document;
    private readonly TextSegmentCollection<DiagnosticSegment> _segments;

    public DiagnosticMarkerRenderer(TextDocument document)
    {
        _document = document;
        _segments = new TextSegmentCollection<DiagnosticSegment>(document);
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetDiagnostics(IEnumerable<EditorDiagnostic> diagnostics)
    {
        _segments.Clear();
        foreach (var diagnostic in diagnostics)
        {
            if (_document.TextLength == 0)
                continue;
            var offset = Math.Clamp(diagnostic.Offset, 0, _document.TextLength - 1);
            var length = Math.Clamp(Math.Max(1, diagnostic.Length), 1, _document.TextLength - offset);
            _segments.Add(new DiagnosticSegment
            {
                StartOffset = offset,
                Length = length,
                Diagnostic = diagnostic
            });
        }
    }

    public EditorDiagnostic? GetDiagnosticAt(int offset) =>
        _segments.FindSegmentsContaining(offset).Select(segment => segment.Diagnostic).FirstOrDefault();

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || textView.VisualLines.Count == 0)
            return;

        var first = textView.VisualLines[0].FirstDocumentLine.Offset;
        var lastLine = textView.VisualLines[^1].LastDocumentLine;
        var visibleLength = lastLine.EndOffset - first;

        foreach (var segment in _segments.FindOverlappingSegments(first, visibleLength))
        {
            var color = segment.Diagnostic.Severity == EditorDiagnosticSeverity.Warning
                ? Color.FromRgb(255, 184, 77)
                : Color.FromRgb(255, 82, 82);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            var pen = new Pen(brush, 1.25);
            pen.Freeze();

            foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                var start = new Point(rectangle.Left, rectangle.Bottom - 1);
                var endX = rectangle.Right;
                if (endX - start.X < 3)
                    endX = start.X + 3;

                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(start, false, false);
                    var points = new List<Point>();
                    var up = true;
                    for (var x = start.X + 2; x <= endX; x += 2)
                    {
                        points.Add(new Point(x, start.Y + (up ? -1.5 : 0)));
                        up = !up;
                    }
                    if (points.Count > 0)
                        context.PolyLineTo(points, true, false);
                }
                geometry.Freeze();
                drawingContext.DrawGeometry(null, pen, geometry);
            }
        }
    }

    private sealed class DiagnosticSegment : TextSegment
    {
        public required EditorDiagnostic Diagnostic { get; init; }
    }
}
