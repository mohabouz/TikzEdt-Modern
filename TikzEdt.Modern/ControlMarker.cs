using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TikzEdt.Modern;

/// <summary>Constant-screen-size X anchors or circular Bézier controls, with a contrasting halo.</summary>
internal sealed class ControlMarker : Canvas
{
    private readonly Shape[] _glyph;
    private readonly Shape[] _halo;

    public ControlMarker(object tag, string tooltip)
    {
        Width = Height = 12;
        Background = Brushes.Transparent;
        Cursor = Cursors.Arrow;
        Tag = tag;
        ToolTip = tooltip;
        RenderTransformOrigin = new Point(0.5, 0.5);
        if (tag is LegacyTikzPoint { IsBezierControl: true })
        {
            var halo = new Ellipse { Width = 6, Height = 6, Stroke = Brushes.White, Fill = Brushes.White, IsHitTestVisible = false };
            var circle = new Ellipse { Width = 6, Height = 6, IsHitTestVisible = false };
            foreach (var shape in new[] { halo, circle }) { SetLeft(shape, 3); SetTop(shape, 3); Children.Add(shape); }
            _halo = [halo];
            _glyph = [circle];
        }
        else
        {
            foreach (var (x1, y1, x2, y2) in new[] { (3d, 3d, 9d, 9d), (9d, 3d, 3d, 9d) })
                Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
                    Stroke = Brushes.White, StrokeThickness = 1.8, IsHitTestVisible = false });
            _halo = Children.OfType<Shape>().ToArray();
            _glyph = [new Line { X1 = 3, Y1 = 3, X2 = 9, Y2 = 9 }, new Line { X1 = 9, Y1 = 3, X2 = 3, Y2 = 9 }];
            foreach (var line in _glyph) Children.Add(line);
        }
        foreach (var line in _glyph)
        {
            line.StrokeThickness = 0.65;
            line.IsHitTestVisible = false;
        }
        Update(1, false, false);
    }

    public void Update(double zoom, bool selected, bool similarBackground)
    {
        RenderTransform = new ScaleTransform(1 / Math.Max(0.01, zoom), 1 / Math.Max(0.01, zoom));
        // Selection must have one recognizable color, even where paths overlap.
        // Use the halo for contrast rather than changing selected markers to orange.
        var color = selected
            ? Color.FromRgb(0, 102, 204)
            : similarBackground ? Color.FromRgb(0, 136, 160) : Color.FromRgb(220, 38, 38);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        foreach (var line in _halo) line.StrokeThickness = selected ? 2.2 : 1.8;
        foreach (var line in _glyph)
        {
            line.Stroke = brush;
            line.StrokeThickness = selected ? 0.85 : 0.65;
        }
    }
}
