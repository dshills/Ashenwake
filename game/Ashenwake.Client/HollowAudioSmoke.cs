using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Earned Act V audio routing, with explicitly separate read-only mixer fixtures.</summary>
public partial class HollowAudioSmoke : Node
{
    private static readonly string[] Styles = ["hollow_rooms", "hollow_memory", "hollow_vault", "hollow_breach"];
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<object> _route = [];
    private readonly HashSet<string> _styles = [], _cues = [], _captures = [];
    private readonly HashSet<int> _phases = [];
    private readonly HashSet<string> _heartStates = [];
    private int _exposures, _coalescedWarnings, _followups, _sealDeaths;
    private CombatView? _liveHeart;
    private CombatSnapshot? _heartSnapshot;
    private readonly Dictionary<string, (string Cue, CombatEvent Event, CombatSnapshot Snapshot, string Style)> _earnedWarnings = [];
    private readonly List<string> _skipped = [];
    private CampaignContent _campaign = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private CampaignRuntimeSession _session = null!;
    private Sandbox _sandbox = null!, _fixtureSandbox = null!;
    private CombatSession _fixtureCombat = null!;
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
            if (!args.Contains("--hollow-audio-smoke") || _output.Length == 0)
                throw new InvalidDataException("Hollow audio smoke requires --hollow-audio-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Hollow audio smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); _writeReport = true;
            Engine.MaxFps = 60; GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _combat = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign")); _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combat, _adventure, _progression, _campaign);
            await CheckMixerFixtures();
            await EarnHollow();
            await ReleasePresentation();
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
            // The third-phase chain announces echo, sweep after one second, then
            // return two thirds of a second later, without resetting the warning lane.
            audio.Reset(hub); int sequenceStart = audio.CueCount;
            audio.Play("breach_echo", Vector3.Zero);
            Pump(audio, hub, 60); audio.Play("breach_sweep", Vector3.Zero);
            Pump(audio, hub, 40); audio.Play("breach_return", Vector3.Zero);
            Check("breach_sequence_delivers_three_distinct_beats_at_authored_cadence", audio.CueCount == sequenceStart + 3 && HasCue(audio, "breach_return"));
            audio.Reset(hub); audio.Play("shadow_tell", Vector3.Zero); audio.Play("breach_echo", Vector3.Zero);
            audio.Play("breach_phase2", Vector3.Zero); int count = audio.CueCount;
            for (int i = 0; i < 200; i++)
            {
                foreach (string cue in new[] { "echo_tell", "causal_tell", "shadow_tell", "breach_return", "low_health", "impact_weapon", "breach_exposed", "step_stone_1" })
                    audio.Play(cue, Vector3.Zero);
            }
            Check("breach_warning_survives_lesser_tells_and_dense_foley", HasCue(audio, "breach_echo") && HasCue(audio, "breach_phase2") && audio.CueCount - count <= 2);
            Pump(audio, hub, 8);
            Check("breach_phase_ducks_music", audio.DuckGain <= .27f);
            audio.Play("breach_containment", Vector3.Zero);
            Pump(audio, hub, 24); audio.Play("breach_phase2", Vector3.Zero); audio.Play("breach_exposed", Vector3.Zero); audio.Play("low_health", Vector3.Zero);
            Check("breach_victory_reserves_critical_voice_over_phase_exposure_and_heartbeat", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("breach_containment")));
            Pump(audio, hub, 240);
            Check("hollow_warning_tail_releases_duck", audio.DuckGain > .99f);
            var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount);
            audio.Advance(0, hub, true, Basis.Identity);
            for (int i = 0; i < 60; i++) { audio.Advance(.1, hub, true, Basis.Identity); audio.Play("breach_containment", Vector3.Zero); }
            Check("hollow_pause_freezes_envelopes_and_rejects_new_transients", frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount));
            audio.Advance(0, hub, false, Basis.Identity);
            await CheckNativePlayback(audio, hub);
            int starts = audio.BankStarts;
            ClientAudio.SetVolume(ClientAudio.MusicBus, 0); ClientAudio.SetVolume(ClientAudio.EffectsBus, 0);
            audio.Play("breach_return", Vector3.Zero); Pump(audio, hub, 20);
            Check("hollow_mute_uses_settings_buses_without_restarting_score", AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.MusicBus)) &&
                AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.EffectsBus)) && audio.BankStarts == starts);
            foreach (var pair in levels) ClientAudio.SetVolume(pair.Key, pair.Value);
            count = audio.CueCount;
            audio.SetStyle("audio_unscored_fixture");
            Pump(audio, hub, 130);
            Check("departure_releases_hollow_banks_and_transients", audio.ActiveMusicBanks == 0 && audio.DesiredStyle == "" && audio.CueCount == count &&
                audio.EffectPlayers.All(p => !p.Playing) && audio.WarningPlayers.All(p => !p.Playing));
            await SettleStyle(audio, hub, "hollow_rooms");
            Check("return_to_hollow_does_not_replay_one_shots", audio.CueCount == count && audio.BankStarts == starts + 1);
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
        Check("native_hollow_stems_play_and_advance_together", active.Length == 3 && active.All(p => p.Playing) &&
            after.Zip(before).All(p => p.First - p.Second > .05) && after.Max() - after.Min() <= .05);
        audio.Advance(0, view, true, Basis.Identity); await Frames(2);
        double[] paused = active.Select(p => (double)p.GetPlaybackPosition()).ToArray(); await Frames(12);
        double[] held = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        Check("native_hollow_pause_holds_score_clock", held.Zip(paused).All(p => Math.Abs(p.First - p.Second) < .01));
        audio.Advance(0, view, false, Basis.Identity); await Frames(12);
        double[] resumed = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        Check("native_hollow_resume_continues_score_clock", resumed.Zip(held).All(p => p.First - p.Second > .05));
        _route.Add(new { kind = "native-playheads", before, after, paused, held, resumed });
    }

    private static CombatActorView? Primary(CombatView view) => view.Actors
        .Where(a => a.DefinitionId == "boss.breach_heart").OrderBy(a => a.Id).FirstOrDefault();

    private async Task EarnHollow()
    {
        _sandbox = new Sandbox { ContentJsonOverride = _combat, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
        AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
        _stage = new CampaignStage(); AddChild(_stage);
        Refresh(); string room = _session.ActiveEncounterId;
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !CampaignRuntimeSmoke.Complete(_session); i++)
        {
            long tick = _session.Tick; var previous = Primary(_session.Combat.View);
            var result = _session.Execute(CampaignRuntimeSmoke.Next(_session)); _commands++;
            if (!result.Success) throw new InvalidDataException("Earned Hollow audio route failed: " + result.Reason);
            if (_session.Capture().Campaign.Deaths > 0) throw new InvalidDataException("Earned Hollow audio route died.");
            string hash = _session.StateHash; var audio = _sandbox.AudioDirector;
            if (room != _session.ActiveEncounterId)
            {
                room = _session.ActiveEncounterId; Refresh(); string style = Style();
                if (Styles.Contains(style))
                {
                    await SettleStyle(audio, _session.Combat.View, style); _styles.Add(style);
                    Check("earned_" + style + "_selects_regional_score", audio.DesiredStyle == style && audio.PlayingStyle == style);
                    _route.Add(new { kind = "earned-room", room, style, tick = _session.Tick, audio.BankStarts, audio.Mode, stateHash = hash });
                    await Capture("hollow-audio-" + style + ".png");
                }
            }
            _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat);
            _sandbox._Process(_session.Tick > tick ? 1d / 30 : 0);
            Check("earned_presentation_preserves_core", _session.StateHash == hash);
            foreach (var cue in OpeningFoley.Cues) if (HasCue(audio, cue.Id)) _cues.Add(cue.Id);
            CaptureEarnedWarnings(result.CombatEvents, hash);
            if (_session.ActiveEncounterId == "campaign.breach_heart")
            {
                var view = _session.Combat.View;
                var heart = Primary(view) ?? throw new InvalidDataException("Breach arena is missing its primary Heart.");
                _phases.Add(view.BossPhase);
                foreach (var change in result.CombatEvents.Where(e => e.Kind == "BossPhaseChanged" && e.ActorId == heart.Id))
                {
                    string cue = "breach_phase" + change.Amount;
                    Check("earned_heart_phase_" + change.Amount + "_uses_critical_voice", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream(cue)));
                    _route.Add(new { kind = "earned-phase", phase = change.Amount, tick = _session.Tick, audio.BossGain, stateHash = hash });
                    Refresh(); await Capture("hollow-audio-heart-phase" + change.Amount + ".png");
                }
                foreach (var death in result.CombatEvents.Where(e => e.Kind is "EntityKilled" or "MechanismDestroyed" &&
                    view.Actors.Any(a => a.Id == e.TargetId && a.DefinitionId == "enemy.seal_channel")))
                {
                    _sealDeaths++;
                    Check("earned_seal_death_" + death.TargetId + "_uses_effect_voice", HasCue(audio, "seal_broken"));
                }
                if (heart.Health > 0)
                {
                    _liveHeart = view; _heartSnapshot = _session.Combat.Capture();
                    if (previous is { Shielded: true, Health: > 0 } && previous.Id == heart.Id && !heart.Shielded)
                    {
                        _exposures++;
                        _route.Add(new
                        {
                            kind = "earned-shield-loss",
                            phase = view.BossPhase,
                            tick = view.Tick,
                            cue = audio.LastCue,
                            criticalStreamIsExposure = HasCue(audio, "breach_exposed"),
                            stateHash = hash
                        });
                    }
                    string key = $"{view.BossPhase}:{heart.Shielded}";
                    if (_heartStates.Add(key))
                    {
                        int starts = audio.BankStarts; Pump(audio, view, 150);
                        float expected = view.BossPhase >= 3 ? 1f : view.BossPhase == 2 ? .8f : heart.Shielded ? .4f : .6f;
                        Check("earned_heart_stem_tracks_" + key, audio.Mode == "boss" && Math.Abs(audio.BossGain - expected) < .001f && audio.BankStarts == starts);
                        Check("earned_heart_music_preserves_core_" + key, _session.StateHash == hash);
                        Refresh(); await Capture("hollow-audio-heart-" + view.BossPhase + (heart.Shielded ? "-shielded.png" : "-exposed.png"));
                    }
                }
                if (result.CombatEvents.Any(e => e.Kind == "EntityKilled" && e.TargetId == heart.Id))
                {
                    _victory = true;
                    Check("earned_primary_heart_death_plays_containment", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("breach_containment")));
                    _route.Add(new { kind = "earned-containment", tick = _session.Tick, stateHash = hash });
                    Refresh(); Pump(audio, view, 480);
                    Check("defeated_primary_heart_releases_boss_stem", audio.BossGain == 0);
                    await Capture("hollow-audio-heart-contained.png");
                    await CheckShippingReset();
                }
            }
            if (i % 30 == 0) { Refresh(); await Frames(1); }
        }
        var final = _session.Capture();
        Check("route_earns_all_five_acts_vault_ending_and_return", CampaignRuntimeSmoke.Complete(_session) &&
            final.Campaign.CompletedExploration.Contains(CampaignRuntimeSession.VaultEvent) && _session.View.Ending is not null);
        Check("earned_route_hears_all_four_hollow_scores", Styles.All(_styles.Contains));
        Check("earned_route_captures_distinct_hollow_warnings", new[] { "shadow_tell", "echo_tell", "causal_tell", "breach_echo", "breach_sweep", "breach_return" }.All(_earnedWarnings.ContainsKey));
        Check("earned_route_observes_all_phases_seal_loss_and_containment", new[] { 1, 2, 3 }.All(_phases.Contains) && _exposures > 0 && _victory && _sealDeaths >= 1);
        Check("earned_route_hears_heart_phase_exposure_and_containment", new[] { "breach_phase2", "breach_phase3", "breach_exposed", "breach_containment", "seal_broken" }.All(_cues.Contains));
        Check("earned_route_observes_shielded_exposed_and_later_phase_music", new[] { "1:True", "1:False", "2:False", "3:False" }.All(_heartStates.Contains));
        Check("earned_route_delivers_breach_followup_sequences", _followups > 0);
        Refresh(); await Capture("hollow-audio-final-return.png");
        var replay = _session.CaptureReplay();
        var verified = CampaignRuntimeReplayRunner.Run(_combat, _adventure, _progression, _campaign, replay);
        Check("earned_campaign_replay_is_identical", verified.Success && verified.FinalHash == _session.StateHash);
        _finalHash = _session.StateHash; _replayHash = verified.FinalHash;
        System.IO.File.WriteAllText(Path.Combine(_output, "hollow-audio.awcampaign"), JsonData.Write(replay));
        string save = JsonData.Write(new CampaignRuntimeSave(1, _session.StateHash, final));
        System.IO.File.WriteAllText(Path.Combine(_output, "hollow-audio.save.json"), save);
        var restored = CampaignRuntimeSaveStore.Read(_combat, _adventure, _progression, _campaign, save);
        Check("earned_save_restores_identically", restored.StateHash == _session.StateHash);
        int cues = _sandbox.AudioDirector.CueCount;
        _session = restored; _sandbox.SetSession(restored.Combat); _sandbox._Process(1d / 30);
        Check("restoring_completed_campaign_does_not_replay_containment", _sandbox.AudioDirector.CueCount == cues);
        CheckHollowImpactFixtures(); CheckExposureAndPrimaryFixtures();
        await CreateFixtureSandbox();
        await CheckCapturedWarningFixtures(); await CheckCloneDeathFixtures(); CheckExpiredFollowupFixtures();
    }

    private async Task ReleasePresentation()
    {
        // Finish the final fixture's rendering before releasing its scene resources.
        if (DisplayServer.GetName() != "headless")
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        foreach (var child in GetChildren()) child.QueueFree();
        _stage = null!; _sandbox = null!; _fixtureSandbox = null!; _fixtureCombat = null!;
        await Frames(3);
    }

    private string ExpectedWarning(CombatEvent ev, CombatView view)
    {
        var actor = view.Actors.FirstOrDefault(a => a.Id == ev.ActorId); var heart = Primary(view);
        bool primary = actor?.Id == heart?.Id && heart is not null;
        bool Future(string content) => (view.CampaignHazards ?? []).Any(h => h.SourceId == ev.ActorId && h.ContentId == content && h.RemainingTicks > 0);
        if (ev.Kind == "CampaignHazardResolved" && primary && actor is { Health: > 0 })
            return ev.ContentId == "campaign.breach_echo" && Future("campaign.seal_sweep") ? "breach_sweep" :
                ev.ContentId is "campaign.breach_echo" or "campaign.seal_sweep" && Future("campaign.returning_echo") ? "breach_return" : "";
        if (ev.Kind is not ("CampaignHazardWarned" or "AbilityStarted" or "EliteAbilityStarted" or "BossPatternStarted")) return "";
        if (ev.ContentId == "rule.causalechoes") return "causal_tell";
        if (ev.ContentId.StartsWith("rule.", StringComparison.Ordinal)) return "";
        if (primary) return ev.ContentId == "campaign.breach_echo" ? "breach_echo" : "";
        return actor?.DefinitionId switch { "enemy.doubled_shadow" => "shadow_tell", "enemy.breach_echo" or "boss.breach_heart" => "echo_tell", _ => "" };
    }

    private void CaptureEarnedWarnings(IReadOnlyList<CombatEvent> events, string hash)
    {
        string style = Style(); if (!Styles.Contains(style)) return;
        var view = _session.Combat.View; var audio = _sandbox.AudioDirector; var heart = Primary(view);
        foreach (var ev in events)
        {
            string cue = ExpectedWarning(ev, view);
            bool silent = ev.ActorId == heart?.Id && (ev.Kind == "BossPatternStarted" ||
                ev.Kind == "CampaignHazardWarned" && ev.ContentId is "campaign.returning_echo" or "campaign.seal_sweep");
            if (cue.Length == 0 && !silent) continue;
            string key = silent ? "silent_" + ev.Kind + "_" + ev.ContentId : cue;
            if (!_earnedWarnings.ContainsKey(key)) _earnedWarnings.Add(key, (cue, ev, _session.Combat.Capture(), style));
            bool delivered = cue.Length > 0 && HasCue(audio, cue); if (cue.Length > 0 && !delivered) _coalescedWarnings++;
            _route.Add(new { kind = "earned-warning-observation", cue, eventKind = ev.Kind, ev.ContentId, ev.Tick, delivered, stateHash = hash });
            if (ev.Kind == "CampaignHazardResolved" && cue.Length > 0) _followups++;
            if (cue == "breach_echo")
            {
                var hazards = (view.CampaignHazards ?? []).Where(h => h.SourceId == heart!.Id).ToArray();
                long elapsed = view.Tick - ev.Tick;
                foreach (var hazard in hazards.Where(h => h.ContentId is "campaign.breach_echo" or "campaign.returning_echo" or "campaign.seal_sweep"))
                {
                    int expected = hazard.ContentId == "campaign.breach_echo" ? 30 : hazard.ContentId == "campaign.returning_echo" ? 70 : 50;
                    Check("earned_phase_" + view.BossPhase + "_" + hazard.ContentId + "_authored_warning_time", elapsed == 1 && hazard.RemainingTicks + elapsed == expected);
                }
                _route.Add(new
                {
                    kind = "earned-breach-sequence-timing",
                    phase = view.BossPhase,
                    eventTick = ev.Tick,
                    viewTick = view.Tick,
                    hazards = hazards.Select(h => new { h.ContentId, h.RemainingTicks, h.SourceId }).ToArray()
                });
            }
        }
    }

    private async Task CreateFixtureSandbox()
    {
        // A capture continuation can run inside FramePostDraw before Godot advances
        // its drawn-frame counter. Keep one fixture world alive across all cases,
        // and let it actually draw before any routing check or teardown.
        _fixtureCombat = _session.Combat;
        _fixtureSandbox = new Sandbox { ContentJsonOverride = _combat, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _fixtureCombat };
        AddChild(_fixtureSandbox); _fixtureSandbox.SetProcess(false); _fixtureSandbox.EnableCampaign();
        _fixtureSandbox.SetSession(_fixtureCombat);
        await Frames(2);
        if (DisplayServer.GetName() != "headless")
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private Sandbox PrepareFixture(CombatSession combat)
    {
        _fixtureCombat = combat;
        _fixtureSandbox.SetSession(combat);
        return _fixtureSandbox;
    }

    private async Task CheckCapturedWarningFixtures()
    {
        foreach (var pair in _earnedWarnings.OrderBy(p => p.Key))
        {
            var (cue, ev, snapshot, style) = pair.Value; var combat = CombatSession.Restore(_combat, snapshot);
            string liveHash = _session.StateHash, fixtureHash = combat.StateHash;
            var fixture = PrepareFixture(combat);
            try
            {
                fixture.SetEnvironmentStyle(style); var audio = fixture.AudioDirector;
                audio.Reset(combat.View); foreach (var voice in audio.WarningPlayers) voice.Stream = null;
                int before = audio.CueCount; fixture.PresentCombatEvents([ev], combat);
                Check("captured_earned_" + pair.Key + "_uses_expected_warning_delivery", cue.Length == 0 ?
                    audio.CueCount == before && audio.WarningPlayers.All(p => p.Stream is null) :
                    audio.CueCount == before + 1 && ReferenceEquals(audio.WarningPlayers[0].Stream, OpeningFoley.GetStream(cue)) && audio.WarningPlayers[1].Stream is null);
                if (ev.Kind == "CampaignHazardResolved")
                {
                    audio.Reset(combat.View); audio.Advance(0, combat.View, true, Basis.Identity);
                    foreach (var voice in audio.WarningPlayers) voice.Stream = null;
                    int count = audio.CueCount; fixture.PresentCombatEvents([ev], combat);
                    Check("captured_" + pair.Key + "_pause_suppresses_followup", audio.CueCount == count && audio.WarningPlayers.All(p => p.Stream is null));
                    audio.Advance(0, combat.View, false, Basis.Identity);
                    Check("captured_" + pair.Key + "_resume_does_not_queue_followup", audio.CueCount == count);
                }
                Check("captured_" + pair.Key + "_fixture_preserves_live_and_restored_core", _session.StateHash == liveHash && combat.StateHash == fixtureHash);
                _route.Add(new { kind = "captured-earned-event-routing-fixture", eventKind = ev.Kind, ev.Tick, ev.ActorId, ev.ContentId, cue, delivered = audio.CueCount - before, fixtureHash, liveHash });
            }
            finally { await Frames(2); }
            if (ev.Kind == "CampaignHazardResolved")
                await CheckCancelledFollowupFixture(pair.Key, ev, snapshot, style);
        }
    }

    private async Task CheckCancelledFollowupFixture(string key, CombatEvent ev, CombatSnapshot earned, string style)
    {
        // Detached projection of an earned state: the primary and player remain
        // alive, but the pending sequence is cancelled. No command enters Core.
        var snapshot = JsonData.Copy(earned);
        int originalHazards = earned.Campaign!.Hazards.Count;
        int removed = snapshot.Campaign!.Hazards.RemoveAll(h => h.SourceId == ev.ActorId && h.ResolveTick > snapshot.Tick);
        var combat = CombatSession.Restore(_combat, snapshot);
        string fixtureHash = combat.StateHash, liveHash = _session.StateHash;
        Check("cancelled_" + key + "_fixture_keeps_primary_and_player_alive", removed > 0 &&
            Primary(combat.View) is { Health: > 0 } primary && primary.Id == ev.ActorId &&
            combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0) && earned.Campaign.Hazards.Count == originalHazards);
        var fixture = PrepareFixture(combat);
        try
        {
            fixture.SetEnvironmentStyle(style); var audio = fixture.AudioDirector;
            audio.Reset(combat.View); foreach (var voice in audio.WarningPlayers) voice.Stream = null;
            int before = audio.CueCount;
            fixture.PresentCombatEvents([ev], combat);
            Check("cancelled_" + key + "_cannot_warn_for_absent_future_hazard", audio.CueCount == before && audio.WarningPlayers.All(p => p.Stream is null));
            Check("cancelled_" + key + "_fixture_preserves_live_and_detached_core", combat.StateHash == fixtureHash && _session.StateHash == liveHash);
            _route.Add(new
            {
                kind = "cancelled-hazard-routing-fixture",
                eventKind = ev.Kind,
                ev.ActorId,
                ev.ContentId,
                removedFutureHazards = removed,
                delivered = audio.CueCount - before,
                fixtureHash,
                liveHash
            });
        }
        finally { await Frames(2); }
    }

    private void CheckExpiredFollowupFixtures()
    {
        var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        _sandbox.SetEnvironmentStyle("hollow_breach");
        foreach (var pair in _earnedWarnings.Where(p => p.Value.Event.Kind == "CampaignHazardResolved"))
        {
            audio.Reset(_session.Combat.View); foreach (var voice in audio.WarningPlayers) voice.Stream = null;
            int before = audio.CueCount; _sandbox.PresentCombatEvents([pair.Value.Event], _session.Combat);
            Check("expired_" + pair.Key + "_cannot_replay_followup", audio.CueCount == before && audio.WarningPlayers.All(p => p.Stream is null));
        }
        Check("expired_followup_fixtures_preserve_core", _session.StateHash == hash);
    }

    private void CheckHollowImpactFixtures()
    {
        string hash = _session.StateHash; var audio = _sandbox.AudioDirector;
        foreach (string content in new[] { "campaign.shadowdouble", "campaign.causalecho", "campaign.memoryarrow", "rule.causalechoes", "campaign.breach_echo", "campaign.returning_echo", "campaign.seal_sweep" })
        {
            audio.Reset(_session.Combat.View); foreach (var voice in audio.EffectPlayers) voice.Stream = null;
            int before = audio.CueCount;
            _sandbox.PresentCombatEvents([new(_session.Combat.View.Tick, "DamageApplied", 1, 1, 1, content)], _session.Combat);
            Check("fixture_" + content + "_dispatches_one_new_void_impact", audio.CueCount == before + 1 &&
                audio.EffectPlayers.Count(p => ReferenceEquals(p.Stream, OpeningFoley.GetStream("impact_spell"))) == 1 &&
                audio.EffectPlayers.All(p => !ReferenceEquals(p.Stream, OpeningFoley.GetStream("impact_weapon"))) && _session.StateHash == hash);
        }
        audio.Reset(_session.Combat.View);
    }

    private void CheckExposureAndPrimaryFixtures()
    {
        var live = _liveHeart ?? throw new InvalidDataException("No living primary Heart view earned.");
        var primary = Primary(live)!; var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        _sandbox.SetEnvironmentStyle("hollow_breach");
        CombatView Project(bool shielded, bool dead = false, long ticks = 0, int phase = 1) => live with
        {
            Tick = live.Tick + ticks,
            BossPhase = phase,
            Actors = live.Actors.Select(a => a.Id == primary.Id ? a with { Shielded = shielded, Health = dead ? 0 : a.Health } : a).ToArray()
        };
        var shield = Project(true); var exposed = Project(false, ticks: 1);
        audio.Reset(exposed); int count = audio.CueCount; audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_load_of_exposed_heart_is_silent", audio.CueCount == count);
        audio.Reset(shield); count = audio.CueCount; audio.Observe(exposed);
        Check("fixture_real_shield_loss_delivers_exposure_once", audio.CueCount == count + 1 && audio.LastCue == "breach_exposed");
        audio.Observe(exposed); audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_refresh_does_not_repeat_exposure", audio.CueCount == count + 1);
        audio.Reset(shield); count = audio.CueCount; audio.Observe(Project(false, dead: true, ticks: 1));
        Check("fixture_shielded_death_cannot_emit_exposure", audio.CueCount == count);
        audio.Reset(Project(false, phase: 2)); count = audio.CueCount; audio.Observe(Project(false, ticks: 1, phase: 2));
        Check("fixture_restored_later_phase_cannot_replay_exposure", audio.CueCount == count);
        audio.Reset(shield); audio.Advance(0, shield, true, Basis.Identity); count = audio.CueCount;
        audio.Reset(exposed); audio.Observe(exposed); audio.Advance(0, exposed, false, Basis.Identity); audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_paused_load_does_not_replay_exposure_on_resume", audio.CueCount == count);
        audio.Reset(shield); audio.SetStyle("audio_unscored_fixture"); audio.Observe(exposed);
        count = audio.CueCount; audio.SetStyle("hollow_breach"); audio.Observe(exposed with { Tick = exposed.Tick + 1 });
        Check("fixture_room_reset_does_not_replay_exposure", audio.CueCount == count);
        var clone = primary with { Id = live.Actors.Max(a => a.Id) + 1, Health = primary.MaxHealth, Shielded = false };
        var reordered = shield with { Actors = new[] { clone }.Concat(shield.Actors.Reverse()).ToArray() };
        audio.Reset(reordered); Pump(audio, reordered, 150);
        Check("fixture_reordered_mirrorborn_does_not_replace_shielded_primary_music", Math.Abs(audio.BossGain - .4f) < .001f);
        var deadPrimary = reordered with { Actors = reordered.Actors.Select(a => a.Id == primary.Id ? a with { Health = 0 } : a).ToArray() };
        audio.Reset(deadPrimary); Pump(audio, deadPrimary, 480);
        Check("fixture_living_mirrorborn_cannot_inherit_dead_primary_boss_stem", audio.BossGain == 0);
        audio.Reset(reordered); count = audio.CueCount;
        audio.Observe(reordered with { Tick = reordered.Tick + 1, Actors = reordered.Actors.Select(a => a.Id == clone.Id ? a with { Shielded = true } : a).ToArray() });
        audio.Observe(reordered with { Tick = reordered.Tick + 2 });
        Check("fixture_mirrorborn_shield_changes_cannot_emit_primary_exposure", audio.CueCount == count);
        Check("exposure_and_primary_fixtures_preserve_earned_core", hash == _session.StateHash);
        audio.Reset(_session.Combat.View);
    }

    private async Task CheckCloneDeathFixtures()
    {
        // Detached mutable snapshots are presentation fixtures only. They never
        // enter the earned campaign save or replay and contain no invented command.
        var snapshot = JsonData.Read<CombatSnapshot>(JsonData.Write(_heartSnapshot!));
        int primary = snapshot.Actors.Where(a => a.DefinitionId == "boss.breach_heart").Min(a => a.Id);
        var original = snapshot.Actors.Single(a => a.Id == primary);
        int cloneId = snapshot.Actors.Max(a => a.Id) + 1;
        snapshot.Actors.Add(original with { Id = cloneId, Health = 0, DeathProcessed = true, Pending = null });
        snapshot.NextActorId = cloneId + 1;
        snapshot.Campaign!.Actors.Add(cloneId, new() { IsEcho = true, ExpiresTick = snapshot.Tick + 180, NextEliteTick = snapshot.Tick + 30 });
        var combat = CombatSession.Restore(_combat, snapshot); string hash = combat.StateHash, liveHash = _session.StateHash;
        var fixture = PrepareFixture(combat); fixture.SetEnvironmentStyle("hollow_breach");
        try
        {
            var audio = fixture.AudioDirector; audio.Reset(combat.View); foreach (var voice in audio.WarningPlayers) voice.Stream = null;
            fixture.PresentCombatEvents([new(combat.Tick, "EliteCopyKilled", 1, cloneId, 0, "boss.breach_heart")], combat);
            Check("fixture_mirrorborn_death_cannot_play_containment", !HasCue(audio, "breach_containment"));
            audio.Reset(combat.View); foreach (var voice in audio.WarningPlayers) voice.Stream = null;
            fixture.PresentCombatEvents([new(combat.Tick, "BossPhaseChanged", cloneId, 0, 3, "boss.breach_heart")], combat);
            Check("fixture_mirrorborn_phase_cannot_play_primary_phase_cue", !HasCue(audio, "breach_phase3"));
            Check("clone_death_and_phase_fixtures_preserve_live_and_detached_core", combat.StateHash == hash && _session.StateHash == liveHash);
        }
        finally { await Frames(2); }
    }

    private async Task CheckShippingReset()
    {
        var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        var ambience = _sandbox.GetChildren().OfType<AudioStreamPlayer>().Single(p => p.Name == "RegionalAmbience");
        float busVolume = ClientAudio.GetVolume(ClientAudio.MusicBus);
        audio.Play("breach_containment", Vector3.Zero);
        for (int i = 0; i < 8; i++) _sandbox._Process(1d / 60);
        Check("shipping_hollow_ambience_ducks_for_warning_without_changing_settings", ambience.Bus == ClientAudio.MusicBus && ambience.VolumeDb < -39 && ClientAudio.GetVolume(ClientAudio.MusicBus) == busVolume);
        _sandbox.SetModalPaused("hollow-audio-diagnostic", true); _sandbox._Process(1d / 30);
        var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts);
        for (int i = 0; i < 12; i++) { _sandbox._Process(1d / 30); await Frames(1); }
        Check("shipping_pause_freezes_hollow_audio_and_core", _sandbox.IsPaused && audio.Paused && hash == _session.StateHash && frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts));
        _sandbox.SetModalPaused("hollow-audio-diagnostic", false); _sandbox._Process(1d / 30);
        int starts = audio.BankStarts, cues = audio.CueCount;
        _sandbox.SetSession(_session.Combat); Refresh(); _sandbox._Process(1d / 30);
        Check("shipping_reset_clears_tails_without_replaying_containment_or_score", !audio.Paused && audio.BankStarts == starts && audio.CueCount == cues && audio.EffectPlayers.All(p => !p.Playing) && audio.WarningPlayers.All(p => !p.Playing) && hash == _session.StateHash);
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
        _sandbox.PresentAuthoredRoom(_session.Room, "hollow-audio:" + _session.ActiveEncounterId, style);
        _sandbox.SetEnvironmentStyle(style);
        _stage.Show(state, _session.View, _session.Room, _session.Interactions, _session.Production.View.ActiveManifestations,
            _session.Production.ProgressionView.HubStage, view.Actors.Single(a => a.Id == 1).Position, view.BossPhase,
            combat: view, activeEncounterId: _session.ActiveEncounterId);
        _sandbox.SetWorldSubtitle("HOLLOW AUDIO / " + style.ToUpperInvariant());
    }
    private async Task Capture(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture-hollow-audio")) return;
        if (DisplayServer.GetName() == "headless") { if (!_skipped.Contains("native screenshots")) _skipped.Add("native screenshots"); return; }
        await Frames(2); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.GetHeight() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Hollow audio check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "HollowAudioClientSmokePassed" : "HollowAudioClientSmokeFailed",
            passed,
            checks = _checks,
            route = _route,
            styles = _styles.Order().ToArray(),
            cues = _cues.Order().ToArray(),
            heartPhases = _phases.Order().ToArray(),
            heartStates = _heartStates.Order().ToArray(),
            exposureTransitions = _exposures,
            coalescedWarnings = _coalescedWarnings,
            followups = _followups,
            sealDeaths = _sealDeaths,
            commands = _commands,
            finalHash = _finalHash,
            replayHash = _replayHash,
            captures = _captures.Order().ToArray(),
            skippedChecks = _skipped,
            error,
            scope = "Fresh public campaign commands earn Acts I–V, the Unremembered Vault, all three primary Breach Heart phases, seal shield loss, containment and the final return. Actual events are presented through Sandbox; overlapping warnings intentionally coalesce. Captured earned events are independently replayed against captured combat states on idle audio lanes. Detached view and combat fixtures check primary-versus-Mirrorborn selection, music levels, exposure baselines, expired sequences and Void impacts without entering Core replay. Mixer fixtures cover voice limits, priority, pause, bus settings and departure. OpeningAudioSmoke provides full PCM and audition coverage. Automated playback is not a listening assessment or loopback recording. No player saves or preferences are used."

        };
        try
        {
            string json = JsonData.Write(report);
            if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "hollow-audio-review.json"), json);
            GD.Print(json);
        }
        catch (Exception ex) { passed = false; GD.PushError("Hollow audio report failed: " + ex); }
        finally { GetTree().Quit(passed ? 0 : 1); }
    }
}
