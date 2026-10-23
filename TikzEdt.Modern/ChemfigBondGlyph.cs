using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TikzEdt.Modern;

internal static class ChemfigBondGlyph
{
    public static Canvas Create(string kind)
    {
        var canvas = new Canvas { Width = 40, Height = 26, IsHitTestVisible = false };
        var ink = (Brush)Application.Current.FindResource("TextBrush");
        void Line(double x1, double y1, double x2, double y2) => canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = ink, StrokeThickness = 1.4 });
        if (kind.StartsWith('>') || kind.StartsWith('<'))
        {
            var tip = kind[0] == '>' ? 36d : 4d; var end = 40 - tip;
            if (kind.EndsWith(':'))
                for (var i = 1; i <= 7; i++) { var t = i / 7d; var x = tip + (end - tip) * t; Line(x, 13 - 6 * t, x, 13 + 6 * t); }
            else canvas.Children.Add(new Polygon { Points = new PointCollection { new(tip, 13), new(end, 7), new(end, 19) }, Stroke = ink, StrokeThickness = 1.2, Fill = kind.EndsWith('|') ? Brushes.Transparent : ink });
        }
        else
        {
            var count = kind == "=" ? 2 : kind == "~" ? 3 : 1;
            for (var i = 0; i < count; i++) { var y = 13 + (i - (count - 1) / 2d) * 5; Line(4, y, 36, y); }
        }
        return canvas;
    }
}
