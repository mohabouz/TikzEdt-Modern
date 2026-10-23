using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace TikzEdt.Modern;

internal static class EditorSearch
{
    public static Regex Pattern(string text, bool regex, bool matchCase, bool wholeWord)
    {
        var pattern = regex ? text : Regex.Escape(text);
        if (wholeWord) pattern = @"(?<![\p{L}\p{N}_])(?:" + pattern + @")(?![\p{L}\p{N}_])";
        return new(pattern, RegexOptions.CultureInvariant | RegexOptions.Multiline | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromMilliseconds(150));
    }
    public static Match[] Matches(string source, string text, bool regex, bool matchCase, bool wholeWord) => string.IsNullOrEmpty(text) ? [] : Pattern(text, regex, matchCase, wholeWord).Matches(source).Cast<Match>().Take(20001).ToArray();
    public static string Replacement(Match match, string value, bool regex) => regex ? match.Result(value) : value;
}

internal sealed class EditorFindReplace : Border, IBackgroundRenderer
{
    private readonly TextEditor _editor;
    internal TextBox FindInput { get; } = new() { Padding = new Thickness(6, 5, 92, 5), MinWidth = 100 };
    internal TextBox ReplaceInput { get; } = new() { Padding = new Thickness(6, 5, 6, 5) };
    internal ToggleButton CaseButton { get; } = new() { Content = "Aa", Width = 29, Padding = new Thickness(1), Margin = new Thickness(0), ToolTip = "Match case" };
    internal ToggleButton WordButton { get; } = new() { Content = "ab", Width = 29, Padding = new Thickness(1), Margin = new Thickness(0), ToolTip = "Whole words (off: match any part)" };
    internal ToggleButton RegexButton { get; } = new() { Content = ".*", Width = 29, Padding = new Thickness(1), Margin = new Thickness(0), ToolTip = "Regular expressions" };
    private readonly TextBlock _status = new() { Margin = new Thickness(4), TextWrapping = TextWrapping.Wrap };
    private readonly Grid _replaceRow;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(130) };
    private Match[] _matches = [];
    private string _source = "";
    private bool _editing;
    private int _lastOffset = -1;
    public KnownLayer Layer => KnownLayer.Background;

    public EditorFindReplace(TextEditor editor)
    {
        _editor = editor; Background = (Brush)Application.Current.FindResource("PanelBrush"); Padding = new Thickness(7); BorderThickness = new Thickness(0, 0, 0, 1); BorderBrush = (Brush)Application.Current.FindResource("BorderBrush");
        var panel = new StackPanel(); Child = panel;
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var field = new Grid(); field.Children.Add(FindInput);
        var switches = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 3, 2) };
        switches.Children.Add(CaseButton); switches.Children.Add(WordButton); switches.Children.Add(RegexButton); field.Children.Add(switches); row.Children.Add(field);
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(actions, 1); row.Children.Add(actions);
        Button Action(string text, string tip, Action click) { var button = new Button { Content = text, ToolTip = tip, Padding = new Thickness(5, 3, 5, 3), MinWidth = 24 }; button.Click += (_, _) => click(); return button; }
        actions.Children.Add(Action("↑", "Previous match (Shift+Enter)", () => FindNext(true))); actions.Children.Add(Action("↓", "Next match (Enter)", () => FindNext(false))); actions.Children.Add(Action("×", "Close (Esc)", Close)); panel.Children.Add(row);
        _replaceRow = new Grid { Margin = new Thickness(0, 4, 0, 0) }; _replaceRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); _replaceRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _replaceRow.Children.Add(ReplaceInput);
        var replacements = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(replacements, 1); _replaceRow.Children.Add(replacements);
        replacements.Children.Add(Action("Replace", "Replace current match (Enter in replacement input)", ReplaceCurrent)); replacements.Children.Add(Action("All", "Replace all matches as one undo step", ReplaceAll)); panel.Children.Add(_replaceRow); panel.Children.Add(_status);
        FindInput.TextChanged += (_, _) => Refresh();
        foreach (var toggle in new[] { CaseButton, WordButton, RegexButton }) toggle.Click += (_, _) => Refresh();
        _refresh.Tick += (_, _) => { _refresh.Stop(); Refresh(); };
        editor.TextChanged += (_, _) => { if (IsVisible && !_editing) { _refresh.Stop(); _refresh.Start(); } };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter) { if (ReplaceInput.IsKeyboardFocusWithin) ReplaceCurrent(); else FindNext(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true; }
        };
        editor.TextArea.TextView.BackgroundRenderers.Add(this);
    }
    public void Open(bool replace)
    {
        Visibility = Visibility.Visible; _replaceRow.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        if (_editor.SelectionLength is > 0 and < 2000) FindInput.Text = _editor.SelectedText;
        Refresh(); FindInput.Focus(); FindInput.SelectAll();
    }
    public void Close() { Visibility = Visibility.Collapsed; _refresh.Stop(); _editor.TextArea.TextView.InvalidateLayer(Layer); _editor.Focus(); }
    internal void Refresh()
    {
        _refresh.Stop(); _source = _editor.Text; _lastOffset = -1;
        try
        {
            _matches = EditorSearch.Matches(_source, FindInput.Text, RegexButton.IsChecked == true, CaseButton.IsChecked == true, WordButton.IsChecked == true);
            _status.Text = _matches.Length > 20000 ? "More than 20,000 matches — narrow the search." : $"{_matches.Length} matches";
            _status.Foreground = _editor.Foreground;
        }
        catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
        { _matches = []; _status.Text = "Search error: " + exception.Message; _status.Foreground = Brushes.Salmon; }
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }
    internal void FindNext(bool previous)
    {
        if (_source != _editor.Text) Refresh();
        if (_matches.Length == 0) return;
        var start = previous ? _editor.SelectionStart : _editor.SelectionStart + Math.Max(_editor.SelectionLength, _lastOffset == _editor.SelectionStart ? 1 : 0);
        var match = previous ? _matches.LastOrDefault(match => match.Index < start) ?? _matches[^1] : _matches.FirstOrDefault(match => match.Index >= start) ?? _matches[0];
        _editor.Select(match.Index, match.Length); _lastOffset = match.Index;
        _editor.ScrollToLine(_editor.Document.GetLineByOffset(match.Index).LineNumber);
        _status.Text = $"{Array.IndexOf(_matches, match) + 1} of {_matches.Length}";
    }
    internal void ReplaceCurrent()
    {
        if (_source != _editor.Text) Refresh();
        var match = _matches.FirstOrDefault(match => match.Index == _editor.SelectionStart && match.Length == _editor.SelectionLength);
        if (match is null) { FindNext(false); return; }
        try
        {
            var replacement = EditorSearch.Replacement(match, ReplaceInput.Text, RegexButton.IsChecked == true);
            _editing = true; _editor.Document.Replace(match.Index, match.Length, replacement); _editor.Select(match.Index + replacement.Length, 0);
            Refresh(); FindNext(false);
        }
        catch (ArgumentException exception) { _status.Text = exception.Message; }
        finally { _editing = false; }
    }
    internal void ReplaceAll()
    {
        Refresh(); if (_matches.Length == 0 || _matches.Length > 20000) return;
        try
        {
            var edits = _matches.Select(match => (match.Index, match.Length, Text: EditorSearch.Replacement(match, ReplaceInput.Text, RegexButton.IsChecked == true))).ToArray();
            _editing = true;
            using (_editor.Document.RunUpdate()) foreach (var edit in edits.Reverse()) _editor.Document.Replace(edit.Index, edit.Length, edit.Text);
            Refresh(); _status.Text = $"Replaced {edits.Length} matches · Ctrl+Z to undo";
        }
        catch (ArgumentException exception) { _status.Text = exception.Message; }
        finally { _editing = false; }
    }
    public void Draw(TextView textView, DrawingContext context)
    {
        if (!IsVisible || !textView.VisualLinesValid || _source != _editor.Text) return;
        var brush = new SolidColorBrush(Color.FromArgb(72, 230, 190, 70));
        var first = textView.VisualLines.FirstOrDefault()?.FirstDocumentLine.Offset ?? 0;
        var last = textView.VisualLines.LastOrDefault()?.LastDocumentLine.EndOffset ?? 0;
        foreach (var match in _matches.Where(match => match.Index <= last && match.Index + match.Length >= first))
            foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, new TextSegment { StartOffset = match.Index, Length = match.Length })) context.DrawRectangle(brush, null, rectangle);
    }
}
