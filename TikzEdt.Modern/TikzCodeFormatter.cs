using System.Text.RegularExpressions;

namespace TikzEdt.Modern;

internal readonly record struct IndentationEdit(int Offset, int Length, string Text);

/// <summary>Structural line breaks and indentation; labels, comments and literal code are preserved.</summary>
internal static partial class TikzCodeFormatter
{
    public static IReadOnlyList<IndentationEdit> CreateEdits(string source, int tabSize, bool spaces)
        => CreateEditsForRange(source, 0, source.Length, tabSize, spaces);

    public static IReadOnlyList<IndentationEdit> CreateEditsForRange(string source, int start, int end, int tabSize, bool spaces)
    {
        var expanded = AddStructuralBreaks(source, start, end);
        var newEnd = end + expanded.Length - source.Length;
        var result = expanded[start..newEnd];
        foreach (var edit in IndentEdits(expanded, tabSize, spaces).Where(e => e.Offset >= start && e.Offset < newEnd).Reverse())
            result = result.Remove(edit.Offset - start, edit.Length).Insert(edit.Offset - start, edit.Text);
        var original = source[start..end];
        if (result == original) return [];
        var prefix = 0; while (prefix < original.Length && prefix < result.Length && original[prefix] == result[prefix]) prefix++;
        var suffix = 0; while (suffix < original.Length - prefix && suffix < result.Length - prefix && original[^(suffix + 1)] == result[^(suffix + 1)]) suffix++;
        return [new(start + prefix, original.Length - prefix - suffix, result.Substring(prefix, result.Length - prefix - suffix))];
    }

    private static string AddStructuralBreaks(string source, int rangeStart, int rangeEnd)
    {
        var result = new System.Text.StringBuilder(); var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var braces = 0; var brackets = 0;
        bool Active(int index) => index >= rangeStart && index < rangeEnd;
        void Before()
        {
            var index = result.Length - 1; while (index >= 0 && result[index] is ' ' or '\t') index--;
            if (index >= 0 && result[index] is not ('\r' or '\n'))
            { result.Length = index + 1; result.Append(newline); }
        }
        void After(int index)
        {
            if (index > rangeEnd) return;
            while (index < source.Length && source[index] is ' ' or '\t') index++;
            if (index < source.Length && source[index] is not ('\r' or '\n' or '%')) result.Append(newline);
        }
        for (var i = 0; i < source.Length;)
        {
            if (source[i] == '%' && !Escaped(source, i))
            { var end = source.IndexOf('\n', i); if (end < 0) end = source.Length; result.Append(source, i, end - i); i = end; continue; }
            if (source[i] == '\\' && !Escaped(source, i))
            {
                var verb = VerbStartRegex().Match(source, i);
                if (verb.Success)
                { var end = source.IndexOf(verb.Groups["delimiter"].Value[0], i + verb.Length); if (end < 0) end = source.IndexOf('\n', i); if (end < 0) end = source.Length - 1; result.Append(source, i, end + 1 - i); i = end + 1; continue; }
                var environment = EnvironmentStartRegex().Match(source, i);
                if (environment.Success && environment.Index == i)
                {
                    var name = environment.Groups[2].Value; var begin = environment.Groups[1].Value == "begin";
                    if (begin && name is "verbatim" or "verbatim*" or "Verbatim" or "lstlisting" or "minted" or "comment")
                    {
                        var closing = "\\end{" + name + "}"; var end = source.IndexOf(closing, i + environment.Length, StringComparison.Ordinal);
                        end = end < 0 ? source.Length : end + closing.Length;
                        result.Append(source, i, end - i); i = end; continue;
                    }
                    var endHeader = i + environment.Length;
                    if (begin)
                    {
                        var cursor = endHeader; while (cursor < source.Length && source[cursor] is ' ' or '\t') cursor++;
                        if (cursor < source.Length && source[cursor] == '[')
                        {
                            var nesting = 1; cursor++;
                            while (cursor < source.Length && nesting > 0)
                            { if (!Escaped(source, cursor)) { if (source[cursor] == '[') nesting++; if (source[cursor] == ']') nesting--; } cursor++; }
                            if (nesting == 0) endHeader = cursor;
                        }
                    }
                    var structural = braces == 0 && brackets == 0 && Active(i) && endHeader <= rangeEnd;
                    if (structural) Before(); result.Append(source, i, endHeader - i);
                    if (structural) After(endHeader);
                    i = endHeader; continue;
                }
                var drawing = DrawingStartRegex().Match(source, i);
                if (drawing.Success && drawing.Index == i && braces == 0 && brackets == 0 && Active(i)) Before();
            }
            var character = source[i]; result.Append(character);
            if (!Escaped(source, i))
            {
                if (character == '{') braces++; if (character == '}') braces = Math.Max(0, braces - 1);
                if (character == '[') brackets++; if (character == ']') brackets = Math.Max(0, brackets - 1);
            }
            i++;
        }
        return result.ToString();
    }

