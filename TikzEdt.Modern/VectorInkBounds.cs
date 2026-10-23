using System.Windows;
using System.Windows.Media;

namespace TikzEdt.Modern;

internal static class VectorInkBounds
{
    private static bool Visible(Brush? brush) => brush is not null && brush.Opacity > 0 && (brush is not SolidColorBrush solid || solid.Color.A > 0);
    public static Rect Measure(Drawing drawing)
    {
        if (drawing is DrawingGroup group)
        {
            if(group.Opacity<=0)return Rect.Empty;
            var bounds = Rect.Empty;
            foreach (var child in group.Children) bounds.Union(Measure(child));
            if (!bounds.IsEmpty && group.ClipGeometry is { } clip) bounds.Intersect(clip.Bounds);
            return bounds.IsEmpty || group.Transform is null ? bounds : group.Transform.TransformBounds(bounds);
        }
        if (drawing is GeometryDrawing geometry)
        {
            var fill = Visible(geometry.Brush); var stroke = geometry.Pen is { Thickness: > 0 } pen && Visible(pen.Brush);
            if (!fill && !stroke) return Rect.Empty;
            return stroke ? geometry.Geometry.GetRenderBounds(geometry.Pen) : geometry.Geometry.Bounds;
        }
        if (drawing is GlyphRunDrawing glyph) return Visible(glyph.ForegroundBrush) ? glyph.Bounds : Rect.Empty;
        return drawing is ImageDrawing ? drawing.Bounds : Rect.Empty;
    }
    public static DrawingImage Crop(DrawingImage source, Rect ink)
    {
        var moved = new DrawingGroup { Transform = new TranslateTransform(-ink.X,-ink.Y) }; moved.Children.Add(source.Drawing);
        var root = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0,0,ink.Width,ink.Height)) }; root.Children.Add(moved);
        root.Freeze(); var image = new DrawingImage(root); image.Freeze(); return image;
    }
}
