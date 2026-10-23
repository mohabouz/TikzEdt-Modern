using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TikzEdt.Modern;

internal sealed partial class ChemfigEditorWindow : Window
{
    private ChemfigMolecule _molecule;
    private readonly DiagramViewport _editorView = new();
    private readonly DiagramViewport _previewView = new();
    private readonly Canvas _canvas;
    private readonly Image _preview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(18) };
    private readonly TextBox _label = new() { Width = 90, ToolTip = "Atom/group label, e.g. O, NH_2 or CH_3. Empty = skeletal carbon." };
    private readonly ComboBox _mode = new() { Width = 105, ItemsSource = new[] { "Move / select", "Draw bond" }, SelectedIndex = 0 };
    private readonly ComboBox _bond = new() { Width = 100, ItemsSource = ChemfigBondTypes.All.Select(t => t.Caption), SelectedIndex = 0 };
    private readonly List<RadioButton> _bondButtons = [];
    private readonly TextBlock _bondSummary = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly ComboBox _pairAngle = new() { ItemsSource = new[] { "Top · 90°", "Right · 0°", "Bottom · 270°", "Left · 180°", "NE · 45°", "NW · 135°", "SW · 225°", "SE · 315°" }, SelectedIndex = 0 };
    private readonly ComboBox _pairStyle = new() { ItemsSource = new[] { "—  Line pair", "••  Dot pair" }, SelectedIndex = 0 };
    private readonly TextBlock _pairSummary = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private bool _syncingPalette;
    private string _drawingBondKind = "-";
    private readonly TextBox _source = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, MaxHeight = 85, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 8) };
    private readonly TextBlock _chemistryWarnings = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(255,198,109)), Margin = new Thickness(4), ToolTip = "Advisory checks only: common valence and electron-count rules, not a full chemistry validator." };
    private readonly ScrollViewer _warningPanel = new() { MaxHeight = 68, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
    private readonly ComboBox _gridMode = new() { Width = 140, ItemsSource = new[] { "Hex + Cartesian", "Cartesian", "Grid off" }, SelectedIndex = 0 };
    private readonly ComboBox _hexOrientation = new() { Width = 105, ItemsSource = new[] { "Hex at 30°", "Hex at 0°" }, SelectedIndex = 0, ToolTip = "Rotate the hexagonal construction grid. Bond directions stay on its six unit-length neighbours." };
    private readonly CheckBox _snap = new() { Content = "Snap", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
    private readonly Button _apply = new() { Content = "Apply molecule" };
    private readonly HashSet<int> _selectedAtoms = [0], _selectedBonds = [];
    private readonly HashSet<int> _movingAtoms = [];
    private HashSet<int> _baseAtoms = [], _baseBonds = [];
    private bool _marquee;
    private Point? _pointer;
    private string _newAtomLabel = "";
    private readonly Stack<ChemfigMolecule> _undo = new(), _redo = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly string _engine;
    private readonly string _preamble;
    private readonly string? _directory;
    private readonly UserSettings _settings;
    private CancellationTokenSource? _cancellation;
    private int _selected;
    private int _generation;
    private Point? _dragStart;
    private int _dragAtom = -1;
    private ChemfigMolecule? _beforeDrag;
    public string Result => "\\chemfig{" + _molecule.ToSource() + "}";
    private string BondKind => _bond.SelectedIndex >= 0 ? ChemfigBondTypes.All[_bond.SelectedIndex].Token : _drawingBondKind;

    public ChemfigEditorWindow(ChemfigMolecule molecule, string engine, string preamble, string? directory)
    {
        _canvas = _editorView.Surface;
        _molecule = molecule.Clone(); _engine = engine; _preamble = preamble; _directory = directory; _settings = SettingsStore.Current.Clone();
        _pairStyle.SelectedIndex = _settings.DefaultLonePairStyle == "Line" ? 0 : 1;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        NativeWindowTheme.Attach(this);
        Title = "Chemfig — visual molecule editor"; Width = 1080; Height = 700; MinWidth = 900; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        root.SetResourceReference(Panel.BackgroundProperty, "WindowBrush");
        _canvas.SetResourceReference(Panel.BackgroundProperty, "WindowBrush");
        _preview.Margin = new Thickness(0); _previewView.Surface.Children.Add(_preview);
        _source.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        _source.SetResourceReference(Control.ForegroundProperty, "TextBrush");
        var tools = BuildToolbar(); DockPanel.SetDock(tools, Dock.Top); root.Children.Add(tools);
        var gridBar = BuildGridBar(); DockPanel.SetDock(gridBar, Dock.Bottom); root.Children.Add(gridBar);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        _warningPanel.Content = _chemistryWarnings; bottom.Children.Add(_warningPanel);
        bottom.Children.Add(_status); bottom.Children.Add(_source);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true }; actions.Children.Add(cancel);
        _apply.Click += (_, _) => { _ = Result; DialogResult = true; }; actions.Children.Add(_apply); bottom.Children.Add(actions);
        var area = new Grid(); area.ColumnDefinitions.Add(new() { Width = new GridLength(42) }); area.ColumnDefinitions.Add(new()); area.ColumnDefinitions.Add(new() { Width = new GridLength(8) }); area.ColumnDefinitions.Add(new()); root.Children.Add(area);
        area.Children.Add(BuildPalette());
        Grid Column(string title, UIElement child)
        {
            var panel = new Grid(); panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new());
            panel.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 8) }); Grid.SetRow(child, 1); panel.Children.Add(child); return panel;
        }
        var editable = Column("Structure · Ctrl+click: multiple selection", _editorView); Grid.SetColumn(editable, 1); area.Children.Add(editable);
        var exact = Column("TeX preview · vector artwork", _previewView); Grid.SetColumn(exact, 3); area.Children.Add(exact);
        _canvas.Focusable = true;
        _gridMode.SelectionChanged += (_, _) => { _hexOrientation.IsEnabled = !Cartesian; Draw(); };
        _hexOrientation.SelectionChanged += (_, _) => Draw();
        _editorView.ViewChanged += Draw;
        _previewView.ViewChanged += UpdatePreviewZoom;
        _editorView.FitRequested += FitEditor;
        _previewView.FitRequested += FitPreview;
        _canvas.MouseLeftButtonDown += Down; _canvas.MouseMove += Move; _canvas.MouseLeftButtonUp += Up;
        _canvas.LostMouseCapture += (_, _) =>
        {
            if (_dragStart is not null) CancelGesture();
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Keyboard.FocusedElement is ComboBox or ComboBoxItem or RadioButton) return;
            if (HandleEditorKey(e.Key, Keyboard.Modifiers, Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)) e.Handled = true;
        };
        _timer.Tick += async (_, _) => { _timer.Stop(); await PreviewAsync(); };
        Loaded += (_, _) => Changed();
        Closed += (_, _) => { _generation++; _timer.Stop(); _cancellation?.Cancel(); _cancellation?.Dispose(); };
    }
    private Point Origin => _editorView.Origin;
    private double UnitScale => 65 * _editorView.Scale;
    private Point Screen(Point p) => new(Origin.X + p.X * UnitScale, Origin.Y - p.Y * UnitScale);
    private Point Local(Point p) => new((p.X - Origin.X) / UnitScale, (Origin.Y - p.Y) / UnitScale);
    private void UpdatePreviewZoom()
    {
        if (_preview.Source is not { } image) return;
        var scale = _previewView.Scale;
        _preview.Width = image.Width * scale; _preview.Height = image.Height * scale;
        Canvas.SetLeft(_preview, _previewView.Origin.X - _preview.Width / 2); Canvas.SetTop(_preview, _previewView.Origin.Y - _preview.Height / 2);
    }
    private void FitPreview() { if (_preview.Source is { } image) _previewView.Fit(new Rect(-image.Width / 2, -image.Height / 2, image.Width, image.Height)); }
    private void FitEditor()
    {
        var bounds = Rect.Empty;
        foreach (var atom in _molecule.Atoms) bounds.Union(new Point(atom.Position.X * 65, -atom.Position.Y * 65));
        if (bounds.IsEmpty) return;
        bounds.Inflate(25, 25); _editorView.Fit(bounds);
    }
    private bool Cartesian => _gridMode.SelectedIndex == 1;
    private double HexOrientation => _hexOrientation.SelectedIndex == 0 ? 30 : 0;
    private bool Snapping(bool bypass) => _snap.IsChecked == true && !bypass;
    private FrameworkElement BuildPalette()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "······", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2), ToolTip = "Atoms, rings and groups" });
        var atoms = new StackPanel(); panel.Children.Add(atoms);
        foreach (var symbol in new[] { "C", "H", "N", "O", "S", "P" })
        {
            var button = new Button { Content = symbol, Height = 27, Style = (Style)FindResource("ChemfigRailButton"), ToolTip = Help(symbol, "Use " + symbol + " for selected and new atoms.") };
            AutomationProperties.SetName(button, "Atom " + symbol);
            button.Click += (_, _) => ChooseAtom(symbol); atoms.Children.Add(button);
        }
        var moreAtoms = PaletteMenu("More atoms", new[] { "F", "Cl", "Br", "I", "B", "Si", "Na", "K", "Mg", "Ca", "Fe", "Cu", "Zn" }.Select(symbol => (symbol, (Action)(() => ChooseAtom(symbol)))));
        moreAtoms.Content = "⋯"; moreAtoms.HorizontalContentAlignment = HorizontalAlignment.Center; moreAtoms.ToolTip = Help("More atoms", "Choose another element for selected and new atoms.");
        moreAtoms.ContextMenu!.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
        moreAtoms.ContextMenu.PlacementTarget = moreAtoms;
        moreAtoms.Style = (Style)FindResource("ChemfigRailButton"); moreAtoms.Height = 27;
        AddMenuIcons(moreAtoms.ContextMenu, "atom");
        panel.Children.Add(RailFlyout(moreAtoms, moreAtoms.ContextMenu));
        var skeletal = new Button(); IconControl(skeletal, "skeletal", "Skeletal carbon", "Remove the atom label; carbon is implied by the bonds.");
        skeletal.HorizontalAlignment = HorizontalAlignment.Center;
        CompactRailButton(skeletal);
        skeletal.Click += (_, _) => ChooseAtom(""); panel.Children.Add(skeletal);
        panel.Children.Add(PaletteSplit("6-ring", () => AddRing(6, false), new[] { 3, 4, 5, 6, 7, 8 }.Select(size => (size + "-ring", (Action)(() => AddRing(size, false)))).Append(("Benzene", (Action)(() => AddRing(6, true)))), "More rings", true));
        panel.Children.Add(PaletteSplit("OH", () => ChooseAtom("OH"), new[] { "OH", "NH_2", "CH_3", "COOH" }.Select(label => (label, (Action)(() => ChooseAtom(label)))), "More groups", true));
        var custom = new Button(); IconControl(custom, "edit", "Custom atom or group", "Enter a custom label, such as NH_2 or CH_3.");
        custom.HorizontalAlignment = HorizontalAlignment.Center;
        CompactRailButton(custom);
        var customPanel = new StackPanel { Width = 195 };
        customPanel.Children.Add(new TextBlock { Text = "Custom atom / group", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        _label.Width = double.NaN; customPanel.Children.Add(_label);
        var customPopup = PalettePopup(custom, customPanel, System.Windows.Controls.Primitives.PlacementMode.Right);
        var apply = new Button { Content = "Use label" }; apply.Click += (_, _) => { ChooseAtom(_label.Text); customPopup.IsOpen = false; }; customPanel.Children.Add(apply);
        panel.Children.Add(custom);
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var rail = new Border { Child = scroll, Margin = new Thickness(0, 0, 6, 0), BorderThickness = new Thickness(0, 0, 1, 0) };
        rail.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        rail.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); AutomationProperties.SetName(rail, "Atom rail"); return rail;
    }
    internal void ChooseAtom(string label)
    {
        if (!ChemfigMolecule.ValidLabel(label)) { _status.Text = "Use a simple label such as NH_2 or CH_{3}."; return; }
        _newAtomLabel = label; _label.Text = label;
        if (_selectedAtoms.Count > 0)
        {
            SaveUndo();
            foreach (var atom in _selectedAtoms) _molecule.Atoms[atom] = _molecule.Atoms[atom] with { Label = label };
            Changed();
        }
        _canvas.Focus();
    }
    private void ChangeSelectedBonds()
    {
        if (_selectedBonds.Count == 0 || _selectedBonds.All(i => _molecule.Bonds[i].Kind == BondKind)) return;
        SaveUndo(); foreach (var i in _selectedBonds) _molecule.Bonds[i] = _molecule.Bonds[i] with { Kind = BondKind }; Changed(); _canvas.Focus();
    }
    private double PairAngle => new double[] { 90, 0, 270, 180, 45, 135, 225, 315 }[_pairAngle.SelectedIndex];
    internal void EditLonePairs(double? angle, bool asLine, bool remove)
    {
        if (_selectedAtoms.Count == 0) { _status.Text = "Select one or more atoms before editing lone pairs."; return; }
        var edits = new Dictionary<int, IReadOnlyList<ChemLonePair>>();
        foreach (var index in _selectedAtoms)
        {
            var atom = _molecule.Atoms[index];
            var normalized = angle is { } value ? (value % 360 + 360) % 360 : (double?)null;
            var pairs = atom.LonePairs.Where(p => normalized is { } target && Math.Abs(p.Angle - target) > 1e-8).ToList();
            if (!remove && normalized is { } at) pairs.Add(new(at, asLine));
            var result = pairs.OrderBy(p => p.Angle).ToArray();
            if (!result.SequenceEqual(atom.LonePairs.OrderBy(p => p.Angle))) edits[index] = result;
        }
        if (edits.Count == 0) return;
        SaveUndo(); foreach (var (index, pairs) in edits) _molecule.Atoms[index] = _molecule.Atoms[index] with { LonePairs = pairs };
        Changed(); _canvas.Focus();
    }
    private void SyncSelectionPalette()
    {
        if (_bondButtons.Count == 0) return;
        _syncingPalette = true;
        try
        {
            var kinds = _selectedBonds.Select(i => _molecule.Bonds[i].Kind).Distinct().ToArray();
            if (kinds.Length == 1) _drawingBondKind = kinds[0];
            _bond.SelectedIndex = kinds.Length > 1 ? -1 : ChemfigBondTypes.Index(_drawingBondKind);
            for (var i = 0; i < _bondButtons.Count; i++) _bondButtons[i].IsChecked = i == _bond.SelectedIndex;
            _bondSummary.Text = kinds.Length > 1 ? "Mixed bond types" : _selectedBonds.Count > 0 ? $"{_selectedBonds.Count} selected" : "Drawing bond type";
            _pairSummary.Text = _selectedAtoms.Count == 0 ? "Select atom(s)" : string.Join(", ", _selectedAtoms.Select(i => _molecule.Atoms[i].LonePairs.Count).Distinct().Order()) + " pair(s) / atom";
        }
        finally { _syncingPalette = false; }
    }
    internal bool HandleEditorKey(Key key, ModifierKeys modifiers, bool textInput)
    {
        if (textInput) return false;
        if (key == Key.Escape) { CancelGesture(); _selectedAtoms.Clear(); _selectedBonds.Clear(); Draw(); return true; }
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (key == Key.Z) { CancelGesture(); if (modifiers.HasFlag(ModifierKeys.Shift)) Redo(); else Undo(); return true; }
            if (key == Key.Y) { CancelGesture(); Redo(); return true; }
            if (key == Key.A) { _selectedAtoms.UnionWith(Enumerable.Range(0, _molecule.Atoms.Count)); _selectedBonds.UnionWith(Enumerable.Range(0, _molecule.Bonds.Count)); Draw(); return true; }
        }
        if (key is Key.Delete or Key.Back) { CancelGesture(); DeleteSelection(); return true; }
        if (key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var delta = key switch { Key.Left => new Vector(-1, 0), Key.Right => new Vector(1, 0), Key.Up => new Vector(0, Cartesian ? 1 : ChemfigGrid.RowHeight), _ => new Vector(0, Cartesian ? -1 : -ChemfigGrid.RowHeight) };
            var moving = MovingAtoms(); if (moving.Count == 0) return true;
            SaveUndo(); var anchor = _molecule.Atoms[moving.Min()].Position;
            if (Snapping(modifiers.HasFlag(ModifierKeys.Alt))) delta = ChemfigGrid.Snap(anchor + delta, Cartesian, HexOrientation) - anchor;
            foreach (var atom in moving) _molecule.Atoms[atom] = _molecule.Atoms[atom] with { Position = _molecule.Atoms[atom].Position + delta };
            Changed(); return true;
        }
        return false;
    }
    private HashSet<int> MovingAtoms() => _selectedAtoms.Concat(_selectedBonds.SelectMany(i => new[] { _molecule.Bonds[i].A, _molecule.Bonds[i].B })).ToHashSet();
    internal void DeleteSelection()
    {
        if (_selectedAtoms.Count == 0 && _selectedBonds.Count == 0) return;
        SaveUndo(); _molecule = _molecule.Without(_selectedAtoms, _selectedBonds); ResetSelection(); _selectedAtoms.Clear(); Changed();
    }
    private void ResetSelection() { _selected = 0; _selectedAtoms.Clear(); _selectedAtoms.Add(0); _selectedBonds.Clear(); }
    private string PreviewCommand => string.Join("\\quad ", _molecule.Components().Select(c => "\\chemfig{" + c.ToSource() + "}"));
    private int Hit(Point p) => Enumerable.Range(0, _molecule.Atoms.Count).OrderBy(i => (Screen(_molecule.Atoms[i].Position) - p).LengthSquared).FirstOrDefault(i => (Screen(_molecule.Atoms[i].Position) - p).Length < 17, -1);
    private void SaveUndo() { _undo.Push(_molecule.Clone()); _redo.Clear(); }
    private void Undo() { if (!_undo.TryPop(out var prior)) return; _redo.Push(_molecule.Clone()); _molecule = prior; ResetSelection(); Changed(); }
    private void Redo() { if (!_redo.TryPop(out var next)) return; _undo.Push(_molecule.Clone()); _molecule = next; ResetSelection(); Changed(); }
    private void Changed()
    {
        _generation++; _cancellation?.Cancel(); Draw(); _source.Text = PreviewCommand;
        var warnings = ChemfigChemistry.Analyze(_molecule);
        _chemistryWarnings.Text = string.Join("\n", warnings.Select(w => "⚠ " + w.Message));
        _warningPanel.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _apply.IsEnabled = _molecule.Components().Count == 1;
        _label.Text = _molecule.Atoms[_selected].Label;
        _status.Text = _apply.IsEnabled ? "Structure updated. Compiling exact preview…" : "Separate fragments: reconnect them or delete unwanted fragments before applying. Undo is available.";
        _timer.Stop(); _timer.Start();
    }
    private void AddRing(int count, bool alternating)
    {
        SaveUndo(); var ring = ChemfigMolecule.Ring(count, alternating); var anchor = _molecule.Atoms[_selected].Position;
        var offset = _molecule.Atoms.Count; var first = ring.Atoms[0].Position;
        foreach (var atom in ring.Atoms.Skip(1)) _molecule.Atoms.Add(atom with { Position = anchor + (atom.Position - first) });
        int Map(int i) => i == 0 ? _selected : offset + i - 1;
        foreach (var bond in ring.Bonds) _molecule.Bonds.Add(bond with { A = Map(bond.A), B = Map(bond.B) });
        Changed();
    }
    private void Down(object sender, MouseButtonEventArgs e)
    {
        BeginPointer(e.GetPosition(_canvas), Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        if (_dragStart is not null) _canvas.CaptureMouse();
        _canvas.Focus(); e.Handled = true;
    }
    private void Move(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) MovePointer(e.GetPosition(_canvas), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    }
    private void Up(object sender, MouseButtonEventArgs e)
    {
        EndPointer(e.GetPosition(_canvas), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)); e.Handled = true;
    }
    private int HitBond(Point p)
    {
        var closest = -1; var distance = 7d;
        for (var i = 0; i < _molecule.Bonds.Count; i++)
        {
            var b = _molecule.Bonds[i]; var a = Screen(_molecule.Atoms[b.A].Position); var end = Screen(_molecule.Atoms[b.B].Position); var v = end - a;
            if (v.LengthSquared < .001) continue;
            var t = Math.Clamp(Vector.Multiply(p - a, v) / v.LengthSquared, 0, 1); var d = (p - (a + v * t)).Length;
            if (d < distance) { closest = i; distance = d; }
        }
        return closest;
    }
    internal void BeginPointer(Point p, bool control)
    {
        CancelGesture(); var hit = Hit(p); var bond = hit < 0 ? HitBond(p) : -1;
        if (_mode.SelectedIndex == 1 && hit < 0) return;
        if (_mode.SelectedIndex == 0)
        {
            if (hit < 0 && bond < 0)
            {
                _marquee = true; _baseAtoms = control ? [.. _selectedAtoms] : []; _baseBonds = control ? [.. _selectedBonds] : [];
                if (!control) { _selectedAtoms.Clear(); _selectedBonds.Clear(); }
            }
            else
            {
                var collection = hit >= 0 ? _selectedAtoms : _selectedBonds; var index = hit >= 0 ? hit : bond;
                if (control)
                { if (!collection.Add(index)) collection.Remove(index); Draw(); return; }
                if (!collection.Contains(index)) { _selectedAtoms.Clear(); _selectedBonds.Clear(); collection.Add(index); }
            }
        }
        else { _selectedAtoms.Clear(); _selectedAtoms.Add(hit); _selectedBonds.Clear(); }
        if (hit >= 0) _selected = hit;
        _dragAtom = _selected; _dragStart = p; _pointer = p; _beforeDrag = _molecule.Clone();
        _movingAtoms.Clear(); _movingAtoms.UnionWith(MovingAtoms());
        _label.Text = _molecule.Atoms[_selected].Label; Draw();
    }
    internal void MovePointer(Point p, bool bypass)
    {
        if (_dragStart is null) return;
        _pointer = p;
        if (_marquee)
        {
            var rectangle = new Rect(_dragStart.Value, p);
            _selectedAtoms.Clear(); _selectedAtoms.UnionWith(_baseAtoms);
            _selectedBonds.Clear(); _selectedBonds.UnionWith(_baseBonds);
            for (var i = 0; i < _molecule.Atoms.Count; i++) if (rectangle.Contains(Screen(_molecule.Atoms[i].Position))) _selectedAtoms.Add(i);
            for (var i = 0; i < _molecule.Bonds.Count; i++)
            { var b = _molecule.Bonds[i]; if (rectangle.Contains(Screen(_molecule.Atoms[b.A].Position)) && rectangle.Contains(Screen(_molecule.Atoms[b.B].Position))) _selectedBonds.Add(i); }
        }
        else if (_mode.SelectedIndex == 0 && (p - _dragStart.Value).Length > 3 && _movingAtoms.Count > 0)
        {
            var delta = Local(p) - Local(_dragStart.Value); var anchor = _beforeDrag!.Atoms[_movingAtoms.Min()].Position;
            if (Snapping(bypass)) delta = ChemfigGrid.Snap(anchor + delta, Cartesian, HexOrientation) - anchor;
            foreach (var atom in _movingAtoms) _molecule.Atoms[atom] = _beforeDrag!.Atoms[atom] with { Position = _beforeDrag.Atoms[atom].Position + delta };
        }
        Draw();
    }
    internal void EndPointer(Point p, bool bypass)
    {
        if (_dragStart is null) return;
        MovePointer(p, bypass);
        var changed = !_marquee && (p - _dragStart.Value).Length > 3;
        if (_mode.SelectedIndex == 1 && changed)
        {
            var target = Hit(p);
            if (target != _dragAtom)
            {
                if (target < 0)
                {
                    var origin = _molecule.Atoms[_dragAtom].Position;
                    var endpoint = ChemfigGrid.BondEnd(origin, Local(p), Cartesian, Snapping(bypass), HexOrientation);
                    target = _molecule.Atoms.FindIndex(a => (a.Position - endpoint).Length < 1e-6);
                    if (target < 0) { target = _molecule.Atoms.Count; _molecule.Atoms.Add(new(_newAtomLabel, endpoint)); }
                }
                var existing = _molecule.Bonds.FindIndex(b => b.A == _dragAtom && b.B == target || b.B == _dragAtom && b.A == target);
                if (existing >= 0) _molecule.Bonds[existing] = new(_dragAtom, target, BondKind);
                else _molecule.Bonds.Add(new(_dragAtom, target, BondKind));
                _selected = target;
                _selectedAtoms.Clear(); _selectedAtoms.Add(target); _selectedBonds.Clear();
            }
            else changed = false;
        }
        var before = _beforeDrag!; _dragStart = null; _beforeDrag = null; _pointer = null; _marquee = false; _canvas.ReleaseMouseCapture();
        if (_mode.SelectedIndex == 0) changed = before.Atoms.Where((a, i) => a != _molecule.Atoms[i]).Any();
        if (_selectedAtoms.Count > 0 && !_selectedAtoms.Contains(_selected)) _selected = _selectedAtoms.Min();
        if (changed) { _undo.Push(before); _redo.Clear(); Changed(); } else Draw();
    }
    private void CancelGesture()
    {
        if (_dragStart is not null && _beforeDrag is not null) _molecule = _beforeDrag;
        _dragStart = null; _beforeDrag = null; _pointer = null; _marquee = false; _canvas.ReleaseMouseCapture(); Draw();
    }
    private void Draw()
    {
        SyncSelectionPalette();
        var ink = (Brush)FindResource("TextBrush");
        var background = (Brush)FindResource("WindowBrush");
        var selection = (Brush)FindResource("ChemfigSelectionBrush");
        var zoom = _editorView.Scale;
        _canvas.Children.Clear();
        if (_gridMode.SelectedIndex != 2 && _canvas.ActualWidth > 0 && _canvas.ActualHeight > 0)
        {
            _canvas.Children.Add(new Rectangle { Width = _canvas.ActualWidth, Height = _canvas.ActualHeight, IsHitTestVisible = false, Tag = "chemfig-grid",
                Fill = ChemfigGrid.Brush(new Size(_canvas.ActualWidth, _canvas.ActualHeight), Origin, UnitScale, Cartesian, Color.FromArgb(45, 145, 155, 175), Color.FromArgb(100, 145, 155, 175), HexOrientation) });
        }
        for (var bondIndex = 0; bondIndex < _molecule.Bonds.Count; bondIndex++)
        {
            var bond = _molecule.Bonds[bondIndex]; var bondInk = _selectedBonds.Contains(bondIndex) ? selection : ink;
            var a = Screen(_molecule.Atoms[bond.A].Position); var b = Screen(_molecule.Atoms[bond.B].Position);
            var delta = b - a; if (delta.Length < 0.01) continue;
            var normal = new Vector(-delta.Y, delta.X); normal.Normalize();
            if (bond.Kind.StartsWith('>') || bond.Kind.StartsWith('<'))
            {
                var tip = bond.Kind[0] == '>' ? b : a; var end = bond.Kind[0] == '>' ? a : b;
                if (bond.Kind.EndsWith(':'))
                {
                    for (var dash = 1; dash <= 8; dash++)
                    {
                        var t = dash / 8d; var center = tip + (end - tip) * t; var half = normal * (5 * zoom * t);
                        _canvas.Children.Add(new Line { X1 = center.X - half.X, Y1 = center.Y - half.Y, X2 = center.X + half.X, Y2 = center.Y + half.Y, Stroke = bondInk, StrokeThickness = 1.3 * zoom, IsHitTestVisible = false, Tag = "cram-hash" });
                    }
                }
                else
                {
                    var hollow = bond.Kind.EndsWith('|');
                    _canvas.Children.Add(new Polygon { Points = new PointCollection { tip, end + normal * (5 * zoom), end - normal * (5 * zoom) }, Fill = hollow ? background : bondInk, Stroke = hollow ? bondInk : null, StrokeThickness = 1.2 * zoom, IsHitTestVisible = false, Tag = hollow ? "cram-hollow" : "cram-solid" });
                }
            }
            else
            {
                var count = bond.Kind == "=" ? 2 : bond.Kind == "~" ? 3 : 1;
                for (var i = 0; i < count; i++)
                { var shift = normal * ((i - (count - 1) / 2d) * 4 * zoom); _canvas.Children.Add(new Line { X1 = a.X + shift.X, Y1 = a.Y + shift.Y, X2 = b.X + shift.X, Y2 = b.Y + shift.Y, Stroke = bondInk, StrokeThickness = 1.5 * zoom, IsHitTestVisible = false }); }
            }
        }
        for (var i = 0; i < _molecule.Atoms.Count; i++)
        {
            var atom = _molecule.Atoms[i]; var p = Screen(atom.Position);
            var label = new TextBlock { Text = atom.Label.Length == 0 ? "·" : atom.Label, FontSize = 19 * zoom, Foreground = ink, Background = background, Padding = new Thickness(2), IsHitTestVisible = false };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); Canvas.SetLeft(label, p.X - label.DesiredSize.Width / 2); Canvas.SetTop(label, p.Y - label.DesiredSize.Height / 2); _canvas.Children.Add(label);
            foreach (var pair in atom.LonePairs)
            {
                var radians = pair.Angle * Math.PI / 180; var radial = new Vector(Math.Cos(radians), -Math.Sin(radians)); var tangent = new Vector(-radial.Y, radial.X);
                var radius = Math.Sqrt(Math.Pow(radial.X * label.DesiredSize.Width / 2, 2) + Math.Pow(radial.Y * label.DesiredSize.Height / 2, 2)) + 5 * zoom;
                var center = p + radial * radius;
                if (pair.AsLine)
                {
                    var half = tangent * (4 * zoom);
                    _canvas.Children.Add(new Line { X1 = center.X - half.X, Y1 = center.Y - half.Y, X2 = center.X + half.X, Y2 = center.Y + half.Y, Stroke = ink, StrokeThickness = 1.4 * zoom, IsHitTestVisible = false, Tag = "lone-pair" });
                }
                else foreach (var offset in new[] { -2.6, 2.6 })
                {
                    var dotPosition = center + tangent * (offset * zoom); var dot = new Ellipse { Width = 2.5 * zoom, Height = 2.5 * zoom, Fill = ink, IsHitTestVisible = false, Tag = "lone-pair" };
                    Canvas.SetLeft(dot, dotPosition.X - dot.Width / 2); Canvas.SetTop(dot, dotPosition.Y - dot.Height / 2); _canvas.Children.Add(dot);
                }
            }
            if (_selectedAtoms.Contains(i))
            { var marker = new Ellipse { Width = 29, Height = 29, Stroke = selection, StrokeThickness = 1, IsHitTestVisible = false }; Canvas.SetLeft(marker, p.X - 14.5); Canvas.SetTop(marker, p.Y - 14.5); _canvas.Children.Add(marker); }
        }
        if (_dragStart is { } start && _pointer is { } pointer)
        {
            if (_marquee)
            {
                var bounds = new Rect(start, pointer); var box = new Rectangle { Width = bounds.Width, Height = bounds.Height, Stroke = selection, StrokeThickness = 1, Fill = new SolidColorBrush(Color.FromArgb(25, 131, 176, 255)), IsHitTestVisible = false };
                Canvas.SetLeft(box, bounds.Left); Canvas.SetTop(box, bounds.Top); _canvas.Children.Add(box);
            }
            else if (_mode.SelectedIndex == 1)
            {
                var a = Screen(_molecule.Atoms[_dragAtom].Position);
                var b = Screen(ChemfigGrid.BondEnd(_molecule.Atoms[_dragAtom].Position, Local(pointer), Cartesian, Snapping(Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)), HexOrientation));
                _canvas.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Stroke = selection, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 }, IsHitTestVisible = false });
            }
        }
    }
    private async Task PreviewAsync()
    {
        var generation = _generation;
        var source = BuildPreviewSource(PreviewCommand, ((SolidColorBrush)FindResource("TextBrush")).Color);
        _cancellation?.Dispose(); _cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.CompilerTimeoutSeconds)); var token = _cancellation.Token;
        try
        {
            var result = await Task.Run(() => TexVectorRenderer.RenderAsync(source, _engine, _directory, _preamble, token, _settings), token);
            if (generation != _generation) return;
            if (result.ExitCode != 0) { _status.Text = "Chemfig compilation failed. Check that the chemfig package is installed. " + result.Log[^Math.Min(result.Log.Length, 700)..]; return; }
            var first = _preview.Source is null; _preview.Source = result.Image; if (first) FitPreview(); else UpdatePreviewZoom(); _status.Text = _apply.IsEnabled ? "Vector TeX preview ready · zoom without recompiling. Apply is one undo step." : "Separate fragments: reconnect them or delete unwanted fragments before applying. Undo is available.";
        }
        catch (OperationCanceledException) { if (generation == _generation) _status.Text = "Chemfig preview timed out. Check the compiler installation or increase its timeout in Settings."; }
        catch (Exception exception) { if (generation == _generation) _status.Text = exception.Message; }
    }
    // Apply the theme only to this temporary preview job, never to Result or exports.
    internal static string BuildPreviewSource(string command, Color ink) =>
        $"\\begingroup\n\\color[RGB]{{{ink.R},{ink.G},{ink.B}}}\n\\begin{{tikzpicture}}\n\\node[inner sep=0pt] at (0,0) {{{command}}};\n\\end{{tikzpicture}}\n\\endgroup";
}
