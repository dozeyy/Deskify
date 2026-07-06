using System.Threading;
using System.Windows;
using Deskify.Interop;
using Deskify.Services;
using Deskify.UI;

namespace Deskify;

public partial class App : Application
{
    /// <summary>Broadcast when a second copy of Deskify is launched, telling the
    /// already-running instance to surface its window. Registered once per process;
    /// the same message name resolves to the same id in every instance.</summary>
    public static readonly uint ShowExistingWindowMessage =
        NativeMethods.RegisterWindowMessage("Deskify.ShowExistingWindow.9F3A");

    // Held for the whole process lifetime so it isn't collected — releasing the mutex
    // is what lets the NEXT launch become the single instance. Named "Local\" so it's
    // per-user-session, which is what we want (one Deskify per logged-in desktop).
    private static Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance: a global Ctrl+Space hotkey can only belong to one process,
        // so a second Deskify would silently fail to register it (and leave the user's
        // quick-switch dead). If one is already running, just bring it forward and exit.
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\Deskify.SingleInstance.9F3A", out bool isFirst);
        if (!isFirst)
        {
            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, ShowExistingWindowMessage, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Log.Info("Deskify started");

        var settings = AppSettings.Load();

        // Merge the chosen theme's brushes, then the shared structural styles that
        // reference them — must happen before the main window is created.
        ThemeManager.Apply(settings.ThemeName);

        // Motion polish, wired once for the whole app: every window fades in as it
        // loads. Registered before the main window is created so the first one is
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

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window) Motion.FadeWindowIn(window);
    }
}
