using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TikzEdt.Modern;

/// <summary>Matches the Windows-owned title bar to the WPF dark palette.</summary>
internal static class NativeWindowTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;
    private static readonly Lazy<ImageSource> ApplicationLogo = new(() =>
    {
        var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/TikzEdt.Modern;component/Assets/Branding/TikzEdt.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault(frame => frame.PixelWidth == 32) ?? decoder.Frames[^1];
        frame.Freeze();
        return frame;
    });

    public static void Attach(Window window)
    {
        window.Icon = ApplicationLogo.Value;
        window.SourceInitialized += (_, _) => Apply(window);
        window.Activated += (_, _) => Apply(window);
        window.Deactivated += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        if (SystemParameters.HighContrast)
            return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var enabled = 1;
        _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref enabled, sizeof(int));
        SetColor(handle, CaptionColor, GetColor(window, window.IsActive ? "PanelBrush" : "WindowBrush"));
        SetColor(handle, TextColor, GetColor(window, window.IsActive ? "TextBrush" : "MutedBrush"));
        SetColor(handle, BorderColor, GetColor(window, "BorderBrush"));
        // Older Windows versions can reject the explicit color attributes;
        // the supported dark-frame attribute above still applies there.
    }

    private static Color GetColor(Window window, string key) =>
        ((SolidColorBrush)window.FindResource(key)).Color;

    private static void SetColor(IntPtr handle, int attribute, Color color)
    {
        var colorRef = color.R | (color.G << 8) | (color.B << 16);
        _ = DwmSetWindowAttribute(handle, attribute, ref colorRef, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}
