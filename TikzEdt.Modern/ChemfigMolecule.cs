using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;

namespace TikzEdt.Modern;

internal sealed record ChemLonePair(double Angle, bool AsLine = true);
internal sealed record ChemAtom(string Label, Point Position)
{
    public IReadOnlyList<ChemLonePair> LonePairs { get; init; } = [];
    public bool PairBounds { get; init; }
}
internal sealed record ChemBond(int A, int B, string Kind);

/// <summary>A deliberately bounded Chemfig graph editor. Unsupported syntax is rejected, never normalized away.</summary>
internal sealed class ChemfigMolecule
{
    public List<ChemAtom> Atoms { get; } = [];
    public List<ChemBond> Bonds { get; } = [];
    public ChemfigMolecule Clone()
    { var copy = new ChemfigMolecule(); copy.Atoms.AddRange(Atoms); copy.Bonds.AddRange(Bonds); return copy; }
    public IReadOnlyList<ChemfigMolecule> Components()
    {
        var remaining = Enumerable.Range(0, Atoms.Count).ToHashSet();
        var result = new List<ChemfigMolecule>();
        while (remaining.Count > 0)
        {
            var found = new HashSet<int>(); var pending = new Stack<int>(); pending.Push(remaining.Min());
            while (pending.TryPop(out var atom))
            {
                if (!found.Add(atom)) continue;
                foreach (var bond in Bonds)
                    if (bond.A == atom) pending.Push(bond.B); else if (bond.B == atom) pending.Push(bond.A);
            }
            var indices = found.Order().ToArray(); var map = indices.Select((old, index) => (old, index)).ToDictionary(p => p.old, p => p.index);
            var component = new ChemfigMolecule(); component.Atoms.AddRange(indices.Select(i => Atoms[i]));
            component.Bonds.AddRange(Bonds.Where(b => found.Contains(b.A) && found.Contains(b.B)).Select(b => b with { A = map[b.A], B = map[b.B] }));
            result.Add(component); remaining.ExceptWith(found);
        }
        return result;
    }
    public ChemfigMolecule Without(IReadOnlySet<int> atoms, IReadOnlySet<int> bonds)
    {
        var result = new ChemfigMolecule(); var map = new Dictionary<int, int>();
        for (var i = 0; i < Atoms.Count; i++) if (!atoms.Contains(i)) { map[i] = result.Atoms.Count; result.Atoms.Add(Atoms[i]); }
        for (var i = 0; i < Bonds.Count; i++)
        { var b = Bonds[i]; if (!bonds.Contains(i) && map.ContainsKey(b.A) && map.ContainsKey(b.B)) result.Bonds.Add(b with { A = map[b.A], B = map[b.B] }); }
        if (result.Atoms.Count == 0) result.Atoms.Add(new("", new Point()));
        return result;
    }
    private static string Number(double value) => (Math.Abs(value) < .0000005 ? 0 : value).ToString("0.######", CultureInfo.InvariantCulture);
    public static bool ValidLabel(string label) => Regex.IsMatch(label, @"^(?:[A-Za-z0-9+]|[_^](?:\{[A-Za-z0-9+()-]+\}|[A-Za-z0-9]))*$");
    internal static string AtomSource(ChemAtom atom) => atom.LonePairs.Count == 0 ? atom.Label
        : "\\" + (atom.PairBounds ? "Charge" : "charge") + "{" + string.Join(",", atom.LonePairs.OrderBy(p => p.Angle).Select(p => Number(p.Angle) + "=" + (p.AsLine ? @"\|" : @"\:"))) + "}{" + atom.Label + "}";
    private static ChemAtom ReadAtom(string source, Point position)
    {
        if (ValidLabel(source)) return new(source, position);
        var charge = Regex.Match(source, @"^\\(?<macro>charge|Charge)\{(?<pairs>[^{}]+)\}\{(?<atom>.*)\}$");
        if (!charge.Success || !ValidLabel(charge.Groups["atom"].Value)) throw new FormatException("This atom macro is not supported. Use a simple label or a lone-pair \\charge command.");
        var pairs = new List<ChemLonePair>();
        foreach (var entry in charge.Groups["pairs"].Value.Split(','))
        {
            var pair = Regex.Match(entry, @"^\s*(?<angle>[+-]?(?:\d+(?:\.\d*)?|\.\d+))\s*=\s*\\(?<kind>:|\|)\s*$");
            if (!pair.Success) throw new FormatException("Only dot-pair (\\:) and line-pair (\\|) charges with numeric angles are visually editable. Source was left unchanged.");
            var angle = double.Parse(pair.Groups["angle"].Value, CultureInfo.InvariantCulture);
            if (!double.IsFinite(angle)) throw new FormatException("Invalid lone-pair angle.");
            angle = (angle % 360 + 360) % 360;
            if (pairs.Any(p => Math.Abs(p.Angle - angle) < 1e-8) || pairs.Count >= 16) throw new FormatException("Duplicate or excessive lone-pair positions.");
            pairs.Add(new(angle, pair.Groups["kind"].Value == "|"));
        }
        return new(charge.Groups["atom"].Value, position) { LonePairs = pairs.ToArray(), PairBounds = charge.Groups["macro"].Value == "Charge" };
    }
    public string ToSource()
    {
        if (Atoms.Count == 0) return "";
        if (CompactRing() is { } ring) return ring;
        var visited = new HashSet<int>(); var treeEdges = new HashSet<int>();
        void Visit(int atom)
        {
            visited.Add(atom);
            for (var i = 0; i < Bonds.Count; i++)
            {
                var bond = Bonds[i]; var other = bond.A == atom ? bond.B : bond.B == atom ? bond.A : -1;
                if (other < 0 || visited.Contains(other)) continue;
                treeEdges.Add(i); Visit(other);
            }
        }
        Visit(0);
        if (visited.Count != Atoms.Count) throw new InvalidOperationException("Connect all atoms before inserting the molecule.");
        var closures = Enumerable.Range(0, Bonds.Count).Where(i => !treeEdges.Contains(i)).ToArray();
        visited.Clear(); var output = new StringBuilder();
        void Write(int atom)
        {
            visited.Add(atom); output.Append(AtomSource(Atoms[atom]));
            foreach (var edge in closures)
            {
                var bond = Bonds[edge];
                if (bond.A != atom && bond.B != atom) continue;
                var hookKind = bond.A == atom ? bond.Kind : ChemfigBondTypes.Reverse(bond.Kind);
                output.Append("?[r").Append(Math.Min(bond.A, bond.B)).Append('x').Append(Math.Max(bond.A, bond.B)).Append(',').Append(ChemfigBondTypes.ToHook(hookKind)).Append(']');
            }
            var children = treeEdges.Order().Select(edge => (Edge: edge, Other: Bonds[edge].A == atom ? Bonds[edge].B : Bonds[edge].B == atom ? Bonds[edge].A : -1))
                .Where(child => child.Other >= 0 && !visited.Contains(child.Other)).ToArray();
            for (var index = 0; index < children.Length; index++)
            {
                var bond = Bonds[children[index].Edge]; var other = children[index].Other;
                var delta = Atoms[other].Position - Atoms[atom].Position;
                var kind = bond.A == atom ? bond.Kind : ChemfigBondTypes.Reverse(bond.Kind);
                var branch = index < children.Length - 1;
                if (branch) output.Append('(');
                output.Append(kind).Append(BondOptions(delta));
                Write(other);
                if (branch) output.Append(')');
            }
        }
        Write(0); return output.ToString();
    }
    private static string BondOptions(Vector delta)
    {
        var angle = Math.Atan2(delta.Y, delta.X) * 180 / Math.PI;
        var length = Math.Abs(delta.Length - 1) < 1e-8 ? "" : Number(delta.Length);
        var direction = Math.Abs(angle) < .0000005 ? "" : ":" + Number(angle);
        // Keep nondefault angles absolute: [2] would change meaning if a preamble
        // changed Chemfig's angle increment. [:90] does not depend on that setting.
        if (direction.Length == 0 && length.Length == 0) return "";
        return "[" + direction + (length.Length > 0 ? "," + length : "") + "]";
    }
    private string? CompactRing()
    {
        var n = Atoms.Count;
        if (n is < 3 or > 8 || Bonds.Count != n || Bonds.Any(b => b.Kind is not ("-" or "=" or "~"))) return null;
        if (Atoms.Any(a => AtomSource(a).IndexOfAny(['(', ')', '*']) >= 0)) return null;
        if (Enumerable.Range(0, n).Any(i => Bonds.Count(b => b.A == i || b.B == i) != 2)) return null;
        var template = Ring(n, false); var current = 0; var prior = -1; var seen = new HashSet<int> { 0 };
        var output = new StringBuilder(AtomSource(Atoms[0]) + "*" + n.ToString(CultureInfo.InvariantCulture) + "(");
        for (var i = 0; i < n; i++)
        {
            var expected = Atoms[0].Position + (template.Atoms[(i + 1) % n].Position - template.Atoms[0].Position);
            var edge = Bonds.FirstOrDefault(b => (b.A == current || b.B == current) &&
                (b.A == current ? b.B : b.A) != prior &&
                (Atoms[b.A == current ? b.B : b.A].Position - expected).Length < 1e-6);
            if (edge is null) return null;
            var next = edge.A == current ? edge.B : edge.A;
            if (i < n - 1 && !seen.Add(next) || i == n - 1 && next != 0) return null;
            output.Append(edge.Kind); if (i < n - 1) output.Append(AtomSource(Atoms[next]));
            prior = current; current = next;
        }
        return output.Append(')').ToString();
    }
    public static ChemfigMolecule Ring(int count, bool alternating)
    {
        var result = new ChemfigMolecule();
        var position = new Point();
        for (var i = 0; i < count; i++)
        {
            result.Atoms.Add(new("", position));
            // Chemfig starts at the south-west vertex and closes vertically down.
            var angle = -Math.PI / 2 + (i + 1) * 2 * Math.PI / count;
            position += new Vector(Math.Cos(angle), Math.Sin(angle));
            result.Bonds.Add(new(i, (i + 1) % count, alternating && i % 2 == 0 ? "=" : "-"));
        }
        return result;
    }
    public static ChemfigMolecule Parse(string source)
    {
        var compact = Regex.Match(source.Trim(), @"^(?<root>[^*()]*)\*(?<n>[3-8])\((?<body>[^()]*)\)$");
        if (compact.Success)
        {
            var n = int.Parse(compact.Groups["n"].Value); var body = compact.Groups["body"].Value;
            var sides = new List<(string Kind, string Label)>(); var start = 0; var depth = 0;
            for (var i = 0; i <= body.Length; i++)
            {
                if (i < body.Length && body[i] == '{') depth++;
                if (i < body.Length && body[i] == '}') depth--;
                if (i == body.Length || depth == 0 && body[i] is '-' or '=' or '~')
                {
                    if (i > start) sides.Add((body[start].ToString(), body[(start + 1)..i]));
                    start = i;
                }
            }
            if (depth != 0 || body.Length == 0 || body[0] is not ('-' or '=' or '~') || sides.Count != n || sides[^1].Label.Length > 0)
                throw new FormatException("The ring must have one bond per side and no repeated closing atom.");
            var rootLabel = compact.Groups["root"].Value;
            var ring = Ring(n, false);
            ring.Atoms[0] = ReadAtom(rootLabel, ring.Atoms[0].Position);
            for (var i = 0; i < n; i++)
            {
                ring.Bonds[i] = ring.Bonds[i] with { Kind = sides[i].Kind };
                if (i < n - 1) ring.Atoms[i + 1] = ReadAtom(sides[i].Label, ring.Atoms[i + 1].Position);
            }
            return ring;
        }
        var molecule = new ChemfigMolecule(); var index = 0;
        var hooks = new Dictionary<string, (int Atom, string Kind)>();
        void Space() { while (index < source.Length && char.IsWhiteSpace(source[index])) index++; }
        string Label()
        {
            Space(); var start = index; var braces = 0;
            while (index < source.Length)
            {
                var c = source[index];
                if (braces == 0 && ("-=~<>()?[*".Contains(c))) break;
                if (c == '{') braces++; if (c == '}') braces--;
                index++;
            }
            var label = source[start..index].Trim();
            if (braces != 0) throw new FormatException("Unbalanced atom label.");
            return label;
        }
        int Atom(Point position)
        {
            var atom = molecule.Atoms.Count;
            if (atom >= 500) throw new FormatException("Visual editing is limited to 500 atoms per molecule.");
            molecule.Atoms.Add(ReadAtom(Label(), position)); Space();
            while (index < source.Length && source[index] == '?')
            {
                var hook = Regex.Match(source[index..], @"^\?\[([A-Za-z0-9]+)(?:,([1-9]))?\]");
                if (!hook.Success) throw new FormatException("Unsupported ring hook.");
                index += hook.Length;
                var kind = ChemfigBondTypes.FromHook(hook.Groups[2].Success ? int.Parse(hook.Groups[2].Value, CultureInfo.InvariantCulture) : 1);
                if (hooks.Remove(hook.Groups[1].Value, out var first)) molecule.Bonds.Add(new(atom, first.Atom, kind));
                else hooks.Add(hook.Groups[1].Value, (atom, kind));
                Space();
            }
            return atom;
        }
        void Chain(int current, double previousAngle, int depth)
        {
            if (depth > 64) throw new FormatException("Molecule branching is too deep for visual editing.");
            while (index < source.Length)
            {
                Space(); if (index >= source.Length || source[index] == ')') return;
                if (source[index] == '(')
                {
                    index++; Chain(current, previousAngle, depth + 1); Space();
                    if (index >= source.Length || source[index++] != ')') throw new FormatException("Unclosed branch.");
                    continue;
                }
                var kind = source[index++].ToString();
                if (!"-=~<>".Contains(kind)) throw new FormatException("Unsupported Chemfig construct. Source was left unchanged.");
                if (kind is ">" or "<" && index < source.Length && source[index] is ':' or '|') kind += source[index++];
                var angle = 0d; var length = 1d; Space();
                if (index < source.Length && source[index] == '[')
                {
                    var end = source.IndexOf(']', index); if (end < 0) throw new FormatException("Unclosed bond options.");
                    var options = source[(index + 1)..end].Split(','); index = end + 1;
                    if (options.Length > 2) throw new FormatException("Atom anchoring/style bond options are not editable yet.");
                    var field = options[0].Trim(); var relative = field.StartsWith("::"); var absolute = field.StartsWith(':');
                    if (field.Length > 0)
                    {
                        angle = double.Parse(field.TrimStart(':'), CultureInfo.InvariantCulture);
                        if (!absolute) angle *= 45;
                        if (relative) angle += previousAngle;
                    }
                    if (options.Length == 2 && options[1].Trim().Length > 0) length = double.Parse(options[1], CultureInfo.InvariantCulture);
                }
                if (!double.IsFinite(angle) || !double.IsFinite(length) || length <= 0 || length > 100) throw new FormatException("Invalid bond geometry.");
                var radians = angle * Math.PI / 180;
                var next = Atom(molecule.Atoms[current].Position + new Vector(length * Math.Cos(radians), length * Math.Sin(radians)));
                molecule.Bonds.Add(new(current, next, kind)); current = next; previousAngle = angle;
            }
        }
        var root = Atom(new Point()); Chain(root, 0, 0); Space();
        if (index != source.Length || hooks.Count > 0) throw new FormatException("Unmatched branch or ring hook.");
        return molecule;
    }

    public static (int Offset, int Length, string Body)? FindCommand(string source, int caret)
    {
        foreach (Match command in Regex.Matches(source, @"(?<!\\)\\chemfig\b\s*(?:\[[^\]]*\]\s*)?\{"))
        {
            var lineStart = source.LastIndexOf('\n', Math.Max(0, command.Index - 1)) + 1;
            if (Regex.IsMatch(source[lineStart..command.Index], @"(?<!\\)%")) continue;
            var start = command.Index + command.Length; var depth = 1; var end = start;
            for (; end < source.Length; end++)
            {
                if (source[end] == '\\') { end++; continue; }
                if (source[end] == '{') depth++; if (source[end] == '}' && --depth == 0) break;
            }
            if (end < source.Length && caret >= command.Index && caret <= end + 1)
            {
                if (command.Value.Contains('[')) throw new FormatException("Global Chemfig options are not editable yet; the original command is unchanged.");
                return (command.Index, end + 1 - command.Index, source[start..end]);
            }
        }
        return null;
    }
}
