using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Godot;

namespace Ashenwake.Client;

public sealed record OpeningScoreAnalysis(int SampleRate, int Channels, int Frames, int ByteCount,
    double DurationSeconds, double Peak, double Rms, double MaximumAdjacentJump, double MaximumLoopJump,
    double DcOffset, string PcmSha256);

/// <summary>Original eight-bar opening, Verdant and Cinder score. Pure managed rendering is safe on a background
/// worker; stream cache access belongs to the Godot thread. No gameplay RNG or clock is read.</summary>
public static class OpeningScore
{
    public const int SampleRate = 22050;
    public const int Channels = 2;
    public const int DurationSeconds = 24;
    public const int SampleCount = SampleRate * DurationSeconds;
    public const int BytesPerFrame = Channels * sizeof(short);
    public const int PcmByteCount = SampleCount * BytesPerFrame;
    public const int BeatsPerMinute = 80;
    public const int Bars = 8;
    public const double BeatSeconds = .75;
    public const double MaximumSummedStemPeak = .631;
    public const string Version = "opening-score.3";

    public static IReadOnlyList<string> StyleNames { get; } = Array.AsReadOnly<string>(["greyhaven", "road", "monastery", "crypt", "sanctum",
        "verdant_ruins", "verdant_village", "verdant_shrine", "verdant_hunt", "verdant_heart",
        "cinder_fields", "cinder_extraction", "cinder_foundry", "cinder_storm", "cinder_furnace"]);
    public static IReadOnlyList<string> StemNames { get; } = Array.AsReadOnly<string>(["exploration", "combat", "boss"]);

    // Forty-five native PCM buffers at most: 95,256,000 bytes. Pure renders retain no PCM cache.
    private static readonly Dictionary<(string Style, string Stem), AudioStreamWav> Streams = [];
    private const int TableSize = 4096;
    private static readonly float[] Sine = BuildSine();
    private static readonly ConcurrentDictionary<(Voice Voice, int Midi), float[]> Tables = new();
    private enum Voice { Bow, Choir, Pluck }

    private sealed record Region(int[][] Chords, int[] Melody, double[] Beats, bool Bells, double Air, double Room);
    private static readonly Region[] Regions =
    [
        // Greyhaven: a suspended D-minor home theme, answering itself in a lower register.
        new([[50,57,62,65], [46,53,57,62], [53,57,60,65], [48,55,62,64]],
            [62,65,64,57,62,60,57,62], [1,5,7,12,17,21,23,28], false, .006, .32),
        // Road: the same opening gesture carried by worn strings over a walking pulse.
        new([[50,57,62,65], [48,55,60,64], [46,53,58,62], [45,52,57,62]],
            [62,65,64,57,62,65,67,64,60,62], [1,3.5,7,10,13,15,19,23,26,29], false, .011, .23),
        // Monastery: long resonant calls leave space for the chamber's answer.
        new([[50,57,62,65], [43,50,58,62], [46,53,58,62], [45,52,57,62]],
            [62,69,65,64,62,58,57,62], [0,5,8,13,17,21,24,29], true, .006, .47),
        // Crypt: the melody becomes a sparse high memory above low, breathy voices.
        new([[38,50,57,62], [46,53,57,62], [43,50,55,58], [45,52,57,60]],
            [74,72,69,65,74,70,69], [2,7,11,16,21,25,29], true, .01, .53),
        // Sanctum: the flattened second darkens the return of the opening motif.
        new([[38,50,57,62], [39,51,58,63], [43,50,58,62], [45,52,57,62]],
            [50,57,62,63,60,57,62], [0,6,10,14,18,23,28], true, .005, .55),
        // Verdant ruins: an E-Dorian reed climbs through stone split by growing roots.
        new([[40,47,54,59], [43,50,57,62], [45,52,59,64], [40,47,55,59]],
            [64,66,67,71,69,66,64,59,62,64], [1,3,6,9,12,15,18,22,26,29], false, .018, .21),
        // Village: the same forest tonal center, with halting minor-second questions.
        new([[40,47,55,59], [41,48,55,60], [45,52,55,60], [40,47,54,59]],
            [64,65,59,62,60,59,65,64], [0,3.5,7,11,15,19,24,28], false, .013, .29),
        // Shrine: open fifths and a high answering reed leave the grove breathing room.
        new([[40,47,59,66], [38,45,57,64], [43,50,62,69], [40,47,59,64]],
            [71,74,76,69,71,66,64], [2,7,10,16,21,25,29], false, .012, .46),
        // Antler hunt: a rising three-note call, then a lower response across the trees.
        new([[40,47,55,59], [43,50,55,62], [45,52,59,64], [42,49,57,61]],
            [59,62,64,71,67,64,59,62,66,64,61,59], [0,2,4,7,10,12,16,18,20,23,26,29], false, .014, .28),
        // Rootheart: slow low-register cycles with a chromatic root threatening the cadence.
        new([[28,40,47,55], [29,41,48,56], [33,45,52,59], [28,40,47,54]],
            [52,55,59,56,53,52,47,52], [0,4,9,12,17,21,25,29], false, .009, .37),
        // Ash fields: a suspended C-minor signal drifts over distant rail resonances.
        new([[36,43,50,55], [32,39,46,51], [34,41,48,53], [36,43,51,55]],
            [67,62,63,55,58,62,55,60], [1,5,9,12,17,22,25,29], false, .028, .23),
        // Extraction: a repeating mechanical answer in minor thirds, with an uneven pickup.
        new([[36,43,48,51], [39,46,51,55], [34,41,48,53], [35,42,47,50]],
            [60,63,60,55,62,65,62,59], [0,2.5,6,10,16,18.5,22,27], false, .015, .33),
        // Sealed foundry: low open fifths make room for isolated, long metal strains.
        new([[24,36,43,50], [27,39,46,53], [25,37,44,51], [24,36,43,48]],
            [55,62,51,58,50,55], [2,7,12,18,23,28], false, .011, .49),
        // Burning rain: close intervals sweep across a higher, more restless signal.
        new([[36,43,49,55], [37,44,50,56], [34,41,48,54], [36,43,50,55]],
            [72,73,67,70,66,72,74,67,70,72], [0,3,6,9,12,16,19,22,26,29], false, .043, .25),
        // Furnace: a deep C pedal with a chromatic gear slipping against the upper voice.
        new([[24,36,43,48], [25,37,44,49], [29,41,48,53], [24,36,43,50]],
            [48,55,49,48,53,56,50,48], [0,4,8,13,16,20,25,29], false, .019, .39)
    ];

