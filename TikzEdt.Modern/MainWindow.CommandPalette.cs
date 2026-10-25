using System.Windows;

namespace TikzEdt.Modern;

public partial class MainWindow
{
    internal IReadOnlyList<EditorAction> CreateActions()
    {
        EditorAction Action(string id, string name, string description, string shortcut, RoutedEventHandler execute, Func<bool>? enabled = null)
            => new(id, name, description, shortcut, () => execute(this, new RoutedEventArgs()), enabled);
        return new[]
        {
            Action("new", "New document", "Create an empty TikZ figure", "Ctrl+N", New_Click),
            Action("open", "Open document", "Choose a TikZ or TeX file", "Ctrl+O", Open_Click),
            Action("save", "Save document", "Save safely with a local history checkpoint", "Ctrl+S", Save_Click),
            Action("save-as", "Save as", "Save to another path", "", SaveAs_Click),
            Action("undo", "Undo", "Undo a source or graphical edit", "Ctrl+Z", Undo_Click, () => Editor.CanUndo),
            Action("redo", "Redo", "Redo an undone edit", "Ctrl+Y", Redo_Click, () => Editor.CanRedo),
            Action("compile", "Compile figure", "Render using the selected TeX engine", "F5", Compile_Click),
            Action("cancel", "Cancel compilation", "Cancel current figure compilation", "", CancelCompile_Click),
            Action("export", "Export figure", "Save SVG, PDF or a raster image", "", ExportFigure_Click),
            Action("copy-svg", "Copy SVG", "Copy vector artwork to the clipboard", "", CopySvg_Click),
            Action("import-image", "Import image asset", "Copy a PNG/JPEG/BMP/TIFF into assets and insert a relative reference", "", ImportImage_Click),
            Action("format", "Format code", "Format the selection or whole document", "Ctrl+Shift+F", FormatCode_Click),
            Action("quick-fix", "Quick fixes", "Apply an explicit, undoable semantic fix", "Ctrl+.", QuickFix_Click),
            Action("sync-preview", "Show in preview", "Locate source using SyncTeX; Alt+click figure navigates back", "Ctrl+Shift+J", ShowInPreview_Click),
            new EditorAction("comment", "Toggle line comment", "Comment or uncomment selected lines", "Ctrl+/", ToggleSourceComment),
            new EditorAction("find", "Find in document", "Search text or a regular expression", "Ctrl+F", () => _findReplace?.Open(false)),
            new EditorAction("replace", "Replace in document", "Replace text or regex matches", "Ctrl+H", () => _findReplace?.Open(true)),
            Action("fold", "Fold all", "Collapse code regions", "Ctrl+Shift+[", FoldAll_Click),
            Action("unfold", "Unfold all", "Expand code regions", "Ctrl+Shift+]", UnfoldAll_Click),
            Action("settings", "Settings", "Editor fonts, compiler and preview preferences", "", Settings_Click),
            Action("doctor", "Toolchain Doctor", "Find missing or broken engines, converters and packages", "", ToolchainDoctor_Click),
            Action("recovery", "Recover unsaved documents", "Open a persisted recovery draft", "", RecoverDocuments_Click),
            Action("history", "Version history", "Review and restore local file checkpoints", "", VersionHistory_Click, () => _filePath is not null),
            Action("reload", "Reload from disk", "Reload an externally edited file with a safety copy", "", ReloadDocument_Click, () => _filePath is not null),
            Action("preamble", "Edit preamble", "Select or edit packages, libraries and macros", "", Preamble_Click),
            Action("math-preview", "Preview math at caret", "Render the equation under the caret using the current preamble", "", PreviewMathAtCaret_Click),
            Action("snippets", "Browse snippets", "Open categorized snippets and previews", "", Snippets_Click),
            Action("user-snippets", "Insert user snippet", "Choose a snippet with linked Tab fields", "", InsertUserSnippet_Click),
            Action("global-snippets", "Edit global snippets", "Edit validated snippets used in every project", "", EditGlobalSnippets_Click),
            Action("project-snippets", "Edit project snippets", "Override global snippets in this folder", "", EditProjectSnippets_Click),
            Action("symbols", "Browse LaTeX symbols", "Find and insert a mathematical symbol", "", SymbolsSidebar_Click),
            Action("color", "Color picker", "Choose RGB/hex colors for TikZ", "", ColorPicker_Click),
            Action("chemfig", "Chemfig molecule editor", "Edit atoms, bonds, rings and lone pairs", "", Chemfig_Click),
            Action("laboratory", "PST-Labo apparatus editor", "Build laboratory diagrams", "", Labo_Click)
        };
    }
    private void CommandPalette_Click(object sender, RoutedEventArgs e)
    {
        var palette = new CommandPaletteWindow(CreateActions(), _sidebarFolder, path =>
        {
            if (!ConfirmDiscardChanges()) return;
            try { OpenTrackedDocument(path); }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { ModeStatus.Text = error.Message; }
        }) { Owner = this };
        if (palette.ShowDialog() == true && palette.Chosen is { Available: true } chosen) chosen.Execute();
    }
}
