using System.Diagnostics;
using System.IO;
using Deskify.Interop;
using Deskify.Models;

namespace Deskify.Services;

public sealed class LaunchResult
{
    public List<string> Errors { get; } = [];
    public int Positioned;
    public int LayoutTargets;
    public bool Success => Errors.Count == 0;
}

/// <summary>Launches a project's apps/urls/folders and restores the saved window layout.</summary>
public static class LaunchEngine
{
    public static async Task<LaunchResult> LaunchAsync(DeskifyProject project, AppSettings settings, IProgress<string> status)
    {
        var result = new LaunchResult();

        // Snapshot windows that existed before launch so we can prefer NEW windows
        // when matching (critical for single-instance apps like VS Code or browsers),
        // and so an app that's already running isn't launched a second time.
        var scanned = await Task.Run(() => WindowScanner.Scan());
        var preExisting = new HashSet<IntPtr>(scanned.Select(w => w.Hwnd));
        var processTree = await Task.Run(ProcessTree.Snapshot);

        var launchedPids = new HashSet<uint>();

        foreach (var group in project.EffectiveLaunchOrder)
        {
            switch (group.ToLowerInvariant())
            {
                case "apps":
                    var toLaunch = project.Apps.Where(a => !a.AutoLinked).ToList();
                    if (toLaunch.Count > 0) status.Report($"Launching {toLaunch.Count} app{(toLaunch.Count == 1 ? "" : "s")}…");
                    foreach (var app in toLaunch)
                    {
                        // Already running — don't spawn a duplicate instance. Repeated
                        // Launch/Fast-Switch on the same project would otherwise pile
                        // up extra background processes every time.
                        if (FindBestWindow(scanned, app, [], [], [], processTree) != null) continue;

                        // Auto-linked entries (File Explorer for a folder, the
                        // default browser for a website) open via the folders/urls
                        // groups below — launching them here too would duplicate them.
                        LaunchApp(app, launchedPids, result);
                        await Task.Delay(150);
                    }
                    break;

                case "urls":
                    if (project.Urls.Count > 0) status.Report("Opening websites…");
                    OpenUrls(project.Urls, result);
                    break;

                case "folders":
                    if (project.Folders.Count > 0) status.Report("Opening folders…");
                    foreach (var folder in project.Folders)
                    {
                        OpenFolder(folder, launchedPids, result);
                        await Task.Delay(250);
                    }
                    break;
            }
        }

        await PositionWindowsAsync(project, settings, status, preExisting, launchedPids, result);

        status.Report(result.Success
            ? (result.LayoutTargets > 0 ? $"Workspace restored — {result.Positioned}/{result.LayoutTargets} windows positioned." : "Workspace launched.")
            : $"Done with {result.Errors.Count} issue{(result.Errors.Count == 1 ? "" : "s")}.");
        return result;
    }

    private static void LaunchApp(AppEntry app, HashSet<uint> launchedPids, LaunchResult result)
    {
        if (WindowScanner.IsProtected(app.Path, app.Name))
        {
            result.Errors.Add($"{app.Name}: blocked — Riot Games/Valorant apps can't be launched by Deskify");
            Log.Error($"Blocked launch attempt: {app.Name} ({app.Path})");
            return;
        }

        // Packaged (Store/UWP) apps have no exe on disk to start directly — their
        // Path is a "shell:AppsFolder\<AUMID>" pseudo-path from the picker, and
        // handing that to explorer.exe activates the app the same way clicking its
        // Start Menu tile would. Window layout still can't be tracked for most of
        // these (they run under a shared host process), so they won't be found by
        // Edit Layout/Launch positioning — same graceful "not found" as any app
        // whose window never appears.
        if (app.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var proc = Process.Start(new ProcessStartInfo("explorer.exe", app.Path) { UseShellExecute = true });
                if (proc != null) launchedPids.Add((uint)proc.Id);
                Log.Info($"Launched packaged app {app.Name} ({app.Path})");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{app.Name}: {ex.Message}");
                Log.Error($"Launch failed for {app.Path}", ex);
            }
            return;
        }