    public static bool IsPrepared(string style, string stem)
    {
        ValidateNames(style, stem); return Streams.ContainsKey((style, stem));
    }

    public static AudioStreamWav GetStream(string style, string stem)
    {
        ValidateNames(style, stem);
        return Streams.TryGetValue((style, stem), out var stream) ? stream : CachePrepared(style, stem, CreateSamples(style, stem));
    }

    /// <summary>Call on the Godot thread after CreateSamples finishes on a worker. Godot owns
    /// its copy of Data; the caller may release or reuse the supplied managed buffer.</summary>
    public static AudioStreamWav CachePrepared(string style, string stem, byte[] samples)
    {
        ValidateNames(style, stem); ValidateSamples(samples);
        if (Streams.TryGetValue((style, stem), out var cached)) return cached;
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = true,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward,
            LoopBegin = 0,
            LoopEnd = SampleCount,
            Data = samples
        };
        Streams.Add((style, stem), stream); return stream;
    }

    /// <summary>Pure managed, deterministic PCM16 rendering. The three stems are simultaneous
    /// layers, not replacement songs; their peak caps sum to .63 (plus PCM rounding) before mixer/bus attenuation.</summary>
    public static byte[] CreateSamples(string style, string stem)
    {
        var (regionIndex, layer) = ValidateNames(style, stem);
        var region = Regions[regionIndex];
        var left = new float[SampleCount]; var right = new float[SampleCount];
        uint seed = unchecked(0x6D2B79F5u ^ (uint)(regionIndex + 1) * 0x9E3779B9u ^ (uint)(layer + 1) * 0x85EBCA6Bu);
        if (regionIndex >= 10) Cinder(region, regionIndex - 10, layer, left, right, seed);
        else if (regionIndex >= 5) Verdant(region, regionIndex - 5, layer, left, right, seed);
        else if (layer == 0) Exploration(region, regionIndex, left, right, seed);
        else if (layer == 1) Combat(region, regionIndex, left, right, seed);
        else Boss(region, regionIndex, left, right, seed);
        Reverberate(left, right, region.Room * (layer == 1 ? .55 : 1));
        return Encode(left, right, layer switch { 0 => .25, 1 => .18, _ => .20 });
    }

    private static void Exploration(Region region, int regionIndex, float[] left, float[] right, uint seed)
    {
        for (int chord = 0; chord < region.Chords.Length; chord++)
        {
            var notes = region.Chords[chord];
            for (int voice = 0; voice < notes.Length; voice++)
            {
                double pan = (voice - 1.5) * .32;
                AddTone(left, right, notes[voice], chord * 6 - .45, 8.2,
                    voice % 2 == 0 ? Voice.Bow : Voice.Choir, .062, pan, seed + (uint)(chord * 7 + voice));
            }
        }
        for (int note = 0; note < region.Melody.Length; note++)
        {
            double at = region.Beats[note] * BeatSeconds;
            double pan = note % 2 == 0 ? -.25 : .3;
            if (region.Bells) AddBell(left, right, region.Melody[note], at, 6.2, regionIndex == 3 ? .044 : .063, pan, .48);
            else AddTone(left, right, region.Melody[note], at, 3.8, Voice.Pluck, .10, pan, seed + (uint)(200 + note));
        }
        AddAir(left, right, region.Air, seed);
    }

    private static void Combat(Region region, int regionIndex, float[] left, float[] right, uint seed)
    {
        for (int bar = 0; bar < Bars; bar++)
        {
            int root = region.Chords[bar / 2][0];
            if (root > 45) root -= 12;
            double start = bar * 4 * BeatSeconds;
            AddTone(left, right, root, start, 1.7, Voice.Pluck, .12, -.15, seed + (uint)bar);
            AddTone(left, right, root + (bar % 2 == 0 ? 7 : 12), start + 2 * BeatSeconds, 1.3, Voice.Pluck, .071, .18, seed + (uint)(30 + bar));
            AddDrum(left, right, start, .8, .09, -.08, false, seed + (uint)(60 + bar));
            AddDrum(left, right, start + 2 * BeatSeconds, .65, .061, .12, false, seed + (uint)(80 + bar));
            // Sparse brush accents and occasional late pickups avoid a four-square metronome.
            if (bar % 2 == 1 || regionIndex == 1)
                AddDrum(left, right, start + 3.5 * BeatSeconds, .33, .022, -.35, true, seed + (uint)(100 + bar));
            if (bar is 3 or 7)
                AddTone(left, right, root + 12, start + 3 * BeatSeconds, 1.2, Voice.Pluck, .048, .25, seed + (uint)(120 + bar));
        }
    }

    private static void Boss(Region region, int regionIndex, float[] left, float[] right, uint seed)
    {
        for (int chord = 0; chord < region.Chords.Length; chord++)
        {
            var notes = region.Chords[chord];
            AddTone(left, right, notes[2] + 12, chord * 6 - .6, 8.8, Voice.Choir, .071, -.38, seed + (uint)chord);
            AddTone(left, right, notes[3] + 12, chord * 6 + .1, 7.9, Voice.Bow, .051, .38, seed + (uint)(10 + chord));
            int low = notes[0] > 45 ? notes[0] - 12 : notes[0];
            AddBell(left, right, low, chord * 6, 7.2, .08, -.06, .34);
            AddDrum(left, right, chord * 6 + 3 * BeatSeconds, 1.65, .10, -.22, false, seed + (uint)(30 + chord), ritual: true);
            AddDrum(left, right, chord * 6 + 5.5 * BeatSeconds, 1.3, .064, .28, false, seed + (uint)(40 + chord), ritual: true);
            if (regionIndex == 4)
                AddTone(left, right, notes[2] + 11, chord * 6 + 4.8, 2.7, Voice.Choir, .018, .2, seed + (uint)(50 + chord));
        }
    }

    private static void Verdant(Region region, int character, int layer, float[] left, float[] right, uint seed)
    {
        // Separate orchestration keeps every Act I sample and its seeded texture unchanged.
        // Reeds, hollow timber and seed-rattle percussion share the established 32-beat grid.
        if (layer == 0)
        {
            for (int chord = 0; chord < region.Chords.Length; chord++)
            {
                var notes = region.Chords[chord];
                AddTone(left, right, notes[0], chord * 6 - .5, 8, Voice.Bow, .048, -.28, seed + (uint)chord);
                AddReed(left, right, notes[2], chord * 6 - .25, 7.5, .040, .34, breathy: true);
                AddReed(left, right, notes[3], chord * 6 + .15, 7, .023, -.36, breathy: true);
            }
            for (int note = 0; note < region.Melody.Length; note++)
            {
                double at = region.Beats[note] * BeatSeconds;
                double pan = note % 2 == 0 ? -.36 : .39;
                AddReed(left, right, region.Melody[note], at, character == 2 ? 3.9 : 2.5, .072, pan, breathy: false);
                // The shrine's delayed answer and the village's wooden knocks are intentionally sparse.
                if (character == 2 && note % 2 == 0)
                    AddWood(left, right, region.Melody[note] - 12, at + 1.5, 2.2, .065, -pan);
                else if (character is 1 or 4 && note % 3 == 0)
                    AddWood(left, right, region.Melody[note] - 24, at + .375, 1.5, .045, -pan);
            }
            AddAir(left, right, region.Air, seed);
            return;
        }
        if (layer == 1)
        {
            for (int bar = 0; bar < Bars; bar++)
            {
                int root = region.Chords[bar / 2][0];
                double at = bar * 4 * BeatSeconds;
                AddWood(left, right, root, at, 1.8, .18, -.18);
                AddWood(left, right, root + 7, at + 1.5 * BeatSeconds, 1.2, .105, .3);
                AddDrum(left, right, at + 2.5 * BeatSeconds, .38, .10, -.3, true, seed + (uint)(bar + 20));
                AddWood(left, right, root + 12, at + 3 * BeatSeconds, .8, .08, .15);
                if (character is 3 or 4 || bar % 2 == 1)
                    AddDrum(left, right, at + 3.5 * BeatSeconds, .24, .065, .4, true, seed + (uint)(bar + 50));
            }
            return;
        }
        for (int chord = 0; chord < region.Chords.Length; chord++)
        {
            var notes = region.Chords[chord]; double at = chord * 6;
            AddReed(left, right, notes[2] - (character == 4 ? 12 : 0), at - .3, 7.4, .073, -.32, breathy: true);
            AddReed(left, right, notes[3], at + .4, 6.8, .048, .32, breathy: true);
            AddWood(left, right, notes[0], at, 3.6, .21, -.08);
            AddWood(left, right, notes[0] + 7, at + 3 * BeatSeconds, 2.5, .13, .24);
            // Antler replies in fifths; Rootheart's low paired pulses suggest living timber.
            if (character == 3)
                AddReed(left, right, notes[2] + 7, at + 4 * BeatSeconds, 2.4, .082, -.38, breathy: false);
            else
                AddWood(left, right, notes[0] + (character == 4 ? 1 : 12), at + 4.5 * BeatSeconds, 2.2, .11, -.25);
        }
    }

    private static void Cinder(Region region, int character, int layer, float[] left, float[] right, uint seed)
    {
        // Append-only regions and a separate renderer preserve all opening/Verdant PCM.
        // Tuned steel, slow machinery and filtered ash share the same eight-bar grid.
        if (layer == 0)
        {
            for (int chord = 0; chord < region.Chords.Length; chord++)
            {
                var notes = region.Chords[chord]; double at = chord * 6;
                AddFurnaceDrone(left, right, notes[0], at - .5, 8, .062, -.25);
                AddFurnaceDrone(left, right, notes[2], at + .1, 7.4, .029, .34);
                if (character is 2 or 4)
                    AddSteel(left, right, notes[1], at + 2.5 * BeatSeconds, 4.4, .065, -.38, strain: true);
            }
            for (int note = 0; note < region.Melody.Length; note++)
            {
                double at = region.Beats[note] * BeatSeconds, pan = note % 2 == 0 ? -.33 : .36;
                AddSteel(left, right, region.Melody[note], at, character == 2 ? 4.8 : 3.1, .092, pan, strain: character == 2);
                if (character == 1)
                    AddSteel(left, right, region.Melody[note] - 12, at + .375, 1.1, .031, -pan, strain: false);
            }
            AddAir(left, right, region.Air, seed);
            return;
        }
        if (layer == 1)
        {
            for (int bar = 0; bar < Bars; bar++)
            {
                int root = region.Chords[bar / 2][0]; double at = bar * 4 * BeatSeconds;
                // Extraction's staggered piston, foundry's slow hammer and storm's rapid
                // ash accents give regions distinct rhythms without changing loop alignment.
                double answer = character == 1 ? 1.5 : character == 2 ? 2.5 : 2;
                AddSteel(left, right, root + 12, at, 1.6, .17, -.14, strain: false);
                AddDrum(left, right, at, .75, .075, .05, false, seed + (uint)bar);
                AddSteel(left, right, root + 19, at + answer * BeatSeconds, 1.1, .094, .28, strain: false);
                AddDrum(left, right, at + 3.5 * BeatSeconds, .34, .052, -.36, true, seed + (uint)(30 + bar));
                if (character is 1 or 3 or 4)
                    AddSteel(left, right, root + 24, at + 3 * BeatSeconds, .6, .043, -.25, strain: false);
                if (character == 3)
                    AddDrum(left, right, at + .5 * BeatSeconds, .45, .08, .4, true, seed + (uint)(60 + bar));
            }
            return;
        }
        for (int chord = 0; chord < region.Chords.Length; chord++)
        {
            var notes = region.Chords[chord]; double at = chord * 6;
            AddFurnaceDrone(left, right, notes[0], at - .4, 8.1, .14, -.18);
            AddSteel(left, right, notes[2], at, 5.5, .10, .25, strain: true);
            AddSteel(left, right, notes[0] + 12, at + 3 * BeatSeconds, 2.8, .15, -.24, strain: false);
            AddSteel(left, right, notes[0] + (character == 4 ? 13 : 19), at + 5.5 * BeatSeconds, 2, .095, .3, strain: false);
            if (character == 3)
                AddAir(left, right, .006, seed + (uint)chord);
        }
    }

    private static void AddSteel(float[] left, float[] right, int midi, double start, double duration,
        double level, double pan, bool strain)
    {
        double[] ratios = [1, 1.997, 2.413, 4.167], phases = new double[4], amplitudes = [.68, .18, .11, .045];
        double[] decays = [Math.Exp(-.85 / SampleRate), Math.Exp(-1.5 / SampleRate), Math.Exp(-2.7 / SampleRate), Math.Exp(-5.2 / SampleRate)];
        for (int p = 0; p < ratios.Length; p++) ratios[p] *= Frequency(midi) / SampleRate;
        int at = Wrap((int)Math.Round(start * SampleRate)), frames = (int)(duration * SampleRate);
        var (gainLeft, gainRight) = Pan(pan, level);
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate, sample = 0;
            double flex = strain ? 1 + .003 * Lookup(Sine, Fraction(t * 2.3)) : 1;
            for (int p = 0; p < ratios.Length; p++)
            {
                sample += Lookup(Sine, phases[p]) * amplitudes[p];
                phases[p] = Fraction(phases[p] + ratios[p] * flex);
                amplitudes[p] *= decays[p];
            }
            sample *= Smooth(t / (strain ? .25 : .008)) * Smooth((duration - t) / .5);
            left[at] += (float)(sample * gainLeft); right[at] += (float)(sample * gainRight);
            if (++at == SampleCount) at = 0;
        }
    }

    private static void AddFurnaceDrone(float[] left, float[] right, int midi, double start, double duration,
        double level, double pan)
    {
        double phase = 0, gear = .23, step = Frequency(midi) / SampleRate;
        int at = Wrap((int)Math.Round(start * SampleRate)), frames = (int)(duration * SampleRate);
        var (gainLeft, gainRight) = Pan(pan, level);
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double sample = .76 * Lookup(Sine, phase) + .14 * Lookup(Sine, Fraction(phase * 2)) +
                .055 * Lookup(Sine, Fraction(phase * 5)) + .045 * Lookup(Sine, gear);
            sample *= Smooth(t / .85) * Smooth((duration - t) / 1.5) * (.86 + .14 * Lookup(Sine, Fraction(t / 1.5)));
            left[at] += (float)(sample * gainLeft); right[at] += (float)(sample * gainRight);
            phase = Fraction(phase + step); gear = Fraction(gear + step * 1.014);
            if (++at == SampleCount) at = 0;
        }
    }

    private static void AddReed(float[] left, float[] right, int midi, double start, double duration,
        double level, double pan, bool breathy)
    {
        double phase = 0, airPhase = .27, vibrato = 0;
        double step = Frequency(midi) / SampleRate;
        int at = Wrap((int)Math.Round(start * SampleRate)), frames = (int)(duration * SampleRate);
        var (gainLeft, gainRight) = Pan(pan, level);
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate, motion = Lookup(Sine, vibrato);
            double sample = .73 * Lookup(Sine, phase) + .16 * Lookup(Sine, Fraction(phase * 3)) +
                .045 * Lookup(Sine, Fraction(phase * 5)) + .045 * Lookup(Sine, airPhase);
            sample *= Smooth(t / (breathy ? .7 : .12)) * Smooth((duration - t) / (breathy ? 1.4 : .65)) * (.94 + .06 * motion);
            left[at] += (float)(sample * gainLeft); right[at] += (float)(sample * gainRight);
            phase = Fraction(phase + step * (1 + motion * .0023));
            airPhase = Fraction(airPhase + step * 2.013); vibrato = Fraction(vibrato + 3.6 / SampleRate);
            if (++at == SampleCount) at = 0;
        }
    }

    private static void AddWood(float[] left, float[] right, int midi, double start, double duration, double level, double pan)
    {
        double[] ratios = [1, 2.71, 4.83], phases = new double[3], amplitudes = [1, .24, .09];
        double[] decays = [Math.Exp(-2.5 / SampleRate), Math.Exp(-5.5 / SampleRate), Math.Exp(-10.0 / SampleRate)];
        for (int partial = 0; partial < ratios.Length; partial++) ratios[partial] *= Frequency(midi) / SampleRate;
        int at = Wrap((int)Math.Round(start * SampleRate)), frames = (int)(duration * SampleRate);
        var (gainLeft, gainRight) = Pan(pan, level);
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate, sample = 0;
            for (int partial = 0; partial < ratios.Length; partial++)
            {
                sample += Lookup(Sine, phases[partial]) * amplitudes[partial];
                phases[partial] = Fraction(phases[partial] + ratios[partial]); amplitudes[partial] *= decays[partial];
            }
            sample *= Smooth(t / .007) * Smooth((duration - t) / .25);
            left[at] += (float)(sample * gainLeft); right[at] += (float)(sample * gainRight);
            if (++at == SampleCount) at = 0;
        }
    }

    private static void AddTone(float[] left, float[] right, int midi, double start, double duration,
        Voice voice, double level, double pan, uint seed)
    {
        var table = Tables.GetOrAdd((voice, midi), key => BuildTable(key.Voice, key.Midi));
        double frequency = Frequency(midi), step = frequency / SampleRate;
        double phase = (seed & 1023) / 1024.0, other = ((seed >> 10) & 1023) / 1024.0;
        double vibrato = ((seed >> 20) & 255) / 256.0, vibratoStep = (voice == Voice.Choir ? 4.8 : 4.15) / SampleRate;
        double decay = 1, decayStep = Math.Exp(-(voice == Voice.Pluck ? 1.7 : .055) / SampleRate);
        int frames = (int)(duration * SampleRate), at = Wrap((int)Math.Round(start * SampleRate));
        var (gainLeft, gainRight) = Pan(pan, level);
        double attack = voice == Voice.Pluck ? .013 : voice == Voice.Choir ? .95 : .68;
        double release = voice == Voice.Pluck ? .4 : 1.55;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            double envelope = Smooth(t / attack) * Smooth((duration - t) / release) * decay;
            double motion = Lookup(Sine, vibrato);
            double tone = .64 * Lookup(table, phase) + .36 * Lookup(table, other);
            if (voice == Voice.Pluck) tone = tone * (.3 + .7 * decay) + Lookup(Sine, phase) * .35 * (1 - decay);
            else tone *= .94 + .06 * motion;
            double sample = tone * envelope;
            left[at] += (float)(sample * gainLeft); right[at] += (float)(sample * gainRight);
            phase = Fraction(phase + step * (1 + motion * .0012));
            other = Fraction(other + step * (voice == Voice.Pluck ? 1.0011 : .9978));
            vibrato = Fraction(vibrato + vibratoStep); decay *= decayStep;
            if (++at == SampleCount) at = 0;
        }
    }

    private static void AddBell(float[] left, float[] right, int midi, double start, double duration, double level, double pan, double brightness)
    {
        double[] ratios = [1, 2.01, 2.756, 4.07, 5.404];
        double[] weights = [.66, .19, .20 * brightness, .11 * brightness, .08 * brightness];
        double[] phases = new double[ratios.Length], amplitudes = new double[ratios.Length], decay = new double[ratios.Length];
        double fundamental = Frequency(midi);
        for (int p = 0; p < ratios.Length; p++)
        { amplitudes[p] = weights[p]; decay[p] = Math.Exp(-(.52 + p * .32) / SampleRate); ratios[p] *= fundamental / SampleRate; }
        var (gainLeft, gainRight) = Pan(pan, level);
        int at = Wrap((int)Math.Round(start * SampleRate)), frames = (int)(duration * SampleRate);
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate, sample = 0;
            for (int p = 0; p < ratios.Length; p++)
            { sample += Lookup(Sine, phases[p]) * amplitudes[p]; phases[p] = Fraction(phases[p] + ratios[p]); amplitudes[p] *= decay[p]; }
            sample *= Smooth(t / .009) * Smooth((duration - t) / .8);
            left[at] += (float)(sample * gainLeft); right[at] += (float)(sample * gainRight);
            if (++at == SampleCount) at = 0;
        }
    }

    private static void AddDrum(float[] left, float[] right, double start, double duration, double level, double pan, bool brush, uint seed, bool ritual = false)
    {
        var (gainLeft, gainRight) = Pan(pan, level);
        double phase = 0, overtone = 0, filtered = 0, slower = 0, decay = 1;
        double decayStep = Math.Exp(-(brush ? 15 : ritual ? 3.9 : 8) / SampleRate);
        int at = Wrap((int)Math.Round(start * SampleRate)), frames = (int)(duration * SampleRate);
        double pitch = ritual ? 76 : 88, pitchFall = Math.Exp(-8.0 / SampleRate);
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate, noise = Noise(ref seed);
            filtered += (noise - filtered) * .15; slower += (noise - slower) * .026;
            double sample = brush ? (filtered - slower) * .8 :
                .74 * Lookup(Sine, phase) + .14 * Lookup(Sine, overtone) + .22 * slower;
            sample *= Smooth(t / (brush ? .013 : .006)) * Smooth((duration - t) / .15) * decay;
            left[at] += (float)(sample * gainLeft); right[at] += (float)(sample * gainRight);
            double step = (pitch + (ritual ? 39 : 48)) / SampleRate;
            phase = Fraction(phase + step); overtone = Fraction(overtone + step * 2.37); pitch *= pitchFall; decay *= decayStep;
            if (++at == SampleCount) at = 0;
        }
    }

    private static void AddAir(float[] left, float[] right, double level, uint seed)
    {
        var noise = new float[SampleCount];
        for (int i = 0; i < noise.Length; i++) noise[i] = (float)Noise(ref seed);
        double low = 0, high = 0;
        // First pass settles the periodic filter. The second pass starts with the exact
        // previous cycle's state, so the seam retains its normal neighbouring sample slope.
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < noise.Length; i++)
            {
                low += (noise[i] - low) * .003; high += (noise[i] - high) * .045;
                if (pass == 0) continue;
                double swell = .72 + .20 * Lookup(Sine, i / (double)SampleCount) + .08 * Lookup(Sine, Fraction(i * 3.0 / SampleCount));
                float sample = (float)((high - low) * level * swell);
                left[i] += sample; right[(i + 719) % SampleCount] += sample * .87f;
            }
    }

    private static void Reverberate(float[] left, float[] right, double wet)
    {
        var dryLeft = (float[])left.Clone(); var dryRight = (float[])right.Clone();
        int[] delays = [(int)(SampleRate * .113), (int)(SampleRate * .271), (int)(SampleRate * .433), (int)(SampleRate * .719), (int)(SampleRate * 1.127)];
        double[] gains = [.29, .24, .18, .12, .075];
        for (int tap = 0; tap < delays.Length; tap++)
        {
            int source = SampleCount - delays[tap]; float gain = (float)(wet * gains[tap]);
            for (int i = 0; i < SampleCount; i++)
            {
                // Alternating cross-channel taps widen the room without unbounded feedback.
                left[i] += (tap % 2 == 0 ? dryRight[source] : dryLeft[source]) * gain;
                right[i] += (tap % 2 == 0 ? dryLeft[source] : dryRight[source]) * gain;
                if (++source == SampleCount) source = 0;
            }
        }
    }

    private static byte[] Encode(float[] left, float[] right, double ceiling)
    {
        double leftMean = 0, rightMean = 0;
        for (int i = 0; i < SampleCount; i++) { leftMean += left[i]; rightMean += right[i]; }
        leftMean /= SampleCount; rightMean /= SampleCount;
        double peak = 0;
        for (int i = 0; i < SampleCount; i++) peak = Math.Max(peak, Math.Max(Math.Abs(left[i] - leftMean), Math.Abs(right[i] - rightMean)));
        if (!double.IsFinite(peak)) throw new InvalidDataException("Opening score synthesis produced a non-finite sample.");
        double gain = ceiling / Math.Max(.15, peak);
        var bytes = new byte[PcmByteCount];
        for (int i = 0; i < SampleCount; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * BytesPerFrame, 2), (short)Math.Round((left[i] - leftMean) * gain * short.MaxValue));
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * BytesPerFrame + 2, 2), (short)Math.Round((right[i] - rightMean) * gain * short.MaxValue));
        }
        return bytes;
    }

    public static OpeningScoreAnalysis AnalyzeSamples(byte[] samples)
    {
        ValidateSamples(samples);
        double peak = 0, sum = 0, square = 0, adjacent = 0, seam = 0;
        for (int channel = 0; channel < Channels; channel++)
        {
            double first = ReadSample(samples, channel * 2), previous = first;
            for (int frame = 0; frame < SampleCount; frame++)
            {
                double value = ReadSample(samples, frame * BytesPerFrame + channel * 2);
                peak = Math.Max(peak, Math.Abs(value)); sum += value; square += value * value;
                adjacent = Math.Max(adjacent, Math.Abs(value - previous)); previous = value;
            }
            seam = Math.Max(seam, Math.Abs(previous - first));
        }
        return new(SampleRate, Channels, SampleCount, samples.Length, DurationSeconds, peak,
            Math.Sqrt(square / (SampleCount * Channels)), adjacent, seam, sum / (SampleCount * Channels), Convert.ToHexString(SHA256.HashData(samples)));
    }

    public static byte[] CreateWaveFile(string style, string stem) => CreateWaveFile(CreateSamples(style, stem));

    public static byte[] CreateWaveFile(byte[] samples)
    {
        ValidateSamples(samples);
        var wave = new byte[samples.Length + 44];
        "RIFF"u8.CopyTo(wave); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), wave.Length - 8);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), Channels);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), SampleRate); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), SampleRate * BytesPerFrame);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), BytesPerFrame); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), 16);
        "data"u8.CopyTo(wave.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), samples.Length);
        samples.CopyTo(wave, 44); return wave;
    }

    private static (int Region, int Stem) ValidateNames(string style, string stem)
    {
        int region = style switch
        {
            "greyhaven" => 0,
            "road" => 1,
            "monastery" => 2,
            "crypt" => 3,
            "sanctum" => 4,
            "verdant_ruins" => 5,
            "verdant_village" => 6,
            "verdant_shrine" => 7,
            "verdant_hunt" => 8,
            "verdant_heart" => 9,
            "cinder_fields" => 10,
            "cinder_extraction" => 11,
            "cinder_foundry" => 12,
            "cinder_storm" => 13,
            "cinder_furnace" => 14,
            _ => -1
        };
        int layer = stem switch { "exploration" => 0, "combat" => 1, "boss" => 2, _ => -1 };
        if (region < 0) throw new ArgumentException("Unknown opening score region.", nameof(style));
        if (layer < 0) throw new ArgumentException("Unknown opening score stem.", nameof(stem));
        return (region, layer);
    }
    private static void ValidateSamples(byte[] samples)
    {
        if (samples is null || samples.Length != PcmByteCount) throw new ArgumentException("Opening score PCM must contain one aligned stereo loop.", nameof(samples));
    }
    private static double ReadSample(byte[] data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset, 2)) / (double)short.MaxValue;
    private static float[] BuildSine()
    {
        var table = new float[TableSize + 1];
        for (int i = 0; i <= TableSize; i++) table[i] = (float)Math.Sin(Math.Tau * i / TableSize);
        return table;
    }
    private static float[] BuildTable(Voice voice, int midi)
    {
        var table = new float[TableSize + 1]; double fundamental = Frequency(midi), total = 0;
        for (int harmonic = 1; harmonic <= 12 && harmonic * fundamental < SampleRate * .42; harmonic++)
        {
            double frequency = harmonic * fundamental;
            double formants = .28 + 1.7 * Math.Exp(-Math.Pow((frequency - 650) / 190, 2)) +
                .75 * Math.Exp(-Math.Pow((frequency - 1150) / 280, 2)) + .28 * Math.Exp(-Math.Pow((frequency - 2450) / 400, 2));
            double weight = voice switch
            {
                Voice.Bow => 1 / Math.Pow(harmonic, 1.8),
                Voice.Choir => formants / Math.Pow(harmonic, 1.25),
                _ => 1 / Math.Pow(harmonic, 2.5)
            };
            total += weight;
            for (int i = 0; i <= TableSize; i++) table[i] += (float)(weight * Math.Sin(Math.Tau * harmonic * i / TableSize));
        }
        for (int i = 0; i <= TableSize; i++) table[i] /= (float)total;
        table[TableSize] = table[0]; return table;
    }
    private static double Lookup(float[] table, double phase)
    {
        // Renderers maintain fractional phases; also accept an exact cycle boundary safely.
        if (phase >= 1 || phase < 0) phase = Fraction(phase);
        double index = phase * TableSize; int whole = (int)index;
        return table[whole] + (table[whole + 1] - table[whole]) * (index - whole);
    }
    private static double Fraction(double value) => value - Math.Floor(value);
    private static double Frequency(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);
    private static double Smooth(double value) { value = Math.Clamp(value, 0, 1); return value * value * (3 - 2 * value); }
    private static (double Left, double Right) Pan(double pan, double level)
        => (Math.Cos((pan + 1) * Math.PI / 4) * level, Math.Sin((pan + 1) * Math.PI / 4) * level);
    private static int Wrap(int sample) => (sample % SampleCount + SampleCount) % SampleCount;
    private static double Noise(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return state / (double)uint.MaxValue * 2 - 1;
    }
}
