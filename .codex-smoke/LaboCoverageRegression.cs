using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TikzEdt.Modern;

internal static class LaboCoverageRegression
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static object Field(object obj,string name)=>obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(obj)!;
    public static void Run()
    {
        var expected="burette couleurReactifBurette niveauReactifBurette reactifBurette reactifBecher phmetre couleurReactif niveauReactif CouleurDistillat AspectMelange bouchon tubeDroit tubeCoude tubeCoudeU tubeCoudeUB tubeRecourbe tubeRecourbeCourt doubletube refrigerantBoulles pince barbotage recuperationGaz tubeSeul tubeDegagementDroit becBunsen Numero etiquette agitateurMagnetique tubePenche niveauLiquide1 niveauLiquide2 niveauLiquide3 aspectLiquide1 aspectLiquide2 aspectLiquide3 substance solide glassType OpenTap".Split(' ').Order().ToArray();
        var actual=LaboProperties.All.Where(p=>!p.Key.StartsWith('$') && p.Group!="Drawing").Select(p=>p.Key).Order().ToArray();Check(expected.SequenceEqual(actual),"Native package key coverage is incomplete.");
        var item=LaboScene.Create("separation",0,0) with {Ink=new(25,25,5,17)};item.Options["aspectLiquide1"]="Custom";item.Options["$color1"]="123456";item.Options["aspectLiquide2"]="Custom";item.Options["$color2"]="FEDCBA";
        var source=LaboScene.ObjectCode(item);Check(source.Contains("AqueoPhase}{style=LaboLiquid1}") && source.Contains("OrganicPhase}{style=LaboLiquid2}") && source.Contains("123456") && source.Contains("FEDCBA"),"Separating funnel phase styles are not independent/local.");
        var scene=new LaboScene {Items=[item,LaboScene.Create("tube",9,0) with {Ink=new(25,25,2,4)}]};
        var tube=new LaboTube(new(item.Id,"top",0,0),new(scene.Items[1].Id,"top",0,0),"Elbow");scene.Tubes.Add(tube);
        var start=scene.Resolve(tube.Start);scene.Items[0]=item with {X=3,Y=2};Check((scene.Resolve(tube.Start)-start-new Vector(3,2)).Length<1e-9,"Attached tubing did not follow apparatus.");
        var restored=LaboScene.ReadDocument(scene.ToDocument())!;Check(restored.Tubes.Count==1 && restored.ToDocument()==scene.ToDocument(),"Tubing/source round trip failed.");
        Check(LaboTubePath.Steps(scene,tube).Any(s=>s.C1 is not null),"Elbow bend radius is not used by the shared path geometry.");
        var original=LaboScene.Create("tube",0,0);var legacy=LaboScene.BuildLegacyDocument(new[]{original});Check(LaboScene.ReadDocument(legacy)!.Items.Count==1,"Previous laboratory documents no longer import.");
        Exception? failure=null;var thread=new Thread(()=>{try{Ui();}catch(Exception e){failure=e;}});thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw new Exception("Laboratory UI regression failed",failure);
        Console.WriteLine($"All {expected.Length} package keys, separate phase styles, ink-only bounds, source migration, attached tubing, duplication and undo passed.");
    }
    static void Ui()
    {
        var app=new App();app.InitializeComponent();
        var root=new DrawingGroup();root.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,1000,1000))));root.Children.Add(new GeometryDrawing(Brushes.Red,new Pen(Brushes.Black,2),new RectangleGeometry(new Rect(20,30,40,50))));root.Freeze();
        var ink=VectorInkBounds.Measure(root);Check(ink==new Rect(19,29,42,52),"Transparent compile frame contaminated selection bounds: "+ink);
        var image=new DrawingImage(root);image.Freeze();var crop=VectorInkBounds.Crop(image,ink);Check(Math.Abs(crop.Width-42)<1e-8&&Math.Abs(crop.Height-52)<1e-8,"Cropping changed vector size.");
        var a=LaboScene.Create("separation",0,0) with {Ink=new(25,25,5,17)};var b=LaboScene.Create("tube",9,0) with {Ink=new(25,25,2,4)};
        var editor=new LaboEditorWindow(new LaboScene {Items=[a,b]});
        var selection=(HashSet<int>)Field(editor,"_selected");selection.Add(0);editor.GetType().GetMethod("Properties",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);
        var properties=(StackPanel)Field(editor,"_properties");Check(properties.Children.OfType<Expander>().Any(e=>Equals(e.Header,"Lower / aqueous phase")) && properties.Children.OfType<Expander>().Any(e=>Equals(e.Header,"Upper / organic phase")),"Both liquid phase property groups are not accessible.");
        editor.SetProperty(a.Id,"aspectLiquide2","Custom");editor.SetProperty(a.Id,"$color2","FF0000");
        editor.AddTube(new(a.Id,"top",0,0),new(b.Id,"top",0,0),"Curve");selection.UnionWith(new[]{0,1});
        editor.GetType().GetMethod("Duplicate",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);
        var scene=LaboScene.ReadDocument(editor.Result)!;Check(scene.Items.Count==4&&scene.Items.Select(i=>i.Id).Distinct().Count()==4&&scene.Tubes.Count==2,"Duplicating connected apparatus lost identifiers/connections.");
        editor.GetType().GetMethod("Undo",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);Check(LaboScene.ReadDocument(editor.Result)!.Items.Count==2,"Connected duplicate is not one undo step.");
        var content=(FrameworkElement)editor.Content;content.Measure(new Size(1200,760));content.Arrange(new Rect(0,0,1200,760));content.UpdateLayout();
        var current=LaboScene.ReadDocument(editor.Result)!;var link=current.Tubes.Single();((HashSet<string>)Field(editor,"_selectedTubes")).Add(link.Id);
        var viewport=(DiagramViewport)Field(editor,"_view");const double unit=.5*96/2.54;
        var point=current.Resolve(link.Start);var screen=viewport.Origin+new Vector(point.X*unit*viewport.Scale,-point.Y*unit*viewport.Scale);
        var before=editor.Result;
        editor.GetType().GetMethod("TubeDown",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,[screen]);
        editor.GetType().GetMethod("TubeMove",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,[screen+new Vector(-90,-70),true]);
        editor.GetType().GetMethod("TubeUp",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);
        Check(LaboScene.ReadDocument(editor.Result)!.Tubes.Single().Start.ItemId is null,"Dragging a tube endpoint into empty space did not detach it.");
        editor.GetType().GetMethod("Undo",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);Check(editor.Result==before,"Tube endpoint drag was not one undo step.");
        var inside=viewport.Origin+new Vector(unit,-unit);Check(editor.EndpointAt(inside).ItemId==a.Id,"Picking an apparatus-local attachment point failed.");
        editor.Close();app.Shutdown();
    }
    public static async Task IntegrationAsync()
    {
        var variants=LaboScene.Catalog.Select(d=>LaboScene.Create(d.Id,0,0)).ToList();
        foreach(var p in LaboProperties.All.Where(p=>!p.Key.StartsWith('$')&&p.Group!="Drawing"))
        {
            var item=LaboScene.Catalog.Select(d=>LaboScene.Create(d.Id,0,0)).FirstOrDefault(i=>LaboProperties.Applies(i,p))??LaboScene.Create("tube",0,0);
            item.Options[p.Key]=p.Kind switch {"Boolean"=>"true","Number"=>p.Key=="tubePenche"?"25":p.Key=="niveauReactif"?"5":p.Key=="niveauReactifBurette"?"15":"10","Text"=>"Cu$^{2+}$","Color"=>"blue","Style"=>"Sang","Choice"=>"verre","Substance"=>@"\pstBullesChampagne[5]","Solid"=>@"\pstTournureCuivre[5]",_=>p.Default};variants.Add(item);
        }
        variants=variants.Select((i,n)=>i with {X=-300+(n%8)*85,Y=-300+(n/8)*85}).ToList();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(300));var settings=new UserSettings();
        var result=await TexVectorRenderer.RenderAsync(LaboScene.BuildDocument(variants),"lualatex",null,null,timeout.Token,settings);
        Check(result.Image is not null,"Catalogue/property compilation failed: "+result.Log);
        Directory.CreateDirectory(".codex-smoke/labo-coverage");File.Copy(result.SvgPath,".codex-smoke/labo-coverage/catalog.svg",true);
        var first=LaboScene.Create("separation",0,0);first.Options["aspectLiquide1"]="Custom";first.Options["$color1"]="FF0000";first.Options["aspectLiquide2"]="Custom";first.Options["$color2"]="0000FF";
        var second=LaboScene.Create("tube",10,0);
        var assets=new Dictionary<string,DrawingImage>();var items=new List<LaboItem>();var sizes=new Dictionary<string,LaboBox>();
        foreach(var item in new[]{first,second})
        {
            var objectResult=await TexVectorRenderer.RenderAsync(LaboScene.ObjectSource(item),"lualatex",null,null,timeout.Token,settings);Check(objectResult.Image is not null,objectResult.Log);
            var ink=VectorInkBounds.Measure(objectResult.Image!.Drawing);const double unit=.5*96/2.54;
            var box=new LaboBox(ink.Left/unit,(objectResult.Image.Height-ink.Bottom)/unit,ink.Width/unit,ink.Height/unit);
            Check(box.Width<15&&box.Height<30,"Ink measurement retained the padded compile frame.");items.Add(item with {Ink=box});assets[LaboScene.ObjectSource(item)]=VectorInkBounds.Crop(objectResult.Image,ink);sizes[item.Id]=box;
        }
        var scene=new LaboScene {Items=items};scene.Tubes.Add(new(new(first.Id,"top",0,0),new(second.Id,"top",0,0),"Elbow") {ControlY=24});
        scene.Tubes.Add(new(new(null,"custom",2,-2),new(null,"custom",12,-2),"Curve") {ControlY=-5});
        var assembled=await TexVectorRenderer.RenderAsync(scene.ToDocument(),"lualatex",null,null,timeout.Token,settings);Check(assembled.Image is not null,assembled.Log);
        File.Copy(assembled.SvgPath,".codex-smoke/labo-coverage/assembly.svg",true);
        Check(!File.ReadAllText(assembled.SvgPath).Contains("<image"),"Assembly contains raster images.");
        Exception? failure=null;var thread=new Thread(()=>
        {
            try
            {
                var app=new App();app.InitializeComponent();var editor=new LaboEditorWindow(scene);var cache=(Dictionary<string,DrawingImage>)Field(editor,"_images");foreach(var asset in assets)cache[asset.Key]=asset.Value;
                ((HashSet<int>)Field(editor,"_selected")).Add(0);editor.GetType().GetMethod("Properties",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);
                var content=(FrameworkElement)editor.Content;content.Measure(new Size(1250,800));content.Arrange(new Rect(0,0,1250,800));content.UpdateLayout();editor.GetType().GetMethod("Fit",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(editor,null);content.UpdateLayout();
                Save(content,".codex-smoke/labo-coverage/editor.png",1250,800);editor.Close();
                var view=new Border {Background=Brushes.White,Width=1100,Height=1000,Child=new Image {Source=assembled.Image,Stretch=Stretch.Uniform}};view.Measure(new Size(1100,1000));view.Arrange(new Rect(0,0,1100,1000));view.UpdateLayout();Save(view,".codex-smoke/labo-coverage/preview.png",1100,1000);app.Shutdown();
            }catch(Exception e){failure=e;}
        });thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();if(failure is not null)throw failure;
        Console.WriteLine($"{LaboScene.Catalog.Count} apparatus/parts + all 39 native property variants compiled. Cropped independent-phase assets and attached/curved tubing compiled and loaded as vectors.");
    }
    static void Save(FrameworkElement element,string path,int width,int height)
    {var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);encoder.Save(stream);}
}
