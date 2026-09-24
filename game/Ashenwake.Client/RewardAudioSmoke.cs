using System.Buffers.Binary;
using System.Security.Cryptography;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Signal and bounded playback checks; shipping transaction checks live in Appearance and SecretChambers smokes.</summary>
public partial class RewardAudioSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<object> _pcm = [];
    private readonly List<string> _skipped = [];
    private string _output = "";
    private bool _writeReport;
    public override async void _Ready()
    {
        bool passed = false; string error = "";
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--reward-audio-smoke") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Reward audio smoke requires a fresh --output directory.");
            Directory.CreateDirectory(_output); Directory.CreateDirectory(Path.Combine(_output, "auditions")); _writeReport = true;
            Engine.MaxFps = 60;
            var fingerprints = new HashSet<string>();
            foreach (var cue in RewardAudio.Cues)
            {
                byte[] samples = RewardAudio.CreateSamples(cue.Id);
                var analysis = OpeningFoley.AnalyzeSamples(samples);
                Check(cue.Id + "_repeatable", samples.AsSpan().SequenceEqual(RewardAudio.CreateSamples(cue.Id)));
                Check(cue.Id + "_duration_headroom_and_endpoints", analysis.Frames == (int)Math.Ceiling(cue.Seconds * RewardAudio.SampleRate) &&
                    analysis.Rms > .0001 && analysis.PeakAbsolute <= RewardAudio.PeakCeiling + 1d / short.MaxValue &&
                    analysis.FirstSample == 0 && analysis.LastSample == 0 && analysis.FullScaleSamples == 0);
                var stream = RewardAudio.GetStream(cue.Id);
                Check(cue.Id + "_cached_pcm_stream", stream == RewardAudio.GetStream(cue.Id) && stream.Data.AsSpan().SequenceEqual(samples) &&
                    stream.MixRate == RewardAudio.SampleRate && !stream.Stereo && stream.LoopMode == AudioStreamWav.LoopModeEnum.Disabled);
                fingerprints.Add(Convert.ToHexString(SHA256.HashData(samples)));
                _pcm.Add(new { cue, analysis }); WriteWave(cue.Id, samples);
            }
            Check("all_twelve_cues_are_distinct_and_cache_is_bounded", RewardAudio.Cues.Count == 12 && fingerprints.Count == 12 && RewardAudio.CachedStreamCount == 12);
            // Shared starter silhouettes vary by discipline; named armor overrides that palette.
            var equipment = ProgressionContent.Parse(Godot.FileAccess.GetFileAsString("res://progression.json")).Capture();
            string MaterialCue(string id, string discipline, bool equipped = true) =>
                RewardAudio.EquipmentCue(new PermanentItem { DefinitionId = id }, equipment, equipped, discipline);
            Check("vanguard_starter_plate_and_mantle_sound_metal", MaterialCue("item.starter_chest", "Vanguard") == "gear_metal_on" &&
                MaterialCue("item.starter_shoulders", "Vanguard", false) == "gear_metal_off");
            Check("shared_starter_hoods_sound_cloth", new[] { "Veilwalker", "Arcanist", "Gravecaller", "Warden" }.All(kind =>
                MaterialCue("item.starter_head", kind) == "gear_cloth_on"));
            Check("starter_tunic_and_gloves_keep_leather", MaterialCue("item.starter_chest", "Veilwalker") == "gear_leather_on" &&
                MaterialCue("item.starter_gloves", "Vanguard", false) == "gear_leather_off");
            Check("named_crown_and_silk_gloves_override_discipline", MaterialCue("item.crown_unsworn", "Gravecaller") == "gear_metal_on" &&
                MaterialCue("item.widows_last_echo", "Vanguard", false) == "gear_cloth_off");
            var owner = new Node { Name = "PresentationOwner" }; AddChild(owner);
            var levels = new[] { ClientAudio.MasterBus, ClientAudio.InterfaceBus, ClientAudio.EffectsBus }.ToDictionary(b => b, ClientAudio.GetVolume);
            try
            {
                Check("quiet_empty_owner_does_not_allocate_players", RewardAudio.Count(owner) == 0 && owner.GetChildCount() == 0);
                RewardAudio.Play(owner, "secret_treasure"); RewardAudio.Play(owner, "drop_relic");
                var pool = owner.GetNode<RewardAudioPlayer>("RewardFeedbackAudio");
                int before = RewardAudio.Count(owner);
                Check("pickup_cannot_steal_two_more_important_reward_voices", !RewardAudio.Play(owner, "collect_equipment") && RewardAudio.Count(owner) == before);
                Check("voices_are_bounded_and_routed_to_effects", pool.Voices.Count == RewardAudioPlayer.Capacity && pool.Voices.All(v => v.Bus == ClientAudio.EffectsBus && v.MaxPolyphony == 1));
                RewardAudio.Stop(owner);
                Check("stop_removes_transients_and_receipts", pool.Voices.All(v => !v.Playing) && RewardAudio.LastCue(owner) == "");
                Check("explicit_gear_gestures_are_not_time_coalesced", RewardAudio.Play(owner, "gear_reject") && RewardAudio.Play(owner, "gear_reject"));
                Check("gear_routes_through_interface_bus", pool.Voices.All(v => v.Bus == ClientAudio.InterfaceBus));
                RewardAudio.Stop(owner); RewardAudio.Play(owner, "collect_currency");
                Check("rapid_repeated_collection_coalesces", !RewardAudio.Play(owner, "collect_currency"));
                RewardAudio.Stop(owner);
                ClientAudio.SetVolume(ClientAudio.EffectsBus, 0); ClientAudio.SetVolume(ClientAudio.InterfaceBus, .37f);
                RewardAudio.Play(owner, "secret_treasure"); RewardAudio.Play(owner, "gear_cloth_on");
                Check("playing_preserves_true_mute_and_independent_bus_gains", AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.EffectsBus)) &&
                    Math.Abs(ClientAudio.GetVolume(ClientAudio.InterfaceBus) - .37f) < .0001);
                RewardAudio.Stop(owner);
                if (DisplayServer.GetName() != "headless")
                {
                    ClientAudio.SetVolume(ClientAudio.EffectsBus, 1);
                    RewardAudio.Play(owner, "secret_treasure");
                    var voice = pool.Voices.Single(v => v.Playing);
                    await Frames(3); double start = voice.GetPlaybackPosition(); await Frames(10);
                    Check("native_reward_voice_playhead_advances", voice.Playing && voice.GetPlaybackPosition() > start);
                }
                else _skipped.Add("native reward playhead timing");
                RewardAudio.StopTree(this);
                Check("session_boundary_stops_all_nested_reward_voices", pool.Voices.All(v => !v.Playing) && RewardAudio.LastCue(owner) == "");
                var preview = new Node(); owner.AddChild(preview); RewardAudio.Play(preview, "gear_metal_on");
                RewardAudio.StopTree(owner);
                Check("nested_panel_transients_are_stopped_too", preview.GetNode<RewardAudioPlayer>("RewardFeedbackAudio").Voices.All(v => !v.Playing));
                Check("playback_does_not_grow_the_pcm_cache", RewardAudio.CachedStreamCount == 12);
                bool rejected = false; try { RewardAudio.Play(owner, "not-a-cue"); } catch (ArgumentException) { rejected = true; }
                Check("unknown_cues_rejected_without_pool_growth", rejected && owner.GetChildCount() == 2);
            }
            finally { foreach (var level in levels) ClientAudio.SetVolume(level.Key, level.Value); owner.QueueFree(); await Frames(2); }
            passed = true;
        }
        catch (Exception ex) { error = ex.Message; GD.PushError(ex.ToString()); }
        try
        {
            string json = JsonData.Write(new
            {
                kind = passed ? "RewardAudioClientSmokePassed" : "RewardAudioClientSmokeFailed",
                passed,
                checks = _checks,
                pcm = _pcm,
                skippedChecks = _skipped,
                error,
                scope = "Authored PCM and bounded per-owner native playback. Real equip/drag/load and secret/loot event checks are in AppearanceSmoke and SecretChambersSmoke. This is not a listening assessment."
            });
            if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "reward-audio-review.json"), json);
            GD.Print(json);
        }
        finally { GetTree().Quit(passed ? 0 : 1); }
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException(name); }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void WriteWave(string cue, byte[] samples)
    {
        byte[] wave = new byte[44 + samples.Length];
        "RIFF"u8.CopyTo(wave); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4), wave.Length - 8);
        "WAVEfmt "u8.CopyTo(wave.AsSpan(8)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20), 1); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24), RewardAudio.SampleRate); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), RewardAudio.SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(32), 2); BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34), 16);
        "data"u8.CopyTo(wave.AsSpan(36)); BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40), samples.Length); samples.CopyTo(wave, 44);
        System.IO.File.WriteAllBytes(Path.Combine(_output, "auditions", cue + ".wav"), wave);
    }
}
