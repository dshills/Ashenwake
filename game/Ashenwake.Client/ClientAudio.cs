using System.Buffers.Binary;
using Godot;

namespace Ashenwake.Client;

/// <summary>Presentation-only mixing. Music includes the existing regional ambient beds.</summary>
public static class ClientAudio
{
    public const string MasterBus = "Master";
    public const string MusicBus = "Music";
    public const string EffectsBus = "Effects";
    public const string InterfaceBus = "UI";
    private static readonly string[] CategoryBuses = [MusicBus, EffectsBus, InterfaceBus];
    private static AudioStreamWav? _interfaceCue;
    internal const string QuietEffectName = "AshenwakeQuietMix";
    internal const string LimiterEffectName = "AshenwakePeakGuard";
    public static bool QuietModeEnabled { get; private set; }

    /// <summary>Safe to call for every scene: existing levels and mute states are preserved.</summary>
    public static void EnsureBuses()
    {
        foreach (string name in CategoryBuses)
        {
            int index = AudioServer.GetBusIndex(name);
            if (index < 0)
            {
                AudioServer.AddBus();
                index = AudioServer.BusCount - 1;
                AudioServer.SetBusName(index, name);
            }
            if (AudioServer.GetBusSend(index) == MasterBus) continue;
            // Godot 4.6.2's send setter does not lock the mixer. Change topology only
            // when necessary, while the audio driver's loop is locked. AddBus above
            // takes its own lock and must remain outside this section.
            AudioServer.Lock();
            try { AudioServer.SetBusSend(index, MasterBus); }
            finally { AudioServer.Unlock(); }
        }
        EnsureMasterEffects();
    }

    // These resources live on the audio server, not a scene. Re-entering the world
    // must neither stack compression nor reset the user's chosen levels.
    private static void EnsureMasterEffects()
    {
        int master = AudioServer.GetBusIndex(MasterBus);
        if (FindMasterEffect(QuietEffectName) < 0)
        {
            AudioServer.AddBusEffect(master, new AudioEffectCompressor
            {
                ResourceName = QuietEffectName,
                Threshold = -18,
                Ratio = 4,
                Gain = 0,
                AttackUs = 500,
                ReleaseMs = 180,
                Mix = 1
            }, 0);
            AudioServer.SetBusEffectEnabled(master, 0, QuietModeEnabled);
        }
        if (FindMasterEffect(LimiterEffectName) < 0)
            AudioServer.AddBusEffect(master, new AudioEffectHardLimiter
            { ResourceName = LimiterEffectName, PreGainDb = 0, CeilingDb = -1, Release = .1f });
    }

    internal static int FindMasterEffect(string name)
    {
        int master = AudioServer.GetBusIndex(MasterBus);
        for (int index = 0; index < AudioServer.GetBusEffectCount(master); index++)
            if (AudioServer.GetBusEffect(master, index).ResourceName == name) return index;
        return -1;
    }

    /// <summary>Reduces loud peaks without makeup gain or changes to the volume sliders.</summary>
    public static void ApplyQuietMode(bool enabled)
    {
        QuietModeEnabled = enabled;
        EnsureBuses();
        AudioServer.SetBusEffectEnabled(AudioServer.GetBusIndex(MasterBus), FindMasterEffect(QuietEffectName), enabled);
    }

    public static void ApplyVolumes(float master, float music, float effects, float ui)
    {
        EnsureBuses();
        SetVolume(MasterBus, master);
        SetVolume(MusicBus, music);
        SetVolume(EffectsBus, effects);
        SetVolume(InterfaceBus, ui);
    }

    /// <summary>Zero is a true mute; invalid persisted values fall back to full channel gain.</summary>
    public static void SetVolume(string bus, float value)
    {
        int index = BusIndex(bus);
        float level = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
        AudioServer.SetBusVolumeDb(index, level == 0 ? -80 : Mathf.LinearToDb(level));
        AudioServer.SetBusMute(index, level == 0);
    }

    public static float GetVolume(string bus)
    {
        int index = BusIndex(bus);
        return AudioServer.IsBusMute(index) ? 0 : Math.Clamp(Mathf.DbToLinear(AudioServer.GetBusVolumeDb(index)), 0, 1);
    }

    /// <summary>Call for an explicit UI action, never while refreshing or rebuilding controls.</summary>
    public static void PlayInterface(Node owner)
    {
        EnsureBuses();
        var player = owner.GetNodeOrNull<AudioStreamPlayer>("InterfaceAudio");
        if (player is null)
        {
            player = new AudioStreamPlayer
            {
                Name = "InterfaceAudio",
                Bus = InterfaceBus,
                VolumeDb = -18,
                ProcessMode = Node.ProcessModeEnum.Always,
                Stream = _interfaceCue ??= CreateInterfaceCue()
            };
            owner.AddChild(player);
        }
        if (DisplayServer.GetName() != "headless") player.Play();
    }

    private static int BusIndex(string bus)
    {
        if (bus is not (MasterBus or MusicBus or EffectsBus or InterfaceBus))
            throw new ArgumentException("Unknown client audio bus.", nameof(bus));
        EnsureBuses();
        return AudioServer.GetBusIndex(bus);
    }

    private static AudioStreamWav CreateInterfaceCue()
    {
        const int sampleRate = 22050;
        const int count = sampleRate / 10;
        byte[] bytes = new byte[count * sizeof(short)];
        for (int i = 0; i < count; i++)
        {
            double time = i / (double)sampleRate;
            double edge = Math.Min(1, time / .004) * Math.Min(1, (count - 1 - i) / (sampleRate * .02));
            double tone = Math.Sin(Math.Tau * 660 * time) + .25 * Math.Sin(Math.Tau * 990 * time);
            short pcm = (short)Math.Round(tone * .32 * Math.Exp(-time * 35) * edge * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * sizeof(short), sizeof(short)), pcm);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = sampleRate, Data = bytes };
    }
}
