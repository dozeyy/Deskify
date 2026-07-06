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
    /// the same way everywhere in the app.
    ///
    /// explorer.exe needs special handling: every File Explorer window shares that
    /// one exe name, so a plain exe-name "keep" set would treat ANY open Explorer
    /// window as belonging to the project the moment it has a single folder in
    /// it — leaving unrelated Explorer windows (other folders, other projects)
    /// never offered for closing on a workspace switch. Explorer windows are kept
    /// only if they're actually showing one of this project's folders (checking
    /// every tab, since Windows 11 can stack folders as tabs in one window).</summary>
    public static List<WindowInfo> OtherWindows(DeskifyProject project, IEnumerable<string>? neverClose = null)
    {
        const string explorerExe = "explorer.exe";
        var keep = new HashSet<string>(
            project.Apps.Select(LaunchEngine.TargetExeName).Where(n => n != explorerExe),
            StringComparer.OrdinalIgnoreCase);
        var pinned = ProtectedSet(neverClose);
        var processTree = ProcessTree.Snapshot();

        return WindowScanner.Scan()
            .Where(w => !NeverCloseMatch.IsProtected(w, pinned, processTree))
            .Where(w => string.Equals(w.ExeName, explorerExe, StringComparison.OrdinalIgnoreCase)
                ? !project.Folders.Any(f => ExplorerWindows.ShowsFolder(w.Hwnd, f))
                : !keep.Contains(w.ExeName))
            .ToList();
    }

    /// <summary>Every closeable window on the desktop, minus the user's "never close" apps —
    /// used for a clean-slate workspace switch, which clears the desktop and relaunches the
    /// target project from scratch so every window lands at its saved position. Reopening
    /// fresh (rather than reusing a still-running instance) is the only reliable fix for
    /// single-instance, position-sticky apps like the browser and File Explorer, which
    /// otherwise stay wherever they were last dragged and ignore the saved layout. Deskify's
    /// own windows and anti-cheat-protected apps are already excluded by the scanner; the
    /// never-close list adds anything the user pinned in Settings (apps they keep open across
    /// workspaces, or that hold unsaved work).</summary>
    public static List<WindowInfo> AllWindows(IEnumerable<string>? neverClose = null)
    {
        var pinned = ProtectedSet(neverClose);
        var processTree = ProcessTree.Snapshot();
        return WindowScanner.Scan().Where(w => !NeverCloseMatch.IsProtected(w, pinned, processTree)).ToList();
    }

    private static HashSet<string> ProtectedSet(IEnumerable<string>? neverClose) =>
        new(neverClose ?? [], StringComparer.OrdinalIgnoreCase);

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
    public static async Task<List<string>> CloseAndVerifyAsync(IEnumerable<WindowInfo> windows, int graceMs = 4000, CancellationToken ct = default)
    {
        var list = windows.ToList();
        var failed = new List<string>();
        if (list.Count == 0) return failed;

        Close(list);

        // File Explorer windows all share the single shell explorer.exe process, which
        // never exits — so they're verified by whether the WINDOW itself is gone, not by
        // process exit. Tracking process exit for them made every closed Explorer window
        // falsely report "explorer.exe: left running", since the shell process is (rightly)
        // never force-killed. Everything else is verified by process exit and escalated to
        // a force-kill, because many apps keep running in the background after their window
        // closes.
        var byPid = list.Where(w => !IsExplorer(w))
            .GroupBy(w => w.Pid).ToDictionary(g => g.Key, g => g.First());
        var remaining = new HashSet<uint>(byPid.Keys);
        var explorerWindows = list.Where(IsExplorer).ToList();
        var stopwatch = Stopwatch.StartNew();

        while ((remaining.Count > 0 || explorerWindows.Count > 0) && stopwatch.ElapsedMilliseconds < graceMs)
        {
            await Task.Delay(300, ct);
            remaining.RemoveWhere(HasExited);
            explorerWindows.RemoveAll(w => !NativeMethods.IsWindow(w.Hwnd));
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

        // An Explorer window still open after the grace period genuinely refused WM_CLOSE
        // (e.g. a modal "confirm delete" child is up) — report just that window, never the
        // shared shell process, which we never kill.
        foreach (var w in explorerWindows)
            failed.Add($"File Explorer{(string.IsNullOrWhiteSpace(w.Title) ? "" : $" ({w.Title})")}: still open — close it and try again");

        return failed;
    }

    private static bool IsExplorer(WindowInfo w) =>
        string.Equals(w.ExeName, "explorer.exe", StringComparison.OrdinalIgnoreCase);

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
