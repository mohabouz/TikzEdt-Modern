using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TikzEdt.Modern;

/// <summary>Resolution-independent view navigation. Panning/zooming never changes document geometry.</summary>
internal sealed class DiagramViewport : DockPanel
{
    public Canvas Surface { get; } = new() { ClipToBounds = true, Focusable = true };
    public ComboBox ZoomSelector { get; } = new() { Width = 86, ToolTip = "Zoom percentage; wheel zooms around the pointer" };
    public ToggleButton HandButton { get; } = new() { Content = "✋ Pan", ToolTip = "Drag to pan. Middle-drag or Space+drag also pans in any tool.", Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(3) };
    public Button FitButton { get; } = new() { Content = "Fit", ToolTip = "Fit the figure in this pane", Padding = new Thickness(7, 3, 7, 3) };
    public event Action? ViewChanged;
    public event Action? FitRequested;
    public double Scale { get; private set; } = 1;
    public Vector Offset { get; private set; }
    public Point Origin => new(Surface.ActualWidth / 2 + Offset.X, Surface.ActualHeight / 2 + Offset.Y);
    private Point? _panStart;
    private bool _sync;
    public DiagramViewport()
    {
        foreach (var value in new[] { "10%", "25%", "50%", "75%", "100%", "150%", "200%", "400%", "800%", "1600%", "3200%", "6400%" }) ZoomSelector.Items.Add(value);
        ZoomSelector.SelectedItem = "100%";
        Surface.SetResourceReference(Panel.BackgroundProperty, "WindowBrush");
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
        SetDock(footer, Dock.Bottom); Children.Add(footer);
        footer.Children.Add(new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3) });
        footer.Children.Add(ZoomSelector); footer.Children.Add(HandButton);
        FitButton.Click += (_, _) => FitRequested?.Invoke(); footer.Children.Add(FitButton);
        var border = new Border { Child = Surface, BorderThickness = new Thickness(1) }; border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); Children.Add(border);
        void Commit()
        {
            if (_sync) return;
            if (double.TryParse(ZoomSelector.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.CurrentCulture, out var percent) && double.IsFinite(percent) && percent > 0)
                ZoomAt(new Point(Surface.ActualWidth / 2, Surface.ActualHeight / 2), percent / 100);
            else SyncSelector();
        }
        ZoomSelector.SelectionChanged += (_, _) => { if (!_sync && ZoomSelector.SelectedItem is string text) { ZoomSelector.Text = text; Commit(); } };
        ZoomSelector.LostKeyboardFocus += (_, _) => Commit();
        ZoomSelector.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); Surface.Focus(); e.Handled = true; } };
        HandButton.Checked += (_, _) => Surface.Cursor = Cursors.Hand;
        HandButton.Unchecked += (_, _) => Surface.Cursor = Cursors.Arrow;
        Surface.PreviewMouseWheel += (_, e) => { ZoomAt(e.GetPosition(Surface), Scale * (e.Delta > 0 ? 1.2 : 1 / 1.2)); e.Handled = true; };
        Surface.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Middle && !(e.ChangedButton == MouseButton.Left && (HandButton.IsChecked == true || Keyboard.IsKeyDown(Key.Space)))) return;
            _panStart = e.GetPosition(Surface); Surface.CaptureMouse(); Surface.Cursor = Cursors.Hand; Surface.Focus(); e.Handled = true;
        };
        Surface.PreviewMouseMove += (_, e) =>
        {
            if (_panStart is not { } last) return;
            var current = e.GetPosition(Surface); Pan(current - last); _panStart = current; e.Handled = true;
        };
        Surface.PreviewMouseUp += (_, e) => { if (_panStart is null) return; _panStart = null; Surface.ReleaseMouseCapture(); RestoreCursor(); e.Handled = true; };
        Surface.LostMouseCapture += (_, _) => { _panStart = null; RestoreCursor(); };
        Surface.SizeChanged += (_, _) => ViewChanged?.Invoke();
    }
    private void RestoreCursor() => Surface.Cursor = HandButton.IsChecked == true ? Cursors.Hand : Cursors.Arrow;
    private void SyncSelector()
    {
        _sync = true; var text = (Scale * 100).ToString("0.#", CultureInfo.CurrentCulture) + "%";
        if (!ZoomSelector.Items.Contains(text)) ZoomSelector.Items.Add(text);
        ZoomSelector.SelectedItem = text; _sync = false;
    }
    public void ZoomAt(Point anchor, double scale)
    {
        scale = Math.Clamp(scale, .1, 64);
        var center = new Point(Surface.ActualWidth / 2, Surface.ActualHeight / 2);
        Offset = (anchor - center) - ((anchor - center) - Offset) * (scale / Scale);
        Scale = scale; SyncSelector(); ViewChanged?.Invoke();
    }
    public void Pan(Vector delta) { Offset += delta; ViewChanged?.Invoke(); }
    public void Fit(Rect screenUnits)
    {
        if (screenUnits.IsEmpty || Surface.ActualWidth <= 0 || Surface.ActualHeight <= 0) return;
        Scale = Math.Clamp(Math.Min(Math.Max(1, Surface.ActualWidth - 40) / Math.Max(1, screenUnits.Width), Math.Max(1, Surface.ActualHeight - 40) / Math.Max(1, screenUnits.Height)), .1, 64);
        Offset = new Vector(-(screenUnits.Left + screenUnits.Width / 2) * Scale, -(screenUnits.Top + screenUnits.Height / 2) * Scale);
        SyncSelector(); ViewChanged?.Invoke();
    }
}
