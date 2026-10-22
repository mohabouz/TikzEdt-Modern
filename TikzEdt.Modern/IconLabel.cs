using System.Windows;
using System.Windows.Controls;

namespace TikzEdt.Modern;

public sealed class IconLabel : StackPanel
{
    private readonly ToolIcon _icon = new();
    private readonly TextBlock _label = new() { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(IconLabel), new PropertyMetadata("path", Changed));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(IconLabel), new PropertyMetadata("", Changed));
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IconLabel() { Orientation = Orientation.Horizontal; Children.Add(_icon); Children.Add(_label); }
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var label = (IconLabel)target;
        label._icon.Kind = label.Icon;
        label._label.Text = label.Text;
        label._label.Visibility = string.IsNullOrEmpty(label.Text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
