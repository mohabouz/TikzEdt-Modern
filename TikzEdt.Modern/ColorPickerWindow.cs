using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TikzEdt.Modern;

internal sealed class ColorPickerWindow : Window
{
    private readonly Slider[] _channels = Enumerable.Range(0, 3).Select(_ => new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(8, 3, 0, 3) }).ToArray();
    private readonly TextBlock[] _values = Enumerable.Range(0, 3).Select(_ => new TextBlock { Width = 35, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right }).ToArray();
    private readonly TextBox _hex = new() { Margin = new Thickness(0, 8, 0, 8), Padding = new Thickness(7), ToolTip = "Hexadecimal RGB color: #RRGGBB" };
    private readonly TextBox _name = new() { Text = "diagramcolor", Padding = new Thickness(7), ToolTip = "Name for the optional LaTeX color definition" };
    private readonly Border _swatch = new() { Height = 65, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
    private readonly TextBlock _error = new() { Foreground = Brushes.Salmon, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private bool _updating;
    public bool DefineNamedColor { get; private set; }
    public string ColorName => _name.Text.Trim();
    public string RgbValues => string.Join(",", _channels.Select(channel => ((int)channel.Value).ToString(CultureInfo.InvariantCulture)));
    public string TikzColor => $"{{rgb,255:red,{(int)_channels[0].Value};green,{(int)_channels[1].Value};blue,{(int)_channels[2].Value}}}";
    public ColorPickerWindow()
    {
        Title = "Color picker"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource(typeof(Window)); NativeWindowTheme.Attach(this);
        var panel = new StackPanel { Margin = new Thickness(18) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Choose a color", FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) }); panel.Children.Add(_swatch);
        var palette = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(palette);
        foreach (var color in new[] { "#000000", "#FFFFFF", "#3574F0", "#DC3545", "#F97316", "#EAB308", "#22C55E", "#06B6D4", "#8B5CF6", "#EC4899", "#6B7280", "#92400E" })
        {
            var button = new Button { Width = 30, Height = 28, Margin = new Thickness(1), Padding = new Thickness(0), Content = new Border { Background = (Brush)new BrushConverter().ConvertFromInvariantString(color)!, Width = 22, Height = 20 }, ToolTip = color };
            button.Click += (_, _) => SetColor((Color)ColorConverter.ConvertFromString(color)); palette.Children.Add(button);
        }
        for (var index = 0; index < 3; index++)
        {
            var row = new DockPanel(); var label = new TextBlock { Text = new[] { "Red", "Green", "Blue" }[index], Width = 42, VerticalAlignment = VerticalAlignment.Center }; row.Children.Add(label);
            DockPanel.SetDock(_values[index], Dock.Right); row.Children.Add(_values[index]); row.Children.Add(_channels[index]); panel.Children.Add(row);
            _channels[index].ValueChanged += (_, _) => UpdateColor();
        }
        panel.Children.Add(_hex); panel.Children.Add(new TextBlock { Text = "Optional color name", FontSize = 11 }); panel.Children.Add(_name); panel.Children.Add(_error);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(actions);
        var cancel = new Button { Content = "Cancel", IsCancel = true }; actions.Children.Add(cancel);
        var define = new Button { Content = "Define color" }; actions.Children.Add(define);
        var insert = new Button { Content = "Insert color", IsDefault = true }; actions.Children.Add(insert);
        insert.Click += (_, _) => Finish(false); define.Click += (_, _) => Finish(true);
        _hex.TextChanged += (_, _) =>
        {
            if (_updating) return;
            if (!Regex.IsMatch(_hex.Text.Trim(), "^#[0-9a-fA-F]{6}$")) { _error.Text = "Enter a color as #RRGGBB."; return; }
            SetColor((Color)ColorConverter.ConvertFromString(_hex.Text.Trim()));
        };
        SetColor(Color.FromRgb(53, 116, 240));
    }
    private void Finish(bool define)
    {
        if (!Regex.IsMatch(_hex.Text.Trim(), "^#[0-9a-fA-F]{6}$")) { _error.Text = "Enter a valid #RRGGBB color first."; return; }
        if (define && !Regex.IsMatch(ColorName, "^[A-Za-z][A-Za-z0-9_-]*$")) { _error.Text = "Use a color name starting with a letter."; return; }
        DefineNamedColor = define; DialogResult = true;
    }
    private void SetColor(Color color)
    {
        _updating = true; _channels[0].Value = color.R; _channels[1].Value = color.G; _channels[2].Value = color.B; _updating = false; UpdateColor();
    }
    private void UpdateColor()
    {
        if (_updating) return;
        _updating = true;
        var color = Color.FromRgb((byte)_channels[0].Value, (byte)_channels[1].Value, (byte)_channels[2].Value);
        _swatch.Background = new SolidColorBrush(color); _hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        for (var index = 0; index < 3; index++) _values[index].Text = ((int)_channels[index].Value).ToString(CultureInfo.InvariantCulture);
        _error.Text = ""; _updating = false;
    }
}
