using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Deskify.Services;

/// <summary>
/// The whole interface sound set, synthesized once at startup as tiny in-memory
/// 16-bit PCM WAV buffers and played through winmm's <c>PlaySound</c>. No asset
/// files, no NuGet dependencies, nothing to ship — just a handful of short, clean
/// sine blips. Sounds are deliberately quiet, sharp, and under ~150 ms so the UI
/// feels responsive rather than noisy; the whole thing is a no-op when
/// <see cref="Enabled"/> is false or the machine has no audio device.
/// </summary>
public static class Sfx
{
    /// <summary>Master switch — mirrors the "Interface sounds" setting. When false,
    /// <see cref="Play"/> returns immediately.</summary>
    public static bool Enabled { get; set; } = true;

    public enum Cue
    {
        /// <summary>Soft, low-key tick — secondary/navigation actions.</summary>
        Tap,
        /// <summary>The default crisp click for buttons.</summary>
        Click,
        /// <summary>Check-box / toggle flip.</summary>
        Toggle,
        /// <summary>Two-note rising blip for a committed primary action.</summary>
        Confirm,
        /// <summary>Three-note resolve for "done" moments (install finished, workspace restored).</summary>
        Success,
    }

    private const int SampleRate = 44100;
    private const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_MEMORY = 0x0004;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool PlaySound(IntPtr data, IntPtr hmod, uint flags);

    // Cached, permanently pinned WAV buffers. Pinning matters: PlaySound plays the
    // buffer asynchronously and reads from it after the call returns, so the GC must
    // not move it out from under the sound. The handles live for the whole process.
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
            // Sound is a nicety, never a hard requirement — if synthesis ever fails,
            // the app simply runs silently rather than refusing to start.
            Buffers.Clear();
        }
    }

    /// <summary>Fire-and-forget: plays <paramref name="cue"/> immediately, cutting
    /// off any still-ringing cue (a single channel keeps rapid clicks crisp).</summary>
    public static void Play(Cue cue)
    {
        if (!Enabled) return;
        if (!Buffers.TryGetValue(cue, out var ptr)) return;
        try { PlaySound(ptr, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT); }
        catch { /* no audio device / driver — stay silent */ }
    }

    private static void Register(Cue cue, byte[] wav)
    {
        var handle = GCHandle.Alloc(wav, GCHandleType.Pinned);
        Buffers[cue] = handle.AddrOfPinnedObject();
    }

    /// <summary>One decaying sine note. <paramref name="decay"/> is the exponential
    /// rate per second — higher is a shorter, snappier tail.</summary>
    private static byte[] Tone(double freq, double seconds, double amp, double decay) =>
        Wav(Render(new List<float>(), freq, seconds, amp, decay).ToArray());

    /// <summary>Several notes played back to back (a short melodic blip).</summary>
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
            // ~1.5 ms linear fade-in so the note starts on a zero crossing instead
            // of a hard step (which would add an ugly DC "pop" before the tone).
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
        bw.Write(16);               // fmt chunk size
        bw.Write((short)1);         // PCM
        bw.Write((short)1);         // mono
        bw.Write(SampleRate);
        bw.Write(SampleRate * 2);   // byte rate (mono, 2 bytes/sample)
        bw.Write((short)2);         // block align
        bw.Write((short)16);        // bits per sample
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
