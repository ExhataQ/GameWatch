using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;

namespace GameWatch.Services;

// Returns an application's own icon, or the standard Windows "generic
// application" icon when it has none (missing/inaccessible path, system
// processes, executables without an embedded icon). Never returns null
// unless Windows itself can't supply the default icon.
public static class ProcessIconProvider
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> TypeCache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _defaultIcon;
    private static bool _defaultLoaded;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string path, int index, out IntPtr largeIcon, out IntPtr smallIcon, uint count);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_SMALLICON = 0x1;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    public static ImageSource? Get(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return GetDefault();
        if (Cache.TryGetValue(path, out var cached)) return cached;
        return Cache[path] = Extract(path) ?? GetDefault();
    }

    public static ImageSource? GetFileTypeIcon(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension is ".crdownload" or ".part" or ".partial" or ".opdownload")
            extension = Path.GetExtension(Path.GetFileNameWithoutExtension(path));
        if (string.IsNullOrEmpty(extension)) return GetDefault();
        if (TypeCache.TryGetValue(extension, out var icon)) return icon;
        var info = new SHFILEINFO();
        try
        {
            var result = SHGetFileInfo("file" + extension, FILE_ATTRIBUTE_NORMAL, ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
            if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return TypeCache[extension] = GetDefault();
            try { return TypeCache[extension] = ToImage(info.hIcon); }
            finally { DestroyIcon(info.hIcon); }
        }
        catch { return TypeCache[extension] = GetDefault(); }
    }

    private static ImageSource? Extract(string path)
    {
        try
        {
            if (ExtractIconEx(path, 0, out var large, out var small, 1) == 0) return null;
            try
            {
                var handle = small != IntPtr.Zero ? small : large;
                return ToImage(handle);
            }
            finally
            {
                if (large != IntPtr.Zero) DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
        }
        catch { return null; }
    }

    // SHGFI_USEFILEATTRIBUTES asks the shell for the icon of a *type*
    // (".exe") without touching any real file - that is the standard
    // Windows default application icon.
    private static ImageSource? GetDefault()
    {
        if (_defaultLoaded) return _defaultIcon;
        _defaultLoaded = true;
        try
        {
            var info = new SHFILEINFO();
            var result = SHGetFileInfo("file.exe", FILE_ATTRIBUTE_NORMAL, ref info,
                (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
            if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                try { _defaultIcon = ToImage(info.hIcon); }
                finally { DestroyIcon(info.hIcon); }
            }
        }
        catch { }
        return _defaultIcon;
    }

    private static ImageSource ToImage(IntPtr handle)
    {
        var image = Imaging.CreateBitmapSourceFromHIcon(handle, System.Windows.Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(20, 20));
        image.Freeze();
        return image;
    }
}
