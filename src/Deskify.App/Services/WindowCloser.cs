using System.Diagnostics;
using Deskify.Interop;
using Deskify.Models;

namespace Deskify.Services;

/// <summary>Finds and closes windows that don't belong to a given project.</summary>
public static class WindowCloser
{
    /// <summary>Currently open windows, excluding the given project's own apps.
    /// Uses the same exe-name resolution as launch/layout matching (handles
    /// launcher-style apps and shortcuts) so "other" windows are identified
    /// the same way everywhere in the app.</summary>
    public static List<WindowInfo> OtherWindows(DeskifyProject project)
    {
        var keep = new HashSet<string>(
            project.Apps.Select(LaunchEngine.TargetExeName),
            StringComparer.OrdinalIgnoreCase);

        return WindowScanner.Scan()
            .Where(w => !keep.Contains(w.ExeName))
            .ToList();
    }

    /// <summary>Politely asks each window to close (WM_CLOSE) — same as clicking its own close button.</summary>
    public static void Close(IEnumerable<WindowInfo> windows)
    {
        foreach (var w in windows)
        {
            try
            {
                NativeMethods.PostMessage(w.Hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to close window '{w.Title}' ({w.ExeName})", ex);
            }
        }
    }

    /// <summary>Closes windows and makes sure the apps behind them actually go away:
    /// WM_CLOSE first, then polls until each owning process has really exited, then
    /// force-terminates whatever's left (killing its whole process tree, so helper/
    /// child processes don't linger either). Many apps keep running in the background
    /// after their window closes — this is what stops them from piling up across
    /// repeated workspace switches. Returns a message per window that couldn't be
    /// fully closed (protected, or the kill itself failed), for status/error reporting.</summary>
    public static async Task<List<string>> CloseAndVerifyAsync(IEnumerable<WindowInfo> windows, int graceMs = 4000)
    {
        var list = windows.ToList();
        var failed = new List<string>();
        if (list.Count == 0) return failed;

        Close(list);

        // One representative window per PID — a process can own several of the
        // windows being closed (e.g. multiple tabs/windows in one app instance).
        var byPid = list.GroupBy(w => w.Pid).ToDictionary(g => g.Key, g => g.First());
        var remaining = new HashSet<uint>(byPid.Keys);
        var stopwatch = Stopwatch.StartNew();

        while (remaining.Count > 0 && stopwatch.ElapsedMilliseconds < graceMs)
        {
            await Task.Delay(300);
            remaining.RemoveWhere(HasExited);
        }

        foreach (var pid in remaining)
        {
            var w = byPid[pid];
            if (!IsSafeToForceKill(w))
            {
                failed.Add($"{w.ExeName}: left running (protected — never force-closed)");
                continue;
            }

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                proc.Kill(entireProcessTree: true);
                Log.Info($"Force-closed {w.ExeName} (pid {pid}) after it didn't respond to a normal close");
            }
            catch (Exception ex)
            {
                failed.Add($"{w.ExeName}: {ex.Message}");
                Log.Error($"Failed to force-close {w.ExeName} (pid {pid})", ex);
            }
        }

        return failed;
    }

    private static bool HasExited(uint pid)
    {
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            return proc.HasExited;
        }
        catch (ArgumentException)
        {
            return true; // no process with this id — already gone
        }
    }

    /// <summary>Hard safety net for the force-kill escalation — never terminate Windows
    /// system processes, services, Explorer, or Deskify itself. File Explorer windows
    /// still get a polite WM_CLOSE above (closes just that window); they're just never
    /// escalated to killing explorer.exe, which would take down the whole shell.</summary>
    private static bool IsSafeToForceKill(WindowInfo w)
    {
        if (string.Equals(w.ExeName, "explorer.exe", StringComparison.OrdinalIgnoreCase)) return false;
        if (w.Pid == (uint)Environment.ProcessId) return false;
        if (WindowScanner.AntiCheatProtectedExes.Contains(w.ExeName)) return false;

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(w.ExePath) &&
            w.ExePath.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }
}
