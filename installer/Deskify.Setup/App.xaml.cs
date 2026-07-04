using System.Linq;
using System.Windows;

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

        base.OnStartup(e);
    }
}
