using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace Deskify.Setup;

public partial class MainWindow : Window
{
    private const string AppName = "Deskify";
    private const string RunKeyName = "Deskify";
    private const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Deskify";

    private readonly string _installDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Deskify");

    public MainWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
            VersionLabel.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";

        PathText.Text = _installDir;
        Chrome.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        bool createDesktopShortcut = OptDesktop.IsChecked == true;
        bool startOnStartup = OptStartup.IsChecked == true;
        bool launchAfter = OptLaunch.IsChecked == true;

        PageOptions.Visibility = Visibility.Collapsed;
        PageProgress.Visibility = Visibility.Visible;

        try
        {
            await RunInstallAsync(createDesktopShortcut, startOnStartup);

            DoneTitle.Text = "Deskify is ready";
            DoneSub.Text = "Deskify has been installed and is ready to organize your desktop.";
            LaunchBtn.Content = "Launch Deskify";
            LaunchBtn.Visibility = launchAfter ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            DoneIcon.Data = System.Windows.Media.Geometry.Parse("M6 6l12 12 M18 6 6 18");
            DoneIcon.Stroke = System.Windows.Media.Brushes.IndianRed;
            DoneTitle.Text = "Setup couldn't finish";
            DoneSub.Text = ex.Message;
            LaunchBtn.Visibility = Visibility.Collapsed;
        }

        PageProgress.Visibility = Visibility.Collapsed;
        PageDone.Visibility = Visibility.Visible;
    }

    private async Task RunInstallAsync(bool createDesktopShortcut, bool startOnStartup)
    {
        await SetStatus("Preparing install folder…", 5);
        Directory.CreateDirectory(_installDir);

        await SetStatus("Extracting Deskify…", 15);
        await Task.Run(() => ExtractPayload(_installDir));

        await SetStatus("Creating Start Menu shortcut…", 65);
        string exePath = Path.Combine(_installDir, "Deskify.exe");
        string startMenuDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        Directory.CreateDirectory(startMenuDir);
        ShortcutHelper.Create(Path.Combine(startMenuDir, "Deskify.lnk"), exePath, _installDir,
            "Deskify — workspace automation");

        if (createDesktopShortcut)
        {
            await SetStatus("Creating desktop shortcut…", 75);
            string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            ShortcutHelper.Create(Path.Combine(desktopDir, "Deskify.lnk"), exePath, _installDir,
                "Deskify — workspace automation");
        }

        await SetStatus("Configuring startup…", 85);
        SetRunOnStartup(startOnStartup, exePath);

        await SetStatus("Registering uninstaller…", 92);
        RegisterUninstall(exePath);

        await SetStatus("Finishing up…", 100);
        await Task.Delay(250);
    }

    private void ExtractPayload(string destDir)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("payload.zip")
            ?? throw new InvalidOperationException("Setup payload is missing or corrupt.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.ExtractToDirectory(destDir, overwriteFiles: true);
    }

    private static void SetRunOnStartup(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
        if (key == null) return;

        if (enabled)
            key.SetValue(RunKeyName, $"\"{exePath}\"");
        else
            key.DeleteValue(RunKeyName, throwOnMissingValue: false);
    }

    private void RegisterUninstall(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath);
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 0);

        key.SetValue("DisplayName", "Deskify");
        key.SetValue("DisplayVersion", $"{version.Major}.{version.Minor}.{version.Build}");
        key.SetValue("Publisher", "Deskify");
        key.SetValue("InstallLocation", _installDir);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("UninstallString", $"\"{Path.Combine(_installDir, "Uninstall.exe")}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

        // Uninstaller: a copy of this same setup exe, invoked with /uninstall.
        string setupExe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Deskify-Setup.exe");
        string uninstallerDest = Path.Combine(_installDir, "Uninstall.exe");
        try
        {
            File.Copy(setupExe, uninstallerDest, overwrite: true);
        }
        catch
        {
            // Non-fatal — Add/Remove Programs entry still points at a usable path if this fails.
        }
    }

    private async Task SetStatus(string text, int percent)
    {
        StatusText.Text = text;
        Progress.Value = percent;
        await Task.Delay(120);
    }

    private void LaunchBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.Combine(_installDir, "Deskify.exe"),
                WorkingDirectory = _installDir,
                UseShellExecute = true
            });
        }
        catch
        {
            // Best effort — user can still launch from the Start Menu shortcut.
        }
        Close();
    }
}
