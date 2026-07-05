using System.Windows;
using System.Windows.Controls.Primitives;
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

        // Interface sound + motion polish, wired once for the whole app:
        //  · every button/toggle click gets a crisp cue (no per-handler plumbing);
        //  · every window fades in as it loads.
        // Class handlers are registered here, before the StartupUri window is
        // created (WPF loads it only after OnStartup returns), so the very first
        // window is covered too.
        Sfx.Enabled = settings.InterfaceSounds;
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent,
            new RoutedEventHandler(OnAnyClick));
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

    private static void OnAnyClick(object sender, RoutedEventArgs e)
    {
        // Toggles (check boxes, the snap toggle) get their own softer flip; every
        // other button gets the standard click.
        Sfx.Play(sender is ToggleButton ? Sfx.Cue.Toggle : Sfx.Cue.Click);
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window) Motion.FadeWindowIn(window);
    }
}
