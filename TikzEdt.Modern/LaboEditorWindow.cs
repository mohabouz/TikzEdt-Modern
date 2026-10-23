using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TikzEdt.Modern;

/// <summary>PST-Labo assembly with cropped native vectors, schema-driven properties and attached tubing.</summary>
internal sealed partial class LaboEditorWindow : Window
{
    private LaboScene _scene;
    private readonly DiagramViewport _view = new();
    private readonly StackPanel _properties = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5) };
    private readonly ComboBox _engine = new() { ItemsSource = new[] { "lualatex", "xelatex" }, SelectedIndex = 0, Width = 115, ToolTip = "LuaLaTeX is the tested default. XeLaTeX requires a compatible PSTricks/Ghostscript driver and may be much slower." };
    private readonly CheckBox _snap = new() { Content = "Snap · 0.5", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, DrawingImage> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DrawingImage> _itemImages = new(StringComparer.Ordinal);
    private readonly Dictionary<Dictionary<string,string>,(string Type,string Source)> _sourceKeys=new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<int> _selected = [];
    private readonly Stack<LaboScene> _undo = new(), _redo = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _job;
    private readonly CancellationTokenSource _lifetime = new();
    private int _generation;
    private LaboScene? _beforeDrag;
    private Point? _dragStart;
    private readonly UserSettings _settings;
    private readonly Button _apply = new() {Content="Apply TeX document"};
    private bool _fitAfterRender=true;
    private const double Unit = .5 * 96 / 2.54;
    public string Result => _scene.ToDocument();
    public string Engine => (string)_engine.SelectedItem;
    public LaboEditorWindow(LaboScene scene)
    {
        _scene = scene.Clone(); _settings = SettingsStore.Current.Clone();
        NativeWindowTheme.Attach(this); Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "PST-Labo — laboratory apparatus"; Width = 1200; Height = 800; MinWidth = 960; MinHeight = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        root.SetResourceReference(Panel.BackgroundProperty,"WindowBrush");
        var tools = new WrapPanel(); DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        Button Action(Panel parent, string text, Action action) { var b = new Button { Content = text }; b.Click += (_, _) => action(); parent.Children.Add(b); return b; }
        Action(tools, "Undo", Undo); Action(tools, "Redo", Redo); Action(tools, "Duplicate", Duplicate); Action(tools, "Delete", Delete);
        BuildTubeTools(tools);
        Action(tools, "Exact scene preview", async () => await PreviewSceneAsync());
        tools.Children.Add(new TextBlock { Text = "Compiler", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,4,0) }); tools.Children.Add(_engine);
        var bottom = new DockPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Right); bottom.Children.Add(buttons);
        var cancel = new Button { Content = "Cancel", IsCancel = true }; buttons.Children.Add(cancel);
        _apply.Click+=(_,_)=> { _ = Result; DialogResult=true; }; buttons.Children.Add(_apply); bottom.Children.Add(_status);
        var area = new Grid(); area.ColumnDefinitions.Add(new() { Width = new GridLength(215) }); area.ColumnDefinitions.Add(new()); area.ColumnDefinitions.Add(new() { Width = new GridLength(235) }); root.Children.Add(area);
        var catalog = new StackPanel { Margin = new Thickness(0,8,10,0) };
        foreach (var category in LaboScene.Catalog.GroupBy(d => d.Category))
        {
            var entries = new StackPanel();
            foreach (var definition in category)
            {
                var b = new Button { Content = definition.Title, HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = "Insert " + definition.Title + " (\\" + definition.Command + ")" };
                b.Click += (_, _) => Add(definition.Id); entries.Children.Add(b);
            }
            catalog.Children.Add(new Expander { Header = category.Key, IsExpanded = true, Content = entries, Margin = new Thickness(0,6,0,0) });
        }
        catalog.Children.Add(new TextBlock { Text = "Drag objects; Ctrl+click selects multiple.\nTube: click two points; endpoints attach to apparatus and follow it.\nDrag selected tube endpoints/bend to edit.\nMiddle-drag / Space+drag pans.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4,16,4,4), Foreground = (Brush)FindResource("MutedBrush") });
        catalog.Children.Add(new TextBlock {Text="Package limitation: pstBigPipette is unfinished upstream. Use the standard pipette; the broken macro is not offered as a working drawing.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(4,8,4,4),Foreground=(Brush)FindResource("MutedBrush")});
        area.Children.Add(new ScrollViewer { Content = catalog, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var center = new DockPanel { Margin = new Thickness(0,8,8,8) }; Grid.SetColumn(center, 1); area.Children.Add(center);
        var footer = new StackPanel { Orientation = Orientation.Horizontal }; footer.Children.Add(_snap); footer.Children.Add(new TextBlock { Text = " · 1 unit = 0.5 cm", Margin = new Thickness(5), VerticalAlignment = VerticalAlignment.Center }); DockPanel.SetDock(footer, Dock.Bottom); center.Children.Add(footer); center.Children.Add(_view);
        var propertyScroll = new ScrollViewer { Content = _properties, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(5,8,0,0) }; Grid.SetColumn(propertyScroll, 2); area.Children.Add(propertyScroll);
        _view.Surface.Background = Brushes.White;
        _view.ViewChanged += Draw; _view.FitRequested += Fit;
        _view.Surface.MouseLeftButtonDown += (_, e) => {var p=e.GetPosition(_view.Surface); if(!TubeDown(p)) BeginDrag(p, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));};
        _view.Surface.MouseMove += (_, e) => { if(!TubeMove(e.GetPosition(_view.Surface),e.LeftButton==MouseButtonState.Pressed) && _dragStart is not null && e.LeftButton == MouseButtonState.Pressed) MoveDrag(e.GetPosition(_view.Surface)); };
        _view.Surface.MouseLeftButtonUp += (_, e) => {if(!TubeUp()) EndDrag();};
        _view.Surface.LostMouseCapture += (_, _) => { if (_dragStart is not null) CancelDrag(); if(_tubeDrag is not null){CancelTubeGesture();Draw();} };
        _engine.SelectionChanged += (_, _) => { _images.Clear(); Changed(); };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or ComboBox or ComboBoxItem) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.A) { _selected.UnionWith(Enumerable.Range(0,_scene.Items.Count)); _selectedTubes.UnionWith(_scene.Tubes.Select(t=>t.Id)); Draw(); Properties(); e.Handled = true; }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key is Key.Z or Key.Y) { CancelDrag(); if (e.Key == Key.Z) Undo(); else Redo(); e.Handled = true; }
            else if (e.Key == Key.Delete) { Delete(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CancelTubeGesture(); CancelDrag(); _selected.Clear(); _selectedTubes.Clear(); Draw(); Properties(); e.Handled = true; }
        };
        _timer.Tick += async (_, _) => { _timer.Stop(); await RenderObjectsAsync(); };
        Loaded += (_, _) => { Changed(); Fit(); };
        Closed += (_, _) => { _generation++; _timer.Stop(); _lifetime.Cancel(); _job?.Cancel(); _job?.Dispose(); };
        Properties();
    }
    internal void Add(string type)
    {
        if (_scene.Items.Count >= 100) { _status.Text = "Initial editor limit: 100 objects."; return; }
        SaveUndo(); var n = _scene.Items.Count; _scene.Items.Add(LaboScene.Create(type, n * 2, 0)); _selected.Clear(); _selectedTubes.Clear(); _selected.Add(n); _fitAfterRender=true; Changed(); Fit();
    }
    private void SaveUndo() { _undo.Push(_scene.Clone()); _redo.Clear(); }
    private void Undo() { CancelTubeGesture(); if (!_undo.TryPop(out var scene)) return; _redo.Push(_scene.Clone()); _scene = scene; _selected.Clear(); _selectedTubes.Clear(); Changed(); }
    private void Redo() { CancelTubeGesture(); if (!_redo.TryPop(out var scene)) return; _undo.Push(_scene.Clone()); _scene = scene; _selected.Clear(); _selectedTubes.Clear(); Changed(); }
    private void Delete()
    {
        if (_selected.Count==0 && _selectedTubes.Count==0) return; SaveUndo(); var ids=_selected.Select(i=>_scene.Items[i].Id).ToHashSet();
        _scene.Tubes.RemoveAll(t=>_selectedTubes.Contains(t.Id) || t.Start.ItemId is {} a && ids.Contains(a) || t.End.ItemId is {} b && ids.Contains(b));
        _scene.Items=_scene.Items.Where((_,i)=>!_selected.Contains(i)).ToList(); _selected.Clear(); _selectedTubes.Clear(); Changed();
    }
    private void Duplicate()
    {
        if(_selected.Count==0 && _selectedTubes.Count==0 || _scene.Items.Count+_selected.Count>100)return;
        SaveUndo();var map=_selected.ToDictionary(i=>_scene.Items[i].Id,_=>Guid.NewGuid().ToString("N"));
        var copies=_selected.Order().Select(i=>_scene.Items[i] with {Id=map[_scene.Items[i].Id],X=_scene.Items[i].X+2,Y=_scene.Items[i].Y+2,Options=new(_scene.Items[i].Options)}).ToArray();
        LaboEnd CopyEnd(LaboEnd e)=>e.ItemId is null?e with {X=e.X+2,Y=e.Y+2}:map.TryGetValue(e.ItemId,out var id)?e with {ItemId=id}:e;
        var tubes=_scene.Tubes.Where(t=>_selectedTubes.Contains(t.Id) || t.Start.ItemId is {} a && t.End.ItemId is {} b && map.ContainsKey(a)&&map.ContainsKey(b)).Select(t=>t with {Id=Guid.NewGuid().ToString("N"),Start=CopyEnd(t.Start),End=CopyEnd(t.End),ControlX=t.ControlX+2,ControlY=t.ControlY+2}).ToArray();
        _selected.Clear();_selectedTubes.Clear();foreach(var item in copies){_selected.Add(_scene.Items.Count);_scene.Items.Add(item);}foreach(var tube in tubes){_scene.Tubes.Add(tube);_selectedTubes.Add(tube.Id);}Changed();
    }
    private Point Screen(double x, double y) => _view.Origin + new Vector(x * Unit * _view.Scale, -y * Unit * _view.Scale);
    private Rect Bounds(LaboItem item) {var b=LaboScene.FigureBox(item);return new Rect(Screen(b.Left,b.Bottom+b.Height),Screen(b.Left+b.Width,b.Bottom));}
    private void Fit()
    {
        var bounds = Rect.Empty;
        foreach (var item in _scene.Items) {var box=LaboScene.FigureBox(item);bounds.Union(new Rect(box.Left*Unit,-(box.Bottom+box.Height)*Unit,box.Width*Unit,box.Height*Unit));}
        foreach(var tube in _scene.Tubes) foreach(var point in _scene.TubePoints(tube)) bounds.Union(new Point(point.X*Unit,-point.Y*Unit));
        if (bounds.IsEmpty) bounds = new Rect(-100,-100,200,200); _view.Fit(bounds);
    }
    internal void BeginDrag(Point point, bool control)
    {
        var hit = Enumerable.Range(0,_scene.Items.Count).Reverse().FirstOrDefault(i => Bounds(_scene.Items[i]).Contains(point), -1);
        if (hit < 0) { _selected.Clear(); _selectedTubes.Clear(); Draw(); Properties(); return; }
        if (control) { if (!_selected.Add(hit)) _selected.Remove(hit); Draw(); Properties(); return; }
        if (!_selected.Contains(hit)) { _selected.Clear(); _selectedTubes.Clear(); _selected.Add(hit); }
        _beforeDrag = _scene.Clone(); _dragStart = point; _view.Surface.CaptureMouse(); _view.Surface.Focus(); Draw(); Properties();
    }
    internal void MoveDrag(Point point)
    {
        if (_dragStart is not { } start || _beforeDrag is null) return;
        var delta = (point-start) / (Unit*_view.Scale); delta.Y = -delta.Y;
        if (_snap.IsChecked == true && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { delta.X = Math.Round(delta.X*2)/2; delta.Y = Math.Round(delta.Y*2)/2; }
        if (_selected.Count == 0) return;
        delta.X = Math.Clamp(delta.X,-500-_selected.Min(i=>_beforeDrag.Items[i].X),500-_selected.Max(i=>_beforeDrag.Items[i].X));
        delta.Y = Math.Clamp(delta.Y,-500-_selected.Min(i=>_beforeDrag.Items[i].Y),500-_selected.Max(i=>_beforeDrag.Items[i].Y));
        foreach (var i in _selected) _scene.Items[i] = _beforeDrag.Items[i] with { X = _beforeDrag.Items[i].X+delta.X, Y = _beforeDrag.Items[i].Y+delta.Y };
        var ids=_selected.Select(i=>_scene.Items[i].Id).ToHashSet();
        for(var i=0;i<_scene.Tubes.Count;i++)
        {
            var t=_beforeDrag.Tubes[i];var both=t.Start.ItemId is {} a && ids.Contains(a) && t.End.ItemId is {} b && ids.Contains(b);
            if(both)_scene.Tubes[i]=t with {ControlX=t.ControlX+delta.X,ControlY=t.ControlY+delta.Y};
            else if(_selectedTubes.Contains(t.Id) && t.Start.ItemId is null && t.End.ItemId is null) _scene.Tubes[i]=t with {Start=t.Start with {X=t.Start.X+delta.X,Y=t.Start.Y+delta.Y},End=t.End with {X=t.End.X+delta.X,Y=t.End.Y+delta.Y},ControlX=t.ControlX+delta.X,ControlY=t.ControlY+delta.Y};
        }
        Draw();
    }
    internal void EndDrag()
    {
        if (_dragStart is null || _beforeDrag is null) return; var before = _beforeDrag; _dragStart = null; _beforeDrag = null; _view.Surface.ReleaseMouseCapture();
        if (before.Items.Where((item,i) => item.X != _scene.Items[i].X || item.Y != _scene.Items[i].Y).Any()) { _undo.Push(before); _redo.Clear(); Changed(); }
    }
    private void CancelDrag() { if (_beforeDrag is not null) _scene = _beforeDrag; _dragStart = null; _beforeDrag = null; _view.Surface.ReleaseMouseCapture(); Draw(); }
    private string ImageKey(LaboItem item)
    {
        if(_sourceKeys.TryGetValue(item.Options,out var cached) && cached.Type==item.Type)return cached.Source;
        var source=LaboScene.ObjectSource(item);_sourceKeys[item.Options]=(item.Type,source);return source;
    }
    private void Draw()
    {
        var canvas = _view.Surface; canvas.Children.Clear();
        if (canvas.ActualWidth > 0 && canvas.ActualHeight > 0)
            canvas.Children.Add(new Rectangle { Width = canvas.ActualWidth, Height = canvas.ActualHeight, Fill = ChemfigGrid.Brush(new Size(canvas.ActualWidth,canvas.ActualHeight),_view.Origin,Unit*_view.Scale,true,Color.FromArgb(35,130,140,160),Color.FromArgb(100,130,140,160),0), IsHitTestVisible = false });
        for (var i=0;i<_scene.Items.Count;i++)
        {
            var item = _scene.Items[i]; var bounds = Bounds(item);
            FrameworkElement visual;
            if (_images.TryGetValue(ImageKey(item),out var image) || _itemImages.TryGetValue(item.Id,out image)) visual = new Image { Source = image, Width = bounds.Width, Height = bounds.Height, IsHitTestVisible = false };
            else visual = new Border { Width = bounds.Width, Height = bounds.Height, BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Child = new TextBlock { Text = LaboScene.Definition(item).Title + "\nPreparing vector…", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) }, IsHitTestVisible = false };
            Canvas.SetLeft(visual,bounds.Left); Canvas.SetTop(visual,bounds.Top); canvas.Children.Add(visual);
            if (_selected.Contains(i)) { var box = new Rectangle { Width = bounds.Width, Height = bounds.Height, Stroke = Brushes.DodgerBlue, StrokeThickness = 1, StrokeDashArray = new DoubleCollection {4,3}, IsHitTestVisible = false }; Canvas.SetLeft(box,bounds.Left); Canvas.SetTop(box,bounds.Top); canvas.Children.Add(box); }
        }
        DrawTubes();
    }
    private void LegacyProperties()
    {
        _properties.Children.Clear();
        void Text(string text) => _properties.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3,5,3,5) });
        if (_selected.Count != 1) { Text(_selected.Count == 0 ? "Choose apparatus from the left, then select it to edit its properties." : _selected.Count + " selected · drag to move together"); return; }
        var index = _selected.Single(); var item = _scene.Items[index]; Text(LaboScene.Definition(item).Title);
        void Change(Func<LaboItem,LaboItem> update)
        {
            try { var value = update(_scene.Items[index]); LaboScene.Validate(value); if (JsonEqual(value,_scene.Items[index])) return; SaveUndo(); _scene.Items[index] = value; Changed(false); }
            catch (Exception e) { _status.Text = e.Message; }
        }
        void Number(string title,string key,int value,int min,int max)
        {
            Text(title + $" ({min}…{max})"); var field = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture) }; _properties.Children.Add(field);
            field.LostKeyboardFocus += (_,_) => { if (int.TryParse(field.Text,out var number) && number >= min && number <= max) Change(current => current with { Options = new(current.Options) { [key] = number.ToString(CultureInfo.InvariantCulture) } }); else { _status.Text = title + " must be " + min + "…" + max; field.Text = _scene.Items[index].Options.GetValueOrDefault(key,value.ToString(CultureInfo.InvariantCulture)); } };
        }
        void Switch(string title,string key,bool initial=false)
        {
            var box = new CheckBox { Content = title, IsChecked = item.Options.GetValueOrDefault(key,initial ? "true" : "false") == "true", Margin = new Thickness(3,5,3,5) }; _properties.Children.Add(box);
            box.Click += (_,_) => Change(current => current with { Options = new(current.Options) { [key] = box.IsChecked == true ? "true" : "false" } });
        }
        var definition = LaboScene.Definition(item);
        if (definition.Id != "distillation")
        {
            Number("Liquid level", "niveauLiquide1", int.Parse(item.Options.GetValueOrDefault("niveauLiquide1","50")),0,definition.Id == "separation" ? 16 : 100);
            Text("Liquid style"); var colors = new ComboBox { ItemsSource = new[] { "aspectLiquide1", "Champagne", "Sang", "Cobalt", "Huile", "Vinaigre", "DiffusionBleue" }, SelectedItem = item.Options.GetValueOrDefault("aspectLiquide1","aspectLiquide1") }; _properties.Children.Add(colors);
            colors.SelectionChanged += (_,_) => { if (colors.SelectedItem is string value) Change(current => current with { Options = new(current.Options) { ["aspectLiquide1"] = value } }); };
        }
        if (definition.Category == "Glassware" || definition.Id is "cylinder" or "funnel")
        { Number("Vessel tilt (degrees)","tubePenche",int.Parse(item.Options.GetValueOrDefault("tubePenche","0")),-65,65); Switch("Stopper","bouchon"); Switch("Straight glass tube","tubeDroit"); Switch("Bent glass tube","tubeCoude"); Switch("Clamp","pince"); }
        if (definition.Id == "titration") { Number("Burette level (mL)","niveauReactifBurette",int.Parse(item.Options.GetValueOrDefault("niveauReactifBurette","20")),0,25); Switch("pH meter","phmetre"); Switch("Magnetic stirrer","agitateurMagnetique",true); Switch("Burette","burette",true); }
        if (definition.Id == "separation") { Number("Upper phase level","niveauLiquide2",int.Parse(item.Options.GetValueOrDefault("niveauLiquide2","12")),0,16); Switch("Open tap","OpenTap"); Switch("Stopper","bouchon"); }
        Text("Position is adjusted by dragging. The white drawing surface preserves the package’s exact ink and liquid colours.");
    }
    private bool JsonEqual(LaboItem a,LaboItem b) => ImageKey(a) == ImageKey(b) && a.X == b.X && a.Y == b.Y;
    private void Changed(bool properties = true)
    { _generation++; _job?.Cancel(); Draw(); _apply.IsEnabled=_scene.Items.All(i=>_images.ContainsKey(ImageKey(i)) && i.Ink is not null); if (properties) Properties(); _timer.Stop(); _timer.Start(); _status.Text = "Preparing authentic PST-Labo vectors…"; }
    private async Task RenderObjectsAsync()
    {
        var generation = _generation; _job?.Dispose(); _job = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); _job.CancelAfter(TimeSpan.FromSeconds(_settings.CompilerTimeoutSeconds)); var token = _job.Token;
        var engine = Engine; var items = _scene.Items.ToArray();
        try
        {
            foreach (var item in items)
            {
                var key = ImageKey(item);
                if (_images.ContainsKey(key) && item.Ink is not null) continue;
                var result = await Task.Run(() => TexVectorRenderer.RenderAsync(key,engine,null,null,token,_settings),token);
                if (generation != _generation) return;
                if (result.ExitCode != 0 || result.Image is null) throw new InvalidOperationException("PST-Labo compilation failed. Install pst-labo and luapstricks (or a working XeLaTeX PSTricks driver).\n" + result.Log[^Math.Min(900,result.Log.Length)..]);
                var ink=VectorInkBounds.Measure(result.Image.Drawing);
                if(ink.IsEmpty || ink.Width<=0 || ink.Height<=0) throw new InvalidOperationException("The apparatus produced no visible vector ink.");
                var box=new LaboBox(ink.Left/Unit,(result.Image.Height-ink.Bottom)/Unit,ink.Width/Unit,ink.Height/Unit);
                _images[key] = VectorInkBounds.Crop(result.Image,ink);
                for(var i=0;i<_scene.Items.Count;i++) if(ImageKey(_scene.Items[i])==key) { _scene.Items[i]=_scene.Items[i] with {Ink=box}; _itemImages[_scene.Items[i].Id]=_images[key]; }
                Draw();
            }
            _apply.IsEnabled=true; if(_fitAfterRender) { _fitAfterRender=false; Fit(); } _status.Text = "Vector apparatus ready · tight ink boundaries · attached tubes follow movement.";
        }
        catch (OperationCanceledException) { if (generation == _generation) _status.Text = "Apparatus compilation timed out. LuaLaTeX is recommended; check the compiler timeout and installed PSTricks packages."; }
        catch (Exception exception) { if (generation == _generation) _status.Text = exception.Message; }
    }
    private async Task PreviewSceneAsync()
    {
        _timer.Stop(); await RenderObjectsAsync(); if(!_apply.IsEnabled) return;
        var source = Result; var engine = Engine;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(_settings.CompilerTimeoutSeconds));
        try
        {
            _status.Text = "Compiling complete laboratory scene…";
            var result = await Task.Run(() => TexVectorRenderer.RenderAsync(source,engine,null,null,timeout.Token,_settings));
            if (timeout.IsCancellationRequested) return;
            if (source != Result || engine != Engine) { _status.Text = "Scene changed during compilation. Request a new exact preview."; return; }
            if (result.ExitCode != 0 || result.Image is null) throw new InvalidOperationException(result.Log[^Math.Min(1000,result.Log.Length)..]);
            var view = new DiagramViewport(); view.Surface.Background = Brushes.White; view.HandButton.IsChecked = true;
            var image = new Image { Source = result.Image, IsHitTestVisible = false }; view.Surface.Children.Add(image);
            void DrawImage() { image.Width = result.Image.Width*view.Scale; image.Height = result.Image.Height*view.Scale; Canvas.SetLeft(image,view.Origin.X-image.Width/2); Canvas.SetTop(image,view.Origin.Y-image.Height/2); }
            void FitImage() => view.Fit(new Rect(-result.Image.Width/2,-result.Image.Height/2,result.Image.Width,result.Image.Height));
            view.ViewChanged += DrawImage; view.FitRequested += FitImage;
            var window = new Window { Title = "PST-Labo — exact vector preview", Width = 850, Height = 650, Owner = this, Content = view, Style = (Style)FindResource(typeof(Window)) }; NativeWindowTheme.Attach(window); window.Loaded += (_,_) => FitImage(); window.ShowDialog(); _status.Text = "Complete scene compiled successfully.";
        }
        catch (OperationCanceledException) { if (!_lifetime.IsCancellationRequested) _status.Text = "Scene compilation timed out. Try LuaLaTeX or increase the compiler timeout."; }
        catch (Exception exception) { if (!_lifetime.IsCancellationRequested) _status.Text = exception.Message; }
    }
}
