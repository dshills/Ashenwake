using System.Buffers.Binary;
using Godot;

namespace Ashenwake.Client;

/// <summary>Quiet, locally synthesized forest beds. The three cached loops never use a gameplay RNG stream.</summary>
public static class VerdantAmbience
{
    public const int SampleRate = 22050;
    public const int DurationSeconds = 10;
    public const int SampleCount = SampleRate * DurationSeconds;
    private static readonly Dictionary<string, AudioStreamWav> Streams = [];
    public static IReadOnlyList<string> CueNames { get; } = Array.AsReadOnly<string>(["forest", "village", "heart"]);
    public static int CachedStreamCount => Streams.Count;

    /// <summary>Called during Sandbox construction, before its first playable frame.</summary>
    public static void Prewarm()
    {
        foreach (string cue in CueNames) GetStream(cue);
    }

    public static string CueForStyle(string style) => style switch
    {
        "verdant_ruins" or "verdant_hunt" => "forest",
        "verdant_village" => "village",
        "verdant_heart" => "heart",
        _ => ""
    };

    public static AudioStreamWav GetStream(string cue)
    {
        if (Streams.TryGetValue(cue, out var stream)) return stream;
        // CreateSamples rejects unknown names, so arbitrary room IDs cannot grow this cache.
        stream = new AudioStreamWav
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

    /// <summary>Returns signed little-endian mono PCM16. A wrapped noise crossfade removes a loop-boundary click.</summary>
    public static byte[] CreateSamples(string cue)
    {
        int kind = cue switch
        {
            "forest" => 0,
            "village" => 1,
            "heart" => 2,
            _ => throw new ArgumentException($"Unknown Verdant ambience cue: {cue}", nameof(cue))
        };
        const int overlap = SampleRate / 2;
        double[] raw = new double[SampleCount + overlap];
        uint noiseState = unchecked(0x713F249Bu + (uint)kind * 0x68E31DA4u);
        double breeze = 0, leaves = 0, water = 0;
        for (int i = -SampleRate; i < raw.Length; i++)
        {
            noiseState ^= noiseState << 13;
            noiseState ^= noiseState >> 17;
            noiseState ^= noiseState << 5;
            double noise = noiseState / (double)uint.MaxValue * 2 - 1;
            breeze += (noise - breeze) * .018;
            leaves += (noise - leaves) * .12;
            water += (noise - water) * .42;
            if (i < 0) continue; // Let the local filters settle before the loop begins.
            double time = i / (double)SampleRate;
            double cycle = time % DurationSeconds;
            double gust = .65 + .16 * Wave(.2, time) + .09 * Wave(.5, time);
            double signal = (kind == 2 ? 1.05 : 1.35) * breeze * gust +
                .24 * leaves * (.7 + .2 * Wave(.3, time));
            // Insects are sparse, low-level clusters rather than a constant high tone.
            double insect = Pulse(cycle, 1.2, .45) + .7 * Pulse(cycle, 6.6, .38);
            double clicks = Math.Pow(Math.Max(0, Wave(14, time)), 6);
            signal += (kind == 2 ? .012 : .028) * insect * clicks * Wave(2900 + kind * 180, time);
            // Warm timber/root creaks give the hamlet and heart different acoustic shapes.
            double creak = Pulse(cycle, kind == 1 ? 3.4 : 4.5, kind == 2 ? 1.5 : .85);
            signal += (kind == 0 ? .014 : .033) * creak *
                Math.Sin(Math.Tau * (kind == 2 ? 83 : 137) * time + .8 * Wave(2, time));
            // The village has a little runnel; the heart has isolated drips under its canopy.
            signal += (kind == 1 ? .2 : .07) * (water - leaves) * (.7 + .16 * Wave(.4, time));
            if (kind == 2)
                signal += .045 * (Drop(cycle, 2.3, 860) + .65 * Drop(cycle, 7.7, 1120));
            raw[i] = Math.Tanh(signal * 1.6) * .75;
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
            short pcm = (short)Math.Round(Math.Clamp(sample, -.85, .85) * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * sizeof(short), sizeof(short)), pcm);
        }
        return bytes;
    }

    private static double Wave(double frequency, double time) => Math.Sin(Math.Tau * frequency * time);
    private static double Pulse(double time, double start, double duration)
    {
        double progress = (time - start) / duration;
        return progress is <= 0 or >= 1 ? 0 : Math.Pow(Math.Sin(progress * Math.PI), 2);
    }
    private static double Drop(double time, double start, double pitch)
    {
        double local = time - start;
        return local is < 0 or > .45 ? 0 : Math.Min(1, local / .006) * Math.Exp(-local * 18) *
            Math.Sin(Math.Tau * (pitch * local - 190 * local * local));
    }
}
