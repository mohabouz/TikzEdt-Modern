namespace TikzEdt.Modern;

internal sealed record ChemfigBondType(string Token, string Caption, string Description, int HookNumber);

internal static class ChemfigBondTypes
{
    public static readonly IReadOnlyList<ChemfigBondType> All = new[]
    {
        new ChemfigBondType("-", "—  Single / in plane", "Ordinary single bond in the plane of the drawing.", 1),
        new ChemfigBondType("=", "═  Double", "Double bond.", 2),
        new ChemfigBondType("~", "≡  Triple", "Triple bond.", 3),
        new ChemfigBondType(">", "▶  Front · solid", "Solid Cram wedge, wide at the first atom and narrow at the second.", 4),
        new ChemfigBondType(">:", "▱  Back · hashed", "Hashed Cram wedge, wide at the first atom and narrow at the second.", 6),
        new ChemfigBondType("<", "◀  Front · reversed", "Solid Cram wedge with its narrow and wide ends reversed.", 5),
        new ChemfigBondType("<:", "▱  Back · reversed", "Hashed Cram wedge with its narrow and wide ends reversed.", 7),
        new ChemfigBondType(">|", "▷  Hollow", "Hollow Cram wedge, narrow at the second atom.", 8),
        new ChemfigBondType("<|", "◁  Hollow · reversed", "Hollow Cram wedge, narrow at the first atom.", 9)
    };
    public static string Reverse(string token) => token.Length > 0 && token[0] is '>' or '<'
        ? (token[0] == '>' ? "<" : ">") + token[1..] : token;
    public static int Index(string token) => Enumerable.Range(0, All.Count).First(i => All[i].Token == token);
    public static string FromHook(int number) => All.Single(t => t.HookNumber == number).Token;
    public static int ToHook(string token) => All.Single(t => t.Token == token).HookNumber;
}
