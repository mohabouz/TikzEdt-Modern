using System.Reflection;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using TikzEdt.Modern;
using TikzEdt.Parser;

internal static class WorkspaceRegression
{
    public static void Run()
    {
        var diagram = LegacyTikzAdapter.Parse("\\begin{tikzpicture}[scale=2]\\begin{scope}[shift={(1,2)},scale=.5]\\draw (0,0)--(2,1);\\end{scope}\\node at (30:2) {Polar};\\end{tikzpicture}");
        if (diagram.RootFrame.ToWorld(new(3, 4)) != new Point(6, 8) || diagram.Scopes.Count != 1) throw new Exception("Root/scoped transforms were not parsed.");
        var snapped = diagram.RootFrame.Snap(new(2.21, 3.81), .5, false);
        if ((snapped - new Point(2, 4)).Length > 1e-8 || diagram.RootFrame.ToLocal(snapped) != new Point(1, 2)) throw new Exception("Scaled snapping/mouse coordinate inverse failed.");
        var matrix = Matrix.Identity; matrix.Scale(2, 3); matrix.Rotate(30); matrix.Translate(1, -2);
        var rotated = new EditorCoordinateFrame(matrix);
        var localSnap = rotated.ToLocal(rotated.Snap(rotated.ToWorld(new(1.12, 2.23)), .5, false));
        if ((localSnap - new Point(1, 2)).Length > 1e-8) throw new Exception("Rotated/scaled scoped grid snap failed.");
        var polar = diagram.Points.First(point => point.IsPolar);
        var moved = LegacyTikzAdapter.MovePoint(polar, polar.Position + new Vector(.7, 1.3));
        if (!moved.Contains("38.396398")) throw new Exception("Polar angle was rounded to a whole degree.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var precise = "\\begin{tikzpicture}\\node at (1.12345678, 2.87654321) {Keep};\\end{tikzpicture}";
            for (var index = 0; index < 20; index++)
            {
                var nodePoint = LegacyTikzAdapter.Parse(precise).Points.Single();
                var replacement = LegacyTikzAdapter.MovePoint(nodePoint, nodePoint.Position + new Vector(index % 2 == 0 ? .5 : -.5, 0));
                precise = precise.Remove(nodePoint.Offset, nodePoint.Length).Insert(nodePoint.Offset, replacement);
            }
            if (!precise.Contains("(1.12345678, 2.87654321)")) throw new Exception("Repeated movement changed original decimal precision, whitespace, or untouched axis: " + precise);
            var relativeSource = "\\begin{tikzpicture}\\draw (0,0) -- ++(1.12345678,0);\\end{tikzpicture}";
            var relative = LegacyTikzAdapter.Parse(relativeSource).Points;
            _ = LegacyTikzAdapter.MovePoint(relative[0], relative[0].Position + new Vector(.5, .5));
            if (LegacyTikzAdapter.MovePoint(relative[1], relative[1].Position + new Vector(.5, .5)) != "++(1.12345678,0)") throw new Exception("A translated relative coordinate was needlessly reserialized.");
        }
        finally { CultureInfo.CurrentCulture = culture; }
        if (EditorSearch.Matches("Foo foo food", "foo", false, false, false).Length != 3 || EditorSearch.Matches("Foo foo food", "foo", false, true, true).Length != 1) throw new Exception("Case/whole-word search failed.");
        var match = EditorSearch.Matches("x12", @"x(\d+)", true, true, false).Single();
        if (EditorSearch.Replacement(match, "$1", true) != "12" || EditorSearch.Replacement(match, "$1", false) != "$1") throw new Exception("Regex/literal replacement semantics failed.");
        if (LatexSymbolCatalog.Items.Count < 170 || LatexSymbolCatalog.Items.Select(item => item.Category).Distinct().Count() < 8 || LatexSymbolCatalog.Items.Any(item => string.IsNullOrWhiteSpace(item.Command))) throw new Exception("Symbol catalogue is missing categories or valid commands.");
        foreach (var math in new[] { "$x", "$$x", @"\(x", @"\[x", @"\begin{align}x" }) if (!LatexSymbolCatalog.IsMathContext(math, math.Length)) throw new Exception("Math context was not recognized: " + math);
        if (LatexSymbolCatalog.IsMathContext("$x$ text", 8)) throw new Exception("Closed math context remained active.");
        Console.WriteLine("Scaled/rotated snapping, scope frames, polar precision, repeated group/relative movement under French locale, search options, regex replacements and symbol categories passed.");
        Exception? failure = null;
        var thread = new Thread(() => { try { CheckUi(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure is not null) throw failure;
    }

    public static async Task ExportIntegrationAsync()
    {
        const string source = "\\begin{tikzpicture}[scale=2]\\begin{scope}[shift={(.5,.5)}]\\draw[blue,thick] (0,0) rectangle (2,1);\\node at (1,.5) {$E=mc^2$};\\end{scope}\\end{tikzpicture}";
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        {
            var result = await TexPreviewRenderer.RenderAsync(source, engine, null, null, 300, CancellationToken.None, new UserSettings());
            if (result.ExitCode != 0 || !File.Exists(result.PdfPath) || !File.Exists(result.PngPath)) throw new Exception(engine + " export compilation failed: " + result.Log);
            var svg = await SvgClipboard.ExportAsync(result.PdfPath, CancellationToken.None);
            var xml = System.Xml.Linq.XDocument.Load(svg);
            if (xml.Root?.Name.LocalName != "svg" || !xml.Descendants().Any(element => element.Name.LocalName == "path")) throw new Exception(engine + " SVG does not contain vector paths.");
            Console.WriteLine($"{engine}: scaled/scoped PDF, 300-DPI PNG and vector SVG export passed.");
        }
    }

    private static void CheckUi()
    {
        var app = new App(); app.InitializeComponent();
        var main = new MainWindow(); var editor = (TextEditor)main.FindName("Editor");
        var type = main.GetType(); const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var search = (EditorFindReplace)type.GetField("_findReplace", flags)!.GetValue(main)!;
        search.Open(true); editor.Text = "foo1 foo2 food"; search.FindInput.Text = @"foo(\d)"; search.RegexButton.IsChecked = true; search.ReplaceInput.Text = "$1-bar"; search.ReplaceAll();
        if (editor.Text != "1-bar 2-bar food") throw new Exception("Replace all did not expand capture groups.");
        if (!main.HandleDocumentHistory(Key.Z, ModifierKeys.Control, false) || editor.Text != "foo1 foo2 food") throw new Exception("Canvas-focused undo did not restore replace-all in one step.");
        if (!main.HandleDocumentHistory(Key.Y, ModifierKeys.Control, false) || editor.Text != "1-bar 2-bar food") throw new Exception("Canvas-focused redo did not restore the edit.");
        if (main.HandleDocumentHistory(Key.Z, ModifierKeys.Control, true)) throw new Exception("Document undo stole focus from an input textbox.");
        search.RegexButton.IsChecked = false; search.FindInput.Text = "bar"; search.ReplaceInput.Text = "$1"; search.ReplaceAll();
        if (editor.Text != "1-$1 2-$1 food") throw new Exception("Plain-text replacement interpreted regex references.");
        search.RegexButton.IsChecked = true; search.FindInput.Text = "["; search.Refresh(); // invalid regex must not escape to the UI
        search.FindInput.Text = "foo";
        foreach (var name in new[] { "SelectTool", "EditTool", "RectangleTool", "BezierTool" })
        {
            var button = (RadioButton)main.FindName(name);
            if (button.Content is not IconLabel { Text: "" } || button.ToolTip is not ToolTip { Content: StackPanel panel } || panel.Children.OfType<TextBlock>().First().FontWeight != FontWeights.Bold)
                throw new Exception("Icon-only tool / structured tooltip is missing: " + name);
        }
        if (((TabItem)main.FindName("SymbolsSidebarTab")).Content is not LatexSymbolsPanel) throw new Exception("Symbols sidebar missing.");
        var picker = new ColorPickerWindow();
        editor.Text = "\\begin{tikzpicture}[scale=2]\\begin{scope}[shift={(1,2)}]\\draw[blue,thick] (1.12345678, 2) rectangle (3,4);\\end{scope}\\end{tikzpicture}";
        editor.CaretOffset = editor.Text.IndexOf("rectangle", StringComparison.Ordinal);
        type.GetMethod("UpdateActiveCoordinateFrame", flags)!.Invoke(main, [editor.CaretOffset]);
        var diagram = (LegacyTikzDiagram)type.GetField("_cachedDiagram", flags)!.GetValue(main)!;
        type.GetMethod("DrawScopeGuides", flags)!.Invoke(main, [diagram]);
        var canvas = (Canvas)main.FindName("OverlayCanvas");
        if (!canvas.Children.OfType<System.Windows.Shapes.Rectangle>().Any(shape => Equals(shape.Tag, "scope-guide") && !shape.IsHitTestVisible)) throw new Exception("Faint non-intercepting scope guide missing.");
        var first = editor.Text.IndexOf("\\draw", StringComparison.Ordinal); var length = editor.Text.IndexOf(';', first) - first + 1;
        var translated = (string)type.GetMethod("TranslateSourceRange", flags)!.Invoke(main, [first, length, new Vector(1, 0)])!;
        if (!translated.Contains("[blue,thick]") || !translated.Contains("(1.62345678, 2)") || !translated.Contains("(3.5,4)")) throw new Exception("Scaled owner movement lost source precision/style: " + translated);
        main.Measure(new Size(1440, 900)); main.Arrange(new Rect(0, 0, 1440, 900)); main.UpdateLayout();
        type.GetMethod("ApplyEditorScrollTheme", flags)!.Invoke(main, []);
        var scrollers = Visuals<ScrollViewer>(editor).ToArray();
        if (scrollers.Length == 0) throw new Exception("Editor scrollbar viewer was not found.");
        foreach (var scroller in scrollers) { scroller.ApplyTemplate(); if (scroller.Template.FindName("ScrollBarCorner", scroller) is not Border corner || corner.Background.ToString() != editor.Background.ToString()) throw new Exception("Editor scrollbar corner color does not match its background."); }
        var outputRoot = Path.GetFullPath("tests/TikzEdt.RegressionTests/workspace-tests"); Directory.CreateDirectory(outputRoot);
        var image = BitmapSource.Create(20, 10, 300, 300, PixelFormats.Bgra32, null, new byte[20 * 10 * 4], 20 * 4);
        foreach (var extension in new[] { ".png", ".jpg", ".tiff", ".bmp" })
        {
            var path = Path.Combine(outputRoot, "export" + extension); FigureImageExport.Save(image, path, extension);
            var loaded = SnippetLibrary.LoadPreview(path);
            if (loaded.PixelWidth != 20 || loaded.PixelHeight != 10 || Math.Abs(loaded.DpiX - 300) > 2) throw new Exception("Raster dimensions/DPI changed for " + extension);
        }
        var jpeg = new FormatConvertedBitmap(SnippetLibrary.LoadPreview(Path.Combine(outputRoot, "export.jpg")), PixelFormats.Bgra32, null, 0); var pixels = new byte[20 * 10 * 4]; jpeg.CopyPixels(pixels, 80, 0);
        if (pixels[0] < 245 || pixels[1] < 245 || pixels[2] < 245) throw new Exception("JPEG transparent background was rendered black.");
        ((TabItem)main.FindName("SymbolsSidebarTab")).IsSelected = true;
        editor.Text = "\\begin{tikzpicture}[scale=1.5]\n    \\begin{scope}[shift={(0.5,0)}]\n        \\draw[blue,thick] (0,0) rectangle (2,1);\n        \\node at (1,.5) {$E=mc^2$};\n    \\end{scope}\n\\end{tikzpicture}";
        search.FindInput.Text = @"\\draw|\\node"; search.RegexButton.IsChecked = true; search.Refresh(); search.ReplaceInput.Text = "";
        var root = (FrameworkElement)main.Content; main.Content = null;
        root.Width = 1440; root.Height = 900; root.Measure(new Size(1440, 900)); root.Arrange(new Rect(0, 0, 1440, 900)); root.UpdateLayout();
        SaveSnapshot(root, Path.Combine(outputRoot, "workspace.png"), 1440, 900);
        var pickerRoot = (FrameworkElement)picker.Content; picker.Content = null;
        var pickerFrame = new Border { Child = pickerRoot, Width = 440, Background = new SolidColorBrush(Color.FromRgb(30, 31, 34)) };
        pickerFrame.Measure(new Size(440, double.PositiveInfinity)); pickerFrame.Arrange(new Rect(new Size(440, pickerFrame.DesiredSize.Height))); pickerFrame.UpdateLayout();
        SaveSnapshot(pickerFrame, Path.Combine(outputRoot, "color-picker.png"), 440, (int)pickerFrame.ActualHeight);
        Console.WriteLine("Find/replace undo/redo, literal replacement, invalid regex, icon tooltips, symbol/color UI, scaled shape translation, scope guide, dark scrollbar corner and PNG/JPEG/TIFF/BMP export passed.");
        app.Shutdown();
    }
    private static IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T match) yield return match; foreach (var descendant in Visuals<T>(child)) yield return descendant; } }
    private static void SaveSnapshot(FrameworkElement root, string path, int width, int height)
    {
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen())
        { context.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 31, 34)), null, new Rect(0, 0, width, height)); context.DrawRectangle(new VisualBrush(root) { AutoLayoutContent = false, Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight)); }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); encoder.Save(file);
    }
}
