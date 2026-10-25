using System.Text.RegularExpressions;

namespace TikzEdt.Modern.Core.Documents;

public sealed record TexFix(string Title, int Offset, int Length, string Expected, string Replacement);
public sealed record TexIssue(int Offset, int Length, string Message, TexFix? Fix = null);
public static class TexSemantics
{
    private static readonly Dictionary<string, string[]> Enumerations = new(StringComparer.Ordinal)
    {
        ["line cap"] = ["butt", "round", "rect"], ["line join"] = ["miter", "round", "bevel"],
        ["anchor"] = ["center", "north", "south", "east", "west", "north east", "north west", "south east", "south west", "base", "base east", "base west", "mid", "mid east", "mid west", "text"]
    };
    public static IReadOnlyList<TexIssue> Analyze(TexIndex index, CancellationToken token = default)
    {
        var issues = new List<TexIssue>(); var missing = new HashSet<string>();
        // Only inspect literal option lists attached to drawing commands, not labels or arbitrary TeX brackets.
        foreach (Match match in Regex.Matches(index.Masked, @"\\(?:draw|path|node|fill|filldraw|clip|coordinate|begin\s*\{(?:tikzpicture|scope)\})\b?\s*\[", RegexOptions.None, TimeSpan.FromMilliseconds(150)))
        {
            token.ThrowIfCancellationRequested(); var start = match.Index + match.Length; var braces = 0; var part = start;
            for (var i = start; i < index.Masked.Length; i++)
            {
                token.ThrowIfCancellationRequested(); var c = index.Masked[i];
                if (c == '\\') { i++; continue; }
                if (c == '{') braces++; if (c == '}') braces--;
                if (braces < 0 || c == ';' && braces == 0) break;
                if ((c == ',' || c == ']') && braces == 0)
                { Inspect(part, i); part = i + 1; if (c == ']') break; }
            }
        }
        foreach (Match command in Regex.Matches(index.Masked, @"\\(?:pstTubeEssais|pstDosage|pstBallon|pstEprouvette|pstEntonnoir|pstDistillation|pstSeparateFunnel|includegraphics)\b", RegexOptions.None, TimeSpan.FromMilliseconds(150)))
        {
            var package = command.Value == @"\includegraphics" ? "graphicx" : "pst-labo";
            if (!index.Packages.Contains(package) && missing.Add("package:" + package)) issues.Add(new(command.Index, command.Length, "This command needs package '" + package + "' in the preamble (it may also be loaded indirectly).", PreambleFix(index.Source, "\\usepackage{" + package + "}", "Load " + package + " in this document")));
        }
        return issues;

        void Inspect(int start, int end)
        {
            var raw = index.Masked[start..end]; var trimmed = raw.Trim(); if (trimmed.Length == 0 || trimmed.Contains('\\')) return;
            var position = start + raw.IndexOf(trimmed, StringComparison.Ordinal); var equal = trimmed.IndexOf('=');
            var key = (equal < 0 ? trimmed : trimmed[..equal]).Trim();
            var required = TexKnowledge.Entries.FirstOrDefault(e => e.Kind == "Option" && e.Name.TrimEnd('=') == key)?.Library;
            if (required is not null && !index.Libraries.Contains(required) && missing.Add("library:" + required))
                issues.Add(new(position, key.Length, "Option '" + key + "' needs TikZ library '" + required + "' (it may also be loaded indirectly).", PreambleFix(index.Source, "\\usetikzlibrary{" + required + "}", "Load library " + required)));
            if (equal < 0 || !Enumerations.TryGetValue(key, out var choices)) return;
            var value = trimmed[(equal + 1)..].Trim(); if (value.Length == 0 || value.Contains('{') || value.Contains('/') || choices.Contains(value)) return;
            // Custom anchors are legal on custom node shapes: only diagnose the two closed PGF stroke enums.
            if (key == "anchor") return;
            var valueOffset = position + equal + 1 + trimmed[(equal + 1)..].IndexOf(value, StringComparison.Ordinal);
            var nearest = choices.OrderByDescending(c => PaletteSearch.Score(value, c)).First();
            issues.Add(new(valueOffset, value.Length, "Invalid " + key + " '" + value + "'. Expected " + string.Join(", ", choices) + ".", new("Set " + key + " to " + nearest, valueOffset, value.Length, index.Source.Substring(valueOffset, value.Length), nearest)));
        }
    }
    public static TexFix PreambleFix(string source, string declaration, string title)
    {
        var marker = Regex.Match(TexKnowledge.Mask(source), @"\\begin\s*\{document\}", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        var offset = marker.Success ? marker.Index : 0;
        var eol = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return new(title, offset, 0, "", declaration + eol);
    }
    public static bool IsApplicable(string source, TexFix fix) => fix.Offset >= 0 && fix.Length >= 0 && fix.Offset + fix.Length <= source.Length && source.Substring(fix.Offset, fix.Length) == fix.Expected;
}
