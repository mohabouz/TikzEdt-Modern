using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DrawingPath = System.Windows.Shapes.Path;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private readonly List<Point> _advancedPoints = [];
    private DrawingPath? _advancedPreview;
    private bool IsSelectionTool => SelectTool.IsChecked == true || EditTool.IsChecked == true;
    private string? AdvancedTool => BezierTool.IsChecked == true ? "bezier" : SmoothTool.IsChecked == true ? "smooth" :
        GridTool.IsChecked == true ? "grid" : ArcTool.IsChecked == true ? "arc" : PieTool.IsChecked == true ? "pie" : null;

    private void DrawingTool_Changed(object sender, RoutedEventArgs e)
    {
        if (OverlayCanvas is null) return;
        CancelAdvancedDrawing();
        ModeStatus.Text = AdvancedTool switch
        {
            "bezier" => "Bézier: click start, control 1, control 2, end · Esc cancels",
            "smooth" => "Smooth curve: click through points · Enter/double-click finishes · Esc cancels",
            "grid" => "Grid: click two opposite corners · uses the grid step below",
            "arc" or "pie" => "Arc/pie: click center, start, end direction · counterclockwise · Esc cancels",
            _ => EditTool.IsChecked == true ? "Edit: drag X markers to edit coordinates · Ctrl+click for multiple points · Delete removes selection" : "Select a tool or drag a selection box"
        };
    }

    private bool TryAdvancedDrawingClick(MouseButtonEventArgs e)
    {
        if (AdvancedTool is null) return false;
        PlaceAdvancedPoint(e.GetPosition(OverlayCanvas), e.ClickCount > 1);
        e.Handled = true;
        return true;
    }

    private void PlaceAdvancedPoint(Point position, bool doubleClick)
    {
        if (AdvancedTool is not { } tool) return;
        var point = SnapPoint(ClampToCanvas(position));
        if (_advancedPoints.Count == 0 || (_advancedPoints[^1] - point).Length > 0.01) _advancedPoints.Add(point);
        OverlayCanvas.Focusable = true;
        OverlayCanvas.Focus();
        var complete = tool switch { "bezier" => _advancedPoints.Count == 4, "smooth" => doubleClick && _advancedPoints.Count >= 3, "grid" => _advancedPoints.Count == 2, _ => _advancedPoints.Count == 3 };
        if (complete) FinishAdvancedDrawing(); else UpdateAdvancedDrawingPreview(point);
    }

    private bool FinishAdvancedDrawing()
    {
        if (AdvancedTool is not { } tool || _advancedPoints.Count == 0) return false;
        try
        {
            var points = _advancedPoints.Select(CanvasToCode).ToArray();
            var code = DrawingCommandBuilder.Build(tool, points, CurrentGridStep);
            CancelAdvancedDrawing();
            InsertTikzCommand("    " + code + "\n");
            SelectTool.IsChecked = true;
            RenderWysiwyg();
        }
        catch (ArgumentException exception) { ModeStatus.Text = exception.Message; }
        return true;
    }

    private void CancelAdvancedDrawing()
    {
        _advancedPoints.Clear();
        if (_advancedPreview is not null) OverlayCanvas.Children.Remove(_advancedPreview);
        _advancedPreview = null;
    }

    private void UpdateAdvancedDrawingPreview(Point pointer)
    {
        if (_advancedPoints.Count == 0 || AdvancedTool is not { } tool) return;
        if (_advancedPreview is null)
        {
            _advancedPreview = new DrawingPath { Stroke = Brushes.DarkOrange, StrokeThickness = 1 / GraphicalScale.ScaleX,
                StrokeDashArray = [4, 3], IsHitTestVisible = false };
            OverlayCanvas.Children.Add(_advancedPreview);
        }
        var points = _advancedPoints.Append(pointer).ToList();
        var geometry = new PathGeometry();
        if (tool == "bezier")
        {
            while (points.Count < 4) points.Add(pointer);
            var curve = new PathFigure { StartPoint = points[0] };
            curve.Segments.Add(new BezierSegment(points[1], points[2], points[3], true));
            geometry.Figures.Add(curve);
            var controls = new PathFigure { StartPoint = points[0] };
            controls.Segments.Add(new PolyLineSegment(points.Skip(1).Take(3), true));
            geometry.Figures.Add(controls);
        }
        else if (tool == "smooth")
        {
            var curve = new PathFigure { StartPoint = points[0] };
            for (var index = 0; index < points.Count - 1; index++)
            {
                var before = points[Math.Max(0, index - 1)];
                var after = points[Math.Min(points.Count - 1, index + 2)];
                curve.Segments.Add(new BezierSegment(points[index] + (points[index + 1] - before) / 6,
                    points[index + 1] - (after - points[index]) / 6, points[index + 1], true));
            }
            geometry.Figures.Add(curve);
        }
        else if (tool == "grid")
        {
            var start = CanvasToCode(points[0]); var end = CanvasToCode(pointer);
            var step = CurrentGridStep;
            var left = Math.Min(start.X, end.X); var right = Math.Max(start.X, end.X);
            var top = Math.Min(start.Y, end.Y); var bottom = Math.Max(start.Y, end.Y);
            void Line(Point a, Point b) { var figure = new PathFigure { StartPoint = ToFrameCanvas(a, _coordinateFrame) }; figure.Segments.Add(new LineSegment(ToFrameCanvas(b, _coordinateFrame), true)); geometry.Figures.Add(figure); }
            var lineCount = 0;
            for (var x = Math.Ceiling(left / step) * step; x <= right && lineCount++ < 400; x += step) Line(new(x, top), new(x, bottom));
            for (var y = Math.Ceiling(top / step) * step; y <= bottom && lineCount++ < 800; y += step) Line(new(left, y), new(right, y));
        }
        else if (points.Count >= 3)
        {
            var center = points[0]; var start = points[1];
            var radius = (start - center).Length;
            var angles = DrawingCommandBuilder.ArcAngles(new(center.X, -center.Y), new(start.X, -start.Y), new(pointer.X, -pointer.Y));
            var endAngle = angles.End * Math.PI / 180;
            var end = new Point(center.X + radius * Math.Cos(endAngle), center.Y - radius * Math.Sin(endAngle));
            var figure = new PathFigure { StartPoint = tool == "pie" ? center : start, IsClosed = tool == "pie" };
            if (tool == "pie") figure.Segments.Add(new LineSegment(start, true));
            if (radius > 0) figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, angles.End - angles.Start > 180, SweepDirection.Counterclockwise, true));
            geometry.Figures.Add(figure);
        }
        else
        {
            var line = new PathFigure { StartPoint = points[0] };
            line.Segments.Add(new LineSegment(pointer, true));
            geometry.Figures.Add(line);
        }
        _advancedPreview.Data = geometry;
    }
}
