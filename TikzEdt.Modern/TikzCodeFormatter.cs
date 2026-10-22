using System.Text.RegularExpressions;

namespace TikzEdt.Modern;

internal readonly record struct IndentationEdit(int Offset, int Length, string Text);

/// <summary>Indentation-only formatting: never rewrites commands, text or line endings.</summary>
internal static partial class TikzCodeFormatter
{
    public static IReadOnlyList<IndentationEdit> CreateEdits(string source, int tabSize, bool spaces)
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
    [GeneratedRegex(@"\\(?:draw|path|filldraw|fill|clip|shade|shadedraw|node|coordinate|addplot)\b")]
    private static partial Regex DrawingCommandRegex();
}
