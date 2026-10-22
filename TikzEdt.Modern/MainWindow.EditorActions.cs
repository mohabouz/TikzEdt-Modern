using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private void InitializeEditorActions()
    {
        var menu = new ContextMenu();
        void Command(string header, ICommand command, string gesture)
            => menu.Items.Add(new MenuItem { Header = header, Command = command, CommandTarget = Editor.TextArea, InputGestureText = gesture });
        Command("Undo", ApplicationCommands.Undo, "Ctrl+Z");
        Command("Redo", ApplicationCommands.Redo, "Ctrl+Y");
        menu.Items.Add(new Separator());
        Command("Cut", ApplicationCommands.Cut, "Ctrl+X");
        Command("Copy", ApplicationCommands.Copy, "Ctrl+C");
        Command("Paste", ApplicationCommands.Paste, "Ctrl+V");
        Command("Delete", ApplicationCommands.Delete, "Delete");
        Command("Select All", ApplicationCommands.SelectAll, "Ctrl+A");
        menu.Items.Add(new Separator());
        var format = new MenuItem { Header = "Format Code", InputGestureText = "Ctrl+Shift+F", ToolTip = "Indent selected lines, or the whole document when nothing is selected" };
        format.Click += FormatCode_Click;
        menu.Items.Add(format);
        var fold = new MenuItem { Header = "Fold All", InputGestureText = "Ctrl+Shift+[" };
        fold.Click += FoldAll_Click;
        menu.Items.Add(fold);
        var unfold = new MenuItem { Header = "Unfold All", InputGestureText = "Ctrl+Shift+]" };
        unfold.Click += UnfoldAll_Click;
        menu.Items.Add(unfold);
        Editor.ContextMenu = menu;
        Editor.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (Editor.SelectionLength == 0 && Editor.GetPositionFromPoint(e.GetPosition(Editor)) is { } position)
                Editor.CaretOffset = Editor.Document.GetOffset(position.Location);
            Editor.Focus();
        };
    }

    private void FormatCode_Click(object sender, RoutedEventArgs e)
    {
        var settings = SettingsStore.Current;
        var document = Editor.Document;
        var selected = Editor.SelectionLength > 0;
        var firstLine = document.GetLineByOffset(selected ? Editor.SelectionStart : 0);
        var lastLine = document.GetLineByOffset(selected ? Editor.SelectionStart + Editor.SelectionLength - 1 : document.TextLength);
        var start = firstLine.Offset;
        var end = lastLine.EndOffset;
        var edits = TikzCodeFormatter.CreateEdits(Editor.Text, settings.TabSize, settings.ConvertTabsToSpaces)
            .Where(edit => edit.Offset >= start && edit.Offset <= end).ToArray();
        if (edits.Length == 0) { ModeStatus.Text = "Code indentation is already formatted"; return; }
        var caretLine = document.GetLineByOffset(Editor.CaretOffset);
        var caretLineNumber = caretLine.LineNumber;
        var originalLine = document.GetText(caretLine);
        var caretColumn = Math.Max(0, Editor.CaretOffset - caretLine.Offset - (originalLine.Length - originalLine.TrimStart(' ', '\t').Length));
        using (document.RunUpdate())
            foreach (var edit in edits.Reverse()) document.Replace(edit.Offset, edit.Length, edit.Text);
        if (selected) Editor.Select(start, end - start + edits.Sum(edit => edit.Text.Length - edit.Length));
        else
        {
            var newLine = document.GetLineByNumber(caretLineNumber);
            var text = document.GetText(newLine);
            Editor.CaretOffset = Math.Min(newLine.EndOffset, newLine.Offset + text.Length - text.TrimStart(' ', '\t').Length + caretColumn);
        }
        RefreshFoldings();
        Editor.Focus();
        ModeStatus.Text = selected ? "Selection formatted · Ctrl+Z to undo" : "Code formatted · Ctrl+Z to undo";
    }
}
