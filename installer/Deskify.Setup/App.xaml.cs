using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace Deskify.Setup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Contains("/uninstall", StringComparer.OrdinalIgnoreCase))
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
