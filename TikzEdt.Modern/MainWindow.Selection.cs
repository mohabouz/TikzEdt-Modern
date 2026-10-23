using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Shape = System.Windows.Shapes.Shape;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private readonly CanvasSelection _canvasSelection = new();
    private string? _selectionSource;
    private FrameworkElement? _selectionDragElement;
    private Point _selectionDragOrigin;
    private Vector _selectionDragDelta;
    private Point? _marqueeStart;
    private Rectangle? _marquee;
    private readonly ControlDragGesture _marqueeGesture = new();
    private HashSet<CanvasSelectionKey> _marqueeBase = [];
    private readonly Dictionary<FrameworkElement, Transform> _selectionTransforms = [];

    private static CanvasSelectionKey? SelectionKey(object? tag) => tag switch
    {
        ShapeVisual shape => new(shape.Offset, shape.Length, "shape"),
        NodeVisual node => new(node.Offset, node.Length, "node"),
        LegacyTikzPoint point => new(point.Offset, point.Length, "point"),
        HandleVisual handle => new(handle.Shape.Offset, handle.Shape.Length, "shape"),
        _ => null
    };

    private void BeginSelectionDrag(FrameworkElement element, object tag, Point center, MouseButtonEventArgs e)
    {
        if (_selectionSource != Editor.Text)
        {
            RenderWysiwyg();
            e.Handled = true;
            return;
        }
        if (SelectionKey(tag) is not { } key) return;
        if (e.ClickCount == 2 && TryEditChemfigRange(key.Offset, key.Length)) { e.Handled = true; return; }
        var toggle = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var retained = _canvasSelection.Click(key, toggle);
        _selectionSource = Editor.Text;
        if (tag is LegacyTikzPoint point) SetPolarGrid(point.IsPolar);
        else SetPolarGrid(false);
        Editor.Select(key.Offset, Math.Min(key.Length, Editor.Document.TextLength - key.Offset));
        OverlayCanvas.Focusable = true;
        OverlayCanvas.Focus();
        RefreshSelectionPresentation();
        e.Handled = true;
        // Ctrl+click changes membership only. It never moves a control.
        if (!retained || toggle) return;
        _selectionDragElement = element;
        _selectionDragOrigin = center;
        _selectionDragDelta = default;
        _controlDrag.Begin(e.GetPosition(GraphicalScroller), e.GetPosition(OverlayCanvas), center);
        _selectionTransforms.Clear();
        foreach (var visual in OverlayCanvas.Children.OfType<FrameworkElement>())
        {
            if (SelectionKey(visual.Tag) is { } visualKey && _canvasSelection.IncludesVisual(visualKey))
                _selectionTransforms[visual] = visual.RenderTransform;
        }
        OverlayCanvas.CaptureMouse();
    }

    private bool UpdateSelectionDrag(MouseEventArgs e)
    {
        if (_selectionDragElement is null) return false;
        if (TryGetControlDragPoint(e, out var point))
        {
            var delta = point - _selectionDragOrigin;
            _selectionDragDelta = delta;
            foreach (var (visual, original) in _selectionTransforms)
            {
                var transforms = new TransformGroup();
                transforms.Children.Add(original);
                transforms.Children.Add(new TranslateTransform(delta.X, delta.Y));
                visual.RenderTransform = transforms;
            }
            UpdateMousePosition(point);
        }
        e.Handled = true;
        return true;
    }

    private bool FinishSelectionDrag(MouseButtonEventArgs e)
    {
        if (_selectionDragElement is null) return false;
        var moved = TryGetControlDragPoint(e, out var point);
        foreach (var (visual, original) in _selectionTransforms) visual.RenderTransform = original;
        _selectionTransforms.Clear();
        _selectionDragElement = null;
        OverlayCanvas.ReleaseMouseCapture();
        if (moved)
        {
            try { MoveCanvasSelection((point - _selectionDragOrigin) / CoordinateScale); }
            catch (Exception exception) { ModeStatus.Text = "Selection could not be moved: " + exception.Message; }
        }
        e.Handled = true;
        return true;
    }

    private void MoveCanvasSelection(Vector canvasDelta)
    {
        var delta = new Vector(canvasDelta.X, -canvasDelta.Y);
        var keys = CanvasSelection.Outermost(_canvasSelection.Keys);
        var tags = OverlayCanvas.Children.OfType<FrameworkElement>().Where(visual => visual.Tag is not HandleVisual)
            .Where(visual => SelectionKey(visual.Tag) is { } key && keys.Contains(key))
            .Select(visual => visual.Tag).DistinctBy(tag => SelectionKey(tag)).OrderBy(tag => SelectionKey(tag)!.Value.Offset).ToArray();
        var changes = new List<(CanvasSelectionKey Key, string Text)>();
        foreach (var tag in tags)
        {
            var key = SelectionKey(tag)!.Value;
            var text = tag switch
            {
                LegacyTikzPoint legacy => LegacyTikzAdapter.MovePoint(legacy, legacy.Position + delta),
                NodeVisual node => TranslateSourceRange(node.Offset, node.Length, delta) ?? $"{node.Prefix}({FormatNumber(node.X + LocalDelta(node.Offset, delta).X)},{FormatNumber(node.Y + LocalDelta(node.Offset, delta).Y)}){node.Suffix}",
                ShapeVisual shape => TranslateSourceRange(shape.Offset, shape.Length, delta) ?? ShapeSource(shape, LocalDelta(shape.Offset, delta)),
                _ => null
            };
            if (text is not null) changes.Add((key, text));
        }
        _updatingFromCanvas = true;
        try
        {
            using (Editor.Document.RunUpdate())
                foreach (var change in changes.OrderByDescending(change => change.Key.Offset))
                    Editor.Document.Replace(change.Key.Offset, change.Key.Length, change.Text);
        }
        finally { _updatingFromCanvas = false; }
        _canvasSelection.Keys.Clear();
        var shift = 0;
        foreach (var change in changes.OrderBy(change => change.Key.Offset))
        {
            _canvasSelection.Keys.Add(new(change.Key.Offset + shift, change.Text.Length, change.Key.Kind));
            shift += change.Text.Length - change.Key.Length;
        }
        _selectionSource = Editor.Text;
        RenderWysiwyg();
        QueueInteractivePreview();
    }

    private static string ShapeSource(ShapeVisual shape, Vector delta) => shape.Kind switch
    {
        ShapeKind.Path => $"\\draw{shape.Options} ({FormatNumber(shape.X1 + delta.X)},{FormatNumber(shape.Y1 + delta.Y)}) -- ({FormatNumber(shape.X2 + delta.X)},{FormatNumber(shape.Y2 + delta.Y)});",
        ShapeKind.Rectangle => $"\\draw{shape.Options} ({FormatNumber(shape.X1 + delta.X)},{FormatNumber(shape.Y1 + delta.Y)}) rectangle ({FormatNumber(shape.X2 + delta.X)},{FormatNumber(shape.Y2 + delta.Y)});",
        _ => $"\\draw{shape.Options} ({FormatNumber(shape.X1 + delta.X)},{FormatNumber(shape.Y1 + delta.Y)}) ellipse ({FormatNumber(shape.X2)} and {FormatNumber(shape.Y2)});"
    };

    private void DeleteCanvasSelection()
    {
        var ranges = new List<CanvasSelectionKey>();
        foreach (var visual in OverlayCanvas.Children.OfType<FrameworkElement>())
        {
            if (visual.Tag is HandleVisual || SelectionKey(visual.Tag) is not { } key || !_canvasSelection.Keys.Contains(key)) continue;
            if (visual.Tag is LegacyTikzPoint point)
            {
                TikzEdt.Parser.TikzParseItem? command = point.Item is TikzEdt.Parser.Tikz_Node
                    ? point.Item : point.Item.Ancestors.FirstOrDefault(ancestor => ancestor is TikzEdt.Parser.Tikz_Path);
                if (command is not null) ranges.Add(new(command.StartPosition(), command.ToString().Length, "command"));
            }
            else ranges.Add(key);
        }
        using (Editor.Document.RunUpdate())
            foreach (var key in CanvasSelection.Outermost(ranges).OrderByDescending(key => key.Offset))
                Editor.Document.Remove(key.Offset, key.Length);
        _canvasSelection.Keys.Clear();
        _selectionSource = Editor.Text;
        RenderWysiwyg();
        QueueInteractivePreview();
    }

    private void BeginMarquee(MouseButtonEventArgs e)
    {
        _marqueeStart = e.GetPosition(OverlayCanvas);
        _marqueeBase = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? [.. _canvasSelection.Keys] : [];
        _marqueeGesture.Begin(e.GetPosition(GraphicalScroller), _marqueeStart.Value, _marqueeStart.Value);
        _marquee = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(0, 102, 204)), StrokeThickness = 0.8 / GraphicalScale.ScaleX,
            Fill = new SolidColorBrush(Color.FromArgb(24, 0, 102, 204)), IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };
        OverlayCanvas.Children.Add(_marquee);
        OverlayCanvas.Focusable = true;
        OverlayCanvas.Focus();
        OverlayCanvas.CaptureMouse();
        e.Handled = true;
    }

    private bool UpdateMarquee(MouseEventArgs e)
    {
        if (_marqueeStart is null || _marquee is null) return false;
        if (_marqueeGesture.TryMove(e.GetPosition(GraphicalScroller), e.GetPosition(OverlayCanvas), out var end))
        {
            var bounds = new Rect(_marqueeStart.Value, end);
            Canvas.SetLeft(_marquee, bounds.Left);
            Canvas.SetTop(_marquee, bounds.Top);
            _marquee.Width = bounds.Width;
            _marquee.Height = bounds.Height;
            _marquee.Visibility = Visibility.Visible;
            SelectCanvasRegion(bounds);
            UpdateControlMarkers();
        }
        e.Handled = true;
        return true;
    }

    private bool FinishMarquee(MouseButtonEventArgs e)
    {
        if (_marqueeStart is null || _marquee is null) return false;
        var moved = _marqueeGesture.TryMove(e.GetPosition(GraphicalScroller), e.GetPosition(OverlayCanvas), out var end);
        if (moved)
            SelectCanvasRegion(new Rect(_marqueeStart.Value, end));
        else
        {
            _canvasSelection.Keys.Clear();
            _canvasSelection.Keys.UnionWith(_marqueeBase);
        }
        OverlayCanvas.Children.Remove(_marquee);
        _marquee = null;
        _marqueeStart = null;
        _selectionSource = Editor.Text;
        OverlayCanvas.ReleaseMouseCapture();
        RefreshSelectionPresentation();
        e.Handled = true;
        return true;
    }

    private void SelectCanvasRegion(Rect bounds)
    {
        _canvasSelection.Keys.Clear();
        _canvasSelection.Keys.UnionWith(_marqueeBase);
        foreach (var visual in OverlayCanvas.Children.OfType<FrameworkElement>())
        {
            // Synthetic resize handles represent their owner, not a separate source coordinate.
            if (visual.Tag is HandleVisual || SelectionKey(visual.Tag) is not { } key) continue;
            if (visual is ControlMarker)
            {
                // Test the coordinate itself, independent of layout and the zoom-compensated X glyph.
                var center = new Point(Canvas.GetLeft(visual) + visual.Width / 2,
                    Canvas.GetTop(visual) + visual.Height / 2);
                if (bounds.Contains(center)) _canvasSelection.Keys.Add(key);
                continue;
            }
            var visualBounds = VisualTreeHelper.GetDescendantBounds(visual);
            if (!visualBounds.IsEmpty && bounds.Contains(visual.TransformToAncestor(OverlayCanvas).TransformBounds(visualBounds)))
                _canvasSelection.Keys.Add(key);
        }
    }

    private void RefreshSelectionPresentation()
    {
        foreach (var handle in OverlayCanvas.Children.OfType<FrameworkElement>().Where(visual => visual.Tag is HandleVisual).ToArray())
            OverlayCanvas.Children.Remove(handle);
        var selectedShapes = new List<ShapeVisual>();
        foreach (var visual in OverlayCanvas.Children.OfType<FrameworkElement>())
        {
            var selected = SelectionKey(visual.Tag) is { } key && _canvasSelection.Keys.Contains(key);
            if (visual is Shape shape && visual.Tag is ShapeVisual shapeInfo)
            {
                shape.Stroke = selected ? new SolidColorBrush(Color.FromRgb(0, 102, 204)) : Brushes.Transparent;
                shape.StrokeThickness = selected ? 0.65 / GraphicalScale.ScaleX : 10 / GraphicalScale.ScaleX;
                if (selected) selectedShapes.Add(shapeInfo);
            }
            if (visual is Border node && visual.Tag is NodeVisual)
            {
                node.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 102, 204));
                node.BorderThickness = new Thickness(selected ? 0.65 / GraphicalScale.ScaleX : 0);
            }
        }
        foreach (var shape in selectedShapes) AddSelectionHandles(shape);
        UpdateControlMarkers();
        var count = CanvasSelection.Outermost(_canvasSelection.Keys).Count;
        ModeStatus.Text = count > 0 ? $"{count} selected · Ctrl+click to add/remove · drag to move" : "Drag a selection box or click an object · Ctrl+click for multiple selection";
    }

    private void UpdateControlMarkers()
    {
        foreach (var guide in OverlayCanvas.Children.OfType<Shape>().Where(shape => Equals(shape.Tag, "scope-guide"))) guide.StrokeThickness = 0.65 / GraphicalScale.ScaleX;
        foreach (var marker in OverlayCanvas.Children.OfType<ControlMarker>())
        {
            var selected = SelectionKey(marker.Tag) is { } key && _canvasSelection.Contains(key);
            marker.Update(GraphicalScale.ScaleX, selected, MarkerOverSimilarColor(marker, selected));
            if (_selectionDragElement is not null && _selectionTransforms.ContainsKey(marker))
            {
                _selectionTransforms[marker] = marker.RenderTransform;
                var transforms = new TransformGroup();
                transforms.Children.Add(marker.RenderTransform);
                transforms.Children.Add(new TranslateTransform(_selectionDragDelta.X, _selectionDragDelta.Y));
                marker.RenderTransform = transforms;
            }
        }
    }

    private void CancelCanvasInteraction()
    {
        if (_selectionDragElement is null && _marqueeStart is null) return;
        foreach (var (visual, original) in _selectionTransforms) visual.RenderTransform = original;
        _selectionTransforms.Clear();
        _selectionDragElement = null;
        if (_marquee is not null) OverlayCanvas.Children.Remove(_marquee);
        _marquee = null;
        _marqueeStart = null;
        if (OverlayCanvas.IsMouseCaptured) OverlayCanvas.ReleaseMouseCapture();
        RefreshSelectionPresentation();
    }

    private bool MarkerOverSimilarColor(ControlMarker marker, bool selected)
    {
        if (CompiledPreviewImage.Width <= 0 || CompiledPreviewImage.Height <= 0) return false;
        if (_compiledVector is not null)
        {
            // A tiny, on-demand colour probe keeps red/blue handles visible over
            // similar vector ink. The displayed figure itself remains vector-only.
            var xProbe = Canvas.GetLeft(marker) + marker.Width / 2 - Canvas.GetLeft(CompiledPreviewImage);
            var yProbe = Canvas.GetTop(marker) + marker.Height / 2 - Canvas.GetTop(CompiledPreviewImage);
            if (!double.IsFinite(xProbe) || !double.IsFinite(yProbe) || xProbe < 0 || yProbe < 0 || xProbe >= CompiledPreviewImage.Width || yProbe >= CompiledPreviewImage.Height) return false;
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawImage(_compiledVector, new Rect(2 - xProbe, 2 - yProbe, CompiledPreviewImage.Width, CompiledPreviewImage.Height));
            var probe = new RenderTargetBitmap(5, 5, 96, 96, PixelFormats.Pbgra32); probe.Render(visual);
            var samples = new byte[100]; probe.CopyPixels(samples, 20, 0);
            for (var i = 0; i < samples.Length; i += 4)
                if (samples[i + 3] >= 100 && (selected ? samples[i] > 120 && samples[i] > samples[i + 2] * 1.3 : samples[i + 2] > 120 && samples[i + 2] > samples[i + 1] * 1.4 && samples[i + 2] > samples[i] * 1.4)) return true;
            return false;
        }
        if (_compiledBitmap is null) return false;
        var x = (Canvas.GetLeft(marker) + marker.Width / 2 - Canvas.GetLeft(CompiledPreviewImage)) / CompiledPreviewImage.Width * _compiledBitmap.PixelWidth;
        var y = (Canvas.GetTop(marker) + marker.Height / 2 - Canvas.GetTop(CompiledPreviewImage)) / CompiledPreviewImage.Height * _compiledBitmap.PixelHeight;
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 || x >= _compiledBitmap.PixelWidth || y >= _compiledBitmap.PixelHeight) return false;
        var bitmap = _compiledBitmap.Format == PixelFormats.Bgra32 || _compiledBitmap.Format == PixelFormats.Pbgra32
            ? _compiledBitmap : new FormatConvertedBitmap(_compiledBitmap, PixelFormats.Bgra32, null, 0);
        var left = Math.Max(0, (int)x - 2);
        var top = Math.Max(0, (int)y - 2);
        var width = Math.Min(5, bitmap.PixelWidth - left);
        var height = Math.Min(5, bitmap.PixelHeight - top);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(new Int32Rect(left, top, width, height), pixels, width * 4, 0);
        for (var index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index + 3] < 100) continue;
            var blue = pixels[index]; var green = pixels[index + 1]; var red = pixels[index + 2];
            if (selected ? blue > 120 && blue > red * 1.3 : red > 120 && red > green * 1.4 && red > blue * 1.4) return true;
        }
        return false;
    }
}
