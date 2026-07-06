using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Deskify.Services;

/// <summary>Reads the user's chosen default web browser from the registry — the
/// same place Windows itself looks when handing off an http/https link.</summary>
public static class DefaultBrowser
{
    public static (string Path, string Name)? Detect()
    {
        try
        {
            using var userChoice = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            var progId = userChoice?.GetValue("ProgId") as string;

            if (!string.IsNullOrEmpty(progId))
            {
                // Primary: the exact ProgId Windows records as the http default.
                if (FromProgId(progId) is { } direct) return direct;

                // Fallback for stale/mismatched registrations. Firefox and its forks
                // (Zen, LibreWolf, Waterfox…) register a per-install *hashed* ProgId like
                // "FirefoxURL-308046B0AF4A39CB", and a browser update can leave UserChoice
                // pointing at an old hash whose shell\open\command no longer exists — while
                // a sibling ProgId with the same base name ("FirefoxURL-F0DC299D809B9700")
                // is registered and working. Windows still resolves the link via other
                // associations, so the browser opens fine; only this strict ProgId→command
                // lookup came up empty. Match by the base name (before the last '-') so we
                // still find the real browser exe instead of giving up and never creating a
                // browser entry to position.
                int dash = progId.LastIndexOf('-');
                if (dash > 0 && FromProgIdPrefix(progId[..(dash + 1)]) is { } byBase) return byBase;
            }

            // Last resort: a registered "internet client" browser (skips the legacy IE stub).
            return FromStartMenuInternet();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Resolve a single ProgId to its browser exe via shell\open\command.</summary>
    private static (string Path, string Name)? FromProgId(string progId)
    {
        using var commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
        if (commandKey?.GetValue(null) is not string command || command.Length == 0) return null;

        var path = ExtractExePath(command);
        if (path == null || !File.Exists(path)) return null;
        return (path, FriendlyName(path));
    }

    /// <summary>Find a registered ProgId whose name starts with <paramref name="prefix"/>
    /// (e.g. "FirefoxURL-") that resolves to a real exe — recovers the working sibling of a
    /// stale hashed ProgId. Only runs when the exact ProgId lookup already failed, so the
    /// one-time enumeration of HKCR is off the hot path.</summary>
    private static (string Path, string Name)? FromProgIdPrefix(string prefix)
    {
        foreach (var name in Registry.ClassesRoot.GetSubKeyNames())
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (FromProgId(name) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>Enumerate registered browsers under StartMenuInternet as a final fallback,
    /// picking the first that resolves to a real exe other than the legacy IE stub.</summary>
    private static (string Path, string Name)? FromStartMenuInternet()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var clients = hive.OpenSubKey(@"Software\Clients\StartMenuInternet");
            if (clients == null) continue;
            foreach (var name in clients.GetSubKeyNames())
            {
                using var commandKey = clients.OpenSubKey($@"{name}\shell\open\command");
                if (commandKey?.GetValue(null) is not string command || command.Length == 0) continue;

                var path = ExtractExePath(command);
                if (path == null || !File.Exists(path)) continue;
                if (string.Equals(Path.GetFileName(path), "iexplore.exe", StringComparison.OrdinalIgnoreCase)) continue;
                return (path, FriendlyName(path));
            }
        }
        return null;
    }

    private static string? ExtractExePath(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : null;
        }
        int space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    private static string FriendlyName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription)) return info.FileDescription!;
            if (!string.IsNullOrWhiteSpace(info.ProductName)) return info.ProductName!;
        }
        catch
        {
            // Fall through to the file name.
        }
        return Path.GetFileNameWithoutExtension(path);
    }
}
