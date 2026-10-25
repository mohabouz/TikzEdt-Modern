using System.IO;
using System.Windows;
using System.Windows.Controls;
using TikzEdt.Modern.Core.Toolchain;

namespace TikzEdt.Modern;

internal sealed class ToolchainDoctorWindow : Window
{
    private readonly StackPanel _results = new();
    private readonly TextBlock _status = new() { Text = "Read-only checks. Nothing will be installed and PATH will not be changed.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 8) };
    private readonly Button _check = new() { Content = "Check again" };
    private readonly Button _cancel = new() { Content = "Cancel checks", IsEnabled = false };
    private readonly UserSettings _settings;
    private CancellationTokenSource? _job;
    public ToolchainDoctorWindow(UserSettings settings)
    {
        _settings = settings.Clone(); NativeWindowTheme.Attach(this);
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "Toolchain Doctor"; Width = 790; Height = 650; MinWidth = 570; MinHeight = 400; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "TeX engines, converters and package discovery", FontSize = 18, FontWeight = FontWeights.SemiBold }); top.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; top.Children.Add(buttons); buttons.Children.Add(_check); buttons.Children.Add(_cancel);
        var note = new TextBlock { Text = "Fix executable paths in Settings → Compiler. Package-file presence is not a compilation guarantee. Engine checks use configured paths; optional package checks use kpsewhich from the selected engine's folder when available, otherwise PATH.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 10, 4, 6) }; DockPanel.SetDock(note, Dock.Bottom); root.Children.Add(note);
        root.Children.Add(new ScrollViewer { Content = _results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _check.Click += async (_, _) => await CheckAsync(); _cancel.Click += (_, _) => _job?.Cancel();
        Loaded += async (_, _) => await CheckAsync(); Closed += (_, _) => _job?.Cancel();
    }
    internal async Task CheckAsync()
    {
        _job?.Cancel(); using var job = new CancellationTokenSource(); _job = job;
        _results.Children.Clear(); _check.IsEnabled = false; _cancel.IsEnabled = true; _status.Text = "Checking… no software is installed or modified.";
        try
        {
            var engines = new[] { ("pdfLaTeX", _settings.PdfLatexPath), ("LuaLaTeX", _settings.LuaLatexPath), ("XeLaTeX", _settings.XeLatexPath), ("PDF → SVG/PNG (pdftocairo)", "pdftocairo"), ("PDF metadata (pdfinfo)", "pdfinfo"), ("Source/preview navigation (synctex)", "synctex") };
            foreach (var (name, command) in engines)
            {
                var args = command is "pdftocairo" or "pdfinfo" ? new[] { "-v" } : command == "synctex" ? new[] { "--version" } : new[] { "--version" };
                Show(await ToolchainDoctor.ProbeAsync(name, command, args, TimeSpan.FromSeconds(5), job.Token));
            }
            var engine = ToolchainDoctor.Resolve(_settings.ResolveEngine(_settings.StartupEngine));
            var nearby = engine is null ? null : Path.Combine(Path.GetDirectoryName(engine)!, OperatingSystem.IsWindows() ? "kpsewhich.exe" : "kpsewhich");
            var locator = nearby is not null && File.Exists(nearby) ? nearby : "kpsewhich";
            Show(await ToolchainDoctor.ProbeAsync("Package locator (kpsewhich)", locator, new[] { "--version" }, TimeSpan.FromSeconds(5), job.Token));
            foreach (var package in new[] { "standalone.cls", "tikz.sty", "chemfig.sty", "circuitikz.sty", "pst-labo.sty" })
                Show(await ToolchainDoctor.ProbeAsync(package, locator, new[] { package }, TimeSpan.FromSeconds(5), job.Token, package: true));
            _status.Text = "Checks complete. Missing optional tools/packages do not prevent unrelated editing features.";
        }
        catch (OperationCanceledException) { _status.Text = "Checks cancelled."; }
        catch (Exception e) { _status.Text = "Could not finish checks: " + e.Message; }
        finally { if (ReferenceEquals(_job, job)) { _job = null; _check.IsEnabled = true; _cancel.IsEnabled = false; } }
    }
    private void Show(ToolProbe result)
    {
        var row = new StackPanel { Margin = new Thickness(4, 10, 4, 4) };
        row.Children.Add(new TextBlock { Text = $"{result.Name} — {result.State}", FontWeight = FontWeights.SemiBold });
        row.Children.Add(new TextBlock { Text = result.Command, TextWrapping = TextWrapping.Wrap, FontSize = 11 });
        row.Children.Add(new TextBlock { Text = result.Detail, TextWrapping = TextWrapping.Wrap }); _results.Children.Add(row);
    }
}
