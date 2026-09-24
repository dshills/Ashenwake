using Godot;

namespace Ashenwake.Client;

/// <summary>One two-voice pool per live presentation owner; reward fanfare cannot be stolen by pickup spam.</summary>
internal sealed partial class RewardAudioPlayer : Node
{
    internal const int Capacity = 2;
    private readonly AudioStreamPlayer[] _voices = new AudioStreamPlayer[Capacity];
    private readonly double[] _until = new double[Capacity];
    private readonly int[] _priorities = new int[Capacity];
    private readonly Dictionary<string, double> _last = new(StringComparer.Ordinal);
    internal int CueCount { get; private set; }
    internal string LastCue { get; private set; } = "";
    internal IReadOnlyList<AudioStreamPlayer> Voices => _voices;
    public override void _Ready()
    {
        ClientAudio.EnsureBuses();
        ProcessMode = ProcessModeEnum.Always;
        for (int i = 0; i < Capacity; i++)
        {
            _voices[i] = new AudioStreamPlayer { Name = "RewardVoice" + i, MaxPolyphony = 1 };
            AddChild(_voices[i]);
        }
    }
    internal bool PlayCue(string cue)
    {
        var spec = RewardAudio.Metadata(cue); double now = Time.GetTicksUsec() / 1_000_000d;
        if (!cue.StartsWith("gear_", StringComparison.Ordinal) && _last.TryGetValue(cue, out double previous) && now - previous < .12) return false;
        int slot = Array.FindIndex(_until, end => end <= now);
        if (slot < 0)
        {
            slot = _priorities[0] <= _priorities[1] ? 0 : 1;
            if (_priorities[slot] > spec.Priority) return false;
        }
        var voice = _voices[slot]; voice.Stop(); voice.Bus = spec.Bus; voice.VolumeDb = spec.GainDb;
        voice.Stream = RewardAudio.GetStream(cue);
        if (DisplayServer.GetName() != "headless") voice.Play();
        _until[slot] = now + spec.Seconds; _priorities[slot] = spec.Priority; _last[cue] = now;
        LastCue = cue; CueCount++; return true;
    }
    internal void StopCues()
    {
        foreach (var voice in _voices) voice?.Stop();
        Array.Clear(_until); Array.Clear(_priorities); _last.Clear(); LastCue = "";
    }
}
