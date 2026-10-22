using System.Text.RegularExpressions;

namespace TikzEdt.Modern;

internal static class SnippetPreviewConfiguration
{
    public static void Validate(string engine, PreambleSnapshot preamble, UserSettings settings)
    {
        settings.Validate();
        if (engine != "pdflatex") return;
        var source = Regex.Replace(preamble.Content ?? "", @"(?m)(?<!\\)%.*$", "");
        var packages = Regex.Matches(source, @"\\(?:usepackage|RequirePackage)(?:\s*\[[^\]]*\])?\s*\{([^}]+)\}")
            .SelectMany(match => match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries));
        var incompatible = packages.FirstOrDefault(package => package is "fontspec" or "polyglossia" or "unicode-math");
        if (incompatible is not null)
            throw new InvalidOperationException($"Snippet build uses pdfLaTeX, but preamble '{preamble.DisplayName}' loads {incompatible}, which requires LuaLaTeX or XeLaTeX. Select one of those engines, then choose Build → Compile all snippet previews to start with the new engine. Resume keeps the saved build's engine. No snippets were attempted.");
    }

    public static async Task ValidateAsync(string engine, PreambleSnapshot preamble, UserSettings settings, CancellationToken token)
    {
        Validate(engine, preamble, settings);
        var probe = new SnippetItem { Name = "Preamble check", Sample = "\\begin{tikzpicture}\\path (0,0) rectangle (0.1,0.1);\\end{tikzpicture}" };
        try { await SnippetLibrary.RenderPreviewAsync(probe, engine, preamble, settings, token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            var summary = string.Join("\n", exception.Message.Split('\n')
                .Where(line => line.Contains("Error", StringComparison.OrdinalIgnoreCase) || line.StartsWith('!')).Take(4));
            if (string.IsNullOrWhiteSpace(summary)) summary = exception.Message[..Math.Min(400, exception.Message.Length)];
            throw new InvalidOperationException($"Snippet preamble check failed with {engine} ({preamble.DisplayName}). Fix the compiler/preamble before retrying; the catalogue was not compiled.\n{summary}", exception);
        }
    }
}
