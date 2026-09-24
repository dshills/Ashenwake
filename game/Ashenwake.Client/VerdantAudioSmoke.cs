using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Earned Act II audio routing, with explicitly separate read-only mixer fixtures.</summary>
public partial class VerdantAudioSmoke : Node
{
    private static readonly string[] Styles = ["verdant_ruins", "verdant_village", "verdant_shrine", "verdant_hunt", "verdant_heart"];
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<object> _route = [];
    private readonly HashSet<string> _styles = [], _cues = [], _captures = [];
    private readonly HashSet<int> _phases = [], _rootDeaths = [];
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
            if (!args.Contains("--verdant-audio-smoke") || _output.Length == 0)
                throw new InvalidDataException("Verdant audio smoke requires --verdant-audio-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Verdant audio smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output); _writeReport = true;
            Engine.MaxFps = 60; GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _combat = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _campaign = CampaignContent.Parse(Read("campaign")); _adventure = AdventureContent.Parse(Read("adventure"));
            _progression = ProgressionContent.Parse(Read("progression"));
            _session = CampaignRuntimeSession.Create(_combat, _adventure, _progression, _campaign);
            await CheckMixerFixtures();
            await EarnVerdant();
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
            audio.Reset(hub); audio.Play("vine_tell", Vector3.Zero); audio.Play("rootheart_tell", Vector3.Zero);
            audio.Play("rootheart_phase2", Vector3.Zero); int count = audio.CueCount;
            for (int i = 0; i < 200; i++)
            {
                foreach (string cue in new[] { "swarm_tell", "carrier_tell", "vine_tell", "low_health", "impact_weapon", "root_severed", "step_moss_1" })
                    audio.Play(cue, Vector3.Zero);
            }
            Check("rootheart_warning_survives_lesser_tells_and_dense_foley", HasCue(audio, "rootheart_tell") && HasCue(audio, "rootheart_phase2") && audio.CueCount - count <= 3);
            Pump(audio, hub, 8);
            Check("rootheart_phase_ducks_music", audio.DuckGain <= .27f);
            audio.Play("rootheart_fall", Vector3.Zero);
            Pump(audio, hub, 24); audio.Play("rootheart_phase2", Vector3.Zero); audio.Play("low_health", Vector3.Zero);
            Check("rootheart_victory_reserves_critical_voice_over_phase_and_heartbeat", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream("rootheart_fall")));
            Pump(audio, hub, 240);
            Check("verdant_warning_tail_releases_duck", audio.DuckGain > .99f);
            var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount);
            audio.Advance(0, hub, true, Basis.Identity);
            for (int i = 0; i < 60; i++) { audio.Advance(.1, hub, true, Basis.Identity); audio.Play("rootheart_fall", Vector3.Zero); }
            Check("verdant_pause_freezes_envelopes_and_rejects_new_transients", frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.BankStarts, audio.CueCount, audio.FootstepCount));
            audio.Advance(0, hub, false, Basis.Identity);
            await CheckNativePlayback(audio, hub);
            int starts = audio.BankStarts;
            ClientAudio.SetVolume(ClientAudio.MusicBus, 0); ClientAudio.SetVolume(ClientAudio.EffectsBus, 0);
            audio.Play("antler_tell", Vector3.Zero); Pump(audio, hub, 20);
            Check("verdant_mute_uses_settings_buses_without_restarting_score", AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.MusicBus)) &&
                AudioServer.IsBusMute(AudioServer.GetBusIndex(ClientAudio.EffectsBus)) && audio.BankStarts == starts);
            foreach (var pair in levels) ClientAudio.SetVolume(pair.Key, pair.Value);
            count = audio.CueCount;
            audio.SetStyle("spine_causeway");
            Pump(audio, hub, 130);
            Check("departure_releases_verdant_banks_and_transients", audio.ActiveMusicBanks == 0 && audio.DesiredStyle == "" && audio.CueCount == count &&
                audio.EffectPlayers.All(p => !p.Playing) && audio.WarningPlayers.All(p => !p.Playing));
            await SettleStyle(audio, hub, "verdant_ruins");
            Check("return_to_verdant_does_not_replay_one_shots", audio.CueCount == count && audio.BankStarts == starts + 1);
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
        Check("native_verdant_stems_play_and_advance_together", active.Length == 3 && active.All(p => p.Playing) &&
            after.Zip(before).All(p => p.First - p.Second > .05) && after.Max() - after.Min() <= .05);
        audio.Advance(0, view, true, Basis.Identity); await Frames(2);
        double[] paused = active.Select(p => (double)p.GetPlaybackPosition()).ToArray(); await Frames(12);
        double[] held = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        Check("native_verdant_pause_holds_score_clock", held.Zip(paused).All(p => Math.Abs(p.First - p.Second) < .01));
        audio.Advance(0, view, false, Basis.Identity); await Frames(12);
        double[] resumed = active.Select(p => (double)p.GetPlaybackPosition()).ToArray();
        Check("native_verdant_resume_continues_score_clock", resumed.Zip(held).All(p => p.First - p.Second > .05));
        _route.Add(new { kind = "native-playheads", before, after, paused, held, resumed });
    }

    private async Task EarnVerdant()
    {
        _sandbox = new Sandbox { ContentJsonOverride = _combat, AutomaticStep = true, AdvanceOverride = _ => [], SessionOverride = () => _session.Combat };
        AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign(); _sandbox.SetSession(_session.Combat);
        _stage = new CampaignStage(); AddChild(_stage);
        Refresh(); string room = _session.ActiveEncounterId;
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !_session.Capture().Campaign.CompletedActs.Contains(2); i++)
        {
            long tick = _session.Tick;
            var result = _session.Execute(CampaignRuntimeSmoke.Next(_session)); _commands++;
            if (!result.Success) throw new InvalidDataException("Earned Verdant audio route failed: " + result.Reason);
            if (_session.Capture().Campaign.Deaths > 0) throw new InvalidDataException("Earned Verdant audio route died.");
            string hash = _session.StateHash;
            _sandbox.PresentCombatEvents(result.CombatEvents, _session.Combat);
            _sandbox._Process(_session.Tick > tick ? 1d / 30 : 0);
            Check("earned_presentation_preserves_core", _session.StateHash == hash);
            var audio = _sandbox.AudioDirector;
            foreach (var cue in OpeningFoley.Cues) if (HasCue(audio, cue.Id)) _cues.Add(cue.Id);
            if (_session.ActiveEncounterId == "campaign.rootheart")
            {
                _phases.Add(_session.Combat.View.BossPhase);
                foreach (var change in result.CombatEvents.Where(e => e.Kind == "BossPhaseChanged"))
                {
                    string cue = "rootheart_phase2";
                    Check("earned_rootheart_has_authored_second_phase", change.Amount == 2);
                    Check("earned_" + cue + "_uses_critical_voice", ReferenceEquals(audio.WarningPlayers[1].Stream, OpeningFoley.GetStream(cue)));
                    if (_session.Combat.View.Actors.Any(a => a.DefinitionId == "boss.rootheart" && a.Health > 0))
                    {
                        Pump(audio, _session.Combat.View, 90);
                        Check("earned_second_phase_fully_raises_rootheart_boss_stem", audio.Mode == "boss" && audio.BossGain > .99f);
                        Refresh(); await Capture("verdant-audio-rootheart-phase2.png");
                    }
                    _route.Add(new { kind = "earned-phase", phase = change.Amount, tick = _session.Tick, cue, audio.BossGain, stateHash = hash });
                }
                foreach (var death in result.CombatEvents.Where(e => e.Kind == "EntityKilled"))
                {
                    var target = _session.Combat.View.Actors.Single(a => a.Id == death.TargetId);
                    if (target.DefinitionId == "enemy.feeding_root")
                    {
                        _rootDeaths.Add(target.Id); Check("earned_root_" + target.Id + "_plays_sever", HasCue(audio, "root_severed"));
                        if (_rootDeaths.Count == 1 && _session.Combat.View.BossPhase == 1)
                        {
                            float before = audio.BossGain; int starts = audio.BankStarts;
                            Pump(audio, _session.Combat.View, 90);
                            Check("first_earned_root_loss_raises_music_without_restarting_phrase", audio.BossGain > before &&
                                audio.BossGain >= .49f && audio.BankStarts == starts && _session.StateHash == hash);
                        }
                        _route.Add(new { kind = "earned-root-severed", actor = target.Id, tick = _session.Tick, stateHash = hash });
                    }
                    if (target.DefinitionId == "boss.rootheart")
                    {
                        _victory = true; Check("earned_rootheart_death_plays_fall", HasCue(audio, "rootheart_fall"));
                        _route.Add(new { kind = "earned-victory", tick = _session.Tick, stateHash = hash });
                    }
                }
            }
            if (room != _session.ActiveEncounterId)
            {
                room = _session.ActiveEncounterId; Refresh(); string style = Style();
                if (Styles.Contains(style))
                {
                    await SettleStyle(audio, _session.Combat.View, style); _styles.Add(style);
                    Check("earned_" + style + "_selects_regional_score", audio.DesiredStyle == style && audio.PlayingStyle == style);
                    if (style == "verdant_heart") Check("earned_rootheart_raises_boss_stem", audio.Mode == "boss" && audio.BossGain >= .39f);
                    if (style == "verdant_hunt" && _session.Combat.View.Actors.Any(a => a.DefinitionId == "boss.antler" && a.Health > 0))
                        Check("earned_antler_raises_hunt_boss_stem", audio.Mode == "boss" && Math.Abs(audio.BossGain - .65f) < .001f);
                    _route.Add(new { kind = "earned-room", room, style, tick = _session.Tick, audio.BankStarts, audio.Mode, stateHash = _session.StateHash });
                    await Capture("verdant-audio-" + style + ".png");
                }
            }
            else if (i % 30 == 0) { Refresh(); await Frames(1); }
        }
        var final = _session.Capture();
        Check("route_earns_act_two_with_briar_shrine_and_antler_hunt", final.Campaign.CompletedActs.Contains(2) &&
            new[] { "campaign.living_ruins", "campaign.plague_village", "campaign.rootheart" }.All(final.Campaign.CompletedEncounters.Contains) &&
            final.Campaign.CompletedExploration.Contains(CampaignRuntimeSession.ShrineEvent) && final.Campaign.CompletedExploration.Contains(CampaignRuntimeSession.HuntEvent));
        Check("earned_route_hears_all_five_verdant_scores", Styles.All(_styles.Contains));
        Check("earned_route_hears_verdant_creature_tells", new[] { "vine_tell", "swarm_tell", "carrier_tell", "antler_tell", "rootheart_tell" }.All(_cues.Contains));
        Check("earned_route_hears_all_rootheart_phases_severs_and_victory", new[] { 1, 2 }.All(_phases.Contains) && _rootDeaths.Count == 3 && _victory &&
            new[] { "rootheart_phase2", "root_severed", "rootheart_fall" }.All(_cues.Contains));
        Check("earned_route_uses_moss_footsteps_and_combat_impacts", _cues.Any(c => c.StartsWith("step_moss_", StringComparison.Ordinal)) && _cues.Contains("impact_weapon"));
        Refresh(); Pump(_sandbox.AudioDirector, _session.Combat.View, 480);
        Check("defeated_rootheart_releases_boss_and_combat_stems", _sandbox.AudioDirector.Mode == "exploration" && _sandbox.AudioDirector.BossGain == 0 && _sandbox.AudioDirector.CombatGain == 0);
        await Capture("verdant-audio-rootheart-defeated.png");
        await CheckShippingReset();
        var replay = _session.CaptureReplay();
        var verified = CampaignRuntimeReplayRunner.Run(_combat, _adventure, _progression, _campaign, replay);
        Check("earned_campaign_replay_is_identical", verified.Success && verified.FinalHash == _session.StateHash);
        _finalHash = _session.StateHash; _replayHash = verified.FinalHash;
        System.IO.File.WriteAllText(Path.Combine(_output, "verdant-audio.awcampaign"), JsonData.Write(replay));
        string save = JsonData.Write(new CampaignRuntimeSave(1, _session.StateHash, _session.Capture()));
        System.IO.File.WriteAllText(Path.Combine(_output, "verdant-audio.save.json"), save);
        var restored = CampaignRuntimeSaveStore.Read(_combat, _adventure, _progression, _campaign, save);
        Check("earned_save_restores_identically", restored.StateHash == _session.StateHash);
        int cues = _sandbox.AudioDirector.CueCount;
        _session = restored; _sandbox.SetSession(restored.Combat); _sandbox._Process(1d / 30);
        Check("restoring_defeated_rootheart_does_not_replay_victory", _sandbox.AudioDirector.CueCount == cues);
        CheckRootheartImpactFixtures();
        _stage.QueueFree(); _sandbox.QueueFree(); await Frames(3);
    }

    private void CheckRootheartImpactFixtures()
    {
        // Construct presentation-only events against the earned arena's actors. These
        // are not evidence that the campaign route took a hit, and never enter Core.
        string hash = _session.StateHash;
        var audio = _sandbox.AudioDirector;
        int rootheart = _session.Combat.View.Actors.Single(a => a.DefinitionId == "boss.rootheart").Id;
        foreach (string content in new[] { "campaign.root_spores", "campaign.root_tangle" })
        {
            audio.Reset(_session.Combat.View);
            // Reset stops voices but intentionally retains their streams. Clear only
            // this diagnostic's stopped presentation slots to reject stale evidence.
            foreach (var voice in audio.EffectPlayers) voice.Stream = null;
            int before = audio.CueCount;
            _sandbox.PresentCombatEvents([new(_session.Combat.View.Tick, "DamageApplied", rootheart, 1, 1, content)], _session.Combat);
            Check("fixture_" + content + "_dispatches_one_new_spell_impact", audio.CueCount == before + 1 &&
                audio.EffectPlayers.Count(p => ReferenceEquals(p.Stream, OpeningFoley.GetStream("impact_spell"))) == 1 &&
                audio.EffectPlayers.All(p => !ReferenceEquals(p.Stream, OpeningFoley.GetStream("impact_weapon"))) &&
                _session.StateHash == hash);
            _route.Add(new { kind = "read-only-impact-fixture", content, cue = audio.LastCue, delivered = audio.CueCount - before, stateHash = hash });
        }
        audio.Reset(_session.Combat.View);
    }

    private async Task CheckShippingReset()
    {
        var audio = _sandbox.AudioDirector; string hash = _session.StateHash;
        var ambience = _sandbox.GetChildren().OfType<AudioStreamPlayer>().Single(p => p.Name == "RegionalAmbience");
        float busVolume = ClientAudio.GetVolume(ClientAudio.MusicBus);
        audio.Play("rootheart_fall", Vector3.Zero);
        for (int i = 0; i < 8; i++) _sandbox._Process(1d / 60);
        Check("shipping_verdant_ambience_ducks_for_warning_without_changing_settings", ambience.Bus == ClientAudio.MusicBus &&
            ambience.VolumeDb < -39 && ClientAudio.GetVolume(ClientAudio.MusicBus) == busVolume);
        _sandbox.SetModalPaused("verdant-audio-diagnostic", true); _sandbox._Process(1d / 30);
        var frozen = (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts);
        for (int i = 0; i < 12; i++) { _sandbox._Process(1d / 30); await Frames(1); }
        Check("shipping_pause_freezes_verdant_audio_and_core", _sandbox.IsPaused && audio.Paused && hash == _session.StateHash &&
            frozen == (audio.DuckGain, audio.CombatGain, audio.BossGain, audio.FootstepCount, audio.CueCount, audio.BankStarts));
        _sandbox.SetModalPaused("verdant-audio-diagnostic", false); _sandbox._Process(1d / 30);
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
        _sandbox.PresentAuthoredRoom(_session.Room, "verdant-audio:" + _session.ActiveEncounterId, style);
        _sandbox.SetEnvironmentStyle(style);
        _stage.Show(state, _session.View, _session.Room, _session.Interactions, _session.Production.View.ActiveManifestations,
            _session.Production.ProgressionView.HubStage, view.Actors.Single(a => a.Id == 1).Position, view.BossPhase,
            combat: view, activeEncounterId: _session.ActiveEncounterId);
        _sandbox.SetWorldSubtitle("VERDANT AUDIO / " + style.ToUpperInvariant());
    }
    private async Task Capture(string name)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture-verdant-audio")) return;
        if (DisplayServer.GetName() == "headless") { if (!_skipped.Contains("native screenshots")) _skipped.Add("native screenshots"); return; }
        await Frames(2); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + name, image.GetWidth() > 0 && image.GetHeight() > 0 && image.SavePng(Path.Combine(_output, name)) == Error.Ok); _captures.Add(name);
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Verdant audio check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "VerdantAudioClientSmokePassed" : "VerdantAudioClientSmokeFailed",
            passed,
            checks = _checks,
            route = _route,
            styles = _styles.Order().ToArray(),
            cues = _cues.Order().ToArray(),
            rootheartPhases = _phases.Order().ToArray(),
            rootDeaths = _rootDeaths.Order().ToArray(),
            commands = _commands,
            finalHash = _finalHash,
            replayHash = _replayHash,
            captures = _captures.Order().ToArray(),
            skippedChecks = _skipped,
            error,
            scope = "Fresh public campaign commands earn Acts I and II, including Briar Shrine, Antler hunt, Rootheart phases, root severing and victory. Sandbox presents actual earned events. Separate read-only mixer fixtures exercise warning priority, bounded voices, bus muting, pause and room departure. PCM and audition coverage is provided by OpeningAudioSmoke's expanded catalog. Faster-than-wall-time command simulation is not a human playtest, hearing-quality assessment or audio loopback recording. No player saves or preferences are used."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "verdant-audio-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
