using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Automation;
using TikzEdt.Parser;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private EditorFindReplace? _findReplace;
    private void InitializeWorkspaceFeatures()
    {
        var editorHost = (Grid)Editor.Parent;
        editorHost.RowDefinitions.Insert(1, new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(Editor, 2);
        _findReplace = new EditorFindReplace(Editor) { Visibility = Visibility.Collapsed };
        Grid.SetRow(_findReplace, 1); editorHost.Children.Add(_findReplace);
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, e) => { _findReplace.Open(false); e.Handled = true; }));
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Replace, (_, e) => { _findReplace.Open(true); e.Handled = true; }));
        foreach (var button in WysiwygTools.Children.OfType<RadioButton>())
        {
            if (button.Content is not IconLabel label) continue;
            var title = label.Icon == "rectangle" ? "Rectangle" : label.Text;
            button.ToolTip = RichToolTip(title, button.ToolTip?.ToString() ?? title);
            AutomationProperties.SetName(button, title);
            label.Text = ""; button.Width = 34; button.Height = 32; button.Padding = new Thickness(6);
        }
        var toolbar = ((StackPanel)WysiwygTools.Parent).Children.OfType<WrapPanel>().First();
        foreach (var button in toolbar.Children.OfType<Button>())
            if (button.Content is IconLabel label && label.Icon is "new" or "open" or "save")
            {
                var title = label.Text;
                button.ToolTip = RichToolTip(title, label.Icon switch { "new" => "Create a new TikZ document (Ctrl+N).", "open" => "Open an existing TikZ or LaTeX source file (Ctrl+O).", _ => "Save the current source document (Ctrl+S)." });
                AutomationProperties.SetName(button, title); label.Text = ""; button.Width = 34; button.Height = 32; button.Padding = new Thickness(6);
            }
        var colors = new Button { Content = new IconLabel { Icon = "color" }, ToolTip = RichToolTip("Color picker", "Choose a color using RGB sliders, a palette, or a hex value. Insert a TikZ color or define a named color."), Width = 34, Padding = new Thickness(6) };
        colors.Click += ColorPicker_Click; toolbar.Children.Add(colors);
        var export = new Button { Content = new IconLabel { Icon = "export", Text = "Export" }, ToolTip = RichToolTip("Export figure", "Save the compiled figure as PDF, SVG, PNG, JPEG, TIFF, or BMP.") };
        export.Click += ExportFigure_Click; toolbar.Children.Add(export);
        var chemistry = new Button { Content = "Chemfig", ToolTip = RichToolTip("Chemfig molecule editor", "Draw atoms, bonds, branches and rings with an exact TeX preview. Place the caret inside a supported Chemfig command to edit it.") };
        chemistry.Click += Chemfig_Click; toolbar.Children.Add(chemistry);
        var symbols = new LatexSymbolsPanel();
        symbols.InsertRequested += InsertLatexSymbol;
        SymbolsSidebarTab.Content = symbols;
        Editor.Loaded += (_, _) => ApplyEditorScrollTheme();
    }
    private void ApplyEditorScrollTheme()
    {
        Editor.ApplyTemplate();
        foreach (var scroller in SidebarVisuals<ScrollViewer>(Editor))
        {
            scroller.Style = (Style)FindResource("EditorScrollViewerStyle");
            scroller.SetBinding(BackgroundProperty, new System.Windows.Data.Binding(nameof(Editor.Background)) { Source = Editor });
        }
    }
    internal static ToolTip RichToolTip(string title, string description)
    {
        var panel = new StackPanel { MaxWidth = 320 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        return new ToolTip { Content = panel };
    }
    private bool HandleWorkspaceShortcut(KeyEventArgs e)
    {
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (!control) return false;
        if (e.Key is Key.F or Key.H && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        { _findReplace?.Open(e.Key == Key.H); e.Handled = true; return true; }
        if (HandleDocumentHistory(e.Key, Keyboard.Modifiers, Keyboard.FocusedElement is TextBoxBase)) { e.Handled = true; return true; }
        return false;
    }
    internal bool HandleDocumentHistory(Key key, ModifierKeys modifiers, bool textInputFocused)
    {
        if (!modifiers.HasFlag(ModifierKeys.Control) || textInputFocused || key is not (Key.Z or Key.Y)) return false;
        CancelCanvasInteraction();
        if (key == Key.Y || modifiers.HasFlag(ModifierKeys.Shift)) Redo_Click(this, new RoutedEventArgs()); else Undo_Click(this, new RoutedEventArgs());
        return true;
    }
    private void UpdateActiveCoordinateFrame(int offset)
    {
        if (_cachedDiagramSource != Editor.Text) { _cachedDiagramSource = Editor.Text; _cachedDiagram = LegacyTikzAdapter.Parse(Editor.Text); }
        var diagram = _cachedDiagram!;
        var frame = diagram.CoordinateRegions.Where(scope => offset >= scope.Offset && offset <= scope.Offset + scope.Length)
            .OrderBy(scope => scope.Length).Select(scope => scope.Frame).FirstOrDefault(diagram.RootFrame);
        var point = diagram.Points.FirstOrDefault(point => offset >= point.Offset && offset <= point.Offset + point.Length);
        if (point?.Item.parent.GetCurrentTransformAt(point.Item, out var transform) == true) frame = LegacyTikzAdapter.Frame(transform);
        _coordinateFrame = frame;
        UpdateGridPresentation();
    }
    private EditorCoordinateFrame FrameForOffset(int offset) => _cachedDiagram?.CoordinateRegions.Where(region => offset >= region.Offset && offset <= region.Offset + region.Length)
        .OrderBy(region => region.Length).Select(region => region.Frame).FirstOrDefault(_cachedDiagram.RootFrame) ?? EditorCoordinateFrame.Identity;
    private Point ToFrameCanvas(Point local, EditorCoordinateFrame frame) { var world = frame.ToWorld(local); return new(ToCanvasX(world.X), ToCanvasY(world.Y)); }
    private System.Windows.Shapes.Shape TransformHitShape(System.Windows.Shapes.Shape shape)
    {
        if (shape.Tag is not ShapeVisual info) return shape;
        var frame = FrameForOffset(info.Offset);
        if (frame.Transform.IsIdentity) return shape;
        Geometry geometry = info.Kind switch
        {
            ShapeKind.Path => new LineGeometry(new(ToCanvasX(info.X1), ToCanvasY(info.Y1)), new(ToCanvasX(info.X2), ToCanvasY(info.Y2))),
            ShapeKind.Rectangle => new RectangleGeometry(new Rect(new Point(ToCanvasX(info.X1), ToCanvasY(info.Y1)), new Point(ToCanvasX(info.X2), ToCanvasY(info.Y2)))),
            _ => new EllipseGeometry(new(ToCanvasX(info.X1), ToCanvasY(info.Y1)), info.X2 * CoordinateScale, info.Y2 * CoordinateScale)
        };
        var value = frame.Transform; var center = CanvasSize / 2;
        geometry.Transform = new MatrixTransform(new Matrix(value.M11, -value.M12, -value.M21, value.M22,
            center * (1 - value.M11 + value.M21) + value.OffsetX * CoordinateScale, center * (1 + value.M12 - value.M22) - value.OffsetY * CoordinateScale));
        var hit = new System.Windows.Shapes.Path { Data = geometry, Stroke = Brushes.Transparent, StrokeThickness = 10, Fill = shape.Fill, Tag = info, Cursor = Cursors.Arrow };
        hit.MouseLeftButtonDown += Shape_MouseLeftButtonDown; return hit;
    }
    private Vector LocalDelta(int offset, Vector worldDelta)
    {
        var inverse = FrameForOffset(offset).Transform; inverse.Invert(); return inverse.Transform(worldDelta);
    }
    private string? TranslateSourceRange(int offset, int length, Vector delta)
    {
        var points = _cachedDiagram?.Points.Where(point => point.Offset >= offset && point.Offset + point.Length <= offset + length).OrderBy(point => point.Offset).ToArray();
        if (points is not { Length: > 0 }) return null;
        var changes = points.Select(point => (Offset: point.Offset - offset, point.Length, Text: LegacyTikzAdapter.MovePoint(point, point.Position + delta))).ToArray();
        var result = Editor.Document.GetText(offset, length);
        foreach (var change in changes.Reverse()) result = result.Remove(change.Offset, change.Length).Insert(change.Offset, change.Text);
        return result;
    }
    private Point CanvasToCode(Point point) => _coordinateFrame.ToLocal(new(FromCanvasX(point.X), FromCanvasY(point.Y)));
    private void DrawScopeGuides(LegacyTikzDiagram diagram)
    {
        if (WysiwygMode.IsChecked != true) return;
        foreach (var scope in diagram.Scopes)
        {
            if (scope.Bounds.IsEmpty) continue;
            var bounds = new Rect(new Point(ToCanvasX(scope.Bounds.Left), ToCanvasY(scope.Bounds.Bottom)), new Point(ToCanvasX(scope.Bounds.Right), ToCanvasY(scope.Bounds.Top)));
            bounds.Inflate(7, 7);
            var guide = new Rectangle { Width = bounds.Width, Height = bounds.Height, Stroke = new SolidColorBrush(Color.FromArgb(42, 100, 118, 145)), StrokeThickness = 0.65 / GraphicalScale.ScaleX, StrokeDashArray = [5, 5], IsHitTestVisible = false, Tag = "scope-guide" };
            Canvas.SetLeft(guide, bounds.Left); Canvas.SetTop(guide, bounds.Top); OverlayCanvas.Children.Add(guide);
        }
    }
    private void SymbolsSidebar_Click(object sender, RoutedEventArgs e) { SetSidebarVisible(true); SidebarTabs.SelectedItem = SymbolsSidebarTab; }
    private void InsertLatexSymbol(string command)
    {
        var text = LatexSymbolCatalog.IsMathContext(Editor.Text, Editor.SelectionStart) ? command + " " : "$" + command + "$";
        Editor.Document.Replace(Editor.SelectionStart, Editor.SelectionLength, text); Editor.Focus();
    }
    private void ColorPicker_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPickerWindow { Owner = this };
        if (picker.ShowDialog() != true) return;
        if (!picker.DefineNamedColor) Editor.Document.Replace(Editor.SelectionStart, Editor.SelectionLength, picker.TikzColor);
        else
        {
            var insertion = Editor.Text.IndexOf("\\begin{tikzpicture}", StringComparison.Ordinal);
            if (insertion < 0) insertion = Editor.Text.IndexOf("\\begin{circuitikz}", StringComparison.Ordinal);
            Editor.Document.Insert(Math.Max(0, insertion), $"\\definecolor{{{picker.ColorName}}}{{RGB}}{{{picker.RgbValues}}}\n");
        }
        Editor.Focus();
    }
}
