using System.Globalization;
using System.Windows;

namespace TikzEdt.Modern;

internal static class DrawingCommandBuilder
{
    public static string Build(string tool, IReadOnlyList<Point> points, double step)
    {
        static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
        static string Coordinate(Point point) => $"({Number(point.X)},{Number(point.Y)})";
        var required = tool switch { "bezier" => 4, "smooth" => 3, "grid" => 2, "arc" or "pie" => 3, _ => throw new ArgumentException("Unknown drawing tool.") };
        if (points.Count < required) throw new ArgumentException($"This tool needs {required} points.");
        if (tool == "bezier") return $"\\draw {Coordinate(points[0])} .. controls {Coordinate(points[1])} and {Coordinate(points[2])} .. {Coordinate(points[3])};";
        if (tool == "smooth") return "\\draw plot[smooth,tension=0.7] coordinates { " + string.Join(" ", points.Select(Coordinate)) + " };";
        if (tool == "grid") return $"\\draw[step={Number(step)}] {Coordinate(points[0])} grid {Coordinate(points[1])};";
        var radius = (points[1] - points[0]).Length;
        if (radius < 0.000001 || (points[2] - points[0]).Length < 0.000001) throw new ArgumentException("Choose start/end directions away from the center.");
        var angles = ArcAngles(points[0], points[1], points[2]);
        var arc = $"arc[start angle={Number(angles.Start)},end angle={Number(angles.End)},radius={Number(radius)}]";
        return tool == "pie"
            ? $"\\draw[fill=blue!12] {Coordinate(points[0])} -- {Coordinate(points[1])} {arc} -- cycle;"
            : $"\\draw {Coordinate(points[1])} {arc};";
    }

    public static (double Start, double End) ArcAngles(Point center, Point start, Point end)
    {
        var angle = Math.Atan2(start.Y - center.Y, start.X - center.X) * 180 / Math.PI;
        var target = Math.Atan2(end.Y - center.Y, end.X - center.X) * 180 / Math.PI;
        var sweep = (target - angle + 360) % 360;
        if (sweep < 0.000001) sweep = 360;
        return (angle, angle + sweep);
    }
}
