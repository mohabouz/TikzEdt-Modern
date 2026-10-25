using System.Text.RegularExpressions;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

internal sealed record OverlayCoordinate(int Offset, int Length, int Separator, char SeparatorKind, string First, string Second, bool Resolved, double X, double Y);
internal sealed record OverlayCoordinateSource(string Text, IReadOnlyList<OverlayCoordinate> Coordinates)
{
    public static OverlayCoordinateSource Prepare(string source)
    {
        var masked = TexKnowledge.Mask(source); var text = source.ToCharArray(); var coordinates = new List<OverlayCoordinate>();
        for (var start = 0; start < masked.Length; start++)
        {
            if (masked[start] != '(') continue;
            var braces = 0; var parentheses = 1; var separator = -1; var end = start + 1;
            for (; end < masked.Length && end - start <= 8192; end++)
            {
                var c = masked[end]; if (c == '\\') { end++; continue; }
                if (c == '{') braces++; else if (c == '}') { if (--braces < 0) break; }
                else if (braces == 0 && c == '(') parentheses++;
                else if (braces == 0 && c == ')' && --parentheses == 0) break;
                else if (braces == 0 && parentheses == 1 && c is ',' or ':') { if (separator >= 0) { separator = -2; } else if (separator != -2) separator = end; }
            }
            if (end >= masked.Length || masked[end] != ')' || parentheses != 0) continue;
            if (separator < 0)
            {
                // TikZ calc expressions remain opaque, not a fake editable origin.
                if (masked.AsSpan(start + 1, end - start - 1).Trim().StartsWith("$")) Replace(start, end);
                start = end; continue;
            }
            var first = source[(start + 1)..separator]; var second = source[(separator + 1)..end];
            if (Literal(first) && Literal(second)) { start = end; continue; }
            var resolvedX = PgfConstantExpression.TryEvaluate(first, out var x); var resolvedY = PgfConstantExpression.TryEvaluate(second, out var y);
            coordinates.Add(new(start, end - start + 1, separator - start, source[separator], first, second, resolvedX && resolvedY, x, y));
            Replace(start, end); start = end;
        }
        return new(new string(text), coordinates);
        void Replace(int start, int end)
        {
            if (end - start < 4) return;
            for (var i = start + 1; i < end; i++) if (text[i] is not '\r' and not '\n') text[i] = ' ';
            // A syntactically accepted but explicitly unevaluable pair. Preserve every offset/newline.
            var write = start + 1;
            foreach (var c in "q,q") { while (write < end && text[write] is '\r' or '\n') write++; if (write < end) text[write++] = c; }
        }
    }
    private static bool Literal(string text) => Regex.IsMatch(text, @"^\s*[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?(?:cm|mm|pt|in|em|ex)?\s*$", RegexOptions.None, TimeSpan.FromMilliseconds(50));
}
