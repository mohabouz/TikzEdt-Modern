using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ICSharpCode.AvalonEdit;
using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;

internal static class ImageAssetRegression
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        Exception? error = null; var thread = new Thread(() => { try {
            var app = new App(); app.InitializeComponent(); var root = Path.GetFullPath("artifacts/image-asset-regression/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8); var png = MainWindow.EncodeAsset(bitmap);
            var asset = ImageAssetStore.SavePng(Path.Combine(root, "figure.tex"), png); var next = ImageAssetStore.SavePng(Path.Combine(root, "figure.tex"), png);
            Check(asset.FullPath != next.FullPath && File.Exists(asset.FullPath) && !asset.RelativePath.Contains('\\'), "Collision-safe relative asset copy failed.");
            Check(MainWindow.DecodeAsset(asset.FullPath).Length > 8, "Copied asset failed to decode.");
            try { ImageAssetStore.SavePng(Path.Combine(root, "x.tex"), [0, 1]); throw new Exception("Invalid PNG accepted."); } catch (InvalidDataException) { }
            var main = new MainWindow(); var editor = (TextEditor)main.FindName("Editor"); var source = "\\begin{tikzpicture}\n\\end{tikzpicture}"; editor.Text = source;
            main.InsertImageReference(asset, new Point(1.5, -2)); Check(editor.Text.Contains("at (1.5,-2)") && editor.Text.Contains(asset.RelativePath) && editor.Text.StartsWith(@"\usepackage{graphicx}"), "Relative node/package insertion failed.");
            editor.Undo(); Check(editor.Text == source && File.Exists(asset.FullPath), "Undo lost source or deleted retained asset.");
            editor.Text = "\\documentclass{article}\n\\usepackage{graphicx}\n\\begin{document}\ntext\n\\end{document}"; var offset = editor.Text.IndexOf("text"); main.InsertImageReference(asset, sourceOffset: offset, selectionLength: 4);
            Check(editor.Text.Contains("\\begin{document}\n\\includegraphics") && editor.Text.Split(@"\usepackage{graphicx}").Length == 2, "Text insertion duplicated package or used wrong position.");
            main.Close(); app.Shutdown(); Console.WriteLine("B6: validated PNG normalization, unique relative assets, package/node/text insertion, one-step undo and retained assets passed.");
        } catch (Exception e) { error = e; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error is not null) throw error;
    }
    public static async Task IntegrationAsync()
    {
        var root = Path.GetFullPath("artifacts/image-asset-integration"); Directory.CreateDirectory(root);
        var fixture = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 255 }, 8); fixture.Freeze();
        var png = MainWindow.EncodeAsset(fixture);
        var asset = ImageAssetStore.SavePng(Path.Combine(root, "figure.tex"), png);
        var source = "\\usepackage{graphicx}\n\\begin{tikzpicture}\n\\node at (0,0) {\\includegraphics[width=2cm]{" + asset.RelativePath + "}};\n\\end{tikzpicture}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        { var result = await TexVectorRenderer.RenderAsync(source, engine, root, null, timeout.Token, new UserSettings { UsePrecompiledFormats = false }, false); Check(result.Image is not null, "Asset compilation failed: " + engine + result.Log); Console.WriteLine("B6 relative image compilation passed: " + engine); }
    }
}
