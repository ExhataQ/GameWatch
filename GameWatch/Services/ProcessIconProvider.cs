using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;

namespace GameWatch.Services;

public static class ProcessIconProvider
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string path, int index, out IntPtr largeIcon, out IntPtr smallIcon, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    public static ImageSource? Get(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;
        try
        {
            if (ExtractIconEx(path, 0, out var large, out var small, 1) == 0) return Cache[path] = null;
            try
            {
                var handle = small != IntPtr.Zero ? small : large;
                var image = Imaging.CreateBitmapSourceFromHIcon(handle, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(20, 20));
                image.Freeze();
                return Cache[path] = image;
            }
            finally
            {
                if (large != IntPtr.Zero) DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
        }
        catch { return Cache[path] = null; }
    }
}
