using System.IO;

namespace Deskify.Services;

/// <summary>Decides whether a discovered install belongs in the "Add Application"
/// picker. Windows' own app inventory (Start Menu, Programs and Features, the
/// Store) is full of things a user never thinks of as "an app": drivers, admin
/// consoles, uninstallers, helper/updater processes, and Microsoft's own inbox
/// utilities. None of that belongs in a workspace layout — this is the single
/// place all of it gets stripped out before the picker ever shows a list.
/// Heuristic, not exhaustive: favors hiding obvious noise over risking a false
/// positive on a real app, but can't catch every oddly-named helper exe.</summary>
public static class InstalledAppFilter
{
    // Built-in Windows tools/consoles a user would only ever reach by searching
    // the Start Menu directly — never something they "installed".
    private static readonly string[] ExactNoiseNames =
    [
        "powershell", "windows powershell", "windows powershell ise", "windows terminal",
        "steps recorder", "system information", "system configuration", "msconfig",
        "resource monitor", "performance monitor", "registry editor", "regedit",
        "odbc data sources", "odbc data sources (32-bit)", "odbc data sources (64-bit)",
        "recovery drive", "character map", "windows memory diagnostic",
        "report a problem", "windows fax and scan", "disk cleanup", "component services",
        "computer management", "local security policy", "local group policy editor",
        "event viewer", "services", "task scheduler", "print management", "device manager",
        "iscsi initiator", "defragment and optimize drives", "on-screen keyboard",
        "magnifier", "narrator", "remote desktop connection", "quick assist",
        "windows security", "feedback hub", "get help", "get started", "tips",
        "mixed reality portal", "3d viewer", "print 3d", "paint 3d", "your phone",
        "phone link", "movies & tv", "groove music", "mail and calendar", "people",
        "maps", "news", "weather", "solitaire & casual games", "sticky notes",
        "snipping tool", "voice recorder", "alarms & clock", "xbox", "xbox game bar",
        "xbox console companion", "cortana", "microsoft store", "app installer",
        "control panel", "run", "settings", "terminal", "click to do", "windows backup",
        "sound recorder", "about java", "realtek audio console",
    ];

    // Substrings that flag helper/driver/update/config/uninstall-style entries
    // regardless of what product they're bundled with.
    private static readonly string[] NoiseSubstrings =
    [
        "uninstall", "read me", "readme", "release notes", "changelog",
        "license agreement", "eula", "documentation", "check for updates",
        "update assistant", "updater", "update manager", "install manager",
        "installer", "diagnostic", "driver", "control panel", "configuration utility",
        "config utility", "local server", "localserver", "plugin server", "pluginserver",
        "report tool", "bug report",
        "bug reporter", "report a problem", "crash handler", "crash reporter",
        "support center", "redistributable", "runtime", " sdk ",
        "software development kit", "hotfix", "security update", "update for ",
        ".net framework", "visual c++", "vc_redist", "webview2", "background service",
        "helper (", " helper", "manual", "user guide", "legacy", "audio console",
    ];

    // Microsoft's own inbox Store apps (Camera, Photos, Notepad, Terminal, Click
    // to Do, Windows Backup, etc.) all publish under one of a small set of
    // publisher IDs reserved for first-party Windows components — no third
    // party can sign a package with these, so matching on them is precise
    // without needing to name every current (and future) inbox app individually.
    private static readonly string[] MicrosoftInboxPublisherSuffixes =
    [
        "_8wekyb3d8bbwe", "_cw5n1h2txyewy",
    ];

    // Explicitly excluded by request: its window can't reliably be found/tracked
    // (see LaunchEngine.FindBestWindow), so it's kept out of the picker rather
    // than added and silently failing every layout save. "adrenalin" is used
    // instead of matching the full name since AMD's shortcut spells "Software"
    // with a lookalike colon character, not a plain ":".
    private static readonly string[] ExcludedByRequest = ["adrenalin"];

    public static bool IsExcluded(InstalledApp app)
    {
        if (app.IsPackaged)
        {
            if (MicrosoftInboxPublisherSuffixes.Any(suf => app.Path.Contains(suf, StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        else
        {
            if (IsUnderWindowsDirectory(app.Path)) return true;
            if (app.Path.Contains(@"\Package Cache\", StringComparison.OrdinalIgnoreCase)) return true;

            var exeFile = Path.GetFileName(app.Path);
            if (exeFile.Equals("setup.exe", StringComparison.OrdinalIgnoreCase) ||
                exeFile.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase) ||
                exeFile.EndsWith("_setup.exe", StringComparison.OrdinalIgnoreCase))
                return true;

            var exeName = Path.GetFileNameWithoutExtension(app.Path);
            if (exeName.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
                exeName.StartsWith("unins", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var name = app.Name.Trim();
        if (ExactNoiseNames.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase))) return true;
        if (NoiseSubstrings.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) return true;
        if (ExcludedByRequest.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) return true;

        return false;
    }

    /// <summary>Anything living under C:\Windows (System32, SysWOW64, WinSxS, the
    /// Windows root itself) is a built-in OS component, not something the user
    /// chose to install — this one rule alone accounts for the vast majority of
    /// admin tools, MMC snap-ins, and Windows accessories.</summary>
    private static bool IsUnderWindowsDirectory(string path)
    {
        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var full = Path.GetFullPath(path);
            return full.StartsWith(windir, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
