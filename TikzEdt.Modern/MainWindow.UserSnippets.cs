using System.IO;
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Snippets;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private InsertionContext? _snippetContext;
    internal void FinishUserSnippet() => _snippetContext?.Deactivate(new SnippetEventArgs(DeactivateReason.Unknown));
    private readonly record struct SnippetSegment(int Offset, int Length) : ISegment { public int EndOffset => Offset + Length; }
    private static string GlobalSnippetsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TikzEdt", "Modern", "snippets.json");
    internal SnippetCatalog ReadUserSnippets() => UserSnippets.Load(GlobalSnippetsPath, _filePath is null ? _sidebarFolder : Path.GetDirectoryName(_filePath)!);
    internal bool TryExpandUserSnippet()
    {
        if (_snippetContext is not null || Editor.SelectionLength != 0) return false;
        var end = Editor.CaretOffset; var start = end;
        while (start > 0 && (char.IsLetterOrDigit(Editor.Text[start - 1]) || Editor.Text[start - 1] is '_' or '-')) start--;
        if (start == end) return false;
        var prefix = Editor.Document.GetText(start, end - start); var catalog = ReadUserSnippets();
        var math = LatexSymbolCatalog.IsMathContext(Editor.Text, end);
        var snippet = catalog.Items.Where(s => s.Prefix == prefix && (s.Context == "any" || s.Context == (math ? "math" : "text"))).OrderBy(s => s.Context == "any" ? 1 : 0).FirstOrDefault();
        if (snippet is null) { if (catalog.Issues.Count > 0) ModeStatus.Text = catalog.Issues[0]; return false; }
        _completionWindow?.Close(); InsertUserSnippet(snippet, start, end - start); return true;
    }
    internal void InsertUserSnippet(UserSnippet snippet, int start, int length)
    {
        var selection = Editor.SelectedText;
        var line = Editor.Document.GetLineByOffset(start);
        var indent = new string(Editor.Document.GetText(line.Offset, start - line.Offset).TakeWhile(c => c is ' ' or '\t').ToArray());
        var eol = Editor.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var body = snippet.Body.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", Editor.Options.IndentationString).Replace("\n", eol + indent);
        var expanded = UserSnippets.Expand(body, selection);
        _snippetContext?.Deactivate(new SnippetEventArgs(DeactivateReason.Unknown));
        using (Editor.Document.RunUpdate())
        {
            var context = new InsertionContext(Editor.TextArea, start); _snippetContext = context;
            Editor.Document.Replace(start, length, expanded.Text); context.InsertionPosition = start + expanded.Text.Length;
            foreach (var group in expanded.Fields.GroupBy(f => f.Number).OrderBy(g => g.Key))
            {
                var occurrences = group.ToArray(); var primary = occurrences[0];
                context.Link(new SnippetSegment(start + primary.Offset, primary.Length), occurrences.Skip(1).Select(f => (ISegment)new SnippetSegment(start + f.Offset, f.Length)).ToArray());
            }
            var caret = Editor.Document.CreateAnchor(start + expanded.Caret); caret.SurviveDeletion = true; caret.MovementType = AnchorMovementType.AfterInsertion;
            context.Deactivated += (_, _) => { if (ReferenceEquals(_snippetContext, context)) _snippetContext = null; if (!caret.IsDeleted) Editor.CaretOffset = caret.Offset; };
            context.RaiseInsertionCompleted(EventArgs.Empty);
        }
        Editor.Focus(); ModeStatus.Text = "Snippet inserted · Tab / Shift+Tab: fields · Escape: finish · Ctrl+Z: undo.";
    }
    private void EditGlobalSnippets_Click(object sender, RoutedEventArgs e) => EditSnippetFile(GlobalSnippetsPath);
    private void EditProjectSnippets_Click(object sender, RoutedEventArgs e) => EditSnippetFile(Path.Combine(_filePath is null ? _sidebarFolder : Path.GetDirectoryName(_filePath)!, ".tikzedt", "snippets.json"));
    private void EditSnippetFile(string path)
    {
        try
        {
            var original = File.Exists(path) ? DocumentSafetyStore.Read(path) : null;
            var dialog = new SnippetFileWindow(path, original?.Text ?? UserSnippets.Example) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            _safety.Save(path, dialog.Result, original?.Fingerprint); ModeStatus.Text = "Snippets saved. Type a prefix and press Tab, or use Insert user snippet.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ModeStatus.Text = "Snippet save failed: " + error.Message; }
    }
    private void InsertUserSnippet_Click(object sender, RoutedEventArgs e)
    {
        var catalog = ReadUserSnippets();
        var actions = catalog.Items.Select(snippet => new EditorAction("snippet:" + snippet.Prefix + ":" + snippet.Context, snippet.Name + " · " + snippet.Prefix, snippet.Description ?? "", "Tab", () => InsertUserSnippet(snippet, Editor.SelectionStart, Editor.SelectionLength)));
        var dialog = new CommandPaletteWindow(actions, _sidebarFolder, _ => { }, includeFiles: false) { Owner = this, Title = "Insert user snippet" };
        if (dialog.ShowDialog() == true) dialog.Chosen?.Execute();
        if (catalog.Issues.Count > 0) ModeStatus.Text = string.Join("\n", catalog.Issues);
    }
}
