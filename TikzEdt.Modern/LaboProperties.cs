using System.Globalization;
using System.Text.RegularExpressions;

namespace TikzEdt.Modern;

internal sealed record LaboProperty(string Key,string Label,string Group,string Kind,string Default="",double Min=0,double Max=100,string[]? Choices=null,string Note="");
internal static class LaboProperties
{
    public static readonly string[] ExclusiveAttachments=["tubeDroit","tubeCoude","tubeCoudeU","tubeCoudeUB","tubeRecourbe","tubeRecourbeCourt","doubletube","refrigerantBoulles"];
    public static readonly string[] Styles = ["aspectLiquide1","aspectLiquide2","aspectLiquide3","AqueoPhase","OrganicPhase","Champagne","Sang","Cobalt","Huile","Vinaigre","Diffusion","DiffusionMelange2","DiffusionBleue","rayuresJaunes","Custom"];
    public static IReadOnlyList<LaboProperty> All { get; } = Create();
    private static IReadOnlyList<LaboProperty> Create()
    {
        var p = new List<LaboProperty>();
        void Switch(string key,string label,string group,string value="false",string note="") => p.Add(new(key,label,group,"Boolean",value,Note:note));
        p.Add(new("glassType","Glass type","Vessel","Choice","tube",Choices:["tube","ballon","becher","erlen","flacon","fioleJauge","verre"]));
        p.Add(new("tubePenche","Tilt (liquid stays horizontal)","Vessel","Number","0",-65,65));
        Switch("bouchon","Stopper","Vessel"); Switch("pince","Wooden clamp","Vessel");
        Switch("etiquette","Show vessel label","Labels"); p.Add(new("Numero","Vessel label (TeX)","Labels","Text"));
        for(var i=1;i<=3;i++)
        {
            p.Add(new("niveauLiquide"+i,"Level", "Liquid "+i,"Number",i==1?"50":"0"));
            p.Add(new("aspectLiquide"+i,"Appearance", "Liquid "+i,"Style","aspectLiquide"+i,Choices:Styles));
            p.Add(new("$color"+i,"Custom colour (#RRGGBB)","Liquid "+i,"Html",i==1?"00BFFF":i==2?"FFFF00":"FF00FF"));
        }
        foreach(var pair in new[]{("tubeDroit","Straight tube"),("tubeCoude","Right-angle tube"),("tubeCoudeU","U-shaped tube"),("tubeCoudeUB","Extended U tube"),("tubeRecourbe","Curved gas delivery tube"),("tubeRecourbeCourt","Short gas delivery tube"),("doubletube","Double tube / tap"),("refrigerantBoulles","Allihn condenser")}) Switch(pair.Item1,pair.Item2,"Attachments");
        Switch("tubeDegagementDroit","Straight outlet (upstream reserved)","Attachments",note:"This key is declared but unused in the current package implementation.");
        Switch("barbotage","Bubbling receiver","Heating / gas"); Switch("recuperationGaz","Collect gas","Heating / gas"); Switch("tubeSeul","Compact picture frame","Heating / gas"); Switch("becBunsen","Bunsen burner","Heating / gas","true");
        p.Add(new("substance","Suspended material / bubbles","Contents","Substance",@"\relax",Choices:[@"\relax",@"\pstBullesChampagne",@"\pstBilles",@"\pstBULLES{white}",@"\pstFilaments{red}"]));
        p.Add(new("solide","Solid material","Contents","Solid",@"\relax",Choices:[@"\relax",@"\pstTournureCuivre",@"\pstClouFer",@"\pstGrenailleZinc"]));
        Switch("burette","Burette","Titration","true"); Switch("phmetre","pH meter","Titration"); Switch("agitateurMagnetique","Magnetic stirrer","Titration","true");
        p.Add(new("niveauReactifBurette","Burette level (mL)","Titration","Number","20",0,25));
        p.Add(new("couleurReactifBurette","Burette liquid colour","Titration","Color","OrangeTresPale"));
        p.Add(new("reactifBurette","Burette reagent label (TeX)","Titration","Text")); p.Add(new("reactifBecher","Receiver reagent label (TeX)","Titration","Text"));
        p.Add(new("niveauReactif","Pipette reagent level","Pipette","Number","2",0,10)); p.Add(new("couleurReactif","Pipette reagent colour","Pipette","Color","OrangePale"));
        p.Add(new("AspectMelange","Mixture appearance","Distillation","Style","DiffusionBleue",Choices:Styles.Where(s=>s!="Custom").ToArray())); p.Add(new("CouleurDistillat","Distillate colour","Distillation","Color","yellow"));
        Switch("OpenTap","Open tap","Separating funnel");
        p.Add(new("linewidth","Line width (cm)","Drawing","Decimal","0.04",.005,.5)); p.Add(new("linecolor","Line colour","Drawing","Color","black")); p.Add(new("fillcolor","Fill colour","Drawing","Color","white"));
        return p;
    }
    public static bool Applies(LaboItem item,LaboProperty p)
    {
        var command=LaboScene.Definition(item).Command;
        if(p.Key is "linewidth" or "linecolor" or "fillcolor") return true;
        if(p.Group.StartsWith("Liquid",StringComparison.Ordinal)) return command is "pstTubeEssais" or "pstBallon" or "pstChauffageBallon" or "pstChauffageTube" or "pstEntonnoir" or "pstDosage" or "pstEprouvette" || command=="pstSeparateFunnel" && p.Group!="Liquid 3";
        return p.Group switch
        {
            "Vessel" or "Labels" or "Contents" => command is "pstTubeEssais" or "pstBallon" or "pstChauffageBallon" or "pstChauffageTube" or "pstEntonnoir" or "pstDosage" || command=="pstEprouvette" && p.Key is "tubePenche" or "substance" || command=="pstSeparateFunnel" && p.Key=="bouchon",
            "Attachments" => p.Key!="tubeDegagementDroit" && (p.Key is "tubeDroit" or "tubeCoude" or "tubeCoudeU" ? command is "pstTubeEssais" or "pstBallon" or "pstChauffageBallon" or "pstChauffageTube" or "pstEntonnoir" or "pstDosage" : command is "pstBallon" or "pstChauffageBallon" or "pstDosage"),
            "Heating / gas" => command is "pstChauffageBallon" or "pstChauffageTube",
            "Titration" => command=="pstDosage" || command=="custom:burette" && p.Key is "niveauReactifBurette" or "couleurReactifBurette" or "reactifBurette", "Pipette" => command is "pstpipette" or "pstBigPipette",
            "Distillation" => command=="pstDistillation", "Separating funnel" => command=="pstSeparateFunnel", _=>false
        };
    }
    public static string Default(LaboItem item,LaboProperty p)
    {
        if(p.Key=="glassType") return LaboScene.Definition(item).Glass??"tube";
        if(item.Type is "separation" or "separation-only") return p.Key switch {"niveauLiquide1"=>"11","niveauLiquide2"=>"12","aspectLiquide1"=>"AqueoPhase","aspectLiquide2"=>"OrganicPhase",_=>p.Default};
        return p.Default;
    }
    public static void Validate(LaboItem item,string key,string value)
    {
        var p=All.SingleOrDefault(p=>p.Key==key)??throw new FormatException("Unsupported apparatus option: "+key);
        if(value.Length>2000) throw new FormatException("Property text is too long.");
        var valid=p.Kind switch
        {
            "Number" => int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var number) && number>=p.Min && number<=(item.Type is "separation" or "separation-only" && key.StartsWith("niveauLiquide",StringComparison.Ordinal)?16:p.Max),
            "Decimal" => double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var number) && double.IsFinite(number) && number>=p.Min && number<=p.Max,
            "Boolean" => value is "true" or "false", "Choice"=>p.Choices!.Contains(value), "Style"=>p.Choices!.Contains(value),
            "Html"=>Regex.IsMatch(value.TrimStart('#'),"^[A-Fa-f0-9]{6}$"),
            "Color"=>Regex.IsMatch(value,"^(?:#[A-Fa-f0-9]{6}|[A-Za-z][A-Za-z0-9]*(?:!(?:100|[0-9]{1,2})(?:![A-Za-z]+)?)?)$"),
            "Substance" or "Solid"=>ValidMaterial(value,p.Kind=="Solid"),
            "Text"=>BalancedText(value), _=>false
        };
        if(!valid) throw new FormatException("Invalid value for "+p.Label+" ("+key+").");
    }
    private static bool ValidMaterial(string value,bool solid)
    {
        if(value==@"\relax") return true;
        var m=Regex.Match(value,@"^\\(?<name>pst[A-Za-z]+)(?:\[(?<n>\d{1,2})\])?(?:\{(?<c>[A-Za-z]+)\})?$");
        if(!m.Success || m.Groups["n"].Success && int.Parse(m.Groups["n"].Value)>80) return false;
        var name=m.Groups["name"].Value; var color=m.Groups["c"].Success;
        return solid ? name is "pstTournureCuivre" or "pstClouFer" or "pstGrenailleZinc" && !color
            : name is "pstBULLES" or "pstFilaments" ? color : name is "pstBullesChampagne" or "pstBilles" && !color;
    }
    private static bool BalancedText(string text)
    {
        if(Regex.IsMatch(text,@"\\(?:input|include|write|openout|read|catcode|csname|directlua|special)\b")) return false;
        var depth=0; for(var i=0;i<text.Length;i++) { if(text[i]=='\\') { i++; continue; } if(text[i]=='{' ) depth++; if(text[i]=='}' && --depth<0) return false; }
        return depth==0 && !text.Contains('%');
    }
}
