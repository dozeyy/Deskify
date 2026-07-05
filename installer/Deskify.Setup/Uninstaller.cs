using System.IO;
using Microsoft.Win32;

namespace Deskify.Setup;

/// <summary>
/// Runs when the copy of setup placed in the install dir is invoked as
/// "Uninstall.exe" (via Add/Remove Programs's UninstallString). Removes
/// shortcuts, the Run-on-startup entry, the Add/Remove Programs entry, and
/// finally the install directory itself.
/// </summary>
internal static class Uninstaller
{
    private const string RunKeyName = "Deskify";
    private const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Deskify";

    public static void Run()
    {
        string installDir = AppContext.BaseDirectory.TrimEnd('\\', '/');

        TryDelete(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Deskify.lnk"));
        TryDelete(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Deskify.lnk"));

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            runKey?.DeleteValue(RunKeyName, throwOnMissingValue: false);
        }
        catch { /* best effort */ }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch { /* best effort */ }

        // Can't delete the running exe's own directory synchronously (file is locked).
        // Schedule a delayed delete via a detached PowerShell process that polls for
        // this process to actually exit (a fixed delay would race the "uninstalled"
        // message box, which stays open however long the user takes to dismiss it).
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-WindowStyle");
            psi.ArgumentList.Add("Hidden");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add(
                $"while (Get-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue) " +
                $"{{ Start-Sleep -Milliseconds 500 }}; " +
                $"Remove-Item -LiteralPath '{installDir}' -Recurse -Force -ErrorAction SilentlyContinue");
            System.Diagnostics.Process.Start(psi);
        }
        catch { /* best effort */ }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best effort */ }
    }
}
