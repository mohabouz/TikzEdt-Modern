using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;
using EllipseShape = System.Windows.Shapes.Ellipse;
using LineShape = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;
using Shape = System.Windows.Shapes.Shape;

namespace TikzEdt.Modern;

public partial class MainWindow : Window
{
    private string? _filePath;
    private bool _isDirty;
    private CancellationTokenSource? _compileCancellation;
    private int _compileGeneration;
    private readonly ControlDragGesture _controlDrag = new();
    private Point? _drawingStart;
    private Shape? _drawingPreview;
    private FrameworkElement? _draggedHandle;
    private HandleVisual? _draggedHandleInfo;
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _interactivePreviewTimer = new() { Interval = TimeSpan.FromMilliseconds(35) };
    private readonly DispatcherTimer _zoomRasterTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _syntaxDiagnosticsTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private CompletionWindow? _completionWindow;
    private readonly List<EditorDiagnostic> _syntaxDiagnostics = [];
    private readonly List<EditorDiagnostic> _compilerDiagnostics = [];
    private DiagnosticMarkerRenderer? _diagnosticRenderer;
    private string? _lastPreviewKey;
    private string? _lastPreviewPath;
    private string? _lastPreviewPngPath;
    private Rect _lastPreviewBoundingBox = Rect.Empty;
    private double _lastPreviewRasterDpi;
    private CancellationTokenSource? _zoomRasterCancellation;
    private HwndSource? _windowSource;
    private bool _hasCenteredExactPreview;
    private bool _isPolarGrid;
    private string? _gridPresentationKey;
    private LegacyTikzDiagram? _cachedDiagram;
    private string? _cachedDiagramSource;
    private BitmapSource? _compiledBitmap;
    private readonly DispatcherTimer _overlayTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private bool _updatingFromCanvas;
    private bool _applyingSettings;
    private const double CanvasSize = 6000;
    private const double CoordinateScale = 50;

    private static readonly Regex NodeRegex = new(
        @"(?<prefix>\\node(?:\s*\[[^\]]*\])?\s+at\s*)\(\s*(?<x>-?\d+(?:\.\d+)?)\s*,\s*(?<y>-?\d+(?:\.\d+)?)\s*\)(?<suffix>\s*\{(?<text>.*?)\}\s*;)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RectangleRegex = new(
        @"\\draw(?<options>\s*\[[^\]]*\])?\s*\(\s*(?<x1>-?\d+(?:\.\d+)?)\s*,\s*(?<y1>-?\d+(?:\.\d+)?)\s*\)\s*rectangle\s*\(\s*(?<x2>-?\d+(?:\.\d+)?)\s*,\s*(?<y2>-?\d+(?:\.\d+)?)\s*\)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex EllipseRegex = new(
        @"\\draw(?<options>\s*\[[^\]]*\])?\s*\(\s*(?<x>-?\d+(?:\.\d+)?)\s*,\s*(?<y>-?\d+(?:\.\d+)?)\s*\)\s*ellipse\s*\(\s*(?<rx>-?\d+(?:\.\d+)?)\s+and\s+(?<ry>-?\d+(?:\.\d+)?)\s*\)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex PathRegex = new(
        @"\\draw(?<options>\s*\[[^\]]*\])?\s*\(\s*(?<x1>-?\d+(?:\.\d+)?)\s*,\s*(?<y1>-?\d+(?:\.\d+)?)\s*\)\s*--\s*\(\s*(?<x2>-?\d+(?:\.\d+)?)\s*,\s*(?<y2>-?\d+(?:\.\d+)?)\s*\)\s*;",
        RegexOptions.Compiled);

