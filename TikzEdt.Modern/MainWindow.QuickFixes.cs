using System.Windows;
using System.Windows.Controls;
using TikzEdt.Modern.Core.Documents;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private string? _semanticSource;
    private IReadOnlyList<TexIssue> _semanticIssues = [];
    internal bool ApplySemanticFix(TexFix fix, string expectedSource)
    {
        if (Editor.Text != expectedSource || !TexSemantics.IsApplicable(Editor.Text, fix)) { ModeStatus.Text = "The document changed; request fresh quick fixes."; return false; }
        using (Editor.Document.RunUpdate()) Editor.Document.Replace(fix.Offset, fix.Length, fix.Replacement);
        ModeStatus.Text = fix.Title + " · Ctrl+Z to undo"; return true;
    }
    private void QuickFix_Click(object sender, RoutedEventArgs e)
    {
        if (_semanticSource != Editor.Text) { RefreshSyntaxDiagnostics(); ModeStatus.Text = "Refreshing diagnostics; try quick fixes again."; return; }
        var source = Editor.Text;
        var candidates = _semanticIssues.Where(i => i.Fix is not null).OrderBy(i => Math.Abs(i.Offset - Editor.CaretOffset)).Take(20).ToArray();
        if (candidates.Length == 0) { ModeStatus.Text = "No safe quick fixes available."; return; }
        var menu = new ContextMenu { PlacementTarget = Editor };
        foreach (var issue in candidates) { var fix = issue.Fix!; var item = new MenuItem { Header = fix.Title, ToolTip = issue.Message }; item.Click += (_, _) => ApplySemanticFix(fix, source); menu.Items.Add(item); }
        menu.IsOpen = true;
    }
}
