using System.Buffers.Binary;

namespace Ashenwake.Client;

/// <summary>Small, deterministic presentation sounds: signed PCM16, mono, 22,050 Hz.</summary>
public static class CombatAudio
{
    private const int SampleRate = 22050;
    private enum Sound { Blade, Bow, Arcane, Bone, Spear, Enemy, Hit, Armor, Death, Dodge, Bell, Chain, Victory, Heal, Loot, Tell, Legendary, Godwrought, Pyre, Reprisal, Widow, Rotwake, Chorus, Cinder }
    public static IReadOnlyList<string> CueNames { get; } = Array.AsReadOnly<string>(
        ["blade", "bow", "arcane", "bone", "spear", "enemy", "hit", "armor", "death", "dodge", "bell", "chain", "victory", "heal", "loot", "tell", "loot_legendary", "loot_godwrought", "legendary_pyre", "legendary_oath", "legendary_widow", "legendary_rotwake", "legendary_chorus", "legendary_cinder"]);

    public static byte[] CreateSamples(string cue)
    {
        (Sound sound, double duration) = cue switch
        {
            "blade" => (Sound.Blade, .28),
            "bow" => (Sound.Bow, .26),
            "arcane" => (Sound.Arcane, .42),
            "bone" => (Sound.Bone, .38),
            "spear" => (Sound.Spear, .3),
            "enemy" => (Sound.Enemy, .32),
            "hit" => (Sound.Hit, .16),
            "armor" => (Sound.Armor, .26),
            "death" => (Sound.Death, .7),
            "dodge" => (Sound.Dodge, .24),
            "bell" => (Sound.Bell, 1.25),
            "chain" => (Sound.Chain, .65),
            "victory" => (Sound.Victory, 1.55),
            "heal" => (Sound.Heal, .75),
            "loot" => (Sound.Loot, .55),
            "tell" => (Sound.Tell, .22),
            "loot_legendary" => (Sound.Legendary, 1.25),
            "loot_godwrought" => (Sound.Godwrought, 1.85),
            "legendary_pyre" => (Sound.Pyre, .48),
            "legendary_oath" => (Sound.Reprisal, .58),
            "legendary_widow" => (Sound.Widow, .68),
            "legendary_rotwake" => (Sound.Rotwake, .48),
            "legendary_chorus" => (Sound.Chorus, .72),
            "legendary_cinder" => (Sound.Cinder, .50),
            _ => throw new ArgumentException($"Unknown combat audio cue: {cue}", nameof(cue))
        };
        int count = (int)Math.Ceiling(duration * SampleRate);
        byte[] bytes = new byte[count * sizeof(short)];
        // Local xorshift noise is repeatable across processes and never touches a Core RNG stream.
        uint noiseState = unchecked(0xB5297A4Du + (uint)sound * 0x68E31DA4u);
        double lowNoise = 0, warmNoise = 0;
        for (int i = 0; i < count; i++)
        {
            double t = i / (double)SampleRate;
            noiseState ^= noiseState << 13;
            noiseState ^= noiseState >> 17;
            noiseState ^= noiseState << 5;
            double noise = noiseState / (double)uint.MaxValue * 2 - 1;
            lowNoise += (noise - lowNoise) * .065;
            warmNoise += (noise - warmNoise) * .3;
            double highNoise = noise - warmNoise;
            double signal = sound switch
            {
                Sound.Blade => .75 * highNoise * Swell(t, .025, .18) +
                    Envelope(t, .003, 22) * (.22 * Sweep(1400, 470, t, duration) + .28 * Wave(2460, t)),
                Sound.Bow => Envelope(t, .002, 27) * (.55 * Wave(165, t) + .24 * Wave(495, t) + .1 * Wave(825, t)) +
                    .4 * highNoise * Swell(t, .018, .2),
                Sound.Arcane => Envelope(t, .025, 7) * (.4 * Sweep(270, 1030, t, duration) +
                    .22 * Math.Sin(Math.Tau * 540 * t + 1.6 * Wave(17, t)) + .33 * warmNoise),
                Sound.Bone => .65 * warmNoise * (Burst(t, 0, 70) + .7 * Burst(t, .055, 85) + .35 * Burst(t, .13, 65)) +
                    Envelope(t, .004, 14) * (.32 * Wave(310, t) + .19 * Wave(877, t)),
                Sound.Spear => .7 * highNoise * Swell(t, .01, .15) +
                    Envelope(t, .002, 20) * (.4 * Sweep(430, 95, t, duration) + .14 * Wave(1710, t)),
                Sound.Enemy => Envelope(t, .003, 13) * (.53 * Sweep(165, 48, t, duration) + .6 * lowNoise + .2 * warmNoise) +
                    .18 * highNoise * Swell(t, .025, .15),
                Sound.Hit => Envelope(t, .001, 31) * (.8 * warmNoise + .4 * Sweep(205, 65, t, duration)),
                Sound.Armor => Envelope(t, .001, 24) * (.42 * Wave(1120, t) + .24 * Wave(1909, t) + .17 * Wave(3040, t) + .6 * highNoise),
                Sound.Death => Envelope(t, .012, 6) * (.7 * lowNoise + .31 * Sweep(128, 39, t, duration) +
                    .22 * Sweep(291, 68, t, duration)) + .22 * warmNoise * Swell(t, .1, .58),
                Sound.Dodge => (.95 * warmNoise + .25 * highNoise) * Swell(t, 0, duration) +
                    .1 * Sweep(290, 95, t, duration) * Swell(t, .015, .19),
                Sound.Bell => Bell(t, 174.61, 3.1) + .18 * highNoise * Envelope(t, .001, 95),
                Sound.Chain => Chain(t, highNoise),
                Sound.Victory => .7 * Note(t, 0, 293.66, 2.7) + .55 * Note(t, .13, 349.23, 2.8) +
                    .48 * Note(t, .26, 440, 2.9) + .35 * Note(t, .41, 659.25, 3.5) +
                    .17 * lowNoise * Swell(t, .05, 1.2),
                Sound.Heal => .56 * Note(t, 0, 528, 5.5) + .4 * Note(t, .11, 660, 6) +
                    .34 * Note(t, .22, 792, 6.5) + .12 * warmNoise * Swell(t, .03, .63),
                Sound.Loot => .52 * Note(t, 0, 880, 10) + .4 * Note(t, .09, 1320, 11) +
                    .17 * Wave(2640, t) * Envelope(t, .001, 25),
                Sound.Tell => Envelope(t, .004, 12) * (.35 * Sweep(650, 910, t, duration) +
                    .2 * Wave(1520, t) + .18 * Wave(194, t) + .22 * highNoise),
                Sound.Legendary => .40 * Note(t, 0, 659.25, 4) + .35 * Note(t, .11, 987.77, 4) +
                    .30 * Note(t, .22, 1318.51, 4.5) + .12 * Bell(t, 329.63, 6),
                Sound.Godwrought => .48 * Bell(t, 130.81, 2.8) + .32 * Note(t, .12, 392, 2.6) +
                    .26 * Note(t, .30, 523.25, 2.8) + .20 * Note(t, .48, 783.99, 3.2) + .12 * lowNoise * Swell(t, .02, 1.2),
                Sound.Pyre => .68 * warmNoise * Swell(t, .005, .43) + .26 * Sweep(195, 63, t, duration) * Envelope(t, .004, 8) +
                    .20 * highNoise * (Burst(t, .04, 45) + Burst(t, .15, 40) + Burst(t, .27, 50)),
                Sound.Reprisal => .48 * Bell(t, 246.94, 8) + .24 * Bell(t, 493.88, 10) +
                    .34 * lowNoise * Envelope(t, .002, 16),
                Sound.Widow => .25 * Note(t, 0, 783.99, 7) + .24 * Note(t, .19, 587.33, 6) +
                    .17 * Note(t, .29, 1174.66, 7) + .30 * highNoise * Swell(t, .03, .5),
                Sound.Rotwake => .45 * warmNoise * Swell(t, .01, .44) +
                    .23 * Sweep(460, 110, t, duration) * Envelope(t, .018, 8),
                Sound.Chorus => .24 * Note(t, 0, 220, 6) + .22 * Note(t, .04, 261.63, 6) +
                    .20 * Note(t, .08, 329.63, 6) + .12 * lowNoise * Swell(t, .03, .6),
                Sound.Cinder => .35 * Bell(t, 392, 11) + .34 * warmNoise * Swell(t, .01, .40) +
                    .20 * Sweep(150, 380, t, duration) * Envelope(t, .005, 8),
                _ => 0
            };
            // Smooth saturation keeps layered transients below full scale; both ends taper to zero.
            double edge = Math.Min(1, i / (SampleRate * .0015)) * Math.Min(1, (count - 1 - i) / (SampleRate * .035));
            short pcm = (short)Math.Round(Math.Tanh(signal * 1.3) * .84 * edge * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * sizeof(short), sizeof(short)), pcm);
        }
        return bytes;
    }

    private static double Wave(double frequency, double time) => Math.Sin(Math.Tau * frequency * time);

    private static double Sweep(double from, double to, double time, double duration)
        => Math.Sin(Math.Tau * (from * time + .5 * (to - from) * time * time / duration));

    private static double Envelope(double time, double attack, double decay)
        => time < 0 ? 0 : Math.Min(1, time / attack) * Math.Exp(-time * decay);

    private static double Swell(double time, double start, double end)
    {
        double progress = (time - start) / (end - start);
        return progress is <= 0 or >= 1 ? 0 : Math.Pow(Math.Sin(progress * Math.PI), 1.4);
    }

    private static double Burst(double time, double start, double decay) => Envelope(time - start, .001, decay);

    private static double Bell(double time, double fundamental, double decay)
    {
        if (time < 0) return 0;
        return Envelope(time, .002, decay) * (.5 * Wave(fundamental, time) + .26 * Wave(fundamental * 2.756, time)) +
            Envelope(time, .001, decay * 2.1) * (.16 * Wave(fundamental * 5.404, time) + .1 * Wave(fundamental * 8.933, time));
    }

    private static double Chain(double time, double noise)
        => .55 * Bell(time, 731, 25) + .39 * Bell(time - .061, 943, 30) +
            .43 * Bell(time - .137, 677, 29) + .32 * Bell(time - .24, 1013, 33) +
            .22 * Bell(time - .37, 823, 35) + .17 * Bell(time - .48, 1187, 39) +
            .21 * noise * (Burst(time, 0, 55) + Burst(time, .137, 60) + .6 * Burst(time, .37, 70));

    private static double Note(double time, double start, double frequency, double decay)
    {
        double local = time - start;
        if (local < 0) return 0;
        return Envelope(local, .012, decay) * (Wave(frequency, local) + .23 * Wave(frequency * 2, local) + .09 * Wave(frequency * 3, local));
    }
}
