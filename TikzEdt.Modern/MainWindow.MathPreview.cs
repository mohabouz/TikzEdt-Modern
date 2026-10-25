using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private CancellationTokenSource? _mathPreviewJob;
    private ToolTip? _mathTip;
    private void CancelMathPreview()
    {
        _mathPreviewJob?.Cancel();
        if (_mathTip is not null) _mathTip.IsOpen = false;
        _mathTip = null;
    }
    internal static string MathPreviewSource(MathSlice slice)
    {
        var body = slice.Kind switch { "align" => "\\begin{aligned}" + slice.Body + "\\end{aligned}", "gather" => "\\begin{gathered}" + slice.Body + "\\end{gathered}", _ => slice.Body };
        return "\\begin{tikzpicture}\n\\node[inner sep=2pt] {\\(\\displaystyle " + body + "\\)};\n\\end{tikzpicture}";
    }
    internal static UserSettings MathPreviewSettings(UserSettings settings)
    {
        var copy = settings.Clone();
        copy.CompilerFlags = string.Join(" ", UserSettings.ParseFlags(copy.CompilerFlags).Where(f => !f.Contains("shell-escape", StringComparison.OrdinalIgnoreCase) && !f.Contains("write18", StringComparison.OrdinalIgnoreCase)).Select(f => f.Any(char.IsWhiteSpace) ? "\"" + f + "\"" : f)) + " -no-shell-escape";
        copy.CompilerTimeoutSeconds = Math.Min(30, copy.CompilerTimeoutSeconds); return copy;
    }
    internal static string MathPreviewPreamble(string source, string external)
    {
        var combined = external + "\n\\usepackage{amsmath,amssymb}\n" + MathContext.Preamble(source);
        var classes = System.Text.RegularExpressions.Regex.Matches(MathContext.MaskComments(combined), @"\\documentclass\s*(?:\[[^\]]*\]\s*)?\{[^}]+\}", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (classes.Count == 0) return combined;
        var declaration = classes[0].Value;
        foreach (System.Text.RegularExpressions.Match match in classes.Reverse()) combined = combined.Remove(match.Index, match.Length);
        return declaration + "\n" + combined;
    }
    private async Task ShowMathPreviewAsync(int offset, bool explicitRequest = false)
    {
        CancelMathPreview(); if (!explicitRequest && !SettingsStore.Current.MathHoverPreview) return;
        _mathPreviewJob?.Dispose(); var job = new CancellationTokenSource(TimeSpan.FromSeconds(30)); _mathPreviewJob = job; var token = job.Token;
        var source = Editor.Text;
        try
        {
            var slice = await Task.Run(() => MathContext.Find(source, offset, token), token);
            if (slice is null || slice.Body.Length > 8000) return;
            await Task.Delay(200, token);
            var snapshot = PreambleSettings.ReadSnapshot();
            var preamble = MathPreviewPreamble(source, snapshot.Content ?? "");
            var compilerIdentity = SettingsStore.Current.CompilerIdentity;
            var settings = MathPreviewSettings(SettingsStore.Current);
            var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
            var directory = _filePath is null ? null : Path.GetDirectoryName(_filePath);
            var result = await Task.Run(() => TexVectorRenderer.RenderAsync(MathPreviewSource(slice), engine, directory, preamble, token, settings), token);
            if (token.IsCancellationRequested || !ReferenceEquals(_mathPreviewJob, job) || Editor.Text != source) return;
            if ((EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() != engine || SettingsStore.Current.CompilerIdentity != compilerIdentity || PreambleSettings.ReadSnapshot() != snapshot) return;
            var content = new StackPanel { MaxWidth = 470 };
            content.Children.Add(new TextBlock { Text = "Math preview · " + engine, FontWeight = FontWeights.Bold });
            if (result.Image is { } image)
                content.Children.Add(new Border { Background = System.Windows.Media.Brushes.White, Padding = new Thickness(8), Margin = new Thickness(0, 5, 0, 0), Child = new Image { Source = image, Width = Math.Clamp(image.Width * 1.5, 70, 440), MaxHeight = 200, Stretch = Stretch.Uniform } });
            else content.Children.Add(new TextBlock { Text = "Math preview could not compile. Check the selected engine and preamble. Hover previews disable shell escape.", TextWrapping = TextWrapping.Wrap });
            _mathTip = new ToolTip { Content = content, PlacementTarget = Editor, Placement = PlacementMode.MousePoint, StaysOpen = false, IsOpen = true };
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Xml.XmlException or FormatException or System.ComponentModel.Win32Exception or TimeoutException or InvalidOperationException) { if (!token.IsCancellationRequested) Editor.ToolTip = "Math preview unavailable: " + e.Message; }
    }
    private async void PreviewMathAtCaret_Click(object sender, RoutedEventArgs e) => await ShowMathPreviewAsync(Editor.CaretOffset, explicitRequest: true);
}
