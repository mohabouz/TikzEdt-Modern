using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using TikzEdt.Modern;

internal static class ChemistryEditingRegression
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    public static void Run()
    {
        foreach (var source in new[] { "C(-H)(-H)(-H)(-H)-H", "C(~N)~N", "CH_3=O", "N(-H)(-H)(-H)-H", @"\charge{0=\|,90=\|,180=\|,270=\|}{C}-H" })
            Require(ChemfigChemistry.Analyze(ChemfigMolecule.Parse(source)).Count > 0, "Missing warning: " + source);
        foreach (var source in new[] { "CH_3-O-H", "H_3C-H", @"H-\charge{90=\|,270=\|}{O}-H", "P(-H)(-H)(-H)(-H)-H", "S(-H)(-H)(-H)(-H)(-H)-H", "COOH(-H)(-H)(-H)(-H)-H" })
            Require(ChemfigChemistry.Analyze(ChemfigMolecule.Parse(source)).Count == 0, "Incorrect warning: " + source);
        var ammonium = ChemfigMolecule.Parse("N(-H)(-H)(-H)-H"); ammonium.Atoms[0] = ammonium.Atoms[0] with { Label = "N^{+}" };
        Require(ChemfigChemistry.Analyze(ammonium).Count == 0, "Ammonium charge was ignored.");
        Require(new UserSettings().DefaultLonePairStyle == "Line", "New settings must default to lines.");
        foreach (var style in new[] { "Line", "Dots" })
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(JsonSerializer.Serialize(new UserSettings { DefaultLonePairStyle = style }))!;
            settings.Validate(); Require(settings.Clone().DefaultLonePairStyle == style, "Lone-pair preference did not persist/clone.");
        }
        string Format(string value)
        {
            foreach (var edit in TikzCodeFormatter.CreateEdits(value, 4, true).Reverse()) value = value.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Text);
            return value;
        }
        const string inline = @"\begin{tikzpicture}[scale=2]\draw (0,0)--(1,1);\begin{scope}\node at (0,0) {text};\end{scope}\end{tikzpicture}";
        var formatted = Format(inline);
        Require(formatted == "\\begin{tikzpicture}[scale=2]\n    \\draw (0,0)--(1,1);\n    \\begin{scope}\n        \\node at (0,0) {text};\n    \\end{scope}\n\\end{tikzpicture}", "Missing structural breaks: " + formatted);
        Require(Format(formatted) == formatted, "Structural formatting is not idempotent.");
        foreach (var literal in new[] { @"\newcommand{\x}{\begin{scope}\draw (0,0);\end{scope}}", @"% \begin{tikzpicture}\draw (0,0);", @"\verb|\begin{scope}\draw x;|", "\\begin{verbatim}\n\\draw x;\\end{scope}\n\\end{verbatim}" })
            Require(Format(literal) == literal, "Formatter changed literal/macro/comment: " + literal);
        Exception? failure = null;
        var thread = new Thread(() => { try { Ui(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (failure is not null) throw failure;
        Console.WriteLine("Advisory valence/octet warnings, lone-pair preferences/import preservation, structural formatting, Ctrl+/ and rectangular multiline editing passed.");
    }
    private static void Ui()
    {
        var app = new App(); app.InitializeComponent();
        var original = SettingsStore.Current.DefaultLonePairStyle;
        try
        {
            foreach (var style in new[] { "Line", "Dots" })
            {
                SettingsStore.Current.DefaultLonePairStyle = style;
                var molecule = ChemfigMolecule.Parse(@"\charge{90=\:,180=\|}{O}");
                var dialog = new ChemfigEditorWindow(molecule, "pdflatex", "", null);
                Require(((ComboBox)Field(dialog, "_pairStyle")).SelectedIndex == (style == "Line" ? 0 : 1), "Chemfig ignored default style.");
                Require(dialog.Result.Contains(@"90=\:,180=\|"), "Preference rewrote imported pairs."); dialog.Close();
                var settings = new SettingsDialog();
                Require(((UserSettings)settings.DataContext).DefaultLonePairStyle == style, "Settings dialog lost preference."); settings.Close();
            }
            var warning = new ChemfigEditorWindow(ChemfigMolecule.Parse("C(-H)(-H)(-H)(-H)-H"), "pdflatex", "", null);
            warning.GetType().GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(warning, null);
            Require(((ScrollViewer)Field(warning, "_warningPanel")).Visibility == Visibility.Visible && ((TextBlock)Field(warning, "_chemistryWarnings")).Text.Contains("total 5"), "C five-bond warning not shown.");
            warning.Close();
            SettingsStore.Current.AutomaticPreview = false;
            var main = new MainWindow(); var editor = (TextEditor)main.FindName("Editor");
            editor.Text = "  alpha\n  beta\n  gamma"; editor.Select(0, editor.Document.GetLineByNumber(3).Offset);
            Require(main.HandleSourceEditorKey(Key.Oem2, ModifierKeys.Control), "Ctrl+/ not handled.");
            Require(editor.Text == "  % alpha\n  % beta\n  gamma", "Comment line range/indent failed: " + editor.Text);
            main.ToggleSourceComment(); Require(editor.Text == "  alpha\n  beta\n  gamma", "Uncomment failed.");
            editor.Undo(); Require(editor.Text == "  % alpha\n  % beta\n  gamma", "Comment operation not one undo step.");
            editor.Text = "abc\ndef\nghi"; editor.CaretOffset = 1;
            var content = (FrameworkElement)main.Content; content.Measure(new Size(1280, 850)); content.Arrange(new Rect(0,0,1280,850)); content.UpdateLayout();
            main.HandleSourceEditorKey(Key.Down, ModifierKeys.Control | ModifierKeys.Alt);
            main.HandleSourceEditorKey(Key.Down, ModifierKeys.Control | ModifierKeys.Alt);
            Require(editor.TextArea.Selection is RectangleSelection && editor.TextArea.Selection.Segments.Count() == 3, "Multiline selection not extended to three lines.");
            editor.TextArea.Selection.ReplaceSelectionWithText("X");
            Require(editor.Text == "aXbc\ndXef\ngXhi", "Multiline insertion failed: " + editor.Text);
            main.HandleSourceEditorKey(Key.Escape, ModifierKeys.None);
            Require(editor.TextArea.Selection is not RectangleSelection, "Escape did not leave multiline mode.");
            main.Close();
        }
        finally { SettingsStore.Current.DefaultLonePairStyle = original; app.Shutdown(); }
    }
}
