using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Deskify.Setup;

public partial class MainWindow : Window
{
    private const string AppName = "Deskify";
    private const string RunKeyName = "Deskify";
    private const string UninstallKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Deskify";

    private bool _closing;

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

        Loaded += (_, _) => AnimateIn();
    }

    // ==================== Window intro / outro ====================

    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Fade + a gentle scale-up as the window appears.</summary>
    private void AnimateIn()
    {
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.20)) { EasingFunction = EaseOut });
        var scale = (ScaleTransform)Root.RenderTransform;
        var pop = new DoubleAnimation(0.97, 1, TimeSpan.FromSeconds(0.24)) { EasingFunction = EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    /// <summary>Fade + settle back down, then actually close. Guarded so the same
    /// click can't kick off two closes.</summary>
    private void AnimateOutAndClose()
    {
        if (_closing) return;
        _closing = true;

        var scale = (ScaleTransform)Root.RenderTransform;
        var shrink = new DoubleAnimation(1, 0.97, TimeSpan.FromSeconds(0.14)) { EasingFunction = EaseOut };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.14)) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Cross-fade between two setup pages: the outgoing one fades away
    /// while the incoming one fades and rises into place.</summary>
    private static void SwapPage(FrameworkElement from, FrameworkElement to)
    {
        to.Opacity = 0;
        to.Visibility = Visibility.Visible;
        var slide = new TranslateTransform(0, 12);
        to.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromSeconds(0.24)) { EasingFunction = EaseOut });
        to.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.24)) { EasingFunction = EaseOut });

        var fadeOut = new DoubleAnimation(from.Opacity, 0, TimeSpan.FromSeconds(0.14)) { EasingFunction = EaseOut };
        fadeOut.Completed += (_, _) =>
        {
            from.BeginAnimation(OpacityProperty, null);
            from.Opacity = 1;
            from.Visibility = Visibility.Collapsed;
        };
        from.BeginAnimation(OpacityProperty, fadeOut);
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) => AnimateOutAndClose();

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        bool createDesktopShortcut = OptDesktop.IsChecked == true;
        bool startOnStartup = OptStartup.IsChecked == true;
        bool launchAfter = OptLaunch.IsChecked == true;

        SwapPage(PageOptions, PageProgress);

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

        SwapPage(PageProgress, PageDone);
        PopBadge();
    }

    /// <summary>A springy little pop on the result badge as the final page lands.</summary>
    private void PopBadge()
    {
        var scale = (ScaleTransform)DoneBadge.RenderTransform;
        var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromSeconds(0.42))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.9 },
            BeginTime = TimeSpan.FromSeconds(0.08),
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private async Task RunInstallAsync(bool createDesktopShortcut, bool startOnStartup)
    {
        await SetStatus("Preparing install folder…", 5);
        Directory.CreateDirectory(_installDir);

        await CloseRunningApp();

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

    /// <summary>If Deskify is already running from this install dir (e.g. reinstalling
    /// or upgrading while the app is open), close it first — otherwise extraction fails
    /// with a locked-file error instead of something the user can act on.</summary>
    private async Task CloseRunningApp()
    {
        string exePath = Path.Combine(_installDir, "Deskify.exe");
        var running = new List<System.Diagnostics.Process>();
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Deskify"))
        {
            try
            {
                if (string.Equals(proc.MainModule?.FileName, exePath, StringComparison.OrdinalIgnoreCase))
                    running.Add(proc);
            }
            catch { /* inaccessible process, assume it's not ours */ }
        }
        if (running.Count == 0) return;

        await SetStatus("Closing the running copy of Deskify…", 10);
        foreach (var proc in running)
        {
            try { proc.CloseMainWindow(); } catch { /* best effort */ }
        }
        foreach (var proc in running)
        {
            try
            {
                if (!proc.WaitForExit(3000)) proc.Kill(entireProcessTree: true);
            }
            catch { /* best effort */ }
        }
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
        string uninstallCmd = $"\"{Path.Combine(_installDir, "Uninstall.exe")}\" /uninstall";
        key.SetValue("UninstallString", uninstallCmd);
        key.SetValue("QuietUninstallString", uninstallCmd);
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
        // Glide the bar to its new value instead of snapping — reads far smoother.
        Progress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
            new DoubleAnimation(percent, TimeSpan.FromSeconds(0.35)) { EasingFunction = EaseOut });
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
        AnimateOutAndClose();
    }
}
