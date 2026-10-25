using System.IO;
using System.Globalization;
using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;
using TikzEdt.Modern.Core.Toolchain;

internal static class SyncTexRegression
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var map = MainWindow.CreateSyncLineMap("\\begin{tikzpicture}\n\\node {A};\n\\end{tikzpicture}\n%tail", 12);
        Check(map.Forward(2) == 13 && map.Reverse(13) == 2 && map.Reverse(1) is null, "Wrapper prefix mapping failed.");
        Check(map.Forward(4) == 15 + TexPreviewRenderer.BoundingBoxWriterLineCount && map.Reverse(map.Forward(4)) == 4 && map.Reverse(16) is null, "Injected metadata mapping failed.");
        Check(map.Reverse(map.Forward(3)) == 3, "End-environment line failed to round trip.");
        var boxes = SyncTexNavigation.ParseBoxes("SyncTeX result begin\nOutput:C:\\a.pdf\nPage:1\nx:20.5\ny:30\nh:19\nv:31\nW:10\nH:4\nOutput:C:\\a.pdf\nPage:2\nx:5\ny:8\nSyncTeX result end");
        Check(boxes.Count == 2 && boxes[0].X == 19 && boxes[0].Height == 4, "Forward records/coordinates parse failed.");
        Check(SyncTexNavigation.ParseSources("Input:C:\\a.tex\nLine:42\nColumn:-1").Single() == new SyncTexSource("C:\\a.tex", 42, 1), "Windows path/unknown-column handling failed.");
        var old = CultureInfo.CurrentCulture; try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); Check(SyncTexNavigation.PointArgument(1, 1.25, 2.5, "C:\\a.pdf") == "1:1.25:2.5:C:\\a.pdf", "Locale corrupted coordinates."); } finally { CultureInfo.CurrentCulture = old; }
        Console.WriteLine("B4: wrapper/metadata line mapping, multiple SyncTeX records, Windows paths and invariant PDF coordinates passed.");
    }
    public static async Task IntegrationAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(100)); var settings = new UserSettings { UsePrecompiledFormats = false, CompilerFlags = "-synctex=1" };
        const string source = "\\usetikzlibrary{positioning}\n\\begin{tikzpicture}\n\\node at (0,0) {Alpha};\n\\node at (3,2) {Beta};\n\\end{tikzpicture}";
        var executable = ToolchainDoctor.Resolve("synctex")!;
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        {
            var result = await TexVectorRenderer.RenderAsync(source, engine, null, null, timeout.Token, settings, false); Check(result.Image is not null, "Fixture failed: " + engine + result.Log);
            var map = MainWindow.CreateSyncLineMap(source, result.UserSourceStartLine); var input = Path.ChangeExtension(result.PdfPath, ".tex");
            var output = await SyncTexNavigation.QueryAsync(executable, ["view", "-i", map.Forward(3) + ":1:" + input, "-o", result.PdfPath], timeout.Token);
            var box = SyncTexNavigation.ParseBoxes(output).FirstOrDefault(b => b.Page == 1); Check(box is not null, "No forward mapping: " + engine + "\n" + output);
            var reverse = await SyncTexNavigation.QueryAsync(executable, ["edit", "-o", SyncTexNavigation.PointArgument(1, box!.X + box.Width / 2, box.Y - box.Height / 2, result.PdfPath)], timeout.Token);
            var location = SyncTexNavigation.ParseSources(reverse).FirstOrDefault(); Check(location is not null && map.Reverse(location.Line) is >= 2 and <= 5, "Reverse mapping did not return user source: " + engine + "\n" + reverse);
            Console.WriteLine("B4 real forward/reverse SyncTeX passed: " + engine + " (source line " + map.Reverse(location!.Line) + ")");
        }
    }
}
