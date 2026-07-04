using Deskify.Interop;
using Deskify.Models;

namespace Deskify.Services;

/// <summary>Captures and applies window placements (position, size, monitor, state).</summary>
public static class LayoutService
{
    /// <summary>Capture the current placement of a window, relative to its monitor.</summary>
    public static WindowLayout? Capture(IntPtr hwnd, List<MonitorInfo> monitors)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return null;
        var mon = Monitors.ForWindow(hwnd, monitors);
        bool maximized = NativeMethods.IsZoomed(hwnd);

        if (maximized)
        {
            // Store the restored ("normal") rect so un-maximizing later still lands sanely.
            var placement = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
            if (NativeMethods.GetWindowPlacement(hwnd, ref placement))
            {
                // rcNormalPosition is in workspace coords; translate via the work-area origin.
                var np = placement.rcNormalPosition;
                return new WindowLayout
                {
                    Monitor = mon.Index,
                    X = np.Left + (mon.Work.Left - mon.Bounds.Left),
                    Y = np.Top + (mon.Work.Top - mon.Bounds.Top),
                    Width = np.Width,
                    Height = np.Height,
                    State = "maximized",
                };
            }
            return new WindowLayout
            {
                Monitor = mon.Index,
                X = 0, Y = 0,
                Width = mon.Work.Width, Height = mon.Work.Height,
                State = "maximized",
            };
        }

        return new WindowLayout
        {
            Monitor = mon.Index,
            X = rect.Left - mon.Bounds.Left,
            Y = rect.Top - mon.Bounds.Top,
            Width = rect.Width,
            Height = rect.Height,
            State = "normal",
        };
    }

    /// <summary>Apply a saved placement. Best effort: returns false if the OS refused.</summary>
    public static bool Apply(IntPtr hwnd, WindowLayout layout, List<MonitorInfo> monitors)
    {
        if (!NativeMethods.IsWindow(hwnd)) return false;

        var mon = Monitors.ByIndex(layout.Monitor, monitors);
        int x = mon.Bounds.Left + layout.X;
        int y = mon.Bounds.Top + layout.Y;
        int w = Math.Max(layout.Width, 100);
        int h = Math.Max(layout.Height, 80);

        // Always leave maximized/minimized state first, otherwise SetWindowPos is ignored.
        if (NativeMethods.IsZoomed(hwnd) || NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        bool ok = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        if (layout.IsMaximized)
        {
            // Window is now on the target monitor, so maximize fills that monitor.
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOWMAXIMIZED);
        }
        return ok;
    }

    /// <summary>True if the window's current rect is within tolerance of the saved layout.</summary>
    public static bool Matches(IntPtr hwnd, WindowLayout layout, List<MonitorInfo> monitors, int tolerance = 16)
    {
        if (!NativeMethods.IsWindow(hwnd)) return false;
        if (layout.IsMaximized) return NativeMethods.IsZoomed(hwnd);
        if (!NativeMethods.GetWindowRect(hwnd, out var rect)) return false;

        var mon = Monitors.ByIndex(layout.Monitor, monitors);
        int x = mon.Bounds.Left + layout.X;
        int y = mon.Bounds.Top + layout.Y;
        return Math.Abs(rect.Left - x) <= tolerance
            && Math.Abs(rect.Top - y) <= tolerance
            && Math.Abs(rect.Width - layout.Width) <= tolerance
            && Math.Abs(rect.Height - layout.Height) <= tolerance;
    }
}
