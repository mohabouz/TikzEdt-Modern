using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TikzEdt.Modern;

internal static class ChemfigLayoutRegression
{
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    public static void Run()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Ui(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) throw failure;
        Console.WriteLine("Chemfig compact layout, disclosures, icon accessibility, shared navigation and source/history isolation passed at normal and minimum window sizes.");
    }
    private static void Ui()
    {
        var app = new App(); app.InitializeComponent();
        var window = new ChemfigEditorWindow(ChemfigMolecule.Parse("H-C(=[:90]O)-O-H"), "pdflatex", "", null);
        var root = (FrameworkElement)window.Content;
        void Layout(int width, int height) { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
        Layout(1056, 637);
        var controls = Descendants<ContentControl>(root).ToArray();
        T Named<T>(string name) where T : ContentControl => controls.OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
        foreach (var name in new[] { "Select", "Draw bond", "Undo", "Redo", "Delete", "Fit both panes", "Add lone pair", "Skeletal carbon" })
            Require(Named<ContentControl>(name).Content is ToolIcon, name + " should be icon-only.");
        var source = window.Result; var generation = Field(window, "_generation"); var undo = ((System.Collections.ICollection)Field(window, "_undo")).Count;
        var editor = (DiagramViewport)Field(window, "_editorView"); var preview = (DiagramViewport)Field(window, "_previewView");
        var pan = Named<ToggleButton>("Pan both panes"); pan.IsChecked = true;
        Require(editor.HandButton.IsChecked == true && preview.HandButton.IsChecked == true, "Pan must affect both panes.");
        Named<RadioButton>("Draw bond").IsChecked = true;
        Require(pan.IsChecked == false && editor.HandButton.IsChecked == false && preview.HandButton.IsChecked == false, "Drawing must exit shared pan.");
        var drawing = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 180, 90))); drawing.Freeze();
        ((Image)Field(window, "_preview")).Source = new DrawingImage(drawing);
        editor.Pan(new Vector(500, 300)); preview.Pan(new Vector(300, 200));
        Named<Button>("Fit both panes").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(editor.Scale != 1 && preview.Scale != 1, "Fit must fit both views.");
        Require(window.Result == source && Equals(generation, Field(window, "_generation")) && undo == ((System.Collections.ICollection)Field(window, "_undo")).Count, "Navigation modified source, compilation or history.");
        var rail = Descendants<Border>(root).Single(b => AutomationProperties.GetName(b) == "Atom rail");
        Require(rail.ActualWidth <= 42, "Left palette must be a narrow rail.");
        Require(Named<Button>("Add lone pair").TranslatePoint(new Point(), root).Y < 50, "Lone pairs must be in the top toolbar.");
        Named<Button>("Add lone pair").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(window.Result != source && window.Result.Contains("charge"), "Top-bar lone pair action did not edit the selected atom.");
        window.GetType().GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Require(window.Result == source, "Lone pair edit did not undo.");
        var pairPopup = (Popup)Field(window, "_pairPopup");
        var pairControls = Descendants<ComboBox>(pairPopup.Child).ToArray();
        Require(pairControls.Contains((ComboBox)Field(window, "_pairAngle")) && pairControls.Contains((ComboBox)Field(window, "_pairStyle")), "Lone-pair options missing angle/style controls.");
        ((ComboBox)Field(window, "_pairAngle")).SelectedIndex = 1;
        ((ComboBox)Field(window, "_pairStyle")).SelectedIndex = 1;
        Require(window.Result == source, "Changing lone-pair options should not edit the molecule.");
        Named<Button>("Add lone pair").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(window.Result.Contains("0="), "Chosen lone-pair angle not applied.");
        Descendants<Button>(pairPopup.Child).Single(b => AutomationProperties.GetName(b) == "Remove at angle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(window.Result == source, "Remove pair action failed.");
        var atoms = new[] { "C", "H", "N", "O", "S", "P" }.Select(s => controls.OfType<Button>().Single(b => Equals(b.Content, s))).ToArray();
        Require(atoms.Select(a => a.TranslatePoint(new Point(), root).X).Distinct().Count() == 1, "Essential atoms must form one vertical column.");
        var more = Named<Button>("More atoms");
        Require(more.ContextMenu!.Items.OfType<MenuItem>().All(m => m.Icon is null && m.Header is string), "Element symbols must appear once, not in both icon and label columns.");
        var chlorine = more.ContextMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Cl"));
        chlorine.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(window.Result.Contains("Cl"), "More atom choices must edit the molecule.");
        window.GetType().GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Require(window.Result == source, "Palette edit must undo cleanly.");
        var rings = Named<Button>("More rings"); var groups = Named<Button>("More groups");
        Require(rings.ContextMenu!.Items.Count == 7 && groups.ContextMenu!.Items.Count == 4, "Ring/group alternatives missing.");
        Require(rings.Width <= 12 && rings.ContextMenu.Placement == PlacementMode.Right && groups.ContextMenu.Placement == PlacementMode.Right && more.ContextMenu.Placement == PlacementMode.Right, "Corner disclosures must open flyouts beside the rail.");
        Require(rings.ContextMenu.Items.OfType<MenuItem>().All(i => i.Icon is ToolIcon), "Flyout choices must have icons.");
        groups.ContextMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "NH_2")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(window.Result.Contains("NH_2"), "Group dropdown must insert the chosen label.");
        window.GetType().GetMethod("Undo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Named<RadioButton>("Select").IsChecked = true;
        Directory.CreateDirectory(".codex-smoke/chemfig-layout");
        foreach (var size in new[] { (1056, 637), (876, 477) })
        {
            Layout(size.Item1, size.Item2);
            var grid = Descendants<Border>(root).Single(b => AutomationProperties.GetName(b) == "Construction grid");
            var sourceBox = (TextBox)Field(window, "_source");
            Require(grid.TranslatePoint(new Point(), root).Y > sourceBox.TranslatePoint(new Point(0, sourceBox.ActualHeight), root).Y, "Grid controls must be below source/actions.");
            Require(grid.ActualWidth > size.Item1 - 30, "Grid bar must span the dialog.");
            var bonds = (List<RadioButton>)Field(window, "_bondButtons");
            Require(bonds.Count == 9 && bonds.All(b => b.TranslatePoint(new Point(), root).Y < 50), "All bonds should be grouped in the top toolbar.");
            Require(editor.HandButton.Visibility == Visibility.Collapsed && preview.FitButton.Visibility == Visibility.Collapsed, "Duplicate navigation controls remain.");
            var bitmap = new RenderTargetBitmap(size.Item1, size.Item2, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create($".codex-smoke/chemfig-layout/{size.Item1}.png"); encoder.Save(stream);
        }
        window.Close(); app.Shutdown();
    }
}
