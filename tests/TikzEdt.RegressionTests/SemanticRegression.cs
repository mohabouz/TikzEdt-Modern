using System.Windows;
using ICSharpCode.AvalonEdit;
using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;

internal static class SemanticRegression
{
    public static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var source = "\\begin{tikzpicture}\n\\draw[pattern=north east lines,line cap=roun] (0,0)--(1,1);\n\\end{tikzpicture}";
        var issues = TexSemantics.Analyze(TexKnowledge.Build(source)); Check(issues.Count == 2, "Expected library and enum warnings.");
        var library = issues.First(i => i.Message.Contains("library")).Fix!;
        var updated = source.Insert(library.Offset, library.Replacement); Check(!TexSemantics.Analyze(TexKnowledge.Build(updated)).Any(i => i.Message.Contains("library")), "Library fix did not resolve warning.");
        Check(TexSemantics.Analyze(TexKnowledge.Build(source, @"\usetikzlibrary{patterns}")).Count == 1, "External preamble not honored.");
        var valid = @"\draw[my key=anything,anchor=my custom anchor,line cap=\mycap] (0,0); \node {text [pattern=x]}; % \includegraphics{x}";
        Check(TexSemantics.Analyze(TexKnowledge.Build(valid)).Count == 0, "Dynamic/custom/label/comment false positive.");
        var full = "\\documentclass{article}\n\\begin{document}\n\\includegraphics{x}\n\\end{document}";
        var package = TexSemantics.Analyze(TexKnowledge.Build(full)).Single().Fix!; Check(package.Offset == full.IndexOf(@"\begin{document}"), "Package inserted into document body.");
        Exception? error = null; var thread = new Thread(() => { try {
            var app = new App(); app.InitializeComponent(); var main = new MainWindow(); var editor = (TextEditor)main.FindName("Editor"); editor.Text = source;
            Check(main.ApplySemanticFix(library, source) && editor.Text.StartsWith(@"\usetikzlibrary{patterns}"), "UI fix not applied."); editor.Undo(); Check(editor.Text == source, "Quick fix is not one undo step.");
            editor.Text += " "; Check(!main.ApplySemanticFix(library, source), "Stale fix changed source."); main.Close(); app.Shutdown();
        } catch (Exception e) { error = e; } }); thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); if (error is not null) throw error;
        Console.WriteLine("B2: missing-library/package hints, closed enum validation, indirect-load preamble, custom-key safety, preamble insertion, undo and stale-fix refusal passed.");
    }
}
