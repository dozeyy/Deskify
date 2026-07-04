using System.Runtime.InteropServices;
using Deskify.Interop;

namespace Deskify.Services;

/// <summary>Walks Windows' process ancestry. Many "launcher" apps show their real
/// window from a helper/child process rather than the one you actually started —
/// Steam's window belongs to steamwebhelper.exe, Riot Client's to a UX child
/// process, etc. Matching by exe name alone misses these; checking whether a
/// candidate window's process descends from the target exe catches them generically,
/// without hardcoding a list of known launcher apps.</summary>
public static class ProcessTree
{
    public readonly record struct Entry(uint ParentPid, string ExeName);

    /// <summary>One-time snapshot of every running process's parent + exe name.
    /// Take one per matching pass and reuse it — a fresh snapshot per window/app
    /// pair would be wasteful.</summary>
    public static Dictionary<uint, Entry> Snapshot()
    {
        var map = new Dictionary<uint, Entry>();
        IntPtr snap = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snap == new IntPtr(-1)) return map;

        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!NativeMethods.Process32FirstW(snap, ref entry)) return map;
            do
            {
                map[entry.th32ProcessID] = new Entry(entry.th32ParentProcessID, entry.szExeFile);
            } while (NativeMethods.Process32NextW(snap, ref entry));
        }
        finally
        {
            NativeMethods.CloseHandle(snap);
        }
        return map;
    }

    /// <summary>True if the given process, or an ancestor within maxDepth hops,
    /// is named targetExeName. Depth-limited and loop-guarded against corrupt/
    /// recycled PID chains.</summary>
    public static bool IsOrDescendsFrom(Dictionary<uint, Entry> tree, uint pid, string targetExeName, int maxDepth = 6)
    {
        var seen = new HashSet<uint>();
        for (int i = 0; i < maxDepth; i++)
        {
            if (!seen.Add(pid)) return false;
            if (!tree.TryGetValue(pid, out var entry)) return false;
            if (string.Equals(entry.ExeName, targetExeName, StringComparison.OrdinalIgnoreCase)) return true;
            if (entry.ParentPid == 0) return false;
            pid = entry.ParentPid;
        }
        return false;
    }
}
