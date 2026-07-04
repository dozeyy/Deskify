using System.Runtime.InteropServices;
using System.Text;

namespace Deskify.Services;

/// <summary>Resolves a .lnk shortcut to the real .exe it points at, plus any
/// arguments baked into the shortcut itself. Without this, an app added by
/// browsing to a desktop shortcut (e.g. Discord, whose shortcut target is really
/// Update.exe with "--processStart Discord.exe" as its arguments) loses those
/// arguments entirely: launching the bare target does nothing useful, and every
/// exe-name-based window match (layout capture, "close other apps") fails forever
/// after, since the running process is never named "whatever.lnk" — or, for
/// launcher-style apps, never named the shortcut's raw target either.</summary>
public static class ShortcutResolver
{
    /// <summary>Just the target path — used where arguments don't matter.</summary>
    public static string Resolve(string path) => ResolveWithArgs(path).Path;

    /// <summary>Target path plus the shortcut's own Arguments field (empty string if none).</summary>
    public static (string Path, string Arguments) ResolveWithArgs(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return (path, "");
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(path, 0);

            var pathBuf = new StringBuilder(260);
            link.GetPath(pathBuf, pathBuf.Capacity, out _, 0);
            var target = pathBuf.ToString();

            var argsBuf = new StringBuilder(1024);
            link.GetArguments(argsBuf, argsBuf.Capacity);

            return (string.IsNullOrWhiteSpace(target) ? path : target, argsBuf.ToString());
        }
        catch
        {
            return (path, "");
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    // Declared in exact vtable order (GetPath through GetArguments) — COM interop
    // maps declared methods to vtable slots positionally, so every method before
    // the one we actually want to call must still be present with a correct signature.
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath,
            out WIN32_FIND_DATAW pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
