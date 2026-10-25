using System.IO;
using TikzEdt.Modern.Core.Toolchain;

internal static class ToolchainRegression
{
    public static async Task RunAsync()
    {
        static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
        var executable = Environment.ProcessPath!;
        var prefix = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? new[] { typeof(ToolchainRegression).Assembly.Location, "--probe-fixture" } : new[] { "--probe-fixture" };
        var found = await ToolchainDoctor.ProbeAsync("fixture", executable, prefix, TimeSpan.FromSeconds(10));
        Require(found.State == ProbeState.Found && found.Detail.Contains("1.2.3"), "Installed executable/version probe failed.");
        var missing = await ToolchainDoctor.ProbeAsync("absent", "tikzedt-does-not-exist-64b918", [], TimeSpan.FromSeconds(1));
        Require(missing.State == ProbeState.Missing, "Missing executable not identified.");
        var broken = await ToolchainDoctor.ProbeAsync("fixture", executable, prefix.Append("broken").ToArray(), TimeSpan.FromSeconds(10));
        Require(broken.State == ProbeState.Broken && broken.Detail.Contains("7"), "Broken executable not identified.");
        var timed = await ToolchainDoctor.ProbeAsync("fixture", executable, prefix.Append("sleep").ToArray(), TimeSpan.FromMilliseconds(200));
        Require(timed.State == ProbeState.TimedOut, "Timeout not enforced.");
        using var cancellation = new CancellationTokenSource(150);
        try { await ToolchainDoctor.ProbeAsync("fixture", executable, prefix.Append("sleep").ToArray(), TimeSpan.FromSeconds(10), cancellation.Token); throw new Exception("Cancellation was swallowed."); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        var flood = await ToolchainDoctor.RunAsync(executable, prefix.Append("flood").ToArray(), CancellationToken.None);
        Require(flood.Output.Length <= 32769, "Probe output is unbounded.");
        var package = await ToolchainDoctor.ProbeAsync("missing.sty", executable, [], TimeSpan.FromSeconds(1), package: true,
            runner: (_, _, _) => Task.FromResult(new ProbeProcess(1, "")));
        Require(package.State == ProbeState.Missing, "Missing package not distinguished from program failure.");
        var root = Path.GetFullPath("artifacts/toolchain-regression"); Directory.CreateDirectory(root);
        var spaced = Path.Combine(root, "folder with spaces"); Directory.CreateDirectory(spaced);
        var marker = Path.Combine(spaced, "probe.exe"); File.WriteAllText(marker, "fixture");
        Require(ToolchainDoctor.Resolve("probe", spaced) == marker && ToolchainDoctor.Resolve('"' + marker + '"') == marker, "Quoted or space-containing paths did not resolve.");
        Require(ToolchainDoctor.Resolve(" ", spaced) is null, "Blank executable resolved unexpectedly.");
        Console.WriteLine("Toolchain A1: found/missing/broken/package states, timeouts, cancellation, bounded output, quoted paths and spaces passed.");
    }
    public static async Task InstalledAsync()
    {
        foreach (var command in new[] { "pdflatex", "lualatex", "xelatex", "kpsewhich", "synctex", "pdftocairo", "pdfinfo" })
        {
            var result = await ToolchainDoctor.ProbeAsync(command, command, new[] { command is "pdftocairo" or "pdfinfo" ? "-v" : "--version" }, TimeSpan.FromSeconds(15));
            Console.WriteLine($"{command}: {result.State} · {result.Detail}");
            if (result.State != ProbeState.Found) throw new Exception("Installed tool probe failed: " + command);
        }
        foreach (var package in new[] { "standalone.cls", "tikz.sty", "chemfig.sty", "circuitikz.sty", "pst-labo.sty" })
        {
            var result = await ToolchainDoctor.ProbeAsync(package, "kpsewhich", new[] { package }, TimeSpan.FromSeconds(15), package: true);
            if (result.State != ProbeState.Found) throw new Exception("Installed package probe failed: " + package + " " + result.Detail);
        }
        Console.WriteLine("Installed TeX/converter and package discovery checks passed.");
    }
}
