using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace Deskify.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Two ways this runs as the uninstaller rather than the installer:
        // Add/Remove Programs invokes the copy at <installDir>\Uninstall.exe with
        // this flag (see MainWindow.RegisterUninstall's UninstallString), and
        // running that same copy directly (by filename) does the obviously-expected
        // thing too, in case anyone launches it without the flag.
        bool isUninstall = e.Args.Contains("/uninstall", StringComparer.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(Environment.ProcessPath ?? ""), "Uninstall.exe", StringComparison.OrdinalIgnoreCase);

        if (isUninstall)
        {
            Uninstaller.Run();
            MessageBox.Show("Deskify has been uninstalled.", "Deskify",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Crisp click on every button/toggle, wired once for the whole installer.
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent,
            new RoutedEventHandler((sender, _) =>
                Sfx.Play(sender is ToggleButton ? Sfx.Cue.Toggle : Sfx.Cue.Click)));

        base.OnStartup(e);
    }
}
