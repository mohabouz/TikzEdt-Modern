using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace TikzEdt.Modern;

internal sealed record TikzVisualStyle(Brush Stroke, Brush? Fill, double Thickness, DoubleCollection? DashArray);

internal static class TikzStyleResolver
{
    private static readonly Dictionary<string, Color> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Colors.Black, ["white"] = Colors.White, ["red"] = Colors.Red,
        ["green"] = Color.FromRgb(0, 150, 70), ["blue"] = Colors.Blue,
        ["cyan"] = Colors.Cyan, ["magenta"] = Colors.Magenta, ["yellow"] = Colors.Yellow,
        ["gray"] = Colors.Gray, ["grey"] = Colors.Gray, ["darkgray"] = Color.FromRgb(80, 80, 80),
        ["lightgray"] = Color.FromRgb(200, 200, 200), ["brown"] = Color.FromRgb(150, 75, 30),
        ["lime"] = Colors.Lime, ["olive"] = Colors.Olive, ["orange"] = Colors.Orange,
        ["pink"] = Colors.Pink, ["purple"] = Colors.Purple, ["teal"] = Colors.Teal,
        ["violet"] = Color.FromRgb(148, 0, 211)
    };

    public static TikzVisualStyle Resolve(string options, Color fallback, bool closedShape = false)
    {
        options = options.Trim().TrimStart('[').TrimEnd(']').Replace("=!", "!", StringComparison.Ordinal);
        var tokens = options.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var strokeColor = fallback;
        Color? fillColor = null;
        var thickness = 1.6;
        double opacity = 1;
        DoubleCollection? dash = null;

        foreach (var token in tokens)
        {
            var keyValue = token.Split('=', 2, StringSplitOptions.TrimEntries);
            var key = keyValue[0];
            var value = keyValue.Length == 2 ? keyValue[1] : string.Empty;

            if (key.Equals("draw", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) opacity = 0;
                else if (TryParseColor(value, out var color)) strokeColor = color;
            }
            else if (key.Equals("color", StringComparison.OrdinalIgnoreCase) && TryParseColor(value, out var color)) strokeColor = color;
            else if (key.Equals("fill", StringComparison.OrdinalIgnoreCase) && TryParseColor(value, out var fill)) fillColor = fill;
            else if ((key.Equals("opacity", StringComparison.OrdinalIgnoreCase) || key.Equals("draw opacity", StringComparison.OrdinalIgnoreCase)) &&
                     double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedOpacity)) opacity = Math.Clamp(parsedOpacity, 0, 1);
            else if (key.Equals("line width", StringComparison.OrdinalIgnoreCase)) thickness = ParseLineWidth(value, thickness);
            else if (key.Equals("ultra thin", StringComparison.OrdinalIgnoreCase)) thickness = 0.5;
            else if (key.Equals("very thin", StringComparison.OrdinalIgnoreCase)) thickness = 0.75;
            else if (key.Equals("thin", StringComparison.OrdinalIgnoreCase)) thickness = 1;
            else if (key.Equals("semithick", StringComparison.OrdinalIgnoreCase)) thickness = 1.5;
            else if (key.Equals("thick", StringComparison.OrdinalIgnoreCase)) thickness = 2.4;
            else if (key.Equals("very thick", StringComparison.OrdinalIgnoreCase)) thickness = 3.2;
            else if (key.Equals("ultra thick", StringComparison.OrdinalIgnoreCase)) thickness = 4.2;
            else if (key.Equals("dashed", StringComparison.OrdinalIgnoreCase)) dash = [5, 3];
            else if (key.Equals("dotted", StringComparison.OrdinalIgnoreCase)) dash = [1, 3];
            else if (TryParseColor(key, out var bareColor)) strokeColor = bareColor;
        }

        var strokeBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), strokeColor.R, strokeColor.G, strokeColor.B));
        Brush? fillBrush = null;
        if (fillColor is { } actualFill)
            fillBrush = new SolidColorBrush(Color.FromArgb((byte)(closedShape ? 180 * opacity : 90 * opacity), actualFill.R, actualFill.G, actualFill.B));
        return new TikzVisualStyle(strokeBrush, fillBrush, thickness, dash);
    }

    private static bool TryParseColor(string expression, out Color color)
    {
        var parts = expression.Trim().Split('!', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !NamedColors.TryGetValue(parts[0], out color))
            return false;
        if (parts.Length < 2 || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            return true;
        var other = parts.Length >= 3 && NamedColors.TryGetValue(parts[2], out var mixed) ? mixed : Colors.White;
        var ratio = Math.Clamp(percent / 100, 0, 1);
        color = Color.FromRgb(
            (byte)(color.R * ratio + other.R * (1 - ratio)),
            (byte)(color.G * ratio + other.G * (1 - ratio)),
            (byte)(color.B * ratio + other.B * (1 - ratio)));
        return true;
    }

    private static double ParseLineWidth(string value, double fallback)
    {
        var match = Regex.Match(value, @"(?<number>\d+(?:\.\d+)?)\s*(?<unit>pt|mm|cm)?", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups["number"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return fallback;
        return match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "cm" => number * 37.8,
            "mm" => number * 3.78,
            _ => number * 1.33
        };
    }
}
