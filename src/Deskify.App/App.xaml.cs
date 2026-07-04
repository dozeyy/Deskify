using System.Windows;
using Deskify.Services;

namespace Deskify;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Info("Deskify started");

        // Merge the chosen theme's brushes, then the shared structural styles that
        // reference them — must happen before the StartupUri window is loaded.
        ThemeManager.Apply(AppSettings.Load().ThemeName);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show(args.Exception.Message, "Deskify — unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
