using System.Windows;
using System.Windows.Media;

namespace TikzEdt.Modern;

internal readonly record struct EditorCoordinateFrame(Matrix Transform)
{
    public static EditorCoordinateFrame Identity => new(Matrix.Identity);
    public Point ToWorld(Point local) => Transform.Transform(local);
    public Point ToLocal(Point world) { var inverse = Transform; inverse.Invert(); return inverse.Transform(world); }
    public Point Snap(Point world, double step, bool polar)
    {
        var local = ToLocal(world);
        static double Round(double value, double unit) => Math.Round(value / unit, MidpointRounding.AwayFromZero) * unit;
        if (!polar) local = new(Round(local.X, step), Round(local.Y, step));
        else
        {
            var radius = Round(Math.Sqrt(local.X * local.X + local.Y * local.Y), step);
            var angle = Round(Math.Atan2(local.Y, local.X), Math.PI / 12);
            local = new(radius * Math.Cos(angle), radius * Math.Sin(angle));
        }
        return ToWorld(local);
    }
    public DrawingBrush Grid(double canvasSize, double coordinateScale, double step, bool polar, double zoom = 1)
    {
        var frame = this;
        Point Canvas(Point point) { var world = frame.ToWorld(point); return new(canvasSize / 2 + world.X * coordinateScale, canvasSize / 2 - world.Y * coordinateScale); }
        var corners = new[] { new Point(-canvasSize / 2 / coordinateScale, -canvasSize / 2 / coordinateScale), new Point(canvasSize / 2 / coordinateScale, -canvasSize / 2 / coordinateScale), new Point(-canvasSize / 2 / coordinateScale, canvasSize / 2 / coordinateScale), new Point(canvasSize / 2 / coordinateScale, canvasSize / 2 / coordinateScale) }.Select(ToLocal).ToArray();
        var minX = corners.Min(point => point.X); var maxX = corners.Max(point => point.X);
        var minY = corners.Min(point => point.Y); var maxY = corners.Max(point => point.Y);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, canvasSize, canvasSize))));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(226, 229, 233)), 0.5 / zoom);
        var axes = new Pen(new SolidColorBrush(Color.FromRgb(175, 182, 190)), 0.8 / zoom);
        void Line(Point first, Point last, Pen stroke) => drawing.Children.Add(new GeometryDrawing(null, stroke, new LineGeometry(Canvas(first), Canvas(last))));
        // Limit guide density only; snapping always retains the requested step.
        var visibleStep = step * Math.Max(1, Math.Ceiling(Math.Max(maxX - minX, maxY - minY) / step / 1600));
        if (!polar)
        {
            for (var x = Math.Ceiling(minX / visibleStep) * visibleStep; x <= maxX; x += visibleStep) Line(new(x, minY), new(x, maxY), pen);
            for (var y = Math.Ceiling(minY / visibleStep) * visibleStep; y <= maxY; y += visibleStep) Line(new(minX, y), new(maxX, y), pen);
            Line(new(0, minY), new(0, maxY), axes); Line(new(minX, 0), new(maxX, 0), axes);
        }
        else
        {
            var origin = Canvas(new()); var basisX = Canvas(new(1, 0)) - origin; var basisY = Canvas(new(0, 1)) - origin;
            var matrix = new Matrix(basisX.X, basisX.Y, basisY.X, basisY.Y, origin.X, origin.Y);
            var radius = corners.Max(point => Math.Sqrt(point.X * point.X + point.Y * point.Y));
            for (var r = visibleStep; r <= radius; r += visibleStep)
                drawing.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(), r, r) { Transform = new MatrixTransform(matrix) }));
            for (var degrees = 0; degrees < 360; degrees += 15) { var angle = degrees * Math.PI / 180; Line(new(), new(radius * Math.Cos(angle), radius * Math.Sin(angle)), pen); }
        }
        drawing.Freeze();
        return new DrawingBrush(drawing) { Viewport = new Rect(0, 0, canvasSize, canvasSize), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
    }
}
