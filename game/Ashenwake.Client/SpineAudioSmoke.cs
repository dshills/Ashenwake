using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Earned Act IV audio routing, with explicitly separate read-only mixer fixtures.</summary>
public partial class SpineAudioSmoke : Node
{
    private static readonly string[] Styles = ["spine_causeway", "spine_hall", "spine_archive", "spine_memory", "spine_warden"];
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<object> _route = [];
    private readonly HashSet<string> _styles = [], _cues = [], _captures = [];
    private readonly HashSet<int> _phases = [];
    private readonly HashSet<string> _wardenStates = [];
    private int _exposures;
    private CombatView? _liveWarden;
    private readonly Dictionary<string, (string Cue, CombatEvent Event, CombatSnapshot Snapshot, string Style)> _earnedWarnings = [];
    private readonly HashSet<string> _faultSequences = [];
    private int _coalescedWarnings, _oathResolutions;
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
            if (!args.Contains("--spine-audio-smoke") || _output.Length == 0)
                throw new InvalidDataException("Spine audio smoke requires --spine-audio-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Spine audio smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); _writeReport = true;
            Engine.MaxFps = 60; GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _combat = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign")); _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combat, _adventure, _progression, _campaign);
            await CheckMixerFixtures();
            await EarnSpine();
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
            // Core announces all fault lanes together, then resolves the first at
            // 30 ticks and the second 20 ticks later. Keep the same warning lane
            // reserved across the whole sequence: isolated routing tests would miss
            // an overlong sample suppressing the third beat at the real cadence.
            audio.Reset(hub);
            int faultStart = audio.CueCount;
            audio.Play("spine_fault", Vector3.Zero);
            Check("fault_cadence_initial_announcement_delivers", audio.CueCount == faultStart + 1);
            Pump(audio, hub, 60); // 30 Core ticks / 30 Hz = one second.
            audio.Play("spine_fault", Vector3.Zero);
            Check("fault_cadence_second_announcement_delivers_without_reset", audio.CueCount == faultStart + 2);
            Pump(audio, hub, 40); // 20 Core ticks / 30 Hz = two thirds of a second.
            audio.Play("spine_fault", Vector3.Zero);
            Check("fault_cadence_third_announcement_delivers_without_reset", audio.CueCount == faultStart + 3 &&
                ReferenceEquals(audio.WarningPlayers[0].Stream, OpeningFoley.GetStream("spine_fault")));
            _route.Add(new
            {
                kind = "read-only-fault-cadence-fixture",
                cue = "spine_fault",
                intervalsSeconds = new[] { 1d, 20d / 30 },
                delivered = audio.CueCount - faultStart,
                authoredDurationSeconds = OpeningFoley.Describe("spine_fault").DurationSeconds
            });
            audio.Reset(hub); audio.Play("giant_tell", Vector3.Zero); audio.Play("warden_oath", Vector3.Zero);
            audio.Play("warden_phase2", Vector3.Zero); int count = audio.CueCount;
            for (int i = 0; i < 200; i++)
            {
                foreach (string cue in new[] { "keeper_tell", "bone_tell", "giant_tell", "spine_fault", "low_health", "impact_weapon", "warden_exposed", "step_stone_1" })
                    audio.Play(cue, Vector3.Zero);
            }
            Check("warden_warning_survives_lesser_tells_and_dense_foley", HasCue(audio, "warden_oath") && HasCue(audio, "warden_phase2") && audio.CueCount - count <= 2);
            Pump(audio, hub, 8);
            Check("warden_phase_ducks_music", audio.DuckGain <= .27f);
            audio.Play("warden_defeat", Vector3.Zero);
            Pump(audio, hub, 24); audio.Play("warden_phase2", Vector3.Zero); audio.Play("warden_exposed", Vector3.Zero); audio.Play("low_health", Vector3.Zero);
            Check("warden_victory_reserves_critical_voice_over_phase_exposure_and_heartbeat", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("warden_defeat")));
            Pump(audio, hub, 240);
            Check("spine_warning_tail_releases_duck", audio.DuckGain > .99f);
            var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount);
            audio.Advance(0, hub, true, Basis.Identity);
            for (int i = 0; i < 60; i++) { audio.Advance(.1, hub, true, Basis.Identity); audio.Play("warden_defeat", Vector3.Zero); }
            Check("spine_pause_freezes_envelopes_and_rejects_new_transients", frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount));
            audio.Advance(0, hub, false, Basis.Identity);
            await CheckNativePlayback(audio, hub);
            int starts = audio.BankStarts;
            ClientAudio.SetVolume(ClientAudio.MusicBus, 0); ClientAudio.SetVolume(ClientAudio.EffectsBus, 0);
            audio.Play("spine_fault", Vector3.Zero); Pump(audio, hub, 20);
            Check("spine_mute_uses_settings_buses_without_restarting_score", AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.MusicBus)) &&
                AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.EffectsBus)) && audio.BankStarts == starts);
            foreach (var pair in levels) ClientAudio.SetVolume(pair.Key, pair.Value);
            count = audio.CueCount;
            audio.SetStyle("hollow_rooms");
            Pump(audio, hub, 130);
            Check("departure_releases_spine_banks_and_transients", audio.ActiveMusicBanks == 0 && audio.DesiredStyle == "" && audio.CueCount == count &&
                audio.EffectPlayers.All(p => !p.Playing) && audio.WarningPlayers.All(p => !p.Playing));
            await SettleStyle(audio, hub, "spine_causeway");
            Check("return_to_spine_does_not_replay_one_shots", audio.CueCount == count && audio.BankStarts == starts + 1);
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
        Check("native_spine_stems_play_and_advance_together", active.Length == 3 && active.All(p => p.Playing) &&
            after.Zip(before).All(p => p.First - p.Second > .05) && after.Max() - after.Min() <= .05);
        audio.Advance(0, view, true, Basis.Identity); await Frames(2);
        double[] paused = active.Select(p => (double)p.GetPlaybackPosition()).ToArray(); await Frames(12);
        double[] held = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        Check("native_spine_pause_holds_score_clock", held.Zip(paused).All(p => Math.Abs(p.First - p.Second) < .01));
        audio.Advance(0, view, false, Basis.Identity); await Frames(12);
        double[] resumed = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        Check("native_spine_resume_continues_score_clock", resumed.Zip(held).All(p => p.First - p.Second > .05));
        _route.Add(new { kind = "native-playheads", before, after, paused, held, resumed });
    }

    private async Task EarnSpine()
    {
        _sandbox = new Sandbox { ContentJsonOverride = _combat, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
        AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
        _stage = new CampaignStage(); AddChild(_stage);
        Refresh(); string room = _session.ActiveEncounterId;
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !_session.Capture().Campaign.CompletedActs.Contains(4); i++)
        {
            long tick = _session.Tick;
            var previous = _session.Combat.View.Actors.FirstOrDefault(a => a.DefinitionId == "boss.covenant_warden");
            var command = CampaignRuntimeSmoke.Next(_session);
            var result = _session.Execute(command); _commands++;
            if (!result.Success) throw new InvalidDataException("Earned Spine audio route failed: " + result.Reason);
            if (_session.Capture().Campaign.Deaths > 0) throw new InvalidDataException("Earned Spine audio route died.");
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
                    await Capture("spine-audio-" + style + ".png");
                }
            }

            _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat);
            _sandbox._Process(_session.Tick > tick ? 1d / 30 : 0);
            Check("earned_presentation_preserves_core", _session.StateHash == hash);
            foreach (var cue in OpeningFoley.Cues) if (HasCue(audio, cue.Id)) _cues.Add(cue.Id);
            CaptureEarnedWarnings(result.CombatEvents, hash);
            if (_session.ActiveEncounterId == "campaign.covenant_warden")
            {
                var view = _session.Combat.View;
                var warden = view.Actors.Single(a => a.DefinitionId == "boss.covenant_warden");
                _phases.Add(view.BossPhase);
                foreach (var change in result.CombatEvents.Where(e => e.Kind == "BossPhaseChanged"))
                {
                    Check("earned_warden_has_authored_second_phase", change.Amount == 2);
                    Check("earned_warden_phase2_uses_critical_voice", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("warden_phase2")));
                    _route.Add(new { kind = "earned-phase", phase = change.Amount, tick = _session.Tick, audio.BossGain, stateHash = hash });
                    Refresh(); await Capture("spine-audio-warden-phase2.png");
                }
                if (warden.Health > 0)
                {
                    _liveWarden = view;
                    if (previous is { Guarded: true, Health: > 0 } && !warden.Guarded)
                    {
                        _exposures++;
                        _route.Add(new
                        {
                            kind = "earned-core-exposure",
                            phase = view.BossPhase,
                            tick = view.Tick,
                            cue = audio.LastCue,
                            criticalStreamIsExposure = ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("warden_exposed")),
                            stateHash = hash
                        });
                    }
                    string key = $"{view.BossPhase}:{warden.Guarded}";
                    if (_wardenStates.Add(key))
                    {
                        int starts = audio.BankStarts;
                        Pump(audio, view, 150);
                        float expected = view.BossPhase >= 2 ? warden.Guarded ? .75f : 1f : warden.Guarded ? .45f : .7f;
                        Check("earned_warden_stem_tracks_" + key, audio.Mode == "boss" && Math.Abs(audio.BossGain - expected) < .001f && audio.BankStarts == starts);
                        Check("earned_warden_music_preserves_core_" + key, _session.StateHash == hash);
                        Refresh(); await Capture("spine-audio-warden-" + view.BossPhase + (warden.Guarded ? "-guarded.png" : "-exposed.png"));
                    }
                }
                foreach (var death in result.CombatEvents.Where(e => e.Kind == "EntityKilled" && e.TargetId == warden.Id))
                {
                    _victory = true;
                    Check("earned_warden_death_plays_defeat", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("warden_defeat")));
                    _route.Add(new { kind = "earned-victory", tick = _session.Tick, stateHash = hash });
                }
            }
            if (i % 30 == 0) { Refresh(); await Frames(1); }
        }
        var final = _session.Capture();
        Check("route_earns_act_four_with_archive_and_divine_memory", final.Campaign.CompletedActs.Contains(4) &&
            new[] { "campaign.bone_causeway", "campaign.contract_hall", "campaign.covenant_warden" }.All(final.Campaign.CompletedEncounters.Contains) &&
            final.Campaign.CompletedExploration.Contains(CampaignRuntimeSession.ArchiveEvent) && final.Campaign.CompletedExploration.Contains(CampaignRuntimeSession.MemoryEvent));
        Check("earned_route_hears_all_five_spine_scores", Styles.All(_styles.Contains));
        Check("earned_route_captures_all_distinct_spine_warnings", new[] { "giant_tell", "keeper_tell", "bone_tell", "warden_oath", "warden_fault", "spine_fault", "spine_fault_after_rule.fault.1", "spine_fault_after_rule.fault.2" }.All(_earnedWarnings.ContainsKey));
        Check("earned_route_hears_warden_oath_fault_and_regional_faults", new[] { "warden_oath", "warden_fault", "spine_fault" }.All(_cues.Contains));
        Check("earned_route_verifies_forward_and_reverse_fault_sequences", new[] { "spine_causeway", "spine_memory" }.All(_faultSequences.Contains) && _oathResolutions > 0);
        Check("earned_route_hears_warden_phase_exposure_and_defeat", new[] { 1, 2 }.All(_phases.Contains) && _exposures > 0 && _victory &&
            new[] { "warden_phase2", "warden_exposed", "warden_defeat" }.All(_cues.Contains));
        Check("earned_route_observes_guarded_and_exposed_music_in_both_phases", new[] { "1:True", "1:False", "2:True", "2:False" }.All(_wardenStates.Contains));
        Check("earned_route_uses_stone_footsteps_and_combat_impacts", _cues.Any(c => c.StartsWith("step_stone_", StringComparison.Ordinal)) && _cues.Contains("impact_weapon"));
        Refresh(); Pump(_sandbox.AudioDirector, _session.Combat.View, 480);
        Check("defeated_warden_releases_boss_and_combat_stems", _sandbox.AudioDirector.Mode == "exploration" && _sandbox.AudioDirector.BossGain == 0 && _sandbox.AudioDirector.CombatGain == 0);
        await Capture("spine-audio-warden-defeated.png");
        await CheckShippingReset();
        var replay = _session.CaptureReplay();
        var verified = CampaignRuntimeReplayRunner.Run(_combat, _adventure, _progression, _campaign, replay);
        Check("earned_campaign_replay_is_identical", verified.Success && verified.FinalHash == _session.StateHash);
        _finalHash = _session.StateHash; _replayHash = verified.FinalHash;
        System.IO.File.WriteAllText(Path.Combine(_output, "spine-audio.awcampaign"), JsonData.Write(replay));
        string save = JsonData.Write(new CampaignRuntimeSave(1, _session.StateHash, _session.Capture()));
        System.IO.File.WriteAllText(Path.Combine(_output, "spine-audio.save.json"), save);
        var restored = CampaignRuntimeSaveStore.Read(_combat, _adventure, _progression, _campaign, save);
        Check("earned_save_restores_identically", restored.StateHash == _session.StateHash);
        int cues = _sandbox.AudioDirector.CueCount;
        _session = restored; _sandbox.SetSession(restored.Combat); _sandbox._Process(1d / 30);
        Check("restoring_defeated_warden_does_not_replay_defeat", _sandbox.AudioDirector.CueCount == cues);
        CheckSpineImpactFixtures();
        CheckExposureFixtures();
        await CheckCapturedWarningFixtures();
        CheckExpiredFollowupFixtures();
        _stage.QueueFree(); _sandbox.QueueFree(); await Frames(3);
    }

    private void CaptureEarnedWarnings(IReadOnlyList<CombatEvent> events, string hash)
    {
        string style = Style();
        if (!Styles.Contains(style)) return;
        var view = _session.Combat.View;
        var audio = _sandbox.AudioDirector;
        foreach (var ev in events)
        {
            var actor = view.Actors.FirstOrDefault(a => a.Id == ev.ActorId);
            string cue = ev.Kind switch
            {
                "CampaignHazardWarned" when ev.ContentId == "rule.fault.1" => "spine_fault",
                "CampaignHazardResolved" when ev.ContentId is "rule.fault.1" or "rule.fault.2" &&
                    (view.CampaignHazards ?? []).Any(h => h.SourceId == ev.ActorId && h.RemainingTicks > 0 &&
                        h.ContentId == (ev.ContentId == "rule.fault.1" ? "rule.fault.2" : "rule.fault.3")) => "spine_fault",
                "CampaignHazardWarned" when ev.ContentId.StartsWith("rule.", StringComparison.Ordinal) => "",
                "CampaignHazardWarned" when actor?.DefinitionId == "boss.covenant_warden" && ev.ContentId == "campaign.oath_mark" => "warden_oath",
                "CampaignHazardResolved" when actor?.DefinitionId == "boss.covenant_warden" && ev.ContentId == "campaign.oath_mark" &&
                    (view.CampaignHazards ?? []).Any(h => h.SourceId == ev.ActorId && h.ContentId == "campaign.covenant_fault" && h.RemainingTicks > 0) => "warden_fault",
                "AbilityStarted" or "EliteAbilityStarted" or "CampaignHazardWarned" => actor?.DefinitionId switch
                {
                    "enemy.oath_giant" => "giant_tell",
                    "enemy.contract_keeper" => "keeper_tell",
                    "enemy.bone_sentinel" => "bone_tell",
                    _ => ""
                },
                _ => ""
            };
            if (cue.Length == 0)
            {
                bool silent = ev.Kind == "CampaignHazardWarned" && (ev.ContentId is "rule.fault.2" or "rule.fault.3" ||
                    ev.ContentId == "campaign.covenant_fault" && actor?.DefinitionId == "boss.covenant_warden") ||
                    ev.Kind == "BossPatternStarted" && actor?.DefinitionId == "boss.covenant_warden";
                string silentKey = "silent_" + ev.Kind + "_" + ev.ContentId;
                if (silent && !_earnedWarnings.ContainsKey(silentKey))
                    _earnedWarnings.Add(silentKey, ("", ev, _session.Combat.Capture(), style));
                continue;
            }
            string key = cue == "spine_fault" && ev.Kind == "CampaignHazardResolved" ? cue + "_after_" + ev.ContentId : cue;
            if (!_earnedWarnings.ContainsKey(key))
                _earnedWarnings.Add(key, (cue, ev, _session.Combat.Capture(), style));
            bool delivered = HasCue(audio, cue);
            if (!delivered) _coalescedWarnings++;
            _route.Add(new { kind = "earned-warning-observation", cue, eventKind = ev.Kind, ev.ContentId, ev.Tick, delivered, stateHash = hash });
            if (cue == "warden_oath")
            {
                var oath = (view.CampaignHazards ?? []).Single(h => h.ContentId == "campaign.oath_mark");
                var fault = (view.CampaignHazards ?? []).Single(h => h.ContentId == "campaign.covenant_fault");
                long elapsed = view.Tick - ev.Tick;
                _route.Add(new
                {
                    kind = "earned-warden-warning-timing",
                    eventTick = ev.Tick,
                    viewTick = view.Tick,
                    oathRemainingTicks = oath.RemainingTicks,
                    faultRemainingTicks = fault.RemainingTicks,
                    oath.SequenceIndex,
                    faultSequenceIndex = fault.SequenceIndex,
                    actor!.Guarded
                });
                // Step emits at the old combat tick, then increments before View.
                Check("earned_warden_announces_oath_before_fault_with_guard", elapsed == 1 &&
                    oath.RemainingTicks + elapsed == 36 && fault.RemainingTicks + elapsed == 54 &&
                    oath.SequenceIndex == 1 && fault.SequenceIndex == 2 && actor.Guarded);
            }
            if (cue == "warden_fault")
            {
                var fault = (view.CampaignHazards ?? []).SingleOrDefault(h => h.ContentId == "campaign.covenant_fault");
                // Death or phase changes can cancel both hazards on the resolution tick.
                if (actor is { Health: > 0 } && fault is not null)
                {
                    long elapsed = view.Tick - ev.Tick;
                    _route.Add(new
                    {
                        kind = "earned-warden-followup-timing",
                        eventTick = ev.Tick,
                        viewTick = view.Tick,
                        faultRemainingTicks = fault.RemainingTicks,
                        actor.Guarded
                    });
                    Check("earned_oath_resolution_warns_eighteen_ticks_before_fault", elapsed == 1 && fault.RemainingTicks + elapsed == 18 && actor.Guarded);
                    _oathResolutions++;
                }
            }
            if (cue == "spine_fault" && ev.Kind == "CampaignHazardWarned" && ev.ContentId == "rule.fault.1" && style is "spine_causeway" or "spine_memory")
            {
                var faults = (view.CampaignHazards ?? []).Where(h => h.ContentId.StartsWith("rule.fault.", StringComparison.Ordinal)).OrderBy(h => h.ContentId).ToArray();
                if (faults.Length != 3) continue;
                bool reverse = style == "spine_memory";
                long elapsed = view.Tick - ev.Tick;
                _route.Add(new
                {
                    kind = "earned-regional-fault-timing",
                    style,
                    eventTick = ev.Tick,
                    viewTick = view.Tick,
                    remainingTicks = faults.Select(h => h.RemainingTicks).ToArray(),
                    laneZ = faults.Select(h => h.Position.Z).ToArray(),
                    reverse
                });
                Check("earned_" + style + "_fault_warning_order", elapsed == 1 &&
                    faults.Select(h => h.RemainingTicks + elapsed).SequenceEqual(new long[] { 30, 50, 70 }) &&
                    (reverse ? faults[0].Position.Z > faults[1].Position.Z && faults[1].Position.Z > faults[2].Position.Z
                        : faults[0].Position.Z < faults[1].Position.Z && faults[1].Position.Z < faults[2].Position.Z));
                _faultSequences.Add(style);
            }
        }
    }

    private async Task CheckCapturedWarningFixtures()
    {
        // Exact public-route events and snapshots, presented on idle warning lanes.
        // This verifies distinct routing independently of intentional live coalescing.
        foreach (var pair in _earnedWarnings.OrderBy(p => p.Key))
        {
            var (cue, ev, snapshot, style) = pair.Value;
            var combat = CombatSession.Restore(_combat, snapshot);
            string liveHash = _session.StateHash, fixtureHash = combat.StateHash;
            var fixture = new Sandbox { ContentJsonOverride = _combat, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => combat };
            AddChild(fixture); fixture.SetProcess(false); fixture.EnableCampaign(); fixture.SetSession(combat);
            try
            {
                fixture.SetEnvironmentStyle(style);
                var audio = fixture.AudioDirector;
                audio.Reset(combat.View); foreach (var voice in audio.WarningPlayers) voice.Stream = null;
                int before = audio.CueCount;
                fixture.PresentCombatEvents([ev], combat);
                Check("captured_earned_" + pair.Key + "_uses_expected_warning_delivery", cue.Length == 0
                    ? audio.CueCount == before && audio.WarningPlayers.All(p => p.Stream is null)
                    : audio.CueCount == before + 1 && ReferenceEquals(audio.WarningPlayers[0].Stream, OpeningFoley.GetStream(cue)) &&
                        audio.WarningPlayers[1].Stream is null);
                if (ev.Kind == "CampaignHazardResolved")
                {
                    audio.Reset(combat.View); audio.Advance(0, combat.View, true, Basis.Identity);
                    foreach (var voice in audio.WarningPlayers) voice.Stream = null;
                    int pausedCount = audio.CueCount;
                    fixture.PresentCombatEvents([ev], combat);
                    Check("captured_" + pair.Key + "_pause_suppresses_followup", audio.CueCount == pausedCount && audio.WarningPlayers.All(p => p.Stream is null));
                    audio.Advance(0, combat.View, false, Basis.Identity);
                    Check("captured_" + pair.Key + "_resume_does_not_queue_followup", audio.CueCount == pausedCount);
                }
                Check("captured_" + pair.Key + "_fixture_preserves_live_and_restored_core", _session.StateHash == liveHash && combat.StateHash == fixtureHash);
                _route.Add(new
                {
                    kind = "captured-earned-event-routing-fixture",
                    eventKind = ev.Kind,
                    ev.Tick,
                    ev.ActorId,
                    ev.ContentId,
                    cue,
                    delivered = audio.CueCount - before,
                    fixtureHash,
                    liveHash
                });
            }
            finally { fixture.QueueFree(); await Frames(2); }
        }
    }

    private void CheckExpiredFollowupFixtures()
    {
        // Previously earned resolution events against the actual completed arena:
        // absent follow-up hazards must not announce attacks that no longer exist.
        var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        foreach (var pair in _earnedWarnings.Where(p => p.Value.Event.Kind == "CampaignHazardResolved"))
        {
            audio.Reset(_session.Combat.View);
            foreach (var voice in audio.WarningPlayers) voice.Stream = null;
            int before = audio.CueCount;
            _sandbox.PresentCombatEvents([pair.Value.Event], _session.Combat);
            Check("expired_" + pair.Key + "_cannot_replay_followup", audio.CueCount == before && audio.WarningPlayers.All(p => p.Stream is null));
        }
        Check("expired_followup_fixtures_preserve_core", _session.StateHash == hash);
    }

    private void CheckSpineImpactFixtures()
    {
        // Explicitly separate presentation-only events, not evidence that the route
        // took these hits. They never enter Core or the campaign replay.
        string hash = _session.StateHash;
        var audio = _sandbox.AudioDirector;
        int warden = _session.Combat.View.Actors.Single(a => a.DefinitionId == "boss.covenant_warden").Id;
        foreach (string content in new[] { "campaign.oath_mark", "campaign.covenant_fault", "campaign.fault", "campaign.oathmark", "rule.fault.1", "rule.fault.2", "rule.fault.3" })
        {
            audio.Reset(_session.Combat.View);
            foreach (var voice in audio.EffectPlayers) voice.Stream = null;
            int before = audio.CueCount;
            _sandbox.PresentCombatEvents([new(_session.Combat.View.Tick, "DamageApplied", warden, 1, 1, content)], _session.Combat);
            Check("fixture_" + content + "_dispatches_one_new_physical_impact", audio.CueCount == before + 1 &&
                audio.EffectPlayers.Count(p => ReferenceEquals(p.Stream, OpeningFoley.GetStream("impact_weapon"))) == 1 &&
                audio.EffectPlayers.All(p => !ReferenceEquals(p.Stream, OpeningFoley.GetStream("impact_spell"))) &&
                _session.StateHash == hash);
            _route.Add(new { kind = "read-only-impact-fixture", content, cue = audio.LastCue, delivered = audio.CueCount - before, stateHash = hash });
        }
        audio.Reset(_session.Combat.View);
    }

    private void CheckExposureFixtures()
    {
        // Views copied from a real earned living boss are detached presentation
        // fixtures. No fabricated guard, death or tick enters the campaign.
        var live = _liveWarden ?? throw new InvalidDataException("No living warden view earned.");
        var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        CombatView Project(bool guarded, bool dead = false, long ticks = 0) => live with
        {
            Tick = live.Tick + ticks,
            Actors = live.Actors.Select(a => a.DefinitionId == "boss.covenant_warden"
                ? a with { Guarded = guarded, Health = dead ? 0 : a.Health } : a).ToArray()
        };
        var guard = Project(true); var exposed = Project(false, ticks: 1);
        audio.Reset(exposed); int count = audio.CueCount;
        audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_load_of_exposed_warden_is_silent", audio.CueCount == count);
        audio.Reset(guard); count = audio.CueCount;
        audio.Observe(exposed);
        Check("fixture_real_guard_to_exposed_view_delivers_once", audio.CueCount == count + 1 && audio.LastCue == "warden_exposed");
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
        count = audio.CueCount; audio.SetStyle("spine_warden"); audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_room_reset_does_not_replay_exposure", audio.CueCount == count);
        Check("exposure_fixtures_preserve_earned_core", hash == _session.StateHash);
        audio.Reset(_session.Combat.View);
    }

    private async Task CheckShippingReset()
    {
        var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        var ambience = _sandbox.GetChildren().OfType<AudioStreamPlayer>().Single(p => p.Name == "RegionalAmbience");
        float busVolume = ClientAudio.GetVolume(ClientAudio.MusicBus);
        audio.Play("warden_defeat", Vector3.Zero);
        for (int i = 0; i < 8; i++) _sandbox._Process(1d / 60);
        Check("shipping_spine_ambience_ducks_for_warning_without_changing_settings", ambience.Bus == ClientAudio.MusicBus &&
            ambience.VolumeDb < -39 && ClientAudio.GetVolume(ClientAudio.MusicBus) == busVolume);
        _sandbox.SetModalPaused("spine-audio-diagnostic", true); _sandbox._Process(1d / 30);
        var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts);
        for (int i = 0; i < 12; i++) { _sandbox._Process(1d / 30); await Frames(1); }
        Check("shipping_pause_freezes_spine_audio_and_core", _sandbox.IsPaused && audio.Paused && hash == _session.StateHash &&
            frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts));
        _sandbox.SetModalPaused("spine-audio-diagnostic", false); _sandbox._Process(1d / 30);
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
        _sandbox.PresentAuthoredRoom(_session.Room, "spine-audio:" + _session.ActiveEncounterId, style);
        _sandbox.SetEnvironmentStyle(style);
        _stage.Show(state, _session.View, _session.Room, _session.Interactions, _session.Production.View.ActiveManifestations,
            _session.Production.ProgressionView.HubStage, view.Actors.Single(a => a.Id == 1).Position, view.BossPhase,
            combat: view, activeEncounterId: _session.ActiveEncounterId);
        _sandbox.SetWorldSubtitle("SPINE AUDIO / " + style.ToUpperInvariant());
    }
    private async Task Capture(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture-spine-audio")) return;
        if (DisplayServer.GetName() == "headless") { if (!_skipped.Contains("native screenshots")) _skipped.Add("native screenshots"); return; }
        await Frames(2); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.GetHeight() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Spine audio check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "SpineAudioClientSmokePassed" : "SpineAudioClientSmokeFailed",
            passed,
            checks = _checks,
            route = _route,
            styles = _styles.Order().ToArray(),
            cues = _cues.Order().ToArray(),
            wardenPhases = _phases.Order().ToArray(),
            wardenStates = _wardenStates.Order().ToArray(),
            exposureTransitions = _exposures,
            coalescedWarnings = _coalescedWarnings,
            faultSequenceStyles = _faultSequences.Order().ToArray(),
            oathResolutions = _oathResolutions,
            commands = _commands,
            finalHash = _finalHash,
            replayHash = _replayHash,
            captures = _captures.Order().ToArray(),
            skippedChecks = _skipped,
            error,
            scope = "Fresh public campaign commands earn Acts I–IV, including the Oathkeeper’s Archive, Divine Memory, both Warden phases, actual guarded-to-exposed transitions and defeat. Sandbox presents actual earned events, while overlapping creature warnings intentionally coalesce. Separate detached shipping Sandboxes replay exact captured earned warnings against their captured combat states to verify distinct routing with idle warning lanes. Read-only mixer and projected-view fixtures exercise warning priority, bounded voices, bus muting, pause, load, room departure and physical impact routing. PCM and audition coverage is provided by OpeningAudioSmoke's expanded catalog. Faster-than-wall-time command simulation is not a human playtest, hearing-quality assessment or audio loopback recording. No player saves or preferences are used."
        };
        try
        {
            string json = JsonData.Write(report);
            if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "spine-audio-review.json"), json);
            GD.Print(json);
        }
        catch (Exception ex) { passed = false; GD.PushError("Spine audio report failed: " + ex); }
        finally { GetTree().Quit(passed ? 0 : 1); }
    }
}
