using System.Windows;
using System.Text.RegularExpressions;
using System.Windows.Media;
using TikzEdt.Parser;

namespace TikzEdt.Modern;

internal sealed class LegacyTikzDiagram
{
    public List<LegacyTikzPoint> Points { get; } = [];
    public List<LegacyTikzSegment> Segments { get; } = [];
    public string? Error { get; set; }
    public EditorCoordinateFrame RootFrame { get; set; } = EditorCoordinateFrame.Identity;
    public List<(int Offset, int Length, EditorCoordinateFrame Frame, Rect Bounds)> Scopes { get; } = [];
    public List<(int Offset, int Length, EditorCoordinateFrame Frame)> CoordinateRegions { get; } = [];
    public bool UsesPolarCoordinates => Points.Any(point => point.IsPolar);
}

internal sealed record LegacyTikzPoint(
    Tikz_XYItem Item,
    Point Position,
    int Offset,
    int Length,
    bool IsPolar)
{
    public bool IsBezierControl => Item.parent is Tikz_Controls;
}

internal sealed record LegacyTikzSegment(LegacyTikzPoint Start, LegacyTikzPoint End, string Options);

internal static class LegacyTikzAdapter
{
    public static LegacyTikzDiagram Parse(string source)
    {
        var result = new LegacyTikzDiagram();
        try
        {
            var tree = TikzParser.Parse(source);
            if (tree is null)
                return result;

            Visit(tree, result);
            var picture = Descendants(tree).OfType<Tikz_Picture>().FirstOrDefault();
            if (picture is not null && picture.GetCurrentTransformAt(null, out var rootTransform)) result.RootFrame = Frame(rootTransform);
            foreach (var container in Descendants(tree).OfType<TikzContainerParseItem>().Where(item => item is Tikz_Picture or Tikz_Scope or Tikz_Path))
                if (container.GetCurrentTransformAt(null, out var regionTransform)) result.CoordinateRegions.Add((container.StartPosition(), container.ToString().Length, Frame(regionTransform)));
            foreach (var scope in Descendants(tree).OfType<Tikz_Scope>())
            {
                if (!scope.GetCurrentTransformAt(null, out var transform)) continue;
                var bounds = Rect.Empty;
                foreach (var child in Descendants(scope).OfType<Tikz_XYItem>()) if (ToPoint(child) is { } point) bounds.Union(point.Position);
                result.Scopes.Add((scope.StartPosition(), scope.ToString().Length, Frame(transform), bounds));
            }
        }
        catch (Exception exception)
        {
            result.Error = exception.Message;
        }
        return result;
    }

