using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    private void InitializeEditorActions()
    {
        var menu = new ContextMenu();
        Editor.Options.EnableRectangularSelection = true;
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
        var format = new MenuItem { Header = "Format Code", InputGestureText = "Ctrl+Shift+F", ToolTip = "Add structural line breaks and indent the selection, or the document when nothing is selected" };
        format.Click += FormatCode_Click;
        menu.Items.Add(format);
        var comment = new MenuItem { Header = "Toggle Line Comment", InputGestureText = "Ctrl+/" };
        comment.Click += (_, _) => ToggleSourceComment(); menu.Items.Add(comment);
        var column = new MenuItem { Header = "Extend Column Selection Down", InputGestureText = "Ctrl+Alt+↓", ToolTip = "Type to edit the same column on several lines; Alt+Shift+arrows or Alt+drag also work. Escape returns to one caret." };
        column.Click += (_, _) => HandleSourceEditorKey(Key.Down, ModifierKeys.Control | ModifierKeys.Alt); menu.Items.Add(column);
        var fold = new MenuItem { Header = "Fold All", InputGestureText = "Ctrl+Shift+[" };
        fold.Click += FoldAll_Click;
        menu.Items.Add(fold);
        var unfold = new MenuItem { Header = "Unfold All", InputGestureText = "Ctrl+Shift+]" };
        unfold.Click += UnfoldAll_Click;
        menu.Items.Add(unfold);
        var chemistry = new MenuItem { Header = "Edit / insert Chemfig molecule…", ToolTip = "Place the caret inside a supported Chemfig command to edit its atoms and bonds." };
        chemistry.Click += Chemfig_Click; menu.Items.Add(new Separator()); menu.Items.Add(chemistry);
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
        var edits = TikzCodeFormatter.CreateEditsForRange(Editor.Text, start, end, settings.TabSize, settings.ConvertTabsToSpaces).ToArray();
        if (edits.Length == 0) { ModeStatus.Text = "Code indentation is already formatted"; return; }
        var charactersBeforeCaret = Editor.Text[..Editor.CaretOffset].Count(c => !char.IsWhiteSpace(c));
        using (document.RunUpdate())
            foreach (var edit in edits.Reverse()) document.Replace(edit.Offset, edit.Length, edit.Text);
        if (selected) Editor.Select(start, end - start + edits.Sum(edit => edit.Text.Length - edit.Length));
        else
        {
            var target = 0; var seen = 0;
            while (target < document.TextLength && seen < charactersBeforeCaret) { if (!char.IsWhiteSpace(document.GetCharAt(target))) seen++; target++; }
            Editor.CaretOffset = target;
        }
        RefreshFoldings();
        Editor.Focus();
        ModeStatus.Text = selected ? "Selection formatted · Ctrl+Z to undo" : "Code formatted · Ctrl+Z to undo";
    }
}
