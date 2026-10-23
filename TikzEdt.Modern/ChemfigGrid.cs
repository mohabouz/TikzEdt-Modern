using System.Windows;
using System.Windows.Media;

namespace TikzEdt.Modern;

internal static class ChemfigGrid
{
    // One model unit = one Chemfig bond-length factor (default atom sep = 3em).
    public const double BondLength = 1;
    public static readonly double RowHeight = Math.Sqrt(3) / 2;
    private static Point Rotate(Point point, double degrees)
    {
        var radians = degrees * Math.PI / 180; var c = Math.Cos(radians); var s = Math.Sin(radians);
        return new Point(c * point.X - s * point.Y, s * point.X + c * point.Y);
    }
    public static Point Snap(Point point, bool cartesian, double orientation = 30)
    {
        if (cartesian) return new(Math.Round(point.X, MidpointRounding.AwayFromZero), Math.Round(point.Y, MidpointRounding.AwayFromZero));
        point = Rotate(point, -orientation);
        var row = (int)Math.Round(point.Y / RowHeight);
        var best = new Point(); var distance = double.MaxValue;
        // Search adjacent rows: independent axial rounding is not nearest-point rounding.
        for (var y = row - 1; y <= row + 1; y++)
        {
            var candidate = new Point(Math.Round(point.X - y / 2d, MidpointRounding.AwayFromZero) + y / 2d, y * RowHeight);
            var d = (point - candidate).LengthSquared;
            if (d < distance) { best = candidate; distance = d; }
        }
        return Rotate(best, orientation);
    }
    public static Point BondEnd(Point origin, Point pointer, bool cartesian, bool snap, double orientation = 30)
    {
        var delta = pointer - origin;
        var increment = cartesian ? Math.PI / 2 : Math.PI / 3;
        var angle = Math.Atan2(delta.Y, delta.X);
        var offset = cartesian ? 0 : orientation * Math.PI / 180;
        if (snap) angle = Math.Round((angle - offset) / increment, MidpointRounding.AwayFromZero) * increment + offset;
        return origin + new Vector(Math.Cos(angle), Math.Sin(angle)) * BondLength;
    }
    public static DrawingBrush Brush(Size size, Point origin, double scale, bool cartesian, Color color, Color axisColor, double orientation = 30)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            var pen = new Pen(new SolidColorBrush(color), .5);
            // Full rectangular Cartesian grid, or a unit triangular/hexagonal lattice.
            var extent = Math.Sqrt(size.Width * size.Width + size.Height * size.Height) / scale + 2;
            void Family(double angle, double spacing)
            {
                var normal = new Vector(-Math.Sin(angle), Math.Cos(angle)); var tangent = new Vector(Math.Cos(angle), Math.Sin(angle));
                for (var i = -(int)Math.Ceiling(extent / spacing); i <= (int)Math.Ceiling(extent / spacing); i++)
                {
                    var a = normal * (i * spacing) - tangent * extent; var b = normal * (i * spacing) + tangent * extent;
                    dc.DrawLine(pen, new Point(origin.X + a.X * scale, origin.Y - a.Y * scale), new Point(origin.X + b.X * scale, origin.Y - b.Y * scale));
                }
            }
            if (cartesian) { Family(0, 1); Family(Math.PI / 2, 1); }
            else { var offset = orientation * Math.PI / 180; Family(offset, RowHeight); Family(offset + Math.PI / 3, RowHeight); Family(offset - Math.PI / 3, RowHeight); }
            var axis = new Pen(new SolidColorBrush(axisColor), .8);
            dc.DrawLine(axis, new Point(0, origin.Y), new Point(size.Width, origin.Y));
            dc.DrawLine(axis, new Point(origin.X, 0), new Point(origin.X, size.Height));
        }
        drawing.Freeze();
        var brush = new DrawingBrush(drawing) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(size), ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(size), Stretch = Stretch.None };
        brush.Freeze(); return brush;
    }
}