    public static string MovePoint(LegacyTikzPoint point, Point absolutePosition)
    {
        var original = point.Item.ToString();
        if (point.Item.GetAbsPos(out var current) && (current - absolutePosition).Length < 1e-8) return original;
        var coordinate = point.Item switch
        {
            Tikz_Coord value => value,
            Tikz_Node nodeItem => nodeItem.coord,
            _ => null
        };
        if (coordinate is null || !point.Item.GetAbsPos(out current) || !point.Item.parent.GetCurrentTransformAt(point.Item, out var transform)) return original;
        var originalCoordinate = coordinate.ToString();
        if (string.IsNullOrEmpty(originalCoordinate) && point.Item is Tikz_Node) originalCoordinate = Regex.Match(original, @"\bat\s*(?<coord>\([^()]*\))").Groups["coord"].Value;
        var pair = Regex.Match(originalCoordinate, @"\(\s*(?<x>[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?(?:cm|mm|pt|in|em|ex)?)(?<between>\s*[:,]\s*)(?<y>[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?(?:cm|mm|pt|in|em|ex)?)\s*\)");
        if (!pair.Success) { point.Item.SetAbsPos(absolutePosition); point.Item.UpdateText(); return point.Item.ToString(); }
        var delta = transform.Inverse().Transform(new Point(absolutePosition.X - current.X, absolutePosition.Y - current.Y), true);
        var desired = coordinate.GetCartesian(); desired.Offset(delta.X, delta.Y);
        var first = desired.X; var second = desired.Y;
        if (coordinate.type == Tikz_CoordType.Polar)
        {
            first = Math.Atan2(desired.Y, desired.X) * 180 / Math.PI;
            first += 360 * Math.Round((coordinate.uX.number - first) / 360);
            second = Math.Sqrt(desired.X * desired.X + desired.Y * desired.Y);
        }
        string Update(Tikz_NumberUnit number, double value, Group originalToken)
        {
            if (Math.Abs(number.GetInCM() - value) < 1e-9) return originalToken.Value;
            var unit = new Tikz_NumberUnit { unit = number.unit }; unit.number = 1;
            var fraction = Regex.Match(originalToken.Value, @"\.(\d+)").Groups[1].Length;
            number.SetNumberWithPrecision(value / unit.GetInCM(), Math.Max(6, fraction));
            return number.ToString();
        }
        var x = Update(coordinate.uX, first, pair.Groups["x"]); var y = Update(coordinate.uY, second, pair.Groups["y"]);
        coordinate.text = originalCoordinate[..pair.Groups["x"].Index] + x + pair.Groups["between"].Value + y + originalCoordinate[(pair.Groups["y"].Index + pair.Groups["y"].Length)..];
        if (point.Item is Tikz_Node node)
        {
            var at = Regex.Match(original, @"\bat\s*");
            var index = original.IndexOf(originalCoordinate, at.Success ? at.Index + at.Length : 0, StringComparison.Ordinal);
            if (index >= 0) node.text = original[..index] + coordinate.text + original[(index + originalCoordinate.Length)..]; else node.UpdateText();
        }
        return point.Item.ToString();
    }

    internal static EditorCoordinateFrame Frame(TikzMatrix matrix)
    {
        var value = new Matrix(matrix.m[0, 0], matrix.m[1, 0], matrix.m[0, 1], matrix.m[1, 1], matrix.m[0, 2], matrix.m[1, 2]);
        return value.HasInverse ? new(value) : EditorCoordinateFrame.Identity;
    }

    private static void Visit(TikzParseItem item, LegacyTikzDiagram diagram)
    {
        if (item is Tikz_Path path)
        {
            var pathPoints = Descendants(path)
                .OfType<Tikz_XYItem>()
                .Select(ToPoint)
                .Where(point => point is not null)
                .Cast<LegacyTikzPoint>()
                .ToList();

            diagram.Points.AddRange(pathPoints);
            if (!path.ToString().Contains(" grid", StringComparison.OrdinalIgnoreCase))
            {
                var options = ExtractOptions(path.ToString());
                for (var index = 1; index < pathPoints.Count; index++)
                    diagram.Segments.Add(new LegacyTikzSegment(pathPoints[index - 1], pathPoints[index], options));
            }
            return;
        }

        if (item is TikzContainerParseItem container)
        {
            foreach (var child in container.Children)
                Visit(child, diagram);
        }
        else if (item is Tikz_XYItem xyItem)
        {
            var point = ToPoint(xyItem);
            if (point is not null)
                diagram.Points.Add(point);
        }
    }

    private static IEnumerable<TikzParseItem> Descendants(TikzParseItem item)
    {
        if (item is not TikzContainerParseItem container)
            yield break;
        foreach (var child in container.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static LegacyTikzPoint? ToPoint(Tikz_XYItem item)
    {
        if (!item.HasEditableCoordinate() || !item.GetAbsPos(out var position))
            return null;
        return new LegacyTikzPoint(item, position, item.StartPosition(), item.ToString().Length, item.IsPolar());
    }

    private static string ExtractOptions(string pathText)
    {
        var match = Regex.Match(pathText, @"\\(?:draw|path|filldraw|fill)\s*(?<options>\[[^\]]*\])", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["options"].Value : string.Empty;
    }
}
