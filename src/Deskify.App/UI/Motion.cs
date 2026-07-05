using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Deskify.UI;

/// <summary>
/// Small, shared UI motion helpers. Everything here is best-effort and purely
/// cosmetic: animations always resolve to their end value (so nothing can get
/// stuck invisible), and callers never have to clean up after them.
/// </summary>
internal static class Motion
{
    private static readonly Duration Quick = new(TimeSpan.FromSeconds(0.16));
    private static readonly Duration Swap = new(TimeSpan.FromSeconds(0.20));

    private static IEasingFunction EaseOut => new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Fade a whole window up from transparent. Paired with Opacity="0" in
    /// the window's XAML so there's no first-frame flash before this runs.</summary>
    public static void FadeWindowIn(Window window)
    {
        var fade = new DoubleAnimation(0, 1, Quick) { EasingFunction = EaseOut };
        window.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    /// <summary>Fade + gently rise a view into place. Used whenever a center-panel
    /// view (projects, detail, settings, wizard, or a wizard step) becomes visible,
    /// so navigating between them reads as a soft settle rather than a hard cut.</summary>
    public static void FadeSlideIn(FrameworkElement element, double rise = 10, bool quick = false)
    {
        var duration = quick ? Quick : Swap;
        var slide = new TranslateTransform(0, rise);
        element.RenderTransform = slide;

        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(rise, 0, duration) { EasingFunction = EaseOut });
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = EaseOut });
    }
}
