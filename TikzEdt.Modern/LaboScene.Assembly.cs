using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace TikzEdt.Modern;

internal sealed record LaboBox(double Left,double Bottom,double Width,double Height);
internal sealed record LaboEnd(string? ItemId,string Port,double X,double Y);
internal sealed record LaboTube(LaboEnd Start,LaboEnd End,string Route="Elbow",double Diameter=.15,double Bend=.3)
{
    public string Id { get; init; }=Guid.NewGuid().ToString("N");
    public double? ControlX { get; init; }
    public double? ControlY { get; init; }
}
internal sealed partial class LaboScene
{
    internal const double CaptureSize=80, CaptureOrigin=25;
    public List<LaboTube> Tubes { get; set; }=[];
    public static string ObjectCode(LaboItem item)
    {
        Validate(item); var definition=Definition(item); var options=new Dictionary<string,string>(item.Options);
        foreach(var key in options.Keys.Where(k=>k.StartsWith('$')).ToArray()) options.Remove(key);
        if(definition.Glass is not null) options.TryAdd("glassType",definition.Glass);
        var styles=new StringBuilder();
        foreach(var key in options.Keys.Where(k=>LaboProperties.All.Any(p=>p.Key==k && p.Kind=="Color") && options[k].StartsWith('#')).ToArray())
        {var name="LaboColour"+key;styles.Append($"\\definecolor{{{name}}}{{HTML}}{{{options[key][1..]}}}");options[key]=name;}
        for(var i=1;i<=3;i++)
        {
            var key="aspectLiquide"+i;
            if(options.GetValueOrDefault(key)=="Custom")
            {
                var color=item.Options.GetValueOrDefault("$color"+i,i==1?"00BFFF":i==2?"FFFF00":"FF00FF").TrimStart('#');
                styles.Append($"\\definecolor{{LaboColour{i}}}{{HTML}}{{{color}}}\\newpsstyle{{LaboLiquid{i}}}{{linestyle=none,fillstyle=solid,fillcolor=LaboColour{i}}}"); options[key]="LaboLiquid"+i;
            }
        }
        if(definition.Command=="pstSeparateFunnel")
        {
            if(options.TryGetValue("aspectLiquide1",out var style1) && style1!="AqueoPhase") styles.Append($"\\newpsstyle{{AqueoPhase}}{{style={style1}}}");
            if(options.TryGetValue("aspectLiquide2",out var style2) && style2!="OrganicPhase") styles.Append($"\\newpsstyle{{OrganicPhase}}{{style={style2}}}");
        }
        var args=string.Join(",",options.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"={"+p.Value+"}"));
        string command;
        if(definition.Command=="custom:burette")
        {
            var volume=int.Parse(options.GetValueOrDefault("niveauReactifBurette","20"),System.Globalization.CultureInfo.InvariantCulture);
            var color=options.GetValueOrDefault("couleurReactifBurette","OrangeTresPale");
            command="\\begin{pspicture}(-2,-1)(2,12)\\psclip{\\pscustom[linestyle=none]{\\csname pst@burette@corps\\endcsname}}"+
                $"\\psframe*[linecolor={color}](-1,-1)(1,{Number(volume*.32+2.68)})"+"\\endpsclip\\csname pst@burette@corps\\endcsname\\csname pst@burette@robinet\\endcsname\\csname pst@burette@graduation\\endcsname";
            if(options.TryGetValue("reactifBurette",out var label)&&label.Length>0)command+="\\rput[l](2,6){"+label+"}";
            command+="\\end{pspicture}";
        }
        else if(definition.Command.StartsWith("raw:",StringComparison.Ordinal))
            command="\\begin{pspicture}(-15,-20)(25,25)\\csname "+definition.Command[4..]+"\\endcsname\\end{pspicture}";
        else
        {
            command="\\"+definition.Command+"["+args+"]";
            if(item.Type=="distillation") command+="(-4,-10)(8,7)";
            if(item.Type=="separation") command="\\begin{pspicture}(-3,-6)(6,17)\\pstSupport "+command+"\\end{pspicture}";
            if(item.Type=="separation-only") command="\\begin{pspicture}(-3,-2)(3,17)"+command+"\\end{pspicture}";
        }
        var defaults=string.Join(",",LaboProperties.All.Where(p=>!p.Key.StartsWith('$')&&p.Group!="Drawing").Select(p=>p.Key+"={"+LaboProperties.Default(item,p)+"}"));
        return "\\begingroup\\psset{"+defaults+"}"+styles+"\\psset{"+args+"}"+command+"\\endgroup";
    }
    public static string ObjectSource(LaboItem item) => Preamble+
        $"\\begin{{pspicture}}(0,0)({Number(CaptureSize)},{Number(CaptureSize)})\\rput[bl]({Number(CaptureOrigin)},{Number(CaptureOrigin)}){{{ObjectCode(item)}}}\\end{{pspicture}}\n\\end{{document}}\n";
    private const string Preamble="\\documentclass[border=0pt]{standalone}\n\\usepackage{pst-labo}\n\\begin{document}\n\\psset{unit=0.5cm}\n";
    public static LaboBox FigureBox(LaboItem item) => item.Ink is { } ink ? new(item.X,item.Y,ink.Width,ink.Height) : new(item.X,item.Y,CaptureSize,CaptureSize);
    public Point Resolve(LaboEnd end)
    {
        if(end.ItemId is null) return new(end.X,end.Y);
        var item=Items.SingleOrDefault(i=>i.Id==end.ItemId)??throw new FormatException("A tube refers to missing apparatus.");
        var box=FigureBox(item);
        var point=end.Port switch
        {
            "top"=>new Point(box.Width/2,box.Height),"bottom"=>new Point(box.Width/2,0),"left"=>new Point(0,box.Height/2),"right"=>new Point(box.Width,box.Height/2),"custom"=>new Point(),
            _=>throw new FormatException("Unknown attachment point: "+end.Port)
        };
        return new(item.X+point.X+end.X,item.Y+point.Y+end.Y);
    }
    public Point[] TubePoints(LaboTube tube)
    {
        var a=Resolve(tube.Start); var b=Resolve(tube.End); var x=tube.ControlX??(a.X+b.X)/2; var y=tube.ControlY??(a.Y+b.Y)/2;
        var bendY=y-(a.Y+b.Y)/2;
        return tube.Route=="Straight" ? [a,b] : tube.Route=="Curve" ? [a,new(x,a.Y+bendY),new(x,b.Y+bendY),b] : [a,new(x,a.Y),new(x,y),new(b.X,y),b];
    }
    public string ToDocument(bool metadata=true)
    {
        if(Items.Count>100 || Tubes.Count>200) throw new FormatException("Use at most 100 apparatus and 200 tubes per drawing.");
        if(Items.Select(i=>i.Id).Distinct().Count()!=Items.Count) throw new FormatException("Duplicate apparatus identifiers.");
        foreach(var item in Items) Validate(item);
        var bounds=Rect.Empty;
        foreach(var item in Items) {var box=FigureBox(item);bounds.Union(new Rect(box.Left,box.Bottom,box.Width,box.Height));}
        var tubing=new StringBuilder();
        foreach(var tube in Tubes)
        {
            if(tube.Route is not ("Straight" or "Elbow" or "Curve") || !double.IsFinite(tube.Diameter) || tube.Diameter is < .03 or > 1 || !double.IsFinite(tube.Bend) || tube.Bend is <0 or >5) throw new FormatException("Invalid tubing properties.");
            var points=TubePoints(tube); foreach(var p in points) {if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||Math.Abs(p.X)>1000||Math.Abs(p.Y)>1000) throw new FormatException("Invalid tube coordinates."); bounds.Union(p);}
            void Path(string color,double width)
            {
                tubing.Append($"\\pscustom[linecolor={color},linewidth={Number(width*.5)}cm,linejoin=1,linecap=1]{{");
                var first=true;
                string Coordinate(Point p)=>$"({Number(p.X)},{Number(p.Y)})";
                foreach(var step in LaboTubePath.Steps(this,tube))
                {
                    if(first){tubing.Append("\\moveto"+Coordinate(step.End));first=false;}
                    else if(step.C1 is {} c1 && step.C2 is {} c2)tubing.Append("\\curveto"+Coordinate(c1)+Coordinate(c2)+Coordinate(step.End));
                    else tubing.Append("\\lineto"+Coordinate(step.End));
                }
                tubing.Append("}\n");
            }
            Path("black",tube.Diameter); Path("white",Math.Max(.005,tube.Diameter-.04));
        }
        if(bounds.IsEmpty) bounds=new Rect(0,0,10,10);
        var border=Tubes.Count==0?.03:Math.Max(.03,Tubes.Max(t=>t.Diameter)/2); bounds.Inflate(border,border);
        var objects=Items.Select(item=>
        {
            var ink=item.Ink;
            return $"\\rput[bl]({Number(item.X-(ink?.Left??0))},{Number(item.Y-(ink?.Bottom??0))}){{\\begin{{pspicture}}(0,0)({Number(CaptureSize)},{Number(CaptureSize)})\\rput[bl]({Number(CaptureOrigin)},{Number(CaptureOrigin)}){{{ObjectCode(item)}}}\\end{{pspicture}}}}";
        });
        var body=Preamble+$"\\begin{{pspicture}}({Number(bounds.Left)},{Number(bounds.Top)})({Number(bounds.Right)},{Number(bounds.Bottom)})\n"+string.Join("\n",objects)+"\n"+tubing+"\\end{pspicture}\n\\end{document}\n";
        if(!metadata) return body;
        var state=Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)));
        return "% !TIKZEDT LABO2 "+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))+" "+state+"\n"+body;
    }
    public static string BuildDocument(IEnumerable<LaboItem> items,bool metadata=true) => new LaboScene {Items=items.ToList()}.ToDocument(metadata);
    public static LaboScene? ReadDocument(string source)
    {
        if(source.StartsWith("% !TIKZEDT LABO1 ",StringComparison.Ordinal)) return ReadLegacyDocument(source);
        if(!source.StartsWith("% !TIKZEDT LABO2 ",StringComparison.Ordinal)) return null;
        source=source.Replace("\r\n","\n",StringComparison.Ordinal); var newline=source.IndexOf('\n'); if(newline<0) throw new FormatException("Incomplete laboratory source.");
        var parts=source[..newline].Split(' '); var body=source[(newline+1)..];
        if(parts.Length!=5 || parts[3]!=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))) throw new FormatException("The TeX drawing was manually changed. Source is preserved; visual import will not overwrite it.");
        var scene=JsonSerializer.Deserialize<LaboScene>(Convert.FromBase64String(parts[4]))??throw new FormatException("Missing laboratory state.");
        if(scene.ToDocument(false)!=body) throw new FormatException("Laboratory metadata and source disagree.");
        return scene;
    }
}
