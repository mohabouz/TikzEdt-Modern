using System.Text.RegularExpressions;

namespace TikzEdt.Modern;

internal sealed record ChemistryWarning(int AtomIndex, string Message);

/// <summary>Advisory checks for common main-group labels, not a full chemical sanitizer.</summary>
internal static class ChemfigChemistry
{
    public static IReadOnlyList<ChemistryWarning> Analyze(ChemfigMolecule molecule)
    {
        var warnings = new List<ChemistryWarning>();
        var orders = new int[molecule.Atoms.Count];
        foreach (var bond in molecule.Bonds)
        { var order = bond.Kind == "=" ? 2 : bond.Kind == "~" ? 3 : 1; orders[bond.A] += order; orders[bond.B] += order; }
        for (var i = 0; i < molecule.Atoms.Count; i++)
        {
            var atom = molecule.Atoms[i]; var raw = atom.Label.Length == 0 ? "C" : atom.Label;
            var positive = raw.EndsWith('+') || raw.Contains("^{+}");
            var negative = raw.EndsWith('-') || raw.Contains("^{-}");
            var label = Regex.Replace(raw, @"\^\{[+-]\}|\^[+-]|[+-]$", "");
            var match = Regex.Match(label, @"^(?<e>[A-Z][a-z]?)(?:H(?:_(?:\{(?<h>\d+)\}|(?<h>\d+)))?)?$");
            if (!match.Success) match = Regex.Match(label, @"^H(?:_(?:\{(?<h>\d+)\}|(?<h>\d+)))?(?<e>C|N|O|S|P|Si)$");
            if (!match.Success) continue; // e.g. COOH or C_2H_5 is a group, not one atom.
            var element = match.Groups["e"].Value;
            var hydrogenCount = match.Groups["h"].Success ? int.TryParse(match.Groups["h"].Value, out var n) ? Math.Min(n, 9999) : 9999
                : label != element && label.Contains('H') ? 1 : 0;
            var max = element switch { "H" or "F" => 1, "B" => negative ? 4 : 3, "C" or "Si" => 4,
                "N" => positive ? 4 : negative ? 2 : 3, "O" => positive ? 3 : negative ? 1 : 2,
                "P" => 5, "S" => 6, _ => -1 };
            if (max < 0) continue; // Metals/ambiguous hypervalent species require a richer model.
            var total = orders[i] + hydrogenCount;
            if (total > max)
                warnings.Add(new(i, $"Atom {i + 1} ({raw}): bond-order total {total} exceeds the usual {max} for {element}. Check bonds, hydrogens and charge."));
            else if (element is "H" or "B" or "C" or "N" or "O" or "F")
            {
                var electrons = 2 * (total + atom.LonePairs.Count); var limit = element == "H" ? 2 : 8;
                if (electrons > limit) warnings.Add(new(i, $"Atom {i + 1} ({raw}): bonds and lone pairs account for {electrons} electrons, exceeding the usual {limit}-electron shell."));
            }
        }
        return warnings;
    }
}
