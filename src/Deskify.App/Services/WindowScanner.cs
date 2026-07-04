using System.Text;
using Deskify.Interop;

namespace Deskify.Services;

/// <summary>A visible top-level window and the process that owns it.</summary>
public sealed class WindowInfo
{
    public IntPtr Hwnd;
    public uint Pid;
    public string Title = "";
    public string ExePath = "";
    public string ExeName = "";   // lowercase file name, e.g. "code.exe"
}

/// <summary>Enumerates real, user-visible application windows.</summary>
public static class WindowScanner
{
    // Shell/system processes that should never be offered or matched.
    // explorer.exe (File Explorer windows) is intentionally NOT excluded — it's
    // a selectable, capturable app like any other.
    private static readonly HashSet<string> ExcludedExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "applicationframehost.exe", // UWP host, can't be relaunched by path
        "textinputhost.exe",
        "systemsettings.exe",
        "shellexperiencehost.exe",
        "startmenuexperiencehost.exe",
        "searchhost.exe",
        "lockapp.exe",
    };

    /// <summary>Riot Games' Vanguard anti-cheat treats external processes that
    /// enumerate, reposition, or otherwise touch its protected games' windows as
    /// potential tampering — this can risk a player's account. Deskify never
    /// captures, matches, positions, or closes anything in this list, and this
    /// exclusion cannot be bypassed (unlike ExcludedExes, includeExcluded has no
    /// effect on it). See Settings → About for the user-facing notice.</summary>
    public static readonly HashSet<string> AntiCheatProtectedExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "riotclientservices.exe",
        "riotclientux.exe",
        "riotclientuxrender.exe",
        "riotclientcrashhandler.exe",
        "valorant.exe",
        "valorant-win64-shipping.exe",
        "league of legends.exe",
        "leagueclient.exe",
        "leagueclientux.exe",
        "leagueclientuxrender.exe",
        "vgc.exe",   // Vanguard client service
        "vgtray.exe", // Vanguard tray icon
    };

    /// <summary>True if a path or display name refers to Riot Games/Valorant software.
    /// Unlike <see cref="AntiCheatProtectedExes"/> (which only governs window scanning),
    /// this is the hard gate checked wherever Deskify could add or launch an app at all —
    /// browsing to an exe, capturing a running window, or launching a saved project.
    /// Keyword-based (not just the fixed exe list) so it also catches installer paths,
    /// shortcuts, and future Riot titles under "Riot Games\..." or named "...Valorant...".</summary>
    public static bool IsProtected(string? path, string? name = null)
    {
        var exeName = string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetFileName(path).ToLowerInvariant();
        if (AntiCheatProtectedExes.Contains(exeName)) return true;
        return ContainsBlockedWord(path) || ContainsBlockedWord(name);
    }

    private static bool ContainsBlockedWord(string? s) =>
        !string.IsNullOrEmpty(s) &&
        (s.Contains("riot", StringComparison.OrdinalIgnoreCase) || s.Contains("valorant", StringComparison.OrdinalIgnoreCase));

    public static List<WindowInfo> Scan(bool includeExcluded = false)
    {
        var results = new List<WindowInfo>();
        var pathCache = new Dictionary<uint, string>();
        uint ownPid = (uint)Environment.ProcessId;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;

            int titleLen = NativeMethods.GetWindowTextLength(hwnd);
            if (titleLen == 0) return true;

            long exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

            // Skip cloaked windows (suspended UWP ghosts, hidden virtual-desktop windows).
            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0
                && cloaked != 0) return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == ownPid) return true;

            if (!pathCache.TryGetValue(pid, out var exePath))
            {
                exePath = GetProcessPath(pid);
                pathCache[pid] = exePath;
            }
            if (exePath.Length == 0) return true;

            var exeName = System.IO.Path.GetFileName(exePath).ToLowerInvariant();
            if (AntiCheatProtectedExes.Contains(exeName)) return true;
            if (!includeExcluded && ExcludedExes.Contains(exeName)) return true;

            var sb = new StringBuilder(titleLen + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);

            results.Add(new WindowInfo
            {
                Hwnd = hwnd,
                Pid = pid,
                Title = sb.ToString(),
                ExePath = exePath,
                ExeName = exeName,
            });
            return true;
        }, IntPtr.Zero);

        return results;
    }

    public static string GetProcessPath(uint pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return "";
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : "";
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
