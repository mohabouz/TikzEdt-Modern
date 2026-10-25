using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

internal sealed record EditorAction(string Id, string Name, string Description, string Shortcut, Action Execute, Func<bool>? Enabled = null)
{
    public bool Available => Enabled?.Invoke() ?? true;
}
internal sealed class CommandPaletteWindow : Window
{
    private readonly TextBox _query = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly ListBox _list = new();
    private readonly TextBlock _status = new() { Text = "Enter: run · Esc: close · file search skips hidden/build folders and scans at most 5,000 entries", TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly List<EditorAction> _actions;
    private readonly CancellationTokenSource _lifetime = new();
    public EditorAction? Chosen { get; private set; }
    public CommandPaletteWindow(IEnumerable<EditorAction> actions, string folder, Action<string> openFile, bool includeFiles = true)
    {
        _actions = actions.ToList(); NativeWindowTheme.Attach(this); Style = (Style)Application.Current.FindResource(typeof(Window));
        _query.MaxLength = 200; _list.Style = (Style)Application.Current.FindResource("WorkflowList");
        Title = "Command palette"; Width = 720; Height = 540; MinWidth = 500; MinHeight = 350; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
        DockPanel.SetDock(_query, Dock.Top); root.Children.Add(_query);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status); root.Children.Add(_list);
        _query.ToolTip = "Search actions, shortcuts and project filenames (Ctrl+K)";
        _query.TextChanged += (_, _) => Refresh(); _list.MouseDoubleClick += (_, _) => Choose();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Choose(); e.Handled = true; }
            else if (e.Key is Key.Down or Key.Up) { _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Math.Max(0, _list.Items.Count - 1)); _list.ScrollIntoView(_list.SelectedItem); e.Handled = true; }
        };
        Loaded += async (_, _) =>
        {
            _query.Focus();
            if (!includeFiles) return;
            try
            {
                var files = await Task.Run(() => PaletteSearch.FindFiles(folder, _lifetime.Token), _lifetime.Token);
                if (_lifetime.IsCancellationRequested) return;
                _actions.AddRange(files.Select(file => new EditorAction("file:" + file, "Open " + Path.GetRelativePath(folder, file), file, "", () => openFile(file)))); Refresh();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _status.Text = "File search unavailable: " + e.Message; }
        };
        Closed += (_, _) => _lifetime.Cancel(); Refresh();
    }
    private void Refresh()
    {
        _list.Items.Clear();
        foreach (var result in _actions.Select(action => (Action: action, Score: PaletteSearch.Score(_query.Text, action.Name + " " + action.Shortcut + " " + action.Description))).Where(x => x.Score >= 0).OrderByDescending(x => x.Score).ThenBy(x => x.Action.Name).Take(60))
        {
            var action = result.Action; var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = action.Name + (action.Shortcut.Length == 0 ? "" : "    " + action.Shortcut), FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = action.Description, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            _list.Items.Add(new ListBoxItem { Content = panel, Tag = action, IsEnabled = action.Available, Padding = new Thickness(6) });
        }
        _list.SelectedItem = _list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.IsEnabled);
    }
    private void Choose()
    { if ((_list.SelectedItem as ListBoxItem)?.Tag is EditorAction { Available: true } action) { Chosen = action; DialogResult = true; } }
}
