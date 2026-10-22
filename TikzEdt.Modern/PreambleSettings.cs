using System.IO;
using System.Text.Json;

namespace TikzEdt.Modern;

internal sealed record PreambleSnapshot(string? FilePath, string? Content)
{
    public bool UsesBuiltIn => string.IsNullOrWhiteSpace(FilePath);

    public string DisplayName => UsesBuiltIn
        ? "Built-in"
        : Path.GetFileName(FilePath!);

    public string CacheIdentity => (FilePath ?? "<built-in>") + "\n" + (Content ?? string.Empty);
}

internal static class PreambleSettings
{
    public const string StarterTemplate = """
% TikzEdt Modern custom preamble
% Add packages, TikZ libraries, colors and macros here.
% A document class is optional; snippets use TikzEdt's standalone class.

\usepackage{amsmath,amssymb,bm,xcolor}
\usetikzlibrary{arrows.meta,calc,positioning,shapes.geometric}

% Example:
% \definecolor{diagramblue}{HTML}{3574F0}
% \tikzset{every node/.style={font=\sffamily}}
""";

    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TikzEdt",
        "Modern");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public static string? SelectedFilePath => LoadSelectedFilePath();

    public static PreambleSnapshot ReadSnapshot()
    {
        var path = SelectedFilePath;
        if (string.IsNullOrWhiteSpace(path))
            return new PreambleSnapshot(null, null);

        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"The selected preamble file could not be found: {path}\nChoose another file from Tools > Preamble….",
                path);

        return new PreambleSnapshot(path, File.ReadAllText(path));
    }

    public static void UseFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("A preamble file path is required.", nameof(filePath));

        SaveSettings(new SettingsModel { PreambleFilePath = Path.GetFullPath(filePath) });
    }

    public static void UseBuiltIn() => SaveSettings(new SettingsModel());

    private static string? LoadSelectedFilePath()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return null;

            var settings = JsonSerializer.Deserialize<SettingsModel>(File.ReadAllText(SettingsPath));
            return string.IsNullOrWhiteSpace(settings?.PreambleFilePath)
                ? null
                : Path.GetFullPath(settings.PreambleFilePath);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void SaveSettings(SettingsModel settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    private sealed class SettingsModel
    {
        public string? PreambleFilePath { get; set; }
    }
}
