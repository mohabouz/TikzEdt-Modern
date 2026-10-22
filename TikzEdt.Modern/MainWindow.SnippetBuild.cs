using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private readonly SnippetPreviewBatch _snippetBatch = new();
    private bool _snippetBuildClosing;

    private void InitializeSnippetBuild()
    {
        Loaded += async (_, _) =>
        {
            try
            {
                var state = _snippetBatch.ReadCheckpoint();
                ResumeSnippetBuildMenu.IsEnabled = state is not null;
                if (state is not null) ResumeSnippetBuildMenu.Header = $"Resume saved snippet preview build ({state.Engine})";
                if (state?.Active == true) await RunSnippetBuildAsync(resume: true);
            }
            catch (Exception exception) { ShowSnippetBuildError(exception); }
        };
        Closed += (_, _) =>
        {
            _snippetBuildClosing = true;
            _snippetBatch.StopForExit();
        };
    }

    private async void BuildSnippetPreviews_Click(object sender, RoutedEventArgs e) => await RunSnippetBuildAsync(resume: false);
    private async void ResumeSnippetPreviews_Click(object sender, RoutedEventArgs e) => await RunSnippetBuildAsync(resume: true);
    private void PauseSnippetPreviews_Click(object sender, RoutedEventArgs e)
    {
        _snippetBatch.Pause();
        PauseSnippetBuildMenu.IsEnabled = false;
        SnippetBuildText.Text = "Pausing snippet previews…";
    }
    private void OpenSnippetCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SnippetLibrary.CacheDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { SnippetLibrary.CacheDirectory }, UseShellExecute = true });
        }
        catch (Exception exception) { ShowSnippetBuildError(exception); }
    }

    private async Task RunSnippetBuildAsync(bool resume)
    {
        if (_snippetBatch.IsRunning) return;
        try
        {
            var items = SnippetLibrary.Load();
            var preambleName = resume ? _snippetBatch.ReadCheckpoint()?.Preamble.DisplayName : PreambleSettings.ReadSnapshot().DisplayName;
            var progress = new Progress<SnippetBatchProgress>(value =>
            {
                if (_snippetBuildClosing) return;
                SnippetBuildProgress.Maximum = Math.Max(1, value.Total);
                SnippetBuildProgress.Value = value.Completed;
                SnippetBuildText.Text = $"Snippet previews · {value.Engine} · {value.Completed}/{value.Total} · {value.Failed} failed" +
                    (string.IsNullOrEmpty(value.Current) ? "" : " · " + value.Current);
                SnippetBuildText.ToolTip = SnippetBuildText.Text + "\nPreamble: " + preambleName +
                    "\nResume uses the saved compiler and preamble, not the current selection.\n" + SnippetLibrary.CacheDirectory;
                SnippetBuildStatusMenu.Header = SnippetBuildText.Text;
            });
            var engine = (EngineSelector.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pdflatex";
            var work = resume
                ? _snippetBatch.ResumeAsync(items, progress)
                : _snippetBatch.StartAsync(items, engine, PreambleSettings.ReadSnapshot(), SettingsStore.Current, progress);
            BuildSnippetPreviewsMenu.IsEnabled = false;
            ResumeSnippetBuildMenu.IsEnabled = false;
            PauseSnippetBuildMenu.IsEnabled = true;
            SnippetBuildPanel.Visibility = Visibility.Visible;
            SnippetBuildText.Text = resume ? "Resuming saved snippet preview build…" : "Starting snippet preview build…";
            await work;
            if (!_snippetBuildClosing)
            {
                var paused = SnippetBuildProgress.Value < SnippetBuildProgress.Maximum;
                SnippetBuildStatusMenu.Header = paused ? "Snippet preview build paused — resume available" : "Snippet preview build finished (see progress / error logs)";
                foreach (var item in _sidebarSnippetItems.Where(item => item.Preview is null && item.State.StartsWith("Preview failed", StringComparison.Ordinal)))
                    item.State = "Waiting for preview";
                ScheduleSidebarThumbnails();
            }
        }
        catch (Exception exception) { if (!_snippetBuildClosing) ShowSnippetBuildError(exception); }
        finally
        {
            if (!_snippetBuildClosing)
            {
                BuildSnippetPreviewsMenu.IsEnabled = true;
                ResumeSnippetBuildMenu.IsEnabled = File.Exists(_snippetBatch.ManifestPath);
                PauseSnippetBuildMenu.IsEnabled = false;
            }
        }
    }

    private void ShowSnippetBuildError(Exception exception)
    {
        SnippetBuildPanel.Visibility = Visibility.Visible;
        SnippetBuildText.Text = "Snippet preview build stopped: " + exception.Message;
        SnippetBuildText.ToolTip = exception.ToString();
        SnippetBuildStatusMenu.Header = "Snippet build stopped — see progress message";
    }
}
