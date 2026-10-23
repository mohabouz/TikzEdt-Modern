using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace TikzEdt.Modern;

/// <summary>Small vector icons, independent of installed symbol fonts and DPI.</summary>
public sealed class ToolIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(ToolIcon), new FrameworkPropertyMetadata("path", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    private static readonly Dictionary<string, Geometry> Icons = new Dictionary<string, string>
    {
        ["color"] = "M12,2 C-1,2 -1,21 10,22 C16,23 12,16 17,16 C25,16 25,2 12,2 Z M7,7 L8,7 M14,5 L15,5 M19,9 L20,9 M6,13 L7,13",
        ["export"] = "M3,13 L3,22 22,22 22,13 M12,17 L12,2 M6,8 L12,2 18,8",
        ["new"] = "M5,2 L14,2 20,8 20,22 5,22 Z M14,2 L14,8 20,8 M8,14 L17,14 M12.5,10 L12.5,19",
        ["open"] = "M2,7 L9,7 11,4 21,4 21,9 M2,7 L2,21 20,21 23,10 6,10 2,21",
        ["save"] = "M3,3 L19,3 22,6 22,22 3,22 Z M7,3 L7,10 17,10 17,3 M7,22 L7,14 18,14 18,22",
        ["undo"] = "M8,4 L2,10 8,16 M2,10 L14,10 C23,10 23,21 14,21",
        ["redo"] = "M16,4 L22,10 16,16 M22,10 L10,10 C1,10 1,21 10,21",
        ["delete"] = "M3,6 L21,6 M8,6 L8,2 16,2 16,6 M5,6 L6,22 18,22 19,6 M10,10 L10,18 M14,10 L14,18",
        ["pan"] = "M5,13 L5,7 C5,4 8,4 8,7 L8,12 8,4 C8,1 11,1 11,4 L11,11 11,3 C11,0 14,0 14,3 L14,11 14,5 C14,2 17,2 17,5 L17,14 19,11 C21,9 23,11 21,14 L17,21 8,22 2,15 C0,12 3,10 5,13 Z",
        ["fit"] = "M3,9 L3,3 9,3 M15,3 L21,3 21,9 M21,15 L21,21 15,21 M9,21 L3,21 3,15 M7,7 L17,7 17,17 7,17 Z",
        ["pair"] = "M6,3 L18,3 M17,15 A5,5 0 1 1 7,15 A5,5 0 1 1 17,15 M19,7 L23,7 M21,5 L21,9",
        ["skeletal"] = "M2,17 L8,7 15,17 22,7",
        ["ring"] = "M12,2 L21,7 21,17 12,22 3,17 3,7 Z",
        ["group"] = "M3,12 L8,12 M16,12 L21,12 M12,3 L12,8 M12,16 L12,21 M16,12 A4,4 0 1 1 8,12 A4,4 0 1 1 16,12",
        ["select"] = "M4,2 L4,21 9,16 13,23 17,21 13,14 21,13 Z",
        ["edit"] = "M3,21 L5,14 17,2 22,7 10,19 Z M5,14 L10,19 M14,5 L19,10",
        ["node"] = "M3,5 L21,5 21,19 3,19 Z M7,9 L17,9 M7,14 L14,14",
        ["path"] = "M3,20 L21,4 M1,18 L5,18 5,22 1,22 Z M19,2 L23,2 23,6 19,6 Z",
        ["bezier"] = "M2,20 C4,1 20,23 22,4 M2,20 L5,3 M22,4 L18,21 M3,3 L7,3 M5,1 L5,5 M16,21 L20,21 M18,19 L18,23",
        ["smooth"] = "M2,18 C6,-2 11,25 15,10 S22,2 23,7 M2,16 L2,20 M13,10 L17,10 M21,7 L24,7",
        ["rectangle"] = "M3,5 L21,5 21,20 3,20 Z",
        ["ellipse"] = "M22,12 A10,7 0 1 1 2,12 A10,7 0 1 1 22,12",
        ["grid"] = "M3,3 L21,3 21,21 3,21 Z M9,3 L9,21 M15,3 L15,21 M3,9 L21,9 M3,15 L21,15",
        ["arc"] = "M3,20 A17,17 0 0 1 21,3 M1,18 L5,22 M19,1 L23,5",
        ["pie"] = "M12,12 L12,2 A10,10 0 1 1 2,12 Z",
        ["compile"] = "M6,3 L21,12 6,21 Z",
        ["copy"] = "M8,7 L21,7 21,22 8,22 Z M4,17 L2,17 2,2 16,2 16,4",
        ["preamble"] = "M8,3 L3,12 8,21 M16,3 L21,12 16,21 M14,2 L10,22",
        ["files"] = "M2,6 L9,6 11,3 22,3 22,21 2,21 Z M2,9 L22,9",
        ["snippets"] = "M3,3 L10,3 10,10 3,10 Z M14,3 L21,3 21,10 14,10 Z M3,14 L10,14 10,21 3,21 Z M14,14 L21,14 21,21 14,21 Z",
        ["fold"] = "M3,4 L21,4 M3,20 L21,20 M8,9 L12,13 16,9",
        ["unfold"] = "M3,4 L21,4 M3,20 L21,20 M8,14 L12,10 16,14"
    }.ToDictionary(pair => pair.Key, pair => { var geometry = Geometry.Parse(pair.Value); geometry.Freeze(); return geometry; });

    public ToolIcon() { Width = Height = 18; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!Icons.TryGetValue(Kind, out var geometry)) return;
        drawingContext.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        drawingContext.DrawGeometry(null, new Pen((Brush)GetValue(TextElement.ForegroundProperty), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);
        drawingContext.Pop();
    }
}
