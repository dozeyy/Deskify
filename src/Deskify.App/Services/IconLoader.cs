using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Deskify.Interop;

namespace Deskify.Services;

/// <summary>Small shell icons for exe paths, cached and frozen for cross-thread use.</summary>
public static class IconLoader
{
    private static readonly Dictionary<string, BitmapSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static BitmapSource? GetSmallIcon(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
        }

        BitmapSource? icon = null;
        var info = new SHFILEINFO();
        var result = NativeMethods.SHGetFileInfo(path, NativeMethods.FILE_ATTRIBUTE_NORMAL, ref info,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<SHFILEINFO>(),
            NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

        if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
        {
            try
            {
                icon = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                icon.Freeze();
            }
            catch
            {
                icon = null;
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }

        lock (Cache)
        {
            Cache[path] = icon;
        }
        return icon;
    }

    private static readonly Dictionary<int, BitmapSource?> LogoCache = new();

    /// <summary>The app's own icon (Assets/deskify.ico), decoded at whichever embedded
    /// frame size is closest to requested. Used anywhere the literal Deskify mark needs
    /// to render (sidebar badge, empty states) so it's the same asset as the window/
    /// taskbar icon, not a hand-redrawn lookalike.</summary>
    public static BitmapSource? GetAppLogo(int size)
    {
        lock (LogoCache)
        {
            if (LogoCache.TryGetValue(size, out var cached)) return cached;
        }

        BitmapSource? logo = null;
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/deskify.ico");
            var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - size)).FirstOrDefault();
            if (frame != null)
            {
                frame.Freeze();
                logo = frame;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load app icon for logo display", ex);
        }

        lock (LogoCache)
        {
            LogoCache[size] = logo;
        }
        return logo;
    }
}
