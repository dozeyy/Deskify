using System.IO;
using Deskify.Models;

namespace Deskify.Services;

/// <summary>Single source of truth for the "Never Close These Apps" feature: turns a
/// picked app into the identity token stored in settings, and decides whether a scanned
/// window belongs to a pinned app. Matching is by the owning PROCESS, never the window
/// title — titles change constantly (Discord shows "Update.exe" mid-update; browsers and
/// editors show the current tab/file) and would make the exclusion unreliable.
///
/// Two kinds of token are stored, so anything the user can download is covered:
///   • Win32 apps → an exe name ("discord.exe"), resolved launcher-aware so a stub like
///     Discord's "Update.exe --processStart Discord.exe" is stored as the real "discord.exe".
///   • Store/MSIX apps → a package family name ("claude_pzs8sxrjxfjjc"). These run their own
///     exe under \WindowsApps\, but the app picker only knows them by an AUMID pseudo-path,
///     so a plain exe-name token could never match them. The family name is the stable
///     identity shared by that AUMID and every window the packaged app opens, and it doesn't
///     change when the app updates (only the version in the install-folder name does).</summary>
public static class NeverCloseMatch
{
    /// <summary>The identity token to STORE for an app the user pinned in the picker.
    /// Lowercased so it compares cleanly against the (also lowercased) settings list.</summary>
    public static string Token(AppEntry app)
    {
        if (app.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            var family = FamilyNameFromAumid(app.Path);
            if (!string.IsNullOrEmpty(family)) return family.ToLowerInvariant();
        }
        return LaunchEngine.TargetExeName(app);
    }

    /// <summary>Normalize a stored token: lowercase/trim, and drop any "!appId" suffix left
    /// by older builds that stored a Store app's raw AUMID fragment ("family!app") instead of
    /// the family name. The family name before the "!" is the stable identity we match on, so
    /// this heals those legacy entries in place — the user doesn't have to re-add the app.</summary>
    public static string Normalize(string token)
    {
        var t = token.Trim().ToLowerInvariant();
        int bang = t.IndexOf('!');
        return bang >= 0 ? t[..bang] : t;
    }

    /// <summary>Whether a scanned window belongs to one of the user's pinned apps, and so
    /// must never be closed. Protected if the window's own exe name is pinned, OR its Store
    /// package family name is pinned (packaged apps), OR its process descends from a pinned
    /// exe (so a pinned app stays protected when its visible window is briefly owned by an
    /// updater/helper child process, e.g. Discord's Update.exe during a self-update — the
    /// same process-descent matching the launch/layout code uses). Result: an excluded app
    /// is ignored by the closing logic no matter its title, updates, tabs, workspace, or
    /// foreground/background state.</summary>
    public static bool IsProtected(WindowInfo w, HashSet<string> pinned, Dictionary<uint, ProcessTree.Entry> processTree)
    {
        if (pinned.Count == 0) return false;
        if (pinned.Contains(w.ExeName)) return true;

        var family = FamilyNameFromExePath(w.ExePath);
        if (family != null && pinned.Contains(family)) return true;

        // Ancestry only makes sense for exe-name tokens (family-name tokens aren't process
        // names). explorer.exe is skipped: the shell launches most user apps, so treating it
        // as an ancestor match would protect nearly everything the moment File Explorer is
        // pinned — Explorer windows are still matched directly by name above.
        foreach (var token in pinned)
        {
            if (!token.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(token, "explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
            if (ProcessTree.IsOrDescendsFrom(processTree, w.Pid, token)) return true;
        }
        return false;
    }

    /// <summary>Package family name for a Store/MSIX app's exe path (under \WindowsApps\),
    /// e.g. "Claude_pzs8sxrjxfjjc". Returns null for ordinary Win32 apps.</summary>
    public static string? FamilyNameFromExePath(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        const string marker = @"\WindowsApps\";
        int i = exePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;

        var rest = exePath[(i + marker.Length)..];
        int slash = rest.IndexOf(Path.DirectorySeparatorChar);
        var packageFullName = slash < 0 ? rest : rest[..slash];

        // PackageFullName is Name_Version_Arch[_ResourceId]_PublisherId, with an empty
        // ResourceId commonly collapsing to "__". The family name is Name_PublisherId —
        // the first and last underscore-delimited segments (Name itself never contains an
        // underscore; publisher packages use dots, e.g. "Microsoft.WhatsApp").
        var parts = packageFullName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? null : $"{parts[0]}_{parts[^1]}";
    }

    /// <summary>Package family name from the picker's "shell:AppsFolder\{family}!{appId}"
    /// pseudo-path — the AUMID already carries the family name before the "!".</summary>
    public static string? FamilyNameFromAumid(string shellPath)
    {
        if (string.IsNullOrEmpty(shellPath)) return null;
        int slash = shellPath.IndexOf('\\');
        var aumid = slash < 0 ? shellPath : shellPath[(slash + 1)..];
        int bang = aumid.IndexOf('!');
        var family = bang < 0 ? aumid : aumid[..bang];
        return string.IsNullOrWhiteSpace(family) ? null : family;
    }
}
