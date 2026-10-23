using System.Reflection;
using System.IO;

var renderer = typeof(TikzEdt.Modern.MainWindow).Assembly.GetType("TikzEdt.Modern.TexPreviewRenderer")!;
var render = renderer.GetMethod("RenderAsync", BindingFlags.Public | BindingFlags.Static)!;
if (args.Contains("--chemfig-layout-regression")) { ChemfigLayoutRegression.Run(); return; }
if (args.Contains("--labo-coverage-regression")) { LaboCoverageRegression.Run(); return; }
if (args.Contains("--labo-coverage-integration")) { await LaboCoverageRegression.IntegrationAsync(); return; }
if (args.Contains("--labo-navigation-regression")) { LaboNavigationRegression.Run(); return; }
if (args.Contains("--labo-integration")) { await LaboNavigationRegression.IntegrationAsync(); return; }
if (args.Contains("--labo-visual-proof")) { await LaboNavigationRegression.VisualProofAsync(); return; }
if (args.Contains("--chemistry-editing-regression")) { ChemistryEditingRegression.Run(); return; }
if (args.Contains("--chemfig-stereo-regression")) { ChemfigStereoRegression.Run(); return; }
if (args.Contains("--chemfig-stereo-integration")) { await ChemfigStereoRegression.IntegrationAsync(); return; }
if (args.Contains("--vector-alignment")) { await VectorAlignmentRegression.RunAsync(); return; }
if (args.Contains("--vector-regression") || args.Contains("--vector-integration")) { await VectorPreviewRegression.RunAsync(args.Contains("--vector-integration")); return; }
if (args.Contains("--chemfig-regression")) { ChemfigRegression.Run(); return; }
if (args.Contains("--chemfig-dark-integration")) { await ChemfigRegression.IntegrationAsync(true); return; }
if (args.Contains("--chemfig-integration")) { await ChemfigRegression.IntegrationAsync(); return; }
if (args.Contains("--workspace-regression")) { WorkspaceRegression.Run(); return; }
if (args.Contains("--workspace-export-integration")) { await WorkspaceRegression.ExportIntegrationAsync(); return; }
if (args.Contains("--snippet-configuration") || args.Contains("--snippet-configuration-integration"))
{
    await SnippetBatchRegression.RunConfigurationRegressionAsync(args.Contains("--snippet-configuration-integration"));
    return;
}
if (args.Contains("--snippet-pages"))
{
    await SnippetBatchRegression.RunPagesAsync();
    return;
}
if (args.Contains("--snippet-batch-integration"))
{
    await SnippetBatchRegression.RunIntegrationAsync();
    return;
}
if (args.Contains("--snippet-batch-regression"))
{
    await SnippetBatchRegression.RunAsync();
    return;
}
if (args.Contains("--compiler-switch"))
{
    Exception? failure = null;
    var switchThread = new Thread(() =>
    {
        try
        {
            var app = new TikzEdt.Modern.App(); app.InitializeComponent();
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            var main = new TikzEdt.Modern.MainWindow();
            var mainType = main.GetType();
            var selector = (System.Windows.Controls.ComboBox)main.FindName("EngineSelector");
            var status = (System.Windows.Controls.TextBlock)main.FindName("ModeStatus");
            var generationField = mainType.GetField("_compileGeneration", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var settingsStore = renderer.Assembly.GetType("TikzEdt.Modern.SettingsStore")!;
            var settings = (TikzEdt.Modern.UserSettings)settingsStore.GetProperty("Current")!.GetValue(null)!;
            settings.AutomaticPreview = false;
            ((ICSharpCode.AvalonEdit.TextEditor)main.FindName("Editor")).Text = "\\begin{tikzpicture}\\draw (0,0) rectangle (2,1);\\end{tikzpicture}";
            ((System.Windows.Controls.RadioButton)main.FindName("ProductionMode")).IsChecked = true;
            foreach (var engineName in new[] { "lualatex", "xelatex", "pdflatex" })
            {
                var oldGeneration = (int)generationField.GetValue(main)!;
                selector.SelectedItem = selector.Items.OfType<System.Windows.Controls.ComboBoxItem>().First(item => (string)item.Tag == engineName);
                if ((int)generationField.GetValue(main)! != oldGeneration + 1 || !status.Text.Contains("Compiling with " + engineName))
                    throw new Exception("Engine selection did not immediately compile: " + engineName);
                var frame = new System.Windows.Threading.DispatcherFrame();
                var deadline = DateTime.UtcNow.AddSeconds(60);
                var poll = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                poll.Tick += (_, _) => { if (!status.Text.StartsWith("Compiling with") || DateTime.UtcNow > deadline) frame.Continue = false; };
                poll.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); poll.Stop();
                if (!status.Text.StartsWith("Exact TeX preview ready · " + engineName)) throw new Exception(status.Text);
                Console.WriteLine("Selection-triggered compilation passed with automatic preview off in Production: " + engineName);
            }
            // Rapid switching must invalidate the first request synchronously.
            var before = (int)generationField.GetValue(main)!;
            selector.SelectedIndex = 1; selector.SelectedIndex = 2;
            if ((int)generationField.GetValue(main)! != before + 2) throw new Exception("Rapid switching did not invalidate old compilation.");
            mainType.GetMethod("CancelCompile_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [main, new System.Windows.RoutedEventArgs()]);
            app.Shutdown();
        }
        catch (Exception exception) { failure = exception; }
    });
    switchThread.SetApartmentState(ApartmentState.STA); switchThread.Start(); switchThread.Join();
    if (failure is not null) throw failure;
    return;
}
if (args.Contains("--format-regression"))
{
    var formatter = renderer.Assembly.GetType("TikzEdt.Modern.TikzCodeFormatter")!;
    string Format(string text, bool spaces = true)
    {
        var edits = ((System.Collections.IEnumerable)formatter.GetMethod("CreateEdits")!.Invoke(null, [text, 4, spaces])!).Cast<object>().Reverse();
        var formatted = text;
        foreach (var edit in edits)
        {
            var offset = (int)edit.GetType().GetProperty("Offset")!.GetValue(edit)!;
            var length = (int)edit.GetType().GetProperty("Length")!.GetValue(edit)!;
            var replacement = (string)edit.GetType().GetProperty("Text")!.GetValue(edit)!;
            formatted = formatted.Remove(offset, length).Insert(offset, replacement);
        }
        return formatted;
    }
    var code = "\\begin{tikzpicture}\r\n\\begin{scope}\r\n\\draw (0,0) -- (1,1); % { ignored\r\n\\end{scope}\r\n\\end{tikzpicture}\r\n";
    var expected = "\\begin{tikzpicture}\r\n    \\begin{scope}\r\n        \\draw (0,0) -- (1,1); % { ignored\r\n    \\end{scope}\r\n\\end{tikzpicture}\r\n";
    if (Format(code) != expected || Format(expected) != expected) throw new Exception("Nested indentation, CRLF preservation or formatter idempotence failed.");
    if (!Format(code, false).Contains("\t\t\\draw")) throw new Exception("Tab indentation preference was ignored.");
    var literal = "\\begin{verbatim}\n   preserve   this {\n\\node unexpected\n\\end{verbatim}\n";
    if (Format(literal) != literal) throw new Exception("Formatter changed verbatim contents.");
    var continuation = "\\begin{tikzpicture}\n\\draw[\nblue,\nthick]\n(0,0) --\n(2,1);\n\\node at (1,1) {100\\% {text}};\n\\end{tikzpicture}";
    var output = Format(continuation);
    if (Format(output) != output || !output.Contains("        (0,0) --")) throw new Exception("Multiline path formatting was not stable.");
    if (string.Join("\n", output.Split('\n').Select(line => line.TrimStart(' ', '\t'))) != continuation)
        throw new Exception("Formatter modified content beyond leading indentation.");
    Console.WriteLine("Formatter nesting, path continuations, tabs/spaces, escaped comments, CRLF, verbatim preservation and idempotence passed.");
    return;
}
if (args.Contains("--sidebar-thumbnails"))
{
    Exception? failure = null;
    var thumbnailThread = new Thread(() =>
    {
        try
        {
            var app = new TikzEdt.Modern.App(); app.InitializeComponent();
            var main = new TikzEdt.Modern.MainWindow();
            var mainType = main.GetType();
            var library = renderer.Assembly.GetType("TikzEdt.Modern.SnippetLibrary")!.GetMethod("Load")!.Invoke(null, [null])!;
            mainType.GetMethod("SetSidebarSnippetItems", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [library]);
            var entry = ((System.Collections.IEnumerable)library).Cast<object>().First(item => (string)item.GetType().GetProperty("Name")!.GetValue(item)! == "bracket");
            var snapshotType = renderer.Assembly.GetType("TikzEdt.Modern.PreambleSnapshot")!;
            var preamble = Activator.CreateInstance(snapshotType, [null, null]);
            var loader = mainType.GetMethod("GetSidebarPreviewTask", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var settings = new TikzEdt.Modern.UserSettings();
            var first = (Task)loader.Invoke(main, [entry, "pdflatex", preamble, settings])!;
            var shared = (Task)loader.Invoke(main, [entry, "pdflatex", preamble, settings])!;
            if (!ReferenceEquals(first, shared)) throw new Exception("Selected preview and thumbnail started duplicate jobs.");
            first.GetAwaiter().GetResult();
            var bitmap = (System.Windows.Media.Imaging.BitmapSource)first.GetType().GetProperty("Result")!.GetValue(first)!;
            if (!bitmap.IsFrozen || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0) throw new Exception("Thumbnail failed to load a thread-safe image.");
            var another = (Task)loader.Invoke(main, [entry, "pdflatex", preamble, settings])!;
            if (!ReferenceEquals(first, another)) throw new Exception("Completed preview was not reused.");
            app.Shutdown();
            Console.WriteLine($"Sidebar thumbnail {bitmap.PixelWidth}x{bitmap.PixelHeight}, frozen bitmap, shared in-flight job and completed reuse passed.");
        }
        catch (Exception exception) { failure = exception; }
    });
    thumbnailThread.SetApartmentState(ApartmentState.STA); thumbnailThread.Start(); thumbnailThread.Join();
    if (failure is not null) throw failure;
    return;
}
if (args.Contains("--folding-regression"))
{
    var foldingType = renderer.Assembly.GetType("TikzEdt.Modern.TikzFolding")!;
    object[] Foldings(string text) => ((System.Collections.IEnumerable)foldingType.GetMethod("Create")!.Invoke(null, [text])!).Cast<object>().ToArray();
    var fixture = "\\begin{tikzpicture}\n% \\end{tikzpicture} ignored\n\\begin{scope}[shift={(1,0)}]\n\\foreach \\x in {1,2,3} {\n\\node at (\\x,0) {\\{text\\}};\n}\n\\end{scope}\n\\end{tikzpicture}";
    if (Foldings(fixture).Length != 3) throw new Exception("Nested environment/loop folding or comment/escape handling failed.");
    if (Foldings("% \\begin{scope}\n% \\end{scope}").Length != 0 || Foldings("\\begin{scope}\\end{scope}").Length != 0 ||
        Foldings("\\begin{scope}\nunfinished").Length != 0)
        throw new Exception("Commented, single-line or unfinished code produced invalid folds.");
    if (Foldings("\\draw[blue]\n (0,0) -- (2,1)\n -- cycle;").Length != 1 ||
        Foldings("\\draw (0,0)\n\\draw (1,0) -- (2,0);").Length != 0)
        throw new Exception("Multiline paths or incomplete-command folding failed.");
    Console.WriteLine("Nested TikZ/scope/loop folds, escaped braces, comments and incomplete/single-line syntax passed.");
    return;
}
if (args.Contains("--drawing-tools"))
{
    var builder = renderer.Assembly.GetType("TikzEdt.Modern.DrawingCommandBuilder")!;
    var commands = new List<string>();
    foreach (var tool in new[] { "bezier", "smooth", "grid", "arc", "pie" })
    {
        System.Windows.Point[] points = tool switch
        {
            "bezier" => [new(0, 0), new(1, 2), new(3, -1), new(4, 1)],
            "smooth" => [new(0, 0), new(1, 2), new(2, 1), new(4, 3)],
            "grid" => [new(0, 0), new(3, 2)],
            _ => [new(0, 0), new(2, 0), new(0, 3)]
        };
        var code = (string)builder.GetMethod("Build")!.Invoke(null, [tool, points, 0.5d])!;
        if (!code.EndsWith(';') || !code.StartsWith("\\draw")) throw new Exception("Invalid drawing command.");
        if (tool == "arc" && !code.Contains("end angle=90,radius=2")) throw new Exception("Arc radius/end projection was incorrect.");
        if (tool == "pie" && !code.EndsWith("-- cycle;")) throw new Exception("Pie was not closed.");
        commands.Add(code);
    }
    foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
    {
        var drawingSource = "\\begin{tikzpicture}\n" + string.Join("\n", commands) + "\n\\end{tikzpicture}";
        var task = (Task)render.Invoke(null, [drawingSource, engine, null, null, 192d, CancellationToken.None, new TikzEdt.Modern.UserSettings()])!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        if ((int)result.GetType().GetProperty("ExitCode")!.GetValue(result)! != 0)
            throw new Exception((string)result.GetType().GetProperty("Log")!.GetValue(result)!);
        Console.WriteLine($"{engine}: Bézier, smooth curve, grid, arc and pie output compiled successfully.");
    }
    return;
}
if (args.Contains("--library-alignment"))
{
    const string libraries = "\\usetikzlibrary{patterns, arrows.meta, calc, positioning, decorations.pathreplacing, shapes.geometric}";
    var fixture = await File.ReadAllTextAsync(".codex-smoke/library-alignment.tikz");
    var picture = fixture[fixture.IndexOf("\\begin{tikzpicture}", StringComparison.Ordinal)..];
    foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
    {
        (int Width, int Height, System.Windows.Rect Bounds)? baseline = null;
        foreach (var prefix in new[] { "", libraries + "\n", libraries + "\n\n\u00a0\u00a0\u00a0\u00a0", "pasted-indentation" })
        {
            var sourceText = prefix == "pasted-indentation"
                ? libraries + "\n" + picture.Replace("\n    ", "\n\u00a0\u00a0\u00a0\u00a0") : prefix + picture;
            var task = (Task)render.Invoke(null, [sourceText, engine, null, null, 192d, CancellationToken.None, new TikzEdt.Modern.UserSettings()])!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            if ((int)result.GetType().GetProperty("ExitCode")!.GetValue(result)! != 0)
                throw new Exception((string)result.GetType().GetProperty("Log")!.GetValue(result)!);
            var png = await File.ReadAllBytesAsync((string)result.GetType().GetProperty("PngPath")!.GetValue(result)!);
            var width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
            var height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
            var bounds = (System.Windows.Rect)result.GetType().GetProperty("BoundingBox")!.GetValue(result)!;
            Console.WriteLine($"{engine} libraryPrefix={prefix.Length > 0}: {width}x{height}, bounds={bounds}");
            if (baseline is { } previous && (width != previous.Width || height != previous.Height || bounds != previous.Bounds))
                throw new Exception("Adding libraries changed the picture's crop/overlay alignment.");
            baseline = (width, height, bounds);
        }
    }
    return;
}
if (args.Contains("--preview-cache-regression"))
{
    var cachedRender = renderer.GetMethod("RenderCachedAsync")!;
    var directory = Path.Combine(Path.GetTempPath(), "TikzEdt-cache-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var dependency = Path.Combine(directory, "figure.tex");
    await File.WriteAllTextAsync(dependency, "\\draw (0,0) rectangle (2,1);");
    var settings = new TikzEdt.Modern.UserSettings();
    var text = "\\begin{tikzpicture}\\input{figure.tex}\\end{tikzpicture}";
    async Task<object> Render(MethodInfo method, string source, double dpi = 192)
    {
        var task = (Task)method.Invoke(null, [source, "pdflatex", directory, null, dpi, CancellationToken.None, settings])!;
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        if ((int)result.GetType().GetProperty("ExitCode")!.GetValue(result)! != 0)
            throw new Exception((string)result.GetType().GetProperty("Log")!.GetValue(result)!);
        return result;
    }
    string Pdf(object result) => (string)result.GetType().GetProperty("PdfPath")!.GetValue(result)!;
    var initial = await Render(cachedRender, text);
    if (Pdf(await Render(cachedRender, text)) != Pdf(initial)) throw new Exception("Unchanged preview was not reused.");
    await File.WriteAllTextAsync(dependency, "\\draw[red,thick] (0,0) rectangle (4,3);");
    var changed = await Render(cachedRender, text);
    if (Pdf(changed) == Pdf(initial)) throw new Exception("Changed input file returned a stale cached preview.");
    if (Pdf(await Render(cachedRender, text, 300)) == Pdf(changed)) throw new Exception("DPI change reused low-resolution pixels.");
    if (Pdf(await Render(render, text)) == Pdf(changed)) throw new Exception("Explicit compilation reused a cached result.");
    settings.CompilerFlags = "-synctex=0";
    var flagsChanged = await Render(cachedRender, text);
    if (Pdf(flagsChanged) == Pdf(changed)) throw new Exception("Compiler flag changes did not invalidate the cache.");
    File.Delete((string)flagsChanged.GetType().GetProperty("PngPath")!.GetValue(flagsChanged)!);
    if (Pdf(await Render(cachedRender, text)) == Pdf(flagsChanged)) throw new Exception("Missing cached pixels were reused.");
    Console.WriteLine("Preview cache: reuse, included-file invalidation, DPI/flags, missing pixels and explicit rebuild checks passed.");
    return;
}
if (args.Contains("--preview-benchmark"))
{
    var previewRender = args.Contains("--uncached") ? render : renderer.GetMethod("RenderCachedAsync") ?? render;
    foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
    {
        var times = new List<double>();
        foreach (var width in new[] { 4, 5, 4, 5 })
        {
            var sourceText = $"\\begin{{tikzpicture}}\n\\draw[blue,thick] (0,0) rectangle ({width},2);\n\\node at (2,1) {{$E=mc^2$}};\n\\end{{tikzpicture}}";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var task = (Task)previewRender.Invoke(null, [sourceText, engine, null, null, 192d, CancellationToken.None, new TikzEdt.Modern.UserSettings()])!;
            await task;
            var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
            if ((int)result.GetType().GetProperty("ExitCode")!.GetValue(result)! != 0)
                throw new Exception((string)result.GetType().GetProperty("Log")!.GetValue(result)!);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine($"{previewRender.Name} {engine}: new/edited/revisit/revisit ms = {string.Join(", ", times.Select(value => value.ToString("0.0")))}");
    }
    return;
}
if (args.Contains("--selection-regression"))
{
    Exception? failure = null;
    var selectionThread = new Thread(() =>
    {
        try
        {
            var assembly = renderer.Assembly;
            var modelType = assembly.GetType("TikzEdt.Modern.CanvasSelection")!;
            var keyType = assembly.GetType("TikzEdt.Modern.CanvasSelectionKey")!;
            var model = Activator.CreateInstance(modelType)!;
            var keys = modelType.GetProperty("Keys")!.GetValue(model)!;
            object Key(int offset, int length, string kind) => Activator.CreateInstance(keyType, [offset, length, kind])!;
            void Click(object key, bool toggle) => modelType.GetMethod("Click")!.Invoke(model, [key, toggle]);
            int Count() => (int)keys.GetType().GetProperty("Count")!.GetValue(keys)!;
            var first = Key(10, 50, "shape");
            var second = Key(100, 20, "node");
            Click(first, false); Click(second, true);
            if (Count() != 2) throw new Exception("Ctrl+click did not add a selection.");
            Click(first, false);
            if (Count() != 2) throw new Exception("Clicking a selected item lost the group needed for dragging.");
            Click(first, true);
            if (Count() != 1) throw new Exception("Ctrl+click did not toggle an item off.");
            Click(first, false);
            if (Count() != 1) throw new Exception("Plain click did not replace the selection.");
            Click(Key(20, 5, "point"), true); Click(second, true); Click(Key(100, 20, "point"), true);
            var outer = ((System.Collections.IEnumerable)modelType.GetMethod("Outermost")!.Invoke(null, [keys])!).Cast<object>().ToArray();
            if (outer.Length != 2) throw new Exception("Overlapping shape/point selections were not normalized.");
            Click(Key(25, 3, "point"), false);
            if (Count() != 4) throw new Exception("Clicking a selected shape's control point lost the group.");
            if (!(bool)modelType.GetMethod("Contains")!.Invoke(model, [Key(25, 3, "point")])!)
                throw new Exception("A selected shape's control point was not selected.");

            var markerType = assembly.GetType("TikzEdt.Modern.ControlMarker")!;
            var marker = (System.Windows.Controls.Canvas)Activator.CreateInstance(markerType, [first, "test"] )!;
            var update = markerType.GetMethod("Update")!;
            foreach (var zoom in new[] { 0.25, 1, 4, 16 })
            {
                update.Invoke(marker, [zoom, false, false]);
                var effective = marker.RenderTransform.TransformBounds(new System.Windows.Rect(0, 0, marker.Width, marker.Height));
                if (Math.Abs(effective.Width * zoom - 12) > 0.0001) throw new Exception("Marker scaled with zoom.");
            }
            var stroke = (System.Windows.Shapes.Line)marker.Children[2];
            var red = ((System.Windows.Media.SolidColorBrush)stroke.Stroke).Color;
            update.Invoke(marker, [1d, false, true]);
            var contrast = ((System.Windows.Media.SolidColorBrush)stroke.Stroke).Color;
            if (red == contrast || stroke.StrokeThickness >= 1 || marker.Cursor != System.Windows.Input.Cursors.Arrow)
                throw new Exception("Thin marker, adaptive color or arrow pointer regression.");
            update.Invoke(marker, [1d, true, false]);
            var selectedColor = ((System.Windows.Media.SolidColorBrush)stroke.Stroke).Color;
            update.Invoke(marker, [1d, true, true]);
            if (((System.Windows.Media.SolidColorBrush)stroke.Stroke).Color != selectedColor || selectedColor == red || stroke.StrokeThickness >= 1)
                throw new Exception("Selected markers must stay consistently blue over overlapping artwork, with a thin stroke.");

            var app = new TikzEdt.Modern.App(); app.InitializeComponent();
            var main = new TikzEdt.Modern.MainWindow();
            var editor = (ICSharpCode.AvalonEdit.TextEditor)main.FindName("Editor");
            var canvas = (System.Windows.Controls.Canvas)main.FindName("OverlayCanvas");
            var original = "\\begin{tikzpicture}\n\\node at (0.1,0.2) {A};\n\\node at (2.3,1.1) {B};\n\\end{tikzpicture}";
            editor.Text = original;
            var adapter = assembly.GetType("TikzEdt.Modern.LegacyTikzAdapter")!;
            var curveSource = "\\begin{tikzpicture}\n\\draw (0,0) .. controls (1,2) and (3,2) .. (4,0);\n\\draw (5,0) .. controls +(1,1) .. (7,0);\n\\end{tikzpicture}";
            var curveDiagram = adapter.GetMethod("Parse")!.Invoke(null, [curveSource])!;
            var curvePoints = ((System.Collections.IEnumerable)curveDiagram.GetType().GetProperty("Points")!.GetValue(curveDiagram)!).Cast<object>().ToArray();
            var controlCount = 0;
            foreach (var point in curvePoints)
            {
                var isControl = (bool)point.GetType().GetProperty("IsBezierControl")!.GetValue(point)!;
                var curveMarker = (System.Windows.Controls.Canvas)Activator.CreateInstance(markerType, [point, "curve marker"])!;
                if (isControl)
                {
                    controlCount++;
                    if (curveMarker.Children.OfType<System.Windows.Shapes.Ellipse>().Count() != 2 || curveMarker.Children.OfType<System.Windows.Shapes.Line>().Any())
                        throw new Exception("Bézier control was not drawn as a circle.");
                }
                else if (curveMarker.Children.OfType<System.Windows.Shapes.Line>().Count() != 4)
                    throw new Exception("Curve endpoint did not retain its X marker.");
                foreach (var zoom in new[] { 0.25, 1, 4, 16 })
                {
                    update.Invoke(curveMarker, [zoom, true, true]);
                    var size = curveMarker.RenderTransform.TransformBounds(new System.Windows.Rect(0, 0, curveMarker.Width, curveMarker.Height));
                    if (Math.Abs(size.Width * zoom - 12) > 0.0001) throw new Exception("Circular control marker scaled with zoom.");
                }
            }
            if (curvePoints.Length != 7 || controlCount != 3) throw new Exception("Cubic and single-control curve points were not classified correctly.");
            Console.WriteLine("Circular Bézier controls, X endpoints, single/dual controls and zoom-independent size passed.");
            var diagram = adapter.GetMethod("Parse")!.Invoke(null, [original])!;
            var points = ((System.Collections.IEnumerable)diagram.GetType().GetProperty("Points")!.GetValue(diagram)!).Cast<object>().ToArray();
            if (points.Length != 2) throw new Exception("Expected two editable node points in fixture.");
            var selection = typeof(TikzEdt.Modern.MainWindow).GetField("_canvasSelection", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            var selectionKeys = modelType.GetProperty("Keys")!.GetValue(selection)!;
            var regionMethod = typeof(TikzEdt.Modern.MainWindow).GetMethod("SelectCanvasRegion", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var markers = points.Select(point => (System.Windows.Controls.Canvas)Activator.CreateInstance(markerType, [point, "test coordinate"])!).ToArray();
            for (var index = 0; index < markers.Length; index++)
            {
                // Deliberately leave these unmeasured, as during a fresh overlay rebuild.
                System.Windows.Controls.Canvas.SetLeft(markers[index], 100 + index * 100 - markers[index].Width / 2);
                System.Windows.Controls.Canvas.SetTop(markers[index], 100 - markers[index].Height / 2);
                canvas.Children.Add(markers[index]);
            }
            foreach (var zoom in new[] { 0.25, 1, 4, 16 })
            {
                foreach (var pointMarker in markers) update.Invoke(pointMarker, [zoom, false, false]);
                regionMethod.Invoke(main, [new System.Windows.Rect(99, 99, 2, 2)]);
                if ((int)selectionKeys.GetType().GetProperty("Count")!.GetValue(selectionKeys)! != 1)
                    throw new Exception("A small selection box did not select exactly the enclosed coordinate at zoom " + zoom);
                var offset = (int)points[0].GetType().GetProperty("Offset")!.GetValue(points[0])!;
                var length = (int)points[0].GetType().GetProperty("Length")!.GetValue(points[0])!;
                if (!(bool)modelType.GetMethod("Contains")!.Invoke(selection, [Key(offset, length, "point")])!)
                    throw new Exception("Selection chose the wrong control point.");
                regionMethod.Invoke(main, [new System.Windows.Rect(90, 90, 120, 20)]);
                if ((int)selectionKeys.GetType().GetProperty("Count")!.GetValue(selectionKeys)! != 2)
                    throw new Exception("Selection did not include every enclosed control point.");
                regionMethod.Invoke(main, [new System.Windows.Rect(300, 300, 10, 10)]);
                if ((int)selectionKeys.GetType().GetProperty("Count")!.GetValue(selectionKeys)! != 0)
                    throw new Exception("Shrinking a selection retained points outside the box.");
            }
            // Different paths can have distinct source coordinates at exactly the same canvas position.
            System.Windows.Controls.Canvas.SetLeft(markers[1], System.Windows.Controls.Canvas.GetLeft(markers[0]));
            regionMethod.Invoke(main, [new System.Windows.Rect(99, 99, 2, 2)]);
            if ((int)selectionKeys.GetType().GetProperty("Count")!.GetValue(selectionKeys)! != 2)
                throw new Exception("Overlapping control points were not all selected.");
            typeof(TikzEdt.Modern.MainWindow).GetMethod("UpdateControlMarkers", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
            foreach (var pointMarker in markers)
                if (((System.Windows.Media.SolidColorBrush)((System.Windows.Shapes.Line)pointMarker.Children[2]).Stroke).Color != selectedColor)
                    throw new Exception("An enclosed overlapping marker was not visibly selected.");
            canvas.Children.Clear();
            selectionKeys.GetType().GetMethod("Clear")!.Invoke(selectionKeys, null);
            foreach (var point in points)
            {
                var offset = (int)point.GetType().GetProperty("Offset")!.GetValue(point)!;
                var length = (int)point.GetType().GetProperty("Length")!.GetValue(point)!;
                canvas.Children.Add(new System.Windows.Controls.Border { Tag = point });
                modelType.GetMethod("Click")!.Invoke(selection, [Key(offset, length, "point"), true]);
            }
            var nodeType = typeof(TikzEdt.Modern.MainWindow).GetNestedType("NodeVisual", BindingFlags.NonPublic)!;
            var nodeOffset = original.IndexOf("\\node", StringComparison.Ordinal);
            var nodeEnd = original.IndexOf(';', nodeOffset) + 1;
            var nodeTag = Activator.CreateInstance(nodeType, [nodeOffset, nodeEnd - nodeOffset, "\\node at ", " {A};", 0.1d, 0.2d])!;
            var nodeBody = new System.Windows.Controls.Border { Tag = nodeTag };
            canvas.Children.Add(nodeBody);
            typeof(TikzEdt.Modern.MainWindow).GetField("_selectionSource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, editor.Text);
            var mouseDown = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
                { RoutedEvent = System.Windows.UIElement.MouseLeftButtonDownEvent };
            typeof(TikzEdt.Modern.MainWindow).GetMethod("BeginSelectionDrag", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [nodeBody, nodeTag, new System.Windows.Point(100, 100), mouseDown]);
            if ((int)selectionKeys.GetType().GetProperty("Count")!.GetValue(selectionKeys)! != 2)
                throw new Exception("Dragging a node body discarded the other selected coordinates.");
            var transforms = (System.Collections.IDictionary)typeof(TikzEdt.Modern.MainWindow)
                .GetField("_selectionTransforms", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            if (transforms.Count != 3 || !transforms.Contains(nodeBody))
                throw new Exception("The drag preview did not include both coordinates and the node body.");
            typeof(TikzEdt.Modern.MainWindow).GetMethod("CancelCanvasInteraction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
            typeof(TikzEdt.Modern.MainWindow).GetMethod("MoveCanvasSelection", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [new System.Windows.Vector(1, -2)]);
            var movedDiagram = adapter.GetMethod("Parse")!.Invoke(null, [editor.Text])!;
            var movedPoints = ((System.Collections.IEnumerable)movedDiagram.GetType().GetProperty("Points")!.GetValue(movedDiagram)!).Cast<object>().ToArray();
            for (var index = 0; index < points.Length; index++)
            {
                var before = (System.Windows.Point)points[index].GetType().GetProperty("Position")!.GetValue(points[index])!;
                var after = (System.Windows.Point)movedPoints[index].GetType().GetProperty("Position")!.GetValue(movedPoints[index])!;
                if ((after - before - new System.Windows.Vector(1, 2)).Length > 0.0001) throw new Exception("Group movement changed spacing or moved the wrong coordinates.");
            }
            editor.Undo();
            if (editor.Text != original) throw new Exception("Group movement was not one undo operation.");
            var mixedSource = "\\begin{tikzpicture}\n\\draw (0.1,0.2) -- ++(1,0) -- ++(0,1);\n\\node at (30:2) {Polar};\n\\node at (8,8) {Stay};\n\\end{tikzpicture}";
            editor.Text = mixedSource;
            var mixedDiagram = adapter.GetMethod("Parse")!.Invoke(null, [mixedSource])!;
            var mixedPoints = ((System.Collections.IEnumerable)mixedDiagram.GetType().GetProperty("Points")!.GetValue(mixedDiagram)!).Cast<object>().ToArray();
            if (mixedPoints.Length != 5) throw new Exception("Expected Cartesian, relative, polar and unselected coordinates.");
            canvas.Children.Clear();
            selectionKeys.GetType().GetMethod("Clear")!.Invoke(selectionKeys, null);
            foreach (var point in mixedPoints.Take(4))
            {
                canvas.Children.Add(new System.Windows.Controls.Border { Tag = point });
                modelType.GetMethod("Click")!.Invoke(selection, [Key((int)point.GetType().GetProperty("Offset")!.GetValue(point)!,
                    (int)point.GetType().GetProperty("Length")!.GetValue(point)!, "point"), true]);
            }
            var mainType = typeof(TikzEdt.Modern.MainWindow);
            if (main.FindName("BuildSnippetPreviewsMenu") is not System.Windows.Controls.MenuItem { IsEnabled: true } ||
                main.FindName("PauseSnippetBuildMenu") is not System.Windows.Controls.MenuItem { IsEnabled: false } ||
                main.FindName("SnippetBuildPanel") is not System.Windows.Controls.Border { Visibility: System.Windows.Visibility.Collapsed })
                throw new Exception("Snippet build menu and progress controls are missing or incorrectly initialized.");
            var shapeType = mainType.GetNestedType("ShapeVisual", BindingFlags.NonPublic)!;
            var shapeKindType = mainType.GetNestedType("ShapeKind", BindingFlags.NonPublic)!;
            var drawOffset = mixedSource.IndexOf("\\draw", StringComparison.Ordinal);
            var drawLength = mixedSource.IndexOf(';', drawOffset) + 1 - drawOffset;
            var shapeTag = Activator.CreateInstance(shapeType, [drawOffset, drawLength, Enum.Parse(shapeKindType, "Path"), "", 0.1d, 0.2d, 1.1d, 0.2d])!;
            var handleType = mainType.GetNestedType("HandleVisual", BindingFlags.NonPublic)!;
            var handleTag = Activator.CreateInstance(handleType, [shapeTag, 0])!;
            var groupHandle = (System.Windows.Controls.Canvas)Activator.CreateInstance(markerType, [handleTag, "group grip"])!;
            System.Windows.Controls.Canvas.SetLeft(groupHandle, 100);
            System.Windows.Controls.Canvas.SetTop(groupHandle, 100);
            canvas.Children.Add(groupHandle);
            mainType.GetField("_selectionSource", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, editor.Text);
            mainType.GetMethod("Handle_MouseLeftButtonDown", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [groupHandle, mouseDown]);
            if (!ReferenceEquals(mainType.GetField("_selectionDragElement", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main), groupHandle) ||
                mainType.GetField("_draggedHandle", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main) is not null)
                throw new Exception("Dragging a marker in a multi-selection started an individual resize instead of group translation.");
            mainType.GetMethod("CancelCanvasInteraction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
            typeof(TikzEdt.Modern.MainWindow).GetMethod("MoveCanvasSelection", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(main, [new System.Windows.Vector(0.7, -1.3)]);
            var afterMixedDiagram = adapter.GetMethod("Parse")!.Invoke(null, [editor.Text])!;
            var afterMixedPoints = ((System.Collections.IEnumerable)afterMixedDiagram.GetType().GetProperty("Points")!.GetValue(afterMixedDiagram)!).Cast<object>().ToArray();
            for (var index = 0; index < mixedPoints.Length; index++)
            {
                var before = (System.Windows.Point)mixedPoints[index].GetType().GetProperty("Position")!.GetValue(mixedPoints[index])!;
                var after = (System.Windows.Point)afterMixedPoints[index].GetType().GetProperty("Position")!.GetValue(afterMixedPoints[index])!;
                var expected = index < 4 ? new System.Windows.Vector(0.7, 1.3) : default;
                if ((after - before - expected).Length > 0.002)
                    throw new Exception($"Mixed coordinate {index} moved from {before} to {after}, expected delta {expected}. Source: {editor.Text}");
            }
            editor.Undo();
            if (editor.Text != mixedSource) throw new Exception("Mixed coordinate group movement was not one undo operation.");
            app.Shutdown();
            Console.WriteLine("Box selection of unmeasured control points at 0.25x–16x, selected-owner membership, multi-selection toggles, group movement/undo and marker styling passed.");
        }
        catch (Exception exception) { failure = exception; }
    });
    selectionThread.SetApartmentState(ApartmentState.STA);
    selectionThread.Start(); selectionThread.Join();
    if (failure is not null) throw failure;
    return;
}
if (args.Contains("--drag-regression"))
{
    var gestureType = renderer.Assembly.GetType("TikzEdt.Modern.ControlDragGesture")!;
    var gesture = Activator.CreateInstance(gestureType)!;
    var begin = gestureType.GetMethod("Begin")!;
    var move = gestureType.GetMethod("TryMove")!;
    var viewport = new System.Windows.Point(100, 100);
    var pointer = new System.Windows.Point(3003.4, 2997.7);
    var center = new System.Windows.Point(3000.7, 2995.2); // deliberately off-grid and off-center grab
    begin.Invoke(gesture, [viewport, pointer, center]);
    object[] click = [viewport, pointer, default(System.Windows.Point)];
    if ((bool)move.Invoke(gesture, click)!) throw new Exception("A stationary click started a drag.");
    var threshold = System.Windows.SystemParameters.MinimumHorizontalDragDistance;
    object[] jitter = [new System.Windows.Point(100 + threshold / 2, 100), new System.Windows.Point(pointer.X + 0.3, pointer.Y), default(System.Windows.Point)];
    if ((bool)move.Invoke(gesture, jitter)!) throw new Exception("Minor pointer jitter started a drag.");
    object[] dragged = [new System.Windows.Point(100 + threshold + 1, 100), new System.Windows.Point(pointer.X + 10, pointer.Y), default(System.Windows.Point)];
    if (!(bool)move.Invoke(gesture, dragged)!) throw new Exception("An intentional drag was ignored.");
    var resultingCenter = (System.Windows.Point)dragged[2];
    if (Math.Abs(resultingCenter.X - center.X - 10) > 0.00001 || Math.Abs(resultingCenter.Y - center.Y) > 0.00001)
        throw new Exception("Dragging lost the original pointer-to-control offset.");
    begin.Invoke(gesture, [viewport, pointer, center]);
    if ((bool)move.Invoke(gesture, click)!) throw new Exception("A new click reused the previous drag state.");
    Console.WriteLine("Stationary click, jitter, intentional drag, off-center grab and gesture reset passed.");
    return;
}
if (args.Contains("--ui-construction"))
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            var app = new TikzEdt.Modern.App();
            app.InitializeComponent();
            var main = new TikzEdt.Modern.MainWindow();
            main.Measure(new System.Windows.Size(1440, 900));
            main.Arrange(new System.Windows.Rect(0, 0, 1440, 900));
            main.UpdateLayout();
            foreach (var toolName in new[] { "SelectTool", "EditTool", "NodeTool", "PathTool", "BezierTool", "SmoothTool", "RectangleTool", "EllipseTool", "GridTool", "ArcTool", "PieTool" })
                if (main.FindName(toolName) is not System.Windows.Controls.RadioButton { Content: TikzEdt.Modern.IconLabel })
                    throw new Exception("Missing icon drawing tool: " + toolName);
            var mainType = typeof(TikzEdt.Modern.MainWindow);
            var canvasEditor = (ICSharpCode.AvalonEdit.TextEditor)main.FindName("Editor");
            if (main.Icon is null) throw new Exception("Application icon was not loaded.");
            var iconDecoder = new System.Windows.Media.Imaging.IconBitmapDecoder(new Uri("pack://application:,,,/TikzEdt.Modern;component/Assets/Branding/TikzEdt.ico"), System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            if (iconDecoder.Frames.Select(frame => frame.PixelWidth).Distinct().Count() != 10) throw new Exception("Application icon is missing DPI resolutions.");
            if (canvasEditor.ContextMenu?.Items.OfType<System.Windows.Controls.MenuItem>().Any(item => item.Header?.ToString() == "Format Code") != true)
                throw new Exception("Editor context menu did not include Format Code.");
            var unformatted = "\\begin{tikzpicture}\n\\begin{scope}\n\\draw (0,0) -- (1,1);\n\\end{scope}\n\\end{tikzpicture}";
            canvasEditor.Text = unformatted;
            canvasEditor.Select(0, 0);
            mainType.GetMethod("FormatCode_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [main, new System.Windows.RoutedEventArgs()]);
            if (!canvasEditor.Text.Contains("        \\draw")) throw new Exception("Format Code did not format the document.");
            canvasEditor.Undo();
            if (canvasEditor.Text != unformatted) throw new Exception("Formatting was not one undo operation.");
            var selectedLine = canvasEditor.Document.GetLineByNumber(3);
            canvasEditor.Select(selectedLine.Offset, selectedLine.Length);
            mainType.GetMethod("FormatCode_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [main, new System.Windows.RoutedEventArgs()]);
            if (!canvasEditor.Text.Contains("        \\draw") || canvasEditor.Document.GetText(canvasEditor.Document.GetLineByNumber(2)).StartsWith(' '))
                throw new Exception("Format selection changed lines outside the selected range.");
            canvasEditor.Undo();
            if (canvasEditor.Text != unformatted) throw new Exception("Selection formatting was not undoable.");
            Console.WriteLine("Ten icon resolutions, window logo, context menu, whole-document/selection formatting and undo passed.");
            var emptyPicture = "\\begin{tikzpicture}\n\\end{tikzpicture}";
            foreach (var toolName in new[] { "BezierTool", "SmoothTool", "GridTool", "ArcTool", "PieTool" })
            {
                canvasEditor.Text = emptyPicture;
                ((System.Windows.Controls.RadioButton)main.FindName(toolName)).IsChecked = true;
                System.Windows.Point[] clicks = toolName switch
                {
                    "BezierTool" => [new(3000, 3000), new(3050, 2900), new(3150, 3050), new(3200, 2950)],
                    "SmoothTool" => [new(3000, 3000), new(3050, 2900), new(3100, 2950), new(3200, 2850)],
                    "GridTool" => [new(3000, 3000), new(3150, 2900)],
                    _ => [new(3000, 3000), new(3100, 3000), new(3000, 2850)]
                };
                foreach (var click in clicks) mainType.GetMethod("PlaceAdvancedPoint", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [click, false]);
                if (toolName == "SmoothTool") mainType.GetMethod("FinishAdvancedDrawing", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
                if (!canvasEditor.Text.Contains("\\draw") || ((System.Windows.Controls.RadioButton)main.FindName("SelectTool")).IsChecked != true)
                    throw new Exception(toolName + " did not finish a real drawing and return to selection.");
                canvasEditor.Undo();
                if (canvasEditor.Text != emptyPicture) throw new Exception(toolName + " was not inserted in a single undo operation.");
            }
            ((System.Windows.Controls.RadioButton)main.FindName("BezierTool")).IsChecked = true;
            mainType.GetMethod("PlaceAdvancedPoint", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [new System.Windows.Point(3000, 3000), false]);
            mainType.GetMethod("CancelAdvancedDrawing", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
            if (canvasEditor.Text != emptyPicture) throw new Exception("Cancelling an unfinished drawing changed source.");
            ((System.Windows.Controls.RadioButton)main.FindName("SelectTool")).IsChecked = true;
            canvasEditor.Text = "\\begin{tikzpicture}\n\\draw (0,0) rectangle (4,2);\n\\end{tikzpicture}";
            Console.WriteLine("Multi-click tool creation, smooth completion, cancellation and single-step undo passed.");
            mainType.GetMethod("RefreshSidebarFiles", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, null);
            if (((System.Windows.Controls.ListBox)main.FindName("SidebarFiles")).Items.Count == 0)
                throw new Exception("Files sidebar did not list the workspace.");
            var snippets = (System.Windows.Controls.ListBox)main.FindName("SidebarSnippets");
            var snippetLibrary = renderer.Assembly.GetType("TikzEdt.Modern.SnippetLibrary")!.GetMethod("Load")!.Invoke(null, [null])!;
            mainType.GetMethod("SetSidebarSnippetItems", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [snippetLibrary]);
            var snippetCount = snippets.Items.Count;
            var allSnippets = ((System.Collections.IEnumerable)snippetLibrary).Cast<object>().ToArray();
            string Category(object snippet) => (string)snippet.GetType().GetProperty("Category")!.GetValue(snippet)!;
            var snippetView = System.Windows.Data.CollectionViewSource.GetDefaultView(snippets.ItemsSource);
            if (snippetView.Groups?.Count != allSnippets.Select(Category).Distinct().Count())
                throw new Exception("Accordion did not include all categories.");
            foreach (System.Windows.Data.CollectionViewGroup category in snippetView.Groups!)
            {
                if (category.ItemCount != allSnippets.Count(item => Category(item) == category.Name.ToString()) ||
                    category.Items.Cast<object>().Any(item => Category(item) != category.Name.ToString()))
                    throw new Exception("Accordion category contains incorrect snippets.");
            }
            ((System.Windows.Controls.TextBox)main.FindName("SidebarSnippetSearch")).Text = "ground";
            if (snippets.Items.Count == 0 || snippets.Items.Count >= snippetCount) throw new Exception("Sidebar snippet search did not filter entries.");
            ((System.Windows.Controls.TextBox)main.FindName("SidebarSnippetSearch")).Text = "__no_such_snippet__";
            if (snippets.Items.Count != 0 || ((System.Windows.Controls.Button)main.FindName("SidebarInsertCodeButton")).IsEnabled ||
                ((System.Windows.Controls.Button)main.FindName("SidebarInsertExampleButton")).IsEnabled)
                throw new Exception("Empty category/search results retained stale insertion actions.");
            ((System.Windows.Controls.TextBox)main.FindName("SidebarSnippetSearch")).Text = "";
            if (snippets.Items.Count != snippetCount) throw new Exception("All categories did not restore the full library.");
            Console.WriteLine($"All {snippetView.Groups!.Count} accordion groups, counts, search and empty-result actions passed.");
            var tooltip = new System.Windows.Controls.ToolTip { Content = "Readable diagnostic and toolbar tooltip", Style = (System.Windows.Style)app.FindResource(typeof(System.Windows.Controls.ToolTip)) };
            tooltip.ApplyTemplate();
            var tipBackground = ((System.Windows.Media.SolidColorBrush)tooltip.Background).Color;
            var tipForeground = ((System.Windows.Media.SolidColorBrush)tooltip.Foreground).Color;
            if (tipBackground.R > 90 || tipForeground.R < 180 || tooltip.Template is null) throw new Exception("Tooltip dark background / bright text regression.");
            mainType.GetMethod("FoldAll_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [main, new System.Windows.RoutedEventArgs()]);
            var foldingManager = mainType.GetField("_foldingManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            var folds = ((System.Collections.IEnumerable)foldingManager.GetType().GetProperty("AllFoldings")!.GetValue(foldingManager)!).Cast<object>().ToArray();
            if (folds.Length == 0 || folds.Any(fold => !(bool)fold.GetType().GetProperty("IsFolded")!.GetValue(fold)!)) throw new Exception("Fold all was not connected to the editor.");
            mainType.GetMethod("UnfoldAll_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [main, new System.Windows.RoutedEventArgs()]);
            if (folds.Any(fold => (bool)fold.GetType().GetProperty("IsFolded")!.GetValue(fold)!)) throw new Exception("Unfold all was not connected to the editor.");
            Console.WriteLine("Icon tools, file listing, searchable sidebar snippets and editor fold/unfold commands passed.");
            var coordinates = (System.Windows.Controls.TextBlock)main.FindName("MousePositionText");
            var gridKind = (System.Windows.Controls.TextBlock)main.FindName("GridKindText");
            var layoutRoot = (System.Windows.FrameworkElement)main.Content;
            // No HWND is created in this test. Detach the root so an unshown
            // Window's deferred measure cannot replace the fixed viewport size.
            main.Content = null;
            layoutRoot.Width = 1440;
            layoutRoot.Height = 900;
            if (args.Contains("--snippet-snapshot"))
            {
                ((System.Windows.Controls.TabControl)main.FindName("SidebarTabs")).SelectedItem = main.FindName("SnippetsSidebarTab");
            }
            layoutRoot.Measure(new System.Windows.Size(1440, 900));
            layoutRoot.Arrange(new System.Windows.Rect(0, 0, 1440, 900));
            layoutRoot.UpdateLayout();
            var previewScroller = (System.Windows.Controls.ScrollViewer)main.FindName("GraphicalScroller");
            previewScroller.ApplyTemplate();
            if (previewScroller.Template.FindName("ScrollBarCorner", previewScroller) is not System.Windows.Controls.Border corner ||
                corner.Background.ToString() != "#FF25262A") throw new Exception("Preview scrollbar corner is not dark.");
            if (previewScroller.Template.FindName("PART_HorizontalScrollBar", previewScroller) is not System.Windows.Controls.Primitives.ScrollBar ||
                previewScroller.Template.FindName("PART_VerticalScrollBar", previewScroller) is not System.Windows.Controls.Primitives.ScrollBar)
                throw new Exception("Preview scrollbar template parts are missing.");
            IEnumerable<T> Visuals<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
            {
                for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
                    if (child is T match) yield return match;
                    foreach (var descendant in Visuals<T>(child)) yield return descendant;
                }
            }
            if (args.Contains("--snippet-snapshot"))
            {
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                var expanders = Visuals<System.Windows.Controls.Expander>(snippets).ToArray();
                if (expanders.Length < 2) throw new Exception("Accordion categories did not create expander controls.");
                expanders[0].IsExpanded = true;
                expanders[1].IsExpanded = true;
                if (expanders[0].IsExpanded || !expanders[1].IsExpanded) throw new Exception("Accordion did not close the previous category.");
                expanders[0].IsExpanded = true;
                layoutRoot.UpdateLayout();
                if (!Visuals<System.Windows.Controls.Border>(snippets).Any(border => border.Name == "SnippetThumbnail" && border.Height == 54 && border.ActualWidth == 76))
                    throw new Exception("Snippet entries did not include preview rectangles.");
                var thumbnail = Visuals<System.Windows.Controls.Border>(snippets).First(border => border.Name == "SnippetThumbnail");
                if (main.FindName("SidebarSnippetImage") is not null ||
                    ((System.Windows.Controls.Grid)snippets.Parent).RowDefinitions.Count != 4 || snippets.ActualHeight < 500)
                    throw new Exception("Removed bottom preview still reserves space in the snippet sidebar.");
                var entryGrid = (System.Windows.Controls.Grid)thumbnail.Parent;
                var labels = entryGrid.Children.OfType<System.Windows.Controls.StackPanel>().Single(panel => panel.Name == "SnippetLabels");
                if (System.Windows.Controls.Grid.GetColumn(labels) != 1 ||
                    labels.TransformToAncestor(entryGrid).Transform(new System.Windows.Point()).X < thumbnail.ActualWidth)
                    throw new Exception("Snippet labels are not positioned to the right of the thumbnail.");
            }
            var sidebarColumn = (System.Windows.Controls.ColumnDefinition)main.FindName("SidebarColumn");
            var sidebarHost = (System.Windows.FrameworkElement)main.FindName("SidebarHost");
            var expandedWidth = sidebarColumn.ActualWidth;
            mainType.GetMethod("SetSidebarVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [false]);
            layoutRoot.UpdateLayout();
            var rail = (System.Windows.FrameworkElement)main.FindName("SidebarRail");
            var chevron = (System.Windows.Shapes.Path)main.FindName("SidebarChevron");
            var collapsedChevron = chevron.Data.ToString();
            if (sidebarHost.Visibility != System.Windows.Visibility.Collapsed || sidebarColumn.ActualWidth != 30 || rail.Visibility != System.Windows.Visibility.Visible)
                throw new Exception("Sidebar did not retract to the vertical rail.");
            mainType.GetMethod("SetSidebarVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [true]);
            layoutRoot.UpdateLayout();
            if (sidebarHost.Visibility != System.Windows.Visibility.Visible || Math.Abs(sidebarColumn.ActualWidth - expandedWidth) > 1) throw new Exception("Sidebar did not restore its width.");
            if (chevron.Data.ToString() == collapsedChevron || rail.Visibility != System.Windows.Visibility.Collapsed) throw new Exception("Chevron direction / rail state did not change on expansion.");
            var thumb = main.FindName("SidebarResizeThumb");
            mainType.GetMethod("SidebarResize_Started", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [thumb, new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)]);
            mainType.GetMethod("SidebarResize_Delta", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [thumb, new System.Windows.Controls.Primitives.DragDeltaEventArgs(80, 0)]);
            mainType.GetMethod("SidebarResize_Completed", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [thumb, new System.Windows.Controls.Primitives.DragCompletedEventArgs(80, 0, false)]);
            layoutRoot.UpdateLayout();
            var resizedWidth = sidebarColumn.ActualWidth;
            if (Math.Abs(resizedWidth - expandedWidth - 80) > 1) throw new Exception("Edge dragging did not resize the sidebar.");
            mainType.GetMethod("SidebarResize_Started", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [thumb, new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0)]);
            mainType.GetMethod("SidebarResize_Delta", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [thumb, new System.Windows.Controls.Primitives.DragDeltaEventArgs(110 - resizedWidth, 0)]);
            mainType.GetMethod("SidebarResize_Completed", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [thumb, new System.Windows.Controls.Primitives.DragCompletedEventArgs(110 - resizedWidth, 0, false)]);
            layoutRoot.UpdateLayout();
            if (rail.Visibility != System.Windows.Visibility.Visible || sidebarColumn.ActualWidth != 30) throw new Exception("Dragging past the collapse threshold did not show the rail.");
            mainType.GetMethod("FilesSidebar_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [main, new System.Windows.RoutedEventArgs()]);
            layoutRoot.UpdateLayout();
            if (!((System.Windows.Controls.TabItem)main.FindName("FilesSidebarTab")).IsSelected || Math.Abs(sidebarColumn.ActualWidth - resizedWidth) > 1)
                throw new Exception("Files rail click did not reopen Files at the saved width.");
            mainType.GetMethod("SetSidebarVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [false]);
            mainType.GetMethod("Snippets_Click", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [main, new System.Windows.RoutedEventArgs()]);
            layoutRoot.UpdateLayout();
            if (!((System.Windows.Controls.TabItem)main.FindName("SnippetsSidebarTab")).IsSelected || sidebarHost.Visibility != System.Windows.Visibility.Visible)
                throw new Exception("Snippets rail click did not reopen Snippets.");
            if (args.Contains("--rail-snapshot"))
            {
                mainType.GetMethod("SetSidebarVisible", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, [false]);
                layoutRoot.UpdateLayout();
            }
            Console.WriteLine("Accordion, edge resize/collapse, vertical rail clicks, width restore, chevron direction and dark tooltip checks passed.");
            if (args.Contains("--ui-snapshot") || args.Contains("--snippet-snapshot") || args.Contains("--rail-snapshot"))
            {
                var visual = new System.Windows.Media.DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    context.DrawRectangle((System.Windows.Media.Brush)app.FindResource("WindowBrush"), null, new System.Windows.Rect(0, 0, 1440, 900));
                    context.DrawRectangle(new System.Windows.Media.VisualBrush(layoutRoot) { AutoLayoutContent = false }, null, new System.Windows.Rect(0, 0, 1440, 900));
                }
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1440, 900, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var output = File.Create(".codex-smoke/ui-workspace.png"); encoder.Save(output);
            }
            var footerControls = new[] { "ZoomSlider", "GridToggle", "GridStepSelector", "CoordinateSystemSelector" }
                .Select(name => (System.Windows.FrameworkElement)main.FindName(name)).ToArray();
            var footerPositions = footerControls.Select(control => control.TransformToAncestor(layoutRoot).Transform(new System.Windows.Point())).ToArray();
            foreach (var coordinateLabel in new[] { "Mouse —", "Snap  -60, 60", "Mouse  -99999.123456, 99999.123456" })
            {
                coordinates.Text = coordinateLabel;
                gridKind.Text = coordinateLabel == "Mouse —" ? "Polar" : "Cartesian";
                layoutRoot.UpdateLayout();
                for (var index = 0; index < footerControls.Length; index++)
                    if ((footerControls[index].TransformToAncestor(layoutRoot).Transform(new System.Windows.Point()) - footerPositions[index]).Length > 0.01)
                        throw new Exception($"Coordinate text moved {footerControls[index].Name}: {footerPositions[index]} -> {footerControls[index].TransformToAncestor(layoutRoot).Transform(new System.Windows.Point())}.");
            }
            Console.WriteLine("Mouse coordinates and Cartesian/Polar labels keep adjacent footer controls fixed.");
            if (main.FindName("CoordinatePanel") is not null) throw new Exception("Old coordinate row remains.");
            if (main.FindName("ZoomSlider") is not System.Windows.Controls.Slider) throw new Exception("Bottom zoom control missing.");
            if (main.FindName("CoordinateSystemSelector") is not System.Windows.Controls.ComboBox selector || selector.SelectedItem is null)
                throw new Exception("Coordinate selector binding failed.");
            var settingsWindow = new TikzEdt.Modern.SettingsDialog();
            settingsWindow.Measure(new System.Windows.Size(820, 740));
            settingsWindow.Arrange(new System.Windows.Rect(0, 0, 820, 740));
            settingsWindow.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (settingsWindow.FindName("StyleSelector") is not System.Windows.Controls.ComboBox style || style.SelectedItem is null)
                throw new Exception("Editor style binding failed.");
            var fontSelector = (System.Windows.Controls.ComboBox)settingsWindow.FindName("FontSelector");
            var fontSample = (ICSharpCode.AvalonEdit.TextEditor)settingsWindow.FindName("FontPreview");
            var fontSizeField = (System.Windows.Controls.TextBox)settingsWindow.FindName("FontSizeField");
            var draft = (TikzEdt.Modern.UserSettings)settingsWindow.DataContext;
            if (fontSelector.SelectedItem is null || fontSelector.Items.Count < System.Windows.Media.Fonts.SystemFontFamilies.Count)
                throw new Exception("Installed font list or saved font selection is missing.");
            var alternative = fontSelector.Items.Cast<System.Windows.Media.FontFamily>().First(font => font.Source != draft.EditorFont);
            fontSelector.SelectedItem = alternative;
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (draft.EditorFont != alternative.Source || fontSample.FontFamily.Source != alternative.Source)
                throw new Exception("Font selection did not update the settings draft and live editor preview.");
            fontSizeField.Text = 19.5.ToString(System.Globalization.CultureInfo.CurrentCulture);
            if (Math.Abs(fontSample.FontSize - 19.5) > 0.001) throw new Exception("Font size did not update the live preview.");
            var fontRow = (System.Windows.Controls.Grid)fontSelector.ItemTemplate.LoadContent();
            fontRow.DataContext = alternative;
            fontRow.Measure(new System.Windows.Size(600, 40));
            fontRow.Arrange(new System.Windows.Rect(0, 0, 600, 40));
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (((System.Windows.Controls.TextBlock)fontRow.Children[0]).Text != alternative.Source ||
                ((System.Windows.Controls.TextBlock)fontRow.Children[1]).FontFamily.Source != alternative.Source)
                throw new Exception("Font dropdown row did not show a readable name and typeface sample.");
            typeof(TikzEdt.Modern.SettingsDialog).GetMethod("Defaults_Click", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(settingsWindow, [settingsWindow, new System.Windows.RoutedEventArgs()]);
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            if (fontSelector.SelectedValue?.ToString() != "Consolas" || fontSample.FontFamily.Source != "Consolas")
                throw new Exception("Restore defaults did not reset the font picker and preview.");
            Console.WriteLine($"Font list ({fontSelector.Items.Count} families), typeface samples, live font/size preview, draft binding and defaults passed.");
            void CheckSelectors(System.Windows.DependencyObject parent)
            {
                if (parent is System.Windows.Controls.ComboBox combo &&
                    combo.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty) is { } binding && combo.SelectedItem is null)
                    throw new Exception("Settings selector binding failed: " + binding.ParentBinding.Path.Path);
                foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<System.Windows.DependencyObject>())
                    CheckSelectors(child);
            }
            CheckSelectors(settingsWindow);
            var snippetsWindow = new TikzEdt.Modern.SnippetsDialog("pdflatex");
            snippetsWindow.Measure(new System.Windows.Size(1100, 780));
            snippetsWindow.Arrange(new System.Windows.Rect(0, 0, 1100, 780));
            snippetsWindow.UpdateLayout();
            var preambleWindow = new TikzEdt.Modern.PreambleDialog();
            Console.WriteLine("Main, settings, snippets and preamble windows constructed with application resources.");
            Console.WriteLine("Bottom zoom, coordinate selector and editor-style binding passed.");
            app.Shutdown();
        }
        catch (Exception exception) { failure = exception; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
    return;
}
if (args.Contains("--features"))
{
    var assembly = renderer.Assembly;
    var library = assembly.GetType("TikzEdt.Modern.SnippetLibrary")!;
    var items = ((System.Collections.IEnumerable)library.GetMethod("Load")!.Invoke(null, [null])!).Cast<object>().ToList();
    Console.WriteLine($"Loaded {items.Count} snippets.");
    if (items.Count < 1000) throw new Exception("Expected the legacy and full Circuitikz libraries.");
    var settings = new TikzEdt.Modern.UserSettings();
    settings.Validate();
    settings.CompilerFlags = "-synctex=1 -include-directory=\"C:\\a path\\tex\"";
    settings.Validate();
    settings.CompilerFlags = "-output-directory=wrong";
    try { settings.Validate(); throw new Exception("Conflicting output flag was accepted."); }
    catch (ArgumentException) { }
    settings = new TikzEdt.Modern.UserSettings();
    var snapshotType = assembly.GetType("TikzEdt.Modern.PreambleSnapshot")!;
    var snapshot = Activator.CreateInstance(snapshotType, [null, null]);
    var previewMethod = library.GetMethod("RenderPreviewAsync")!;
    foreach (var name in new[] { "R", "C", "L", "D", "npn (node)", "op amp (node)", "ground (node)", "nmos (node)", "american and port (node)", "transformer (node)", "dipchip (node)", "circ (node)" })
    {
        var item = items.FirstOrDefault(entry => (string)entry.GetType().GetProperty("Name")!.GetValue(entry)! == name);
        if (item is null) throw new Exception("Missing documented component: " + name);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var path = await (Task<string>)previewMethod.Invoke(null, [item, "pdflatex", snapshot, settings, timeout.Token])!;
        var written = File.GetLastWriteTimeUtc(path);
        var cached = await (Task<string>)previewMethod.Invoke(null, [item, "pdflatex", snapshot, settings, timeout.Token])!;
        if (path != cached || written != File.GetLastWriteTimeUtc(path)) throw new Exception("Preview cache was not reused.");
        Console.WriteLine($"{name}: rendered and cache reused.");
    }
    return;
}
if (args.Contains("--rtl"))
{
    var rtlPreamble = """
\usepackage{fontspec}
\usepackage{polyglossia}
\setmainlanguage{arabic}
\setotherlanguage{french}
\newfontfamily\arabicfont[Script=Arabic]{Ubuntu Arabic}
""";
    foreach (var rtlEngine in new[] { "lualatex", "xelatex" })
    foreach (var ltr in new[] { false, true })
    {
        var rtlSource = (ltr ? "\\ifdefined\\pagedir\\pagedir TLT\\fi\\ifdefined\\setLTR\\setLTR\\fi\n" : "") + """
\begin{tikzpicture}
\draw[blue,thick] (0,0) rectangle (4,2);
\node at (2,1) {TikzEdt Modern};
\node at (2,0.5) {\textarabic{مرحبا بالعالم}};
\end{tikzpicture}
""";
        var rtlTask = (Task)render.Invoke(null, [rtlSource, rtlEngine, null, rtlPreamble, 192d, CancellationToken.None, null])!;
        await rtlTask;
        var rtlResult = rtlTask.GetType().GetProperty("Result")!.GetValue(rtlTask)!;
        var png = rtlResult.GetType().GetProperty("PngPath")!.GetValue(rtlResult);
        Console.WriteLine($"{rtlEngine} LTR={ltr}: {png}");
        var pdf = (string)rtlResult.GetType().GetProperty("PdfPath")!.GetValue(rtlResult)!;
        Console.WriteLine("PDF: " + pdf);
        if ((int)rtlResult.GetType().GetProperty("ExitCode")!.GetValue(rtlResult)! != 0)
            throw new Exception((string)rtlResult.GetType().GetProperty("Log")!.GetValue(rtlResult)!);
    }
    return;
}
var source = "\\begin{tikzpicture}\n\\node[draw,fill=customteal!20] at (0,0) {\\diagramtext};\n\\end{tikzpicture}";
var preamble = "\\usepackage{amsmath,xcolor}\n\\usetikzlibrary{positioning}\n\\definecolor{customteal}{RGB}{0,140,140}\n\\newcommand{\\diagramtext}{Preamble works}";
var syntax = renderer.Assembly.GetType("TikzEdt.Modern.TikzSyntaxDiagnostics")!;
var analyze = syntax.GetMethod("Analyze")!;
foreach (var sample in new[] {
    "\\begin{tikzpicture}\n\\draw (0,0) -- (1,1); % comment\n\\end{tikzpicture}",
    "\\begin{tikzpicture}\n\\draw (0,0)\n -- (1,1);\n% trailing comment\n\\end{tikzpicture}",
    "\\begin{tikzpicture}\n\\node at (0,0) {text; more text};\n\\end{tikzpicture}" })
{
    var diagnostics = (System.Collections.IEnumerable)analyze.Invoke(null, [sample])!;
    if (diagnostics.Cast<object>().Any()) throw new Exception("Valid TikZ reported a diagnostic: " + sample);
}
Console.WriteLine("Syntax regression checks passed.");
foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
    var timer = System.Diagnostics.Stopwatch.StartNew();
    var task = (Task)render.Invoke(null, [source, engine, null, preamble, 192d, timeout.Token, null])!;
    await task;
    var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
    var exitCode = (int)result.GetType().GetProperty("ExitCode")!.GetValue(result)!;
    var pngPath = (string)result.GetType().GetProperty("PngPath")!.GetValue(result)!;
    if (exitCode != 0 || !File.Exists(pngPath))
        throw new Exception(engine + ": " + result.GetType().GetProperty("Log")!.GetValue(result));
    Console.WriteLine($"{engine}: custom preamble rendered in {timer.ElapsedMilliseconds}ms");
    var svgType = renderer.Assembly.GetType("TikzEdt.Modern.SvgClipboard")!;
    var pdfPath = (string)result.GetType().GetProperty("PdfPath")!.GetValue(result)!;
    var svgTask = (Task<string>)svgType.GetMethod("ExportAsync")!.Invoke(null, [pdfPath, timeout.Token])!;
    var svgPath = await svgTask;
    var document = System.Xml.Linq.XDocument.Load(svgPath);
    if (document.Root?.Name.LocalName != "svg" || document.Descendants().Any(e => e.Name.LocalName == "image"))
        throw new Exception("Expected a vector SVG without raster images.");
    Console.WriteLine($"{engine}: vector SVG export passed.");
}
