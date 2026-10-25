using System.IO;
using System.Text.Json;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

internal static class SymbolSearch
{
    private static readonly Dictionary<string, string> Meanings = new()
    {
        [@"\infty"] = "infinity infinite unlimited", [@"\partial"] = "partial derivative differentiation", [@"\nabla"] = "gradient divergence curl del",
        [@"\sum"] = "summation sum series", [@"\prod"] = "product multiplication", [@"\int"] = "integral integration calculus", [@"\iint"] = "double integral integration", [@"\iiint"] = "triple integral integration",
        [@"\oint"] = "contour closed integral", [@"\sqrt{}"] = "square root radical", [@"\frac{}{}"] = "fraction quotient division",
        [@"\ne"] = "not equal unequal", [@"\approx"] = "approximately equal approximation", [@"\leq"] = "less than or equal", [@"\geq"] = "greater than or equal",
        [@"\times"] = "multiply cross product multiplication", [@"\cdot"] = "dot product multiplication", [@"\pm"] = "plus minus uncertainty",
        [@"\in"] = "element of membership belongs", [@"\notin"] = "not element of", [@"\emptyset"] = "empty set null", [@"\forall"] = "for all universal quantifier", [@"\exists"] = "there exists existential quantifier",
        [@"\mathbb{N}"] = "natural numbers positive integers", [@"\mathbb{Z}"] = "integer numbers", [@"\mathbb{Q}"] = "rational numbers", [@"\mathbb{R}"] = "real numbers", [@"\mathbb{C}"] = "complex numbers",
        [@"\rightarrow"] = "right arrow implies maps to", [@"\leftrightarrow"] = "bidirectional arrow equivalent", [@"\Rightarrow"] = "implies implication", [@"\Leftrightarrow"] = "if and only if equivalence iff",
        [@"\therefore"] = "therefore consequently", [@"\because"] = "because since", [@"\nexists"] = "does not exist", [@"\square"] = "square box qed", [@"\measuredangle"] = "measured angle geometry", [@"\rightleftharpoons"] = "equilibrium reversible reaction",
        [@"\perp"] = "perpendicular orthogonal", [@"\parallel"] = "parallel", [@"\cup"] = "union sets", [@"\cap"] = "intersection sets", [@"\subseteq"] = "subset inclusion", [@"\vec{}"] = "vector arrow accent"
    };
    public static string Meaning(string command) => Meanings.GetValueOrDefault(command, command.TrimStart('\\'));
    public static string? Package(string command) => command.StartsWith(@"\mathbb", StringComparison.Ordinal) || command is @"\nexists" or @"\therefore" or @"\because" or @"\square" or @"\measuredangle" or @"\mho" ? "amssymb" : command is @"\iint" or @"\iiint" ? "amsmath" : null;
    public static int Score(string query, LatexSymbol symbol)
    {
        if (string.IsNullOrWhiteSpace(query)) return 0;
        var text = symbol.Command.TrimStart('\\') + " " + symbol.Glyph + " " + symbol.Meaning + " " + symbol.Category;
        var score = PaletteSearch.Score(query.TrimStart('\\'), text);
        if (score >= 0) return score;
        // A single mistyped letter should not hide a familiar symbol name.
        var word = query.Trim().TrimStart('\\').ToLowerInvariant();
        return word.Length >= 4 && text.ToLowerInvariant().Split(' ').Any(w => OneEdit(word, w)) ? 1 : -1;
    }
    private static bool OneEdit(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 1) return false;
        var i = 0; var j = 0; var edits = 0;
        while (i < a.Length && j < b.Length) { if (a[i] == b[j]) { i++; j++; continue; } if (++edits > 1) return false; if (a.Length >= b.Length) i++; if (b.Length >= a.Length) j++; }
        return edits + (a.Length - i) + (b.Length - j) <= 1;
    }
}

internal sealed class SymbolPreferences
{
    public HashSet<string> Favorites { get; set; } = new(StringComparer.Ordinal);
    public List<string> Recent { get; set; } = [];
    private static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TikzEdt", "Modern", "symbols.json");
    public void Record(string command) { Recent.Remove(command); Recent.Insert(0, command); if (Recent.Count > 30) Recent.RemoveRange(30, Recent.Count - 30); }
    public static SymbolPreferences Load(string? path = null)
    {
        try { path ??= DefaultPath; if (!File.Exists(path) || new FileInfo(path).Length > 100000) return new(); var result = JsonSerializer.Deserialize<SymbolPreferences>(File.ReadAllText(path)) ?? new(); result.Favorites ??= []; result.Recent ??= []; result.Recent = result.Recent.Distinct().Take(30).ToList(); return result; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public void Save(string? path = null)
    {
        try { path ??= DefaultPath; Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, path, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); } }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Preferences must never block editing. */ }
    }
}
