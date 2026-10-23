using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using TikzEdt.Modern;

internal static class ChemfigRegression
{
    public static void Run()
    {
        const string chain = "CH_3-CH=CH-CH_2-CH_3";
        const string verbose = "CH_3(-[:0,1]CH(=[:0,1]CH(-[:0,1]CH_2(-[:0,1]CH_3))))";
        if (ChemfigMolecule.Parse(chain).ToSource() != chain || ChemfigMolecule.Parse(verbose).ToSource() != chain)
            throw new Exception("Straight chains are not simplified to the user's original compact notation.");
        if (ChemfigMolecule.Parse("C(-[:90,1]O)-[:0,1]N").ToSource() != "C(-[:90]O)-N") throw new Exception("Branches/default angles/lengths were not simplified safely.");
        if (ChemfigMolecule.Parse("C-[:0,1.5]O").ToSource() != "C-[,1.5]O") throw new Exception("A nondefault bond length was lost.");
        if (ChemfigMolecule.Ring(6, true).ToSource() != "*6(=-=-=-)") throw new Exception("Regular benzene is not emitted in compact ring notation.");
        var regular = ChemfigMolecule.Ring(6, false);
        for (var i = 0; i < 6; i++)
        {
            var point = regular.Atoms[i].Position;
            var a = regular.Atoms[(i + 5) % 6].Position - point; var b = regular.Atoms[(i + 1) % 6].Position - point;
            if (Math.Abs(a.Length - 1) > 1e-9 || Math.Abs(Math.Acos(Vector.Multiply(a, b) / (a.Length * b.Length)) * 180 / Math.PI - 120) > 1e-7)
                throw new Exception("Hexagon has distorted lengths or sharp interior angles.");
            if ((ChemfigGrid.Snap(point, false) - point).Length > 1e-9) throw new Exception("Hexagon vertex is not on the 30-degree chemical lattice.");
        }
        var deformed = regular.Clone(); deformed.Atoms[2] = deformed.Atoms[2] with { Position = deformed.Atoms[2].Position + new Vector(.1, .2) };
        if (deformed.ToSource().Contains("*6")) throw new Exception("Simplification flattened a deformed ring.");
        foreach (var degrees in new[] { -150, -90, -30, 30, 90, 150 })
        {
            var radians = degrees * Math.PI / 180; var expected = new Point(Math.Cos(radians), Math.Sin(radians));
            var endpoint = ChemfigGrid.BondEnd(new Point(), expected, false, true);
            if ((endpoint - expected).Length > 1e-9 || (ChemfigGrid.Snap(endpoint, false) - endpoint).Length > 1e-9) throw new Exception("Chemical bond direction does not match the existing grid.");
        }
        foreach (var angle in Enumerable.Range(0, 6).Select(i => i * Math.PI / 3))
        {
            var expected = new Point(Math.Cos(angle), Math.Sin(angle));
            var endpoint = ChemfigGrid.BondEnd(new Point(), new Point(expected.X * 2, expected.Y * 2), false, true, 0);
            if ((endpoint - expected).Length > 1e-9 || (ChemfigGrid.Snap(expected, false, 0) - expected).Length > 1e-9) throw new Exception("Default bond length/hex lattice mismatch.");
        }
        if ((ChemfigGrid.Snap(new Point(.51, .8), false, 0) - new Point(.5, Math.Sqrt(3) / 2)).Length > 1e-9) throw new Exception("Nearest hex snapping failed.");
        if (ChemfigGrid.Snap(new Point(.7, 1.1), true) != new Point(1, 1)) throw new Exception("Cartesian snapping failed.");
        var preferences = new UserSettings { RememberLastEngine = true, DefaultEngine = "pdflatex", LastUsedEngine = "xelatex" };
        var restored = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(preferences))!;
        if (restored.StartupEngine != "xelatex") throw new Exception("Remember-last-compiler did not round trip.");
        restored.RememberLastEngine = false;
        if (restored.StartupEngine != "pdflatex") throw new Exception("Fixed default compiler was not respected.");
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            foreach (var source in new[] { chain, verbose, "CH_3-CH_2-OH", "H_3C-[:30]C(=[:90]O)-[:-30]OH", "*6(=-=-=-)", "C*6(-N=-=-=)", "*5(-----)", "C>[:45]O", "A-[:30]B(-[::90]N)-[7]O" })
            {
                var graph = ChemfigMolecule.Parse(source); var serialized = graph.ToSource(); var roundTrip = ChemfigMolecule.Parse(serialized);
                if (graph.Atoms.Count != roundTrip.Atoms.Count || graph.Bonds.Count != roundTrip.Bonds.Count || roundTrip.ToSource() != serialized)
                    throw new Exception("Chemfig round trip failed: " + source + " -> " + serialized);
            }
            foreach (var unsupported in new[] { @"\charge{90=+}{N}-O", "C-[,1,,,red]O", "*6(-=-(-O)=-=)", "C?(:30)", "C-[::nan]O" })
            {
                try { ChemfigMolecule.Parse(unsupported); throw new Exception("Unsupported source was silently rewritten: " + unsupported); }
                catch (FormatException) { }
            }
            var document = @"\node at (0,0) {\chemfig{CH_{3}-OH}};";
            var target = ChemfigMolecule.FindCommand(document, document.IndexOf("OH", StringComparison.Ordinal))!.Value;
            if (target.Body != "CH_{3}-OH" || document.Substring(target.Offset, target.Length) != @"\chemfig{CH_{3}-OH}") throw new Exception("Balanced Chemfig extraction failed.");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        var watch = Stopwatch.StartNew();
        _ = TikzSyntaxDiagnostics.Analyze("\\begin{tikzpicture}\n" + string.Concat(Enumerable.Repeat("\\draw (0,0)--(1,1);\n", 4000)) + "\\end{tikzpicture}");
        Console.WriteLine($"4,000-command syntax analysis: {watch.ElapsedMilliseconds} ms (now dispatched to a background worker).");
        Exception? failure = null;
        var thread = new Thread(() => { try { Ui(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure is not null) throw failure;
        Console.WriteLine("Last-used/fixed compiler settings, Chemfig chains/branches/rings/wedges, locale-independent round trips, unsupported-source protection, visual undo/redo, menu spacing and cross-thread bitmap loading passed.");
    }
    private static void Ui()
    {
        var app = new App(); app.InitializeComponent();
        var main = new MainWindow(); main.Measure(new Size(1440, 900)); main.Arrange(new Rect(0, 0, 1440, 900)); main.UpdateLayout();
        foreach (var item in Descendants<MenuItem>(main).Where(i => ItemsControl.ItemsControlFromItemContainer(i) is Menu))
        {
            var gesture = (TextBlock)item.Template.FindName("GestureText", item);
            var header = (System.Windows.Controls.AccessText)item.Template.FindName("HeaderText", item);
            if (gesture.Visibility != Visibility.Collapsed || header.Margin.Right != 0 || item.ActualWidth > header.ActualWidth + 20)
                throw new Exception("Top menu still reserves unused gesture spacing.");
        }
        var molecule = ChemfigMolecule.Parse("");
        var editor = new ChemfigEditorWindow(molecule, "pdflatex", "", null);
        var method = typeof(ChemfigEditorWindow).GetMethod("AddRing", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(editor, [6, true]);
        if (ChemfigMolecule.Parse(editor.Result[9..^1]).Atoms.Count != 6) throw new Exception("Visual ring tool failed.");
        typeof(ChemfigEditorWindow).GetMethod("Undo", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
        if (editor.Result != "\\chemfig{}") throw new Exception("Molecule undo failed.");
        typeof(ChemfigEditorWindow).GetMethod("Redo", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(editor, null);
        var canvas = (Canvas)typeof(ChemfigEditorWindow).GetField("_canvas", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        var previewHost = ((DiagramViewport)typeof(ChemfigEditorWindow).GetField("_previewView", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!).Surface;
        var background = ((SolidColorBrush)app.FindResource("WindowBrush")).Color;
        var ink = ((SolidColorBrush)app.FindResource("TextBrush")).Color;
        if (((SolidColorBrush)editor.Background).Color != background || ((SolidColorBrush)canvas.Background).Color != background || ((SolidColorBrush)previewHost.Background).Color != background)
            throw new Exception("Chemfig window/canvases do not use the dark theme.");
        foreach (var line in canvas.Children.OfType<System.Windows.Shapes.Line>())
            if (((SolidColorBrush)line.Stroke).Color != ink) throw new Exception("Chemfig bonds are not readable on dark canvas.");
        foreach (var label in canvas.Children.OfType<TextBlock>())
            if (((SolidColorBrush)label.Foreground).Color != ink || ((SolidColorBrush)label.Background).Color != background) throw new Exception("Chemfig atom labels have light patches or low contrast.");
        var resultBefore = editor.Result;
        var themedSource = ChemfigEditorWindow.BuildPreviewSource(resultBefore, ink);
        if (!themedSource.Contains("\\color[RGB]{223,225,229}") || editor.Result != resultBefore || editor.Result.Contains("\\color")) throw new Exception("Preview theme changed the exported molecule.");
        var sample = Path.GetFullPath(".codex-smoke/vector-tests/pdflatex.svg");
        if (File.Exists(sample)) ((Image)typeof(ChemfigEditorWindow).GetField("_preview", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!).Source = TexVectorRenderer.LoadAsync(sample, CancellationToken.None).GetAwaiter().GetResult();
        var content = (FrameworkElement)editor.Content; content.Measure(new Size(1056, 630)); content.Arrange(new Rect(0, 0, 1056, 630)); content.UpdateLayout();
        var folder = Path.GetFullPath(".codex-smoke/chemfig-tests"); Directory.CreateDirectory(folder);
        var bitmap = new RenderTargetBitmap(1080, 660, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var path = Path.Combine(folder, "editor.png"); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(path)) encoder.Save(stream);
        var loaded = PreviewImageLoader.LoadAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        if (!loaded.IsFrozen || loaded.PixelWidth != 1080) throw new Exception("Cross-thread frozen decode failed.");
        Interaction(editor, canvas);
        editor.Close(); main.Close(); app.Shutdown();
    }
    private static void Interaction(ChemfigEditorWindow editor, Canvas canvas)
    {
        ChemfigMolecule Graph() => (ChemfigMolecule)typeof(ChemfigEditorWindow).GetField("_molecule", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        Point Screen(Point p) => new(canvas.ActualWidth / 2 + p.X * 65, canvas.ActualHeight / 2 - p.Y * 65);
        void Click(int atom, bool control = false) { var p = Screen(Graph().Atoms[atom].Position); editor.BeginPointer(p, control); editor.EndPointer(p, false); }
        Click(0);
        Descendants<Button>((DependencyObject)editor.Content).Single(b => b.Content?.ToString() == "C").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Graph().Atoms[0].Label != "C" || !editor.Result.StartsWith("\\chemfig{C*6")) throw new Exception("The Carbon C button still creates an empty label.");
        Descendants<Button>((DependencyObject)editor.Content).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Skeletal carbon").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (Graph().Atoms[0].Label != "") throw new Exception("Skeletal carbon is not separately selectable.");
        if (!canvas.Children.OfType<System.Windows.Shapes.Rectangle>().Any(r => Equals(r.Tag, "chemfig-grid"))) throw new Exception("Construction grid is not drawn.");
        Click(0); var unchanged = editor.Result; Click(0); if (editor.Result != unchanged) throw new Exception("A stationary selection snapped an atom.");
        Click(1, true);
        var before = Graph().Clone(); var start = Screen(before.Atoms[0].Position); var end = start + new Vector(65 * Math.Sqrt(3) / 2, -32.5);
        editor.BeginPointer(start, false); editor.MovePointer(end, false); editor.EndPointer(end, false);
        foreach (var index in new[] { 0, 1 })
            if ((Graph().Atoms[index].Position - before.Atoms[index].Position - new Vector(Math.Sqrt(3) / 2, .5)).Length > 1e-9) throw new Exception("Selected atoms did not move by the same snapped vector.");
        if (Graph().Atoms[2] != before.Atoms[2]) throw new Exception("Unselected atom was moved.");
        editor.HandleEditorKey(Key.Delete, ModifierKeys.None, false);
        if (Graph().Atoms.Count != 4) throw new Exception("Mouse multi-selection Delete failed.");
        editor.HandleEditorKey(Key.Z, ModifierKeys.Control, false);
        if (Graph().Atoms.Count != 6) throw new Exception("Delete was not a single undo step.");
        editor.HandleEditorKey(Key.Z, ModifierKeys.Control, false); // undo translation
        var bond = Graph().Bonds[0]; var mid = Screen(new Point((Graph().Atoms[bond.A].Position.X + Graph().Atoms[bond.B].Position.X) / 2, (Graph().Atoms[bond.A].Position.Y + Graph().Atoms[bond.B].Position.Y) / 2));
        editor.BeginPointer(mid, false); editor.EndPointer(mid, false);
        var tripleBond = Descendants<RadioButton>((DependencyObject)editor.Content).Single(r => System.Windows.Automation.AutomationProperties.GetName(r) == "≡  Triple");
        tripleBond.IsChecked = true;
        if (Graph().Bonds[0].Kind != "~") throw new Exception("Bond side palette did not update the selected bond.");
        editor.HandleEditorKey(Key.Z, ModifierKeys.Control, false);
        editor.BeginPointer(mid, false); editor.EndPointer(mid, false);
        editor.HandleEditorKey(Key.Delete, ModifierKeys.None, false);
        if (Graph().Atoms.Count != 6 || Graph().Bonds.Count != 5) throw new Exception("Bond selection/Delete removed the wrong graph elements.");
        editor.HandleEditorKey(Key.Z, ModifierKeys.Control, false);
        editor.HandleEditorKey(Key.Escape, ModifierKeys.None, false);
        editor.BeginPointer(new Point(2, 2), false); editor.MovePointer(new Point(canvas.ActualWidth - 2, canvas.ActualHeight - 2), false); editor.EndPointer(new Point(canvas.ActualWidth - 2, canvas.ActualHeight - 2), false);
        editor.HandleEditorKey(Key.Delete, ModifierKeys.None, false);
        if (Graph().Atoms.Count != 1 || Graph().Bonds.Count != 0) throw new Exception("Marquee-selected atoms/bonds were not deleted together.");
        editor.HandleEditorKey(Key.Z, ModifierKeys.Control, false);
        if (Graph().Atoms.Count != 6) throw new Exception("Marquee deletion undo failed.");
        editor.HandleEditorKey(Key.A, ModifierKeys.Control, false);
        editor.ChooseAtom("O"); if (Graph().Atoms.Any(a => a.Label != "O")) throw new Exception("Side-palette atom did not apply to selected atoms.");
        if (editor.HandleEditorKey(Key.Delete, ModifierKeys.None, true) || Graph().Atoms.Count != 6) throw new Exception("Delete intercepted text field editing.");
        editor.HandleEditorKey(Key.Z, ModifierKeys.Control, false);
        editor.HandleEditorKey(Key.Escape, ModifierKeys.None, false);
        var mode = (ComboBox)typeof(ChemfigEditorWindow).GetField("_mode", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(editor)!;
        mode.SelectedIndex = 1; editor.ChooseAtom("N");
        var root = Graph().Atoms[0].Position; var count = Graph().Atoms.Count;
        editor.BeginPointer(Screen(root), false); editor.EndPointer(Screen(root + new Vector(-.7, -1.2)), false);
        if (Graph().Atoms.Count != count + 1 || Graph().Atoms[^1].Label != "N" || Math.Abs((Graph().Atoms[^1].Position - root).Length - 1) > 1e-9)
            throw new Exception("Side-palette drawing did not preserve the default bond length or new atom choice.");
        editor.HandleEditorKey(Key.Z, ModifierKeys.Control, false); mode.SelectedIndex = 0;
        mode.SelectedIndex = 1;
        var existingCount = Graph().Atoms.Count;
        editor.BeginPointer(Screen(Graph().Atoms[0].Position), false);
        editor.EndPointer(Screen(Graph().Atoms[0].Position + new Vector(0, 1.5)), false);
        if (Graph().Atoms.Count != existingCount) throw new Exception("Snapping onto an existing grid atom created a duplicate atom.");
        editor.HandleEditorKey(Key.Z, ModifierKeys.Control, false); mode.SelectedIndex = 0;
        var chain = new ChemfigEditorWindow(ChemfigMolecule.Parse("C-C-C"), "pdflatex", "", null);
        var chainCanvas = (Canvas)typeof(ChemfigEditorWindow).GetField("_canvas", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chain)!;
        // Without layout Origin = 0, so the middle atom is at (65,0).
        chain.BeginPointer(new Point(65, 0), false); chain.EndPointer(new Point(65, 0), false); chain.HandleEditorKey(Key.Delete, ModifierKeys.None, false);
        var apply = (Button)typeof(ChemfigEditorWindow).GetField("_apply", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chain)!;
        if (apply.IsEnabled) throw new Exception("Disconnected fragment deletion should not allow an invalid apply.");
        chain.HandleEditorKey(Key.Z, ModifierKeys.Control, false); if (!apply.IsEnabled) throw new Exception("Undo did not restore connected apply state.");
        chain.Close();
        Console.WriteLine("Hex/Cartesian default-length grid, marquee/Ctrl selection, snapped group movement, bond/atom Delete, undo, text-input isolation, side atoms and fragment safety passed.");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    public static async Task IntegrationAsync(bool dark = false)
    {
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        {
            foreach (var body in new[] { ChemfigMolecule.Parse("H_3C-[:30]C(=[:90]O)-[:-30]OH").ToSource(), ChemfigMolecule.Parse("CH_3(-[:0,1]CH(=[:0,1]CH(-[:0,1]CH_2(-[:0,1]CH_3))))").ToSource(), ChemfigMolecule.Ring(6, true).ToSource() })
            {
                var command = "\\chemfig{" + body + "}";
                var source = dark ? ChemfigEditorWindow.BuildPreviewSource(command, Color.FromRgb(223, 225, 229))
                    : "\\begin{tikzpicture}\n\\node[inner sep=0pt] at (0,0) {" + command + "};\n\\end{tikzpicture}";
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var result = await TexPreviewRenderer.RenderAsync(source, engine, null, "", 192, timeout.Token, new UserSettings());
                if (result.ExitCode != 0 || !File.Exists(result.PngPath) || result.BoundingBox.IsEmpty) throw new Exception(engine + ": " + result.Log);
                var image = await PreviewImageLoader.LoadAsync(result.PngPath, timeout.Token);
                if (!image.IsFrozen) throw new Exception("TeX image cannot cross worker threads.");
                if (dark)
                {
                    var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
                    var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
                    var opaque = 0; var bright = 0; var transparent = 0;
                    for (var i = 0; i < pixels.Length; i += 4)
                    {
                        if (pixels[i + 3] < 10) transparent++;
                        if (pixels[i + 3] < 220) continue;
                        opaque++; if (pixels[i] > 180 && pixels[i + 1] > 180 && pixels[i + 2] > 180) bright++;
                    }
                    if (opaque < 10 || bright < opaque * .95 || transparent < pixels.Length / 8) throw new Exception("Dark TeX preview contains black ink or an opaque light background.");
                    var folder = Path.GetFullPath(".codex-smoke/chemfig-tests"); Directory.CreateDirectory(folder);
                    File.Copy(result.PngPath, Path.Combine(folder, "dark-" + engine + ".png"), true);
                }
            }
            Console.WriteLine("Chemfig branched molecule + generated ring compiled and decoded" + (dark ? " with readable light ink/transparency" : "") + " with " + engine);
        }
    }
}
