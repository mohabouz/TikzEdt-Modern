using System.Text.RegularExpressions;

namespace TikzEdt.Modern.Core.Documents;

public sealed record MathSlice(int Offset, int Length, string Body, string Kind);
public static class MathContext
{
    private static bool Escaped(string text, int at) { var count = 0; for (var i = at - 1; i >= 0 && text[i] == '\\'; i--) count++; return count % 2 != 0; }
    public static MathSlice? Find(string source, int offset, CancellationToken token = default)
    {
        if (offset < 0 || offset > source.Length || source.Length > 2 * 1024 * 1024) return null;
        var opening = -1; var bodyStart = -1; var closing = ""; var kind = "";
        for (var i = 0; i < source.Length; i++)
        {
            if (i % 1024 == 0) token.ThrowIfCancellationRequested();
            if (source[i] == '%' && !Escaped(source, i)) { var lineEnd = source.IndexOf('\n', i); if (lineEnd < 0) return null; i = lineEnd; continue; }
            if (Escaped(source, i)) continue;
            var rest = source.AsSpan(i);
            if (opening >= 0)
            {
                if (!rest.StartsWith(closing, StringComparison.Ordinal)) continue;
                var end = i + closing.Length;
                if (offset >= opening && offset <= end) return new(opening, end - opening, source[bodyStart..i], kind);
                opening = -1; i = end - 1; continue;
            }
            if (i > offset) return null;
            if (rest.StartsWith("\\verb", StringComparison.Ordinal))
            {
                var delimiter = i + 5; if (delimiter < source.Length && source[delimiter] == '*') delimiter++;
                if (delimiter < source.Length && !char.IsLetter(source[delimiter]) && source[delimiter] is not '\r' and not '\n')
                { var end = source.IndexOf(source[delimiter], delimiter + 1); if (end < 0) return null; i = end; continue; }
            }
            var matchedCode = false;
            foreach (var environment in new[] { "verbatim", "verbatim*", "lstlisting", "minted" })
            {
                var begin = "\\begin{" + environment + "}";
                if (!rest.StartsWith(begin, StringComparison.Ordinal)) continue;
                var endToken = "\\end{" + environment + "}"; var end = source.IndexOf(endToken, i + begin.Length, StringComparison.Ordinal);
                if (end < 0) return null; i = end + endToken.Length - 1; matchedCode = true; break;
            }
            if (matchedCode) continue;
            var length = 0;
            if (rest.StartsWith("$$", StringComparison.Ordinal)) { length = 2; closing = "$$"; kind = "display"; }
            else if (source[i] == '$') { length = 1; closing = "$"; kind = "inline"; }
            else if (rest.StartsWith("\\(", StringComparison.Ordinal)) { length = 2; closing = "\\)"; kind = "inline"; }
            else if (rest.StartsWith("\\[", StringComparison.Ordinal)) { length = 2; closing = "\\]"; kind = "display"; }
            else
            {
                foreach (var environment in new[] { "equation", "equation*", "align", "align*", "gather", "gather*" })
                {
                    var begin = "\\begin{" + environment + "}";
                    if (!rest.StartsWith(begin, StringComparison.Ordinal)) continue;
                    length = begin.Length; closing = "\\end{" + environment + "}"; kind = environment.TrimEnd('*'); break;
                }
            }
            if (length > 0) { opening = i; bodyStart = i + length; i += length - 1; }
        }
        return null;
    }
    public static string Preamble(string source)
    {
        var masked = MaskComments(source);
        var begin = Regex.Match(masked, @"\\begin\s*\{(?:document|tikzpicture|circuitikz)\}", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        return begin.Success && begin.Index <= 128 * 1024 ? source[..begin.Index] : "";
    }
    public static string MaskComments(string source)
    {
        var result = source.ToCharArray(); var comment = false;
        for (var i = 0; i < source.Length; i++)
        { if (source[i] is '\r' or '\n') comment = false; else if (comment || source[i] == '%' && !Escaped(source, i)) { comment = true; result[i] = ' '; } }
        return new string(result);
    }
}
