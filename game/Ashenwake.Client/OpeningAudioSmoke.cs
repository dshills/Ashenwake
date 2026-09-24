using System.Buffers.Binary;
using System.Security.Cryptography;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Opt-in audio checks and audition exports. The campaign route earns its state through public commands;
/// separate read-only view fixtures exercise mixer edges without changing any gameplay snapshot.</summary>
public partial class OpeningAudioSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<object> _pcm = [], _route = [];
    private readonly List<string> _auditions = [], _captures = [], _skipped = [];
    private readonly HashSet<string> _styles = [], _heardCues = [];
    private readonly HashSet<int> _bellPhases = [];
    private string _output = "", _combat = "";
    private bool _writeReport;
    private CampaignContent _campaign = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private CampaignRuntimeSession _session = null!;
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private int _commands;
    private string _finalHash = "", _replayHash = "";
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--opening-audio-smoke") || _output.Length == 0)
                throw new InvalidDataException("Opening audio smoke requires --opening-audio-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Opening audio smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); Directory.CreateDirectory(Path.Combine(_output, "auditions")); _writeReport = true;
            Engine.MaxFps = 60; GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _combat = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign")); _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combat, _adventure, _progression, _campaign);
            await CheckPcm();
            await CheckMixer();
            await EarnOpening();
            Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private async Task CheckPcm()
    {
        string coreHash = _session.StateHash;
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        foreach (string style in OpeningScore.StyleNames)
        {
            var stems = new List<byte[]>();
            foreach (string stem in OpeningScore.StemNames)
            {
                var pair = await Task.Run(() => (First: OpeningScore.CreateSamples(style, stem), Second: OpeningScore.CreateSamples(style, stem)));
                byte[] pcm = pair.First; var analysis = OpeningScore.AnalyzeSamples(pcm);
                string key = style + "_" + stem;
                Check(key + "_pcm_is_repeatable", pcm.AsSpan().SequenceEqual(pair.Second));
                Check(key + "_aligned_stereo_loop", pcm.Length == OpeningScore.PcmByteCount && analysis.Frames == 529200 &&
                    analysis.SampleRate == 22050 && analysis.Channels == 2 && analysis.DurationSeconds == 24);
                double ceiling = stem == "exploration" ? .25 : stem == "combat" ? .18 : .20;
                Check(key + "_audible_with_headroom", analysis.Rms > .0001 && analysis.Peak <= ceiling + 1d / short.MaxValue);
                Check(key + "_loop_seam_is_within_signal_slope", analysis.MaximumLoopJump <= Math.Max(.0015, analysis.MaximumAdjacentJump * 1.5) && Math.Abs(analysis.DcOffset) < .003);
                Check(key + "_fingerprint_matches_bytes", analysis.PcmSha256 == Convert.ToHexString(SHA256.HashData(pcm)));
                fingerprints.Add(analysis.PcmSha256); stems.Add(pcm); _pcm.Add(new { kind = "score-stem", style, stem, analysis });
            }
            var full = Mix(stems, [1, 1, 1]); var fullAnalysis = OpeningScore.AnalyzeSamples(full);
            Check(style + "_all_stems_add_without_clipping", fullAnalysis.Peak <= OpeningScore.MaximumSummedStemPeak);
            _pcm.Add(new { kind = "maximum-score-mix", style, analysis = fullAnalysis });
            foreach (string mode in new[] { "exploration", "combat", "boss" })
            {
                double[] gains = mode == "exploration" ? [1, 0, 0] : mode == "combat" ? [1, .78, 0] : [1, .78, .85];
                // Auditions retain authored relative stem levels, before the runtime's per-player music gain.
                WriteWave(style + "-" + mode + ".wav", OpeningScore.CreateWaveFile(Mix(stems, gains)), 2, OpeningScore.SampleRate);
            }
            await Frames(1);
        }
        Check("all_score_stems_are_distinct", fingerprints.Count == OpeningScore.StyleNames.Count * OpeningScore.StemNames.Count);
        fingerprints.Clear();
        foreach (var cue in OpeningFoley.Cues)
        {
            byte[] pcm = OpeningFoley.CreateSamples(cue.Id); var analysis = OpeningFoley.AnalyzeSamples(pcm);
            Check(cue.Id + "_pcm_is_repeatable", pcm.AsSpan().SequenceEqual(OpeningFoley.CreateSamples(cue.Id)));
            Check(cue.Id + "_correct_duration_and_headroom", analysis.Frames == (int)Math.Ceiling(cue.DurationSeconds * OpeningFoley.SampleRate) &&
                analysis.Rms > .0001 && analysis.PeakAbsolute <= OpeningFoley.PeakCeiling + 1d / short.MaxValue && analysis.FullScaleSamples == 0);
            Check(cue.Id + "_silent_endpoints", analysis.FirstSample == 0 && analysis.LastSample == 0);
            var streamTimer = System.Diagnostics.Stopwatch.StartNew();
            var stream = OpeningFoley.GetStream(cue.Id);
            double streamCreationMilliseconds = streamTimer.Elapsed.TotalMilliseconds;
            Check(cue.Id + "_stream_matches_pcm", !stream.Stereo && stream.MixRate == OpeningFoley.SampleRate &&
                stream.Format == AudioStreamWav.FormatEnum.Format16Bits && stream.LoopMode == AudioStreamWav.LoopModeEnum.Disabled && stream.Data.AsSpan().SequenceEqual(pcm) &&
                ReferenceEquals(stream, OpeningFoley.GetStream(cue.Id)));
            fingerprints.Add(analysis.Sha256); _pcm.Add(new { kind = "foley", cue, analysis, streamCreationMilliseconds });
            WriteWave(cue.Id + ".wav", OpeningFoley.CreateWaveFile(cue.Id), 1, OpeningFoley.SampleRate);
        }
        Check("all_foley_cues_are_distinct_and_cache_is_bounded", fingerprints.Count == OpeningFoley.Cues.Count && OpeningFoley.CachedStreamCount == OpeningFoley.Cues.Count);
        foreach (string surface in new[] { "dirt", "stone" })
            Check(surface + "_steps_cycle_three_variants", Enumerable.Range(0, 3).Select(i => OpeningFoley.FootstepCue(surface, i)).Distinct().Count() == 3 &&
                OpeningFoley.FootstepCue(surface, 0) == OpeningFoley.FootstepCue(surface, 3) && OpeningFoley.FootstepCue(surface, -1) == OpeningFoley.FootstepCue(surface, 2));
        Check("pcm_rendering_never_mutates_core", _session.StateHash == coreHash);
    }

    private static byte[] Mix(IReadOnlyList<byte[]> stems, double[] gains)
    {
        var mixed = new byte[stems[0].Length];
        for (int i = 0; i < mixed.Length; i += 2)
        {
            double sample = 0;
            for (int layer = 0; layer < stems.Count; layer++) sample += BinaryPrimitives.ReadInt16LittleEndian(stems[layer].AsSpan(i, 2)) * gains[layer];
            if (Math.Abs(sample) >= short.MaxValue) throw new InvalidDataException("Authored score stems clipped when summed.");
            BinaryPrimitives.WriteInt16LittleEndian(mixed.AsSpan(i, 2), (short)Math.Round(sample));
        }
        return mixed;
    }

    private void WriteWave(string name, byte[] wave, short channels, int sampleRate)
    {
        Check(name + "_riff_header", wave.AsSpan(0, 4).SequenceEqual("RIFF"u8) && wave.AsSpan(8, 8).SequenceEqual("WAVEfmt "u8) &&
            wave.AsSpan(36, 4).SequenceEqual("data"u8) && BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(4, 4)) == wave.Length - 8 &&
            BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(20, 2)) == 1 && BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(22, 2)) == channels &&
            BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(24, 4)) == sampleRate && BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(34, 2)) == 16 &&
            BinaryPrimitives.ReadInt32LittleEndian(wave.AsSpan(40, 4)) == wave.Length - 44);
        System.IO.File.WriteAllBytes(Path.Combine(_output, "auditions", name), wave); _auditions.Add("auditions/" + name);
    }

    private async Task CheckMixer()
    {
        var audio = new OpeningAudio(); AddChild(audio);
        var levels = new[] { ClientAudio.MasterBus, ClientAudio.MusicBus, ClientAudio.EffectsBus, ClientAudio.InterfaceBus }
            .ToDictionary(bus => bus, ClientAudio.GetVolume);
        try
        {
            string hash = _session.StateHash; var hub = _session.Combat.View;
            var coldPreparation = System.Diagnostics.Stopwatch.StartNew();
            audio.SetStyle("greyhaven"); audio.Reset(hub);
            audio.Advance(1d / 60, hub, true, Basis.Identity);
            Check("cold_score_prepares_while_paused_without_starting", audio.PreparationPending && audio.BankStarts == 0 && audio.Paused);
            for (int i = 0; i < 1800 && audio.PreparationPending; i++) { audio.Advance(1d / 60, hub, true, Basis.Identity); await Frames(1); }
            Check("prepared_score_waits_for_resume", !audio.PreparationPending && audio.BankStarts == 0);
            _route.Add(new { kind = "cold-score-preparation", milliseconds = coldPreparation.Elapsed.TotalMilliseconds, paused = audio.Paused, bankStarts = audio.BankStarts });
            await SettleStyle(audio, hub, "greyhaven"); Pump(audio, hub, 120);
            Check("bounded_category_routed_players", audio.MusicVoiceCount == 6 && audio.EffectPlayers.Count + audio.WarningPlayers.Count == OpeningAudio.VoiceCapacity &&
                audio.MusicPlayers.All(p => p.Bus == ClientAudio.MusicBus) && audio.EffectPlayers.All(p => p.Bus == ClientAudio.EffectsBus) && audio.WarningPlayers.All(p => p.Bus == ClientAudio.EffectsBus));
            Check("hub_uses_exploration_layer", audio.Mode == "exploration" && audio.CombatGain == 0 && audio.BossGain == 0 && audio.ActiveMusicBanks == 1);
            await CheckNativePlayback(audio, hub);
            await CheckNativePositionalPlayback(audio, hub);
            var ids = audio.MusicPlayers.Select(p => p.Stream?.GetInstanceId() ?? 0).ToArray(); int starts = audio.BankStarts;
            audio.SetStyle("greyhaven"); audio.Reset(hub); Pump(audio, hub, 30);
            Check("same_style_reset_keeps_aligned_score_bank", audio.BankStarts == starts && ids.SequenceEqual(audio.MusicPlayers.Select(p => p.Stream?.GetInstanceId() ?? 0)));
            foreach (string stem in OpeningScore.StemNames)
            {
                var stream = OpeningScore.GetStream("greyhaven", stem);
                Check("cached_" + stem + "_loop_contract", stream.Stereo && stream.LoopMode == AudioStreamWav.LoopModeEnum.Forward && stream.LoopBegin == 0 && stream.LoopEnd == OpeningScore.SampleCount &&
                    stream.MixRate == OpeningScore.SampleRate && stream.Data.Length == OpeningScore.PcmByteCount && ReferenceEquals(stream, OpeningScore.GetStream("greyhaven", stem)));
            }
            audio.SetStyle("road"); await SettleStyle(audio, hub, "road", settleFade: false);
            Check("room_change_crossfades_two_bounded_banks", audio.ActiveMusicBanks == 2 && audio.BankStarts == starts + 1);
            Pump(audio, hub, 130);
            Check("outgoing_score_bank_is_released", audio.ActiveMusicBanks == 1 && audio.PlayingStyle == "road");
            var combat = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CreateEncounter("campaign.road").View;
            Pump(audio, combat, 80);
            Check("nearby_living_enemies_raise_combat_layer", audio.Mode == "combat" && audio.CombatGain > .99f && audio.BossGain == 0);
            Pump(audio, hub, 450);
            Check("combat_release_returns_to_exploration", audio.Mode == "exploration" && audio.CombatGain < .01f);
            CheckWarnings(audio, hub);
            var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount);
            audio.Advance(1d / 30, hub, true, Basis.Identity);
            for (int i = 0; i < 60; i++) { audio.Advance(.1, combat, true, Basis.Identity); audio.Play("impact_weapon", Vector3.Zero); }
            _route.Add(new
            {
                kind = "paused-voice-state",
                music = audio.MusicPlayers.Select(p => new { name = p.Name.ToString(), playing = p.Playing, playback = p.HasStreamPlayback(), paused = p.StreamPaused }).ToArray(),
                effects = audio.EffectPlayers.Select(p => new { name = p.Name.ToString(), playing = p.Playing, playback = p.HasStreamPlayback(), paused = p.StreamPaused }).ToArray(),
                warnings = audio.WarningPlayers.Select(p => new { name = p.Name.ToString(), playing = p.Playing, playback = p.HasStreamPlayback(), paused = p.StreamPaused }).ToArray()
            });
            // Playing becomes false while paused; HasStreamPlayback identifies the
            // retained clock. Inactive pool slots have no clock to pause. Pending 3D
            // Play requests are canceled by the director so they cannot start later.
            Check("pause_freezes_envelopes_and_transients", frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount) &&
                audio.MusicPlayers.Count(p => p.HasStreamPlayback()) == 3 && audio.MusicPlayers.Where(p => p.HasStreamPlayback()).All(p => p.StreamPaused) &&
                audio.EffectPlayers.Where(p => p.HasStreamPlayback()).All(p => p.StreamPaused) && audio.WarningPlayers.Where(p => p.HasStreamPlayback()).All(p => p.StreamPaused));
            Check("pause_cancels_pending_positional_plays", audio.EffectPlayers.All(p => p.HasStreamPlayback() || !p.Playing));
            audio.Advance(1d / 30, hub, false, Basis.Identity);
            Check("resume_unpauses_all_voices", audio.MusicPlayers.All(p => !p.StreamPaused) && audio.EffectPlayers.All(p => !p.StreamPaused) && audio.WarningPlayers.All(p => !p.StreamPaused));
            ClientAudio.SetVolume(ClientAudio.MusicBus, 0); ClientAudio.SetVolume(ClientAudio.EffectsBus, 0); starts = audio.BankStarts;
            Pump(audio, hub, 30); audio.Play("impact_spell", Vector3.Zero);
            Check("zero_volume_is_true_bus_mute_without_score_restart", AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.MusicBus)) &&
                AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.EffectsBus)) && audio.BankStarts == starts && audio.PlayingStyle == "road");
            foreach (var pair in levels) ClientAudio.SetVolume(pair.Key, pair.Value);
            CheckFootsteps(audio);
            await SettleStyle(audio, hub, "road");
            audio.Play("bell_phase3", Vector3.Zero); starts = audio.BankStarts; int cues = audio.CueCount;
            audio.Reset(hub); audio.Observe(hub); Pump(audio, hub, 1);
            Check("reset_silences_transients_without_replaying_warning_or_score", audio.CueCount == cues && audio.BankStarts == starts && audio.DuckGain == 1 &&
                audio.EffectPlayers.All(p => !p.Playing) && audio.WarningPlayers.All(p => !p.Playing));
            // A cold request superseded before completion must never start in the departed room.
            audio.SetStyle("crypt"); audio.Advance(1d / 60, hub, false, Basis.Identity); audio.SetStyle("cinder_fields");
            for (int i = 0; i < 1800 && (audio.PreparationPending || audio.ActiveMusicBanks > 0); i++) { audio.Advance(1d / 60, hub, false, Basis.Identity); await Frames(1); }
            Check("departing_opening_drops_late_preparation_and_releases_banks", !audio.PreparationPending && audio.ActiveMusicBanks == 0 && audio.DesiredStyle == "" && audio.BankStarts == starts &&
                !audio.Play("impact_weapon", Vector3.Zero) && !audio.Play("unknown", Vector3.Zero));
            Check("mixer_and_all_edge_fixtures_are_read_only", _session.StateHash == hash);
        }
        finally { foreach (var pair in levels) ClientAudio.SetVolume(pair.Key, pair.Value); audio.QueueFree(); await Frames(2); }
    }

    private void CheckWarnings(OpeningAudio audio, CombatView hub)
    {
        audio.Reset(hub); audio.Play("guard_tell", Vector3.Zero); audio.Play("bell_phase2", Vector3.Zero);
        ulong critical = audio.WarningPlayers[1].Stream!.GetInstanceId(), creature = audio.WarningPlayers[0].Stream!.GetInstanceId();
        int count = audio.CueCount;
        for (int i = 0; i < 200; i++)
        {
            audio.Play("impact_weapon", Vector3.One); audio.Play("impact_armor", Vector3.Zero); audio.Play("impact_spell", -Vector3.One);
            audio.Play("step_stone_1", Vector3.Zero); audio.Play("archer_tell", Vector3.Zero); audio.Play("low_health", Vector3.Zero);
        }
        Check("warning_reservations_survive_dense_foley", audio.WarningPlayers[1].Stream!.GetInstanceId() == critical && audio.WarningPlayers[0].Stream!.GetInstanceId() == creature &&
            ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("bell_phase2")) && audio.EffectPlayers.Count == 8);
        Check("same_frame_foley_burst_is_coalesced", audio.CueCount - count <= 4);
        Check("near_listener_foley_keeps_authored_headroom", audio.EffectPlayers.Where(p => p.Stream is not null).All(p => Math.Abs(p.MaxDb - p.VolumeDb) < .001f && p.MaxDb <= -10));
        Pump(audio, hub, 8);
        Check("critical_warning_ducks_music", audio.DuckGain <= .27f);
        audio.Play("bell_phase3", Vector3.Zero);
        Check("new_phase_can_replace_older_critical_warning", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("bell_phase3")));
        audio.Play("saint_tell", Vector3.Zero);
        Check("saint_warning_replaces_weaker_creature_tell", ReferenceEquals(audio.WarningPlayers[0].Stream, OpeningFoley.GetStream("saint_tell")));
        Pump(audio, hub, 24); audio.Play("crypt_tell", Vector3.Zero);
        Check("lesser_tell_cannot_interrupt_saint", ReferenceEquals(audio.WarningPlayers[0].Stream, OpeningFoley.GetStream("saint_tell")));
        Pump(audio, hub, 220);
        Check("music_recovers_after_warning_tail", audio.DuckGain > .99f);
    }

    private void CheckFootsteps(OpeningAudio audio)
    {
        var registry = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat"));
        var walk = registry.CreateEncounter("hub"); audio.SetStyle("road"); audio.Reset(walk.View);
        int before = audio.FootstepCount;
        for (int i = 0; i < 45; i++) { walk.Step(); audio.Advance(1d / 30, walk.View, false, Basis.Identity); audio.Observe(walk.View); }
        Check("idle_ticks_do_not_generate_steps", audio.FootstepCount == before);
        for (int i = 0; i < 28; i++) { walk.Step([new(CombatCommandKind.Move, X: 1)]); audio.Advance(1d / 30, walk.View, false, Basis.Identity); audio.Observe(walk.View); }
        Check("real_core_movement_generates_bounded_dirt_steps", audio.FootstepCount > before && audio.FootstepCount - before <= 5 && audio.LastCue.StartsWith("step_dirt_", StringComparison.Ordinal));
        before = audio.FootstepCount; var repeated = walk.View;
        for (int i = 0; i < 40; i++) { audio.Observe(repeated); audio.Advance(1d / 30, repeated, false, Basis.Identity); }
        Check("repeated_view_does_not_double_count_distance", audio.FootstepCount == before);
        // These isolated view fixtures cover discontinuities without assigning them to any Core session.
        var actor = repeated.Actors.Single(a => a.Id == 1);
        var teleported = repeated with { Tick = repeated.Tick + 1, Actors = [actor with { Position = new CorePosition(actor.Position.X + 4000, actor.Position.Z) }] };
        audio.Observe(teleported); audio.Observe(teleported with { Tick = teleported.Tick + 1 }, dodged: true);
        audio.Observe(teleported with { Tick = 0 });
        Check("teleport_dodge_and_rollback_do_not_create_steps", audio.FootstepCount == before);
        audio.SetStyle("monastery"); audio.Reset(registry.CreateEncounter("hub").View); walk = registry.CreateEncounter("hub");
        for (int i = 0; i < 28; i++) { walk.Step([new(CombatCommandKind.Move, X: 1)]); audio.Advance(1d / 30, walk.View, false, Basis.Identity); audio.Observe(walk.View); }
        Check("paving_uses_stone_footsteps", audio.FootstepCount > before && audio.LastCue.StartsWith("step_stone_", StringComparison.Ordinal));
        var healthy = registry.CreateEncounter("hub").View; actor = healthy.Actors.Single(a => a.Id == 1); audio.Reset(healthy);
        int cues = audio.CueCount;
        var low = healthy with { Tick = healthy.Tick + 1, Actors = [actor with { Health = Math.Max(1, actor.MaxHealth / 5) }] };
        audio.Observe(low);
        Check("new_low_health_crossing_warns_once", audio.CueCount == cues + 1 && audio.LastCue == "low_health");
        for (int i = 0; i < 40; i++) { audio.Advance(.1, low, false, Basis.Identity); audio.Observe(low with { Tick = low.Tick + i + 1 }); }
        Check("remaining_low_health_does_not_repeat_warning", audio.CueCount == cues + 1);
        audio.Reset(low); cues = audio.CueCount; audio.Observe(low with { Tick = low.Tick + 1 });
        Check("loading_low_health_does_not_replay_warning", audio.CueCount == cues);
        audio.Reset(healthy); audio.Play("bell_phase2", Vector3.Zero); cues = audio.CueCount;
        audio.Observe(low);
        Check("phase_reserves_critical_voice_over_new_heartbeat", audio.CueCount == cues && ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("bell_phase2")));
        for (int i = 0; i < 80; i++) { audio.Advance(1d / 30, low, false, Basis.Identity); audio.Observe(low with { Tick = low.Tick + i + 1 }); }
        Check("suppressed_low_health_crossing_is_delivered_after_phase", audio.CueCount == cues + 1 && audio.LastCue == "low_health");
    }

    private async Task CheckNativePlayback(OpeningAudio audio, CombatView hub)
    {
        if (DisplayServer.GetName() == "headless") { _skipped.Add("native audio playback position and device timing"); return; }
        var active = audio.MusicPlayers.Where(player => player.Stream is not null).ToArray();
        await Frames(4);
        Check("native_score_voices_are_playing", active.Length == 3 && active.All(player => player.Playing));
        double[] initial = active.Select(player => (double)player.GetPlaybackPosition()).ToArray();
        await Frames(12);
        double[] advanced = active.Select(player => (double)player.GetPlaybackPosition()).ToArray();
        Check("native_aligned_score_playheads_advance", advanced.Zip(initial).All(pair => pair.First - pair.Second > .05) && advanced.Max() - advanced.Min() <= .05);
        int starts = audio.BankStarts;
        audio.Advance(0, hub, true, Basis.Identity); await Frames(2);
        double[] paused = active.Select(player => (double)player.GetPlaybackPosition()).ToArray();
        await Frames(12);
        double[] held = active.Select(player => (double)player.GetPlaybackPosition()).ToArray();
        Check("native_pause_holds_playback_positions", held.Zip(paused).All(pair => Math.Abs(pair.First - pair.Second) < .01) && audio.BankStarts == starts);
        audio.Advance(0, hub, false, Basis.Identity); await Frames(12);
        double[] resumed = active.Select(player => (double)player.GetPlaybackPosition()).ToArray();
        Check("native_resume_continues_existing_bank", resumed.Zip(held).All(pair => pair.First - pair.Second > .05) && audio.BankStarts == starts);
        _route.Add(new { kind = "native-playheads", initial, advanced, paused, held, resumed, bankStarts = starts });
    }

    private async Task CheckNativePositionalPlayback(OpeningAudio audio, CombatView hub)
    {
        if (DisplayServer.GetName() == "headless") { _skipped.Add("native positional foley playback timing"); return; }
        audio.Play("impact_armor", Vector3.Zero);
        var voice = audio.EffectPlayers.Single(player => ReferenceEquals(player.Stream, OpeningFoley.GetStream("impact_armor")));
        // Positional Play is queued until Godot's physics/audio update. A queued sound
        // has no playback clock yet; wait for that clock before testing a true pause.
        for (int i = 0; i < 10 && !voice.HasStreamPlayback(); i++)
        { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); await Frames(1); }
        Check("native_positional_foley_starts_playback", voice.HasStreamPlayback() && voice.Playing);
        double initial = voice.GetPlaybackPosition(); await Frames(4); double advanced = voice.GetPlaybackPosition();
        Check("native_positional_foley_clock_advances", advanced - initial > .01);
        audio.Advance(0, hub, true, Basis.Identity); await Frames(2);
        double paused = voice.GetPlaybackPosition(); await Frames(8); double held = voice.GetPlaybackPosition();
        Check("native_positional_pause_holds_actual_playback", voice.HasStreamPlayback() && voice.StreamPaused && Math.Abs(held - paused) < .01);
        audio.Advance(0, hub, false, Basis.Identity); await Frames(6); double resumed = voice.GetPlaybackPosition();
        Check("native_positional_resume_continues_clock", !voice.StreamPaused && resumed - held > .025);
        _route.Add(new { kind = "native-positional-playhead", initial, advanced, paused, held, resumed });
    }

    private static void Pump(OpeningAudio audio, CombatView view, int frames)
    { for (int i = 0; i < frames; i++) audio.Advance(1d / 60, view, false, Basis.Identity); }
    private async Task SettleStyle(OpeningAudio audio, CombatView view, string style, bool settleFade = true)
    {
        audio.SetStyle(style);
        for (int i = 0; i < 1800 && (audio.PlayingStyle != style || audio.PreparationPending); i++) { audio.Advance(1d / 60, view, false, Basis.Identity); await Frames(1); }
        Check("score_ready_" + style, audio.PlayingStyle == style && !audio.PreparationPending);
        if (settleFade) Pump(audio, view, 120);
    }

    private async Task EarnOpening()
    {
        _sandbox = new Sandbox { ContentJsonOverride = _combat, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
        AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
        _stage = new CampaignStage(); AddChild(_stage);
        Refresh(); await SettleStyle(_sandbox.AudioDirector, _session.Combat.View, "greyhaven");
        Check("shipping_hub_score_is_connected", _sandbox.AudioDirector.DesiredStyle == "greyhaven" && _sandbox.AudioDirector.Mode == "exploration");
        _styles.Add("greyhaven"); await Capture("opening-audio-greyhaven.png");
        string room = _session.ActiveEncounterId;
        for (int i = 0; i < 12000 && !_session.Capture().Campaign.CompletedActs.Contains(1); i++)
        {
            var command = CampaignRuntimeSmoke.Next(_session); long tick = _session.Tick;
            var result = _session.Execute(command); _commands++;
            if (!result.Success) throw new InvalidDataException("Earned opening audio route failed: " + result.Reason);
            if (_session.Capture().Campaign.Deaths > 0) throw new InvalidDataException("Earned opening audio route died.");
            string coreHash = _session.StateHash;
            _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat);
            // Render the shipping presentation path at the actual simulated cadence. Core
            // has already advanced through the public command above; the callback is read-only.
            _sandbox._Process(_session.Tick > tick ? 1d / 30 : 0);
            Check("earned_route_presentation_preserves_core", _session.StateHash == coreHash);
            var audio = _sandbox.AudioDirector;
            if (audio.LastCue.Length > 0) _heardCues.Add(audio.LastCue);
            foreach (var warning in audio.WarningPlayers)
                foreach (var cue in OpeningFoley.Cues.Where(c => c.IsWarning))
                    if (ReferenceEquals(warning.Stream, OpeningFoley.GetStream(cue.Id))) _heardCues.Add(cue.Id);
            foreach (var effect in audio.EffectPlayers)
                foreach (var cue in OpeningFoley.Cues.Where(c => !c.IsWarning))
                    if (ReferenceEquals(effect.Stream, OpeningFoley.GetStream(cue.Id))) _heardCues.Add(cue.Id);
            if (_session.ActiveEncounterId == "campaign.bell_saint")
            {
                _bellPhases.Add(_session.Combat.View.BossPhase);
                foreach (var change in result.CombatEvents.Where(e => e.Kind == "BossPhaseChanged"))
                {
                    string expected = change.Amount >= 3 ? "bell_phase3" : "bell_phase2";
                    Check("earned_" + expected + "_uses_reserved_warning", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream(expected)));
                    _route.Add(new { kind = "phase", phase = change.Amount, tick = _session.Tick, audio.Mode, audio.BossGain, cue = expected, stateHash = coreHash });
                }
            }
            if (room != _session.ActiveEncounterId)
            {
                room = _session.ActiveEncounterId; Refresh();
                string style = Style(); await SettleStyle(audio, _session.Combat.View, style);
                _styles.Add(style);
                _route.Add(new { kind = "room", room, style, tick = _session.Tick, audio.Mode, audio.BankStarts, audio.FootstepCount, stateHash = _session.StateHash });
                Check("earned_" + style + "_selects_authored_score", audio.DesiredStyle == style && audio.PlayingStyle == style);
                if (style == "sanctum") Check("earned_bell_saint_raises_boss_layer", audio.Mode == "boss" && audio.BossGain >= .39f);
                if (style == "crypt")
                {
                    // This earned route reaches the optional vault that the main Journey
                    // smoke bypasses; use the same ground, obstacle and lighting checks here.
                    var architecture = _stage.FindChild("GreyMarchArchitecture", true, false) as Node3D
                        ?? throw new InvalidDataException("Missing earned crypt architecture.");
                    var targets = _session.Interactions.Select(interaction => new WorldInteractionTarget(interaction.ActionId,
                        interaction.Name, interaction.Position, interaction.Range, _stage.GetInteractionVisual(interaction.ActionId))).ToArray();
                    _route.Add(OpeningEnvironmentChecks.Inspect(_sandbox, architecture, _session.Room, style, targets, Check));
                    _route.Add(OpeningLightingChecks.Inspect(_sandbox, "earned_crypt", style, Check));
                }
                await Capture("opening-audio-" + style + ".png");
            }
            else if (i % 30 == 0) { Refresh(); await Frames(1); }
        }
        var final = _session.Capture();
        Check("route_earns_all_opening_encounters_and_optional_crypt", final.Campaign.CompletedActs.Contains(1) &&
            new[] { "campaign.road", "campaign.monastery", "campaign.bell_saint" }.All(final.Campaign.CompletedEncounters.Contains) &&
            final.Campaign.CompletedExploration.Contains(CampaignRuntimeSession.CryptEvent));
        Check("route_witnesses_all_five_opening_scores", new[] { "greyhaven", "road", "monastery", "crypt", "sanctum" }.All(_styles.Contains));
        Check("route_witnesses_all_bell_phases_and_authored_warnings", new[] { 1, 2, 3 }.All(_bellPhases.Contains) &&
            _heardCues.Contains("bell_phase2") && _heardCues.Contains("bell_phase3") && _heardCues.Contains("guard_tell") && _heardCues.Contains("saint_tell"));
        Check("route_witnesses_impacts_and_both_ground_materials", _heardCues.Contains("impact_weapon") && _heardCues.Any(c => c.StartsWith("step_dirt_", StringComparison.Ordinal)) &&
            _heardCues.Any(c => c.StartsWith("step_stone_", StringComparison.Ordinal)) && _sandbox.AudioDirector.FootstepCount > 0);
        Refresh(); Pump(_sandbox.AudioDirector, _session.Combat.View, 480);
        Check("defeated_saint_releases_boss_and_combat_layers", _sandbox.AudioDirector.Mode == "exploration" && _sandbox.AudioDirector.BossGain == 0 && _sandbox.AudioDirector.CombatGain == 0);
        await Capture("opening-audio-bell-defeated.png");
        await CheckShippingPauseAndReset();
        CheckAlliedCast();
        var replay = _session.CaptureReplay();
        var verified = CampaignRuntimeReplayRunner.Run(_combat, _adventure, _progression, _campaign, replay);
        Check("earned_campaign_replay_is_identical", verified.Success && verified.FinalHash == _session.StateHash);
        _finalHash = _session.StateHash; _replayHash = verified.FinalHash;
        System.IO.File.WriteAllText(Path.Combine(_output, "opening-audio.awcampaign"), JsonData.Write(replay));
        System.IO.File.WriteAllText(Path.Combine(_output, "opening-audio.save.json"), JsonData.Write(new CampaignRuntimeSave(1, _session.StateHash, _session.Capture())));
        Check("earned_save_restores_identically", CampaignRuntimeSaveStore.Read(_combat, _adventure, _progression, _campaign,
            System.IO.File.ReadAllText(Path.Combine(_output, "opening-audio.save.json"))).StateHash == _session.StateHash);
        _stage.QueueFree(); _sandbox.QueueFree(); await Frames(3);
    }

    private async Task CheckShippingPauseAndReset()
    {
        var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        audio.Play("bell_phase3", Vector3.Zero); _sandbox._Process(1d / 30);
        _sandbox.SetModalPaused("opening-audio-diagnostic", true); _sandbox._Process(1d / 30);
        var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts);
        for (int i = 0; i < 20; i++) { _sandbox._Process(1d / 30); await Frames(1); }
        Check("shipping_modal_pause_freezes_audio_and_core", _sandbox.IsPaused && audio.Paused && hash == _session.StateHash &&
            frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts) && audio.MusicPlayers.Count(p => p.HasStreamPlayback()) == 3 && audio.MusicPlayers.Where(p => p.HasStreamPlayback()).All(p => p.StreamPaused));
        _sandbox.SetModalPaused("opening-audio-diagnostic", false); _sandbox._Process(1d / 30);
        int starts = audio.BankStarts, cues = audio.CueCount, steps = audio.FootstepCount;
        _sandbox.SetSession(_session.Combat); Refresh(); _sandbox._Process(1d / 30);
        Check("shipping_set_session_clears_tails_without_replaying_victory", !audio.Paused && audio.BankStarts == starts && audio.CueCount == cues && audio.FootstepCount == steps &&
            audio.EffectPlayers.All(p => !p.Playing) && audio.WarningPlayers.All(p => !p.Playing) && _session.StateHash == hash);
        await Frames(2);
    }

    private void CheckAlliedCast()
    {
        var fixture = CampaignRuntimeSession.Create(_combat, _adventure, _progression, _campaign, discipline: "Gravecaller");
        Check("ally_fixture_starts_through_public_travel", fixture.EnterAct(1).Success);
        CombatEvent? alliedCast = null;
        for (int i = 0; i < 3000 && alliedCast is null; i++)
        {
            var result = fixture.Step(CampaignCombatSmoke.Commands(fixture.Combat.View, fixture.Room));
            if (!result.Success) throw new InvalidDataException("Gravecaller audio fixture failed: " + result.Reason);
            var allies = fixture.Combat.View.Actors.Where(actor => actor.Faction == CombatFaction.Ally).Select(actor => actor.Id).ToHashSet();
            alliedCast = result.CombatEvents.FirstOrDefault(e => e.Kind == "AbilityStarted" && allies.Contains(e.ActorId));
        }
        Check("ally_fixture_earns_an_actual_summoned_attack", alliedCast is not null);
        string hash = fixture.StateHash;
        _sandbox.SetSession(fixture.Combat); _sandbox.SetEnvironmentStyle("road");
        int cues = _sandbox.AudioDirector.CueCount;
        _sandbox.PresentCombatEvents([alliedCast!], fixture.Combat);
        Check("allied_attack_has_no_enemy_warning", _sandbox.AudioDirector.CueCount == cues && fixture.StateHash == hash && _sandbox.AudioDirector.WarningPlayers.All(p => !p.Playing));
        _sandbox.SetSession(_session.Combat); Refresh();
    }

    private string Style()
    {
        var state = _session.Capture().Campaign;
        return EnvironmentGround.Style(_session.InHub, _session.ActiveEncounterId, state.Exploration?.Id, state.CurrentAct);
    }
    private void Refresh()
    {
        var state = _session.Capture().Campaign; var view = _session.Combat.View; var player = view.Actors.Single(a => a.Id == 1);
        _sandbox.AdoptSession(_session.Combat);
        string style = Style();
        _sandbox.PresentAuthoredRoom(_session.Room, "opening-audio:" + _session.ActiveEncounterId, style);
        _sandbox.SetEnvironmentStyle(style);
        _stage.Show(state, _session.View, _session.Room, _session.Interactions, _session.Production.View.ActiveManifestations,
            _session.Production.ProgressionView.HubStage, player.Position, view.BossPhase,
            _session.ActiveEncounterId == "campaign.bell_saint" && _session.EncounterCleared, view, _session.ActiveEncounterId);
        _sandbox.SetWorldSubtitle("OPENING AUDIO / " + style.ToUpperInvariant());
    }
    private async Task Capture(string name)
    {
        await Frames(2);
        if (!OS.GetCmdlineUserArgs().Contains("--capture-opening-audio")) return;
        if (DisplayServer.GetName() == "headless") { if (!_skipped.Contains("native screenshots")) _skipped.Add("native screenshots"); return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.GetHeight() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok);
        if (!_captures.Contains(name)) _captures.Add(name);
    }
    private async Task Frames(int count)
    { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(string name, bool passed)
    { _checks[name] = passed; if (!passed) throw new InvalidDataException("Opening audio check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "OpeningAudioClientSmokePassed" : "OpeningAudioClientSmokeFailed",
            passed,
            checks = _checks,
            pcm = _pcm,
            auditions = _auditions,
            route = _route,
            styles = _styles.Order().ToArray(),
            cues = _heardCues.Order().ToArray(),
            bellPhases = _bellPhases.Order().ToArray(),
            commands = _commands,
            finalHash = _finalHash,
            replayHash = _replayHash,
            captures = _captures,
            skippedChecks = _skipped,
            error,
            scope = "Original generated PCM and native Godot streams/mixer. Separate read-only view fixtures test discontinuities. A fresh campaign earns Greyhaven, the road, Widow's Crypt, monastery, ritual choice and all Bell Saint phases using public commands; Sandbox presents those actual events. Simulation runs faster than wall time, so this is not a human playtest, hearing-quality assessment or loopback recording. Audition WAVs contain authored source PCM before runtime bus attenuation. No player saves or preferences are used."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "opening-audio-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
