using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TikzEdt.Modern;

internal static class ChemfigStereoRegression
{
    public static void Run()
    {
        foreach (var type in ChemfigBondTypes.All)
        {
            var source = "C" + type.Token + "O";
            var graph = ChemfigMolecule.Parse(source);
            if (graph.Bonds.Single().Kind != type.Token || graph.ToSource() != source) throw new Exception("Bond token changed: " + source);
            if (ChemfigBondTypes.Reverse(ChemfigBondTypes.Reverse(type.Token)) != type.Token) throw new Exception("Reversing a Cram bond lost its depth/style.");
            var ring = ChemfigMolecule.Ring(6, false); ring.Bonds[^1] = ring.Bonds[^1] with { Kind = type.Token };
            var serialized = ring.ToSource();
            if (ChemfigMolecule.Parse(serialized).ToSource() != serialized) throw new Exception("Stereobond ring closure did not round trip: " + serialized);
        }
        foreach (var source in new[] { @"\charge{90=\:,180=\|}{O}-H", @"C-\Charge{90=\:,270=\:}{N}", @"\charge{90=\:}{N}*6(-=-=-=)", @"C*6(-\charge{90=\:}{N}=-=-=)" })
        {
            var graph = ChemfigMolecule.Parse(source); var result = graph.ToSource(); var restored = ChemfigMolecule.Parse(result);
            if (restored.ToSource() != result || restored.Atoms.Sum(a => a.LonePairs.Count) != graph.Atoms.Sum(a => a.LonePairs.Count)) throw new Exception("Lone-pair source round trip failed: " + source);
            if (!graph.Clone().Atoms.SelectMany(a => a.LonePairs).SequenceEqual(graph.Atoms.SelectMany(a => a.LonePairs))) throw new Exception("Clone lost lone pairs.");
        }
        Exception? failure = null;
        var thread = new Thread(() => { try { Ui(); } catch (Exception e) { failure = e; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure is not null) throw failure;
        Console.WriteLine("Nine bond styles, Cram reversals/closures, lone-pair import/export and read-only bond-type auto-detection passed.");
    }
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Ui()
    {
        var app = new App(); app.InitializeComponent();
        foreach (var type in ChemfigBondTypes.All)
        {
            var editor = new ChemfigEditorWindow(ChemfigMolecule.Parse("C" + type.Token + "O"), "pdflatex", "", null);
            var count = ((System.Collections.ICollection)Field(editor, "_undo")).Count; var generation = Field(editor, "_generation"); var source = editor.Result;
            editor.BeginPointer(new Point(32.5, 0), false); editor.EndPointer(new Point(32.5, 0), false);
            var buttons = (List<RadioButton>)Field(editor, "_bondButtons");
            if (buttons.Count(b => b.IsChecked == true) != 1 || buttons[ChemfigBondTypes.Index(type.Token)].IsChecked != true) throw new Exception("Bond selection did not detect " + type.Token);
            if (editor.Result != source || !Equals(generation, Field(editor, "_generation")) || ((System.Collections.ICollection)Field(editor, "_undo")).Count != count) throw new Exception("Reading a bond's type mutated source/history.");
            var canvas = (Canvas)Field(editor, "_canvas");
            if (type.Token.StartsWith('>') || type.Token.StartsWith('<'))
            {
                var rightTip = type.Token.StartsWith('>');
                if (type.Token.EndsWith(':'))
                {
                    var segments = canvas.Children.OfType<System.Windows.Shapes.Line>().Where(c=>Equals(c.Tag,"cram-hash")).ToArray();
                    var widest = segments.OrderByDescending(c=>Math.Abs(c.Y2-c.Y1)).First();
                    if (rightTip ? widest.X1 > 1 : widest.X1 < 64) throw new Exception("Hashed wedge wide end disagrees with Chemfig: "+type.Token);
                }
                else
                {
                    var wedge = canvas.Children.OfType<System.Windows.Shapes.Polygon>().Single(c=>Equals(c.Tag,"cram-solid") || Equals(c.Tag,"cram-hollow"));
                    if (rightTip ? wedge.Points[0].X < 64 : wedge.Points[0].X > 1) throw new Exception("Wedge narrow end disagrees with Chemfig: "+type.Token);
                }
            }
            if (type.Token.EndsWith(':') && canvas.Children.OfType<FrameworkElement>().Count(c => Equals(c.Tag, "cram-hash")) != 8) throw new Exception("Hashed Cram representation not drawn.");
            if (type.Token.EndsWith('|') && !canvas.Children.OfType<FrameworkElement>().Any(c => Equals(c.Tag, "cram-hollow"))) throw new Exception("Hollow Cram representation not drawn.");
            editor.Close();
        }
        var mixed = new ChemfigEditorWindow(ChemfigMolecule.Parse("C-C=O"), "pdflatex", "", null);
        mixed.BeginPointer(new Point(32.5, 0), false); mixed.EndPointer(new Point(32.5, 0), false);
        mixed.BeginPointer(new Point(97.5, 0), true);
        if (((List<RadioButton>)Field(mixed, "_bondButtons")).Any(b => b.IsChecked == true) || ((TextBlock)Field(mixed, "_bondSummary")).Text != "Mixed bond types") throw new Exception("Mixed selection pretended to have one bond type.");
        var before = mixed.Result; ((List<RadioButton>)Field(mixed, "_bondButtons"))[ChemfigBondTypes.Index(">:")].IsChecked = true;
        if (((ChemfigMolecule)Field(mixed, "_molecule")).Bonds.Any(b => b.Kind != ">:")) throw new Exception("Applying a Cram style did not update all selected bonds.");
        mixed.HandleEditorKey(Key.Z, ModifierKeys.Control, false); if (mixed.Result != before) throw new Exception("Multi-bond style update is not one undo step."); mixed.Close();
        var pairs = new ChemfigEditorWindow(ChemfigMolecule.Parse("O-N"), "pdflatex", "", null);
        pairs.HandleEditorKey(Key.A, ModifierKeys.Control, false);
        pairs.EditLonePairs(90, false, false); pairs.EditLonePairs(180, true, false);
        var graph = (ChemfigMolecule)Field(pairs, "_molecule");
        if (graph.Atoms.Any(a => a.LonePairs.Count != 2) || !pairs.Result.Contains(@"\charge{90=\:,180=\|}{O}")) throw new Exception("Lone pairs were not assigned to all selected atoms.");
        pairs.EditLonePairs(90, false, true); if (((ChemfigMolecule)Field(pairs, "_molecule")).Atoms.Any(a => a.LonePairs.Count != 1)) throw new Exception("Removing one pair removed the wrong positions.");
        pairs.HandleEditorKey(Key.Z, ModifierKeys.Control, false); pairs.HandleEditorKey(Key.A, ModifierKeys.Control, false); pairs.EditLonePairs(null, false, true);
        if (((ChemfigMolecule)Field(pairs, "_molecule")).Atoms.Any(a => a.LonePairs.Count != 0)) throw new Exception("Clearing pairs failed.");
        pairs.HandleEditorKey(Key.Z, ModifierKeys.Control, false);
        var content = (FrameworkElement)pairs.Content; content.Measure(new Size(1056, 640)); content.Arrange(new Rect(0, 0, 1056, 640)); content.UpdateLayout();
        var canvasPairs = (Canvas)Field(pairs, "_canvas");
        if (canvasPairs.Children.OfType<FrameworkElement>().Count(c => Equals(c.Tag, "lone-pair")) != 6) throw new Exception("Lone dot/line pairs are not drawn as native vectors.");
        var bitmap = new RenderTargetBitmap(1080, 670, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); Directory.CreateDirectory(".codex-smoke/chemfig-stereo-tests"); using (var output = File.Create(".codex-smoke/chemfig-stereo-tests/editor.png")) encoder.Save(output);
        pairs.Close(); app.Shutdown();
    }
    public static async Task IntegrationAsync()
    {
        var bonds = "C" + string.Concat(ChemfigBondTypes.All.Select(t => t.Token + "C"));
        var pairs = @"\charge{90=\:,270=\:}{O}-\charge{90=\|}{N}(-[:90]H)>:[:30]C<|[:-30]F";
        var ring = ChemfigMolecule.Ring(6, true); ring.Atoms[0] = ring.Atoms[0] with { Label = "N", LonePairs = new[] { new ChemLonePair(180) } }; ring.Bonds[^1] = ring.Bonds[^1] with { Kind = ">:" };
        var source = ChemfigEditorWindow.BuildPreviewSource(@"\chemfig{" + ChemfigMolecule.Parse(bonds).ToSource() + @"}\\[12pt]\chemfig{" + ChemfigMolecule.Parse(pairs).ToSource() + @"}\\[12pt]\chemfig{" + ring.ToSource() + "}", Color.FromRgb(223,225,229));
        // A short stack is valid inside the TikZ node's text box.
        source = source.Replace(@"\node[inner sep=0pt]", @"\node[inner sep=0pt,align=center]");
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var result = await TexVectorRenderer.RenderAsync(source, engine, null, "", timeout.Token, new UserSettings());
            if (result.ExitCode != 0 || result.Image is null) throw new Exception(engine + ": " + result.Log);
            Directory.CreateDirectory(".codex-smoke/chemfig-stereo-tests"); File.Copy(result.SvgPath, ".codex-smoke/chemfig-stereo-tests/" + engine + ".svg", true);
            Console.WriteLine(engine + ": nine Cram/in-plane styles, dot/line lone pairs and stereobond ring closure compile as vectors.");
        }
    }
}
