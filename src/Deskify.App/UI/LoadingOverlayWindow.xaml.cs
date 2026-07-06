using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Deskify.Interop;
using Deskify.Services;

namespace Deskify.UI;

/// <summary>Full-screen translucent "please wait" overlay shown while a workspace
/// launches. Apps open and finish loading behind the dimmed screen, then snap into
/// their saved positions; only once everything is placed does this fade away — so the
/// user never watches windows shuffle mid-load or clicks an app before it's ready.
/// Sized to the whole virtual screen so it covers every monitor.</summary>
public partial class LoadingOverlayWindow : Window
{
    // WPF's Topmost alone isn't enough: an app's splash/loading window (FL Studio's
    // fruit, Photoshop's splash, etc.) that opens AFTER us and is itself topmost gets
    // inserted above us in the topmost z-order band, briefly poking through the scrim.
    // This timer re-asserts us at the top of that band a few times a second — with
    // SWP_NOACTIVATE, so we never steal focus from the apps launching and positioning
    // behind the overlay. Runs only while the overlay is on screen.
    private readonly DispatcherTimer _keepOnTop;

    public LoadingOverlayWindow()
    {
        InitializeComponent();

        _keepOnTop = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _keepOnTop.Tick += (_, _) => BringToFront();
        Closed += (_, _) => _keepOnTop.Stop();

        // The scrim covers the entire virtual desktop (all monitors), not just the primary one.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        // ...but the card is centered on the PRIMARY monitor regardless of how many
        // monitors are connected or where they sit. The primary monitor's origin is
        // always (0,0) in virtual-screen coordinates, so its offset from this window's
        // top-left is simply -VirtualScreenLeft/-VirtualScreenTop. Sizing the region to
        // the primary monitor makes the card's "Center" alignment land dead-center on it.
        // (All SystemParameters values are in device-independent units, so they combine
        // consistently.)
        PrimaryMonitorArea.Margin = new Thickness(
            -SystemParameters.VirtualScreenLeft, -SystemParameters.VirtualScreenTop, 0, 0);
        PrimaryMonitorArea.Width = SystemParameters.PrimaryScreenWidth;
        PrimaryMonitorArea.Height = SystemParameters.PrimaryScreenHeight;

        ThemeManager.ApplyTitleBar(this);
    }

    /// <summary>Update the sub-line describing the current launch phase.</summary>
    public void SetStatus(string text)
    {
        if (Dispatcher.CheckAccess()) StatusLine.Text = text;
        else Dispatcher.Invoke(() => StatusLine.Text = text);
    }

    public void FadeIn()
    {
        Show();
        BringToFront();      // win the z-order immediately, before the first timer tick
        _keepOnTop.Start();  // then keep re-asserting it over late-appearing splash windows
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    /// <summary>Fade the scrim out and close once it's gone, revealing the arranged windows.</summary>
    public void FadeOutAndClose()
    {
        _keepOnTop.Stop();   // stop fighting for the top as we hand the screen back
        var anim = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(240));
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Re-insert the overlay at the top of the topmost z-order band without
    /// activating it, so it stays visually above every other window — including other
    /// topmost splash/loading windows — while never taking focus from the launching apps.</summary>
    private void BringToFront()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }
}
