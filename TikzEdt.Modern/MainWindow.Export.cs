using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private async void ExportFigure_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export compiled figure", FileName = Path.GetFileNameWithoutExtension(_filePath ?? "figure"), DefaultExt = ".pdf",
            Filter = "PDF document (*.pdf)|*.pdf|SVG vector (*.svg)|*.svg|PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|TIFF image (*.tiff)|*.tiff|BMP image (*.bmp)|*.bmp",
            AddExtension = true, OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var source = Editor.Text;
            var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
            var preamble = PreambleSettings.ReadSnapshot();
            var key = PreviewKey(engine, preamble, source);
            var settings = SettingsStore.Current.Clone();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(settings.CompilerTimeoutSeconds));
            var pdf = _lastPreviewPath; var bounds = _lastPreviewBoundingBox;
            if (_lastPreviewKey != key || pdf is null || !File.Exists(pdf))
            {
                ModeStatus.Text = "Compiling current figure for export…";
                var workingDirectory = _filePath is null ? null : Path.GetDirectoryName(_filePath);
                var result = await Task.Run(() => TexPreviewRenderer.RenderAsync(source, engine, workingDirectory, preamble.Content, settings.PreviewDpi, cancellation.Token, settings));
                if (result.ExitCode != 0 || !File.Exists(result.PdfPath)) { OutputText.Text = result.Log; BottomTabs.SelectedItem = OutputTab; throw new InvalidOperationException("Fix the compilation errors before exporting."); }
                pdf = result.PdfPath; bounds = result.BoundingBox;
            }
            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            if (extension is not (".pdf" or ".svg" or ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp")) throw new InvalidOperationException("Choose PDF, SVG, PNG, JPEG, TIFF, or BMP.");
            string? prepared = extension == ".pdf" ? pdf : extension == ".svg" ? await SvgClipboard.ExportAsync(pdf, cancellation.Token) : null;
            BitmapSource? bitmap = null;
            if (prepared is null)
            {
                var raster = await TexPreviewRenderer.RasterizeAsync(pdf, bounds, settings.PreviewDpi, cancellation.Token);
                if (raster.ExitCode != 0 || !File.Exists(raster.PngPath)) throw new IOException("Image conversion failed: " + raster.Log);
                bitmap = SnippetLibrary.LoadPreview(raster.PngPath);
            }
            if (source != Editor.Text || key != PreviewKey((EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex", PreambleSettings.ReadSnapshot(), Editor.Text))
                throw new InvalidOperationException("The document or compiler changed during export. Export again to save the current figure.");
            if (prepared is not null) { if (!string.Equals(prepared, dialog.FileName, StringComparison.OrdinalIgnoreCase)) File.Copy(prepared, dialog.FileName, true); }
            else FigureImageExport.Save(bitmap!, dialog.FileName, extension);
            ModeStatus.Text = "Exported " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception exception) { ModeStatus.Text = "Export failed: " + exception.Message; }
    }
}

internal static class FigureImageExport
{
    public static void Save(BitmapSource source, string path, string extension)
    {
        BitmapEncoder encoder = extension switch
        {
            ".png" => new PngBitmapEncoder(), ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".tif" or ".tiff" => new TiffBitmapEncoder(), ".bmp" => new BmpBitmapEncoder(),
            _ => throw new ArgumentException("Unsupported raster export format.")
        };
        if (extension is ".jpg" or ".jpeg" or ".bmp")
        {
            var visual = new DrawingVisual();
            var dpiX = source.DpiX > 0 ? source.DpiX : 96; var dpiY = source.DpiY > 0 ? source.DpiY : 96;
            var bounds = new Rect(0, 0, source.PixelWidth * 96 / dpiX, source.PixelHeight * 96 / dpiY);
            using (var drawing = visual.RenderOpen()) { drawing.DrawRectangle(Brushes.White, null, bounds); drawing.DrawImage(source, bounds); }
            var white = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, dpiX, dpiY, PixelFormats.Pbgra32); white.Render(visual); source = white;
        }
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var file = File.Create(path); encoder.Save(file);
    }
}
