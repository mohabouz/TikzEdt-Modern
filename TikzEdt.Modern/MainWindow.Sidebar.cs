using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private IReadOnlyList<SnippetItem> _sidebarSnippetItems = [];
    private sealed record SidebarFile(string Path, string Name, bool IsFolder)
    {
        public string Icon => IsFolder ? "files" : "new";
    }
    private string _sidebarFolder = Environment.CurrentDirectory;
    private string? _sidebarDocumentPath;
    private CancellationTokenSource? _sidebarPreviewCancellation;
    private int _sidebarPreviewGeneration;
    private bool _sidebarReady;

    private void InitializeSidebar()
    {
        _sidebarReady = true;
        InitializeSidebarAccordion();
        SidebarTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, SidebarTabs)) return;
            if (SnippetsSidebarTab.IsSelected)
            {
                if (SidebarSnippets.ItemsSource is null) Snippets_Click(this, new RoutedEventArgs());
                else _ = RefreshSidebarSnippetAsync();
            }
            else _sidebarPreviewCancellation?.Cancel();
        };
        EngineSelector.SelectionChanged += Compiler_SelectionChanged;
        Closed += (_, _) => _sidebarPreviewCancellation?.Cancel();
    }

    private void FollowDocumentFolder()
    {
        if (!_sidebarReady || _filePath is null || _filePath == _sidebarDocumentPath) return;
        _sidebarDocumentPath = _filePath;
        _sidebarFolder = Path.GetDirectoryName(_filePath)!;
        RefreshSidebarFiles();
    }

    private void RefreshSidebarFiles()
    {
        if (!_sidebarReady) return;
        try
        {
            FolderPathText.Text = _sidebarFolder;
            FolderPathText.ToolTip = _sidebarFolder;
            var folders = Directory.EnumerateDirectories(_sidebarFolder).OrderBy(Path.GetFileName)
                .Select(path => new SidebarFile(path, Path.GetFileName(path), true));
            var files = Directory.EnumerateFiles(_sidebarFolder).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".tex" or ".tikz" or ".sty" or ".cls")
                .OrderBy(Path.GetFileName).Select(path => new SidebarFile(path, Path.GetFileName(path), false));
            SidebarFiles.ItemsSource = folders.Concat(files).Take(1000).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { ModeStatus.Text = "Cannot list folder: " + exception.Message; }
    }

    private void FilesSidebar_Click(object sender, RoutedEventArgs e) { SetSidebarVisible(true); SidebarTabs.SelectedItem = FilesSidebarTab; RefreshSidebarFiles(); }
    private void RefreshFiles_Click(object sender, RoutedEventArgs e) => RefreshSidebarFiles();
    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose a TeX project folder", InitialDirectory = _sidebarFolder };
        if (dialog.ShowDialog(this) != true) return;
        _sidebarFolder = dialog.FolderName;
        RefreshSidebarFiles();
    }
    private void ParentFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.GetParent(_sidebarFolder) is { } parent) { _sidebarFolder = parent.FullName; RefreshSidebarFiles(); }
    }
    private void SidebarFile_Open(object sender, MouseButtonEventArgs e)
    {
        if (SidebarFiles.SelectedItem is not SidebarFile file) return;
        if (file.IsFolder) { _sidebarFolder = file.Path; RefreshSidebarFiles(); return; }
        if (!ConfirmDiscardChanges()) return;
        try
        {
            var source = File.ReadAllText(file.Path);
            _filePath = file.Path;
            Editor.Text = source;
            _isDirty = false;
            UpdateTitle();
            ModeStatus.Text = "Opened " + file.Name;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { ModeStatus.Text = "Cannot open file: " + exception.Message; }
    }

    private void Snippets_Click(object sender, RoutedEventArgs e)
    {
        SetSidebarVisible(true);
        if (SidebarSnippets.ItemsSource is null)
        {
            try { SetSidebarSnippetItems(SnippetLibrary.Load()); }
            catch (Exception exception) { SidebarSnippetState.Text = "Cannot load snippets: " + exception.Message; }
        }
        SidebarTabs.SelectedItem = SnippetsSidebarTab;
        if (SidebarSnippets.SelectedItem is null && SidebarSnippets.Items.Count > 0) SidebarSnippets.SelectedIndex = 0;
    }

    private void SidebarSnippetSearch_Changed(object sender, TextChangedEventArgs e)
        => FilterSidebarSnippets();

    private void SetSidebarSnippetItems(IReadOnlyList<SnippetItem> items)
    {
        _sidebarSnippetItems = items;
        var view = new ListCollectionView(items.OrderBy(item => item.Category).ThenBy(item => item.Name).ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SnippetItem.Category)));
        SidebarSnippets.ItemsSource = view;
        _expandedSnippetCategory = items.FirstOrDefault()?.Category;
        FilterSidebarSnippets();
    }

    private void FilterSidebarSnippets()
    {
        if (SidebarSnippets?.ItemsSource is null) return;
        var text = SidebarSnippetSearch.Text.Trim();
        var view = CollectionViewSource.GetDefaultView(SidebarSnippets.ItemsSource);
        view.Filter = value => value is SnippetItem item &&
            (item.Name + " " + item.Category + " " + item.Description).Contains(text, StringComparison.OrdinalIgnoreCase);
        SidebarSnippetCount.Text = $"{SidebarSnippets.Items.Count} snippets · {view.Groups?.Count ?? 0} categories";
        if (SidebarSnippets.SelectedItem is { } selected && !SidebarSnippets.Items.Contains(selected)) SidebarSnippets.SelectedItem = null;
        if (SidebarSnippets.SelectedItem is null && SidebarSnippets.Items.Count > 0) SidebarSnippets.SelectedIndex = 0;
        if (SidebarSnippets.Items.Count == 0)
        {
            _sidebarPreviewGeneration++;
            _sidebarPreviewCancellation?.Cancel();
            SidebarSnippetProgress.Visibility = Visibility.Collapsed;
            SidebarSnippetState.Text = "No matching snippets. Change the search.";
        }
        UpdateSidebarSnippetActions();
        if (SidebarSnippets.SelectedItem is SnippetItem current) _expandedSnippetCategory = current.Category;
        Dispatcher.BeginInvoke(ExpandSelectedSnippetCategory, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void UpdateSidebarSnippetActions()
    {
        var item = SidebarSnippets.SelectedItem as SnippetItem;
        SidebarInsertCodeButton.IsEnabled = !string.IsNullOrWhiteSpace(item?.Code);
        SidebarInsertExampleButton.IsEnabled = !string.IsNullOrWhiteSpace(item?.Sample);
    }
    private async void SidebarSnippet_Selected(object sender, SelectionChangedEventArgs e)
    {
        UpdateSidebarSnippetActions();
        await RefreshSidebarSnippetAsync();
    }
    private async Task RefreshSidebarSnippetAsync()
    {
        var generation = ++_sidebarPreviewGeneration;
        _sidebarPreviewCancellation?.Cancel();
        _sidebarPreviewCancellation?.Dispose();
        _sidebarPreviewCancellation = new CancellationTokenSource();
        var token = _sidebarPreviewCancellation.Token;
        SidebarSnippetProgress.Visibility = Visibility.Collapsed;
        if (SidebarSnippets.SelectedItem is not SnippetItem item) { SidebarSnippetState.Text = "Select a snippet"; return; }
        SidebarSnippetState.Text = item.Name;
        if (!IsLoaded || !SnippetsSidebarTab.IsSelected) return;
        try
        {
            var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
            var preamble = PreambleSettings.ReadSnapshot();
            var settings = SettingsStore.Current.Clone();
            var cachePath = SnippetLibrary.GetCachePath(item, engine, preamble, settings);
            var cached = File.Exists(cachePath);
            SidebarSnippetState.Text = cached ? "Loading cached preview…" : "Compiling preview… (saved to cache when ready)";
            SidebarSnippetProgress.Visibility = cached ? Visibility.Collapsed : Visibility.Visible;
            var bitmap = await GetSidebarPreviewTask(item, engine, preamble, settings).WaitAsync(token);
            if (generation != _sidebarPreviewGeneration || token.IsCancellationRequested) return;
            item.Preview = bitmap;
            item.Error = null;
            item.State = cached ? "Cached preview" : "Preview ready";
            SidebarSnippetState.Text = cached ? "Cached preview · " + item.Category : "Preview ready · cached for next visit";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation == _sidebarPreviewGeneration)
            {
                SidebarSnippetState.Text = "Preview unavailable: " + exception.Message[..Math.Min(exception.Message.Length, 220)];
                if (item.Preview is null) { item.Error = exception.Message; item.State = "Preview failed · click to retry"; }
            }
        }
        finally { if (generation == _sidebarPreviewGeneration) SidebarSnippetProgress.Visibility = Visibility.Collapsed; }
    }
    private void SidebarInsertCode_Click(object sender, RoutedEventArgs e)
    {
        if (SidebarSnippets.SelectedItem is SnippetItem item && !string.IsNullOrWhiteSpace(item.Code)) InsertSnippet(item.Code, item.Dependencies, item.RequiredPackages);
        else SidebarSnippetState.Text = "No standalone code; use Example instead.";
    }
    private void SidebarInsertExample_Click(object sender, RoutedEventArgs e)
    {
        if (SidebarSnippets.SelectedItem is SnippetItem item && !string.IsNullOrWhiteSpace(item.Sample)) InsertSnippet(item.Sample, item.Dependencies, item.RequiredPackages);
    }
}
