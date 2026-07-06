using System.Runtime.InteropServices;
using System.Text;

namespace Deskify.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left, Top, Right, Bottom;
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
public struct POINT { public int X, Y; }

[StructLayout(LayoutKind.Sequential)]
public struct WINDOWPLACEMENT
{
    public int length;
    public int flags;
    public int showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct SHFILEINFO
{
    public IntPtr hIcon;
    public int iIcon;
    public uint dwAttributes;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szDisplayName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
    public string szTypeName;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct PROCESSENTRY32W
{
    public uint dwSize;
    public uint cntUsage;
    public uint th32ProcessID;
    public IntPtr th32DefaultHeapID;
    public uint th32ModuleID;
    public uint cntThreads;
    public uint th32ParentProcessID;
    public int pcPriClassBase;
    public uint dwFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
    public string szExeFile;
}

internal static class NativeMethods
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    // ---- Window enumeration / inspection ----
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int maxCount);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    // ---- Window manipulation ----
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndAfter, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // ---- Cross-instance signalling (single-instance guard) ----
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string msg);
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    // ---- Global hotkey ----
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);

    // ---- Monitors ----
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")]
    public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

    // ---- Process path ----
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder sb, ref int size);

    // ---- Icons ----
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint cbInfo, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

    // ---- Process tree (for launcher-style apps whose visible window belongs
    // to a child/helper process, e.g. Steam -> steamwebhelper.exe) ----
    [DllImport("kernel32.dll")] public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);
    public const uint TH32CS_SNAPPROCESS = 0x00000002;

    // ---- Constants ----
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x00000080;

    public const int DWMWA_CLOAKED = 14;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_ASYNCWINDOWPOS = 0x4000;

    // Re-inserting a window here (with SWP_NOACTIVATE) bumps it back to the top of the
    // topmost z-order band without taking focus — used to keep the launch overlay above
    // app splash/loading windows that appear later and are themselves topmost.
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWMAXIMIZED = 3;
    public const int SW_RESTORE = 9;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const uint MONITORINFOF_PRIMARY = 1;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_SMALLICON = 0x1;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    public const uint WM_CLOSE = 0x0010;
    public const int WM_HOTKEY = 0x0312;

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint VK_SPACE = 0x20;
    public const int VK_CONTROL = 0x11;
}
