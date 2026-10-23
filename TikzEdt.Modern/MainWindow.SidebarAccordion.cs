using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private double _sidebarExpandedWidth = 250;
    private const double SidebarRailWidth = 30;
    private const double SidebarCollapseThreshold = 120;
    private double _sidebarDragStartWidth;
    private double _sidebarDragRestoreWidth;
    private double _sidebarDragDelta;
    private bool _sidebarDragCollapsed;
    private string? _expandedSnippetCategory;
    private readonly DispatcherTimer _thumbnailTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly SemaphoreSlim _thumbnailWorkers = new(2);
    private CancellationTokenSource _thumbnailCancellation = new();
    private string? _thumbnailContext;
    private readonly Dictionary<string, Task<BitmapSource>> _thumbnailJobs = [];
    private readonly HashSet<SnippetItem> _thumbnailPending = [];

    private void InitializeSidebarAccordion()
    {
        _thumbnailTimer.Tick += (_, _) => { _thumbnailTimer.Stop(); QueueVisibleSidebarThumbnails(); };
        SidebarSnippets.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => ScheduleSidebarThumbnails()));
        Closed += (_, _) => { _thumbnailTimer.Stop(); _thumbnailCancellation.Cancel(); };
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => SetSidebarVisible(SidebarHost.Visibility != Visibility.Visible);
    private void SetSidebarVisible(bool visible)
    {
        if (visible && SidebarHost.Visibility == Visibility.Visible) return;
        if (visible)
        {
            SidebarColumn.MinWidth = 200;
            SidebarColumn.Width = new GridLength(_sidebarExpandedWidth);
            SidebarSplitterColumn.Width = new GridLength(10);
            SidebarHost.Visibility = Visibility.Visible;
            SidebarRail.Visibility = Visibility.Collapsed;
            if (SnippetsSidebarTab.IsSelected) { _ = RefreshSidebarSnippetAsync(); ScheduleSidebarThumbnails(); }
        }
        else
        {
            if (SidebarColumn.ActualWidth >= 200) _sidebarExpandedWidth = SidebarColumn.ActualWidth;
            SidebarColumn.MinWidth = SidebarRailWidth;
            SidebarColumn.Width = new GridLength(SidebarRailWidth);
            SidebarSplitterColumn.Width = new GridLength(10);
            SidebarHost.Visibility = Visibility.Collapsed;
            SidebarRail.Visibility = Visibility.Visible;
            FilesRailButton.IsChecked = FilesSidebarTab.IsSelected;
            SnippetsRailButton.IsChecked = SnippetsSidebarTab.IsSelected;
            SymbolsRailButton.IsChecked = SymbolsSidebarTab.IsSelected;
            _sidebarPreviewCancellation?.Cancel();
            _thumbnailTimer.Stop();
            _thumbnailCancellation.Cancel();
        }
        SidebarChevron.Data = Geometry.Parse(visible ? "M 6 2 L 2 7 L 6 12" : "M 2 2 L 6 7 L 2 12");
        SidebarChevronButton.ToolTip = visible ? "Collapse sidebar (Ctrl+B)" : "Expand sidebar (Ctrl+B)";
    }

    private void SidebarResize_Started(object sender, DragStartedEventArgs e)
    {
        _sidebarDragStartWidth = SidebarColumn.ActualWidth > 0 ? SidebarColumn.ActualWidth : SidebarColumn.Width.Value;
        _sidebarDragRestoreWidth = SidebarHost.Visibility == Visibility.Visible ? Math.Max(200, _sidebarDragStartWidth) : _sidebarExpandedWidth;
        _sidebarDragDelta = 0;
        _sidebarDragCollapsed = false;
        if (SidebarHost.Visibility == Visibility.Visible) SidebarColumn.MinWidth = 0;
    }

    private void SidebarResize_Delta(object sender, DragDeltaEventArgs e)
    {
        _sidebarDragDelta += e.HorizontalChange;
        ApplySidebarDragWidth(_sidebarDragStartWidth + _sidebarDragDelta);
    }

    private void ApplySidebarDragWidth(double requestedWidth)
    {
        if (_sidebarDragCollapsed) return;
        if (requestedWidth <= SidebarCollapseThreshold)
        {
            if (SidebarHost.Visibility == Visibility.Visible)
            {
                SetSidebarVisible(false);
                _sidebarExpandedWidth = _sidebarDragRestoreWidth;
                _sidebarDragCollapsed = true;
            }
            return;
        }
        if (SidebarHost.Visibility != Visibility.Visible)
        {
            if (requestedWidth < 200) return;
            SetSidebarVisible(true);
        }
        SidebarColumn.MinWidth = 0;
        SidebarColumn.Width = new GridLength(Math.Clamp(requestedWidth, SidebarCollapseThreshold + 1, 600));
    }

    private void SidebarResize_Completed(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled)
        {
            if (_sidebarDragStartWidth <= SidebarRailWidth) SetSidebarVisible(false);
            else
            {
                SetSidebarVisible(true);
                SidebarColumn.MinWidth = 200;
                _sidebarExpandedWidth = _sidebarDragRestoreWidth;
                SidebarColumn.Width = new GridLength(_sidebarExpandedWidth);
            }
            return;
        }
        if (SidebarHost.Visibility != Visibility.Visible) return;
        SidebarColumn.MinWidth = 200;
        _sidebarExpandedWidth = Math.Clamp(SidebarColumn.Width.Value, 200, 600);
        SidebarColumn.Width = new GridLength(_sidebarExpandedWidth);
    }

    private static IEnumerable<T> SidebarVisuals<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var descendant in SidebarVisuals<T>(child)) yield return descendant;
        }
    }

    private void SnippetCategory_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && expander.DataContext is CollectionViewGroup group)
            expander.IsExpanded = group.Name?.ToString() == _expandedSnippetCategory;
    }
    private void ExpandSelectedSnippetCategory()
    {
        foreach (var expander in SidebarVisuals<Expander>(SidebarSnippets))
            if (expander.DataContext is CollectionViewGroup group)
                expander.IsExpanded = group.Name?.ToString() == _expandedSnippetCategory;
        ScheduleSidebarThumbnails();
    }
    private void SnippetCategory_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not Expander expander || expander.DataContext is not CollectionViewGroup group) return;
        _expandedSnippetCategory = group.Name?.ToString();
        foreach (var other in SidebarVisuals<Expander>(SidebarSnippets).ToArray())
            if (!ReferenceEquals(expander, other)) other.IsExpanded = false;
        if (SidebarSnippets.SelectedItem is not SnippetItem selected || selected.Category != _expandedSnippetCategory)
            SidebarSnippets.SelectedItem = group.Items.OfType<SnippetItem>().FirstOrDefault();
        ScheduleSidebarThumbnails();
        e.Handled = true;
    }
    private void SnippetCategory_Collapsed(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup group } && group.Name?.ToString() == _expandedSnippetCategory)
        { _thumbnailTimer.Stop(); _expandedSnippetCategory = null; }
        e.Handled = true;
    }
    private void SidebarThumbnail_Loaded(object sender, RoutedEventArgs e) => ScheduleSidebarThumbnails();
    private void ScheduleSidebarThumbnails()
    {
        if (!IsLoaded || SidebarHost.Visibility != Visibility.Visible || !SnippetsSidebarTab.IsSelected) return;
        _thumbnailTimer.Stop();
        _thumbnailTimer.Start();
    }

    private Task<BitmapSource> GetSidebarPreviewTask(SnippetItem item, string engine, PreambleSnapshot preamble, UserSettings settings)
    {
        var context = engine + "\n" + preamble.CacheIdentity + "\n" + settings.CompilerIdentity;
        if (_thumbnailContext != context || _thumbnailCancellation.IsCancellationRequested)
        {
            _thumbnailCancellation.Cancel();
            _thumbnailCancellation.Dispose();
            _thumbnailCancellation = new CancellationTokenSource();
            _thumbnailJobs.Clear();
            _thumbnailContext = context;
            foreach (var entry in _sidebarSnippetItems) { entry.Preview = null; entry.State = "Waiting for preview"; }
        }
        var key = SnippetLibrary.GetCachePath(item, engine, preamble, settings);
        if (_thumbnailJobs.TryGetValue(key, out var existing) && !existing.IsCanceled && !existing.IsFaulted) return existing;
        var token = _thumbnailCancellation.Token;
        var job = Task.Run(async () =>
        {
            await _thumbnailWorkers.WaitAsync(token);
            try
            {
                var path = await SnippetLibrary.RenderPreviewAsync(item, engine, preamble, settings, token);
                token.ThrowIfCancellationRequested();
                return SnippetLibrary.LoadPreview(path);
            }
            finally { _thumbnailWorkers.Release(); }
        }, token);
        _thumbnailJobs[key] = job;
        return job;
    }

    private void QueueVisibleSidebarThumbnails()
    {
        if (!IsLoaded || SidebarHost.Visibility != Visibility.Visible || !SnippetsSidebarTab.IsSelected) return;
        foreach (var border in SidebarVisuals<Border>(SidebarSnippets))
        {
            if (_thumbnailPending.Count >= 2) break;
            if (border.Name != "SnippetThumbnail" || !border.IsVisible || border.DataContext is not SnippetItem item ||
                item.Preview is not null || item.State.StartsWith("Preview failed", StringComparison.Ordinal) || _thumbnailPending.Contains(item)) continue;
            var bounds = border.TransformToAncestor(SidebarSnippets).TransformBounds(new Rect(border.RenderSize));
            if (!bounds.IntersectsWith(new Rect(SidebarSnippets.RenderSize))) continue;
            _ = LoadSidebarThumbnailAsync(item);
        }
    }
    private async Task LoadSidebarThumbnailAsync(SnippetItem item)
    {
        _thumbnailPending.Add(item);
        var context = _thumbnailContext;
        try
        {
            var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
            var preamble = PreambleSettings.ReadSnapshot();
            var job = GetSidebarPreviewTask(item, engine, preamble, SettingsStore.Current.Clone());
            context = _thumbnailContext;
            item.State = System.IO.File.Exists(SnippetLibrary.GetCachePath(item, engine, preamble, SettingsStore.Current)) ? "Loading cached preview…" : "Compiling…";
            var bitmap = await job;
            if (context != _thumbnailContext || _thumbnailCancellation.IsCancellationRequested) return;
            item.Preview = bitmap;
            item.Error = null;
            item.State = "Preview ready";
        }
        catch (OperationCanceledException)
        { if (context == _thumbnailContext && item.Preview is null) item.State = "Waiting for preview"; }
        catch (Exception exception)
        {
            if (context == _thumbnailContext && !_thumbnailCancellation.IsCancellationRequested && item.Preview is null)
            { item.State = "Preview failed · click to retry"; item.Error = exception.Message; }
        }
        finally { _thumbnailPending.Remove(item); ScheduleSidebarThumbnails(); }
    }
}
