using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

internal sealed class SnippetFileWindow : Window
{
    private readonly TextBox _editor = new() { AcceptsReturn = true, AcceptsTab = true, FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    public string Result => _editor.Text;
    public SnippetFileWindow(string path, string text)
    {
        NativeWindowTheme.Attach(this); Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "Edit user snippets"; Width = 850; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var help = new TextBlock { Text = path + "\nJSON array: name, prefix, body, description, context (any/math/text). Use $1 or ${1:default}; repeated numbers are linked; $0 ends the snippet; ${SELECTION} wraps selected text. Tab visits fields numerically. Regex triggers, nested fields and transforms are not supported.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(help, Dock.Top); root.Children.Add(help);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; bottom.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; bottom.Children.Add(buttons);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true }); var save = new Button { Content = "Validate and save" }; buttons.Children.Add(save);
        save.Click += (_, _) => { try { UserSnippets.Parse(Result); DialogResult = true; } catch (FormatException exception) { error.Text = exception.Message; } };
        _editor.Text = text; root.Children.Add(_editor);
    }
}
