using System.Windows;
using Deskify.Services;
using Deskify.UI;

namespace Deskify;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Info("Deskify started");

        var settings = AppSettings.Load();

        // Merge the chosen theme's brushes, then the shared structural styles that
        // reference them — must happen before the StartupUri window is loaded.
        ThemeManager.Apply(settings.ThemeName);

        // Motion polish, wired once for the whole app: every window fades in as it
        // loads. Registered here, before the StartupUri window is created (WPF
        // loads it only after OnStartup returns), so the very first window is
        // covered too.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            MessageBox.Show(args.Exception.Message, "Deskify — unexpected error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window) Motion.FadeWindowIn(window);
    }
}
