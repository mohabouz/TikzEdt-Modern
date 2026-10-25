using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;
using System.Windows;
using System.Globalization;

internal static class CalculatedCoordinateRegression
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var cases = new[]
        {
            @"\begin{tikzpicture}\draw (0,0) grid (5,5);\draw (0,0)--({5+0.5},0);\end{tikzpicture}",
            @"\begin{tikzpicture}\draw (0,0) grid (5,5);\draw[smooth,domain=0:5,samples=100] (0,0) plot(\x,{4*(1-exp(-1*\x))});\end{tikzpicture}",
            Fixture,
            @"\begin{tikzpicture}[scale=2]\begin{scope}[shift={(1,2)}]\draw (0,0)--({2+1},{4/2});\end{scope}\end{tikzpicture}",
            @"\begin{tikzpicture}\draw (0,0) plot (\x,{sin(\x)+exp(-\x)});\draw (2,1)--(3,4);\end{tikzpicture}",
            @"\begin{tikzpicture}\draw (0,0)--({\unknown+1},0);\draw (2,1)--(3,4);\end{tikzpicture}",
            @"\begin{tikzpicture}\draw (0,0)--($(a)+(b)$);\draw (2,1)--(3,4);\end{tikzpicture}"
        };
        foreach (var source in cases)
        {
            var diagram = LegacyTikzAdapter.Parse(source);
            Check(diagram.Error is null && diagram.Points.Count >= (source == cases[3] ? 2 : 3), "Calculated coordinate/plot hid unrelated control points: " + diagram.Error);
            foreach (var point in diagram.Points) Check(source.Substring(point.Offset, point.Length) == point.Item.ToString(), "Overlay normalization leaked into source offsets/text.");
        }
        var axis = LegacyTikzAdapter.Parse(cases[0]).Points.Single(p => Math.Abs(p.Position.X - 5.5) < 1e-8);
        var translated = LegacyTikzAdapter.MovePoint(axis, new Point(6, 0)); Check(translated.Contains("5+0.5") && translated.Contains("0.5"), "Movement flattened a formula.");
        var movedSource = cases[0].Remove(axis.Offset, axis.Length).Insert(axis.Offset, translated);
        Check(LegacyTikzAdapter.Parse(movedSource).Points.Any(p => (p.Position - new Point(6, 0)).Length < 1e-8), "Expression marker movement did not round trip.");
        var repeated = cases[0];
        for (var i = 0; i < 60; i++)
        {
            var endpoint = LegacyTikzAdapter.Parse(repeated).Points.Single(p => p.Position.X > 5 && p.Position.Y == 0);
            var replacement = LegacyTikzAdapter.MovePoint(endpoint, endpoint.Position + new Vector(i % 2 == 0 ? .5 : -.5, 0));
            repeated = repeated.Remove(endpoint.Offset, endpoint.Length).Insert(endpoint.Offset, replacement);
        }
        Check(repeated.Length < cases[0].Length + 30 && LegacyTikzAdapter.Parse(repeated).Points.Any(p => p.Position.X == 5.5), "Repeated formula movement accumulated nested expressions or drift.");
        var functionSource = @"\begin{tikzpicture}\draw (0,0)--({max(1,2)},0);\end{tikzpicture}";
        var functionPoint = LegacyTikzAdapter.Parse(functionSource).Points.Single(p => p.Position.X == 2);
        Check(LegacyTikzAdapter.MovePoint(functionPoint, new Point(3, 0)).Contains("max(1,2)"), "Moving a function coordinate edited its arguments instead of translating it.");
        Check(LegacyTikzAdapter.Parse(Fixture).Points.Count == 11, "Full screenshot fixture lost grid/axis/plot-origin markers.");
        Check(LegacyTikzAdapter.Parse(cases[3]).Points.Any(p => (p.Position - new Point(8, 8)).Length < 1e-8), "Calculated coordinate lost scoped transforms.");
        var node = @"\begin{tikzpicture}\node[shift={({1+1},0)}] at (3,4) {f(x,y)};\node at ({5+0.5},0) {Calc};\end{tikzpicture}";
        var nodeDiagram = LegacyTikzAdapter.Parse(node); Check(nodeDiagram.Error is null && nodeDiagram.Points.Count == 2 && nodeDiagram.Points.Any(p => p.Position == new Point(5.5, 0)), "Calculated node coordinates failed.");
        var old = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Check(PgfConstantExpression.TryEvaluate("{5+0.5}", out var n) && n == 5.5, "Culture changed math."); } finally { CultureInfo.CurrentCulture = old; }
        Check(PgfConstantExpression.TryEvaluate("{sin(30)+exp(0)+2^3}", out var math) && Math.Abs(math - 9.5) < 1e-10, "Constant PGF math evaluation failed.");
        foreach (var invalid in new[] { @"\x", "1/0", "sqrt(-1)", "random(1)", "1+", "file(abc)", new string('(', 100) + "0" + new string(')', 100) }) Check(!PgfConstantExpression.TryEvaluate(invalid, out _), "Unsafe/dynamic math was evaluated: " + invalid);
        Console.WriteLine("Calculated-coordinate and plot isolation, exact source offsets, expression-preserving movement, scoped transforms, nodes, culture and bounded math checks passed.");
    }
    internal const string Fixture = """
\usepackage{graphicx}
\begin{tikzpicture}[>=stealth]
\draw[step=0.1, blue!30, thin] (0,0) grid (5,5);
\draw[step=0.5, blue!50, thin] (0,0) grid (5,5);
\draw[step=1, blue!60, thick] (0,0) grid (5,5);
\draw[->, black, very thick] (0,0) -- ({5+0.5},0) node[above] {$x$};
\draw[->, black, very thick] (0,0) -- (0,{5+0.5}) node[right] {$f(x)$};
\draw[smooth, domain=0:5, samples=100, very thick] (0,0) plot(\x,{4*(1-exp(-1*\x))});
\end{tikzpicture}
""";
    public static async Task IntegrationAsync()
    {
        const string source = @"\begin{tikzpicture}\draw (0,0) grid (5,5);\draw (0,0)--({5+0.5},0);\end{tikzpicture}";
        var axis = LegacyTikzAdapter.Parse(source).Points.Single(p => p.Position.X == 5.5);
        var moved = source.Remove(axis.Offset, axis.Length).Insert(axis.Offset, LegacyTikzAdapter.MovePoint(axis, new Point(6, 0)));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        {
            var settings = new UserSettings { UsePrecompiledFormats = false };
            var original = await TexVectorRenderer.RenderAsync(source, engine, null, null, timeout.Token, settings);
            var translated = await TexVectorRenderer.RenderAsync(moved, engine, null, null, timeout.Token, settings);
            var plot = await TexVectorRenderer.RenderAsync(Fixture, engine, null, null, timeout.Token, settings);
            if (original.Image is null || translated.Image is null || plot.Image is null) throw new Exception("Calculated/plot compilation failed: " + engine + "\n" + translated.Log);
            if (Math.Abs(translated.BoundingBox.Width - original.BoundingBox.Width - .5) > .001) throw new Exception("TeX and overlay expression translation disagree: " + engine);
            Console.WriteLine("Calculated coordinate marker/TeX translation + screenshot plot compiled correctly: " + engine);
        }
    }
}