        try
        {
            // Heal shortcuts saved before resolving-on-add existed (or added by
            // hand-editing the project JSON): a .lnk still launches fine via the
            // shell, but resolving it first means it always launches — exe or
            // shortcut — with the same, consistent behavior and correct args.
            var path = app.Path;
            var args = app.Args ?? "";
            if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var (target, lnkArgs) = ShortcutResolver.ResolveWithArgs(path);
                path = target;
                if (args.Length == 0) args = lnkArgs;
            }

            if (!File.Exists(path))
            {
                result.Errors.Add($"{app.Name}: file not found ({path})");
                return;
            }
            if (WindowScanner.IsProtected(path, app.Name))
            {
                result.Errors.Add($"{app.Name}: blocked — Riot Games/Valorant apps can't be launched by Deskify");
                Log.Error($"Blocked launch attempt: {app.Name} ({path})");
                return;
            }

            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = args,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            };
            var proc = Process.Start(psi);
            if (proc != null) launchedPids.Add((uint)proc.Id);
            Log.Info($"Launched {app.Name} ({path})");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{app.Name}: {ex.Message}");
            Log.Error($"Launch failed for {app.Path}", ex);
        }
    }

    private static void TryShellOpen(string target, string label, LaunchResult result)
    {
        if (WindowScanner.IsProtected(target))
        {
            result.Errors.Add($"{label}: blocked — Riot Games/Valorant links can't be opened by Deskify");
            Log.Error($"Blocked URL open attempt: {target}");
            return;
        }

        try
        {
            // Same as double-clicking the link or typing it in Run — Windows hands
            // it to whatever's registered as the default browser. Whether that
            // becomes a new tab or a new window is entirely that browser's own
            // single-instance logic; Deskify has no further control over it.
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            Log.Info($"Opened {label}");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"{label}: {ex.Message}");
            Log.Error($"Failed to open {label}", ex);
        }
    }

    /// <summary>Opens every website in one go rather than one <c>ShellExecute</c> call
    /// per URL. Firing them individually races the browser's own cold start: the
    /// first URL launches the browser process, and if the second URL is sent before
    /// that instance's single-instance channel is ready to receive it, Windows
    /// starts a whole second browser process instead of a new tab in the first —
    /// which is exactly why multiple sites ended up in separate windows. Passing
    /// every URL as its own argument to the resolved default browser in a single
    /// launch avoids the race entirely; every mainstream browser opens each as a
    /// tab in one window.</summary>
    private static void OpenUrls(List<string> urls, LaunchResult result)
    {
        var allowed = new List<string>();
        foreach (var url in urls)
        {
            if (WindowScanner.IsProtected(url))
            {
                result.Errors.Add($"URL {url}: blocked — Riot Games/Valorant links can't be opened by Deskify");
                Log.Error($"Blocked URL open attempt: {url}");
            }
            else allowed.Add(url);
        }
        if (allowed.Count == 0) return;

        var browser = allowed.Count > 1 ? DefaultBrowser.Detect() : null;
        if (browser == null)
        {
            foreach (var url in allowed) TryShellOpen(url, $"URL {url}", result);
            return;
        }

        try
        {
            var args = string.Join(' ', allowed.Select(u => $"\"{u}\""));
            Process.Start(new ProcessStartInfo(browser.Value.Path, args) { UseShellExecute = true });
            foreach (var url in allowed) Log.Info($"Opened URL {url}");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Websites: {ex.Message}");
            Log.Error("Failed to open websites in one launch, falling back to one at a time", ex);
            foreach (var url in allowed) TryShellOpen(url, $"URL {url}", result);
        }
    }

    /// <summary>Opens one folder in its own File Explorer window.</summary>
    private static void OpenFolder(string folder, HashSet<uint> launchedPids, LaunchResult result)
    {
        if (!Directory.Exists(folder))
        {
            result.Errors.Add($"Folder not found: {folder}");
            return;
        }
        try
        {
            var proc = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
            if (proc != null) launchedPids.Add((uint)proc.Id);
            Log.Info($"Opened Folder {folder}");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Folder {folder}: {ex.Message}");
            Log.Error($"Failed to open folder {folder}", ex);
        }
    }

    private static async Task PositionWindowsAsync(
        DeskifyProject project, AppSettings settings, IProgress<string> status,
        HashSet<IntPtr> preExisting, HashSet<uint> launchedPids, LaunchResult result)
    {
        var targets = project.Apps.Where(a => a.Window != null && a.Path.Length > 0).ToList();
        result.LayoutTargets = targets.Count;
        if (targets.Count == 0) return;

        status.Report("Waiting for windows…");
        var monitors = Monitors.All();
        var claimed = new HashSet<IntPtr>();
        var applied = new List<(IntPtr Hwnd, WindowLayout Layout)>();
        var remaining = new List<AppEntry>(targets);
        var stopwatch = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(settings.DetectTimeoutSeconds);

        // Retry loop: poll for windows until every target is matched or we time out.
        while (remaining.Count > 0 && stopwatch.Elapsed < timeout)
        {
            var windows = await Task.Run(() => WindowScanner.Scan());
            var processTree = await Task.Run(ProcessTree.Snapshot);

            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                var app = remaining[i];
                var match = FindBestWindow(windows, app, launchedPids, preExisting, claimed, processTree);
                if (match == null) continue;

                claimed.Add(match.Hwnd);
                if (LayoutService.Apply(match.Hwnd, app.Window!, monitors))
                {
                    result.Positioned++;
                    applied.Add((match.Hwnd, app.Window!));
                    status.Report($"Positioned {app.Name} ({result.Positioned}/{targets.Count})");
                }
                else
                {
                    // Don't fail silently: the window was found but Windows refused
                    // to move it (commonly an elevated/admin process, which a
                    // non-elevated app is not allowed to reposition).
                    result.Errors.Add($"{app.Name}: found its window but couldn't move it (apps running as administrator can't be repositioned)");
                }
                remaining.RemoveAt(i);
            }

            if (remaining.Count > 0)
                await Task.Delay(settings.RetryIntervalMs);
        }

        foreach (var app in remaining)
            result.Errors.Add($"{app.Name}: window not found within {settings.DetectTimeoutSeconds}s — layout not applied");

        // Strict mode: some apps restore their own size shortly after startup.
        // Verify after a settle delay and re-apply anything that drifted.
        if (project.StrictLayout && applied.Count > 0)
        {
            status.Report("Double-checking window positions…");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(1000);
                bool allGood = true;
                foreach (var (hwnd, layout) in applied)
                {
                    if (!LayoutService.Matches(hwnd, layout, monitors))
                    {
                        allGood = false;
                        LayoutService.Apply(hwnd, layout, monitors);
                    }
                }
                if (allGood) break;
            }
        }
    }

    /// <summary>Rank candidate windows for an app entry. Preference order:
    /// new window from a launched pid → new window from same exe → any window from same exe.
    /// "Same exe" also matches a window whose process descends from the target exe
    /// (e.g. Steam's window is really owned by steamwebhelper.exe, a child of steam.exe) —
    /// this covers launcher-style apps generically, without needing per-app rules.
    /// Lowest-priority fallback: same install folder. Some launchers (Blender's
    /// blender-launcher.exe spawning blender.exe) hand off to a differently-named
    /// exe and then exit, so there's no live ancestor process left to walk up to —
    /// the shared install directory is the only remaining signal that ties the
    /// window back to the app that was launched.</summary>
    private static WindowInfo? FindBestWindow(
        List<WindowInfo> windows, AppEntry app,
        HashSet<uint> launchedPids, HashSet<IntPtr> preExisting, HashSet<IntPtr> claimed,
        Dictionary<uint, ProcessTree.Entry> processTree)
    {
        var exeName = TargetExeName(app);
        var targetFolder = ExplorerTargetFolder(app, exeName);
        var appFolder = Path.GetDirectoryName(ResolvedAppExePath(app));

        WindowInfo? Find(bool skipClaimed)
        {
            WindowInfo? best = null;
            int bestScore = 0;

            foreach (var w in windows)
            {
                if (skipClaimed && claimed.Contains(w.Hwnd)) continue;

                bool samePath = string.Equals(w.ExePath, app.Path, StringComparison.OrdinalIgnoreCase);
                bool sameExe = samePath || w.ExeName == exeName
                    || ProcessTree.IsOrDescendsFrom(processTree, w.Pid, exeName);
                bool sameFolder = !sameExe && appFolder != null &&
                    string.Equals(Path.GetDirectoryName(w.ExePath), appFolder, StringComparison.OrdinalIgnoreCase);
                if (!sameExe && !sameFolder) continue;

                // explorer.exe hosts every File Explorer window in one shared process,
                // so multiple captured Explorer "apps" are indistinguishable by exe name
                // alone — the folder each window is actually showing is the only signal.
                // ShowsFolder checks EVERY tab of the window (Windows 11 can stack
                // several folders as tabs behind a single window handle).
                if (targetFolder != null && !ExplorerWindows.ShowsFolder(w.Hwnd, targetFolder))
                    continue;

                bool isNew = !preExisting.Contains(w.Hwnd);
                int score = !sameExe ? 1 // folder-only fallback, lowest priority
                          : launchedPids.Contains(w.Pid) && isNew ? 5
                          : isNew ? 4
                          : samePath ? 3
                          : 2;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = w;
                }
            }
            return best;
        }

        var match = Find(skipClaimed: true);
        // Tabbed Explorer: two project folders can legitimately live in ONE window,
        // so if every window showing this folder is already claimed by another
        // folder entry, share it rather than reporting the folder as "not found".
        if (match == null && targetFolder != null)
            match = Find(skipClaimed: false);
        return match;
    }

    /// <summary>The real exe path an app entry ultimately launches, resolving a
    /// stored ".lnk" the same way TargetExeName does, so directory comparisons
    /// use the app's actual install folder rather than a Start Menu shortcut's.</summary>
    private static string ResolvedAppExePath(AppEntry app) =>
        app.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutResolver.Resolve(app.Path) : app.Path;

    private static string? ExplorerTargetFolder(AppEntry app, string exeName)
    {
        if (exeName != "explorer.exe" || string.IsNullOrWhiteSpace(app.Args)) return null;
        return app.Args.Trim().Trim('"');
    }

    /// <summary>The exe whose window we should look for. Launcher-style apps
    /// (e.g. Discord's Update.exe --processStart Discord.exe) spawn a different
    /// process than the one we start, so honor --processStart when present.</summary>
    public static string TargetExeName(AppEntry app)
    {
        var path = app.Path;
        var args = app.Args;

        // Defensive: heal projects saved before shortcuts were resolved on add.
        // A stored ".lnk" path never equals a running process's real exe name,
        // and any launcher-style args baked into the shortcut itself (rather than
        // stored on the entry) would otherwise be lost.
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (target, lnkArgs) = ShortcutResolver.ResolveWithArgs(path);
            path = target;
            if (string.IsNullOrWhiteSpace(args)) args = lnkArgs;
        }

        if (!string.IsNullOrWhiteSpace(args))
        {
            const string marker = "--processStart";
            int idx = args.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var rest = args[(idx + marker.Length)..].Trim().Trim('"');
                var candidate = rest.Split(' ', 2)[0].Trim('"');
                if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return candidate.ToLowerInvariant();
            }
        }
        return Path.GetFileName(path).ToLowerInvariant();
    }

    /// <summary>Find a currently-open window for each project app and capture its
    /// placement. Used by "Save Layout" — a single, immediate scan, on purpose:
    /// by the time you're arranging windows and hitting Save, they're already on
    /// screen, so this should be instant rather than making every save wait on a
    /// timeout. (Apps that are still slow to appear are handled by Edit Layout's
    /// own launch step, which does wait for them before showing the toolbar.)
    /// Returns names of apps whose window couldn't be found.</summary>
    public static List<string> CaptureLayouts(DeskifyProject project)
    {
        var missing = new List<string>();
        var windows = WindowScanner.Scan();
        var monitors = Monitors.All();
        var claimed = new HashSet<IntPtr>();
        var processTree = ProcessTree.Snapshot();

        foreach (var app in project.Apps)
        {
            var match = FindBestWindow(windows, app, [], [], claimed, processTree);
            if (match == null)
            {
                missing.Add(app.Name);
                continue;
            }
            claimed.Add(match.Hwnd);
            var layout = LayoutService.Capture(match.Hwnd, monitors);
            if (layout != null) app.Window = layout;
            else missing.Add(app.Name);
        }
        return missing;
    }

    /// <summary>Launch only the project apps that have no window on screen right now
    /// (used by Edit Layout so the user can arrange everything). Waits for each
    /// newly-launched app to actually show a window before returning — a browser's
    /// cold start in particular can take several seconds, far longer than File
    /// Explorer or most native apps, so returning immediately (as if "arrange your
    /// windows" meant the windows already existed) let people hit Save Layout before
    /// a slow app had opened anything, reporting it as "not found" even though it
    /// was only ever a few seconds away from appearing.</summary>
    public static async Task<int> LaunchMissingAppsAsync(DeskifyProject project, AppSettings settings, LaunchResult result)
    {
        var windows = WindowScanner.Scan();
        var processTree = ProcessTree.Snapshot();
        int launched = 0;
        var pids = new HashSet<uint>();
        var launchedApps = new List<AppEntry>();

        // The auto-linked browser entry only stores the browser's exe, never the
        // URLs (those live on the project). Launching its bare exe opens a blank
        // browser with no website, and the generic "skip if a window already exists"
        // rule below means an already-open browser gets skipped entirely — so the
        // project's sites never load either way. Handle it exactly like Launch does:
        // OpenUrls loads every site as a tab in a single window — a NEW browser when
        // none is running, or additional tabs in the existing one (current tabs kept).
        var browserPath = project.Urls.Count > 0 ? DefaultBrowser.Detect()?.Path : null;
        bool IsBrowserEntry(AppEntry a) => browserPath != null && a.AutoLinked &&
            string.Equals(a.Path, browserPath, StringComparison.OrdinalIgnoreCase);

        if (project.Urls.Count > 0)
        {
            OpenUrls(project.Urls, result);
            launched++;
            // Wait for the browser window so Save Layout can find it — matches the
            // already-open window immediately, or a cold-started browser once it shows.
            var browserEntry = project.Apps.FirstOrDefault(IsBrowserEntry);
            if (browserEntry != null) launchedApps.Add(browserEntry);
            await Task.Delay(300);
        }

        foreach (var app in project.Apps)
        {
            if (IsBrowserEntry(app)) continue; // opened via OpenUrls above

            // Reuses the same matching as layout capture/positioning — a plain
            // exe-name check would miss launcher-style apps and get this wrong.
            if (FindBestWindow(windows, app, [], [], [], processTree) != null) continue;
            LaunchApp(app, pids, result);
            launched++;
            launchedApps.Add(app);
            // Multiple explorer.exe (or any) launches fired back-to-back with no
            // gap can race each other's window/registration.
            await Task.Delay(300);
        }

        if (launchedApps.Count > 0)
            await WaitForWindowsAsync(launchedApps, pids, settings.DetectTimeoutSeconds);

        return launched;
    }

    /// <summary>Polls until every given app has a matching window or the timeout
    /// elapses. Best effort — if an app never opens a window (blocked, crashed,
    /// needs manual sign-in), Edit Layout still returns and Save Layout will
    /// simply report it as not found, same as today.</summary>
    private static async Task WaitForWindowsAsync(List<AppEntry> apps, HashSet<uint> launchedPids, int timeoutSeconds)
    {
        var remaining = new List<AppEntry>(apps);
        var stopwatch = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        while (remaining.Count > 0 && stopwatch.Elapsed < timeout)
        {
            var windows = WindowScanner.Scan();
            var processTree = ProcessTree.Snapshot();
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                if (FindBestWindow(windows, remaining[i], launchedPids, [], [], processTree) != null)
                    remaining.RemoveAt(i);
            }
            if (remaining.Count > 0) await Task.Delay(300);
        }
    }

    /// <summary>Keeps the hidden "AutoLinked" app entries (one File Explorer entry
    /// per folder, one shared default-browser entry for all websites) in sync with
    /// a project's Folders/Urls lists. These entries are what let Edit Layout and
    /// Launch actually find and position a window for each folder/website —
    /// without them, folders and links silently never open during Edit Layout.
    /// Folders each get their own entry (and their own window, and their own saved
    /// position) since they open in separate File Explorer windows; websites share
    /// one entry because they're tabs in the same browser window. Safe to call
    /// anytime (load, save, launch): it adds what's missing, drops entries for
    /// folders/urls that were removed, and never touches manually-added
    /// (non-AutoLinked) apps.</summary>
    public static void SyncAutoLinkedEntries(DeskifyProject project)
    {
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        bool ExplorerEntry(AppEntry a) => a.AutoLinked && string.Equals(a.Path, explorerPath, StringComparison.OrdinalIgnoreCase);

        // Drop folder entries whose folder was removed from the project.
        // PathsEqual ignores trailing backslashes so "D:\X\" and "D:\X" don't
        // read as two different folders (which duplicated or dropped entries).
        project.Apps.RemoveAll(a => ExplorerEntry(a) &&
            !project.Folders.Any(f => ExplorerWindows.PathsEqual(a.Args?.Trim('"'), f)));

        // Add entries for folders that don't have one yet.
        if (File.Exists(explorerPath))
        {
            foreach (var folder in project.Folders)
            {
                bool exists = project.Apps.Any(a => ExplorerEntry(a) &&
                    ExplorerWindows.PathsEqual(a.Args?.Trim('"'), folder));
                if (exists) continue;
                var leaf = Path.GetFileName(folder.TrimEnd('\\'));
                project.Apps.Add(new AppEntry
                {
                    Name = string.IsNullOrEmpty(leaf) ? folder : leaf,
                    Path = explorerPath,
                    Args = $"\"{folder}\"",
                    AutoLinked = true,
                });
            }
        }

        // Browser entry: one shared entry covers every website (they're tabs in
        // the same window). Drop it if there are no websites left; add it if
        // there are websites but no browser entry yet.
        var browser = project.Urls.Count > 0 ? DefaultBrowser.Detect() : null;
        project.Apps.RemoveAll(a => a.AutoLinked && !ExplorerEntry(a) &&
            (browser == null || !string.Equals(a.Path, browser.Value.Path, StringComparison.OrdinalIgnoreCase)));

        if (browser != null && !project.Apps.Any(a => a.AutoLinked && string.Equals(a.Path, browser.Value.Path, StringComparison.OrdinalIgnoreCase)))
        {
            project.Apps.Add(new AppEntry
            {
                Name = browser.Value.Name,
                Path = browser.Value.Path,
                AutoLinked = true,
            });
        }
    }
}
