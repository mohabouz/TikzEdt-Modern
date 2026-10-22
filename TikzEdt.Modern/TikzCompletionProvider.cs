using System.Windows.Media;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace TikzEdt.Modern;

internal static class TikzCompletionProvider
{
    private static readonly TikzCompletionData[] Commands =
    [
        C("\\draw", "\\draw[$0] (0,0) -- (1,1);", "Draw a TikZ path", "Command", 100),
        C("\\node", "\\node[$0] at (0,0) {text};", "Place a node", "Command", 100),
        C("\\path", "\\path[$0] (0,0) -- (1,1);", "Create a path", "Command", 90),
        C("\\fill", "\\fill[$0] (0,0) circle (1);", "Fill a shape", "Command", 90),
        C("\\filldraw", "\\filldraw[$0] (0,0) rectangle (1,1);", "Fill and draw a shape", "Command", 90),
        C("\\coordinate", "\\coordinate ($0) at (0,0);", "Define a named coordinate", "Command", 80),
        C("\\clip", "\\clip ($0) rectangle (1,1);", "Set the clipping path", "Command", 70),
        C("\\foreach", "\\foreach \\x in {1,...,5} {\n    $0\n}", "Repeat TikZ commands", "Command", 80),
        C("\\tikzset", "\\tikzset{$0}", "Define reusable TikZ styles", "Command", 75),
        C("\\usetikzlibrary", "\\usetikzlibrary{$0}", "Load TikZ libraries", "Command", 75),
        C("\\begin{tikzpicture}", "\\begin{tikzpicture}\n    $0\n\\end{tikzpicture}", "Insert a TikZ picture", "Environment", 95),
        C("\\begin{scope}", "\\begin{scope}[$0]\n    \n\\end{scope}", "Insert a scoped group", "Environment", 85),
        C("\\end{scope}", "\\end{scope}", "Close the current scope", "Environment", 60)
    ];

    private static readonly TikzCompletionData[] Options =
    [
        C("draw=", "draw=$0", "Stroke color", "Option", 100),
        C("fill=", "fill=$0", "Fill color", "Option", 100),
        C("line width=", "line width=$0pt", "Explicit line width", "Option", 90),
        C("thin", "thin", "Thin stroke", "Option"), C("thick", "thick", "Thick stroke", "Option"),
        C("very thick", "very thick", "Very thick stroke", "Option"),
        C("dashed", "dashed", "Dashed stroke", "Option"), C("dotted", "dotted", "Dotted stroke", "Option"),
        C("rounded corners", "rounded corners=$0pt", "Round path corners", "Option"),
        C("->", "->", "Arrow at path end", "Option"), C("<-", "<-", "Arrow at path start", "Option"),
        C("<->", "<->", "Arrows at both ends", "Option"),
        C("opacity=", "opacity=$0", "Object opacity from 0 to 1", "Option"),
        C("rotate=", "rotate=$0", "Rotation in degrees", "Option"),
        C("scale=", "scale=$0", "Scale the object", "Option"),
        C("anchor=", "anchor=$0", "Node anchor", "Option"),
        C("above", "above", "Place node above", "Option"), C("below", "below", "Place node below", "Option"),
        C("left", "left", "Place node left", "Option"), C("right", "right", "Place node right", "Option"),
        C("inner sep=", "inner sep=$0pt", "Node inner padding", "Option"),
        C("outer sep=", "outer sep=$0pt", "Node outer padding", "Option"),
        C("font=", "font=$0", "Node font command", "Option"),
        C("blue", "blue", "Built-in color", "Color"), C("red", "red", "Built-in color", "Color"),
        C("green", "green", "Built-in color", "Color"), C("orange", "orange", "Built-in color", "Color"),
        C("black", "black", "Built-in color", "Color"), C("white", "white", "Built-in color", "Color")
    ];

    public static IReadOnlyList<TikzCompletionData> GetItems(string source, int caretOffset)
    {
        var beforeCaret = source[..Math.Clamp(caretOffset, 0, source.Length)];
        var lastOpenBracket = beforeCaret.LastIndexOf('[');
        var lastCloseBracket = beforeCaret.LastIndexOf(']');
        return lastOpenBracket > lastCloseBracket ? Options : Commands;
    }

    private static TikzCompletionData C(string text, string insertion, string description, string kind, double priority = 50) =>
        new(text, insertion, description, kind, priority);
}

internal sealed class TikzCompletionData(
    string text,
    string insertionText,
    string description,
    string kind,
    double priority) : ICompletionData
{
    public ImageSource? Image => null;
    public string Text { get; } = text;
    public object Content => $"{KindGlyph}  {Text}";
    public object Description => $"{kind}  ·  {description}";
    public double Priority { get; } = priority;

    private string KindGlyph => kind switch
    {
        "Environment" => "E",
        "Option" => "◇",
        "Color" => "●",
        _ => "λ"
    };

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        var caretMarker = insertionText.IndexOf("$0", StringComparison.Ordinal);
        var replacement = insertionText.Replace("$0", string.Empty, StringComparison.Ordinal);
        textArea.Document.Replace(completionSegment, replacement);
        textArea.Caret.Offset = completionSegment.Offset + (caretMarker >= 0 ? caretMarker : replacement.Length);
    }
}
