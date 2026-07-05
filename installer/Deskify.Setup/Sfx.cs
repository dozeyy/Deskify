using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Deskify.Setup;

/// <summary>
/// The installer's interface sound set — the same crisp, synthesized blips the
/// app uses, kept self-contained here so setup stays a single dependency-free exe.
/// Short 16-bit PCM WAV buffers built once at startup and played through winmm's
/// <c>PlaySound</c>; a no-op when there's no audio device.
/// </summary>
internal static class Sfx
{
    public static bool Enabled { get; set; } = true;

    public enum Cue
    {
        Tap,
        Click,
        Toggle,
        Confirm,
        Success,
    }

    private const int SampleRate = 44100;
    private const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(IntPtr data, IntPtr hmod, uint flags);

    private static readonly Dictionary<Cue, IntPtr> Buffers = new();

    static Sfx()
    {
        try
        {
            Register(Cue.Tap, Tone(1500, 0.026, 0.13, 150));
            Register(Cue.Click, Tone(1850, 0.034, 0.22, 120));
            Register(Cue.Toggle, Tone(2100, 0.028, 0.17, 135));
            Register(Cue.Confirm, Sequence((880, 0.05, 0.20, 60), (1320, 0.09, 0.22, 42)));
            Register(Cue.Success, Sequence((660, 0.06, 0.17, 52), (990, 0.06, 0.18, 48), (1400, 0.16, 0.22, 24)));
        }
        catch
        {
            Buffers.Clear();
        }
    }

    public static void Play(Cue cue)
    {
        if (!Enabled) return;
        if (!Buffers.TryGetValue(cue, out var ptr)) return;
        try { PlaySound(ptr, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT); }
        catch { /* no audio device — stay silent */ }
    }

    private static void Register(Cue cue, byte[] wav)
    {
        var handle = GCHandle.Alloc(wav, GCHandleType.Pinned);
        Buffers[cue] = handle.AddrOfPinnedObject();
    }

    private static byte[] Tone(double freq, double seconds, double amp, double decay) =>
        Wav(Render(new List<float>(), freq, seconds, amp, decay).ToArray());

    private static byte[] Sequence(params (double freq, double sec, double amp, double decay)[] notes)
    {
        var samples = new List<float>();
        foreach (var (freq, sec, amp, decay) in notes)
            Render(samples, freq, sec, amp, decay);
        return Wav(samples.ToArray());
    }

    private static List<float> Render(List<float> into, double freq, double seconds, double amp, double decay)
    {
        int n = (int)(SampleRate * seconds);
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)SampleRate;
            double env = Math.Exp(-t * decay);
            double attack = Math.Min(1.0, t / 0.0015);
            into.Add((float)(Math.Sin(2 * Math.PI * freq * t) * env * attack * amp));
        }
        return into;
    }

    private static byte[] Wav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataBytes);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(SampleRate);
        bw.Write(SampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);

        foreach (var s in samples)
        {
            int v = (int)(Math.Clamp(s, -1f, 1f) * short.MaxValue);
            bw.Write((short)v);
        }
        bw.Flush();
        return ms.ToArray();
    }
}
