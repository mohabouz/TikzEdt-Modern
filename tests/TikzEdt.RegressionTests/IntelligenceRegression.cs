using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;

internal static class IntelligenceRegression
{
    public static void Run()
    {
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var code = "\\usetikzlibrary{patterns,positioning}\n\\newcommand{\\hello}[2]{#1 #2}\n\\tikzset{box/.style={draw}}\n\\begin{tikzpicture}\n\\coordinate (origin) at (0,0);\n\\node[";
        var index = TexKnowledge.Build(code, @"\newcommand{\external}{x}");
        var items = TikzCompletionProvider.GetItems(code, code.Length, index);
        Check(items.Any(i => i.Text == "pattern=") && items.Any(i => i.Text == "box") && !items.Any(i => i.Text == "diamond"), "Library/style-aware options incorrect.");
        Check(TexKnowledge.Complete(index, 0).Any(e => e.Name == @"\hello" && e.Insertion == @"\hello{$0}{}") && index.Local.Any(e => e.Name == @"\external"), "Local/preamble macro signatures missing.");
        Check(TexKnowledge.Complete(TexKnowledge.Build(@"\usetikzlibrary{pat"), 19).Any(e => e.Name == "patterns"), "Library argument completion failed.");
        var coord = TexKnowledge.Build(code + "] at (or"); Check(TexKnowledge.Complete(coord, coord.Source.Length).Any(e => e.Name == "origin"), "Named coordinate completion failed.");
        var text = TexKnowledge.Build(@"\node {ordinary [text"); Check(TexKnowledge.Context(text, text.Source.Length) == "command", "Text brackets treated as option list.");
        var nested = TexKnowledge.Build(@"\node[font=\tiny"); Check(TexKnowledge.Context(nested, nested.Source.Length) == "option", "Macro inside option list broke context.");
        var value = @"\node[anchor=no"; var values = TikzCompletionProvider.GetItems(value, value.Length); Check(values.Any(i => i.Text == "north") && !values.Any(i => i.Text == "draw="), "Enumerated value context offered unrelated keys.");
        var multiword = @"\node[anchor=north ea"; Check(MainWindow.CompletionStart(multiword, multiword.Length, TexKnowledge.Build(multiword)) == multiword.IndexOf("north"), "Multiword completion replacement would duplicate text.");
        var labo = @"\pstTubeEssais["; Check(TikzCompletionProvider.GetItems(labo, labo.Length).Any(i => i.Text == "niveauLiquide2="), "Existing PST-Labo property catalogue not integrated.");
        var comment = TexKnowledge.Build("% \\node[draw"); Check(TexKnowledge.Complete(comment, comment.Source.Length).Count == 0, "Comments suggested code.");
        Check(TexKnowledge.Build(@"\begin{verbatim}\usepackage{evil}\end{verbatim}").Packages.Contains("evil") == false, "Verbatim indexed.");
        Check(TexKnowledge.Hover(index, code.IndexOf(@"\hello") + 2)?.Contains("2 argument") == true, "Macro hover signature missing.");
        var cts = new CancellationTokenSource(); cts.Cancel(); try { TexKnowledge.Build(code, token: cts.Token); throw new Exception("Cancellation ignored."); } catch (OperationCanceledException) { }
        Console.WriteLine("B1: package/library contexts, local/preamble macro signatures, styles, named coordinates, comment/verbatim exclusions, hover and cancellation passed.");
    }
}
