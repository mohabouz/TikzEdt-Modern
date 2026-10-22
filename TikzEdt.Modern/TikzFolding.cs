using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Folding;

namespace TikzEdt.Modern;

internal static partial class TikzFolding
{
    public static IReadOnlyList<NewFolding> Create(string source)
    {
        var masked = source.ToCharArray();
        bool Escaped(int offset)
        {
            var count = 0;
            while (offset > 0 && source[--offset] == '\\') count++;
            return count % 2 != 0;
        }
        for (var index = 0; index < source.Length; index++)
            if (source[index] == '%' && !Escaped(index))
                while (index < source.Length && source[index] is not ('\r' or '\n')) masked[index++] = ' ';
        var text = new string(masked);
        var result = new List<NewFolding>();
        var environments = new Stack<(string Name, int Start)>();
        var braces = new Stack<int>();
        var commands = EnvironmentRegex().Matches(text).ToDictionary(match => match.Index);
        void Add(int start, int end, string name)
        {
            if (end > start && source.AsSpan(start, end - start).IndexOf('\n') >= 0)
                result.Add(new NewFolding(start, end) { Name = name });
        }
        for (var index = 0; index < text.Length; index++)
        {
            if (Escaped(index)) continue;
            if (commands.TryGetValue(index, out var command))
            {
                var name = command.Groups["name"].Value;
                if (command.Groups["action"].Value == "begin") environments.Push((name, index + command.Length));
                else if (environments.TryPeek(out var open) && open.Name == name)
                {
                    environments.Pop();
                    Add(open.Start, index, name + " …");
                }
                index += command.Length - 1;
            }
            else if (text[index] == '{') braces.Push(index + 1);
            else if (text[index] == '}' && braces.TryPop(out var start)) Add(start, index, "…");
        }
        var drawingCommands = DrawingCommandRegex().Matches(text).ToArray();
        var drawingStarts = drawingCommands.Select(command => command.Index).ToHashSet();
        foreach (var command in drawingCommands)
        {
            var braceDepth = 0;
            var bracketDepth = 0;
            for (var index = command.Index + command.Length; index < text.Length; index++)
            {
                if (Escaped(index)) continue;
                if (text[index] == '{') braceDepth++;
                else if (text[index] == '}' && --braceDepth < 0) break;
                else if (text[index] == '[') bracketDepth++;
                else if (text[index] == ']') bracketDepth--;
                else if (text[index] == ';' && braceDepth == 0 && bracketDepth == 0)
                {
                    Add(command.Index + command.Length, index, command.Value[1..] + " …");
                    break;
                }
                // Do not fold an unfinished path into the next command/environment.
                if (braceDepth == 0 && (commands.ContainsKey(index) || drawingStarts.Contains(index))) break;
            }
        }
        return result.OrderBy(folding => folding.StartOffset).ThenByDescending(folding => folding.EndOffset).ToArray();
    }

    [GeneratedRegex(@"\\(?<action>begin|end)\s*\{(?<name>[\w*]+)\}")]
    private static partial Regex EnvironmentRegex();

    [GeneratedRegex(@"\\(?:draw|path|filldraw|fill|clip|shade|shadedraw|node|coordinate|addplot)\b")]
    private static partial Regex DrawingCommandRegex();
}
