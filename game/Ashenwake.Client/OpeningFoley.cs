using System.Buffers.Binary;
using System.Security.Cryptography;
using Godot;

namespace Ashenwake.Client;

public sealed record OpeningFoleyCue(string Id, string Category, double DurationSeconds, float SuggestedGainDb,
    bool IsWarning, string Description);
public sealed record OpeningFoleyAnalysis(int Frames, double DurationSeconds, double PeakAbsolute, double Rms,
    double MaxAdjacentDelta, short FirstSample, short LastSample, int FullScaleSamples, string Sha256);

/// <summary>Original opening-region, Verdant and Cinder foley. Pure deterministic synthesis is independent of gameplay state;
/// cached Godot streams are created/accessed on the scene thread. PCM16 mono, no loops or external assets.</summary>
public static class OpeningFoley
{
    public const int SampleRate = 22050;
    public const double PeakCeiling = .78;
    // Catalog-validated, append-only keys bound the native cache to 38 short mono buffers.
    private static readonly Dictionary<string, AudioStreamWav> Streams = new(StringComparer.Ordinal);
    public static IReadOnlyList<OpeningFoleyCue> Cues { get; } = Array.AsReadOnly<OpeningFoleyCue>(
    [
        new("step_dirt_1", "footstep", .30, -17, false, "Weighted heel, granular ash compression and cloth toe-off."),
        new("step_dirt_2", "footstep", .32, -17, false, "Loose grit dispersal with a softer leather heel."),
        new("step_dirt_3", "footstep", .29, -17, false, "Firm earth contact and a short dragging crunch."),
        new("step_stone_1", "footstep", .34, -18, false, "Hard sole contact, stone body resonance and a quiet armor buckle."),
        new("step_stone_2", "footstep", .31, -18, false, "Rounded heel on broken paving with a dry toe tap."),
        new("step_stone_3", "footstep", .36, -18, false, "Chipped-stone scuff with a lower heel and trailing grit."),
        new("impact_weapon", "impact", .52, -10, false, "Low physical weight, a sharp cutting transient and a short blade ring."),
        new("impact_armor", "impact", .68, -11, false, "Broad plate body, irregular metal partials and loose fastening rattle."),
        new("impact_spell", "impact", .82, -11, false, "Air-pressure crack, unstable glass resonances and a falling breath tail."),
        new("guard_tell", "enemy_tell", 1.05, -10, true, "Funeral guard: chain-weight scrape and a dry, low chest rasp."),
        new("archer_tell", "enemy_tell", .86, -11, true, "Memory archer: inhaled whisper, glass harmonics and a tense string release."),
        new("crypt_tell", "enemy_tell", 1.12, -10, true, "Crypt creature: bone clicks, guttering throat and a rough exhalation."),
        new("saint_tell", "enemy_tell", 1.30, -10, true, "Bell Saint: a ruined brass throat drawing breath behind moving chains."),
        new("bell_phase2", "phase_warning", 1.85, -9, true, "Two iron-cage blows descend into a low ritual resonance."),
        new("bell_phase3", "phase_warning", 1.95, -9, true, "Three fractured bells answer a released creature's coarse breath."),
        new("low_health", "status_warning", .72, -14, true, "A restrained double heartbeat with a dry upper warning texture."),
        // Append-only: existing cue indices seed their PCM and must remain stable.
        new("vine_tell", "enemy_tell", 1.02, -10, true, "Vine thrall: fibrous creaking rises into a hollow rooted groan."),
        new("swarm_tell", "enemy_tell", .83, -11, true, "Spore swarm: a gathering flutter punctuated by three brittle seed clicks."),
        new("carrier_tell", "enemy_tell", 1.10, -10, true, "Plague carrier: a wet swelling gurgle ends in a pressured exhalation."),
        new("antler_tell", "enemy_tell", 1.42, -10, true, "Antler: a low wooden horn call answered by a higher rasp."),
        new("rootheart_tell", "enemy_tell", 1.40, -10, true, "Rootheart: two heavy living-timber pulses draw a deep root creak."),
        new("rootheart_phase2", "phase_warning", 2.00, -9, true, "Rootheart: three accelerating trunk blows open into a rough canopy roar."),
        new("root_severed", "impact", .90, -12, false, "A feeding root splits with a fibrous snap, wet recoil and fading timber strain."),
        new("rootheart_fall", "phase_warning", 2.80, -9, true, "Rootheart: a descending trunk groan breaks into falling branches and settling leaves."),
        new("step_moss_1", "footstep", .34, -17, false, "A soft moss cushion, damp heel compression and a quiet leaf rub."),
        new("step_moss_2", "footstep", .36, -17, false, "A lower heel squelch beneath a short leafy toe-off."),
        new("step_moss_3", "footstep", .33, -17, false, "A firmer root beneath moss, with trailing damp foliage."),
        new("emberling_tell", "enemy_tell", .94, -11, true, "Emberling: a swelling coal whistle and crackling intake before detonation."),
        new("brute_tell", "enemy_tell", 1.15, -10, true, "Furnace brute: a dragging iron weight answers a deep abrasive exhalation."),
        new("sentinel_tell", "enemy_tell", 1.10, -10, true, "Forge sentinel: a ratcheting servo winds into a narrow metal warning tone."),
        new("furnace_tell", "enemy_tell", 1.44, -10, true, "Furnace Spindle: two locking gears open into a low pressure surge."),
        new("storm_tell", "enemy_tell", 1.22, -10, true, "Burning Rain: a rising ash hiss crosses a bright, unstable cinder whistle."),
        new("furnace_phase2", "phase_warning", 2.12, -9, true, "Two heavy safety locks release, followed by a rising turbine and vented pressure."),
        new("furnace_shutdown", "phase_warning", 3.10, -9, true, "A winding turbine falls through settling gear clanks into a final steam release."),
        new("furnace_exposed", "window_warning", 1.18, -10, true, "A clean steam release and two tuned steel clanks mark the open furnace window."),
        new("step_metal_1", "footstep", .39, -18, false, "A heavy heel on grating with a short hollow steel ring."),
        new("step_metal_2", "footstep", .42, -18, false, "A lower plate strike followed by a loose rivet and toe tap."),
        new("step_metal_3", "footstep", .38, -18, false, "A firm boot on narrow grating with a bright trailing fastening.")
    ]);
    public static IReadOnlyList<string> CueNames { get; } = Array.AsReadOnly(Cues.Select(c => c.Id).ToArray());
    public static int CachedStreamCount => Streams.Count;

