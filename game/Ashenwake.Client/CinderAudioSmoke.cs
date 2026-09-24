using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Earned Act III audio routing, with explicitly separate read-only mixer fixtures.</summary>
public partial class CinderAudioSmoke : Node
{
    private static readonly string[] Styles = ["cinder_fields", "cinder_extraction", "cinder_foundry", "cinder_storm", "cinder_furnace"];
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<object> _route = [];
    private readonly HashSet<string> _styles = [], _cues = [], _captures = [];
    private readonly HashSet<int> _phases = [];
    private readonly HashSet<string> _furnaceStates = [];
    private int _exposures;
    private CombatView? _liveFurnace;
    private CombatEvent? _earnedBruteTell;
    private CombatSnapshot? _earnedBruteSnapshot;
    private int _coalescedBruteWarnings;
    private readonly List<string> _skipped = [];
    private CampaignContent _campaign = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private CampaignRuntimeSession _session = null!;
    private Sandbox _sandbox = null!;
    private CampaignStage _stage = null!;
    private string _output = "", _combat = "", _finalHash = "", _replayHash = "";
    private int _commands;
    private bool _writeReport, _victory;
    private static string Read(string name) => Godot.FileAccess.GetFileAsString("res://" + name + ".json");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--cinder-audio-smoke") || _output.Length == 0)
                throw new InvalidDataException("Cinder audio smoke requires --cinder-audio-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Cinder audio smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); _writeReport = true;
            Engine.MaxFps = 60; GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _combat = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign")); _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combat, _adventure, _progression, _campaign);
            await CheckMixerFixtures();
            await EarnCinder();
            Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private async Task CheckMixerFixtures()
    {
        string hash = _session.StateHash; var hub = _session.Combat.View;
        var levels = new[] { ClientAudio.MasterBus, ClientAudio.MusicBus, ClientAudio.EffectsBus, ClientAudio.InterfaceBus }
            .ToDictionary(bus => bus, ClientAudio.GetVolume);
        var audio = new OpeningAudio(); AddChild(audio);
        try
        {
            foreach (string style in Styles)
            {
                await SettleStyle(audio, hub, style);
                Check(style + "_bounded_routed_voices", audio.ActiveMusicBanks == 1 && audio.MusicVoiceCount == 6 &&
                    audio.EffectPlayers.Count + audio.WarningPlayers.Count == OpeningAudio.VoiceCapacity &&
                    audio.MusicPlayers.All(p => p.Bus == ClientAudio.MusicBus) &&
                    audio.EffectPlayers.All(p => p.Bus == ClientAudio.EffectsBus) && audio.WarningPlayers.All(p => p.Bus == ClientAudio.EffectsBus));
            }
            audio.Reset(hub); audio.Play("emberling_tell", Vector3.Zero); audio.Play("furnace_tell", Vector3.Zero);
            audio.Play("furnace_phase2", Vector3.Zero); int count = audio.CueCount;
            for (int i = 0; i < 200; i++)
            {
                foreach (string cue in new[] { "brute_tell", "sentinel_tell", "emberling_tell", "storm_tell", "low_health", "impact_weapon", "furnace_exposed", "step_metal_1" })
                    audio.Play(cue, Vector3.Zero);
            }
            Check("furnace_warning_survives_lesser_tells_and_dense_foley", HasCue(audio, "furnace_tell") && HasCue(audio, "furnace_phase2") && audio.CueCount - count <= 2);
            Pump(audio, hub, 8);
            Check("furnace_phase_ducks_music", audio.DuckGain <= .27f);
            audio.Play("furnace_shutdown", Vector3.Zero);
            Pump(audio, hub, 24); audio.Play("furnace_phase2", Vector3.Zero); audio.Play("furnace_exposed", Vector3.Zero); audio.Play("low_health", Vector3.Zero);
            Check("furnace_victory_reserves_critical_voice_over_phase_exposure_and_heartbeat", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("furnace_shutdown")));
            Pump(audio, hub, 240);
            Check("cinder_warning_tail_releases_duck", audio.DuckGain > .99f);
            var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount);
            audio.Advance(0, hub, true, Basis.Identity);
            for (int i = 0; i < 60; i++) { audio.Advance(.1, hub, true, Basis.Identity); audio.Play("furnace_shutdown", Vector3.Zero); }
            Check("cinder_pause_freezes_envelopes_and_rejects_new_transients", frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount));
            audio.Advance(0, hub, false, Basis.Identity);
            await CheckNativePlayback(audio, hub);
            int starts = audio.BankStarts;
            ClientAudio.SetVolume(ClientAudio.MusicBus, 0); ClientAudio.SetVolume(ClientAudio.EffectsBus, 0);
            audio.Play("storm_tell", Vector3.Zero); Pump(audio, hub, 20);
            Check("cinder_mute_uses_settings_buses_without_restarting_score", AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.MusicBus)) &&
                AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.EffectsBus)) && audio.BankStarts == starts);
            foreach (var pair in levels) ClientAudio.SetVolume(pair.Key, pair.Value);
            count = audio.CueCount;
            audio.SetStyle("hollow_rooms");
            Pump(audio, hub, 130);
            Check("departure_releases_cinder_banks_and_transients", audio.ActiveMusicBanks == 0 && audio.DesiredStyle == "" && audio.CueCount == count &&
                audio.EffectPlayers.All(p => !p.Playing) && audio.WarningPlayers.All(p => !p.Playing));
            await SettleStyle(audio, hub, "cinder_fields");
            Check("return_to_cinder_does_not_replay_one_shots", audio.CueCount == count && audio.BankStarts == starts + 1);
            Check("mixer_fixture_preserves_core_and_user_bus_levels", hash == _session.StateHash && levels.All(pair => Math.Abs(ClientAudio.GetVolume(pair.Key) - pair.Value) < .0001));
        }
        finally { foreach (var pair in levels) ClientAudio.SetVolume(pair.Key, pair.Value); audio.QueueFree(); await Frames(2); }
    }

    private async Task CheckNativePlayback(OpeningAudio audio, CombatView view)
    {
        if (DisplayServer.GetName() == "headless") { _skipped.Add("native score playheads and pause timing"); return; }
        var active = audio.MusicPlayers.Where(p => p.Stream is not null).ToArray();
        await Frames(4); double[] before = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        await Frames(12); double[] after = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        Check("native_cinder_stems_play_and_advance_together", active.Length == 3 && active.All(p => p.Playing) &&
            after.Zip(before).All(p => p.First - p.Second > .05) && after.Max() - after.Min() <= .05);
        audio.Advance(0, view, true, Basis.Identity); await Frames(2);
        double[] paused = active.Select(p => (double)p.GetPlaybackPosition()).ToArray(); await Frames(12);
        double[] held = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        Check("native_cinder_pause_holds_score_clock", held.Zip(paused).All(p => Math.Abs(p.First - p.Second) < .01));
        audio.Advance(0, view, false, Basis.Identity); await Frames(12);
        double[] resumed = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        Check("native_cinder_resume_continues_score_clock", resumed.Zip(held).All(p => p.First - p.Second > .05));
        _route.Add(new { kind = "native-playheads", before, after, paused, held, resumed });
    }

    private async Task EarnCinder()
    {
        _sandbox = new Sandbox { ContentJsonOverride = _combat, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
        AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
        _stage = new CampaignStage(); AddChild(_stage);
        Refresh(); string room = _session.ActiveEncounterId; int emberlingWait = 0;
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !_session.Capture().Campaign.CompletedActs.Contains(3); i++)
        {
            long tick = _session.Tick;
            var previous = _session.Combat.View.Actors.FirstOrDefault(a => a.DefinitionId == "boss.furnace_spindle");
            var command = CampaignRuntimeSmoke.Next(_session);
            // Ordinary idle commands allow the short-lived emberling's real windup
            // to reach presentation. These inputs remain part of the verified replay.
            if (_session.ActiveEncounterId == "campaign.cinder_pack" && !_cues.Contains("emberling_tell") && emberlingWait++ < 180)
                command = new(CampaignRuntimeAction.Tick, Commands: []);
            var result = _session.Execute(command); _commands++;
            if (!result.Success) throw new InvalidDataException("Earned Cinder audio route failed: " + result.Reason);
            if (_session.Capture().Campaign.Deaths > 0) throw new InvalidDataException("Earned Cinder audio route died.");
            string hash = _session.StateHash;
            var audio = _sandbox.AudioDirector;
            if (room != _session.ActiveEncounterId)
            {
                room = _session.ActiveEncounterId; Refresh(); string style = Style();
                if (Styles.Contains(style))
                {
                    await SettleStyle(audio, _session.Combat.View, style); _styles.Add(style);
                    Check("earned_" + style + "_selects_regional_score", audio.DesiredStyle == style && audio.PlayingStyle == style);
                    _route.Add(new { kind = "earned-room", room, style, tick = _session.Tick, audio.BankStarts, audio.Mode, stateHash = _session.StateHash });
                    await Capture("cinder-audio-" + style + ".png");
                }
            }

            _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat);
            _sandbox._Process(_session.Tick > tick ? 1d / 30 : 0);
            Check("earned_presentation_preserves_core", _session.StateHash == hash);
            foreach (var cue in OpeningFoley.Cues) if (HasCue(audio, cue.Id)) _cues.Add(cue.Id);
            foreach (var ev in result.CombatEvents.Where(e => e.Kind == "AbilityStarted"))
            {
                var source = _session.Combat.View.Actors.FirstOrDefault(a => a.Id == ev.ActorId);
                if (source?.DefinitionId != "enemy.furnace_brute") continue;
                if (_earnedBruteTell is null)
                { _earnedBruteTell = ev; _earnedBruteSnapshot = _session.Combat.Capture(); }
                bool delivered = HasCue(audio, "brute_tell");
                var warning = OpeningFoley.Cues.FirstOrDefault(c => ReferenceEquals(audio.WarningPlayers[0].Stream, OpeningFoley.GetStream(c.Id)));
                if (!delivered)
                {
                    Check("earned_brute_coalescing_retains_another_creature_warning", warning is { Category: "enemy_tell" } && !audio.Paused && audio.DesiredStyle == Style());
                    _coalescedBruteWarnings++;
                }
                var observation = new
                {
                    kind = "earned-brute-warning",
                    tick = ev.Tick,
                    eventKind = ev.Kind,
                    ev.ContentId,
                    source.State,
                    delivered,
                    warningStream = warning?.Id,
                    stateHash = hash
                };
                _route.Add(observation); GD.Print(JsonData.Write(observation));
            }
            if (_session.ActiveEncounterId == "campaign.furnace_spindle")
            {
                var view = _session.Combat.View;
                var furnace = view.Actors.Single(a => a.DefinitionId == "boss.furnace_spindle");
                _phases.Add(view.BossPhase);
                foreach (var opening in result.CombatEvents.Where(e => e.Kind == "BossCoreWindow"))
                {
                    Check("earned_core_window_event_marks_guard_start", furnace.Guarded);
                    _route.Add(new { kind = "earned-core-guard-start", tick = view.Tick, furnace.Guarded, stateHash = hash });
                }
                foreach (var change in result.CombatEvents.Where(e => e.Kind == "BossPhaseChanged"))
                {
                    Check("earned_furnace_has_authored_second_phase", change.Amount == 2);
                    Check("earned_furnace_phase2_uses_critical_voice", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("furnace_phase2")));
                    _route.Add(new { kind = "earned-phase", phase = change.Amount, tick = _session.Tick, audio.BossGain, stateHash = hash });
                    Refresh(); await Capture("cinder-audio-furnace-phase2.png");
                }
                if (furnace.Health > 0)
                {
                    _liveFurnace = view;
                    if (previous is { Guarded: true, Health: > 0 } && !furnace.Guarded)
                    {
                        _exposures++;
                        _route.Add(new
                        {
                            kind = "earned-core-exposure",
                            phase = view.BossPhase,
                            tick = view.Tick,
                            cue = audio.LastCue,
                            criticalStreamIsExposure = ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("furnace_exposed")),
                            stateHash = hash
                        });
                    }
                    string key = $"{view.BossPhase}:{furnace.Guarded}";
                    if (_furnaceStates.Add(key))
                    {
                        int starts = audio.BankStarts;
                        Pump(audio, view, 150);
                        float expected = view.BossPhase >= 2 ? furnace.Guarded ? .75f : 1f : furnace.Guarded ? .45f : .7f;
                        Check("earned_furnace_stem_tracks_" + key, audio.Mode == "boss" && Math.Abs(audio.BossGain - expected) < .001f && audio.BankStarts == starts);
                        Check("earned_furnace_music_preserves_core_" + key, _session.StateHash == hash);
                        Refresh(); await Capture("cinder-audio-furnace-" + view.BossPhase + (furnace.Guarded ? "-guarded.png" : "-exposed.png"));
                    }
                }
                foreach (var death in result.CombatEvents.Where(e => e.Kind == "EntityKilled" && e.TargetId == furnace.Id))
                {
                    _victory = true;
                    Check("earned_furnace_death_plays_shutdown", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("furnace_shutdown")));
                    _route.Add(new { kind = "earned-victory", tick = _session.Tick, stateHash = hash });
                }
            }
            if (i % 30 == 0) { Refresh(); await Frames(1); }
        }
        var final = _session.Capture();
        Check("route_earns_act_three_with_sealed_foundry_and_burning_rain", final.Campaign.CompletedActs.Contains(3) &&
            new[] { "campaign.cinder_pack", "campaign.extraction_floor", "campaign.furnace_spindle" }.All(final.Campaign.CompletedEncounters.Contains) &&
            final.Campaign.CompletedExploration.Contains(CampaignRuntimeSession.FoundryEvent) && final.Campaign.CompletedExploration.Contains(CampaignRuntimeSession.StormEvent));
        Check("earned_route_hears_all_five_cinder_scores", Styles.All(_styles.Contains));
        Check("earned_route_hears_emberling_sentinel_furnace_and_storm_tells", new[] { "emberling_tell", "sentinel_tell", "furnace_tell", "storm_tell" }.All(_cues.Contains));
        Check("earned_route_observes_brute_windup_and_overlapping_warning_coalescing", _earnedBruteTell is not null && _coalescedBruteWarnings > 0);
        Check("earned_route_hears_furnace_phase_exposure_and_shutdown", new[] { 1, 2 }.All(_phases.Contains) && _exposures > 0 && _victory &&
            new[] { "furnace_phase2", "furnace_exposed", "furnace_shutdown" }.All(_cues.Contains));
        Check("earned_route_observes_guarded_and_exposed_music_in_both_phases", new[] { "1:True", "1:False", "2:True", "2:False" }.All(_furnaceStates.Contains));
        Check("earned_route_uses_metal_footsteps_and_combat_impacts", _cues.Any(c => c.StartsWith("step_metal_", StringComparison.Ordinal)) && _cues.Contains("impact_weapon"));
        Refresh(); Pump(_sandbox.AudioDirector, _session.Combat.View, 480);
        Check("defeated_furnace_releases_boss_and_combat_stems", _sandbox.AudioDirector.Mode == "exploration" && _sandbox.AudioDirector.BossGain == 0 && _sandbox.AudioDirector.CombatGain == 0);
        await Capture("cinder-audio-furnace-defeated.png");
        await CheckShippingReset();
        var replay = _session.CaptureReplay();
        var verified = CampaignRuntimeReplayRunner.Run(_combat, _adventure, _progression, _campaign, replay);
        Check("earned_campaign_replay_is_identical", verified.Success && verified.FinalHash == _session.StateHash);
        _finalHash = _session.StateHash; _replayHash = verified.FinalHash;
        System.IO.File.WriteAllText(Path.Combine(_output, "cinder-audio.awcampaign"), JsonData.Write(replay));
        string save = JsonData.Write(new CampaignRuntimeSave(1, _session.StateHash, _session.Capture()));
        System.IO.File.WriteAllText(Path.Combine(_output, "cinder-audio.save.json"), save);
        var restored = CampaignRuntimeSaveStore.Read(_combat, _adventure, _progression, _campaign, save);
        Check("earned_save_restores_identically", restored.StateHash == _session.StateHash);
        int cues = _sandbox.AudioDirector.CueCount;
        _session = restored; _sandbox.SetSession(restored.Combat); _sandbox._Process(1d / 30);
        Check("restoring_defeated_furnace_does_not_replay_shutdown", _sandbox.AudioDirector.CueCount == cues);
        CheckFurnaceImpactFixtures();
        CheckExposureFixtures();
        await CheckCapturedBruteTellFixture();
        _stage.QueueFree(); _sandbox.QueueFree(); await Frames(3);
    }

    private async Task CheckCapturedBruteTellFixture()
    {
        // The real route intentionally coalesces overlapping creature warnings. Replay
        // its exact earned brute event and captured combat state in a detached shipping
        // Sandbox with an idle warning lane to verify routing separately from audibility.
        var ev = _earnedBruteTell ?? throw new InvalidDataException("No earned brute tell captured.");
        var snapshot = _earnedBruteSnapshot ?? throw new InvalidDataException("No earned brute state captured.");
        var combat = CombatSession.Restore(_combat, snapshot);
        string liveHash = _session.StateHash, fixtureHash = combat.StateHash;
        var fixture = new Sandbox
        {
            ContentJsonOverride = _combat,
            AutomaticStep = true,
            AdvanceOverride = _ => [],
            SessionOverride = () => combat
        };
        AddChild(fixture); fixture.SetProcess(false); fixture.EnableCampaign(); fixture.SetSession(combat);
        try
        {
            fixture.SetEnvironmentStyle("cinder_fields");
            var audio = fixture.AudioDirector;
            audio.Reset(combat.View); foreach (var voice in audio.WarningPlayers) voice.Stream = null;
            int before = audio.CueCount;
            fixture.PresentCombatEvents([ev], combat);
            Check("captured_earned_brute_event_routes_to_its_distinct_idle_warning_voice", audio.CueCount == before + 1 &&
                ReferenceEquals(audio.WarningPlayers[0].Stream, OpeningFoley.GetStream("brute_tell")) &&
                audio.WarningPlayers[1].Stream is null);
            Check("captured_brute_routing_fixture_preserves_live_and_restored_core", _session.StateHash == liveHash && combat.StateHash == fixtureHash);
            _route.Add(new
            {
                kind = "captured-earned-event-routing-fixture",
                eventKind = ev.Kind,
                ev.Tick,
                ev.ActorId,
                ev.ContentId,
                cue = "brute_tell",
                delivered = audio.CueCount - before,
                fixtureHash,
                liveHash
            });
        }
        finally { fixture.QueueFree(); await Frames(2); }
    }

    private void CheckFurnaceImpactFixtures()
    {
        // Explicitly separate presentation-only events, not evidence that the route
        // took these hits. They never enter Core or the campaign replay.
        string hash = _session.StateHash;
        var audio = _sandbox.AudioDirector;
        int furnace = _session.Combat.View.Actors.Single(a => a.DefinitionId == "boss.furnace_spindle").Id;
        foreach (string content in new[] { "campaign.furnace_vent", "campaign.slag", "campaign.heatvent", "campaign.forgesweep", "enemy.detonate", "rule.storm" })
        {
            audio.Reset(_session.Combat.View);
            foreach (var voice in audio.EffectPlayers) voice.Stream = null;
            int before = audio.CueCount;
            _sandbox.PresentCombatEvents([new(_session.Combat.View.Tick, "DamageApplied", furnace, 1, 1, content)], _session.Combat);
            Check("fixture_" + content + "_dispatches_one_new_spell_impact", audio.CueCount == before + 1 &&
                audio.EffectPlayers.Count(p => ReferenceEquals(p.Stream, OpeningFoley.GetStream("impact_spell"))) == 1 &&
                audio.EffectPlayers.All(p => !ReferenceEquals(p.Stream, OpeningFoley.GetStream("impact_weapon"))) &&
                _session.StateHash == hash);
            _route.Add(new { kind = "read-only-impact-fixture", content, cue = audio.LastCue, delivered = audio.CueCount - before, stateHash = hash });
        }
        audio.Reset(_session.Combat.View);
    }

    private void CheckExposureFixtures()
    {
        // Views copied from a real earned living boss are detached presentation
        // fixtures. No fabricated guard, death or tick enters the campaign.
        var live = _liveFurnace ?? throw new InvalidDataException("No living furnace view earned.");
        var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        CombatView Project(bool guarded, bool dead = false, long ticks = 0) => live with
        {
            Tick = live.Tick + ticks,
            Actors = live.Actors.Select(a => a.DefinitionId == "boss.furnace_spindle"
                ? a with { Guarded = guarded, Health = dead ? 0 : a.Health } : a).ToArray()
        };
        var guard = Project(true); var exposed = Project(false, ticks: 1);
        audio.Reset(exposed); int count = audio.CueCount;
        audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_load_of_exposed_furnace_is_silent", audio.CueCount == count);
        audio.Reset(guard); count = audio.CueCount;
        audio.Observe(exposed);
        Check("fixture_real_guard_to_exposed_view_delivers_once", audio.CueCount == count + 1 && audio.LastCue == "furnace_exposed");
        audio.Observe(exposed); audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_refresh_does_not_repeat_exposure", audio.CueCount == count + 1);
        audio.Reset(guard); count = audio.CueCount;
        audio.Observe(Project(false, dead: true, ticks: 1));
        Check("fixture_guarded_death_cannot_emit_exposure", audio.CueCount == count);
        audio.Reset(guard); audio.Advance(0, guard, true, Basis.Identity); count = audio.CueCount;
        audio.Reset(exposed); audio.Observe(exposed); audio.Advance(0, exposed, false, Basis.Identity);
        audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_paused_load_does_not_replay_exposure_on_resume", audio.CueCount == count);
        audio.Reset(guard); audio.SetStyle("hollow_rooms"); audio.Observe(exposed);
        count = audio.CueCount; audio.SetStyle("cinder_furnace"); audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_room_reset_does_not_replay_exposure", audio.CueCount == count);
        Check("exposure_fixtures_preserve_earned_core", hash == _session.StateHash);
        audio.Reset(_session.Combat.View);
    }

    private async Task CheckShippingReset()
    {
        var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        var ambience = _sandbox.GetChildren().OfType<AudioStreamPlayer>().Single(p => p.Name == "RegionalAmbience");
        float busVolume = ClientAudio.GetVolume(ClientAudio.MusicBus);
        audio.Play("furnace_shutdown", Vector3.Zero);
        for (int i = 0; i < 8; i++) _sandbox._Process(1d / 60);
        Check("shipping_cinder_ambience_ducks_for_warning_without_changing_settings", ambience.Bus == ClientAudio.MusicBus &&
            ambience.VolumeDb < -39 && ClientAudio.GetVolume(ClientAudio.MusicBus) == busVolume);
        _sandbox.SetModalPaused("cinder-audio-diagnostic", true); _sandbox._Process(1d / 30);
        var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts);
        for (int i = 0; i < 12; i++) { _sandbox._Process(1d / 30); await Frames(1); }
        Check("shipping_pause_freezes_cinder_audio_and_core", _sandbox.IsPaused && audio.Paused && hash == _session.StateHash &&
            frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts));
        _sandbox.SetModalPaused("cinder-audio-diagnostic", false); _sandbox._Process(1d / 30);
        int starts = audio.BankStarts, cues = audio.CueCount;
        _sandbox.SetSession(_session.Combat); Refresh(); _sandbox._Process(1d / 30);
        Check("shipping_reset_clears_tails_without_replaying_victory_or_score", !audio.Paused && audio.BankStarts == starts && audio.CueCount == cues &&
            audio.EffectPlayers.All(p => !p.Playing) && audio.WarningPlayers.All(p => !p.Playing) && hash == _session.StateHash);
    }

    private static bool HasCue(OpeningAudio audio, string cue) => audio.WarningPlayers.Any(p => ReferenceEquals(p.Stream, OpeningFoley.GetStream(cue))) ||
        audio.EffectPlayers.Any(p => ReferenceEquals(p.Stream, OpeningFoley.GetStream(cue)));
    private static void Pump(OpeningAudio audio, CombatView view, int count)
    { for (int i = 0; i < count; i++) audio.Advance(1d / 60, view, false, Basis.Identity); }
    private async Task SettleStyle(OpeningAudio audio, CombatView view, string style)
    {
        audio.SetStyle(style);
        for (int i = 0; i < 1800 && (audio.PlayingStyle != style || audio.PreparationPending); i++)
        { audio.Advance(1d / 60, view, false, Basis.Identity); await Frames(1); }
        Check("score_prepared_" + style, audio.PlayingStyle == style && !audio.PreparationPending);
        Pump(audio, view, 120);
    }
    private string Style()
    {
        var state = _session.Capture().Campaign;
        return EnvironmentGround.Style(_session.InHub, _session.ActiveEncounterId, state.Exploration?.Id, state.CurrentAct);
    }
    private void Refresh()
    {
        var state = _session.Capture().Campaign; var view = _session.Combat.View;
        _sandbox.AdoptSession(_session.Combat); string style = Style();
        _sandbox.PresentAuthoredRoom(_session.Room, "cinder-audio:" + _session.ActiveEncounterId, style);
        _sandbox.SetEnvironmentStyle(style);
        _stage.Show(state, _session.View, _session.Room, _session.Interactions, _session.Production.View.ActiveManifestations,
            _session.Production.ProgressionView.HubStage, view.Actors.Single(a => a.Id == 1).Position, view.BossPhase,
            combat: view, activeEncounterId: _session.ActiveEncounterId);
        _sandbox.SetWorldSubtitle("CINDER AUDIO / " + style.ToUpperInvariant());
    }
    private async Task Capture(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture-cinder-audio")) return;
        if (DisplayServer.GetName() == "headless") { if (!_skipped.Contains("native screenshots")) _skipped.Add("native screenshots"); return; }
        await Frames(2); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.GetHeight() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Cinder audio check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "CinderAudioClientSmokePassed" : "CinderAudioClientSmokeFailed",
            passed,
            checks = _checks,
            route = _route,
            styles = _styles.Order().ToArray(),
            cues = _cues.Order().ToArray(),
            furnacePhases = _phases.Order().ToArray(),
            furnaceStates = _furnaceStates.Order().ToArray(),
            exposureTransitions = _exposures,
            coalescedBruteWarnings = _coalescedBruteWarnings,
            commands = _commands,
            finalHash = _finalHash,
            replayHash = _replayHash,
            captures = _captures.Order().ToArray(),
            skippedChecks = _skipped,
            error,
            scope = "Fresh public campaign commands earn Acts I–III, including Sealed Foundry, Burning Rain, Furnace phases, actual guarded-to-exposed transitions and shutdown. Sandbox presents actual earned events. Real brute windups coalesce behind overlapping creature warnings; a separate detached shipping Sandbox replays the exact captured earned brute event against its captured combat state to verify distinct routing with an idle warning lane. Separate read-only mixer fixtures exercise warning priority, bounded voices, bus muting, pause and room departure. PCM and audition coverage is provided by OpeningAudioSmoke's expanded catalog. Faster-than-wall-time command simulation is not a human playtest, hearing-quality assessment or audio loopback recording. No player saves or preferences are used."
        };
        try
        {
            string json = JsonData.Write(report);
            if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "cinder-audio-review.json"), json);
            GD.Print(json);
        }
        catch (Exception ex) { passed = false; GD.PushError("Cinder audio report failed: " + ex); }
        finally { GetTree().Quit(passed ? 0 : 1); }
    }
}
