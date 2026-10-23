using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace TikzEdt.Modern;

internal sealed record LaboDefinition(string Id, string Category, string Title, string Command, double Width, double Height, string? Glass = null);
internal sealed record LaboItem(string Type, double X, double Y, Dictionary<string, string> Options)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public LaboBox? Ink { get; init; }
}
internal sealed partial class LaboScene
{
    public List<LaboItem> Items { get; set; } = [];
    public static IReadOnlyList<LaboDefinition> Catalog { get; } = new[]
    {
        new LaboDefinition("tube", "Glassware", "Test tube", "pstTubeEssais", 3, 4, "tube"),
        new("beaker", "Glassware", "Beaker", "pstTubeEssais", 1.5, 2.8, "becher"),
        new("flask", "Glassware", "Round-bottom flask", "pstTubeEssais", 3, 4, "ballon"),
        new("erlen", "Glassware", "Erlenmeyer flask", "pstTubeEssais", 3, 4, "erlen"),
        new("bottle", "Glassware", "Reagent bottle", "pstTubeEssais", 3, 4, "flacon"),
        new("volumetric", "Glassware", "Volumetric flask", "pstTubeEssais", 3, 4, "fioleJauge"),
        new("cylinder", "Measuring / transfer", "Graduated cylinder", "pstEprouvette", 4, 6),
        new("pipette", "Measuring / transfer", "Pipette", "pstpipette", 2, 7),
        new("funnel", "Measuring / transfer", "Funnel + receiver", "pstEntonnoir", 4, 7),
        new("titration", "Assemblies", "Titration", "pstDosage", 8, 16, "becher"),
        new("heating", "Assemblies", "Heating flask", "pstChauffageBallon", 6, 11, "ballon"),
        new("reflux", "Assemblies", "Reflux condenser", "pstBallon", 4, 12, "ballon"),
        new("gas", "Assemblies", "Gas collection", "pstChauffageBallon", 15, 16, "erlen"),
        new("distillation", "Assemblies", "Distillation", "pstDistillation", 12, 17),
        new("separation", "Assemblies", "Separating funnel + stand", "pstSeparateFunnel", 9, 23),
        new("separation-only", "Measuring / transfer", "Separating funnel", "pstSeparateFunnel", 6, 19),
        new("heated-tube", "Assemblies", "Heated test tube", "pstChauffageTube", 13, 9, "tube"),
        new("ballon", "Glassware", "Flask with attachments", "pstBallon", 4, 12, "ballon"),
        new("stand", "Basic parts", "Support stand", "raw:pstSupport", 40, 45),
        new("column", "Basic parts", "Fractionating column", "raw:pscolon", 40, 45),
        new("condenser", "Basic parts", "Straight condenser", "raw:psrefri", 40, 45),
        new("burner", "Basic parts", "Bunsen burner", "raw:pst@BecBunsen", 40, 45),
        new("stopper", "Basic parts", "Stopper", "raw:pst@TubeEssais@Bouchon", 40, 45),
        new("clamp", "Basic parts", "Wooden clamp", "raw:pst@TubeEssais@Pince", 40, 45),
        new("straight-tube", "Basic parts", "Straight glass tube", "raw:pst@TubeEssais@Droit", 40, 45),
        new("bent-tube", "Basic parts", "Bent glass tube", "raw:pst@TubeEssais@Coude", 40, 45),
        new("u-tube", "Basic parts", "U glass tube", "raw:pst@TubeEssais@CoudeU", 40, 45),
        new("long-u-tube", "Basic parts", "Extended U glass tube", "raw:pst@TubeEssais@CoudeUB", 40, 45),
        new("allihn", "Basic parts", "Allihn condenser", "raw:pst@Refrigerant@Boulles", 40, 45),
        new("water-bath", "Basic parts", "Gas collection water bath", "raw:pst@Cristallisoir", 40, 45),
        new("burette-only", "Measuring / transfer", "Standalone burette", "custom:burette", 4, 13),
        new("ph-meter", "Basic parts", "pH meter", "raw:pst@dosage@pHmetre", 40, 45),
        new("hotplate", "Basic parts", "Magnetic stirrer / hotplate", "raw:pst@dosage@support", 40, 45),
        new("stir-bar", "Basic parts", "Magnetic stirring bar", "raw:pst@dosage@aimant", 40, 45),
        new("burner-grid", "Basic parts", "Burner with gauze", "raw:pst@BecBunsenGrille", 40, 45),
        new("mantle", "Basic parts", "Heating mantle", "raw:pst@ChauffeBallon", 40, 45),
        new("inverted-tube", "Basic parts", "Inverted gas collection tube", "raw:pst@TubeRenverse", 40, 45),
        new("tube-support", "Basic parts", "Gas tube support", "raw:pst@ReposeTube", 40, 45)
    };
    internal static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    public LaboScene Clone() => new() { Items = Items.Select(i => i with { Options = new(i.Options) }).ToList(), Tubes = Tubes.Select(t => t with { }).ToList() };
    public static LaboDefinition Definition(LaboItem item) => Catalog.Single(d => d.Id == item.Type);
    public static LaboItem Create(string id, double x, double y)
    {
        var options = new Dictionary<string, string> { ["niveauLiquide1"] = "50" };
        if (id == "reflux") options["refrigerantBoulles"] = "true";
        if (id == "gas") { options["recuperationGaz"] = "true"; options["tubeRecourbe"] = "true"; }
        if (id is "separation" or "separation-only") { options["niveauLiquide1"] = "11"; options["niveauLiquide2"] = "12"; options["OpenTap"] = "false"; }
        return new(id, x, y, options);
    }
    public static (double Width, double Height) Size(LaboItem item)
    {
        var definition = Definition(item);
        // Use a stable generous frame across option changes (tilt, neck attachments, meters).
        if (definition.Category == "Glassware") return (10, 12);
        return (definition.Width + 8, definition.Height + 6);
    }
    private static string LegacyObjectCode(LaboItem item)
    {
        Validate(item); var definition = Definition(item);
        var options = new Dictionary<string, string>(item.Options);
        if (definition.Glass is not null) options["glassType"] = definition.Glass;
        var args = string.Join(",", options.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
        var command = "\\" + definition.Command + "[" + args + "]";
        if (item.Type == "distillation") command += "(-4,-10)(8,7)";
        if (item.Type == "separation") command = "\\begin{pspicture}(-3,-6)(6,17)\\pstSupport " + command + "\\end{pspicture}";
        return command;
    }
    public static void Validate(LaboItem item)
    {
        _ = Definition(item);
        if (!double.IsFinite(item.X) || !double.IsFinite(item.Y) || Math.Abs(item.X) > 500 || Math.Abs(item.Y) > 500) throw new FormatException("Apparatus coordinates must be within ±500 units.");
        if(item.Ink is { } ink && (new[]{ink.Left,ink.Bottom,ink.Width,ink.Height}.Any(v=>!double.IsFinite(v)) || ink.Width<=0 || ink.Height<=0 || ink.Left<0 || ink.Bottom<0 || ink.Left+ink.Width>CaptureSize+.01 || ink.Bottom+ink.Height>CaptureSize+.01)) throw new FormatException("Invalid vector ink bounds.");
        foreach (var (key, value) in item.Options) LaboProperties.Validate(item,key,value);
    }
    internal static string BuildLegacyDocument(IEnumerable<LaboItem> items, bool metadata = true)
    {
        var list = items.ToList(); if (list.Count > 100) throw new FormatException("This initial editor supports up to 100 apparatus objects per drawing.");
        foreach (var item in list) Validate(item);
        var left = list.Count == 0 ? 0 : list.Min(i => i.X) - 1;
        var bottom = list.Count == 0 ? 0 : list.Min(i => i.Y) - 1;
        var right = list.Count == 0 ? 10 : list.Max(i => i.X + Size(i).Width) + 1;
        var top = list.Count == 0 ? 10 : list.Max(i => i.Y + Size(i).Height) + 1;
        var objects = list.Select(item =>
        {
            var size = Size(item);
            return $"\\rput[bl]({Number(item.X)},{Number(item.Y)}){{\\begin{{pspicture}}(0,0)({Number(size.Width)},{Number(size.Height)})\\rput[bl](3,3){{{LegacyObjectCode(item)}}}\\end{{pspicture}}}}";
        });
        // This initial catalog uses opaque artwork, not extended transparency states.
        // Avoid enabling experimental tagging/latex-lab just to render these figures.
        var body = "\\documentclass[border=0pt]{standalone}\n\\usepackage{pst-labo}\n\\begin{document}\n\\psset{unit=0.5cm}\n" +
            $"\\begin{{pspicture}}({Number(left)},{Number(bottom)})({Number(right)},{Number(top)})\n" + string.Join("\n", objects) + "\n\\end{pspicture}\n\\end{document}\n";
        if (!metadata) return body;
        var state = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new LaboScene { Items = list })));
        return "% !TIKZEDT LABO1 " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))) + " " + state + "\n" + body;
    }
    private static LaboScene? ReadLegacyDocument(string source)
    {
        if (!source.StartsWith("% !TIKZEDT LABO1 ", StringComparison.Ordinal)) return null;
        var newline = source.IndexOf('\n'); if (newline < 0) throw new FormatException("Incomplete laboratory document.");
        var header = source[..newline].TrimEnd('\r').Split(' '); var body = source[(newline + 1)..].Replace("\r\n", "\n", StringComparison.Ordinal);
        if (header.Length != 5 || header[3] != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))) throw new FormatException("Laboratory source was edited manually. Visual import is paused to avoid overwriting those edits.");
        var scene = JsonSerializer.Deserialize<LaboScene>(Convert.FromBase64String(header[4])) ?? throw new FormatException("Invalid laboratory state.");
        if (scene.Items.Count > 100) throw new FormatException("Too many apparatus objects.");
        foreach (var item in scene.Items) Validate(item);
        var expected = BuildLegacyDocument(scene.Items,false);
        if (expected != body) throw new FormatException("Laboratory metadata and drawing disagree.");
        return scene;
    }
}
