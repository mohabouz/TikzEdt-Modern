using System.IO;
using System.Windows;
using ICSharpCode.AvalonEdit;
using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;

internal static class UserSnippetRegression
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run()
    {
        Require(UserSnippets.Parse(UserSnippets.Example).Count == 2, "Default snippet schema invalid.");
        var expanded = UserSnippets.Expand("$2 ${1:α} ${1} $0"); Require(expanded.Text == " α α " && expanded.Fields.Count == 3 && expanded.Caret == 5, "Numbered defaults/mirrors/caret failed.");
        Require(UserSnippets.Expand(@"\${literal} ${SELECTION}", "x$1").Text == "${literal} x$1", "Escaped dollar or selection was reinterpreted.");
        Require(UserSnippets.Expand(@"${1:\frac{a}{b}}=${1}").Text == @"\frac{a}{b}=\frac{a}{b}", "LaTeX braces inside defaults broken.");
        foreach (var invalid in new[] { "${1", "${1:${2:x}}", "${1|a,b|}", "$100", "$0 $0" })
            try { UserSnippets.Expand(invalid); throw new Exception("Invalid snippet accepted: " + invalid); } catch (FormatException) { }
        var root = Path.GetFullPath("artifacts/user-snippet-regression/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, ".tikzedt"));
        var global = Path.Combine(root, "global.json"); File.WriteAllText(global, "[{\"name\":\"Global\",\"prefix\":\"tnode\",\"body\":\"global\"}]");
        File.WriteAllText(Path.Combine(root, ".tikzedt", "snippets.json"), "[{\"name\":\"Project\",\"prefix\":\"tnode\",\"body\":\"project\"}]");
        Require(UserSnippets.Load(global, root).Items.Single(s => s.Prefix == "tnode").Body == "project", "Project/global precedence failed.");
        File.WriteAllText(Path.Combine(root, ".tikzedt", "snippets.json"), "broken"); var catalog = UserSnippets.Load(global, root);
        Require(catalog.Issues.Count == 1 && catalog.Items.Single(s => s.Prefix == "tnode").Body == "global", "Invalid project file hid valid lower-layer snippets.");
        Exception? error = null; var thread = new Thread(() => { try { Ui(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error is not null) throw error;
        Console.WriteLine("A4: JSON validation/layering, escapes, balanced TeX defaults, selection, numbered field order, live mirrored edits, one-step undo and Tab-prefix expansion passed.");
    }
    static void Ui()
    {
        var app = new App(); app.InitializeComponent(); var main = new MainWindow(); var editor = (TextEditor)main.FindName("Editor");
        editor.Text = ""; main.InsertUserSnippet(new("linked", "linked", "${2:second} ${1:first} ${1} $0"), 0, 0);
        Require(editor.SelectedText == "first", "Tab order must start at field 1, not physical source order.");
        editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, "changed");
        Require(editor.Text == "second changed changed ", "Linked field edit did not update its mirror.");
        editor.Text = "before"; editor.Select(0, editor.Text.Length);
        main.InsertUserSnippet(new("wrap", "wrap", @"\textbf{${SELECTION}}$0"), 0, editor.Text.Length);
        Require(editor.Text == @"\textbf{before}", "Selection wrapping failed."); editor.Undo(); Require(editor.Text == "before", "Snippet insertion is not one undo step.");
        editor.Text = "abc"; var popupSnippet = new UserSnippetCompletion(new("popup", "abc", "${1:field}"), segment => main.InsertUserSnippet(new("popup", "abc", "${1:field}"), segment.Offset, segment.Length));
        popupSnippet.Complete(editor.TextArea, new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = 0, Length = 3 }, EventArgs.Empty); Require(editor.Text == "field" && editor.SelectedText == "field", "Completion did not preserve linked snippet editing.");
        main.FinishUserSnippet(); editor.Text = "tnode"; editor.Select(editor.Text.Length, 0); Require(main.TryExpandUserSnippet() && editor.Text.StartsWith(@"\node"), "Literal prefix+Tab expansion failed.");
        main.Close(); app.Shutdown();
    }
}
