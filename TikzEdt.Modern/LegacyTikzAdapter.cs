using System.Windows;
using System.Text.RegularExpressions;
using TikzEdt.Parser;

namespace TikzEdt.Modern;

internal sealed class LegacyTikzDiagram
{
    public List<LegacyTikzPoint> Points { get; } = [];
    public List<LegacyTikzSegment> Segments { get; } = [];
    public string? Error { get; set; }
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
        }
        catch (Exception exception)
        {
            result.Error = exception.Message;
        }
        return result;
    }

    public static string MovePoint(LegacyTikzPoint point, Point absolutePosition)
    {
        point.Item.SetAbsPos(absolutePosition);
        var coordinate = point.Item switch
        {
            Tikz_Coord value => value,
            Tikz_Node node => node.coord,
            _ => null
        };
        if (coordinate?.type == Tikz_CoordType.Polar && point.Item.GetAbsPos(out var roundedPosition) &&
            point.Item.parent.GetCurrentTransformAt(point.Item, out var transform))
        {
            // The legacy setter rounds polar angles to whole degrees. Correct that
            // rounding in local space so polar members share the group's translation.
            var correction = transform.Inverse().Transform(
                new Point(absolutePosition.X - roundedPosition.X, absolutePosition.Y - roundedPosition.Y), true);
            var local = coordinate.GetCartesian();
            local.Offset(correction.X, correction.Y);
            coordinate.uX.SetInCM(Math.Atan2(local.Y, local.X) * 180 / Math.PI);
            coordinate.uY.SetInCM(Math.Sqrt(local.X * local.X + local.Y * local.Y));
        }
        point.Item.UpdateText();
        return point.Item.ToString();
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
