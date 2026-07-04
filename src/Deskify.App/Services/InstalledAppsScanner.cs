using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Deskify.Services;

/// <summary>An installed application discovered from Start Menu shortcuts, the
/// Programs-and-Features registry, or the Microsoft Store. <see cref="Path"/> is
/// either a real exe path, or — for <see cref="IsPackaged"/> apps — a
/// "shell:AppsFolder\..." pseudo-path that only <c>explorer.exe</c> can launch.</summary>
public sealed record InstalledApp(string Name, string Path, string? Args, bool IsPackaged = false);

/// <summary>Enumerates installed applications the same way Windows itself would
/// list them — Start Menu shortcuts, "Programs and Features", and the Store —
/// so adding an app to a project is picking from a list instead of hunting
/// through Program Files. <see cref="InstalledAppFilter"/> strips out the
/// system tools, drivers, and helper exes those sources are full of before
/// anything reaches the picker. Browse still exists for anything this scan
/// misses (portable apps, unusual install locations, etc).</summary>
public static partial class InstalledAppsScanner
{
    public static List<InstalledApp> Scan()
    {
        // Keyed by Path so shortcut-sourced entries (which carry launcher args,
        // e.g. Discord's "--processStart Discord.exe") always win over a registry
        // entry for the same exe found later.
        var byPath = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in ScanStartMenuShortcuts())
            byPath[app.Path] = app;

        foreach (var app in ScanUninstallRegistry())
            byPath.TryAdd(app.Path, app);

        foreach (var app in ScanPackagedApps())
            byPath.TryAdd(app.Path, app);

        return byPath.Values
            .Where(a => !WindowScanner.IsProtected(a.Path, a.Name) && !InstalledAppFilter.IsExcluded(a))
            // The same product often shows up under one name from more than one
            // exe — a tray-icon helper alongside the main program, or a versioned
            // subfolder copy alongside the top-level one (Logitech G HUB, Medal).
            // Keep a single row per name, preferring whichever path looks most
            // like "the app itself" rather than a helper/versioned copy of it.
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(LaunchPreferenceScore).First())
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int LaunchPreferenceScore(InstalledApp a)
    {
        int score = a.Path.Length / 20; // mild tiebreak toward the shorter, usually top-level, path
        if (a.Path.Contains("tray", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (a.Path.Contains("helper", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (VersionedFolderSegment().IsMatch(a.Path)) score += 10;
        return score;
    }

    [GeneratedRegex(@"[\\/](app|bin)-[\d.]+[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex VersionedFolderSegment();

    private static IEnumerable<InstalledApp> ScanStartMenuShortcuts()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in StartMenuRoots())
        {
            if (!Directory.Exists(root)) continue;

            IEnumerable<string> shortcuts;
            try
            {
                shortcuts = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                continue; // Some Start Menu subfolders can be access-restricted.
            }

            foreach (var lnk in shortcuts)
            {
                string target, args;
                try
                {
                    (target, args) = ShortcutResolver.ResolveWithArgs(lnk);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(target)) continue;
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(target)) continue;
                if (!seen.Add(target)) continue;

                var displayName = Path.GetFileNameWithoutExtension(lnk);
                yield return new InstalledApp(displayName, target, string.IsNullOrWhiteSpace(args) ? null : args);
            }
        }
    }

    private static IEnumerable<string> StartMenuRoots()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
    }

    /// <summary>Fills gaps for apps whose Start Menu shortcut is missing or was
    /// deleted, by reading the same "Programs and Features" registry data Windows'
    /// own uninstall list uses. Only yields an app when DisplayIcon resolves to a
    /// real .exe — anything else (an .ico file, an msiexec reference) gives no
    /// reliable launch target, and guessing one from InstallLocation risks picking
    /// the wrong exe entirely, so those entries are simply skipped.</summary>
    private static IEnumerable<InstalledApp> ScanUninstallRegistry()
    {
        var roots = new (RegistryKey Hive, string SubKey)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        };

        foreach (var (hive, subKey) in roots)
        {
            using var uninstallKey = TryOpen(hive, subKey);
            if (uninstallKey == null) continue;

            foreach (var name in uninstallKey.GetSubKeyNames())
            {
                using var appKey = TryOpen(uninstallKey, name);
                if (appKey == null) continue;

                if (appKey.GetValue("SystemComponent") is int sc && sc != 0) continue;
                if (appKey.GetValue("ParentKeyName") != null) continue; // sub-component of another product
                if (appKey.GetValue("ReleaseType") is string releaseType &&
                    (releaseType.Equals("Update", StringComparison.OrdinalIgnoreCase) ||
                     releaseType.Equals("Hotfix", StringComparison.OrdinalIgnoreCase) ||
                     releaseType.Equals("SecurityUpdate", StringComparison.OrdinalIgnoreCase)))
                    continue;

                if (appKey.GetValue("DisplayName") is not string displayName || string.IsNullOrWhiteSpace(displayName))
                    continue;

                var exePath = ExeFromDisplayIcon(appKey.GetValue("DisplayIcon") as string);
                if (exePath == null) continue;

                yield return new InstalledApp(displayName.Trim(), exePath, null);
            }
        }
    }

    private static RegistryKey? TryOpen(RegistryKey hive, string subKey)
    {
        try { return hive.OpenSubKey(subKey); } catch { return null; }
    }

    private static string? ExeFromDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        var path = displayIcon.Trim().Trim('"');
        int comma = path.LastIndexOf(',');
        if (comma > 0 && int.TryParse(path[(comma + 1)..], out _)) path = path[..comma];
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    /// <summary>Store/UWP apps don't live in the file system or the Uninstall
    /// registry — Windows itself only knows about them via the packaging APIs.
    /// Rather than pull in a WinRT/CsWinRT dependency for this one list, shell
    /// out to the same built-in cmdlet the Start Menu's own app list is backed
    /// by (<c>Get-StartApps</c>) and keep only the packaged half of its output —
    /// Win32 apps are already covered, better, by the shortcut scan above (which
    /// preserves launcher args that Get-StartApps' AppID does not).</summary>
    private static List<InstalledApp> ScanPackagedApps()
    {
        try
        {
            var psi = new ProcessStartInfo("powershell.exe")
            {
                Arguments = "-NoProfile -NoLogo -NonInteractive -Command " +
                    "\"Get-StartApps | Where-Object { $_.AppID -like '*!*' } | ConvertTo-Json -Compress\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return [];

            string output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(); } catch { /* best effort */ }
                return [];
            }

            output = output.Trim();
            if (output.Length == 0) return [];

            // Get-StartApps prints a single object (not an array) when exactly one matches.
            List<StartAppJson>? items = output.StartsWith('[')
                ? JsonSerializer.Deserialize<List<StartAppJson>>(output)
                : JsonSerializer.Deserialize<StartAppJson>(output) is { } single ? [single] : null;

            return (items ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i.Name) && !string.IsNullOrWhiteSpace(i.AppID))
                .Select(i => new InstalledApp(i.Name!.Trim(), $"shell:AppsFolder\\{i.AppID}", null, IsPackaged: true))
                .ToList();
        }
        catch
        {
            return []; // Best effort — Store app discovery is a bonus, never fatal to the rest of the scan.
        }
    }

    private sealed class StartAppJson
    {
        [JsonPropertyName("Name")] public string? Name { get; set; }
        [JsonPropertyName("AppID")] public string? AppID { get; set; }
    }
}
