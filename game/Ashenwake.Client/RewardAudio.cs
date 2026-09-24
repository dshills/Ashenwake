using System.Buffers.Binary;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

internal sealed record RewardCue(string Id, double Seconds, float GainDb, int Priority, string Bus);

/// <summary>Original, deterministic presentation foley. Never reads or advances a gameplay RNG.</summary>
internal static class RewardAudio
{
    internal const int SampleRate = 22050;
    internal const double PeakCeiling = .42;
    internal static IReadOnlyList<RewardCue> Cues { get; } = Array.AsReadOnly<RewardCue>([
        new("gear_metal_on", .36, -13, 1, ClientAudio.InterfaceBus),
        new("gear_metal_off", .30, -15, 1, ClientAudio.InterfaceBus),
        new("gear_leather_on", .28, -12, 1, ClientAudio.InterfaceBus),
        new("gear_leather_off", .31, -13, 1, ClientAudio.InterfaceBus),
        new("gear_cloth_on", .34, -11, 1, ClientAudio.InterfaceBus),
        new("gear_cloth_off", .38, -12, 1, ClientAudio.InterfaceBus),
        new("gear_reject", .19, -16, 1, ClientAudio.InterfaceBus),
        new("collect_equipment", .30, -16, 1, ClientAudio.EffectsBus),
        new("collect_currency", .38, -17, 1, ClientAudio.EffectsBus),
        new("drop_rare", .62, -15, 2, ClientAudio.EffectsBus),
        new("drop_relic", .88, -14, 3, ClientAudio.EffectsBus),
        new("secret_treasure", 1.45, -14, 4, ClientAudio.EffectsBus)
    ]);
    private static readonly Dictionary<string, AudioStreamWav> Streams = new(StringComparer.Ordinal);
    internal static int CachedStreamCount => Streams.Count;

    internal static RewardCue Metadata(string cue) => Cues.FirstOrDefault(c => c.Id == cue)
        ?? throw new ArgumentException("Unknown reward cue: " + cue, nameof(cue));

    internal static AudioStreamWav GetStream(string cue)
    {
        if (!Streams.TryGetValue(cue, out var stream))
            Streams.Add(cue, stream = new AudioStreamWav
            {
                ResourceName = "Reward_" + cue,
                MixRate = SampleRate,
                Format = AudioStreamWav.FormatEnum.Format16Bits,
                Stereo = false,
                Data = CreateSamples(cue)
            });
        return stream;
    }

    internal static bool Play(Node owner, string cue)
    {
        Metadata(cue);
        if (!GodotObject.IsInstanceValid(owner) || !owner.IsInsideTree() || owner.IsQueuedForDeletion()) return false;
        var player = owner.GetNodeOrNull<RewardAudioPlayer>("RewardFeedbackAudio");
        if (player is null) { player = new RewardAudioPlayer { Name = "RewardFeedbackAudio" }; owner.AddChild(player); }
        return player.PlayCue(cue);
    }
    internal static int Count(Node owner) => owner.GetNodeOrNull<RewardAudioPlayer>("RewardFeedbackAudio")?.CueCount ?? 0;
    internal static string LastCue(Node owner) => owner.GetNodeOrNull<RewardAudioPlayer>("RewardFeedbackAudio")?.LastCue ?? "";
    internal static void Stop(Node owner) => owner.GetNodeOrNull<RewardAudioPlayer>("RewardFeedbackAudio")?.StopCues();
    internal static void StopTree(Node owner)
    {
        foreach (var child in owner.GetChildren())
            if (child is RewardAudioPlayer player) player.StopCues();
            else StopTree(child);
    }

    internal static string EquipmentCue(PermanentItem item, ProgressionDefinition content, bool equipped, string discipline = "Vanguard")
    {
        var definition = content.Items.FirstOrDefault(d => d.Id == item.DefinitionId);
        string id = item.DefinitionId, kind = discipline.ToLowerInvariant();
        // Named silhouettes keep their own material. Shared starter armor follows
        // the discipline's visible wardrobe, including plate, tunics and hoods.
        string material = id is "item.ash_weave" or "item.serath_shroud" or "item.emberwake_mantle" or
            "item.mourning_choir" or "item.widows_last_echo" ? "cloth" :
            id is "item.march_plate" or "item.oath_plate" or "item.oathkeeper_reprisal" or "item.pyrebound_treads" or
                "item.furnaceheart_cinch" or "item.stolen_hour" or "item.tithebreakers_grasp" or "item.crown_unsworn" ? "metal" :
            id is "item.greatstaff" or "item.bone_staff" or "item.ash_bow" ? "leather" :
            definition?.Slots.FirstOrDefault() switch
            {
                EquipmentSlot.Head => kind == "vanguard" ? "metal" : "cloth",
                EquipmentSlot.Chest => kind switch { "vanguard" => "metal", "veilwalker" or "warden" => "leather", _ => "cloth" },
                EquipmentSlot.Shoulders => ArmorPalette.For(kind).Metallic ? "metal" : "cloth",
                EquipmentSlot.Legs => ArmorPalette.For(kind).Metallic ? "metal" : "leather",
                EquipmentSlot.Belt or EquipmentSlot.Gloves or EquipmentSlot.Boots => "leather",
                _ => "metal"
            };
        return "gear_" + material + (equipped ? "_on" : "_off");
    }

