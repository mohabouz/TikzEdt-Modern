using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace TikzEdt.Modern;

public partial class SnippetsDialog : Window
{
    private readonly string _engine;
    private readonly UserSettings _settings = SettingsStore.Current.Clone();
    private readonly CancellationTokenSource _closed = new();
    private PreambleSnapshot _preamble = new(null, null);
    private IReadOnlyList<SnippetItem> _items = [];
    private bool _isCompiling;
    private int _complete;
    private int _failures;
    public string? InsertedCode { get; private set; }
    public string Dependencies { get; private set; } = "";

    public SnippetsDialog(string engine)
    {
        _engine = engine;
        NativeWindowTheme.Attach(this);
        InitializeComponent();
        EditorAppearance.Apply(SnippetCode, _settings);
        EngineText.Text = $"{engine} · previews cached on disk";
        Loaded += async (_, _) => await LoadAsync();
        Closed += (_, _) => _closed.Cancel();
    }

    private async Task LoadAsync()
    {
        try
        {
            _preamble = PreambleSettings.ReadSnapshot();
            _items = SnippetLibrary.Load();
            CategorySelector.ItemsSource = new[] { "All categories" }.Concat(_items.Select(item => item.Category).Distinct()).ToArray();
            CategorySelector.SelectedIndex = 0;
            SnippetList.ItemsSource = _items;
            SnippetList.SelectedIndex = 0;
            await CompileMissingAsync();
        }
        catch (Exception exception) { ProgressText.Text = "Cannot load snippets: " + exception.Message; }
    }

    private async Task CompileMissingAsync()
    {
        if (_isCompiling) return;
        _isCompiling = true;
        _complete = 0;
        _failures = 0;
        CompileProgress.Maximum = Math.Max(1, _items.Count);
        var pending = new List<SnippetItem>();
        foreach (var item in _items)
        {
            var path = SnippetLibrary.GetCachePath(item, _engine, _preamble, _settings);
            if (File.Exists(path))
            {
                try { item.Preview = SnippetLibrary.LoadPreview(path); item.State = "Cached preview"; _complete++; }
                catch (Exception) { pending.Add(item); }
            }
            else pending.Add(item);
        }
        UpdateProgress();
        try
        {
            if (pending.Count > 0)
            {
                ProgressText.Text = $"Checking shared preamble with {_engine}…";
                await Task.Run(() => SnippetPreviewConfiguration.ValidateAsync(_engine, _preamble, _settings, _closed.Token), _closed.Token);
            }
            while (pending.Count > 0)
            {
                _closed.Token.ThrowIfCancellationRequested();
                var selected = SnippetList.SelectedItem as SnippetItem;
                var category = CategorySelector.SelectedItem?.ToString();
                var group = pending.OrderBy(item => ReferenceEquals(item, selected) ? 0 : item.Category == category ? 1 : 2).Take(16).ToArray();
                foreach (var item in group) { pending.Remove(item); item.State = "Compiling preview pages…"; }
                var outcomes = await Task.Run(() => SnippetLibrary.RenderPreviewPagesAsync(group, _engine, _preamble, _settings, _closed.Token), _closed.Token);
                foreach (var outcome in outcomes)
                {
                    var item = outcome.Item;
                    try
                    {
                        if (outcome.Error is not null) throw outcome.Error;
                        item.Preview = SnippetLibrary.LoadPreview(outcome.Path);
                        item.Error = null;
                        item.State = "Preview ready";
                    }
                    catch (Exception exception)
                    {
                        item.Error = exception.Message;
                        item.State = "Preview failed · select to see details";
                        _failures++;
                    }
                    _complete++;
                    if (!_closed.IsCancellationRequested) UpdateProgress();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ProgressText.Text = "Preview build stopped: " + exception.Message;
            ProgressText.ToolTip = exception.ToString();
        }
        finally { _isCompiling = false; }
    }

    private void UpdateProgress()
    {
        CompileProgress.Value = _complete;
        ProgressText.Text = _complete == _items.Count
            ? $"{_complete - _failures} previews ready · {_failures} failed. Completed previews are cached for the next visit."
            : $"Preparing previews: {_complete}/{_items.Count} · {_failures} failed. Completed previews are saved as they finish.";
        if (SnippetList.SelectedItem is SnippetItem item) PreviewError.Text = CompactError(item.Error);
    }

    private static string CompactError(string? message) => message is null ? "" :
        string.Join("\n", message.Split('\n').Where(line => line.Contains('!') || line.Contains("Error", StringComparison.OrdinalIgnoreCase)).Take(3)) is { Length: > 0 } summary
            ? summary : message.Length > 300 ? message[..300] + "…" : message;

    private void Search_Changed(object sender, RoutedEventArgs e)
    {
        if (SnippetList?.ItemsSource is null) return;
        var search = SearchBox.Text.Trim();
        var category = CategorySelector.SelectedItem?.ToString();
        CollectionViewSource.GetDefaultView(SnippetList.ItemsSource).Filter = value => value is SnippetItem item &&
            (category is null or "All categories" || item.Category == category) &&
            (item.Name + " " + item.Category + " " + item.Description).Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void Snippet_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (SnippetList.SelectedItem is not SnippetItem item) return;
        SnippetCode.Text = string.IsNullOrWhiteSpace(item.Code) ? item.Sample : item.Code;
        SnippetCode.Parent.SetValue(DataContextProperty, item);
        PreviewError.Text = CompactError(item.Error);
    }

    private async void Retry_Click(object sender, RoutedEventArgs e) => await CompileMissingAsync();
    private void InsertCode_Click(object sender, RoutedEventArgs e) => Insert(false);
    private void InsertExample_Click(object sender, RoutedEventArgs e) => Insert(true);

    private void Insert(bool example)
    {
        if (SnippetList.SelectedItem is not SnippetItem item) return;
        InsertedCode = example ? item.Sample : item.Code;
        if (string.IsNullOrWhiteSpace(InsertedCode)) { ProgressText.Text = "This snippet has no code for that action."; return; }
        Dependencies = item.Dependencies;
        DialogResult = true;
    }
}
