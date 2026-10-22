using System.Text.RegularExpressions;

namespace TikzEdt.Modern;

internal static partial class TikzSyntaxDiagnostics
{
    public static IReadOnlyList<EditorDiagnostic> Analyze(string source)
    {
        var diagnostics = new List<EditorDiagnostic>();
        CheckDelimiters(source, diagnostics);
        CheckEnvironments(source, diagnostics);
        CheckCommandTerminators(source, diagnostics);
        return diagnostics
            .GroupBy(item => (item.Offset, item.Message))
            .Select(group => group.First())
            .OrderBy(item => item.Offset)
            .ToArray();
    }

    private static void CheckDelimiters(string source, List<EditorDiagnostic> diagnostics)
    {
        var stack = new Stack<(char Character, int Offset)>();
        var pairs = new Dictionary<char, char> { [')'] = '(', [']'] = '[', ['}'] = '{' };
        var inComment = false;

        for (var index = 0; index < source.Length; index++)
        {
            var character = source[index];
            if (character is '\r' or '\n')
            {
                inComment = false;
                continue;
            }
            if (inComment)
                continue;
            if (character == '%' && !IsEscaped(source, index))
            {
                inComment = true;
                continue;
            }
            if (IsEscaped(source, index))
                continue;

            if (character is '(' or '[' or '{')
            {
                stack.Push((character, index));
                continue;
            }
            if (!pairs.TryGetValue(character, out var expected))
                continue;

            if (stack.Count == 0 || stack.Peek().Character != expected)
            {
                diagnostics.Add(Create(source, index, 1, $"Unexpected closing '{character}'.", "Syntax"));
                continue;
            }
            stack.Pop();
        }

        foreach (var opening in stack)
        {
            var expected = opening.Character switch { '(' => ')', '[' => ']', _ => '}' };
            diagnostics.Add(Create(source, opening.Offset, 1, $"Missing closing '{expected}'.", "Syntax"));
        }
    }

    private static void CheckEnvironments(string source, List<EditorDiagnostic> diagnostics)
    {
        var stack = new Stack<(string Name, int Offset, int Length)>();
        foreach (Match match in EnvironmentRegex().Matches(source))
        {
            var name = match.Groups["name"].Value.Trim();
            if (match.Groups["kind"].Value.Equals("begin", StringComparison.OrdinalIgnoreCase))
            {
                stack.Push((name, match.Index, match.Length));
                continue;
            }

            if (stack.Count == 0)
            {
                diagnostics.Add(Create(source, match.Index, match.Length, $"No matching \\begin{{{name}}}.", "Syntax"));
                continue;
            }

            var opening = stack.Pop();
            if (!opening.Name.Equals(name, StringComparison.Ordinal))
            {
                diagnostics.Add(Create(source, match.Index, match.Length,
                    $"Expected \\end{{{opening.Name}}}, but found \\end{{{name}}}.", "Syntax"));
            }
        }

        foreach (var opening in stack)
            diagnostics.Add(Create(source, opening.Offset, opening.Length, $"Missing \\end{{{opening.Name}}}.", "Syntax"));
    }

    private static void CheckCommandTerminators(string source, List<EditorDiagnostic> diagnostics)
    {
        foreach (Match match in TikzCommandRegex().Matches(source))
        {
            // A command ends at its first top-level semicolon. Comments and
            // nested node text may follow it or contain semicolons themselves.
            var braces = 0;
            var comment = false;
            var terminated = false;
            foreach (var (character, index) in match.Value.Select((value, index) => (value, index)))
            {
                if (character is '\r' or '\n') { comment = false; continue; }
                if (comment) continue;
                if (IsEscaped(match.Value, index)) continue;
                if (character == '%') { comment = true; continue; }
                if (character == '{') braces++;
                else if (character == '}') braces--;
                else if (character == ';' && braces == 0) { terminated = true; break; }
            }
            if (terminated)
                continue;
            var command = match.Groups["command"];
            diagnostics.Add(Create(source, command.Index, command.Length,
                $"TikZ command \\{command.Value} should end with a semicolon.", "Syntax"));
        }
    }

    internal static EditorDiagnostic Create(
        string source,
        int offset,
        int length,
        string message,
        string diagnosticSource,
        EditorDiagnosticSeverity severity = EditorDiagnosticSeverity.Error)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, source.Length));
        var line = 1;
        var column = 1;
        for (var index = 0; index < offset && index < source.Length; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }
        return new EditorDiagnostic(offset, Math.Max(1, length), line, column, message, diagnosticSource, severity);
    }

    private static bool IsEscaped(string source, int offset)
    {
        var slashes = 0;
        for (var index = offset - 1; index >= 0 && source[index] == '\\'; index--)
            slashes++;
        return slashes % 2 == 1;
    }

    [GeneratedRegex(@"\\(?<kind>begin|end)\s*\{(?<name>[^{}]+)\}", RegexOptions.IgnoreCase)]
    private static partial Regex EnvironmentRegex();

    [GeneratedRegex(@"^[\t ]*\\(?<command>draw|path|node|fill|filldraw|shade|shadedraw|clip|coordinate)\b.*?(?=^[\t ]*\\(?:draw|path|node|fill|filldraw|shade|shadedraw|clip|coordinate)\b|^[\t ]*\\end\s*\{tikzpicture\}|\z)", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex TikzCommandRegex();
}
