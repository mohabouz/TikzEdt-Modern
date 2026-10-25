using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TikzEdt.Modern.Core.Documents;
using TikzEdt.Modern.Core.Toolchain;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private string? _syncSource;
    private SyncTexLineMap? _syncLines;
    private CancellationTokenSource? _syncJob;
    private readonly System.Windows.Shapes.Rectangle _syncHighlight = new() { Stroke = Brushes.DodgerBlue, StrokeThickness = 1.5, Fill = new SolidColorBrush(Color.FromArgb(45, 50, 130, 255)), IsHitTestVisible = false };
    internal static SyncTexLineMap CreateSyncLineMap(string source, int userStartLine)
    {
        var offset = TexPreviewRenderer.BoundingBoxInsertionOffset(source);
        return new(userStartLine, offset >= 0 ? source[..offset].Count(c => c == '\n') + 1 : int.MaxValue,
            offset >= 0 ? TexPreviewRenderer.BoundingBoxWriterLineCount : 0, source.Count(c => c == '\n') + 1, offset >= 0 ? offset - source.LastIndexOf('\n', Math.Max(0, offset - 1)) : 1);
    }
    private string? SyncExecutable()
    {
        var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
        var path = ToolchainDoctor.Resolve(SettingsStore.Current.ResolveEngine(engine));
        return path is not null ? ToolchainDoctor.Resolve(Path.Combine(Path.GetDirectoryName(path)!, "synctex")) ?? ToolchainDoctor.Resolve("synctex") : ToolchainDoctor.Resolve("synctex");
    }
    private bool CanNavigate()
    {
        if (_lastPreviewPath is null || _syncLines is null || _syncSource != Editor.Text || !File.Exists(_lastPreviewPath)) { ModeStatus.Text = "Compile the current document before using source navigation."; return false; }
        if (!File.Exists(Path.ChangeExtension(_lastPreviewPath, ".synctex.gz")) && !File.Exists(Path.ChangeExtension(_lastPreviewPath, ".synctex"))) { ModeStatus.Text = "No SyncTeX map. Enable -synctex=1 in compiler flags and recompile."; return false; }
        try { var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex"; if (_lastPreviewKey != PreviewKey(engine, PreambleSettings.ReadSnapshot(), Editor.Text)) { ModeStatus.Text = "Preamble/compiler changed; recompile before navigating."; return false; } }
        catch (IOException error) { ModeStatus.Text = error.Message; return false; }
        return true;
    }
    private async void ShowInPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!CanNavigate()) return;
        var executable = SyncExecutable(); if (executable is null) { ModeStatus.Text = "SyncTeX executable not found; run Toolchain Doctor."; return; }
        _syncJob?.Cancel(); _syncJob?.Dispose(); var job = _syncJob = new(); var token = job.Token; var source = Editor.Text; var pdf = _lastPreviewPath!;
        try
        {
            var position = Editor.Document.GetLocation(Editor.CaretOffset); var line = _syncLines!.Forward(position.Line, position.Column);
            var input = Path.ChangeExtension(pdf, ".tex");
            var output = await Task.Run(() => SyncTexNavigation.QueryAsync(executable, ["view", "-i", $"{line}:{position.Column}:" + input, "-o", pdf], token));
            if (job.IsCancellationRequested || source != Editor.Text || pdf != _lastPreviewPath) return;
            var box = SyncTexNavigation.ParseBoxes(output).FirstOrDefault(b => b.Page == 1);
            if (box is null) { ModeStatus.Text = "TeX supplied no location for this line. Try a nearby node label."; return; }
            var target = PdfToCanvas(new Rect(box.X, box.Y - box.Height, box.Width, box.Height));
            if (target.IsEmpty) return;
            if (_syncHighlight.Parent is Panel parent) parent.Children.Remove(_syncHighlight);
            CompiledPreviewLayer.Children.Add(_syncHighlight); Canvas.SetLeft(_syncHighlight, target.X); Canvas.SetTop(_syncHighlight, target.Y); _syncHighlight.Width = target.Width; _syncHighlight.Height = target.Height;
            GraphicalScroller.ScrollToHorizontalOffset(target.X * GraphicalScale.ScaleX - GraphicalScroller.ViewportWidth / 2);
            GraphicalScroller.ScrollToVerticalOffset(target.Y * GraphicalScale.ScaleY - GraphicalScroller.ViewportHeight / 2);
            ModeStatus.Text = "SyncTeX location · Alt+click in the figure returns to source";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException) { ModeStatus.Text = "SyncTeX: " + error.Message; }
    }
    internal Rect PdfToCanvas(Rect pdfBox)
    {
        var width = _compiledVector?.Width ?? _compiledBitmap?.Width ?? 0; var height = _compiledVector?.Height ?? _compiledBitmap?.Height ?? 0;
        if (width <= 0 || height <= 0) return Rect.Empty;
        var sx = CompiledPreviewImage.Width / (width * 72 / 96); var sy = CompiledPreviewImage.Height / (height * 72 / 96);
        return new(Canvas.GetLeft(CompiledPreviewImage) + pdfBox.X * sx, Canvas.GetTop(CompiledPreviewImage) + pdfBox.Y * sy, Math.Max(2, pdfBox.Width * sx), Math.Max(2, pdfBox.Height * sy));
    }
    private async void SyncTexCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt || e.ChangedButton != MouseButton.Left) return;
        e.Handled = true; if (!CanNavigate()) return;
        var executable = SyncExecutable(); if (executable is null) { ModeStatus.Text = "SyncTeX executable not found."; return; }
        var point = e.GetPosition(CompiledPreviewImage); var width = _compiledVector?.Width ?? _compiledBitmap?.Width ?? 0; var height = _compiledVector?.Height ?? _compiledBitmap?.Height ?? 0;
        if (point.X < 0 || point.Y < 0 || point.X > CompiledPreviewImage.Width || point.Y > CompiledPreviewImage.Height || width <= 0 || height <= 0) return;
        var x = point.X / CompiledPreviewImage.Width * width * 72 / 96; var y = point.Y / CompiledPreviewImage.Height * height * 72 / 96;
        _syncJob?.Cancel(); _syncJob?.Dispose(); var job = _syncJob = new(); var token = job.Token; var source = Editor.Text; var pdf = _lastPreviewPath!;
        try
        {
            var output = await Task.Run(() => SyncTexNavigation.QueryAsync(executable, ["edit", "-o", SyncTexNavigation.PointArgument(1, x, y, pdf)], token));
            if (job.IsCancellationRequested || Editor.Text != source || pdf != _lastPreviewPath) return;
            var location = SyncTexNavigation.ParseSources(output).FirstOrDefault();
            if (location is null) { ModeStatus.Text = "No source mapping at this point."; return; }
            if (!string.Equals(Path.GetFullPath(location.Input), Path.ChangeExtension(pdf, ".tex"), StringComparison.OrdinalIgnoreCase)) { ModeStatus.Text = $"SyncTeX: included file {Path.GetFileName(location.Input)}, line {location.Line}. Open it explicitly to edit."; return; }
            var line = _syncLines!.Reverse(location.Line); if (line is null) { ModeStatus.Text = "This point maps to generated preamble/metadata, not editable source."; return; }
            var documentLine = Editor.Document.GetLineByNumber(line.Value); Editor.CaretOffset = documentLine.Offset + Math.Clamp(location.Column - 1, 0, documentLine.Length); Editor.ScrollToLine(line.Value); Editor.Focus();
            ModeStatus.Text = "SyncTeX → source line " + line;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException) { ModeStatus.Text = "SyncTeX: " + error.Message; }
    }
}