    public static OpeningFoleyCue Describe(string cue) => Cues.FirstOrDefault(c => c.Id == cue)
        ?? throw new ArgumentException("Unknown opening foley cue: " + cue, nameof(cue));

    /// <summary>Use a presentation-only step counter. Negative counters are also deterministic.</summary>
    public static string FootstepCue(string surface, long cosmeticStepIndex)
    {
        if (surface is not ("dirt" or "stone" or "moss" or "metal")) throw new ArgumentException("Footstep surface must be dirt, stone, moss or metal.", nameof(surface));
        int variant = (int)((cosmeticStepIndex % 3 + 3) % 3) + 1;
        return "step_" + surface + "_" + variant;
    }

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
            Stereo = false,
            LoopMode = AudioStreamWav.LoopModeEnum.Disabled,
            Data = samples
        };
        Streams.Add(cue, stream);
        return stream;
    }

    public static byte[] CreateSamples(string cue)
    {
        var metadata = Describe(cue);
        int kind = 0;
        while (CueNames[kind] != cue) kind++; // Describe already validated this small immutable catalog.
        int frames = (int)Math.Ceiling(metadata.DurationSeconds * SampleRate);
        var bytes = new byte[frames * sizeof(short)];
        uint noiseState = unchecked(0xAF2C71B9u + (uint)kind * 0x68E31DA4u);
        double low = 0, middle = 0, smooth = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            double time = frame / (double)SampleRate;
            noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
            double noise = noiseState / (double)uint.MaxValue * 2 - 1;
            low += (noise - low) * .018;
            middle += (noise - middle) * .12;
            smooth += (noise - smooth) * .42;
            double grain = noise - smooth, breath = middle - low;
            double signal = kind switch
            {
                0 or 1 or 2 => DirtStep(time, kind, low, middle, grain),
                3 or 4 or 5 => StoneStep(time, kind - 3, low, middle, grain),
                6 => WeaponImpact(time, middle, grain),
                7 => ArmorImpact(time, middle, grain),
                8 => SpellImpact(time, low, breath, grain),
                9 => GuardTell(time, low, breath, grain),
                10 => ArcherTell(time, breath, grain),
                11 => CryptTell(time, low, breath, grain),
                12 => SaintTell(time, low, breath, grain),
                13 => PhaseTwo(time, low, breath, grain),
                14 => PhaseThree(time, low, breath, grain),
                15 => LowHealth(time, middle, grain),
                16 => VineTell(time, low, breath, grain),
                17 => SwarmTell(time, breath, grain),
                18 => CarrierTell(time, low, breath),
                19 => AntlerTell(time, low, breath),
                20 => RootheartTell(time, low, breath, grain),
                21 => RootheartPhaseTwo(time, low, breath, grain),
                22 => RootSevered(time, low, breath, grain),
                23 => RootheartFall(time, low, breath, grain),
                24 or 25 or 26 => MossStep(time, kind - 24, low, middle, grain),
                27 => EmberlingTell(time, breath, grain),
                28 => BruteTell(time, low, breath, grain),
                29 => SentinelTell(time, breath, grain),
                30 => FurnaceTell(time, low, breath, grain),
                31 => StormTell(time, breath, grain),
                32 => FurnacePhaseTwo(time, low, breath, grain),
                33 => FurnaceShutdown(time, low, breath, grain),
                34 => FurnaceExposed(time, breath, grain),
                35 or 36 or 37 => MetalStep(time, kind - 35, low, middle, grain),
                _ => throw new InvalidOperationException("Opening foley metadata and synthesis differ.")
            };
            // A two-millisecond entrance removes discontinuities; all tails taper to exact silence.
            // Headroom is fixed across the library, with metadata gains applied by the caller's mixer.
            double edge = Math.Min(1, frame / (SampleRate * .002)) * Math.Min(1, (frames - 1 - frame) / (SampleRate * .055));
            short pcm = (short)Math.Round(Math.Tanh(signal * 1.12) * PeakCeiling * edge * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(frame * sizeof(short), sizeof(short)), pcm);
        }
        return bytes;
    }

    public static OpeningFoleyAnalysis AnalyzeSamples(byte[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length < sizeof(short) * 2 || samples.Length % sizeof(short) != 0)
            throw new ArgumentException("Expected nonempty mono PCM16 samples.", nameof(samples));
        double peak = 0, squareSum = 0, adjacent = 0; int fullScale = 0;
        short first = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(0, sizeof(short))), prior = first;
        for (int index = 0; index < samples.Length; index += sizeof(short))
        {
            short sample = BinaryPrimitives.ReadInt16LittleEndian(samples.AsSpan(index, sizeof(short)));
            double normalized = sample / (double)short.MaxValue;
            peak = Math.Max(peak, Math.Abs(normalized)); squareSum += normalized * normalized;
            adjacent = Math.Max(adjacent, Math.Abs(sample - prior) / (double)short.MaxValue);
            if (sample is short.MaxValue or short.MinValue) fullScale++;
            prior = sample;
        }
        int frames = samples.Length / sizeof(short);
        return new(frames, frames / (double)SampleRate, peak, Math.Sqrt(squareSum / frames), adjacent,
            first, prior, fullScale, Convert.ToHexString(SHA256.HashData(samples)));
    }

    public static byte[] CreateWaveFile(string cue)
    {
        byte[] pcm = CreateSamples(cue), wave = new byte[44 + pcm.Length];
        "RIFF"u8.CopyTo(wave); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), wave.Length - 8);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), SampleRate); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), 16);
        "data"u8.CopyTo(wave.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), pcm.Length);
        pcm.CopyTo(wave, 44); return wave;
    }

    private static double DirtStep(double t, int variant, double low, double mid, double grain)
    {
        double pitch = 1 + (variant - 1) * .032, toe = .095 + variant * .011;
        return .34 * Envelope(t, .004, 24) * (Sweep(112 * pitch, 48 * pitch, t, .22) + .55 * low) +
            .70 * mid * Swell(t, .002, .20 + variant * .016) +
            .30 * grain * (Burst(t, .011, 90) + .6 * Burst(t, .047 + variant * .006, 62) + .3 * Burst(t, .136, 60)) +
            .12 * Modal(t - toe, 148 * pitch, 35, 1.87, 3.42) + .28 * low * Swell(t, toe, .28);
    }

    private static double StoneStep(double t, int variant, double low, double mid, double grain)
    {
        double pitch = 1 + (variant - 1) * .026, toe = .105 + variant * .012;
        return .32 * Sweep(126 * pitch, 58, t, .24) * Envelope(t, .002, 24) +
            .32 * Modal(t, 224 * pitch, 28, 2.73, 5.16) + .23 * grain * Burst(t, .003, 85) +
            .16 * Modal(t - toe, 372 * pitch, 32, 2.18, 4.51) +
            .11 * Modal(t - toe - .018, 717, 45, 1.41, 2.47) +
            (.32 * mid + .16 * low) * Swell(t, .038, .29 + variant * .012);
    }

    private static double WeaponImpact(double t, double mid, double grain)
        => .60 * Sweep(158, 47, t, .30) * Envelope(t, .0015, 17) +
            .44 * grain * Envelope(t, .001, 57) + .24 * mid * Swell(t, .009, .29) +
            .26 * Modal(t - .008, 657, 13, 1.79, 3.43) + .11 * Modal(t - .032, 117, 20, 2.57, 4.11);

    private static double ArmorImpact(double t, double mid, double grain)
        => .43 * Modal(t, 188, 10, 2.814, 6.633) + .24 * Modal(t - .004, 533, 17, 2.33, 4.11) +
            .22 * grain * Envelope(t, .001, 68) + .27 * mid * Swell(t, .018, .32) +
            .12 * Modal(t - .092, 841, 29, 1.43, 2.89) + .075 * Modal(t - .156, 692, 32, 2.19, 3.71);

    private static double SpellImpact(double t, double low, double breath, double grain)
        => .36 * Sweep(282, 56, t, .55) * Envelope(t, .008, 7) + .38 * grain * Envelope(t, .001, 41) +
            .29 * Modal(t - .018, 246, 5.7, 2.77, 4.43) +
            .18 * Math.Sin(Math.Tau * 683 * t + 1.9 * Wave(21, t)) * Envelope(t, .019, 7.5) +
            (.66 * breath + .55 * low) * Swell(t, .021, .76);

    private static double GuardTell(double t, double low, double breath, double grain)
        => .26 * Modal(t - .012, 327, 23, 2.17, 4.61) + .17 * Modal(t - .138, 483, 27, 1.89, 3.47) +
            (.83 * breath + .30 * grain) * Swell(t, .02, .41) +
            (.32 * Throat(t, 83, 5.7) + .62 * low + .32 * breath * (.6 + .4 * Wave(29, t))) * Swell(t, .22, 1.01);

    private static double ArcherTell(double t, double breath, double grain)
        => (.80 * breath + .17 * grain) * Swell(t, .005, .43) +
            .16 * Math.Sin(Math.Tau * 347 * t + .95 * Wave(7, t)) * Swell(t, .05, .67) +
            .20 * Modal(t - .30, 621, 7.5, 1.607, 2.417) + .19 * Modal(t - .325, 177, 20, 3.03, 5.11) +
            .18 * grain * Burst(t, .329, 86);

    private static double CryptTell(double t, double low, double breath, double grain)
        => .17 * Modal(t, 407, 28, 2.47, 4.19) + .12 * Modal(t - .13, 563, 33, 1.79, 3.53) +
            .10 * Modal(t - .237, 347, 29, 2.39, 5.07) +
            (.28 * Throat(t, 63, 7.1) + .65 * low + .59 * breath * (.6 + .4 * Wave(22, t))) * Swell(t, .025, 1.03) +
            .18 * grain * Swell(t, .42, .89);

    private static double SaintTell(double t, double low, double breath, double grain)
        => (.25 * Throat(t, 54, 3.9) + .45 * low + .64 * breath) * Swell(t, .02, 1.16) +
            .28 * Modal(t - .039, 148, 3.9, 2.14, 4.129) +
            .13 * Modal(t - .183, 719, 24, 1.53, 2.77) + .095 * Modal(t - .292, 531, 28, 1.89, 3.41) +
            .12 * grain * (Burst(t, .04, 80) + .6 * Burst(t, .185, 85));

    private static double PhaseTwo(double t, double low, double breath, double grain)
        => .50 * Modal(t, 157, 3.1, 2.756, 5.404) + .36 * Modal(t - .36, 110, 2.7, 2.756, 5.404) +
            .34 * grain * (Burst(t, .003, 70) + .8 * Burst(t, .363, 60)) +
            .19 * Modal(t - .115, 943, 25, 1.43, 2.77) + .15 * Modal(t - .224, 677, 29, 2.13, 3.37) +
            (.51 * low + .45 * breath + .14 * Throat(t, 63, 4.3)) * Swell(t, .32, 1.76);

    private static double PhaseThree(double t, double low, double breath, double grain)
        => .41 * Modal(t, 123, 2.5, 2.756, 5.404) + .30 * Modal(t - .215, 194, 3.1, 2.713, 5.39) +
            .28 * Modal(t - .435, 259, 3.7, 2.79, 5.417) +
            .21 * grain * (Burst(t, .003, 80) + Burst(t, .219, 80) + Burst(t, .437, 80)) +
            (.52 * low + .57 * breath * (.7 + .3 * Wave(19, t)) + .18 * Throat(t, 71, 5.1)) * Swell(t, .37, 1.78);

    private static double LowHealth(double t, double mid, double grain)
        => .33 * Heart(t) + .23 * Heart(t - .205) +
            .10 * mid * (Burst(t, .018, 35) + .6 * Burst(t, .221, 38)) + .025 * grain * Swell(t, .015, .55);

    private static double MossStep(double t, int variant, double low, double mid, double grain)
    {
        double pitch = 1 + (variant - 1) * .04, toe = .10 + variant * .014;
        return .33 * Sweep(91 * pitch, 42, t, .28) * Envelope(t, .008, 23) +
            (.79 * low + .28 * mid) * Swell(t, .008, .29) +
            .16 * grain * (Burst(t, .025, 54) + .4 * Burst(t, toe, 45)) +
            .15 * Modal(t - toe, 103 * pitch, 23, 2.71, 4.83);
    }

    private static double VineTell(double t, double low, double breath, double grain)
        => (.26 * Throat(t, 72, 2.3) + .62 * low + .43 * breath) * Swell(t, .012, .97) +
            .16 * Modal(t - .055, 187, 8, 2.71, 4.83) +
            .12 * grain * (Burst(t, .16, 32) + .7 * Burst(t, .39, 28)) +
            .11 * Math.Sin(Math.Tau * 233 * t + 2.4 * Wave(9, t)) * Swell(t, .18, .83);

    private static double SwarmTell(double t, double breath, double grain)
        => (.72 * breath + .10 * grain) * (.56 + .44 * Wave(37, t)) * Swell(t, .008, .79) +
            .26 * Modal(t - .019, 641, 37, 2.71, 4.83) +
            .21 * Modal(t - .165, 823, 40, 2.71, 4.83) +
            .20 * Modal(t - .277, 1057, 42, 2.71, 4.83) +
            .15 * Wave(182, t) * Swell(t, .12, .64);

    private static double CarrierTell(double t, double low, double breath)
        => (.24 * Throat(t, 93, 11.7) + .82 * low) * Swell(t, .015, .92) +
            .20 * Sweep(138, 271, t, .8) * Swell(t, .08, .67) +
            .16 * Modal(t - .123, 219, 22, 1.52, 2.43) + .14 * Modal(t - .283, 162, 18, 1.61, 2.79) +
            .92 * breath * Swell(t, .41, 1.04);

    private static double AntlerTell(double t, double low, double breath)
        => (.26 * Throat(t, 117, 3.7) + .28 * low + .19 * breath) * Swell(t, .018, .85) +
            (.22 * Throat(t, 175.5, 4.2) + .31 * breath) * Swell(t, .59, 1.36) +
            .18 * Modal(t - .03, 147, 5, 2.71, 4.83) + .13 * Modal(t - .67, 221, 6, 2.71, 4.83);

    private static double RootheartTell(double t, double low, double breath, double grain)
        => .42 * Modal(t, 58, 4.5, 2.71, 4.83) + .34 * Modal(t - .29, 67, 4.1, 2.71, 4.83) +
            (.23 * Throat(t, 43, 2.1) + .85 * low + .38 * breath) * Swell(t, .14, 1.33) +
            .16 * grain * (Burst(t, .027, 33) + .8 * Burst(t, .322, 29));

    private static double RootheartPhaseTwo(double t, double low, double breath, double grain)
        => .43 * Modal(t, 62, 3.4, 2.71, 4.83) + .38 * Modal(t - .41, 74, 3.7, 2.71, 4.83) +
            .35 * Modal(t - .68, 89, 4.0, 2.71, 4.83) +
            .22 * grain * (Burst(t, .016, 36) + Burst(t, .425, 38) + Burst(t, .696, 40)) +
            (.24 * Throat(t, 49, 3.1) + .73 * low + .84 * breath) * Swell(t, .36, 1.91);

    private static double RootSevered(double t, double low, double breath, double grain)
        => .38 * grain * Envelope(t, .002, 37) + .40 * Modal(t - .009, 172, 9, 2.71, 4.83) +
            .25 * Sweep(151, 48, t, .73) * Swell(t, .012, .78) +
            (.68 * low + .49 * breath) * Swell(t, .022, .57) +
            .12 * Modal(t - .148, 283, 20, 2.71, 4.83);

    private static double RootheartFall(double t, double low, double breath, double grain)
        => .28 * Sweep(89, 27, Math.Min(t, 2.5), 2.5) * Swell(t, .01, 2.55) +
            (.22 * Throat(t, 47, 2.5) + .73 * low) * Swell(t, .018, 1.73) +
            .43 * Modal(t - .46, 61, 2.7, 2.71, 4.83) + .31 * Modal(t - 1.09, 43, 3.2, 2.71, 4.83) +
            .19 * Modal(t - 1.37, 179, 14, 2.71, 4.83) + .14 * Modal(t - 1.61, 247, 17, 2.71, 4.83) +
            .24 * grain * (Burst(t, .48, 28) + .8 * Burst(t, 1.10, 24) + .4 * Burst(t, 1.62, 36)) +
            .73 * breath * Swell(t, .74, 2.73);

    // Separate append-only synthesis keeps all earlier region samples and noise seeds unchanged.
    private static double EmberlingTell(double t, double breath, double grain)
        => .24 * Sweep(417, 831, t, .94) * Swell(t, .012, .89) +
            (.92 * breath + .16 * grain) * Swell(t, .005, .90) +
            .19 * grain * (Burst(t, .07, 66) + Burst(t, .24, 58) + Burst(t, .39, 51)) +
            .10 * Modal(t - .10, 1019, 18, 1.997, 2.413);

    private static double BruteTell(double t, double low, double breath, double grain)
        => .32 * Modal(t - .012, 91, 4.7, 1.997, 4.167) +
            (.25 * Throat(t, 61, 4.3) + .81 * low + .53 * breath) * Swell(t, .025, 1.10) +
            .14 * Modal(t - .16, 257, 15, 2.413, 4.167) + .10 * Modal(t - .30, 331, 18, 1.997, 4.167) +
            .18 * grain * Swell(t, .016, .53);

    private static double SentinelTell(double t, double breath, double grain)
        => .29 * Modal(t, 263, 12, 1.997, 2.413) + .24 * Modal(t - .18, 331, 14, 1.997, 2.413) +
            .20 * Modal(t - .32, 394, 16, 1.997, 2.413) +
            .21 * Wave(525, t) * Swell(t, .28, 1.05) +
            (.47 * breath + .12 * grain) * (.65 + .35 * Wave(31, t)) * Swell(t, .008, .78);

    private static double FurnaceTell(double t, double low, double breath, double grain)
        => .40 * Modal(t, 73, 4.9, 1.997, 4.167) + .33 * Modal(t - .30, 98, 4.7, 1.997, 4.167) +
            (.24 * Wave(49, t) + .82 * low + .90 * breath) * Swell(t, .15, 1.37) +
            .19 * grain * (Burst(t, .017, 47) + .8 * Burst(t, .32, 43)) +
            .12 * Sweep(183, 247, t, 1.44) * Swell(t, .34, 1.28);

    private static double StormTell(double t, double breath, double grain)
        => (.99 * breath + .24 * grain) * (.76 + .24 * Wave(13, t)) * Swell(t, .015, 1.16) +
            .20 * Sweep(698, 1137, t, 1.22) * Swell(t, .16, 1.09) +
            .14 * Modal(t - .043, 879, 17, 1.997, 2.413) + .13 * Modal(t - .29, 659, 14, 1.997, 2.413);

    private static double FurnacePhaseTwo(double t, double low, double breath, double grain)
        => .46 * Modal(t, 82, 3.1, 1.997, 4.167) + .41 * Modal(t - .38, 123, 3.6, 1.997, 4.167) +
            .24 * grain * (Burst(t, .013, 49) + Burst(t, .40, 43)) +
            .23 * Sweep(98, 294, t, 2.12) * Swell(t, .24, 2.02) +
            (.66 * low + .95 * breath) * Swell(t, .43, 2.04) +
            .15 * Modal(t - .73, 392, 6, 1.997, 2.413);

    private static double FurnaceShutdown(double t, double low, double breath, double grain)
        => .27 * Sweep(196, 29, t, 3.10) * Swell(t, .006, 3.03) +
            (.63 * low + .19 * Wave(49, t)) * Swell(t, .02, 2.22) +
            .34 * Modal(t - .27, 147, 3.0, 1.997, 4.167) + .28 * Modal(t - .83, 110, 3.6, 1.997, 4.167) +
            .23 * Modal(t - 1.57, 73, 4.4, 1.997, 4.167) + .13 * Modal(t - 2.28, 196, 8, 1.997, 2.413) +
            .20 * grain * (Burst(t, .29, 46) + .8 * Burst(t, .85, 42) + .6 * Burst(t, 1.59, 38)) +
            .82 * breath * Swell(t, .79, 3.04);

    private static double FurnaceExposed(double t, double breath, double grain)
        => (.87 * breath + .13 * grain) * Swell(t, .007, .59) +
            .34 * Modal(t - .033, 523.25, 5.0, 1.997, 2.413) +
            .29 * Modal(t - .22, 783.99, 5.4, 1.997, 2.413);

    private static double MetalStep(double t, int variant, double low, double mid, double grain)
    {
        double pitch = 1 + (variant - 1) * .035, toe = .11 + variant * .012;
        return .26 * Sweep(121 * pitch, 51, t, .28) * Envelope(t, .002, 22) +
            .31 * Modal(t, 181 * pitch, 11, 1.997, 4.167) + .19 * grain * Burst(t, .003, 72) +
            .14 * Modal(t - toe, 362 * pitch, 16, 1.997, 2.413) +
            .085 * Modal(t - toe - .023, 719, 27, 1.997, 4.167) +
            (.22 * mid + .20 * low) * Swell(t, .019, .34);
    }

    private static double Heart(double t) => t < 0 ? 0 : Envelope(t, .008, 19) * (Wave(64, t) + .27 * Wave(137, t));
    private static double Throat(double t, double fundamental, double flutter)
        => Math.Sin(Math.Tau * fundamental * t + .6 * Wave(flutter, t)) +
            .35 * Math.Sin(Math.Tau * fundamental * 2.03 * t + .8 * Wave(flutter * 1.71, t)) + .16 * Wave(fundamental * 4.11, t);
    private static double Wave(double frequency, double time) => Math.Sin(Math.Tau * frequency * time);
    private static double Sweep(double from, double to, double time, double duration)
        => Math.Sin(Math.Tau * (from * time + .5 * (to - from) * time * time / duration));
    private static double Envelope(double time, double attack, double decay)
        => time < 0 ? 0 : Math.Min(1, time / attack) * Math.Exp(-time * decay);
    private static double Burst(double time, double start, double decay) => Envelope(time - start, .0015, decay);
    private static double Swell(double time, double start, double end)
    {
        double progress = (time - start) / (end - start);
        return progress is <= 0 or >= 1 ? 0 : Math.Pow(Math.Sin(progress * Math.PI), 1.35);
    }
    private static double Modal(double time, double fundamental, double decay, double partial2, double partial3)
    {
        if (time < 0) return 0;
        return Envelope(time, .0015, decay) * (.68 * Wave(fundamental, time) + .22 * Wave(fundamental * partial2, time)) +
            Envelope(time, .001, decay * 1.8) * .13 * Wave(fundamental * partial3, time);
    }
}
