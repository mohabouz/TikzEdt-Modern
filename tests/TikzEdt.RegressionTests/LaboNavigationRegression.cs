using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TikzEdt.Modern;

internal static class LaboNavigationRegression
{
    static object Field(object target,string name) => target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    static void Require(bool value,string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        var scene = new LaboScene { Items = LaboScene.Catalog.Select((d,i) => LaboScene.Create(d.Id,i*2,0)).ToList() };
        var code = LaboScene.BuildDocument(scene.Items);
        Require(LaboScene.ReadDocument(code)!.Items.Count == LaboScene.Catalog.Count,"Laboratory source/state round trip failed.");
        Require(LaboScene.ReadDocument(code.Replace("\n","\r\n"))!.Items.Count == LaboScene.Catalog.Count,"CRLF laboratory source import failed.");
        try { LaboScene.ReadDocument(code.Replace("unit=0.5cm","unit=1cm")); throw new Exception("Manual source edit was overwritten."); } catch (FormatException) { }
        var bad = LaboScene.Create("tube",0,0); bad.Options["tubePenche"] = "90";
        try { LaboScene.BuildDocument(new[] {bad}); throw new Exception("Out-of-range tilt accepted."); } catch (FormatException) { }
        bad.Options["tubePenche"] = "0"; bad.Options["substance"] = @"\input{x}";
        try { LaboScene.BuildDocument(new[] {bad}); throw new Exception("Arbitrary macro injection accepted."); } catch (FormatException) { }
        Exception? failure=null; var thread=new Thread(()=>{try { Ui(); } catch(Exception e) {failure=e;}}); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(failure is not null) throw failure;
        Console.WriteLine($"All {LaboScene.Catalog.Count} apparatus definitions, state/CRLF round trips, source-edit protection, property validation, wedge endpoints, anchored zoom, pan and source/history isolation passed.");
    }
    static void Ui()
    {
        var app=new App(); app.InitializeComponent();
        var chem=new ChemfigEditorWindow(ChemfigMolecule.Parse("C>O"),"pdflatex","",null);
        var canvas=(Canvas)Field(chem,"_canvas"); var view=(DiagramViewport)Field(chem,"_editorView");
        var content=(FrameworkElement)chem.Content; content.Measure(new Size(1150,740)); content.Arrange(new Rect(0,0,1150,740)); content.UpdateLayout();
        var wedge=canvas.Children.OfType<Polygon>().Single(p=>Equals(p.Tag,"cram-solid"));
        Require(wedge.Points[0].X > wedge.Points[1].X,"C>O must have its narrow end at O (right), matching Chemfig.");
        var before=chem.Result; var generation=Field(chem,"_generation"); var undo=((System.Collections.ICollection)Field(chem,"_undo")).Count;
        var anchor=new Point(90,130); var oldOrigin=view.Origin; var local=(anchor-oldOrigin)/view.Scale;
        view.ZoomAt(anchor,4);
        Require((view.Origin+local*view.Scale-anchor).Length<1e-8,"Wheel zoom did not preserve the world position under the pointer.");
        var shifted=view.Origin; view.Pan(new Vector(82,-35)); Require((view.Origin-shifted-new Vector(82,-35)).Length<1e-8,"Hand pan offset incorrect.");
        Require(chem.Result==before && Equals(generation,Field(chem,"_generation")) && ((System.Collections.ICollection)Field(chem,"_undo")).Count==undo,"Navigating the editor changed source or triggered compilation.");
        var preview=(DiagramViewport)Field(chem,"_previewView"); var image=(Image)Field(chem,"_preview");
        var drawing=new GeometryDrawing(Brushes.Black,null,new RectangleGeometry(new Rect(0,0,180,100))); drawing.Freeze(); var source=new DrawingImage(drawing); source.Freeze(); image.Source=source;
        preview.ZoomAt(new Point(30,70),5); preview.Pan(new Vector(-60,25));
        Require(ReferenceEquals(image.Source,source) && Equals(generation,Field(chem,"_generation")),"Preview navigation regenerated artwork.");
        view.Fit(new Rect(-50,-60,200,160));
        content.UpdateLayout(); Directory.CreateDirectory("tests/TikzEdt.RegressionTests/labo-tests"); Save(content,"tests/TikzEdt.RegressionTests/labo-tests/chemfig-ui.png",1150,740);
        chem.Close();
        var lab=new LaboEditorWindow(new LaboScene()); lab.Add("erlen"); lab.Add("titration");
        Require(LaboScene.ReadDocument(lab.Result)!.Items.Count==2,"Lab UI insertion failed.");
        var labContent=(FrameworkElement)lab.Content; labContent.Measure(new Size(1176,730)); labContent.Arrange(new Rect(0,0,1176,730)); labContent.UpdateLayout();
        var labView=(DiagramViewport)Field(lab,"_view"); var selected=(HashSet<int>)Field(lab,"_selected"); selected.UnionWith(new[]{0,1});
        var originalScene=LaboScene.ReadDocument(lab.Result)!; var compileGeneration=Field(lab,"_generation");
        var start=labView.Origin+new Vector(150,-120);
        lab.BeginDrag(start,false); lab.MoveDrag(start+new Vector(2*.5*96/2.54*labView.Scale,-.5*96/2.54*labView.Scale));
        Require(Equals(compileGeneration,Field(lab,"_generation")),"Dragging laboratory objects triggered recompilation.");
        lab.EndDrag(); var moved=LaboScene.ReadDocument(lab.Result)!;
        for(var i=0;i<2;i++) Require(Math.Abs(moved.Items[i].X-originalScene.Items[i].X-2)<1e-8 && Math.Abs(moved.Items[i].Y-originalScene.Items[i].Y-1)<1e-8,"Laboratory group translation is not uniform/snapped.");
        lab.GetType().GetMethod("Undo",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(lab,null);
        Require(lab.Result==LaboScene.BuildDocument(originalScene.Items),"Laboratory group movement is not one undo step.");
        Save(labContent,"tests/TikzEdt.RegressionTests/labo-tests/labo-ui.png",1176,730); lab.Close(); app.Shutdown();
    }
    static void Save(FrameworkElement element,string path,int width,int height)
    { var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(element); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream=File.Create(path); encoder.Save(stream); }
    public static async Task IntegrationAsync()
    {
        var scene=new LaboScene { Items=LaboScene.Catalog.Select((d,i)=>LaboScene.Create(d.Id,(i%5)*32,(i/5)*32)).ToList() };
        var source=LaboScene.BuildDocument(scene.Items);
        foreach(var engine in new[]{"lualatex"})
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150));
            var result=await TexVectorRenderer.RenderAsync(source,engine,null,null,timeout.Token,new UserSettings());
            if(result.ExitCode!=0 || result.Image is null) throw new Exception(engine+": "+result.Log);
            Directory.CreateDirectory("tests/TikzEdt.RegressionTests/labo-tests"); File.Copy(result.SvgPath,"tests/TikzEdt.RegressionTests/labo-tests/"+engine+".svg",true); File.Copy(result.PdfPath,"tests/TikzEdt.RegressionTests/labo-tests/"+engine+".pdf",true);
            Require(!File.ReadAllText(result.SvgPath).Contains("<image"),"Unexpected raster content in apparatus SVG.");
            Console.WriteLine(engine+": all 15 apparatus types compiled and loaded as native vectors with no embedded raster images.");
        }
    }
    public static async Task VisualProofAsync()
    {
        var ink=Color.FromRgb(223,225,229);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var wedge=await TexVectorRenderer.RenderAsync(ChemfigEditorWindow.BuildPreviewSource(@"\chemfig{C>O}",ink),"lualatex",null,null,timeout.Token,new UserSettings());
        Require(wedge.Image is not null,"Wedge proof compilation failed: "+wedge.Log);
        var items=new[]{LaboScene.Create("erlen",0,0),LaboScene.Create("titration",16,0)};
        var images=new Dictionary<string,DrawingImage>();
        foreach(var item in items)
        {
            var key=LaboScene.ObjectSource(item);
            var result=await TexVectorRenderer.RenderAsync(key,"lualatex",null,null,timeout.Token,new UserSettings());
            Require(result.Image is not null,"Individual apparatus failed: "+result.Log); images[key]=result.Image!;
        }
        var gallery=await TexVectorRenderer.LoadAsync(System.IO.Path.GetFullPath("tests/TikzEdt.RegressionTests/labo-tests/lualatex.svg"),timeout.Token);
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            try
            {
                var app=new App(); app.InitializeComponent();
                var chem=new ChemfigEditorWindow(ChemfigMolecule.Parse("C>O"),"lualatex","",null);
                var content=(FrameworkElement)chem.Content; content.Measure(new Size(1150,740)); content.Arrange(new Rect(0,0,1150,740)); content.UpdateLayout();
                ((Image)Field(chem,"_preview")).Source=wedge.Image;
                chem.GetType().GetMethod("FitPreview",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(chem,null);
                chem.GetType().GetMethod("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(chem,null);
                content.UpdateLayout(); Save(content,"tests/TikzEdt.RegressionTests/labo-tests/wedge-proof.png",1150,740); chem.Close();
                var lab=new LaboEditorWindow(new LaboScene {Items=items.ToList()}); var cached=(Dictionary<string,DrawingImage>)Field(lab,"_images"); foreach(var entry in images) cached[entry.Key]=entry.Value;
                var root=(FrameworkElement)lab.Content; root.Measure(new Size(1176,730)); root.Arrange(new Rect(0,0,1176,730)); root.UpdateLayout();
                lab.GetType().GetMethod("Fit",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(lab,null); root.UpdateLayout(); Save(root,"tests/TikzEdt.RegressionTests/labo-tests/labo-ready.png",1176,730); lab.Close();
                var overview=new Image {Source=gallery,Stretch=Stretch.Uniform,Width=1800,Height=1300}; var paper=new Border {Background=Brushes.White,Child=overview,Width=1800,Height=1300}; paper.Measure(new Size(1800,1300)); paper.Arrange(new Rect(0,0,1800,1300)); paper.UpdateLayout(); Save(paper,"tests/TikzEdt.RegressionTests/labo-tests/catalog.png",1800,1300); app.Shutdown();
            }
            catch(Exception e){failure=e;}
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if(failure is not null) throw failure;
        Console.WriteLine("Actual compiled wedge/editor comparison, individual apparatus composition and 15-object vector gallery captured.");
    }
}
