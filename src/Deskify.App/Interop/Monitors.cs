namespace Deskify.Interop;

public sealed class MonitorInfo
{
    public int Index;
    public IntPtr Handle;
    public RECT Bounds;   // full monitor rect, physical px, virtual-screen coords
    public RECT Work;     // work area (minus taskbar)
    public bool Primary;

    public override string ToString() =>
        $"Monitor {Index + 1} ({Bounds.Width}x{Bounds.Height}{(Primary ? ", primary" : "")})";
}

public static class Monitors
{
    /// <summary>All monitors, sorted left-to-right then top-to-bottom for a stable index.</summary>
    public static List<MonitorInfo> All()
    {
        var list = new List<MonitorInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (IntPtr hMon, IntPtr _, ref RECT _, IntPtr _) =>
            {
                var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
                if (NativeMethods.GetMonitorInfo(hMon, ref info))
                {
                    list.Add(new MonitorInfo
                    {
                        Handle = hMon,
                        Bounds = info.rcMonitor,
                        Work = info.rcWork,
                        Primary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                    });
                }
                return true;
            }, IntPtr.Zero);

        list.Sort((a, b) => a.Bounds.Left != b.Bounds.Left
            ? a.Bounds.Left.CompareTo(b.Bounds.Left)
            : a.Bounds.Top.CompareTo(b.Bounds.Top));
        for (int i = 0; i < list.Count; i++) list[i].Index = i;
        return list;
    }

    public static MonitorInfo ForWindow(IntPtr hwnd, List<MonitorInfo> monitors)
    {
        var hMon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        foreach (var m in monitors)
            if (m.Handle == hMon) return m;
        return monitors.Find(m => m.Primary) ?? monitors[0];
    }

    /// <summary>Clamp a saved monitor index to what's actually connected right now.</summary>
    public static MonitorInfo ByIndex(int index, List<MonitorInfo> monitors)
    {
        if (index >= 0 && index < monitors.Count) return monitors[index];
        return monitors.Find(m => m.Primary) ?? monitors[0];
    }
}
