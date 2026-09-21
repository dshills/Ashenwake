using System.Buffers.Binary;
using Godot;

namespace Ashenwake.Client;

/// <summary>Bounded, locally synthesized void ambience. No gameplay random stream or clock is involved.</summary>
public static class HollowAmbience
{
    public const int SampleRate = 22050;
    public const int DurationSeconds = 10;
    public const int SampleCount = SampleRate * DurationSeconds;
    private static readonly Dictionary<string, AudioStreamWav> Streams = [];
    public static IReadOnlyList<string> CueNames { get; } = Array.AsReadOnly<string>(["hollow_rooms", "hollow_memory", "hollow_breach"]);
    public static int CachedStreamCount => Streams.Count;

    public static string CueForStyle(string style) => style == "hollow_vault" ? "hollow_memory" : style is "hollow_rooms" or "hollow_memory" or "hollow_breach" ? style : "";

    public static void Prewarm()
    {
        foreach (string cue in CueNames) GetStream(cue);
    }

    public static AudioStreamWav GetStream(string cue)
    {
        if (Streams.TryGetValue(cue, out var cached)) return cached;
        byte[] samples = CreateSamples(cue);
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Data = samples,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            LoopEnd = SampleCount
        };
        Streams.Add(cue, stream);
        return stream;
    }

    public static byte[] CreateSamples(string cue)
    {
        int kind = cue switch
        {
            "hollow_rooms" => 0,
            "hollow_memory" => 1,
            "hollow_breach" => 2,
            _ => throw new ArgumentException($"Unknown Hollow ambience cue: {cue}", nameof(cue))
        };
        const int overlap = SampleRate / 2;
        double[] raw = new double[SampleCount + overlap];
        uint noiseState = unchecked(0x71F034ABu + (uint)kind * 0x68E31DA4u);
        double air = 0;
        for (int i = -SampleRate; i < raw.Length; i++)
        {
            noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
            double noise = noiseState / (double)uint.MaxValue * 2 - 1;
            air += (noise - air) * .007;
            if (i < 0) continue;
            double time = i / (double)SampleRate, cycle = time % DurationSeconds;
            double signal = air * (.9 + .15 * Wave(.2, time));
            double pitch = kind == 2 ? 36 : kind == 1 ? 55 : 44;
            signal += .034 * Wave(pitch, time) * (.75 + .25 * Wave(.2, time));
            signal += .016 * Wave(pitch * 1.5, time) * (.7 + .3 * Wave(.3, time));
            if (kind == 0)
            {
                // The same quiet room resonance returns with diminishing, displaced echoes.
                signal += .045 * (Resonance(cycle, 1.1, 176) + .6 * Resonance(cycle, 3.5, 176) + .3 * Resonance(cycle, 6.1, 176));
            }
            else if (kind == 1)
            {
                signal += .034 * (Resonance(cycle, 1.3, 220) + .7 * Resonance(cycle, 5.6, 330));
                // A slow reversed envelope gives the Memory a sense of sound arriving before its source.
                double swell = Math.Pow(.5 - .5 * Math.Cos(Math.Tau * cycle / DurationSeconds), 2);
                signal += .024 * Wave(165, time) * swell;
            }
            else
            {
                signal += .019 * Wave(72.3, time) * (.7 + .3 * Wave(.1, time));
                signal += .038 * (Resonance(cycle, 2.0, 108) + .6 * Resonance(cycle, 6.0, 144));
            }
            raw[i] = Math.Tanh(signal * 1.5) * .72;
        }
        byte[] bytes = new byte[SampleCount * sizeof(short)];
        for (int i = 0; i < SampleCount; i++)
        {
            double sample = raw[i];
            if (i < overlap)
            {
                double blend = .5 - .5 * Math.Cos(Math.PI * i / overlap);
                sample = raw[SampleCount + i] * (1 - blend) + sample * blend;
            }
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2, 2), (short)Math.Round(Math.Clamp(sample, -.85, .85) * short.MaxValue));
        }
        return bytes;
    }

    private static double Wave(double frequency, double time) => Math.Sin(Math.Tau * frequency * time);
    private static double Resonance(double time, double start, double pitch)
    {
        double t = time - start;
        if (t is < 0 or > 3) return 0;
        return Math.Min(1, t / .12) * Math.Exp(-t * 1.8) *
            (Wave(pitch, t) + .28 * Wave(pitch * 2.01, t) + .12 * Wave(pitch * 3, t)) * (1 - Math.Max(0, t - 2.7) / .3);
    }
}
