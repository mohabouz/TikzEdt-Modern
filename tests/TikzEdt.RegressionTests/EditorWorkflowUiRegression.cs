using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;

internal static class EditorWorkflowUiRegression
{
    public static void Run()
    {
        Exception? error = null; var thread = new Thread(() => { try {
            var app = new App(); app.InitializeComponent(); var main = new MainWindow();
            var root = Path.GetFullPath("artifacts/editor-workflow-ui"); Directory.CreateDirectory(root);
            void Capture(Window window, string name, double width = 780, double height = 520)
            {
                var content = (FrameworkElement)window.Content; content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
                var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle((Brush)app.FindResource("WindowBrush"), null, new Rect(0, 0, width, height)); drawing.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height)); }
                var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(root, name + ".png")); encoder.Save(stream);
            }
            var snippets = new SnippetFileWindow(".tikzedt/snippets.json", UserSnippets.Example); Capture(snippets, "user-snippets");
            var history = new DocumentCopiesWindow("Version history", [new(Guid.NewGuid().ToString("N"), "figure.tex", "\\begin{tikzpicture}\n\\end{tikzpicture}", DateTime.UtcNow, "before-save")]); Capture(history, "history");
            var doctor = new ToolchainDoctorWindow(new UserSettings()); Capture(doctor, "toolchain-doctor");
            var palette = new CommandPaletteWindow(main.CreateActions(), root, _ => { }, false); Capture(palette, "command-palette");
            var paletteList = (ListBox)typeof(CommandPaletteWindow).GetField("_list", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(palette)!;
            if (paletteList.Background is not SolidColorBrush brush || brush.Color.R > 80 || paletteList.ItemContainerStyle is null) throw new Exception("Workflow list fell back to an unreadable light theme.");
            var index = new LatexSymbolsPanel(); index.Measure(new Size(260, 700)); index.Arrange(new Rect(0, 0, 260, 700)); index.UpdateLayout();
            var search = (TextBox)typeof(LatexSymbolsPanel).GetField("_search", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(index)!; search.Text = "real numbers";
            var items = (WrapPanel)typeof(LatexSymbolsPanel).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(index)!;
            if (!items.Children.OfType<Button>().Any(b => ((TextBlock)b.Content).Text == "ℝ")) throw new Exception("Meaning search did not reach the symbol UI.");
            var image = (Image)main.FindName("CompiledPreviewImage"); image.Width = 400; image.Height = 200; Canvas.SetLeft(image, 2900); Canvas.SetTop(image, 2800);
            var group = new DrawingGroup(); group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 960, 480)))); group.Freeze(); var vector = new DrawingImage(group); vector.Freeze();
            typeof(MainWindow).GetField("_compiledVector", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(main, vector);
            var mapped = main.PdfToCanvas(new Rect(360, 180, 72, 36));
            if (Math.Abs(mapped.X - 3100) > .001 || Math.Abs(mapped.Y - 2900) > .001 || Math.Abs(mapped.Width - 40) > .001) throw new Exception("SyncTeX page-to-WYSIWYG transform drifted.");
            snippets.Close(); history.Close(); doctor.Close(); palette.Close(); main.Close(); app.Shutdown();
            Console.WriteLine("New workflow dialogs laid out/rendered, meaning search reached the symbol UI, and PDF-point-to-canvas transform passed.");
        } catch (Exception e) { error = e; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error is not null) throw error;
    }
}
