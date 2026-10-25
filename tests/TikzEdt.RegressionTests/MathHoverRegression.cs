using TikzEdt.Modern;
using TikzEdt.Modern.Core.Documents;

internal static class MathHoverRegression
{
    public static void Run()
    {
        static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        foreach (var sample in new[] { "$x^2$", "$$x^2$$", @"\(x^2\)", @"\[x^2\]", @"\begin{equation*}x^2\end{equation*}", @"\begin{align}x^2&=y\end{align}" })
            Require(MathContext.Find(sample, sample.IndexOf('x'))?.Body.Contains("x^2") == true, "Math range missing: " + sample);
        var comments = "% $bad$\n\\node { $good$ };";
        Require(MathContext.Find(comments, comments.IndexOf("bad")) is null && MathContext.Find(comments, comments.IndexOf("good"))?.Body == "good", "Comment masking failed.");
        foreach (var sample in new[] { @"\verb|$x$|", @"\begin{minted}{tex}$x$\end{minted}", @"\begin{verbatim}$x$\end{verbatim}", @"\$x\$", "$unclosed" })
            Require(MathContext.Find(sample, sample.IndexOf('x') >= 0 ? sample.IndexOf('x') : sample.Length) is null, "Escaped/code/unclosed math offered a preview.");
        var source = "% \\documentclass{bad}\n\\documentclass{article}\n\\newcommand{\\RR}{\\mathbb{R}}\n\\begin{document}\n$x$";
        var preamble = MainWindow.MathPreviewPreamble(source, @"\usepackage{xcolor}");
        Require(preamble.StartsWith(@"\documentclass{article}") && preamble.Contains(@"\newcommand{\RR}") && !preamble.Contains(@"\begin{document}"), "Document-class ordering or inline macros lost.");
        var settings = MainWindow.MathPreviewSettings(new UserSettings { CompilerFlags = "-shell-escape -synctex=1" });
        Require(!UserSettings.ParseFlags(settings.CompilerFlags).Contains("-shell-escape") && settings.CompilerFlags.Contains("-no-shell-escape"), "Hover enabled shell escape.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { MathContext.Find("$x$", 1, cancelled.Token); throw new Exception("Math scanning ignored cancellation."); } catch (OperationCanceledException) { }
        Console.WriteLine("A5: inline/display/environment math, escaped delimiters, comments/code exclusions, macros, document-class ordering, disabled shell escape and cancellation passed.");
    }
    public static async Task IntegrationAsync()
    {
        var source = "\\newcommand{\\RR}{\\mathbb{R}}\n\\begin{tikzpicture}\n\\node {$\\frac{\\RR}{2}$};\n\\end{tikzpicture}";
        var slice = MathContext.Find(source, source.IndexOf(@"\frac"))!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        foreach (var engine in new[] { "pdflatex", "lualatex", "xelatex" })
        {
            var result = await TexVectorRenderer.RenderAsync(MainWindow.MathPreviewSource(slice), engine, null, MainWindow.MathPreviewPreamble(source, ""), timeout.Token, MainWindow.MathPreviewSettings(new UserSettings()));
            if (result.ExitCode != 0 || result.Image is null || !result.Image.IsFrozen) throw new Exception(engine + ": " + result.Log);
            Console.WriteLine("A5 real vector math + user macro preview passed: " + engine);
        }
    }
}
