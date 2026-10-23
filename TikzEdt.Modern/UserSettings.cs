using System.IO;
using System.Text.Json;

namespace TikzEdt.Modern;

public sealed class UserSettings
{
    public string EditorFont { get; set; } = "Consolas";
    public double EditorFontSize { get; set; } = 14;
    public string EditorStyle { get; set; } = "Dark";
    public bool ShowLineNumbers { get; set; } = true;
    public bool WordWrap { get; set; }
    public bool HighlightCurrentLine { get; set; } = true;
    public bool SyntaxHighlighting { get; set; } = true;
    public bool AutomaticCompletion { get; set; } = true;
    public int TabSize { get; set; } = 4;
    public bool ConvertTabsToSpaces { get; set; } = true;
    public bool AutomaticPreview { get; set; } = true;
    public int PreviewDelayMs { get; set; } = 150;
    public string DefaultEngine { get; set; } = "pdflatex";
    public bool RememberLastEngine { get; set; } = true;
    public string LastUsedEngine { get; set; } = "pdflatex";
    public string StartupEngine => RememberLastEngine ? LastUsedEngine : DefaultEngine;
    public string PdfLatexPath { get; set; } = "pdflatex";
    public string LuaLatexPath { get; set; } = "lualatex";
    public string XeLatexPath { get; set; } = "xelatex";
    public string CompilerFlags { get; set; } = "-synctex=1";
    public int CompilerTimeoutSeconds { get; set; } = 120;
    public int SnippetTimeoutSeconds { get; set; } = 45;
    public bool UsePrecompiledFormats { get; set; } = true;
    public double PreviewDpi { get; set; } = 300;
    public string CoordinateSystem { get; set; } = "Auto";
    public double GridStep { get; set; } = 0.5;
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public string DefaultLonePairStyle { get; set; } = "Line";

    public string ResolveEngine(string engine) => engine switch
    {
        "lualatex" => LuaLatexPath,
        "xelatex" => XeLatexPath,
        _ => PdfLatexPath
    };

    public string CompilerIdentity => string.Join("\n", PdfLatexPath, LuaLatexPath,
        XeLatexPath, CompilerFlags, UsePrecompiledFormats);

    public UserSettings Clone() => (UserSettings)MemberwiseClone();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EditorFont)) throw new ArgumentException("Enter an editor font family.");
        if (EditorStyle is not ("Dark" or "Light" or "High contrast")) throw new ArgumentException("Choose a supported editor color style.");
        if (!double.IsFinite(EditorFontSize) || EditorFontSize is < 8 or > 72) throw new ArgumentException("Font size must be 8–72.");
        if (TabSize is < 1 or > 16) throw new ArgumentException("Tab size must be 1–16.");
        if (PreviewDelayMs is < 100 or > 10000) throw new ArgumentException("Preview delay must be 100–10000 milliseconds.");
        if (CompilerTimeoutSeconds is < 5 or > 1800 || SnippetTimeoutSeconds is < 5 or > 600)
            throw new ArgumentException("Use 5–1800 seconds for compilation and 5–600 seconds for snippets.");
        if (!double.IsFinite(PreviewDpi) || PreviewDpi is < 96 or > 1200) throw new ArgumentException("Preview DPI must be 96–1200.");
        if (new[] { PdfLatexPath, LuaLatexPath, XeLatexPath }.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Enter an executable path or command for each compiler.");
        if (DefaultEngine is not ("pdflatex" or "lualatex" or "xelatex")) throw new ArgumentException("Choose a supported engine.");
        if (LastUsedEngine is not ("pdflatex" or "lualatex" or "xelatex")) LastUsedEngine = DefaultEngine;
        if (CoordinateSystem is not ("Auto" or "Cartesian" or "Polar")) throw new ArgumentException("Choose Auto, Cartesian, or Polar coordinates.");
        if (GridStep is not (0.1 or 0.2 or 0.5 or 1)) throw new ArgumentException("Choose a grid step of 0.1, 0.2, 0.5, or 1.");
        if (DefaultLonePairStyle is not ("Line" or "Dots")) throw new ArgumentException("Choose Line or Dots for lone pairs.");
        _ = ParseFlags(CompilerFlags);
    }

    internal static IReadOnlyList<string> ParseFlags(string text)
    {
        var result = new List<string>();
        var token = new System.Text.StringBuilder();
        var quoted = false;
        foreach (var character in text)
        {
            if (character == '"') { quoted = !quoted; continue; }
            if (char.IsWhiteSpace(character) && !quoted)
            {
                if (token.Length > 0) { result.Add(token.ToString()); token.Clear(); }
            }
            else token.Append(character);
        }
        if (quoted) throw new ArgumentException("Compiler flags contain an unclosed double quote.");
        if (token.Length > 0) result.Add(token.ToString());
        foreach (var argument in result)
        {
            var name = argument.TrimStart('-').Split('=')[0].ToLowerInvariant();
            if (!argument.StartsWith('-') || name is "output-directory" or "aux-directory" or "jobname" or "fmt" or "ini" or "draftmode" or "output-format" or "interaction" or "help" or "version")
                throw new ArgumentException($"Flag '{argument}' conflicts with preview compilation. Use flags such as -synctex=1 or -shell-escape; quote values containing spaces.");
        }
        return result;
    }
}

internal static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TikzEdt", "Modern", "preferences.json");

    public static UserSettings Current { get; private set; } = Load();

    public static void Save(UserSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true);
        Current = settings.Clone();
    }

    private static UserSettings Load()
    {
        try
        {
            var json = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
            var settings = json is not null
                ? JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings()
                : new UserSettings();
            // Preferences saved before engine history existed should retain their
            // configured default on the first upgraded launch.
            if (json is not null)
            {
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty(nameof(UserSettings.LastUsedEngine), out _))
                    settings.LastUsedEngine = settings.DefaultEngine;
            }
            settings.Validate();
            return settings;
        }
        catch (Exception exception) when (exception is IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }
}
