using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private bool _importingImage;
    private static readonly HashSet<string> RasterExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };
    private void InitializeImageAssets()
    {
        DataObject.AddPastingHandler(Editor, async (_, e) =>
        {
            // Plain source text retains normal paste behavior, even if HTML also has an image.
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText)) return;
            if (e.DataObject.GetDataPresent(DataFormats.Bitmap) && e.DataObject.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            { e.CancelCommand(); await ImportImageAsync(bitmap: bitmap, sourceOffset: Editor.SelectionStart, selectionLength: Editor.SelectionLength); }
            else if (DroppedRaster(e.DataObject) is { } path) { e.CancelCommand(); await ImportImageAsync(path, sourceOffset: Editor.SelectionStart, selectionLength: Editor.SelectionLength); }
        });
        foreach (var target in new UIElement[] { Editor, GraphicalSurface })
        {
            target.AllowDrop = true;
            target.PreviewDragOver += (_, e) => { if (DroppedRaster(e.Data) is null) return; e.Effects = DragDropEffects.Copy; e.Handled = true; };
            target.PreviewDrop += async (_, e) =>
            {
                if (DroppedRaster(e.Data) is not { } path) return; e.Handled = true;
                if (target == Editor)
                { var position = Editor.GetPositionFromPoint(e.GetPosition(Editor)); var offset = position is null ? Editor.CaretOffset : Editor.Document.GetOffset(position.Value.Location); await ImportImageAsync(path, sourceOffset: offset); }
                else
                {
                    Point local;
                    if (WysiwygMode.IsChecked == true) local = CanvasToCode(SnapPoint(e.GetPosition(OverlayCanvas)));
                    else if (!_lastPreviewBoundingBox.IsEmpty && CompiledPreviewImage.Width > 0 && CompiledPreviewImage.Height > 0)
                    { var p = e.GetPosition(CompiledPreviewImage); local = _coordinateFrame.ToLocal(new(_lastPreviewBoundingBox.Left + p.X / CompiledPreviewImage.Width * _lastPreviewBoundingBox.Width, _lastPreviewBoundingBox.Bottom - p.Y / CompiledPreviewImage.Height * _lastPreviewBoundingBox.Height)); }
                    else local = new(0, 0);
                    await ImportImageAsync(path, position: local);
                }
            };
        }
    }
    private static string? DroppedRaster(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop) || data.GetData(DataFormats.FileDrop) is not string[] { Length: 1 } paths) return null;
        return RasterExtensions.Contains(Path.GetExtension(paths[0])) ? paths[0] : null;
    }
    internal static byte[] EncodeAsset(BitmapSource bitmap)
    {
        if (bitmap.PixelWidth < 1 || bitmap.PixelHeight < 1 || (long)bitmap.PixelWidth * bitmap.PixelHeight > 25_000_000) throw new InvalidDataException("Image must have at most 25 million pixels.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var memory = new MemoryStream(); encoder.Save(memory);
        if (memory.Length > ImageAssetStore.MaximumBytes) throw new InvalidDataException("PNG image exceeds the 20 MB asset limit.");
        return memory.ToArray();
    }
    internal static byte[] DecodeAsset(string path)
    {
        if (!RasterExtensions.Contains(Path.GetExtension(path))) throw new InvalidDataException("Choose a PNG, JPEG, BMP or TIFF image.");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ImageAssetStore.MaximumBytes) throw new InvalidDataException("Image exceeds the 20 MB asset limit.");
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
        return EncodeAsset(decoder.Frames[0]);
    }
    internal void InsertImageReference(ImageAsset asset, Point? position = null, int? sourceOffset = null, int selectionLength = 0)
    {
        var reference = "\\includegraphics[width=4cm]{" + asset.RelativePath + "}";
        var needsPackage = !TexKnowledge.Build(Editor.Text, ReadIntelligencePreamble()).Packages.Contains("graphicx");
        using (Editor.Document.RunUpdate())
        {
            if (sourceOffset is { } offset) { Editor.Document.Replace(offset, selectionLength, reference); Editor.CaretOffset = offset + reference.Length; }
            else { var p = position ?? new Point(0, 0); InsertTikzCommand("    \\node[inner sep=0pt] at (" + FormatNumber(p.X) + "," + FormatNumber(p.Y) + ") {" + reference + "};\n"); }
            if (needsPackage) { var fix = TexSemantics.PreambleFix(Editor.Text, "\\usepackage{graphicx}", "Load graphicx"); Editor.Document.Insert(fix.Offset, fix.Replacement); }
        }
        ModeStatus.Text = "Image inserted: " + asset.RelativePath + " · Ctrl+Z undoes source; copied asset is retained";
    }
    private async Task ImportImageAsync(string? path = null, BitmapSource? bitmap = null, Point? position = null, int? sourceOffset = null, int selectionLength = 0)
    {
        if (_importingImage) { ModeStatus.Text = "An image import is already in progress."; return; }
        if (_filePath is null && !SaveDocument(false)) return;
        var source = Editor.Text; var documentPath = _filePath!; _importingImage = true;
        try
        {
            byte[] png;
            if (bitmap is not null) { var copy = bitmap.Clone(); copy.Freeze(); png = await Task.Run(() => EncodeAsset(copy)); }
            else png = await Task.Run(() => DecodeAsset(path!));
            var asset = await Task.Run(() => ImageAssetStore.SavePng(documentPath, png));
            if (_safetyClosed || _filePath != documentPath || Editor.Text != source) { ModeStatus.Text = "Document changed during import. Asset retained at " + asset.FullPath + "; insert it again when ready."; return; }
            InsertImageReference(asset, position, sourceOffset, selectionLength);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException or FileFormatException) { ModeStatus.Text = "Image import failed: " + error.Message; }
        finally { _importingImage = false; }
    }
    private async void ImportImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Raster images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await ImportImageAsync(dialog.FileName);
    }
}
