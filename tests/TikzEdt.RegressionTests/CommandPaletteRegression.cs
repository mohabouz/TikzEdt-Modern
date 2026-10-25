using System.IO;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;

internal static class CommandPaletteRegression
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        Require(PaletteSearch.Score("cmp", "Compile figure") >= 0 && PaletteSearch.Score("zzzz", "Compile figure") == -1, "Fuzzy matching incorrect.");
        Require(PaletteSearch.Score("compile", "Compile figure") > PaletteSearch.Score("compile", "Cancel compile"), "Exact prefix should rank first.");
        Require(PaletteSearch.Score("figure compile", "Compile figure") >= 0 && PaletteSearch.Score("f5", "Compile figure F5") >= 0, "Terms/shortcut search failed.");
        var root = Path.GetFullPath("artifacts/palette-regression/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "figure.tikz"), "fixture"); Directory.CreateDirectory(Path.Combine(root, "build")); File.WriteAllText(Path.Combine(root, "build", "generated.tex"), "skip");
        Directory.CreateDirectory(Path.Combine(root, ".hidden")); File.WriteAllText(Path.Combine(root, ".hidden", "secret.tex"), "skip");
        Require(PaletteSearch.FindFiles(root, CancellationToken.None).Count == 1, "Ignored directories leaked into file search.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { PaletteSearch.FindFiles(root, cancelled.Token); throw new Exception("File scan ignored cancellation."); } catch (OperationCanceledException) { }
        Exception? failure = null; var thread = new Thread(() => { try { Ui(root); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure is not null) throw failure;
        Console.WriteLine("A3: fuzzy/ranked/shortcut search, bounded file scan, ignored folders, cancellation, unique actions, enabled states and actual undo/redo dispatch passed.");
    }
    static void Ui(string root)
    {
        var app = new App(); app.InitializeComponent(); var main = new MainWindow(); var editor = (TextEditor)main.FindName("Editor");
        var actions = main.CreateActions(); Require(actions.Select(a => a.Id).Distinct().Count() == actions.Count, "Duplicate action IDs.");
        Require(!actions.Single(a => a.Id == "history").Available, "History action enabled on an untitled document.");
        var source = editor.Text; editor.Document.Insert(0, "% edit\n"); Require(actions.Single(a => a.Id == "undo").Available, "Undo availability did not refresh.");
        actions.Single(a => a.Id == "undo").Execute(); Require(editor.Text == source, "Palette undo failed.");
        Require(actions.Single(a => a.Id == "redo").Available, "Redo availability stale."); actions.Single(a => a.Id == "redo").Execute(); Require(editor.Text.StartsWith("% edit"), "Palette redo failed.");
        var palette = new CommandPaletteWindow(actions, root, _ => throw new Exception("File must not open while searching."));
        var content = (FrameworkElement)palette.Content; content.Measure(new Size(720, 540)); content.Arrange(new Rect(0, 0, 720, 540)); content.UpdateLayout();
        Require(editor.Text.StartsWith("% edit"), "Opening/searching palette modified document."); palette.Close(); main.Close(); app.Shutdown();
    }
}
