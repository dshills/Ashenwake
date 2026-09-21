using System.Buffers.Binary;
using Godot;

namespace Ashenwake.Client;

/// <summary>Bounded, locally synthesized industrial ambience, prepared before the first playable frame.</summary>
public static class CinderAmbience
{
    public const int SampleRate = 22050;
    public const int DurationSeconds = 10;
    public const int SampleCount = SampleRate * DurationSeconds;
    private static readonly Dictionary<string, AudioStreamWav> Streams = [];
    public static IReadOnlyList<string> CueNames { get; } = Array.AsReadOnly<string>(["cinder_wind", "cinder_machinery", "cinder_furnace", "cinder_storm"]);
    public static int CachedStreamCount => Streams.Count;

    public static string CueForStyle(string style) => style switch
    {
        "cinder_fields" => "cinder_wind",
        "cinder_extraction" or "cinder_foundry" => "cinder_machinery",
        "cinder_furnace" => "cinder_furnace",
        "cinder_storm" => "cinder_storm",
        _ => ""
    };

    public static void Prewarm()
    {
        foreach (string cue in CueNames) GetStream(cue);
    }

    public static AudioStreamWav GetStream(string cue)
    {
        if (Streams.TryGetValue(cue, out var cached)) return cached;
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Data = CreateSamples(cue),
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
            "cinder_wind" => 0,
            "cinder_machinery" => 1,
            "cinder_furnace" => 2,
            "cinder_storm" => 3,
            _ => throw new ArgumentException($"Unknown Cinder ambience cue: {cue}", nameof(cue))
        };
        const int overlap = SampleRate / 2;
        double[] raw = new double[SampleCount + overlap];
        uint noiseState = unchecked(0x817F935Du + (uint)kind * 0x68E31DA4u);
        double wind = 0, hiss = 0;
        for (int i = -SampleRate; i < raw.Length; i++)
        {
            noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
            double noise = noiseState / (double)uint.MaxValue * 2 - 1;
            wind += (noise - wind) * .011;
            hiss += (noise - hiss) * .18;
            if (i < 0) continue;
            double time = i / (double)SampleRate, cycle = time % DurationSeconds;
            double gust = .7 + .18 * Wave(.2, time) + .1 * Wave(.3, time);
            double signal = wind * gust * (kind == 3 ? 2.6 : 1.7) + hiss * (kind == 3 ? .1 : .035);
            // Quiet pressure and metal resonances sit behind the existing attack/warning cues.
            signal += .022 * Wave(kind == 2 ? 55 : 44, time) * (1 + .2 * Wave(.5, time));
            if (kind == 1)
            {
                signal += .025 * Wave(82, time) * Wave(.6, time);
                signal += .055 * (Metal(cycle, 1.2, 173) + .7 * Metal(cycle, 4.5, 227) + .6 * Metal(cycle, 7.8, 173));
            }
            else if (kind == 2)
            {
                signal += .035 * Wave(73, time) * (.7 + .3 * Wave(.2, time));
                signal += .12 * hiss * Pulse(cycle, 2.1, 1.8) + .05 * Metal(cycle, 6.7, 109);
            }
            else if (kind == 3)
            {
                signal += .035 * (Metal(cycle, 2.3, 311) + .7 * Metal(cycle, 6.1, 263));
                signal += .03 * Wave(37, time) * Pulse(cycle, 3.3, 3.8);
            }
            else signal += .024 * Metal(cycle, 5.8, 137);
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
    private static double Pulse(double time, double start, double duration)
    {
        double p = (time - start) / duration;
        return p is <= 0 or >= 1 ? 0 : Math.Pow(Math.Sin(p * Math.PI), 2);
    }
    private static double Metal(double time, double start, double pitch)
    {
        double t = time - start;
        if (t is < 0 or > 2) return 0;
        return Math.Min(1, t / .018) * Math.Exp(-t * 3.8) *
            (Wave(pitch, t) + .4 * Wave(pitch * 2.71, t) + .18 * Wave(pitch * 4.13, t)) * (1 - Math.Max(0, t - 1.8) / .2);
    }
}
