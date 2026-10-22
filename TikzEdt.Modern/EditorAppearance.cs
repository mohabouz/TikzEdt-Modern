using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace TikzEdt.Modern;

internal static class EditorAppearance
{
    public static void Apply(TextEditor editor, UserSettings settings)
    {
        var light = settings.EditorStyle == "Light";
        var contrast = settings.EditorStyle == "High contrast";
        editor.FontFamily = new FontFamily(settings.EditorFont);
        editor.FontSize = settings.EditorFontSize;
        editor.WordWrap = settings.WordWrap;
        editor.ShowLineNumbers = settings.ShowLineNumbers;
        editor.Options.IndentationSize = settings.TabSize;
        editor.Options.ConvertTabsToSpaces = settings.ConvertTabsToSpaces;
        editor.Options.HighlightCurrentLine = settings.HighlightCurrentLine;
        editor.Background = Brush(light ? "#FAFAFA" : contrast ? "#000000" : "#1E1F22");
        editor.Foreground = Brush(light ? "#202124" : contrast ? "#FFFFFF" : "#DFE1E5");
        editor.LineNumbersForeground = Brush(light ? "#62666C" : "#9DA1A9");
        editor.TextArea.Caret.CaretBrush = editor.Foreground;
        editor.TextArea.SelectionBrush = Brush(light ? "#C4DAFF" : "#264F78");
        editor.TextArea.SelectionForeground = editor.Foreground;
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.TextView.CurrentLineBackground = Brush(light ? "#EFF2F5" : contrast ? "#242424" : "#25272B");
        editor.TextArea.TextView.CurrentLineBorder = new Pen(Brush(light ? "#DFE2E6" : "#303338"), 1);
        editor.SyntaxHighlighting = null;
        var syntaxPath = Path.Combine(AppContext.BaseDirectory, "TikzSyntax.xshd");
        if (!settings.SyntaxHighlighting || !File.Exists(syntaxPath)) return;
        using var reader = XmlReader.Create(syntaxPath);
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        if (light)
        {
            var colors = new Dictionary<string, string>
            {
                ["Comment"] = "#627460", ["String"] = "#23732C", ["MathString"] = "#873B92",
                ["TikzCommands"] = "#0059B3", ["OtherCommands"] = "#254FA4", ["TikzKeywords"] = "#9A4815",
                ["Environments"] = "#873B92", ["Digits"] = "#007B78"
            };
            foreach (var (name, color) in colors)
                if (definition.GetNamedColor(name) is { } named)
                    named.Foreground = new SimpleHighlightingBrush((Color)ColorConverter.ConvertFromString(color));
        }
        editor.SyntaxHighlighting = definition;
    }

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
