using System.IO;
using TikzEdt.Modern;

internal static class SymbolRegression
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        LatexSymbol Find(string command) => LatexSymbolCatalog.Items.First(s => s.Command == command);
        Check(SymbolSearch.Score("real numbers", Find(@"\mathbb{R}")) > 0, "Meaning search failed.");
        Check(SymbolSearch.Score("integrl", Find(@"\int")) > 0 && SymbolSearch.Score("\\alpha", Find(@"\alpha")) > 0, "Fuzzy/command search failed.");
        Check(Find(@"\therefore").Package == "amssymb" && Find(@"\iint").Package == "amsmath" && Find(@"\alpha").Package is null, "Package metadata incorrect.");
        var path = Path.GetFullPath("artifacts/symbol-regression/" + Guid.NewGuid().ToString("N") + "/symbols.json");
        var prefs = new SymbolPreferences(); prefs.Favorites.Add(@"\alpha"); for (var i = 0; i < 40; i++) prefs.Record(i.ToString()); prefs.Record("39"); prefs.Save(path);
        var loaded = SymbolPreferences.Load(path); Check(loaded.Favorites.Contains(@"\alpha") && loaded.Recent.Count == 30 && loaded.Recent[0] == "39", "Favorites/recent persistence failed.");
        File.WriteAllText(path, "broken"); Check(SymbolPreferences.Load(path).Recent.Count == 0, "Corrupt preferences not tolerated.");
        Check(LatexSymbolCatalog.IsMathContext("$x ", 3) && !LatexSymbolCatalog.IsMathContext("text ", 5), "Math insertion context regressed.");
        Console.WriteLine("A6: command/meaning/fuzzy search, package metadata, bounded MRU, favorites persistence and corrupt-file fallback passed.");
    }
}
