namespace TikzEdt.Modern.Core.Documents;

public sealed record ImageAsset(string FullPath, string RelativePath);
public static class ImageAssetStore
{
    public const int MaximumBytes = 20 * 1024 * 1024;
    public static ImageAsset SavePng(string documentPath, byte[] png)
    {
        if (png.Length is < 8 or > MaximumBytes || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("Expected a PNG image under 20 MB.");
        var root = Path.GetDirectoryName(Path.GetFullPath(documentPath))!;
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Save the document in an existing folder first.");
        var folder = Path.Combine(root, "assets");
        if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new IOException("The assets folder is a link. Choose a regular local assets folder.");
        Directory.CreateDirectory(folder);
        var name = "image-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N") + ".png";
        var destination = Path.Combine(folder, name);
        using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(png); stream.Flush(true); }
        return new(destination, "assets/" + name);
    }
}
