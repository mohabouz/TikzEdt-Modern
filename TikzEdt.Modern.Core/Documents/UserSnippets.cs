using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TikzEdt.Modern.Core.Documents;

public sealed record UserSnippet(string Name, string Prefix, string Body, string Description = "", string Context = "any");
public sealed record SnippetField(int Number, int Offset, int Length);
public sealed record SnippetExpansion(string Text, IReadOnlyList<SnippetField> Fields, int Caret);
public sealed record SnippetCatalog(IReadOnlyList<UserSnippet> Items, IReadOnlyList<string> Issues);

public static class UserSnippets
{
    public const string Example = """
[
  {
    "name": "Named TikZ node",
    "prefix": "tnode",
    "body": "\\node[${1:draw}] (${2:name}) at (${3:0,0}) {${4:label}};\n$0",
    "description": "Tab through options, name, coordinates and label",
    "context": "any"
  },
  {
    "name": "Linked coordinates",
    "prefix": "tlink",
    "body": "\\draw (${1:0},${2:0}) -- (${1},${3:2});$0",
    "description": "The repeated first coordinate stays linked",
    "context": "any"
  }
]
""";
    public static IReadOnlyList<UserSnippet> Parse(string json)
    {
        if (json.Length > 1024 * 1024) throw new FormatException("Snippet file exceeds 1 MB.");
        UserSnippet[] items;
        try { items = JsonSerializer.Deserialize<UserSnippet[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip }) ?? []; }
        catch (JsonException e) { throw new FormatException("Invalid snippets JSON: " + e.Message, e); }
        if (items.Length > 200) throw new FormatException("Use at most 200 snippets per file.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name) || item.Name.Length > 120 || item.Prefix is null || !Regex.IsMatch(item.Prefix, "^[A-Za-z][A-Za-z0-9_-]{0,39}$") || item.Body is null || item.Body.Length > 65536)
                throw new FormatException("Each snippet needs a name, a 1–40 character letter-led prefix and a body no larger than 64 KB.");
            if (item.Context is not ("any" or "math" or "text")) throw new FormatException("Snippet context must be any, math or text.");
            if (!keys.Add(item.Prefix + ":" + item.Context)) throw new FormatException("Duplicate snippet prefix/context: " + item.Prefix);
            Expand(item.Body);
        }
        return items;
    }
    public static SnippetCatalog Load(string globalPath, string projectFolder)
    {
        var items = Parse(Example).ToDictionary(s => s.Prefix + ":" + s.Context); var issues = new List<string>();
        foreach (var path in new[] { globalPath, Path.Combine(projectFolder, ".tikzedt", "snippets.json") })
        {
            try
            {
                if (!File.Exists(path)) continue;
                if (new FileInfo(path).Length > 1024 * 1024) throw new FormatException("Snippet file exceeds 1 MB.");
                foreach (var snippet in Parse(File.ReadAllText(path))) items[snippet.Prefix + ":" + snippet.Context] = snippet;
            }
            catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException) { issues.Add(path + ": " + e.Message); }
        }
        return new(items.Values.ToArray(), issues);
    }
    public static SnippetExpansion Expand(string body, string selection = "")
    {
        var text = new StringBuilder(); var tokens = new List<(string? Literal, int Number, string? Default)>();
        void Flush() { if (text.Length > 0) { tokens.Add((text.ToString(), -1, null)); text.Clear(); } }
        for (var i = 0; i < body.Length;)
        {
            if (body[i] == '\\' && i + 1 < body.Length && body[i + 1] == '$') { text.Append('$'); i += 2; continue; }
            if (body[i] != '$' || i + 1 >= body.Length || (!char.IsDigit(body[i + 1]) && body[i + 1] != '{')) { text.Append(body[i++]); continue; }
            Flush(); i++;
            if (body[i] == '{')
            {
                var start = ++i; var depth = 1;
                while (i < body.Length && depth > 0) { if (body[i] == '{') depth++; else if (body[i] == '}') depth--; if (depth > 0) i++; }
                if (depth != 0) throw new FormatException("Unclosed snippet placeholder.");
                var value = body[start..i++];
                if (value is "SELECTION" or "TM_SELECTED_TEXT") { tokens.Add((selection, -1, null)); continue; }
                var separator = value.IndexOf(':'); var numberText = separator < 0 ? value : value[..separator];
                if (!int.TryParse(numberText, out var number) || number is < 0 or > 99) throw new FormatException("Use $0, $1…$99, ${1:default} or ${SELECTION}. Choices, nested fields and transforms are not supported.");
                var initial = separator < 0 ? null : value[(separator + 1)..];
                if (initial?.Contains("${", StringComparison.Ordinal) == true) throw new FormatException("Nested snippet fields are not supported.");
                tokens.Add((null, number, initial));
            }
            else
            {
                var start = i; while (i < body.Length && char.IsDigit(body[i])) i++;
                if (!int.TryParse(body[start..i], out var number) || number > 99) throw new FormatException("Snippet field numbers must be 0–99.");
                tokens.Add((null, number, null));
            }
        }
        Flush(); var defaults = tokens.Where(t => t.Number > 0).GroupBy(t => t.Number).ToDictionary(g => g.Key, g => g.FirstOrDefault(t => t.Default is not null).Default ?? "");
        var fields = new List<SnippetField>(); int? caret = null;
        foreach (var token in tokens)
        {
            if (token.Literal is { } literal) text.Append(literal);
            else if (token.Number == 0) { if (caret is not null) throw new FormatException("Use $0 only once."); caret = text.Length; }
            else { var value = defaults[token.Number]; fields.Add(new(token.Number, text.Length, value.Length)); text.Append(value); }
        }
        return new(text.ToString(), fields, caret ?? text.Length);
    }
}
