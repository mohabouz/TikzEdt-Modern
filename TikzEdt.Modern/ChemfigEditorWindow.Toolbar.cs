using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace TikzEdt.Modern;

internal sealed partial class ChemfigEditorWindow
{
    private readonly ToggleButton _panBoth = new();
    private Popup? _pairPopup;

    private static ToolTip Help(string title, string explanation) => new()
    {
        Content = new StackPanel { Children = {
            new TextBlock { Text = title, FontWeight = FontWeights.Bold },
            new TextBlock { Text = explanation, MaxWidth = 260, TextWrapping = TextWrapping.Wrap }
        } }
    };

    private static void IconControl(ContentControl control, string icon, string title, string explanation)
    {
        control.Content = new ToolIcon { Kind = icon };
        control.Width = control.Height = 34;
        control.Padding = new Thickness(6); control.Margin = new Thickness(2);
        control.ToolTip = Help(title, explanation);
        AutomationProperties.SetName(control, title);
    }

    private FrameworkElement BuildToolbar()
    {
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        StackPanel Group(string name)
        {
            var items = new StackPanel { Orientation = Orientation.Horizontal };
            var border = new Border { Child = items, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(0, 0, 7, 0), Margin = new Thickness(0, 0, 7, 0) };
            border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            AutomationProperties.SetName(border, name); toolbar.Children.Add(border); return items;
        }
        var actions = Group("Actions");
        var toolChoices = new List<RadioButton>();
        foreach (var (name, icon, index) in new[] { ("Select", "select", 0), ("Draw bond", "path", 1) })
        {
            var choice = new RadioButton { GroupName = "ChemfigTool", Style = (Style)FindResource("ToolButton") };
            IconControl(choice, icon, name, index == 0 ? "Select or move atoms and bonds. Ctrl+click adds to the selection." : "Drag from an atom to create a bond using the chosen bond type.");
            choice.Checked += (_, _) => { CancelGesture(); _mode.SelectedIndex = index; _panBoth.IsChecked = false; };
            choice.IsChecked = index == 0; actions.Children.Add(choice); toolChoices.Add(choice);
        }
        void ActionButton(Panel panel, string icon, string name, string help, Action action)
        {
            var button = new Button(); IconControl(button, icon, name, help);
            button.Click += (_, _) => action(); panel.Children.Add(button);
        }
        ActionButton(actions, "undo", "Undo", "Undo the last molecule edit (Ctrl+Z).", Undo);
        ActionButton(actions, "redo", "Redo", "Redo the last undone edit (Ctrl+Y).", Redo);
        ActionButton(actions, "delete", "Delete", "Delete the selected atoms and bonds (Delete).", DeleteSelection);

        var bonds = Group("Bond types");
        for (var i = 0; i < ChemfigBondTypes.All.Count; i++)
        {
            var index = i; var type = ChemfigBondTypes.All[i];
            var button = new RadioButton { Content = new Viewbox { Child = ChemfigBondGlyph.Create(type.Token), Width = 28, Height = 22 }, GroupName = "ChemfigBondPalette", IsChecked = i == _bond.SelectedIndex, Style = (Style)FindResource("ToolButton"), Width = 38, Height = 34, Margin = new Thickness(1), Padding = new Thickness(3), ToolTip = Help(type.Caption, type.Description) };
            AutomationProperties.SetName(button, type.Caption);
            button.Checked += (_, _) => { if (_syncingPalette) return; _bond.SelectedIndex = index; _drawingBondKind = type.Token; ChangeSelectedBonds(); };
            bonds.Children.Add(button); _bondButtons.Add(button);
        }
        bonds.ToolTip = _bondSummary;
        var pairs = Group("Lone pairs");
        ActionButton(pairs, "pair", "Add lone pair", "Add or update a non-bonding pair on selected atoms. Use the arrow to choose its angle and line/dot style.", () => EditLonePairs(PairAngle, _pairStyle.SelectedIndex == 0, false));
        var pairOptions = new Button { Content = "▾", Width = 22, Height = 34, Padding = new Thickness(2), Margin = new Thickness(0, 2, 2, 2), ToolTip = Help("Lone-pair options", "Choose the angle and line/dot representation, or remove electron pairs.") };
        AutomationProperties.SetName(pairOptions, "Lone-pair options"); pairs.Children.Add(pairOptions);
        var pairPanel = new StackPanel { Width = 220 };
        pairPanel.Children.Add(new TextBlock { Text = "Lone pairs", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
        pairPanel.Children.Add(new TextBlock { Text = "Position" }); pairPanel.Children.Add(_pairAngle);
        pairPanel.Children.Add(new TextBlock { Text = "Representation", Margin = new Thickness(0, 8, 0, 0) }); pairPanel.Children.Add(_pairStyle);
        pairPanel.Children.Add(_pairSummary);
        _pairPopup = PalettePopup(pairOptions, pairPanel, PlacementMode.Bottom);
        void PairAction(string title, Action action)
        {
            var button = new Button { Content = title }; AutomationProperties.SetName(button, title);
            button.Click += (_, _) => { action(); _pairPopup.IsOpen = false; }; pairPanel.Children.Add(button);
        }
        PairAction("Add / update pair", () => EditLonePairs(PairAngle, _pairStyle.SelectedIndex == 0, false));
        PairAction("Remove at angle", () => EditLonePairs(PairAngle, false, true));
        PairAction("Clear lone pairs", () => EditLonePairs(null, false, true));
        var navigation = Group("Shared navigation");
        IconControl(_panBoth, "pan", "Pan both panes", "Enable hand panning in the structure and preview. Drag either pane independently. Space+drag or middle-drag also pans.");
        void Pan(bool enabled)
        {
            CancelGesture(); _editorView.HandButton.IsChecked = _previewView.HandButton.IsChecked = enabled;
            for (var i = 0; i < toolChoices.Count; i++) toolChoices[i].IsChecked = !enabled && i == _mode.SelectedIndex;
        }
        _panBoth.Checked += (_, _) => Pan(true); _panBoth.Unchecked += (_, _) => Pan(false);
        navigation.Children.Add(_panBoth);
        ActionButton(navigation, "fit", "Fit both panes", "Fit the molecule in the structure and the TeX artwork in the preview.", () => { FitEditor(); FitPreview(); });
        foreach (var view in new[] { _editorView, _previewView })
        { view.HandButton.Visibility = Visibility.Collapsed; view.FitButton.Visibility = Visibility.Collapsed; }
        return toolbar;
    }

    private FrameworkElement BuildGridBar()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "Grid", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) });
        row.Children.Add(_gridMode); row.Children.Add(_hexOrientation); row.Children.Add(_snap);
        row.Children.Add(new TextBlock { Text = "1 step = 1 bond · Alt: bypass snap", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        row.ToolTip = "Construction grid uses the Chemfig bond length (default atom sep = 3em).";
        var border = new Border { Child = row, Padding = new Thickness(4), Margin = new Thickness(0, 8, 0, 0) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush");
        AutomationProperties.SetName(border, "Construction grid"); return border;
    }

    private static Button PaletteMenu(string title, IEnumerable<(string Label, Action Run)> choices)
    {
        var button = new Button { Content = title + " ▾", Padding = new Thickness(5), HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, title);
        var menu = new ContextMenu { Placement = PlacementMode.Bottom, PlacementTarget = button };
        foreach (var (label, run) in choices)
        { var item = new MenuItem { Header = label }; item.Click += (_, _) => run(); menu.Items.Add(item); }
        button.ContextMenu = menu; button.Click += (_, _) => menu.IsOpen = true; return button;
    }

    private Popup PalettePopup(Button anchor, FrameworkElement content, PlacementMode placement)
    {
        var border = new Border { Child = content, Padding = new Thickness(12), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush"); border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        border.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextBrush");
        var popup = new Popup { Child = border, PlacementTarget = anchor, Placement = placement, StaysOpen = false, AllowsTransparency = true };
        anchor.Click += (_, _) => { popup.IsOpen = !popup.IsOpen; if (popup.IsOpen) content.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.First)); };
        // Keep keyboard dismissal consistent with menus without leaking Escape to the molecule selection.
        border.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { popup.IsOpen = false; anchor.Focus(); e.Handled = true; } };
        Closed += (_, _) => popup.IsOpen = false;
        return popup;
    }

    private static Grid PaletteSplit(string title, Action primary, IEnumerable<(string Label, Action Run)> choices, string moreTitle, bool compact = false)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var main = new Button { Content = title, Padding = new Thickness(5), ToolTip = Help(title, "Apply the chosen item. Use the arrow for alternatives.") };
        AutomationProperties.SetName(main, title);
        if (compact) { main.Padding = new Thickness(2); main.Margin = new Thickness(1); main.Height = 30; }
        object Caption(string label) => compact ? new ToolIcon { Kind = moreTitle == "More rings" ? "ring" : "group" } : label;
        main.Content = Caption(title);
        if (compact)
        {
            CompactRailButton(main);
            var menu = new ContextMenu();
            foreach (var (label, run) in choices)
            {
                var item = new MenuItem { Header = label };
                item.Click += (_, _) => { main.Content = Caption(label); AutomationProperties.SetName(main, label); main.ToolTip = Help(label, "Click to apply. Use the corner arrow or right-click for alternatives."); primary = run; run(); };
                menu.Items.Add(item);
            }
            AddMenuIcons(menu, moreTitle == "More rings" ? "ring" : "group");
            main.Click += (_, _) => primary();
            return RailFlyout(main, menu, moreTitle);
        }
        main.Click += (_, _) => primary(); row.Children.Add(main);
        var more = PaletteMenu(moreTitle, choices.Select(c => (c.Label, (Action)(() => { main.Content = Caption(c.Label); AutomationProperties.SetName(main, c.Label); main.ToolTip = Help(c.Label, "Apply the chosen item. Use the arrow for alternatives."); primary = c.Run; c.Run(); }))));
        more.Content = "▾"; more.Width = compact ? 15 : 26; more.Padding = new Thickness(1); more.ToolTip = moreTitle;
        if (compact) { more.Margin = new Thickness(1); more.Height = 30; }
        Grid.SetColumn(more, 1); row.Children.Add(more); return row;
    }

    private static void CompactRailButton(Button button)
    {
        button.Style = (Style)Application.Current.FindResource("ChemfigRailButton");
        button.Width = 34; button.Height = 28; button.Padding = new Thickness(3); button.Margin = new Thickness(0);
    }

    private static void AddMenuIcons(ContextMenu menu, string kind)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
            item.Icon = kind == "atom" ? null : new ToolIcon { Kind = kind };
    }

    // One tool tile with an unobtrusive corner disclosure, rather than two separate buttons.
    private static Grid RailFlyout(Button main, ContextMenu menu, string? arrowName = null)
    {
        CompactRailButton(main);
        var tile = new Grid { Width = 34, Height = 28, HorizontalAlignment = HorizontalAlignment.Center };
        tile.Children.Add(main); main.ContextMenu = menu;
        menu.PlacementTarget = main; menu.Placement = PlacementMode.Right;
        var triangle = new System.Windows.Shapes.Path { Data = System.Windows.Media.Geometry.Parse("M0,4 L4,4 4,0 Z"), Width = 4, Height = 4, IsHitTestVisible = false };
        triangle.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedBrush");
        var arrow = new Button { Content = triangle, Width = 11, Height = 11, Padding = new Thickness(1), Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Style = (Style)Application.Current.FindResource("ChemfigRailButton"), ToolTip = "More choices · right-click the tool", ContextMenu = menu };
        AutomationProperties.SetName(arrow, arrowName ?? AutomationProperties.GetName(main) + " options");
        arrow.Click += (_, _) => menu.IsOpen = true;
        tile.Children.Add(arrow);
        main.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Down && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt))
            { menu.IsOpen = true; e.Handled = true; }
        };
        return tile;
    }
}
