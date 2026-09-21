using System.Buffers.Binary;
using Godot;

namespace Ashenwake.Client;

/// <summary>Quiet mountain air and stone resonance, synthesized independently of gameplay state.</summary>
public static class SpineAmbience
{
    public const int SampleRate = 22050;
    public const int DurationSeconds = 10;
    public const int SampleCount = SampleRate * DurationSeconds;
    private static readonly Dictionary<string, AudioStreamWav> Streams = [];
    public static IReadOnlyList<string> CueNames { get; } = Array.AsReadOnly<string>(["spine_wind", "spine_hall", "spine_warden", "spine_memory"]);
    public static int CachedStreamCount => Streams.Count;

    public static string CueForStyle(string style) => style switch
    {
        "spine_causeway" => "spine_wind",
        "spine_hall" or "spine_archive" => "spine_hall",
        "spine_warden" => "spine_warden",
        "spine_memory" => "spine_memory",
        _ => ""
    };

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
            "spine_wind" => 0,
            "spine_hall" => 1,
            "spine_warden" => 2,
            "spine_memory" => 3,
            _ => throw new ArgumentException($"Unknown Spine ambience cue: {cue}", nameof(cue))
        };
        const int overlap = SampleRate / 2;
        double[] raw = new double[SampleCount + overlap];
        uint noiseState = unchecked(0xD43B6179u + (uint)kind * 0x68E31DA4u);
        double wind = 0, dust = 0;
        for (int i = -SampleRate; i < raw.Length; i++)
        {
            noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
            double noise = noiseState / (double)uint.MaxValue * 2 - 1;
            wind += (noise - wind) * .009;
            dust += (noise - dust) * .08;
            if (i < 0) continue;
            double time = i / (double)SampleRate, cycle = time % DurationSeconds;
            double gust = .7 + .18 * Wave(.2, time) + .08 * Wave(.3, time);
            double signal = wind * gust * (kind == 0 ? 2.1 : 1.2) + dust * .025;
            signal += .035 * Wave(kind == 3 ? 65 : kind == 2 ? 39 : 49, time) * (.8 + .2 * Wave(.2, time));
            if (kind == 1)
                signal += .07 * (Stone(cycle, 1.4, 147) + .7 * Stone(cycle, 5.8, 196));
            else if (kind == 2)
            {
                signal += .025 * Wave(58.5, time) * (.7 + .3 * Wave(.4, time));
                signal += .06 * (Stone(cycle, 2.2, 98) + .8 * Stone(cycle, 6.3, 131));
            }
            else if (kind == 3)
            {
                // The remembered covenant has an intact, consonant resonance above the low drone.
                signal += .018 * (Wave(130, time) + .5 * Wave(195, time)) * (.7 + .3 * Wave(.1, time));
                signal += .05 * (Stone(cycle, 1.1, 260) + .6 * Stone(cycle, 5.9, 325));
            }
            else signal += .032 * Stone(cycle, 5.3, 123);
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
    private static double Stone(double time, double start, double pitch)
    {
        double t = time - start;
        if (t is < 0 or > 3) return 0;
        return Math.Min(1, t / .025) * Math.Exp(-t * 2.1) *
            (Wave(pitch, t) + .3 * Wave(pitch * 2.37, t) + .12 * Wave(pitch * 3.81, t)) * (1 - Math.Max(0, t - 2.7) / .3);
    }
}