    private static readonly Regex GridCommandRegex = new(
        @"\\(?:draw|path)(?<options>\s*\[[^\]]*\])?\s*\(\s*(?<x1>-?\d+(?:\.\d+)?)\s*,\s*(?<y1>-?\d+(?:\.\d+)?)\s*\)\s*grid(?<gridoptions>\s*\[[^\]]*\])?\s*\(\s*(?<x2>-?\d+(?:\.\d+)?)\s*,\s*(?<y2>-?\d+(?:\.\d+)?)\s*\)\s*;",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GridStepRegex = new(
        @"step\s*=\s*(?<value>\d+(?:\.\d+)?)\s*(?<unit>cm|mm|pt)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PolarCoordinateRegex = new(
        @"\(\s*-?\d+(?:\.\d+)?\s*:\s*-?\d+(?:\.\d+)?\s*\)", RegexOptions.Compiled);

    private enum EditorMode
    {
        Wysiwyg,
        Preview,
        Production
    }

    private enum ShapeKind
    {
        Path,
        Rectangle,
        Ellipse
    }

    public MainWindow()
    {
        NativeWindowTheme.Attach(this);
        InitializeComponent();
        InitializeEditorActions();
        InitializeFolding();
        InitializeSidebar();
        InitializeSnippetBuild();
        OverlayCanvas.LostMouseCapture += (_, _) => CancelCanvasInteraction();
        _overlayTimer.Tick += (_, _) => { _overlayTimer.Stop(); RenderWysiwyg(); };
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdateCoordinateContext();
        Editor.MouseMove += (_, args) =>
        {
            if (_selectionDragElement is not null || _draggedHandle is not null) return;
            var position = Editor.GetPositionFromPoint(args.GetPosition(Editor));
            if (position is null) return;
            var line = Editor.Document.GetLineByNumber(position.Value.Line);
            var column = position.Value.Column - 1;
            var coordinate = Regex.Matches(Editor.Document.GetText(line), @"\([^()]*\)")
                .Cast<Match>().FirstOrDefault(match => column >= match.Index && column <= match.Index + match.Length);
            if (coordinate is not null) SetPolarGrid(coordinate.Value.Contains(':'));
        };
        _diagnosticRenderer = new DiagnosticMarkerRenderer(Editor.Document);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticRenderer);
        Editor.TextArea.TextEntered += Editor_TextEntered;
        Editor.TextArea.TextEntering += Editor_TextEntering;
        Editor.TextArea.TextView.MouseHover += Editor_MouseHover;
        Editor.TextArea.TextView.MouseHoverStopped += (_, _) => Editor.ToolTip = null;
        SourceInitialized += (_, _) =>
        {
            _windowSource = PresentationSource.FromVisual(this) as HwndSource;
            _windowSource?.AddHook(WindowMessageHook);
        };
        Closed += (_, _) => _windowSource?.RemoveHook(WindowMessageHook);
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            Compile_Click(_previewTimer, new RoutedEventArgs());
        };
        _interactivePreviewTimer.Tick += (_, _) =>
        {
            _interactivePreviewTimer.Stop();
            Compile_Click(_interactivePreviewTimer, new RoutedEventArgs());
        };
        _zoomRasterTimer.Tick += async (_, _) =>
        {
            _zoomRasterTimer.Stop();
            await RefreshRasterForZoomAsync();
        };
        _syntaxDiagnosticsTimer.Tick += (_, _) =>
        {
            _syntaxDiagnosticsTimer.Stop();
            RefreshSyntaxDiagnostics();
        };
        LoadSyntaxHighlighting();
        Editor.Text = "\\begin{tikzpicture}\n    \\draw[blue, thick] (0,0) rectangle (4,2);\n    \\node at (2,1) {TikzEdt Modern: $\\displaystyle E = m\\cdot c^2$};\n\\end{tikzpicture}";
        Editor.TextChanged += (_, _) =>
        {
            if (_advancedPoints.Count > 0) CancelAdvancedDrawing();
            _isDirty = true;
            _compilerDiagnostics.Clear();
            UpdateTitle();
            if (!_updatingFromCanvas && WysiwygMode.IsChecked == true)
            {
                _overlayTimer.Stop();
                _overlayTimer.Start();
            }
            if (IsLoaded && SettingsStore.Current.AutomaticPreview && ProductionMode.IsChecked != true && !_updatingFromCanvas)
            {
                _previewTimer.Stop();
                _previewTimer.Start();
            }
            _syntaxDiagnosticsTimer.Stop();
            _syntaxDiagnosticsTimer.Start();
        };
        _isDirty = false;
        UpdateTitle();
        UpdatePreambleStatus();
        ApplySettings();
        Loaded += (_, _) =>
        {
            _isDirty = false;
            UpdateTitle();
            RenderWysiwyg();
            RefreshFoldings();
            RefreshSidebarFiles();
            RefreshSyntaxDiagnostics();
            Dispatcher.BeginInvoke(CenterGraphicalViewport, DispatcherPriority.Loaded);
            if (SettingsStore.Current.AutomaticPreview) _previewTimer.Start();
        };
    }

    private void LoadSyntaxHighlighting()
    {
        var syntaxPath = Path.Combine(AppContext.BaseDirectory, "TikzSyntax.xshd");
        if (!File.Exists(syntaxPath))
            return;

        using var reader = XmlReader.Create(syntaxPath);
        Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    private void Editor_TextEntered(object sender, TextCompositionEventArgs e)
    {
        if (SettingsStore.Current.AutomaticCompletion && e.Text is "\\" or "[")
            ShowCompletion();
    }

    private void Editor_TextEntering(object sender, TextCompositionEventArgs e)
    {
        if (_completionWindow is null || string.IsNullOrEmpty(e.Text))
            return;

        var character = e.Text[0];
        if (character is '{' or '[' or '(' or ',' or ';')
            _completionWindow.CompletionList.RequestInsertion(e);
        else if (char.IsWhiteSpace(character) || character is '}' or ']' or ')')
            _completionWindow.Close();
    }

    private void ShowCompletion()
    {
        _completionWindow?.Close();
        var caretOffset = Editor.CaretOffset;
        var startOffset = FindCompletionStartOffset(Editor.Text, caretOffset);
        var items = TikzCompletionProvider.GetItems(Editor.Text, caretOffset);
        if (items.Count == 0)
            return;

        var window = new CompletionWindow(Editor.TextArea)
        {
            StartOffset = startOffset,
            Width = 430,
            MaxHeight = 340,
            Background = new SolidColorBrush(Color.FromRgb(43, 45, 48)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(68, 71, 77)),
            BorderThickness = new Thickness(1)
        };
        window.CompletionList.IsFiltering = true;
        window.CompletionList.ListBox.Background = window.Background;
        window.CompletionList.ListBox.Foreground = new SolidColorBrush(Color.FromRgb(223, 225, 229));
        window.CompletionList.ListBox.BorderThickness = new Thickness(0);
        foreach (var item in items.OrderByDescending(item => item.Priority).ThenBy(item => item.Text))
            window.CompletionList.CompletionData.Add(item);

        _completionWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_completionWindow, window))
                _completionWindow = null;
        };
        window.Show();

        var prefix = Editor.Document.GetText(startOffset, caretOffset - startOffset);
        if (prefix.Length > 0)
            window.CompletionList.SelectItem(prefix);
    }

    private static int FindCompletionStartOffset(string source, int caretOffset)
    {
        var offset = Math.Clamp(caretOffset, 0, source.Length);
        while (offset > 0)
        {
            var character = source[offset - 1];
            if (!char.IsLetterOrDigit(character) && character is not ('\\' or '@' or '*' or '-' or '<' or '>' or '='))
                break;
            offset--;
        }
        return offset;
    }

    private void RefreshSyntaxDiagnostics()
    {
        _syntaxDiagnostics.Clear();
        _syntaxDiagnostics.AddRange(TikzSyntaxDiagnostics.Analyze(Editor.Text));
        RefreshDiagnosticsUi();
    }

    private void RefreshDiagnosticsUi()
    {
        var diagnostics = _syntaxDiagnostics
            .Concat(_compilerDiagnostics)
            .OrderBy(item => item.Line)
            .ThenBy(item => item.Column)
            .ThenBy(item => item.Source)
            .ToArray();
        _diagnosticRenderer?.SetDiagnostics(diagnostics);
        Editor.TextArea.TextView.Redraw();
        ProblemsList.ItemsSource = diagnostics;
        ProblemsTab.Header = $"Problems  {diagnostics.Length}";
    }

    private void Editor_MouseHover(object sender, MouseEventArgs e)
    {
        if (_diagnosticRenderer is null)
            return;
        var position = Editor.GetPositionFromPoint(e.GetPosition(Editor));
        if (position is null)
            return;
        var offset = Editor.Document.GetOffset(position.Value.Location);
        var diagnostic = _diagnosticRenderer.GetDiagnosticAt(offset);
        Editor.ToolTip = diagnostic is null
            ? null
            : $"{diagnostic.Source}: {diagnostic.Message}\n{diagnostic.Location}";
    }

    private void ProblemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProblemsList.SelectedItem is not EditorDiagnostic diagnostic)
            return;
        Editor.Focus();
        Editor.Select(Math.Clamp(diagnostic.Offset, 0, Editor.Document.TextLength),
            Math.Min(diagnostic.Length, Math.Max(0, Editor.Document.TextLength - diagnostic.Offset)));
        if (diagnostic.Line > 0)
            Editor.ScrollToLine(diagnostic.Line);
    }

    private void UpdateTitle()
    {
        var name = _filePath is null ? "Untitled.tikz" : Path.GetFileName(_filePath);
        Title = $"TikzEdt Modern — {name}{(_isDirty ? " *" : string.Empty)}";
        if (DocumentNameText is not null) DocumentNameText.Text = name;
        FollowDocumentFolder();
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
            return;

        _filePath = null;
        Editor.Text = "\\begin{tikzpicture}\n    \n\\end{tikzpicture}";
        _isDirty = false;
        UpdateTitle();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscardChanges())
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "TikZ and TeX files|*.tikz;*.tex|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        _filePath = dialog.FileName;
        Editor.Text = File.ReadAllText(_filePath);
        _isDirty = false;
        UpdateTitle();
        ModeStatus.Text = $"Opened {_filePath}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveDocument(false);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        SaveDocument(true);
    }

    private bool SaveDocument(bool saveAs)
    {
        if (saveAs || _filePath is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "TikZ file|*.tikz|TeX file|*.tex|All files|*.*",
                DefaultExt = ".tikz",
                AddExtension = true,
                FileName = _filePath is null ? "diagram.tikz" : Path.GetFileName(_filePath)
            };
            if (dialog.ShowDialog(this) != true)
                return false;
            _filePath = dialog.FileName;
        }

        File.WriteAllText(_filePath, Editor.Text, new UTF8Encoding(false));
        _isDirty = false;
        UpdateTitle();
        ModeStatus.Text = $"Saved {_filePath}";
        return true;
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_isDirty)
            return true;

        var result = MessageBox.Show(this, "Save changes to the current document?", "TikzEdt Modern", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(false),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.CanUndo)
            Editor.Undo();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.CanRedo)
            Editor.Redo();
    }

    private void Compiler_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsInitialized || _applyingSettings || e.AddedItems.Count == 0 ||
            EngineSelector.SelectedItem is not ComboBoxItem { Tag: string }) return;

        // An explicit engine change must rebuild even with automatic preview off
        // or in Production mode. Compile_Click invalidates in-flight results.
        _previewTimer.Stop();
        _interactivePreviewTimer.Stop();
        _lastPreviewKey = null;
        Compile_Click(sender, e);
        if (SnippetsSidebarTab.IsSelected) _ = RefreshSidebarSnippetAsync();
        ScheduleSidebarThumbnails();
    }

    private async void Compile_Click(object sender, RoutedEventArgs e)
    {
        var generation = ++_compileGeneration;
        var isInteractiveRefresh = ReferenceEquals(sender, _interactivePreviewTimer);
        var isBackgroundRefresh = ReferenceEquals(sender, _previewTimer) || isInteractiveRefresh;
        _compileCancellation?.Cancel();
        _compileCancellation?.Dispose();
        _zoomRasterTimer.Stop();
        _zoomRasterCancellation?.Cancel();
        _compileCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(SettingsStore.Current.CompilerTimeoutSeconds));
        var cancellationToken = _compileCancellation.Token;
        var source = Editor.Text;

        var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
        PreambleSnapshot preamble;
        try
        {
            preamble = PreambleSettings.ReadSnapshot();
        }
        catch (Exception exception)
        {
            _compilerDiagnostics.Clear();
            _compilerDiagnostics.Add(CreateCompilerDiagnostic(1, exception.Message, EditorDiagnosticSeverity.Error));
            RefreshDiagnosticsUi();
            BottomTabs.SelectedItem = ProblemsTab;
            ModeStatus.Text = "Preamble file could not be loaded";
            return;
        }
        var previewKey = PreviewKey(engine, preamble, source);
        var outputHeader = $"> {engine} · exact TeX preview\n\n";
        if (!isInteractiveRefresh)
            OutputText.Text = outputHeader;
        if (!isBackgroundRefresh)
            BottomTabs.SelectedItem = OutputTab;
        if (!isBackgroundRefresh || PreviewMode.IsChecked == true)
            ModeStatus.Text = $"Compiling with {engine}…";

        var previewWatch = Stopwatch.StartNew();
        try
        {
            var render = isBackgroundRefresh
                ? TexPreviewRenderer.RenderCachedAsync
                : (Func<string, string, string?, string?, double, CancellationToken, UserSettings?, Task<TexPreviewResult>>)TexPreviewRenderer.RenderAsync;
            var documentDirectory = _filePath is null ? null : Path.GetDirectoryName(_filePath);
            var rasterDpi = isBackgroundRefresh ? 192 : RequestedRasterDpi;
            var settings = SettingsStore.Current.Clone();
            // Process startup, dependency checks and format preparation must not block input.
            var result = await Task.Run(() => render(source, engine, documentDirectory, preamble.Content,
                rasterDpi, cancellationToken, settings), cancellationToken);
            if (generation != _compileGeneration || source != Editor.Text)
                return;
            ModeStatus.Text = result.ExitCode == 0 && File.Exists(result.PngPath)
                ? $"Exact TeX preview ready · {engine} · {previewWatch.ElapsedMilliseconds} ms"
                : $"Compilation failed with exit code {result.ExitCode}";
            if (result.ExitCode == 0 && File.Exists(result.PngPath))
            {
                _lastPreviewKey = previewKey;
                _lastPreviewPath = result.PdfPath;
                _lastPreviewPngPath = result.PngPath;
                _lastPreviewBoundingBox = result.BoundingBox;
                _lastPreviewRasterDpi = result.RasterDpi;
                DisplayCompiledPreview(result.PngPath, result.BoundingBox);
                if (isBackgroundRefresh && RequestedRasterDpi > result.RasterDpi * 1.15)
                {
                    _zoomRasterTimer.Stop();
                    _zoomRasterTimer.Start();
                }
                if (!_hasCenteredExactPreview)
                {
                    _hasCenteredExactPreview = true;
                    _ = Dispatcher.BeginInvoke(CenterGraphicalViewport, DispatcherPriority.Loaded);
                }

                // Let WPF present the new TeX pixels before formatting the
                // compiler log.  On interactive edits the picture, not a
                // hidden diagnostics textbox, is the latency-sensitive part.
                if (isInteractiveRefresh)
                    await Dispatcher.Yield(DispatcherPriority.Render);
            }

            if (generation != _compileGeneration || source != Editor.Text) return;
            OutputText.Text = outputHeader + result.Log;
            _compilerDiagnostics.Clear();
            _compilerDiagnostics.AddRange(ParseCompilerDiagnostics(result.Log, result.UserSourceStartLine, result.ExitCode));
            RefreshDiagnosticsUi();
            if (_compilerDiagnostics.Count > 0 && !isBackgroundRefresh)
                BottomTabs.SelectedItem = ProblemsTab;
        }
        catch (OperationCanceledException)
        {
            if (generation != _compileGeneration) return;
            OutputText.Text += "\nCompilation cancelled or timed out.";
            ModeStatus.Text = "Compilation cancelled";
        }
        catch (Exception exception)
        {
            if (generation != _compileGeneration) return;
            _compilerDiagnostics.Clear();
            _compilerDiagnostics.Add(CreateCompilerDiagnostic(1, exception.Message, EditorDiagnosticSeverity.Error));
            RefreshDiagnosticsUi();
            BottomTabs.SelectedItem = ProblemsTab;
            ModeStatus.Text = "Compiler could not be started";
        }
    }

    private IReadOnlyList<EditorDiagnostic> ParseCompilerDiagnostics(string log, int userSourceStartLine, int exitCode)
    {
        var diagnostics = new List<EditorDiagnostic>();
        foreach (Match match in Regex.Matches(log,
                     @"tikzedt-preview\.tex:(?<line>\d+):\s*(?<message>[^\r\n]+)",
                     RegexOptions.IgnoreCase))
        {
            var compiledLine = int.Parse(match.Groups["line"].Value, CultureInfo.InvariantCulture);
            var userLine = compiledLine - userSourceStartLine + 1;
            if (userLine < 1 || userLine > Editor.Document.LineCount)
                continue;
            var message = match.Groups["message"].Value.Trim();
            if (message.StartsWith("==>", StringComparison.Ordinal))
                continue;
            var severity = message.Contains("warning", StringComparison.OrdinalIgnoreCase)
                ? EditorDiagnosticSeverity.Warning
                : EditorDiagnosticSeverity.Error;
            diagnostics.Add(CreateCompilerDiagnostic(userLine, message, severity));
        }

        if (diagnostics.Count == 0)
        {
            var lines = log.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].StartsWith('!'))
                    continue;
                var message = lines[index].TrimStart('!', ' ');
                var compiledLine = 0;
                for (var lookAhead = index + 1; lookAhead < Math.Min(lines.Length, index + 9); lookAhead++)
                {
                    var lineMatch = Regex.Match(lines[lookAhead], @"^l\.(?<line>\d+)");
                    if (lineMatch.Success)
                    {
                        compiledLine = int.Parse(lineMatch.Groups["line"].Value, CultureInfo.InvariantCulture);
                        break;
                    }
                }
                var userLine = Math.Clamp(compiledLine - userSourceStartLine + 1, 1, Editor.Document.LineCount);
                diagnostics.Add(CreateCompilerDiagnostic(userLine, message, EditorDiagnosticSeverity.Error));
            }
        }

        if (diagnostics.Count == 0 && exitCode != 0)
            diagnostics.Add(CreateCompilerDiagnostic(1, "LaTeX compilation failed. Open Output for the complete compiler message.", EditorDiagnosticSeverity.Error));

        return diagnostics
            .GroupBy(item => (item.Line, item.Message))
            .Select(group => group.First())
            .ToArray();
    }

    private EditorDiagnostic CreateCompilerDiagnostic(int lineNumber, string message, EditorDiagnosticSeverity severity)
    {
        lineNumber = Math.Clamp(lineNumber, 1, Math.Max(1, Editor.Document.LineCount));
        var line = Editor.Document.GetLineByNumber(lineNumber);
        var lineText = Editor.Document.GetText(line);
        var indentation = lineText.TakeWhile(char.IsWhiteSpace).Count();
        return new EditorDiagnostic(
            line.Offset + Math.Min(indentation, line.Length),
            Math.Max(1, line.Length - indentation),
            lineNumber,
            indentation + 1,
            message,
            "LaTeX",
            severity);
    }

    private void CancelCompile_Click(object sender, RoutedEventArgs e)
    {
        _compileCancellation?.Cancel();
    }

    private void Preamble_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PreambleDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        UpdatePreambleStatus();
        _lastPreviewKey = null;
        _compileCancellation?.Cancel();
        _previewTimer.Stop();
        Compile_Click(sender, e);
    }

    private void UpdatePreambleStatus()
    {
        var path = PreambleSettings.SelectedFilePath;
        PreambleButton.ToolTip = string.IsNullOrWhiteSpace(path)
            ? "Using TikzEdt Modern's built-in standalone preamble. Click to select or create a preamble file."
            : $"Using {path}\nClick to edit or choose another preamble.";
    }

    private static string PreviewKey(string engine, PreambleSnapshot preamble, string source) =>
        engine + "\n" + SettingsStore.Current.CompilerIdentity + "\n" + preamble.CacheIdentity + "\n" + source;

    private void ApplySettings()
    {
        _applyingSettings = true;
        try
        {
            var settings = SettingsStore.Current;
            EditorAppearance.Apply(Editor, settings);
            _previewTimer.Interval = TimeSpan.FromMilliseconds(settings.PreviewDelayMs);
            EngineSelector.SelectedItem = EngineSelector.Items.OfType<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == settings.DefaultEngine);
            GridToggle.IsChecked = settings.ShowGrid;
            SnapToggle.IsChecked = settings.SnapToGrid;
            GridStepSelector.SelectedItem = GridStepSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag.ToString() == settings.GridStep.ToString(CultureInfo.InvariantCulture));
            CoordinateSystemSelector.SelectedItem = CoordinateSystemSelector.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag.ToString() == settings.CoordinateSystem);
            SetPolarGrid(false);
            PreviewGrid.Opacity = settings.ShowGrid ? 1 : 0;
        }
        finally { _applyingSettings = false; }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ApplySettings();
        _lastPreviewKey = null;
        _previewTimer.Stop();
        if (ProductionMode.IsChecked != true) Compile_Click(sender, e);
        ModeStatus.Text = "Settings saved";
    }

    private void CoordinateSystem_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _applyingSettings) return;
        UpdateCoordinateContext();
        var settings = SettingsStore.Current.Clone();
        settings.CoordinateSystem = (CoordinateSystemSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Auto";
        try { SettingsStore.Save(settings); }
        catch (Exception exception) { ModeStatus.Text = "Could not save coordinate preference: " + exception.Message; }
    }

    private void BrowseSnippets_Click(object sender, RoutedEventArgs e)
    {
        var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
        var dialog = new SnippetsDialog(engine) { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.InsertedCode)) return;
        InsertSnippet(dialog.InsertedCode, dialog.Dependencies);
    }

    private void InsertSnippet(string code, string dependencies, string requiredPackages = "")
    {
        var libraries = string.IsNullOrWhiteSpace(dependencies) ? "" : $"\\usetikzlibrary{{{dependencies}}}\n";
        using (Editor.Document.RunUpdate())
        {
            if (code.Contains("\\documentclass", StringComparison.Ordinal))
            {
                if (!ConfirmDiscardChanges()) return;
                _filePath = null;
                Editor.Text = code;
            }
            else
            {
                // Examples include their own picture wrapper. Strip it when
                // inserting into an existing picture to avoid nested TikZ.
                if (Editor.Text.Contains("\\begin{tikzpicture}", StringComparison.Ordinal) || Editor.Text.Contains("\\begin{circuitikz}", StringComparison.Ordinal))
                {
                    code = Regex.Replace(code, @"\\begin\s*\{(?:tikzpicture|circuitikz)\}(\s*\[[^\]]*\])?",
                        match => "\\begin{scope}" + match.Groups[1].Value);
                    code = Regex.Replace(code, @"\\end\s*\{(?:tikzpicture|circuitikz)\}", "\\end{scope}");
                }
                var insertion = Editor.SelectionLength > 0 ? Editor.SelectionStart : Editor.CaretOffset;
                if (Editor.SelectionLength == 0 && Editor.Text.Contains("\\begin{tikzpicture}") &&
                    insertion < Editor.Text.IndexOf("\\begin{tikzpicture}", StringComparison.Ordinal) + "\\begin{tikzpicture}".Length)
                    insertion = Editor.Text.LastIndexOf("\\end{tikzpicture}", StringComparison.Ordinal);
                insertion = Math.Max(0, insertion);
                Editor.Document.Replace(insertion, Editor.SelectionLength, "\n" + code.Trim() + "\n");
                if (libraries.Length > 0 && !Editor.Text.Contains(libraries.Trim(), StringComparison.Ordinal))
                    Editor.Document.Insert(0, libraries);
                foreach (var package in requiredPackages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var directive = "% !TIKZEDT PACKAGE " + package;
                    if (!Editor.Text.Contains(directive, StringComparison.Ordinal)) Editor.Document.Insert(0, directive + "\n");
                }
            }
        }
        Editor.Focus();
        RenderWysiwyg();
        QueueInteractivePreview();
    }

    private async void CopySvg_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
            var preamble = PreambleSettings.ReadSnapshot();
            var source = Editor.Text;
            var key = PreviewKey(engine, preamble, source);
            var pdfPath = _lastPreviewPath;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(SettingsStore.Current.CompilerTimeoutSeconds));
            if (_lastPreviewKey != key || pdfPath is null || !File.Exists(pdfPath))
            {
                ModeStatus.Text = "Compiling current figure for SVG…";
                var result = await TexPreviewRenderer.RenderAsync(source, engine,
                    _filePath is null ? null : Path.GetDirectoryName(_filePath),
                    preamble.Content, 192, timeout.Token);
                if (result.ExitCode != 0 || !File.Exists(result.PdfPath))
                {
                    OutputText.Text = result.Log;
                    BottomTabs.SelectedItem = OutputTab;
                    ModeStatus.Text = "SVG copy failed: fix the compilation error first";
                    return;
                }
                pdfPath = result.PdfPath;
            }
            var svgPath = await SvgClipboard.ExportAsync(pdfPath, timeout.Token);
            SvgClipboard.Copy(svgPath, key == _lastPreviewKey ? _compiledBitmap : null);
            ModeStatus.Text = "Figure copied as SVG · paste into your application";
        }
        catch (Exception exception)
        {
            ModeStatus.Text = "SVG copy failed: " + exception.Message;
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Space && Editor.IsKeyboardFocusWithin)
        {
            ShowCompletion();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            Compile_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.B)
        {
            ToggleSidebar_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
        {
            SaveDocument(false);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            Open_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            New_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.F)
        {
            FormatCode_Click(sender, e);
            e.Handled = true;
        }
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key is Key.Oem4 or Key.Oem6)
        {
            if (e.Key == Key.Oem4) FoldAll_Click(sender, e); else UnfoldAll_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && OverlayCanvas.IsKeyboardFocusWithin && FinishAdvancedDrawing())
            e.Handled = true;
        else if (e.Key == Key.Delete && OverlayCanvas.IsKeyboardFocusWithin && _canvasSelection.Keys.Count > 0)
        {
            DeleteCanvasSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && OverlayCanvas.IsKeyboardFocusWithin)
        {
            CancelCanvasInteraction();
            CancelAdvancedDrawing();
            _canvasSelection.Keys.Clear();
            RefreshSelectionPresentation();
            e.Handled = true;
        }
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        var mode = WysiwygMode.IsChecked == true
            ? EditorMode.Wysiwyg
            : PreviewMode.IsChecked == true
                ? EditorMode.Preview
                : EditorMode.Production;

        WysiwygTools.Visibility = mode == EditorMode.Wysiwyg ? Visibility.Visible : Visibility.Collapsed;
        OverlayCanvas.Visibility = mode == EditorMode.Wysiwyg ? Visibility.Visible : Visibility.Collapsed;
        PreviewGrid.Visibility = mode == EditorMode.Wysiwyg ? Visibility.Visible : Visibility.Collapsed;
        PreviewMessage.Visibility = Visibility.Collapsed;
        ConfigureSurfaceForMode();

        ModeStatus.Text = mode switch
        {
            EditorMode.Wysiwyg => "WYSIWYG mode · graphical editing enabled",
            EditorMode.Preview => "Preview mode · automatic preview without graphical overlay",
            _ => "Production mode · final PDF and manual compilation"
        };

        if (mode == EditorMode.Wysiwyg)
        {
            RenderWysiwyg();
            Dispatcher.BeginInvoke(CenterGraphicalViewport, DispatcherPriority.Loaded);
        }
        else
        {
            var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
            string key;
            try { key = PreviewKey(engine, PreambleSettings.ReadSnapshot(), Editor.Text); }
            catch { Compile_Click(sender, e); return; }
            if (_lastPreviewKey == key && _lastPreviewPath is not null && File.Exists(_lastPreviewPath))
            {
                if (_lastPreviewPngPath is not null && File.Exists(_lastPreviewPngPath))
                    DisplayCompiledPreview(_lastPreviewPngPath, _lastPreviewBoundingBox);
                ModeStatus.Text = "Preview ready";
            }
            else
                Compile_Click(sender, e);
        }
    }

    private void DisplayCompiledPreview(string pngPath, Rect boundingBox)
    {
        if (_compiledBitmap is not BitmapImage cached || cached.UriSource?.LocalPath != pngPath)
        {
            var loaded = new BitmapImage();
            loaded.BeginInit();
            loaded.CacheOption = BitmapCacheOption.OnLoad;
            loaded.UriSource = new Uri(pngPath, UriKind.Absolute);
            loaded.EndInit();
            loaded.Freeze();
            _compiledBitmap = loaded;
        }
        var bitmap = _compiledBitmap;

        CompiledPreviewImage.Source = bitmap;
        ConfigureSurfaceForMode();
        if (WysiwygMode.IsChecked != true)
        {
            Canvas.SetLeft(CompiledPreviewImage, 0);
            Canvas.SetTop(CompiledPreviewImage, 0);
            CompiledPreviewImage.Width = GraphicalSurface.Width;
            CompiledPreviewImage.Height = GraphicalSurface.Height;
            UpdateControlMarkers();
            return;
        }
        if (boundingBox.IsEmpty)
        {
            CompiledPreviewImage.Width = bitmap.PixelWidth * 96d / bitmap.DpiX;
            CompiledPreviewImage.Height = bitmap.PixelHeight * 96d / bitmap.DpiY;
            Canvas.SetLeft(CompiledPreviewImage, CanvasSize / 2 - CompiledPreviewImage.Width / 2);
            Canvas.SetTop(CompiledPreviewImage, CanvasSize / 2 - CompiledPreviewImage.Height / 2);
            UpdateControlMarkers();
            return;
        }

        CompiledPreviewImage.Width = boundingBox.Width * CoordinateScale;
        CompiledPreviewImage.Height = boundingBox.Height * CoordinateScale;
        Canvas.SetLeft(CompiledPreviewImage, ToCanvasX(boundingBox.Left));
        // System.Windows.Rect calls the smaller Y value "Top". TikZ uses an
        // upward Y axis, so the visual top is the bounding box's maximum Y.
        Canvas.SetTop(CompiledPreviewImage, ToCanvasY(boundingBox.Bottom));
        UpdateControlMarkers();
    }

    private void ConfigureSurfaceForMode()
    {
        if (WysiwygMode.IsChecked == true)
        {
            GraphicalSurface.Width = CanvasSize;
            GraphicalSurface.Height = CanvasSize;
            if (!_lastPreviewBoundingBox.IsEmpty)
            {
                CompiledPreviewImage.Width = _lastPreviewBoundingBox.Width * CoordinateScale;
                CompiledPreviewImage.Height = _lastPreviewBoundingBox.Height * CoordinateScale;
                Canvas.SetLeft(CompiledPreviewImage, ToCanvasX(_lastPreviewBoundingBox.Left));
                Canvas.SetTop(CompiledPreviewImage, ToCanvasY(_lastPreviewBoundingBox.Bottom));
            }
        }
        else if (_compiledBitmap is not null)
        {
            // The PDF page is already cropped by TeX. Use its physical size,
            // rather than the workspace extent or coordinate-overlay scale.
            GraphicalSurface.Width = _compiledBitmap.PixelWidth * 96d / _compiledBitmap.DpiX;
            GraphicalSurface.Height = _compiledBitmap.PixelHeight * 96d / _compiledBitmap.DpiY;
            CompiledPreviewImage.Width = GraphicalSurface.Width;
            CompiledPreviewImage.Height = GraphicalSurface.Height;
            Canvas.SetLeft(CompiledPreviewImage, 0);
            Canvas.SetTop(CompiledPreviewImage, 0);
        }
    }

    private void GridToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || PreviewGrid is null)
            return;

        PreviewGrid.Opacity = GridToggle.IsChecked == true ? 1 : 0;
    }

    private void GridStepSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
            return;
        UpdateGridPresentation();
        RenderWysiwyg();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GraphicalScale is null || ZoomText is null || GraphicalScroller is null)
            return;

        var oldScale = e.OldValue <= 0 ? 1 : e.OldValue / 100d;
        var scale = e.NewValue / 100d;
        var localCenterX = (GraphicalScroller.HorizontalOffset + GraphicalScroller.ViewportWidth / 2) / oldScale;
        var localCenterY = (GraphicalScroller.VerticalOffset + GraphicalScroller.ViewportHeight / 2) / oldScale;
        GraphicalScale.ScaleX = scale;
        GraphicalScale.ScaleY = scale;
        if (IsLoaded && _selectionDragElement is null && _draggedHandle is null) RefreshSelectionPresentation();
        else if (IsLoaded) UpdateControlMarkers();
        ZoomText.Text = $"{e.NewValue:0}%";
        if (IsLoaded)
        {
            Dispatcher.BeginInvoke(() =>
            {
                GraphicalScroller.ScrollToHorizontalOffset(localCenterX * scale - GraphicalScroller.ViewportWidth / 2);
                GraphicalScroller.ScrollToVerticalOffset(localCenterY * scale - GraphicalScroller.ViewportHeight / 2);
            }, DispatcherPriority.Loaded);

            if (RequestedRasterDpi > _lastPreviewRasterDpi * 1.15 && _lastPreviewPath is not null)
            {
                _zoomRasterTimer.Stop();
                _zoomRasterTimer.Start();
                ModeStatus.Text = $"Zoom {e.NewValue:0}% · sharpening preview…";
            }
        }
    }

    private double RequestedRasterDpi => Math.Clamp(SettingsStore.Current.PreviewDpi * ZoomSlider.Value / 100d, 192d, 4800d);

    private async Task RefreshRasterForZoomAsync()
    {
        if (_lastPreviewPath is null || !File.Exists(_lastPreviewPath))
            return;

        var pdfPath = _lastPreviewPath;
        var boundingBox = _lastPreviewBoundingBox;
        var generation = _compileGeneration;
        var source = Editor.Text;
        _zoomRasterCancellation?.Cancel();
        _zoomRasterCancellation?.Dispose();
        _zoomRasterCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            var result = await TexPreviewRenderer.RasterizeAsync(
                pdfPath,
                boundingBox,
                RequestedRasterDpi,
                _zoomRasterCancellation.Token);
            if (generation != _compileGeneration || pdfPath != _lastPreviewPath || source != Editor.Text ||
                result.ExitCode != 0 || !File.Exists(result.PngPath))
                return;

            _lastPreviewPngPath = result.PngPath;
            _lastPreviewRasterDpi = result.Dpi;
            DisplayCompiledPreview(result.PngPath, boundingBox);
            ModeStatus.Text = $"Crisp preview · {ZoomSlider.Value:0}% · {result.Dpi:0} DPI";
        }
        catch (OperationCanceledException)
        {
            // A newer zoom request superseded this rasterization.
        }
    }

    private void CenterOrigin_Click(object sender, RoutedEventArgs e) => CenterGraphicalViewport();

    private void CenterGraphicalViewport()
    {
        if (GraphicalScroller is null || GraphicalScale is null)
            return;
        GraphicalScroller.UpdateLayout();
        GraphicalScroller.ScrollToHorizontalOffset(GraphicalSurface.Width / 2 * GraphicalScale.ScaleX - GraphicalScroller.ViewportWidth / 2);
        GraphicalScroller.ScrollToVerticalOffset(GraphicalSurface.Height / 2 * GraphicalScale.ScaleY - GraphicalScroller.ViewportHeight / 2);
    }

    private void GraphicalScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomSlider.Value = Math.Clamp(ZoomSlider.Value + (e.Delta > 0 ? 25 : -25), ZoomSlider.Minimum, ZoomSlider.Maximum);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            GraphicalScroller.ScrollToHorizontalOffset(
                GraphicalScroller.HorizontalOffset - e.Delta / 120d * 80d);
            e.Handled = true;
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmMouseHorizontalWheel = 0x020E;
        if (message != WmMouseHorizontalWheel || GraphicalScroller?.IsMouseOver != true)
            return IntPtr.Zero;

        var delta = unchecked((short)((wParam.ToInt64() >> 16) & 0xffff));
        GraphicalScroller.ScrollToHorizontalOffset(
            GraphicalScroller.HorizontalOffset + delta / 120d * 80d);
        handled = true;
        return IntPtr.Zero;
    }

    private void RenderWysiwyg()
    {
        if (!IsLoaded || OverlayCanvas is null || _selectionDragElement is not null || _draggedHandle is not null || _marqueeStart is not null || _advancedPoints.Count > 0)
            return;

        if (_selectionSource != Editor.Text) _canvasSelection.Keys.Clear();
        _selectionSource = Editor.Text;

        if (_cachedDiagramSource != Editor.Text)
        {
            _cachedDiagramSource = Editor.Text;
            _cachedDiagram = LegacyTikzAdapter.Parse(_cachedDiagramSource);
        }
        var legacyDiagram = _cachedDiagram!;
        UpdateCoordinateContext();
        OverlayCanvas.Children.Clear();

        foreach (Match match in PathRegex.Matches(Editor.Text))
        {
            var line = new LineShape
            {
                X1 = ToCanvasX(ParseNumber(match.Groups["x1"].Value)),
                Y1 = ToCanvasY(ParseNumber(match.Groups["y1"].Value)),
                X2 = ToCanvasX(ParseNumber(match.Groups["x2"].Value)),
                Y2 = ToCanvasY(ParseNumber(match.Groups["y2"].Value)),
                Stroke = Brushes.Transparent,
                StrokeThickness = 10,
                Cursor = Cursors.Arrow,
                Tag = new ShapeVisual(match.Index, match.Length, ShapeKind.Path, match.Groups["options"].Value,
                    ParseNumber(match.Groups["x1"].Value), ParseNumber(match.Groups["y1"].Value),
                    ParseNumber(match.Groups["x2"].Value), ParseNumber(match.Groups["y2"].Value))
            };
            line.MouseLeftButtonDown += Shape_MouseLeftButtonDown;
            OverlayCanvas.Children.Add(line);
        }

        foreach (Match match in RectangleRegex.Matches(Editor.Text))
        {
            var x1 = ParseNumber(match.Groups["x1"].Value);
            var y1 = ParseNumber(match.Groups["y1"].Value);
            var x2 = ParseNumber(match.Groups["x2"].Value);
            var y2 = ParseNumber(match.Groups["y2"].Value);
            var left = Math.Min(ToCanvasX(x1), ToCanvasX(x2));
            var top = Math.Min(ToCanvasY(y1), ToCanvasY(y2));
            var rectangle = new Rectangle
            {
                Width = Math.Abs(x2 - x1) * CoordinateScale,
                Height = Math.Abs(y2 - y1) * CoordinateScale,
                Stroke = Brushes.Transparent,
                StrokeThickness = 10,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Arrow,
                Tag = new ShapeVisual(match.Index, match.Length, ShapeKind.Rectangle, match.Groups["options"].Value, x1, y1, x2, y2)
            };
            rectangle.MouseLeftButtonDown += Shape_MouseLeftButtonDown;
            Canvas.SetLeft(rectangle, left);
            Canvas.SetTop(rectangle, top);
            OverlayCanvas.Children.Add(rectangle);
        }

        foreach (Match match in EllipseRegex.Matches(Editor.Text))
        {
            var x = ParseNumber(match.Groups["x"].Value);
            var y = ParseNumber(match.Groups["y"].Value);
            var rx = Math.Abs(ParseNumber(match.Groups["rx"].Value));
            var ry = Math.Abs(ParseNumber(match.Groups["ry"].Value));
            var ellipse = new EllipseShape
            {
                Width = rx * 2 * CoordinateScale,
                Height = ry * 2 * CoordinateScale,
                Stroke = Brushes.Transparent,
                StrokeThickness = 10,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Arrow,
                Tag = new ShapeVisual(match.Index, match.Length, ShapeKind.Ellipse, match.Groups["options"].Value, x, y, rx, ry)
            };
            ellipse.MouseLeftButtonDown += Shape_MouseLeftButtonDown;
            Canvas.SetLeft(ellipse, ToCanvasX(x) - ellipse.Width / 2);
            Canvas.SetTop(ellipse, ToCanvasY(y) - ellipse.Height / 2);
            OverlayCanvas.Children.Add(ellipse);
        }

        foreach (Match match in NodeRegex.Matches(Editor.Text))
        {
            var info = new NodeVisual(
                match.Index,
                match.Length,
                match.Groups["prefix"].Value,
                match.Groups["suffix"].Value,
                ParseNumber(match.Groups["x"].Value),
                ParseNumber(match.Groups["y"].Value));
            var node = new Border
            {
                Width = 140,
                Height = 36,
                Padding = new Thickness(4, 2, 4, 2),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Tag = info,
            };
            node.Loaded += (_, _) => PositionNode(node, info.X, info.Y);
            node.MouseLeftButtonDown += Node_MouseLeftButtonDown;
            OverlayCanvas.Children.Add(node);
        }

        foreach (var point in legacyDiagram.Points)
        {
            var marker = CreateCrossMarker(
                point,
                point.IsBezierControl ? "Bézier control point · drag to shape the curve" :
                point.IsPolar ? "Polar coordinate · drag to move" : "TikZ coordinate · drag to move");
            Canvas.SetLeft(marker, ToCanvasX(point.Position.X) - marker.Width / 2);
            Canvas.SetTop(marker, ToCanvasY(point.Position.Y) - marker.Height / 2);
            marker.MouseLeftButtonDown += LegacyPoint_MouseLeftButtonDown;
            marker.MouseEnter += (_, _) => SetPolarGrid(point.IsPolar);
            OverlayCanvas.Children.Add(marker);
        }

        RefreshSelectionPresentation();

        if (legacyDiagram.Error is not null)
            ModeStatus.Text = "Parser waiting for complete TikZ syntax";
    }

    private void LegacyPoint_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsSelectionTool || sender is not FrameworkElement marker || marker.Tag is not LegacyTikzPoint point)
            return;
        BeginSelectionDrag(marker, point, new Point(ToCanvasX(point.Position.X), ToCanvasY(point.Position.Y)), e);
    }

    private void RenderTikzGridCommands()
    {
        foreach (Match match in GridCommandRegex.Matches(Editor.Text))
        {
            var x1 = ParseNumber(match.Groups["x1"].Value);
            var y1 = ParseNumber(match.Groups["y1"].Value);
            var x2 = ParseNumber(match.Groups["x2"].Value);
            var y2 = ParseNumber(match.Groups["y2"].Value);
            var left = Math.Min(x1, x2);
            var right = Math.Max(x1, x2);
            var bottom = Math.Min(y1, y2);
            var top = Math.Max(y1, y2);
            var optionText = match.Groups["options"].Value + match.Groups["gridoptions"].Value;
            var step = ParseTikzGridStep(optionText);
            if (step <= 0)
                step = 1;

            var style = TikzStyleResolver.Resolve(optionText, Color.FromRgb(112, 120, 132));
            var verticalCount = (int)Math.Ceiling((right - left) / step);
            var horizontalCount = (int)Math.Ceiling((top - bottom) / step);
            if (verticalCount + horizontalCount > 1000)
                continue;

            for (var index = 0; index <= verticalCount; index++)
            {
                var x = Math.Min(left + index * step, right);
                OverlayCanvas.Children.Add(new LineShape
                {
                    X1 = ToCanvasX(x), Y1 = ToCanvasY(bottom),
                    X2 = ToCanvasX(x), Y2 = ToCanvasY(top),
                    Stroke = style.Stroke, StrokeThickness = style.Thickness,
                    StrokeDashArray = style.DashArray, IsHitTestVisible = false
                });
            }
            for (var index = 0; index <= horizontalCount; index++)
            {
                var y = Math.Min(bottom + index * step, top);
                OverlayCanvas.Children.Add(new LineShape
                {
                    X1 = ToCanvasX(left), Y1 = ToCanvasY(y),
                    X2 = ToCanvasX(right), Y2 = ToCanvasY(y),
                    Stroke = style.Stroke, StrokeThickness = style.Thickness,
                    StrokeDashArray = style.DashArray, IsHitTestVisible = false
                });
            }
        }
    }

    private static double ParseTikzGridStep(string options)
    {
        var match = GridStepRegex.Match(options);
        if (!match.Success)
            return 1;
        var value = ParseNumber(match.Groups["value"].Value);
        return match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "mm" => value / 10,
            "pt" => value / 28.45,
            _ => value
        };
    }

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsSelectionTool || sender is not Border node || node.Tag is not NodeVisual info)
            return;

        BeginSelectionDrag(node, info, new Point(ToCanvasX(info.X), ToCanvasY(info.Y)), e);
    }

    private void Shape_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsSelectionTool || sender is not Shape shape || shape.Tag is not ShapeVisual info)
            return;

        var center = shape is LineShape line
            ? new Point((line.X1 + line.X2) / 2, (line.Y1 + line.Y2) / 2)
            : new Point(Canvas.GetLeft(shape) + shape.Width / 2, Canvas.GetTop(shape) + shape.Height / 2);
        BeginSelectionDrag(shape, info, center, e);
    }

    private void AddSelectionHandles(ShapeVisual shape)
    {
        var points = shape.Kind switch
        {
            ShapeKind.Path => new[]
            {
                new Point(ToCanvasX(shape.X1), ToCanvasY(shape.Y1)),
                new Point(ToCanvasX(shape.X2), ToCanvasY(shape.Y2))
            },
            ShapeKind.Rectangle => new[]
            {
                new Point(ToCanvasX(Math.Min(shape.X1, shape.X2)), ToCanvasY(Math.Max(shape.Y1, shape.Y2))),
                new Point(ToCanvasX(Math.Max(shape.X1, shape.X2)), ToCanvasY(Math.Max(shape.Y1, shape.Y2))),
                new Point(ToCanvasX(Math.Max(shape.X1, shape.X2)), ToCanvasY(Math.Min(shape.Y1, shape.Y2))),
                new Point(ToCanvasX(Math.Min(shape.X1, shape.X2)), ToCanvasY(Math.Min(shape.Y1, shape.Y2)))
            },
            _ => new[]
            {
                new Point(ToCanvasX(shape.X1 + shape.X2), ToCanvasY(shape.Y1)),
                new Point(ToCanvasX(shape.X1 - shape.X2), ToCanvasY(shape.Y1)),
                new Point(ToCanvasX(shape.X1), ToCanvasY(shape.Y1 + shape.Y2)),
                new Point(ToCanvasX(shape.X1), ToCanvasY(shape.Y1 - shape.Y2))
            }
        };

        for (var index = 0; index < points.Length; index++)
        {
            var handle = CreateCrossMarker(
                new HandleVisual(shape, index),
                "Drag control point");
            Canvas.SetLeft(handle, points[index].X - handle.Width / 2);
            Canvas.SetTop(handle, points[index].Y - handle.Height / 2);
            handle.MouseLeftButtonDown += Handle_MouseLeftButtonDown;
            handle.MouseMove += Handle_MouseMove;
            handle.MouseLeftButtonUp += Handle_MouseLeftButtonUp;
            OverlayCanvas.Children.Add(handle);
        }
    }

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsSelectionTool || sender is not FrameworkElement handle || handle.Tag is not HandleVisual info)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _canvasSelection.Click(SelectionKey(info)!.Value, true);
            RefreshSelectionPresentation();
            e.Handled = true;
            return;
        }
        // In a multi-selection a marker is a grip for the group, not a request
        // to resize just its owner. Single-shape handles retain resize behavior.
        if (_canvasSelection.IncludesVisual(SelectionKey(info)!.Value) &&
            CanvasSelection.Outermost(_canvasSelection.Keys).Count > 1)
        {
            BeginSelectionDrag(handle, info,
                new Point(Canvas.GetLeft(handle) + handle.Width / 2, Canvas.GetTop(handle) + handle.Height / 2), e);
            return;
        }
        _draggedHandle = handle;
        _draggedHandleInfo = info;
        _controlDrag.Begin(e.GetPosition(GraphicalScroller), e.GetPosition(OverlayCanvas),
            new Point(Canvas.GetLeft(handle) + handle.Width / 2, Canvas.GetTop(handle) + handle.Height / 2));
        handle.CaptureMouse();
        e.Handled = true;
    }

    private void Handle_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedHandle is null || e.LeftButton != MouseButtonState.Pressed)
            return;
        if (!TryGetControlDragPoint(e, out var point)) return;
        UpdateMousePosition(point);
        Canvas.SetLeft(_draggedHandle, point.X - _draggedHandle.Width / 2);
        Canvas.SetTop(_draggedHandle, point.Y - _draggedHandle.Height / 2);
        e.Handled = true;
    }

    private void Handle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedHandle is null || _draggedHandleInfo is null)
            return;

        if (!TryGetControlDragPoint(e, out var point))
        {
            _draggedHandle.ReleaseMouseCapture();
            _draggedHandle = null;
            _draggedHandleInfo = null;
            e.Handled = true;
            return;
        }
        var x = FromCanvasX(point.X);
        var y = FromCanvasY(point.Y);
        var shape = _draggedHandleInfo.Shape;
        var x1 = shape.X1;
        var y1 = shape.Y1;
        var x2 = shape.X2;
        var y2 = shape.Y2;

        if (shape.Kind == ShapeKind.Path)
        {
            if (_draggedHandleInfo.Index == 0) { x1 = x; y1 = y; }
            else { x2 = x; y2 = y; }
        }
        else if (shape.Kind == ShapeKind.Rectangle)
        {
            var left = Math.Min(shape.X1, shape.X2);
            var right = Math.Max(shape.X1, shape.X2);
            var bottom = Math.Min(shape.Y1, shape.Y2);
            var top = Math.Max(shape.Y1, shape.Y2);
            (x1, y1, x2, y2) = _draggedHandleInfo.Index switch
            {
                0 => (x, y, right, bottom),
                1 => (x, y, left, bottom),
                2 => (x, y, left, top),
                _ => (x, y, right, top)
            };
        }
        else
        {
            if (_draggedHandleInfo.Index < 2)
                x2 = Math.Abs(x - shape.X1);
            else
                y2 = Math.Abs(y - shape.Y1);
        }

        ReplaceShapeSource(shape, x1, y1, x2, y2);
        _draggedHandle.ReleaseMouseCapture();
        _draggedHandle = null;
        _draggedHandleInfo = null;
        RenderWysiwyg();
        e.Handled = true;
    }

    private static FrameworkElement CreateCrossMarker(object tag, string tooltip)
    {
        return new ControlMarker(tag, tooltip);
    }

    private bool TryGetControlDragPoint(MouseEventArgs e, out Point point)
    {
        if (!_controlDrag.TryMove(e.GetPosition(GraphicalScroller), e.GetPosition(OverlayCanvas), out var position))
        {
            point = default;
            return false;
        }
        point = SnapPoint(ClampToCanvas(position));
        return true;
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (UpdateSelectionDrag(e) || UpdateMarquee(e)) return;
        var mousePoint = ClampToCanvas(e.GetPosition(OverlayCanvas));
        UpdateMousePosition(mousePoint);
        UpdateAdvancedDrawingPreview(SnapPoint(mousePoint));

        if (_drawingStart is null || _drawingPreview is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        UpdateDrawingPreview(_drawingStart.Value, ClampToCanvas(e.GetPosition(OverlayCanvas)));
    }

    private void OverlayCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_selectionDragElement is null && _draggedHandle is null && _drawingStart is null && _marqueeStart is null)
            MousePositionText.Text = "Mouse —";
    }

    private void UpdateMousePosition(Point point)
    {
        var displayedPoint = SnapToggle.IsChecked == true ? SnapPoint(point) : point;
        var label = SnapToggle.IsChecked == true ? "Snap" : "Mouse";
        MousePositionText.Text = $"{label}  {FormatNumber(FromCanvasX(displayedPoint.X))}, {FormatNumber(FromCanvasY(displayedPoint.Y))}";
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FinishSelectionDrag(e) || FinishMarquee(e)) return;
        if (_drawingStart is null)
            return;

        var start = _drawingStart.Value;
        var end = SnapPoint(ClampToCanvas(e.GetPosition(OverlayCanvas)));
        OverlayCanvas.ReleaseMouseCapture();
        _drawingStart = null;
        _drawingPreview = null;

        if ((end - start).Length < 4)
        {
            RenderWysiwyg();
            return;
        }

        InsertShapeCommand(start, end);
        SelectTool.IsChecked = true;
        RenderWysiwyg();
        e.Handled = true;
    }

    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TryAdvancedDrawingClick(e)) return;
        if (IsSelectionTool)
        {
            BeginMarquee(e);
            return;
        }
        if (NodeTool.IsChecked == true && e.OriginalSource is not Border)
        {
            var point = SnapPoint(ClampToCanvas(e.GetPosition(OverlayCanvas)));
            InsertTikzCommand($"    \\node at ({FormatNumber(FromCanvasX(point.X))},{FormatNumber(FromCanvasY(point.Y))}) {{node}};\n");
            SelectTool.IsChecked = true;
            RenderWysiwyg();
            e.Handled = true;
            return;
        }

        if (PathTool.IsChecked != true && RectangleTool.IsChecked != true && EllipseTool.IsChecked != true)
            return;

        _drawingStart = SnapPoint(ClampToCanvas(e.GetPosition(OverlayCanvas)));
        _drawingPreview = PathTool.IsChecked == true ? new LineShape() :
            RectangleTool.IsChecked == true ? new Rectangle() : new EllipseShape();
        _drawingPreview.Stroke = new SolidColorBrush(Color.FromRgb(255, 176, 32));
        _drawingPreview.StrokeThickness = 2;
        _drawingPreview.StrokeDashArray = [4, 3];
        _drawingPreview.Fill = new SolidColorBrush(Color.FromArgb(30, 255, 176, 32));
        OverlayCanvas.Children.Add(_drawingPreview);
        OverlayCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void UpdateDrawingPreview(Point start, Point end)
    {
        end = SnapPoint(end);
        if (_drawingPreview is LineShape line)
        {
            line.X1 = start.X;
            line.Y1 = start.Y;
            line.X2 = end.X;
            line.Y2 = end.Y;
            return;
        }

        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        _drawingPreview!.Width = Math.Abs(end.X - start.X);
        _drawingPreview.Height = Math.Abs(end.Y - start.Y);
        Canvas.SetLeft(_drawingPreview, left);
        Canvas.SetTop(_drawingPreview, top);
    }

    private void InsertShapeCommand(Point start, Point end)
    {
        string command;
        if (PathTool.IsChecked == true)
        {
            command = $"    \\draw ({FormatNumber(FromCanvasX(start.X))},{FormatNumber(FromCanvasY(start.Y))}) -- ({FormatNumber(FromCanvasX(end.X))},{FormatNumber(FromCanvasY(end.Y))});\n";
        }
        else if (RectangleTool.IsChecked == true)
        {
            command = $"    \\draw ({FormatNumber(FromCanvasX(start.X))},{FormatNumber(FromCanvasY(start.Y))}) rectangle ({FormatNumber(FromCanvasX(end.X))},{FormatNumber(FromCanvasY(end.Y))});\n";
        }
        else
        {
            var center = new Point((start.X + end.X) / 2, (start.Y + end.Y) / 2);
            var rx = Math.Abs(end.X - start.X) / (2 * CoordinateScale);
            var ry = Math.Abs(end.Y - start.Y) / (2 * CoordinateScale);
            command = $"    \\draw ({FormatNumber(FromCanvasX(center.X))},{FormatNumber(FromCanvasY(center.Y))}) ellipse ({FormatNumber(rx)} and {FormatNumber(ry)});\n";
        }
        InsertTikzCommand(command);
    }

    private void InsertTikzCommand(string command)
    {
        var insertionPoint = Editor.Text.LastIndexOf("\\end{tikzpicture}", StringComparison.Ordinal);
        if (insertionPoint < 0) insertionPoint = Editor.Text.LastIndexOf("\\end{circuitikz}", StringComparison.Ordinal);
        if (insertionPoint < 0)
            insertionPoint = Editor.Text.Length;
        Editor.Document.Insert(insertionPoint, command);
        QueueInteractivePreview();
    }

    private static Point ClampToCanvas(Point point) =>
        new(Math.Clamp(point.X, 0, CanvasSize), Math.Clamp(point.Y, 0, CanvasSize));

    private Point SnapPoint(Point point)
    {
        if (SnapToggle.IsChecked != true)
            return point;

        var step = CurrentGridStep * CoordinateScale;
        if (!_isPolarGrid)
        {
            var origin = CanvasSize / 2;
            return ClampToCanvas(new Point(
                origin + Math.Round((point.X - origin) / step, MidpointRounding.AwayFromZero) * step,
                origin + Math.Round((point.Y - origin) / step, MidpointRounding.AwayFromZero) * step));
        }

        var dx = point.X - CanvasSize / 2;
        var dy = CanvasSize / 2 - point.Y;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Atan2(dy, dx);
        var snappedRadius = Math.Round(radius / step, MidpointRounding.AwayFromZero) * step;
        var angularStep = Math.PI / 12;
        var snappedAngle = Math.Round(angle / angularStep, MidpointRounding.AwayFromZero) * angularStep;
        return ClampToCanvas(new Point(
            CanvasSize / 2 + Math.Cos(snappedAngle) * snappedRadius,
            CanvasSize / 2 - Math.Sin(snappedAngle) * snappedRadius));
    }

    private double CurrentGridStep
    {
        get
        {
            var text = (GridStepSelector?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0.5;
        }
    }

    private void UpdateGridPresentation()
    {
        if (PreviewGrid is null || GridKindText is null)
            return;

        var key = $"{_isPolarGrid}:{CurrentGridStep}";
        if (_gridPresentationKey == key)
            return;
        _gridPresentationKey = key;

        GridKindText.Text = _isPolarGrid ? "Polar · 15°" : "Cartesian";
        var spacing = Math.Max(2, CurrentGridStep * CoordinateScale);
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(226, 229, 233)), 0.5);

        if (!_isPolarGrid)
        {
            var grid = new DrawingGroup();
            grid.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, CanvasSize, CanvasSize))));
            var cartesianCenter = CanvasSize / 2;
            for (var x = cartesianCenter; x <= CanvasSize; x += spacing)
                grid.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(x, 0), new Point(x, CanvasSize))));
            for (var x = cartesianCenter - spacing; x >= 0; x -= spacing)
                grid.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(x, 0), new Point(x, CanvasSize))));
            for (var y = cartesianCenter; y <= CanvasSize; y += spacing)
                grid.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, y), new Point(CanvasSize, y))));
            for (var y = cartesianCenter - spacing; y >= 0; y -= spacing)
                grid.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, y), new Point(CanvasSize, y))));
            var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(175, 182, 190)), 0.8);
            grid.Children.Add(new GeometryDrawing(null, axisPen, new LineGeometry(new Point(cartesianCenter, 0), new Point(cartesianCenter, CanvasSize))));
            grid.Children.Add(new GeometryDrawing(null, axisPen, new LineGeometry(new Point(0, cartesianCenter), new Point(CanvasSize, cartesianCenter))));
            PreviewGrid.Fill = new DrawingBrush(grid)
            {
                Viewport = new Rect(0, 0, CanvasSize, CanvasSize),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            return;
        }

        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, CanvasSize, CanvasSize))));
        var center = new Point(CanvasSize / 2, CanvasSize / 2);
        for (var radius = spacing; radius <= CanvasSize * 0.72; radius += spacing)
            group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(center, radius, radius)));
        for (var degrees = 0; degrees < 360; degrees += 15)
        {
            var radians = degrees * Math.PI / 180;
            var end = new Point(center.X + Math.Cos(radians) * CanvasSize, center.Y - Math.Sin(radians) * CanvasSize);
            group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(center, end)));
        }
        PreviewGrid.Fill = new DrawingBrush(group)
        {
            Viewport = new Rect(0, 0, CanvasSize, CanvasSize),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }

    private void SetPolarGrid(bool polar)
    {
        var mode = (CoordinateSystemSelector?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Auto";
        _isPolarGrid = mode == "Polar" || (mode == "Auto" && polar);
        UpdateGridPresentation();
    }

    private void UpdateCoordinateContext()
    {
        if (!IsLoaded || _selectionDragElement is not null || _draggedHandle is not null)
            return;
        var offset = Editor.CaretOffset;
        var line = Editor.Document.GetLineByOffset(offset);
        var coordinates = Regex.Matches(Editor.Document.GetText(line), @"\([^()]*\)");
        Match? nearest = coordinates.Cast<Match>().OrderBy(match =>
            Math.Abs(line.Offset + match.Index + match.Length / 2 - offset)).FirstOrDefault();
        SetPolarGrid(nearest is not null && nearest.Value.Contains(':'));
    }

    private void ReplaceShapeSource(ShapeVisual shape, double x1, double y1, double x2, double y2)
    {
        var replacement = shape.Kind switch
        {
            ShapeKind.Path => $"\\draw{shape.Options} ({FormatNumber(x1)},{FormatNumber(y1)}) -- ({FormatNumber(x2)},{FormatNumber(y2)});",
            ShapeKind.Rectangle => $"\\draw{shape.Options} ({FormatNumber(x1)},{FormatNumber(y1)}) rectangle ({FormatNumber(x2)},{FormatNumber(y2)});",
            _ => $"\\draw{shape.Options} ({FormatNumber(x1)},{FormatNumber(y1)}) ellipse ({FormatNumber(Math.Abs(x2))} and {FormatNumber(Math.Abs(y2))});"
        };

        _updatingFromCanvas = true;
        Editor.Document.Replace(shape.Offset, shape.Length, replacement);
        _updatingFromCanvas = false;
        QueueInteractivePreview();
    }

    private void QueueInteractivePreview()
    {
        if (!IsLoaded || ProductionMode.IsChecked == true)
            return;

        _previewTimer.Stop();
        _interactivePreviewTimer.Stop();
        _interactivePreviewTimer.Start();
        ModeStatus.Text = "Updating TeX preview…";
    }

    private static double ParseNumber(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    private static string ConvertTexNodeText(string source)
    {
        var text = source.Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace("\\displaystyle", string.Empty, StringComparison.Ordinal)
            .Replace("\\textstyle", string.Empty, StringComparison.Ordinal)
            .Replace("\\cdot", "·", StringComparison.Ordinal)
            .Replace("\\times", "×", StringComparison.Ordinal)
            .Replace("\\pm", "±", StringComparison.Ordinal)
            .Replace("\\leq", "≤", StringComparison.Ordinal)
            .Replace("\\geq", "≥", StringComparison.Ordinal)
            .Replace("\\neq", "≠", StringComparison.Ordinal)
            .Replace("\\infty", "∞", StringComparison.Ordinal)
            .Replace("\\rightarrow", "→", StringComparison.Ordinal)
            .Replace("\\leftarrow", "←", StringComparison.Ordinal);

        var commands = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
            ["epsilon"] = "ε", ["theta"] = "θ", ["lambda"] = "λ", ["mu"] = "μ",
            ["pi"] = "π", ["rho"] = "ρ", ["sigma"] = "σ", ["phi"] = "φ",
            ["omega"] = "ω", ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ",
            ["Lambda"] = "Λ", ["Pi"] = "Π", ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Omega"] = "Ω"
        };
        text = Regex.Replace(text, @"\\(?<command>[A-Za-z]+)", match =>
            commands.TryGetValue(match.Groups["command"].Value, out var replacement) ? replacement : match.Value);
        text = Regex.Replace(text, @"\\frac\s*\{(?<top>[^{}]*)\}\s*\{(?<bottom>[^{}]*)\}", "${top}⁄${bottom}");
        text = Regex.Replace(text, @"\\(?:text|textrm|mathrm|mathbf|mathit)\s*\{(?<content>[^{}]*)\}", "${content}");
        text = Regex.Replace(text, @"\^\{(?<value>[^{}]+)\}|\^(?<single>[A-Za-z0-9+\-=()])", match =>
            ToScript(match.Groups["value"].Success ? match.Groups["value"].Value : match.Groups["single"].Value, true));
        text = Regex.Replace(text, @"_\{(?<value>[^{}]+)\}|_(?<single>[A-Za-z0-9+\-=()])", match =>
            ToScript(match.Groups["value"].Success ? match.Groups["value"].Value : match.Groups["single"].Value, false));
        return text.Replace("{", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string ToScript(string value, bool superscript)
    {
        const string normal = "0123456789+-=()abcdefghijklmnopqrstuvwxyz";
        const string superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼⁽⁾ᵃᵇᶜᵈᵉᶠᵍʰⁱʲᵏˡᵐⁿᵒᵖ𐞥ʳˢᵗᵘᵛʷˣʸᶻ";
        const string subscripts = "₀₁₂₃₄₅₆₇₈₉₊₋₌₍₎ₐbcdₑfghᵢⱼkₗₘₙₒₚqᵣₛₜᵤᵥwₓyz";
        var map = superscript ? superscripts : subscripts;
        var result = new StringBuilder();
        foreach (var character in value)
        {
            var index = normal.IndexOf(char.ToLowerInvariant(character));
            result.Append(index >= 0 && index < map.Length ? map[index] : character);
        }
        return result.ToString();
    }
    private static string FormatNumber(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
    private static double ToCanvasX(double x) => CanvasSize / 2 + x * CoordinateScale;
    private static double ToCanvasY(double y) => CanvasSize / 2 - y * CoordinateScale;
    private static double FromCanvasX(double x) => (x - CanvasSize / 2) / CoordinateScale;
    private static double FromCanvasY(double y) => (CanvasSize / 2 - y) / CoordinateScale;

    private static void PositionNode(FrameworkElement node, double x, double y)
    {
        Canvas.SetLeft(node, ToCanvasX(x) - node.ActualWidth / 2);
        Canvas.SetTop(node, ToCanvasY(y) - node.ActualHeight / 2);
    }

    private sealed record NodeVisual(int Offset, int Length, string Prefix, string Suffix, double X, double Y);
    private sealed record ShapeVisual(int Offset, int Length, ShapeKind Kind, string Options, double X1, double Y1, double X2, double Y2);
    private sealed record HandleVisual(ShapeVisual Shape, int Index);
}
