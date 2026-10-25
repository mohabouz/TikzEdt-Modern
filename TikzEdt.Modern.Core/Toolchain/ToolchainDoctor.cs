using System.Diagnostics;
using System.Text;

namespace TikzEdt.Modern.Core.Toolchain;

public enum ProbeState { Found, Missing, Broken, TimedOut }
public sealed record ToolProbe(string Name, string Command, ProbeState State, string Detail);
public sealed record ProbeProcess(int ExitCode, string Output);

/// <summary>Read-only executable and package discovery; never installs tools or invokes a shell.</summary>
public static class ToolchainDoctor
{
    public static string? Resolve(string command, string? searchPath = null)
    {
        command = command.Trim().Trim('"');
        if (command.Length == 0) return null;
        var suffixes = OperatingSystem.IsWindows() && !Path.HasExtension(command) ? new[] { "", ".exe", ".com", ".cmd", ".bat" } : new[] { "" };
        var explicitPath = Path.IsPathRooted(command) || command.IndexOfAny(new[] { '/', '\\' }) >= 0;
        var directories = explicitPath ? new[] { "" } : (searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var directory in directories)
            foreach (var suffix in suffixes)
            {
                try
                {
                    var candidate = explicitPath ? command + suffix : Path.Combine(directory.Trim().Trim('"'), command + suffix);
                    if (File.Exists(candidate)) return Path.GetFullPath(candidate);
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException or UnauthorizedAccessException) { }
            }
        return null;
    }

    public static async Task<ToolProbe> ProbeAsync(string name, string command, IReadOnlyList<string> arguments,
        TimeSpan timeout, CancellationToken token = default, bool package = false,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProbeProcess>>? runner = null)
    {
        token.ThrowIfCancellationRequested();
        var executable = Resolve(command);
        if (executable is null) return new(name, command, ProbeState.Missing, "Not found. Configure its executable path or install it separately, then restart TikzEdt if PATH changed.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(timeout);
        try
        {
            var result = await (runner ?? RunAsync)(executable, arguments, deadline.Token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var lines = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            var detail = lines.FirstOrDefault(s => s.Any(char.IsDigit)) ?? lines.FirstOrDefault() ?? "No output.";
            var found = result.ExitCode == 0 && (!package || lines.Length > 0);
            return new(name, executable, found ? ProbeState.Found : package ? ProbeState.Missing : ProbeState.Broken,
                found ? (package ? lines.First() : detail) : package ? "Package file not located. Use this TeX distribution's package manager; no package was installed." : $"Exit {result.ExitCode}: {detail}");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(name, executable, ProbeState.TimedOut, "Version/package check timed out. The program may be waiting for configuration. No installation was requested."); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        { return new(name, executable, ProbeState.Broken, e.Message); }
    }

    public static async Task<ProbeProcess> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        process.Start();
        async Task<string> ReadBoundedAsync(StreamReader reader)
        {
            var text = new StringBuilder(); var buffer = new char[2048]; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
                if (text.Length < 16384) text.Append(buffer, 0, Math.Min(count, 16384 - text.Length));
            return text.ToString();
        }
        var stdout = ReadBoundedAsync(process.StandardOutput); var stderr = ReadBoundedAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            return new(process.ExitCode, (await stdout.ConfigureAwait(false)) + "\n" + (await stderr.ConfigureAwait(false)));
        }
        finally
        {
            if (!process.HasExited) { try { process.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { } }
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }
}
