using System.Reflection;

namespace Deskify.Services;

/// <summary>Maps open File Explorer windows to the folder path(s) each is showing.
/// explorer.exe hosts every window in one shared shell process, so relaunching it
/// with no arguments just opens a default window — matching needs the actual path.
/// On Windows 11, Explorer can also stack several folders as TABS in one window:
/// the shell reports one entry per tab, all sharing the same window handle, so a
/// window maps to a LIST of paths — matching against only the first tab made
/// stacked folders silently fail to save or restore their layout.</summary>
public static class ExplorerWindows
{
    private static Dictionary<long, List<string>> _cache = [];
    private static DateTime _cacheTime = DateTime.MinValue;
    private static readonly object Gate = new();

    /// <summary>True if the given Explorer window is showing this folder in any
    /// of its tabs. Paths compare case-insensitively with trailing backslashes
    /// ignored, so "D:\Projects\" and "D:\Projects" are the same folder.</summary>
    public static bool ShowsFolder(IntPtr hwnd, string folder) =>
        Snapshot().TryGetValue(hwnd.ToInt64(), out var paths) && paths.Any(p => PathsEqual(p, folder));

    /// <summary>First (usually initial) tab's folder path for a window, or null.</summary>
    public static string? PathFor(IntPtr hwnd) =>
        Snapshot().TryGetValue(hwnd.ToInt64(), out var paths) && paths.Count > 0 ? paths[0] : null;

    public static bool PathsEqual(string? a, string? b) =>
        a != null && b != null &&
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>HWND → folder paths for every open Explorer window/tab. One COM
    /// enumeration serves a whole matching pass (window-scan loops call this per
    /// candidate window); cached briefly since tab sets don't change mid-save.</summary>
    private static Dictionary<long, List<string>> Snapshot()
    {
        lock (Gate)
        {
            if ((DateTime.UtcNow - _cacheTime).TotalMilliseconds < 500) return _cache;
            _cache = Build();
            _cacheTime = DateTime.UtcNow;
            return _cache;
        }
    }

    private static Dictionary<long, List<string>> Build()
    {
        var map = new Dictionary<long, List<string>>();
        try
        {
            var shellAppType = Type.GetTypeFromProgID("Shell.Application");
            if (shellAppType == null) return map;
            object? shellApp = Activator.CreateInstance(shellAppType);
            if (shellApp == null) return map;

            var windows = (object)InvokeGet(shellApp, "Windows")!;
            int count = (int)InvokeGet(windows, "Count")!;

            for (int i = 0; i < count; i++)
            {
                var window = InvokeMethod(windows, "Item", i);
                if (window == null) continue;

                // HWND comes back as a 64-bit COM Automation LONG on a 64-bit process
                // (boxed as Int64) — casting straight to int throws InvalidCastException
                // on every single call, which silently broke every folder-window match.
                long hwnd = Convert.ToInt64(InvokeGet(window, "HWND"));

                var document = InvokeGet(window, "Document");
                var folder = document != null ? InvokeGet(document, "Folder") : null;
                var self = folder != null ? InvokeGet(folder, "Self") : null;
                var path = self != null ? InvokeGet(self, "Path") as string : null;
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (!map.TryGetValue(hwnd, out var list)) map[hwnd] = list = [];
                list.Add(path);
            }
        }
        catch (Exception ex)
        {
            // Best effort — Shell.Application isn't guaranteed, and virtual folders
            // (This PC, Recycle Bin) don't always expose a usable path.
            Log.Error("Couldn't read Explorer windows via Shell.Application", ex);
        }
        return map;
    }

    // COM's IDispatch doesn't cleanly separate "property get" from "no-arg method
    // call" the way .NET does — Type.InvokeMember needs both flags together for
    // late-bound automation objects like Shell.Application, or member access
    // that's technically method-dispatched (e.g. "Windows") throws and the whole
    // lookup silently fails.
    private static object? InvokeGet(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty | BindingFlags.InvokeMethod, null, target, null);

    private static object? InvokeMethod(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);
}
