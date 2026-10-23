using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TikzEdt.Modern;

internal static class VectorPreviewRegression
{
    public static async Task RunAsync(bool integration)
    {
        var folder = Path.GetFullPath("tests/TikzEdt.RegressionTests/vector-tests"); Directory.CreateDirectory(folder);
        var sample = Path.Combine(folder, "sample.svg");
        File.WriteAllText(sample, """
<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="120pt" height="80pt" viewBox="0 0 120 80">
<defs><path id="glyph" d="M 0,0 L 4,0 L 2,-8 Z"/><clipPath id="clip"><rect x="0" y="0" width="120" height="80"/></clipPath></defs>
<g clip-path="url(#clip)" transform="translate(20 20)"><path d="M 0,0 L 50,0 L 25,43 Z" stroke="#dfe1e5" fill="none" stroke-width="1"/><use xlink:href="#glyph" x="25" y="10" fill="#dfe1e5"/></g></svg>
""");
        var image = await TexVectorRenderer.LoadAsync(sample, CancellationToken.None);
        Verify(image);
        if (Math.Abs(image.Width - 160) > 1e-7 || Math.Abs(image.Height - 80 * 96d / 72) > 1e-7) throw new Exception("SVG page bounds were trimmed and will misalign editing overlays.");
        Console.WriteLine($"Vector sample: {image.Width:0.###} × {image.Height:0.###} DIP.");
        var unitless = Path.Combine(folder, "unitless.svg"); File.WriteAllText(unitless, File.ReadAllText(sample).Replace("120pt", "120").Replace("80pt", "80"));
        var unitlessImage = await TexVectorRenderer.LoadAsync(unitless, CancellationToken.None);
        if (Math.Abs(unitlessImage.Width - image.Width) > 1e-7 || Math.Abs(unitlessImage.Height - image.Height) > 1e-7) throw new Exception("Cairo unitless point sizes clipped the WPF page.");
        var cached = await TexVectorRenderer.LoadAsync(sample, CancellationToken.None);
        if (!ReferenceEquals(image, cached)) throw new Exception("SVG drawing was regenerated instead of reused.");
        var bad = Path.Combine(folder, "external.svg"); File.WriteAllText(bad, "<svg xmlns='http://www.w3.org/2000/svg'><image href='https://example.com/a.png'/></svg>");
        try { await TexVectorRenderer.LoadAsync(bad, CancellationToken.None); throw new Exception("External SVG reference was accepted."); } catch (InvalidDataException) { }
        if (integration)
        {
            foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
            {
                const string source = "\\begin{tikzpicture}\\draw[blue,thick] (0,0) rectangle (5,2);\\node at (2,1) {\\chemfig{CH_3-CH=CH-CH_2-CH_3}};\\node at (2,.3) {$E=mc^2$};\\end{tikzpicture}";
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var result = await TexVectorRenderer.RenderAsync(source, engine, null, "", timeout.Token, new UserSettings());
                if (result.ExitCode != 0 || result.Image is null) throw new Exception(engine + ": " + result.Log);
                Verify(result.Image);
                File.Copy(result.SvgPath, Path.Combine(folder, engine + ".svg"), true);
                var again = await TexVectorRenderer.RenderAsync(source, engine, null, "", timeout.Token, new UserSettings());
                if (again.PdfPath != result.PdfPath || !ReferenceEquals(result.Image, again.Image)) throw new Exception("Vector preview cache missed an unchanged figure.");
                Console.WriteLine(engine + ": chemistry, math, paths, glyphs, vector-only drawing and dependency-aware reuse passed.");
                image = result.Image;
            }
        }
        if (!integration && File.Exists(Path.Combine(folder, "xelatex.svg"))) image = await TexVectorRenderer.LoadAsync(Path.Combine(folder, "xelatex.svg"), CancellationToken.None);
        Exception? failure = null;
        var thread = new Thread(() => { try { Ui(image); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure is not null) throw failure;
    }
    private static void Verify(DrawingImage image)
    {
        if (!image.IsFrozen || image.Width <= 0 || image.Height <= 0) throw new Exception("Vector drawing is empty or not thread-safe.");
        var count = 0;
        void Visit(Drawing drawing)
        {
            if (drawing is ImageDrawing) throw new Exception("TeX vector preview contains an image layer instead of vector geometry.");
            if (drawing is GeometryDrawing) count++;
            if (drawing is DrawingGroup group) foreach (var child in group.Children) Visit(child);
        }
        Visit(image.Drawing); if (count < 2) throw new Exception("SVG paths/glyph references were not rendered.");
    }
    private static void Ui(DrawingImage image)
    {
        var app = new App(); app.InitializeComponent(); var main = new MainWindow();
        var width = (int)Math.Ceiling(image.Width * 3); var height = (int)Math.Ceiling(image.Height * 3);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height)); drawing.DrawImage(image, new Rect(0, 0, width, height)); }
        var snapshot = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); snapshot.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(snapshot));
        using (var output = File.Create(Path.GetFullPath("tests/TikzEdt.RegressionTests/vector-tests/vector-3x-qa.png"))) encoder.Save(output);
        typeof(MainWindow).GetField("_lastPreviewBoundingBox", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(main, new Rect(0, 0, 5, 3));
        typeof(MainWindow).GetMethod("DisplayVectorPreview", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, [image, new Rect(0, 0, 5, 3)]);
        var generation = typeof(MainWindow).GetField("_compileGeneration", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var initial = generation.GetValue(main);
        var zoom = (Slider)main.FindName("ZoomSlider");
        foreach (var value in new[] { 100d, 800, 3200 }) zoom.Value = value;
        var timer = (DispatcherTimer)typeof(MainWindow).GetField("_zoomRasterTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        if (timer.IsEnabled || !Equals(initial, generation.GetValue(main)) || !ReferenceEquals(((Image)main.FindName("CompiledPreviewImage")).Source, image))
            throw new Exception("Main canvas zoom started regeneration or replaced its vector source.");
        var chemistry = new ChemfigEditorWindow(ChemfigMolecule.Parse("C-C"), "pdflatex", "", null);
        var chemistryImage = (Image)typeof(ChemfigEditorWindow).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chemistry)!;
        chemistryImage.Source = image;
        var chemistryGeneration = typeof(ChemfigEditorWindow).GetField("_generation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var before = chemistryGeneration.GetValue(chemistry);
        foreach (var name in new[] { "_previewView", "_editorView" })
            ((DiagramViewport)typeof(ChemfigEditorWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(chemistry)!).ZoomAt(new Point(0,0),8);
        if (!Equals(before, chemistryGeneration.GetValue(chemistry)) || !ReferenceEquals(chemistryImage.Source, image)) throw new Exception("Chemfig zoom regenerated its preview.");
        if (Math.Abs(chemistryImage.Width - image.Width * 8) > 1e-8) throw new Exception("Chemfig vector preview did not scale on zoom.");
        chemistry.Close(); main.Close(); app.Shutdown();
        Console.WriteLine("Main WYSIWYG/Preview and Chemfig editor/preview zoom reuse vector sources without compilation, conversion or zoom-raster jobs.");
    }
}
