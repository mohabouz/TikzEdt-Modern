using System.Windows;
using System.Windows.Controls;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

internal sealed class DocumentCopiesWindow : Window
{
    public DocumentCopy? Selected { get; private set; }
    public DocumentCopiesWindow(string title, IReadOnlyList<DocumentCopy> copies)
    {
        NativeWindowTheme.Attach(this); Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = title; Width = 850; Height = 600; MinWidth = 580; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var restore = new Button { Content = "Restore into editor", IsEnabled = false }; footer.Children.Add(new Button { Content = "Cancel", IsCancel = true }); footer.Children.Add(restore);
        var list = new ListBox { Style = (Style)Application.Current.FindResource("WorkflowList"), MaxHeight = 190, Margin = new Thickness(0, 0, 0, 8) }; DockPanel.SetDock(list, Dock.Top); root.Children.Add(list);
        foreach (var copy in copies) list.Items.Add(new ListBoxItem { Content = $"{copy.CreatedUtc.ToLocalTime():g} · {copy.Path ?? "Untitled"} · {copy.Reason}", Tag = copy });
        if (copies.Count == 0) list.Items.Add(new ListBoxItem { Content = "No saved copies available.", IsEnabled = false });
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas") }; root.Children.Add(preview);
        list.SelectionChanged += (_, _) => { Selected = (list.SelectedItem as ListBoxItem)?.Tag as DocumentCopy; preview.Text = Selected?.Text ?? ""; restore.IsEnabled = Selected is not null; };
        restore.Click += (_, _) => DialogResult = true;
    }
}