    private static IReadOnlyList<IndentationEdit> IndentEdits(string source, int tabSize, bool spaces)
    {
        var edits = new List<IndentationEdit>();
        var environmentDepth = 0;
        var braces = 0;
        var brackets = 0;
        var pathContinuation = false;
        string? literal = null;
        var offset = 0;
        foreach (var line in source.Split('\n'))
        {
            var text = line.TrimStart(' ', '\t');
            var indentation = line.Length - text.Length;
            var code = MaskComment(text);
            var environments = EnvironmentRegex().Matches(code).Where(match => !Escaped(code, match.Index)).ToArray();
            if (literal is not null)
            {
                if (environments.Any(match => match.Groups[1].Value == "end" && match.Groups[2].Value == literal))
                { literal = null; environmentDepth = Math.Max(0, environmentDepth - 1); }
                offset += line.Length + 1;
                continue;
            }
            var closing = 0;
            var cursor = 0;
            while (cursor < code.Length)
            {
                if (char.IsWhiteSpace(code[cursor])) { cursor++; continue; }
                if (code[cursor] is '}' or ']') { closing++; cursor++; continue; }
                var end = environments.FirstOrDefault(match => match.Index == cursor && match.Groups[1].Value == "end");
                if (end is null) break;
                closing++; cursor += end.Length;
            }
            var level = Math.Clamp(environmentDepth + braces + brackets - closing +
                (pathContinuation && brackets == 0 && closing == 0 ? 1 : 0), 0, 64);
            var desired = spaces ? new string(' ', level * Math.Clamp(tabSize, 1, 16)) : new string('\t', level);
            if (text.TrimEnd('\r').Length > 0 && line[..indentation] != desired)
                edits.Add(new(offset, indentation, desired));

            var characters = code.ToCharArray();
            foreach (var environment in environments)
            {
                var begin = environment.Groups[1].Value == "begin";
                environmentDepth = Math.Max(0, environmentDepth + (begin ? 1 : -1));
                if (begin && environment.Groups[2].Value is "verbatim" or "verbatim*" or "Verbatim" or "lstlisting" or "minted" or "comment")
                    literal = environment.Groups[2].Value;
                Array.Fill(characters, ' ', environment.Index, environment.Length);
            }
            foreach (Match command in DrawingCommandRegex().Matches(code))
                if (!Escaped(code, command.Index)) pathContinuation = true;
            for (var index = 0; index < characters.Length; index++)
            {
                if (Escaped(code, index)) continue;
                switch (characters[index])
                {
                    case '{': braces++; break;
                    case '}': braces = Math.Max(0, braces - 1); break;
                    case '[': brackets++; break;
                    case ']': brackets = Math.Max(0, brackets - 1); break;
                    case ';': pathContinuation = false; break;
                }
            }
            offset += line.Length + 1;
        }
        return edits;
    }

    private static string MaskComment(string text)
    {
        for (var index = 0; index < text.Length; index++)
            if (text[index] == '%' && !Escaped(text, index)) return text[..index];
        return text;
    }
    private static bool Escaped(string text, int offset)
    {
        var count = 0;
        while (offset > 0 && text[--offset] == '\\') count++;
        return count % 2 != 0;
    }
    [GeneratedRegex(@"\\(begin|end)\s*\{([\w*]+)\}")]
    private static partial Regex EnvironmentRegex();
    [GeneratedRegex(@"\G\\(begin|end)\s*\{([\w*]+)\}")]
    private static partial Regex EnvironmentStartRegex();
    [GeneratedRegex(@"\G\\verb\*?(?<delimiter>[^a-zA-Z\r\n])")]
    private static partial Regex VerbStartRegex();
    [GeneratedRegex(@"\G\\(?:draw|path|filldraw|fill|clip|shade|shadedraw|node|coordinate|addplot)\b")]
    private static partial Regex DrawingStartRegex();
    [GeneratedRegex(@"\\(?:draw|path|filldraw|fill|clip|shade|shadedraw|node|coordinate|addplot)\b")]
    private static partial Regex DrawingCommandRegex();
}
