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
            if (userChoice?.GetValue("ProgId") is not string progId || progId.Length == 0) return null;

            using var commandKey = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            if (commandKey?.GetValue(null) is not string command || command.Length == 0) return null;

            var path = ExtractExePath(command);
            if (path == null || !File.Exists(path)) return null;

            return (path, FriendlyName(path));
        }
        catch
        {
            return null;
        }
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
