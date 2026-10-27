using System.Windows;
using TikzEdt.Modern;

internal static class CoordinateUnitsRegression
{
    public static void Run()
    {
        void Points(string options, string body, params Point[] expected)
        {
            var source = @"\begin{tikzpicture}[" + options + "]" + body + @"\end{tikzpicture}";
            var diagram = LegacyTikzAdapter.Parse(source);
            if (diagram.Error is not null || diagram.Points.Count != expected.Length)
                throw new Exception("Coordinate units parse failed: " + source + " / " + diagram.Error + " / " + diagram.Points.Count);
            for (var i = 0; i < expected.Length; i++)
            {
                var point = LegacyTikzAdapter.Parse(source).Points[i];
                if ((point.Position - expected[i]).Length > 1e-7)
                    throw new Exception($"{source}: marker {i} expected {expected[i]}, got {point.Position}");
                var target = point.Position + new Vector(.4, -.3);
                var replacement = LegacyTikzAdapter.MovePoint(point, target);
                var moved = source.Remove(point.Offset, point.Length).Insert(point.Offset, replacement);
                var roundtrip = LegacyTikzAdapter.Parse(moved).Points[i];
                if ((roundtrip.Position - target).Length > 2e-6)
                    throw new Exception("Unit-aware dragging did not round trip: " + moved);
            }
        }
        Points("x=2cm,y=5mm", @"\draw (1,2)--(2,4);", new(2,1), new(4,2));
        Points("x=2cm,y=5mm", @"\draw (1cm,2cm)--(1,2cm)--(1cm,2);", new(1,2), new(2,2), new(1,1));
        Points("x=2cm,y=5mm,scale=2", @"\draw (0,0)--++(1,2);", new(0,0), new(4,2));
        Points("x=2cm,y=5mm", @"\draw (90:2)--(90:2cm);", new(0,1), new(0,2));
        Points("x=2cm,y=5mm", @"\begin{scope}[x=3cm,shift={(1,2)}]\draw (1,2)--(1cm,2cm);\end{scope}\draw (1,2);", new(6,2), new(4,3), new(2,1));
        Points("x=2cm,y=5mm,rotate=90", @"\draw (1,2);", new Point(-1,2));
        Points("x={(1cm,.5cm)},y={(-.5cm,1cm)}", @"\draw (2,1)--(2cm,1);", new(1.5,2), new(1.5,1));
        Points("x=1cm,x=2cm,y=-1cm", @"\node at (1,2) {Node};", new Point(2,-2));
        var diagram = LegacyTikzAdapter.Parse(@"\begin{tikzpicture}[x=2cm,y=5mm]\draw (1,2);\end{tikzpicture}");
        var frame = diagram.RootFrame;
        if ((frame.ToLocal(new(2,1)) - new Point(1,2)).Length > 1e-8 ||
            (frame.Snap(new(2.1,1.1), .5, false) - new Point(2,1)).Length > 1e-8)
            throw new Exception("Coordinate-unit grid/mouse inverse failed.");
        Console.WriteLine("x/y units: dimensions, mixed coordinates, nested overrides/shifts, scale/rotation, vectors, relative/polar, nodes, drag round trips and grid snapping passed.");
    }
}
