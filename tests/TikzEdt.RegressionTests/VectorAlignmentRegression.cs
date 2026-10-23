using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TikzEdt.Modern;

internal static class VectorAlignmentRegression
{
    public static async Task RunAsync()
    {
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        {
          foreach (var (name, source, expected) in new[]
          {
            ("origin", "\\begin{tikzpicture}\n\\draw[blue,thick] (0,0) rectangle (4,2);\n\\node at (2,1) {TikzEdt Modern: $\\displaystyle E=m\\cdot c^2$};\n\\end{tikzpicture}", new Rect(0,0,4,2)),
            ("negative", "\\begin{tikzpicture}\\draw[blue,thick] (-3,-2) rectangle (1,0);\\end{tikzpicture}", new Rect(-3,-2,4,2)),
            ("scale-and-scope", "\\begin{tikzpicture}[scale=1.7]\\begin{scope}[shift={(.3,-.2)}]\\draw[blue,thick] (0,0) rectangle (4,2);\\end{scope}\\end{tikzpicture}", new Rect(.51,-.34,6.8,3.4)),
            ("point-units", "\\begin{tikzpicture}\\draw[blue,thick] (0pt,0pt) rectangle (72.27pt,36.135pt);\\end{tikzpicture}", new Rect(0,0,2.54,1.27))
          })
          {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var result = await TexVectorRenderer.RenderAsync(source, engine, null, "", timeout.Token, new UserSettings(), false);
            if (result.ExitCode != 0 || result.Image is null) throw new Exception(result.Log);
            var blue = BlueBounds(result.Image.Drawing, Matrix.Identity);
            var bb = result.BoundingBox;
            var actual = new Rect(new Point(bb.Left + blue.Left / result.Image.Width * bb.Width, bb.Bottom - blue.Bottom / result.Image.Height * bb.Height),
                new Point(bb.Left + blue.Right / result.Image.Width * bb.Width, bb.Bottom - blue.Top / result.Image.Height * bb.Height));
            // Cairo quantizes vector coordinates to 1/256 PDF point. Allow a
            // fraction of a canvas pixel, rather than hiding a scale/offset error.
            var error = new[] { Math.Abs(actual.Left - expected.Left), Math.Abs(actual.Top - expected.Top), Math.Abs(actual.Right - expected.Right), Math.Abs(actual.Bottom - expected.Bottom) }.Max();
            if (error > .0003) throw new Exception($"{engine}/{name}: vector border does not coincide with editing coordinates. Expected {expected}; actual {actual}; max error={error} cm.");
            Console.WriteLine($"{engine}/{name}: maximum border/marker mismatch {error * 50:0.0000} canvas pixels at 100%.");
            if (name == "origin") Snapshot(result.Image, bb, expected, engine);
          }
        }
    }
    private static void Snapshot(DrawingImage image, Rect bb, Rect expected, string engine)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                const double scale = 180; const double margin = 8;
                var width = (int)Math.Ceiling(bb.Width * scale + margin * 2); var height = (int)Math.Ceiling(bb.Height * scale + margin * 2);
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                    drawing.DrawImage(image, new Rect(margin, margin, bb.Width * scale, bb.Height * scale));
                    var pen = new Pen(Brushes.Red, .8);
                    foreach (var point in new[] { expected.TopLeft, expected.TopRight, expected.BottomLeft, expected.BottomRight })
                    {
                        var p = new Point(margin + (point.X - bb.Left) * scale, margin + (bb.Bottom - point.Y) * scale);
                        drawing.DrawLine(pen, p + new Vector(-4, -4), p + new Vector(4, 4)); drawing.DrawLine(pen, p + new Vector(-4, 4), p + new Vector(4, -4));
                    }
                }
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory("tests/TikzEdt.RegressionTests/vector-tests"); using var output = File.Create("tests/TikzEdt.RegressionTests/vector-tests/alignment-" + engine + ".png"); encoder.Save(output);
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure is not null) throw failure;
    }
    private static Rect BlueBounds(Drawing drawing, Matrix parent)
    {
        if (drawing is DrawingGroup group)
        {
            var matrix = group.Transform?.Value ?? Matrix.Identity; matrix.Append(parent);
            var result = Rect.Empty; foreach (var child in group.Children) result.Union(BlueBounds(child, matrix)); return result;
        }
        if (drawing is GeometryDrawing { Pen.Brush: SolidColorBrush brush } geometry && brush.Color.B > 200 && brush.Color.R < 30 && brush.Color.G < 30)
        { var bounds = geometry.Geometry.Bounds; bounds.Transform(parent); return bounds; }
        return Rect.Empty;
    }
}
