using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    internal bool HandleSourceEditorKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Control) && !modifiers.HasFlag(ModifierKeys.Alt) &&
            (key is Key.Oem2 or Key.Divide || key == Key.OemPeriod && modifiers.HasFlag(ModifierKeys.Shift)))
        { ToggleSourceComment(); return true; }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && key is Key.Up or Key.Down)
        {
            var command = key == Key.Up ? RectangleSelection.BoxSelectUpByLine : RectangleSelection.BoxSelectDownByLine;
            command.Execute(null, Editor.TextArea); return true;
        }
        if (key == Key.Escape && Editor.TextArea.Selection is RectangleSelection)
        { Editor.TextArea.ClearSelection(); return true; }
        return false;
    }
    internal void ToggleSourceComment()
    {
        var document = Editor.Document;
        var selected = !Editor.TextArea.Selection.IsEmpty;
        var lines = new SortedSet<int>();
        if (Editor.TextArea.Selection is RectangleSelection rectangle)
            foreach (var segment in rectangle.Segments) lines.Add(document.GetLineByOffset(segment.StartOffset).LineNumber);
        else
        {
            var first = document.GetLineByOffset(selected ? Editor.SelectionStart : Editor.CaretOffset).LineNumber;
            var last = document.GetLineByOffset(selected ? Editor.SelectionStart + Math.Max(0, Editor.SelectionLength - 1) : Editor.CaretOffset).LineNumber;
            for (var number = first; number <= last; number++) lines.Add(number);
        }
        if (lines.Count == 0) lines.Add(document.GetLineByOffset(Editor.CaretOffset).LineNumber);
        var entries = lines.Select(number => document.GetLineByNumber(number)).Select(line =>
        { var text = document.GetText(line); var spaces = text.TakeWhile(c => c is ' ' or '\t').Count(); return (Line: line, Text: text, Indent: spaces); }).ToArray();
        var nonempty = entries.Where(e => e.Indent < e.Text.Length).ToArray();
        var remove = nonempty.Length > 0 && nonempty.All(e => e.Text[e.Indent] == '%');
        var anchor = document.CreateAnchor(Editor.CaretOffset); anchor.MovementType = AnchorMovementType.AfterInsertion; anchor.SurviveDeletion = true;
        using (document.RunUpdate())
            foreach (var entry in entries.Reverse())
            {
                if (entry.Indent == entry.Text.Length && entries.Length > 1) continue;
                var offset = entry.Line.Offset + entry.Indent;
                if (remove)
                {
                    if (entry.Indent >= entry.Text.Length || entry.Text[entry.Indent] != '%') continue;
                    var length = entry.Indent + 1 < entry.Text.Length && entry.Text[entry.Indent + 1] == ' ' ? 2 : 1;
                    document.Remove(offset, length);
                }
                else document.Insert(offset, "% ");
            }
        if (selected)
        {
            var first = document.GetLineByNumber(lines.Min); var last = document.GetLineByNumber(lines.Max);
            Editor.Select(first.Offset, last.EndOffset - first.Offset);
        }
        else Editor.CaretOffset = anchor.Offset;
        Editor.Focus(); ModeStatus.Text = remove ? "Lines uncommented · Ctrl+Z to undo" : "Lines commented · Ctrl+Z to undo";
    }
}
