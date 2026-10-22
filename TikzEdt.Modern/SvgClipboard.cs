using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TikzEdt.Modern;

internal static class SvgClipboard
{
    public static async Task<string> ExportAsync(string pdfPath, CancellationToken cancellationToken)
    {
        var svgPath = Path.Combine(Path.GetDirectoryName(pdfPath)!, "TikzEdt-figure.svg");
        var start = new ProcessStartInfo("pdftocairo")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in new[] { "-svg", "-f", "1", "-l", "1", pdfPath, svgPath })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start SVG conversion.");
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        using var registration = cancellationToken.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
        await process.WaitForExitAsync(cancellationToken);
        await output;
        var errorText = await error;
        if (process.ExitCode != 0 || !File.Exists(svgPath))
            throw new IOException("SVG conversion failed: " + errorText);
        return svgPath;
    }

    public static void Copy(string svgPath, BitmapSource? bitmap)
    {
        var data = new DataObject();
        var svg = File.ReadAllText(svgPath);
        // Native SVG consumers (including Inkscape) read these registered
        // clipboard formats as UTF-8 bytes, rather than .NET strings.
        data.SetData("image/svg+xml", new MemoryStream(Encoding.UTF8.GetBytes(svg)), false);
        data.SetData("image/x-inkscape-svg", new MemoryStream(Encoding.UTF8.GetBytes(svg)), false);
        data.SetFileDropList(new StringCollection { svgPath });
        if (bitmap is not null)
        {
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            var stream = new MemoryStream();
            png.Save(stream);
            stream.Position = 0;
            data.SetData("PNG", stream, false);
            data.SetImage(bitmap);
        }
        Clipboard.SetDataObject(data, true);
    }
}