    internal static byte[] CreateSamples(string cue)
    {
        var spec = Metadata(cue);
        int index = Cues.ToList().FindIndex(c => c.Id == cue), frames = (int)Math.Ceiling(spec.Seconds * SampleRate);
        var pcm = new byte[frames * 2];
        uint seed = unchecked(0xA53C917Du + (uint)index * 0x68E31DA4u);
        double smooth = 0, warm = 0, peak = 0;
        var signal = new double[frames];
        bool removing = cue.EndsWith("_off", StringComparison.Ordinal);
        double strike = removing ? .055 : .09;
        for (int i = 0; i < frames; i++)
        {
            double t = i / (double)SampleRate;
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            double noise = seed / (double)uint.MaxValue * 2 - 1;
            smooth += (noise - smooth) * .055; warm += (noise - warm) * .26;
            double sound = cue switch
            {
                "gear_metal_on" or "gear_metal_off" => .19 * (noise - warm) * Hit(t, .014, 50) +
                    .34 * Bell(t, strike, removing ? 710 : 950, 22) + .20 * Bell(t, strike + .045, 1430, 30),
                "gear_leather_on" or "gear_leather_off" => 1.4 * smooth * Puff(t, .005, .20) +
                    .20 * Math.Sin(Math.Tau * (removing ? 180 : 250) * t) * Hit(t, strike, 37) + .14 * warm * Hit(t, strike + .028, 60),
                "gear_cloth_on" or "gear_cloth_off" => .80 * warm * Puff(t, .005, spec.Seconds * .83) +
                    .32 * smooth * Puff(t, spec.Seconds * .23, spec.Seconds * .55),
                "gear_reject" => .22 * Bell(t, 0, 245, 28) + .19 * Bell(t, .065, 185, 35),
                "collect_equipment" => .33 * Bell(t, .015, 660, 26) + .24 * Bell(t, .085, 990, 29) + .35 * smooth * Hit(t, 0, 35),
                "collect_currency" => .26 * Bell(t, 0, 1450, 29) + .22 * Bell(t, .065, 1820, 31) + .13 * Bell(t, .125, 2160, 32),
                "drop_rare" => .29 * Bell(t, .01, 523.25, 8) + .23 * Bell(t, .12, 783.99, 10),
                "drop_relic" => .26 * Bell(t, 0, 440, 6) + .21 * Bell(t, .13, 659.25, 7) + .19 * Bell(t, .26, 880, 8),
                "secret_treasure" => .23 * Bell(t, .06, 329.63, 3.4) + .20 * Bell(t, .23, 493.88, 4) +
                    .18 * Bell(t, .41, 659.25, 4.3) + .35 * smooth * Puff(t, 0, .4),
                _ => 0
            };
            double edge = Math.Min(1, i / (SampleRate * .004)) * Math.Min(1, (frames - 1 - i) / (SampleRate * .035));
            signal[i] = sound * edge; peak = Math.Max(peak, Math.Abs(signal[i]));
        }
        double scale = peak > PeakCeiling ? PeakCeiling / peak : 1;
        for (int i = 0; i < frames; i++)
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), (short)Math.Round(signal[i] * scale * short.MaxValue));
        return pcm;
    }
    private static double Hit(double t, double start, double decay) => t < start ? 0 : Math.Min(1, (t - start) / .003) * Math.Exp(-(t - start) * decay);
    private static double Puff(double t, double start, double length) => t < start || t >= start + length ? 0 : Math.Pow(Math.Sin(Math.PI * (t - start) / length), 1.5);
    private static double Bell(double t, double start, double pitch, double decay) => t < start ? 0 :
        Hit(t, start, decay) * (Math.Sin(Math.Tau * pitch * (t - start)) + .24 * Math.Sin(Math.Tau * pitch * 2.76 * (t - start)));
}
