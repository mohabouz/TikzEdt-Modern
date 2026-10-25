using System.Text.RegularExpressions;

namespace TikzEdt.Modern.Core.Documents;

public sealed record TexEntry(string Name, string Insertion, string Help, string Kind = "Command", string? Package = null, string? Library = null);
public sealed record TexIndex(string Source, string Masked, IReadOnlySet<string> Packages, IReadOnlySet<string> Libraries, IReadOnlyList<TexEntry> Local);

/// <summary>Deliberately scoped, non-executing TeX intelligence. Unknown macros and keys remain valid.</summary>
public static class TexKnowledge
{
    public static IReadOnlyList<TexEntry> Entries { get; } =
    [
        new("\\draw", "\\draw[$0] (0,0) -- (1,1);", "Draw a TikZ path; terminate with a semicolon.", Package: "tikz"),
        new("\\node", "\\node[$0] at (0,0) {text};", "Place a named or unnamed TikZ text/shape node.", Package: "tikz"),
        new("\\path", "\\path[$0] (0,0) -- (1,1);", "Construct a path without an implicit stroke.", Package: "tikz"),
        new("\\fill", "\\fill[$0] (0,0) circle (1);", "Fill a closed TikZ path.", Package: "tikz"),
        new("\\coordinate", "\\coordinate ($0) at (0,0);", "Define a reusable named coordinate.", Package: "tikz"),
        new("\\tikzset", "\\tikzset{$0}", "Define TikZ styles and key defaults.", Package: "tikz"),
        new("\\usetikzlibrary", "\\usetikzlibrary{$0}", "Load comma-separated TikZ libraries in the preamble.", Package: "tikz"),
        new("\\chemfig", "\\chemfig{$0}", "Molecular formula: atoms, bonds and branches.", Package: "chemfig"),
        new("\\chemname", "\\chemname{\\chemfig{$0}}{name}", "Name below a molecule.", Package: "chemfig"),
        new("\\setchemfig", "\\setchemfig{$0}", "Configure bond length, atom separation and molecule styles.", Package: "chemfig"),
        new("\\lewis", "\\lewis{$0}", "Lewis lone-pair notation.", Package: "chemfig"),
        new("\\schemestart", "\\schemestart\n$0\n\\schemestop", "Start a chemical reaction scheme.", Package: "chemfig"),
        new("\\arrow", "\\arrow{$0}", "Reaction scheme arrow.", Package: "chemfig"),
        new("\\ctikzset", "\\ctikzset{$0}", "Configure CircuitikZ keys.", Package: "circuitikz"),
        new("\\begin{circuitikz}", "\\begin{circuitikz}\n$0\n\\end{circuitikz}", "Circuit diagram environment.", "Environment", "circuitikz"),
        new("\\psset", "\\psset{$0}", "PSTricks drawing parameters.", Package: "pstricks"),
        new("\\pstTubeEssais", "\\pstTubeEssais[$0]", "Test tube apparatus.", Package: "pst-labo"),
        new("\\pstDosage", "\\pstDosage[$0]", "Titration apparatus with burette.", Package: "pst-labo"),
        new("\\pgfmathsetmacro", "\\pgfmathsetmacro{\\value}{$0}", "Evaluate a PGF mathematical expression.", Package: "tikz"),
        new("\\includegraphics", "\\includegraphics[width=4cm]{$0}", "Include a relative image asset.", Package: "graphicx"),
        new("\\usepackage", "\\usepackage{$0}", "Load a package in the document preamble."),
        new("pattern=", "pattern=$0", "Pattern fill; requires patterns.", "Option", Library: "patterns"),
        new("pattern color=", "pattern color=$0", "Pattern ink color.", "Option", Library: "patterns"),
        new("decorate", "decorate", "Decorate a path.", "Option", Library: "decorations.pathreplacing"),
        new("decoration=", "decoration={$0}", "Decoration settings.", "Option", Library: "decorations.pathreplacing"),
        new("diamond", "diamond", "Diamond-shaped node.", "Option", Library: "shapes.geometric"),
        new("regular polygon", "regular polygon", "Regular polygon node.", "Option", Library: "shapes.geometric"),
        new("right=of", "right=of $0", "Place a node relative to a named node.", "Option", Library: "positioning"),
        new("below=of", "below=of $0", "Place a node relative to a named node.", "Option", Library: "positioning"),
        new("R", "R", "Resistor (to-path component).", "Option", "circuitikz"),
        new("C", "C", "Capacitor (to-path component).", "Option", "circuitikz"),
        new("L", "L", "Inductor (to-path component).", "Option", "circuitikz"),
        new("D", "D", "Diode (to-path component).", "Option", "circuitikz"),
        new("battery1", "battery1", "Single-cell battery.", "Option", "circuitikz"),
        new("ground", "ground", "Ground node.", "Option", "circuitikz"),
        new("atom sep=", "atom sep=$0pt", "Default bond length.", "Chemfig option", "chemfig"),
        new("bond style=", "bond style={$0}", "TikZ style for bonds.", "Chemfig option", "chemfig"),
        new("angle increment=", "angle increment=$0", "Chemfig angle increment in degrees.", "Chemfig option", "chemfig"),
        new("niveauLiquide1=", "niveauLiquide1=$0", "First liquid level.", "Labo option", "pst-labo"),
        new("aspectLiquide1=", "aspectLiquide1=$0", "First liquid appearance style.", "Labo option", "pst-labo")
    ];
    public static readonly string[] Libraries = ["calc", "positioning", "patterns", "arrows.meta", "shapes.geometric", "shapes.symbols", "decorations.pathreplacing", "decorations.markings", "intersections", "fit", "matrix", "backgrounds", "quotes", "angles", "babel", "3d"];
    public static readonly string[] Packages = ["tikz", "pgfplots", "circuitikz", "chemfig", "pst-labo", "pstricks", "amsmath", "amssymb", "graphicx", "xcolor", "siunitx", "mhchem"];
    private static MatchCollection Matches(string text, string expression) => Regex.Matches(text, expression, RegexOptions.None, TimeSpan.FromMilliseconds(150));
    public static string Mask(string source)
    {
        var masked = MathContext.MaskComments(source);
        return Regex.Replace(masked, @"\\begin\{(verbatim|lstlisting|minted)\}[\s\S]*?\\end\{\1\}|\\verb\*?(?<d>[^a-zA-Z\s])[\s\S]*?\k<d>", m => new string(m.Value.Select(c => c is '\r' or '\n' ? c : ' ').ToArray()), RegexOptions.None, TimeSpan.FromMilliseconds(150));
    }
    public static TexIndex Build(string source, string preamble = "", CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (source.Length + preamble.Length > 2_000_000) return new(source, "", new HashSet<string>(), new HashSet<string>(), []);
        var masked = Mask(source); var all = Mask(preamble) + "\n" + masked;
        var packages = new HashSet<string>(StringComparer.Ordinal); var libraries = new HashSet<string>(StringComparer.Ordinal);
        void Collect(string pattern, HashSet<string> target) { foreach (Match match in Matches(all, pattern)) { token.ThrowIfCancellationRequested(); foreach (var item in match.Groups[1].Value.Split(',')) target.Add(item.Trim()); } }
        Collect(@"\\(?:usepackage|RequirePackage)\s*(?:\[[^\]]*\]\s*)?\{([^{}]+)\}", packages);
        Collect(@"\\usetikzlibrary\s*\{([^{}]+)\}", libraries);
        // These are guaranteed by the app's fragment wrapper, not by full documents.
        if (!all.Contains(@"\begin{document}", StringComparison.Ordinal)) foreach (var package in new[] { "tikz", "amsmath", "amssymb", "xcolor", "bm" }) packages.Add(package);
        if (masked.Contains(@"\chemfig", StringComparison.Ordinal)) packages.Add("chemfig");
        if (masked.Contains(@"\begin{circuitikz}", StringComparison.Ordinal)) packages.Add("circuitikz");
        foreach (Match match in Matches(source + "\n" + preamble, @"(?m)^\s*%\s*!TIKZEDT\s+PACKAGE\s+([a-zA-Z0-9_.-]+)")) packages.Add(match.Groups[1].Value);
        var local = new List<TexEntry>();
        foreach (Match match in Matches(all, @"\\(?:newcommand|renewcommand|providecommand|DeclareRobustCommand)\*?\s*\{?\s*(\\[a-zA-Z@]+)\s*\}?\s*(?:\[(\d)\])?|\\def\s*(\\[a-zA-Z@]+)((?:#[1-9])*)"))
        {
            token.ThrowIfCancellationRequested(); var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value;
            var count = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : match.Groups[4].Value.Length / 2;
            local.Add(new(name, name + string.Concat(Enumerable.Range(0, count).Select(i => i == 0 ? "{$0}" : "{}")), "Locally defined macro; " + count + " argument(s).", "Local macro"));
        }
        foreach (Match match in Matches(all, @"(?:\{|,)\s*([^{}\[\],=\r\n]+?)/\.(?:style|append style|code)\s*=")) local.Add(new(match.Groups[1].Value.Trim(), match.Groups[1].Value.Trim(), "Locally defined TikZ style/key.", "Style"));
        foreach (Match match in Matches(all, @"\\(?:node|coordinate)\b[^;{}]*?\(([a-zA-Z][a-zA-Z0-9:_-]*)\)")) local.Add(new(match.Groups[1].Value, match.Groups[1].Value, "Named node or coordinate in this source.", "Coordinate"));
        token.ThrowIfCancellationRequested();
        return new(source, masked, packages, libraries, local.DistinctBy(e => (e.Name, e.Kind)).Take(2000).ToArray());
    }
    public static string Context(TexIndex index, int offset)
    {
        offset = Math.Clamp(offset, 0, index.Masked.Length); if (offset > 0 && index.Source[offset - 1] != index.Masked[offset - 1] && index.Masked[offset - 1] == ' ') return "none";
        var prefix = index.Masked[..offset];
        if (Regex.IsMatch(prefix, @"\\usetikzlibrary\s*\{[^}]*$", RegexOptions.None, TimeSpan.FromMilliseconds(100))) return "library";
        if (Regex.IsMatch(prefix, @"\\(?:usepackage|RequirePackage)\s*(?:\[[^\]]*\]\s*)?\{[^}]*$", RegexOptions.None, TimeSpan.FromMilliseconds(100))) return "package";
        if (Regex.IsMatch(prefix, @"\([a-zA-Z][a-zA-Z0-9:_-]*$|\($", RegexOptions.None, TimeSpan.FromMilliseconds(100))) return "coordinate";
        // Only a live option list in the current statement; node labels with text [ are not options.
        var bracket = prefix.LastIndexOf('['); var close = prefix.LastIndexOf(']'); var command = bracket < 0 ? -1 : prefix.LastIndexOf('\\', bracket);
        if (bracket > close && command >= 0 && !prefix[(command)..bracket].Contains('{') && !prefix[(command)..bracket].Contains(';'))
        {
            if (prefix[command..].StartsWith(@"\chemfig")) return "Chemfig option";
            if (prefix[command..].StartsWith(@"\pst")) return "Labo option";
            return "option";
        }
        var setting = Regex.Match(prefix, @"\\(tikzset|ctikzset|setchemfig)\s*\{[^{}]*$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        if (setting.Success) return setting.Groups[1].Value == "setchemfig" ? "Chemfig option" : "option";
        return "command";
    }
    public static IReadOnlyList<TexEntry> Complete(TexIndex index, int offset)
    {
        var context = Context(index, offset);
        if (context == "none") return [];
        if (context == "library") return Libraries.Select(s => new TexEntry(s, s, "TikZ library; load in the preamble.", "Library")).ToArray();
        if (context == "package") return Packages.Select(s => new TexEntry(s, s, "LaTeX package; installation is separate from loading.", "Package")).ToArray();
        if (context == "coordinate") return index.Local.Where(e => e.Kind == "Coordinate").ToArray();
        if (context == "option")
        {
            var value = Regex.Match(index.Masked[..Math.Clamp(offset, 0, index.Masked.Length)], @"(?:\[|,)\s*(anchor|line cap|line join|pattern)\s*=\s*[^,\]{}]*$", RegexOptions.None, TimeSpan.FromMilliseconds(100));
            if (value.Success)
            {
                string[] choices = value.Groups[1].Value switch { "anchor" => ["center", "north", "south", "east", "west", "north east", "north west", "south east", "south west", "base", "mid"], "line cap" => ["butt", "round", "rect"], "line join" => ["miter", "round", "bevel"], _ => ["north east lines", "north west lines", "horizontal lines", "vertical lines", "grid", "dots", "crosshatch"] };
                return choices.Select(c => new TexEntry(c, c, "Value for " + value.Groups[1].Value, "Value")).ToArray();
            }
        }
        var entries = context == "command" ? Entries.Where(e => e.Kind is "Command" or "Environment").Concat(index.Local.Where(e => e.Kind == "Local macro"))
            : Entries.Where(e => (e.Kind == "Option" || e.Kind == context) && (e.Library is null || index.Libraries.Contains(e.Library)) && (e.Package is null || index.Packages.Contains(e.Package))).Concat(index.Local.Where(e => e.Kind == "Style"));
        return entries.ToArray();
    }
    public static string Describe(TexEntry e) => e.Help + (e.Package is null ? "" : "\nPackage: " + e.Package) + (e.Library is null ? "" : "\nTikZ library: " + e.Library);
    public static string? Hover(TexIndex index, int offset)
    {
        if (offset < 0 || offset >= index.Masked.Length) return null;
        foreach (Match m in Matches(index.Masked, @"\\[a-zA-Z@]+|[a-zA-Z][a-zA-Z0-9._-]*"))
            if (offset >= m.Index && offset < m.Index + m.Length)
            { var entry = index.Local.Concat(Entries).FirstOrDefault(e => e.Name.TrimEnd('=') == m.Value); return entry is null ? null : entry.Name + "\n" + Describe(entry); }
        return null;
    }
}
