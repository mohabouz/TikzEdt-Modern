using System.Windows.Media.Imaging;

namespace TikzEdt.Modern;

internal static class PreviewImageLoader
{
    private static readonly SemaphoreSlim DecodeGate = new(1);
    public static async Task<BitmapImage> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await DecodeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze(); // A frozen image can safely cross worker/UI threads.
                cancellationToken.ThrowIfCancellationRequested();
                return bitmap;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { DecodeGate.Release(); }
    }
}
