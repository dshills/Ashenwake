using System.Reflection;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Earned expeditions and native board controls exercise the complete endgame transaction surface.</summary>
public partial class ExpeditionSmoke : Node3D
{
    private sealed record ReplaySegment(string File, int Commands, string FinalHash);
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<ReplaySegment> _replays = [];
    private readonly Dictionary<string, int> _events = [];
    private EndgameRuntimeSession _session = null!;
    private Sandbox _sandbox = null!;
    private EndgameHud _hud = null!;
    private EndgamePresentation _effects = null!;
    private AdventureContent _adventure = null!;
    private ProgressionContent _progression = null!;
    private CampaignContent _campaign = null!;
    private EndgameContent _endgame = null!;
    private string _combatJson = "", _output = "", _fixture = "";
    private bool _writeReport;
    private int _commands, _requests, _segmentCommands, _saves;
    private long _revision;
    private EndgameRuntimeCommand? _lastRequest;
    private HashSet<string> PauseOwners => (HashSet<string>)(typeof(Sandbox).GetField("_modalPauses", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_sandbox) ?? throw new MissingFieldException("_modalPauses"));
    private ConfirmationDialog Confirmation => Find<ConfirmationDialog>("ExpeditionConfirmation");

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--expedition-smoke") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Expedition smoke requires --expedition-smoke --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800);
            _adventure = AdventureContent.Parse(Read("adventure")); _progression = ProgressionContent.Parse(Read("progression"));
            _campaign = CampaignContent.Parse(Read("campaign")); _endgame = EndgameContent.Parse(Read("endgame"));
            string campaignCombat = CampaignCombatContent.Parse(Read("combat"), Read("campaign-combat")).CombatJson;
            _combatJson = EndgameCombatContent.Parse(campaignCombat, Read("endgame-combat"), _endgame).CombatJson;
            _session = EndgameRuntimeSession.Create(_combatJson, _adventure, _progression, _campaign, _endgame);
            _sandbox = new Sandbox { ContentJsonOverride = _combatJson, AutomaticStep = false };
            AddChild(_sandbox); _sandbox.EnableCampaign(); _sandbox.SetProcess(false); _sandbox.SetSession(_session.Combat); _sandbox.SetPaused(true);
            _effects = new EndgamePresentation(); AddChild(_effects); _effects.AttachOverlay(_sandbox);
            _hud = new EndgameHud(); _sandbox.AddOverlay(_hud); WireBoard();
            Refresh(); _hud.ShowTab("Sigils"); await Frames(6);
            await CheckCampaignGate();

            _fixture = Read("phase4-campaign-complete");
            string previous = CampaignCombatContent.Parse(Read("combat-phase4"), Read("campaign-combat-phase4")).CombatJson;
            _session = EndgameRuntimeMigration.ImportPhaseFour(_fixture, previous, _combatJson, _adventure, _progression, _campaign, _endgame);
            _sandbox.SetSession(_session.Combat); _hud.SessionRestored(); Refresh(); await Frames();
            Check("maintained_campaign_fixture_unlocks_board_without_fabricated_sigils", _session.InHub && _session.View.Unlocked && _session.View.AvailableSigils.Length == 0);
            await CheckHuntGates();
            await ClaimRecovery("first");
            await CheckSigilPreview();
            await CheckEntryConfirmation();
            await CheckDeathRetryAbandon();
            await ClaimRecovery("after_abandonment");
            await StartSelectedSigil("recovered");
            await CompleteThroughBoard("fracture");
            Check("real_four_room_victory_earns_tier_two_sigil", _session.InHub && _session.View.HighestClearedTier == 1 &&
                _session.View.AvailableSigils.Any(s => s.Tier == 2) && _session.Capture().Endgame.Rewards.Values.Count(r => r.Kind == "Fracture") == 1);
            await CheckAttunement();
            await EarnFirstHunt();
            await CompleteThroughBoard("hunt");
            await CheckEarnedRewards();
            Check("maintained_fixture_bytes_are_unchanged", Read("phase4-campaign-complete") == _fixture);
            SaveAndCheck("expedition.final.save.json"); RecordReplay();
            Check("all_executed_commands_are_covered_by_verified_replay_segments", _replays.Sum(r => r.Commands) == _commands);
            Finish(true, "");
        }
        catch (Exception ex)
        {
            try { await Capture("expedition-failure.png"); } catch (Exception captureError) { GD.PushError(captureError.Message); }
            GD.PushError(ex.ToString()); Finish(false, ex.Message);
        }
    }

    private void WireBoard()
    {
        _hud.FractureRequested += id => Request(new(EndgameRuntimeAction.StartFracture, SigilId: id));
        _hud.AttuneRequested += (id, old, next) => Request(new(EndgameRuntimeAction.AttuneSigil, SigilId: id, Id: old, Value: next));
        _hud.HuntRequested += id => Request(new(EndgameRuntimeAction.StartGodHunt, Id: id));
        _hud.RecoveryRequested += () => Request(new(EndgameRuntimeAction.ClaimRecoverySigil));
        _hud.AdvanceRequested += () => Request(new(EndgameRuntimeAction.AdvanceEncounter));
        _hud.RetryRequested += () => Request(new(EndgameRuntimeAction.RetryEncounter));
        _hud.AbandonRequested += () => Request(new(EndgameRuntimeAction.Abandon));
        _hud.HubRequested += () => Request(new(EndgameRuntimeAction.ReturnToHub));
        _hud.ModalChanged += open => _sandbox.SetModalPaused("endgame-confirmation", open);
    }

    private void Request(EndgameRuntimeCommand command)
    {
        _requests++; _lastRequest = command; Execute(command); Refresh();
    }

    private void Execute(EndgameRuntimeCommand command)
    {
        // Export each complete bounded replay before Core starts its next replay window.
        if (_segmentCommands == 1800) { RecordReplay(); _segmentCommands = 0; }
        var result = _session.Execute(command); _commands++; _segmentCommands++; _revision++;
        if (!result.Success) throw new InvalidDataException("Expedition public command failed: " + result.Reason + " / " + command.Action);
        foreach (string value in result.WorldEvents)
        {
            string key = value.Split(':')[0]; _events[key] = _events.GetValueOrDefault(key) + 1;
        }
    }

    private async Task CheckCampaignGate()
    {
        string hash = _session.StateHash;
        Check("fresh_campaign_cannot_enter_endgame", !_session.View.Unlocked && VisibleText.Contains("campaign", StringComparison.OrdinalIgnoreCase) &&
            !Descendants(_hud).OfType<Button>().Any(b => b.Name == "ExpeditionEnter" && b.IsVisibleInTree() && !b.Disabled));
        foreach (string tab in new[] { "Hunts", "Run", "Rewards", "Sigils" }) { _hud.ShowTab(tab); await Frames(); }
        Check("locked_board_tabs_cannot_mutate_campaign_or_replay", _session.StateHash == hash && _session.CaptureReplay().Frames.Length == 0 && _requests == 0);
        await Capture("expedition-campaign-gate.png");
    }

    private async Task CheckHuntGates()
    {
        string hash = _session.StateHash; int requests = _requests;
        await Click("ExpeditionTabHunts");
        var definition = _endgame.Capture();
        foreach (var hunt in definition.Hunts.Where(h => !h.Secret))
        {
            await Click("ExpeditionHunt" + SafeId(hunt.Id));
            Check("locked_" + SafeId(hunt.Id) + "_inspectable_but_cannot_start", Find<Button>("ExpeditionEnter").Disabled &&
                VisibleText.Contains(hunt.Name, StringComparison.Ordinal) && VisibleText.Contains(hunt.RequiredTier.ToString(), StringComparison.Ordinal));
        }
        foreach (var hunt in definition.Hunts.Where(h => h.Secret))
            Check("secret_" + SafeId(hunt.Id) + "_identity_and_mechanics_are_hidden", !Descendants(_hud).OfType<Button>().Any(b => b.Name == "ExpeditionHunt" + SafeId(hunt.Id)) &&
                !VisibleText.Contains(hunt.Name, StringComparison.Ordinal) && !hunt.Counterplay.Any(c => VisibleText.Contains(c, StringComparison.Ordinal)));
        Check("hunt_inspection_is_read_only", _session.StateHash == hash && _requests == requests);
        await Capture("expedition-locked-hunts.png");
        _hud.ShowTab("Sigils"); await Frames();
    }

    private void WalkToGate()
    {
        var request = new EndgameRuntimeCommand(EndgameRuntimeAction.ClaimRecoverySigil);
        _hud.SetOpen(false);
        for (int i = 0; i < 500; i++)
        {
            var next = EndgameRuntimeSmoke.AtGate(_session, request);
            if (next.Action != EndgameRuntimeAction.Tick) { Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); return; }
            Execute(next);
        }
        throw new InvalidDataException("Could not reach the Fracture gate.");
    }

    private async Task ClaimRecovery(string label)
    {
        Refresh(); _hud.ShowTab("Sigils"); await Frames();
        bool wasAway = !AtGate();
        if (wasAway) Check(label + "_recovery_is_disabled_away_from_gate", Find<Button>("ExpeditionRecovery").Disabled);
        WalkToGate(); Refresh(); _hud.ShowTab("Sigils"); await Frames();
        int requests = _requests, materials = _session.View.Materials;
        await Click("ExpeditionRecovery");
        Check(label + "_recovery_claim_is_one_real_free_command", _requests == requests + 1 && _lastRequest?.Action == EndgameRuntimeAction.ClaimRecoverySigil &&
            _session.View.AvailableSigils is [{ Tier: 1 }] && _session.View.Materials == materials && !_session.View.CanClaimRecoverySigil);
    }

    private async Task CheckSigilPreview()
    {
        long id = _session.View.AvailableSigils.Single().Id;
        string hash = _session.StateHash; int frames = _session.CaptureReplay().Frames.Length, requests = _requests;
        await Click("ExpeditionSigil" + id);
        Check("sigil_selection_shows_authored_route_without_consuming", _session.StateHash == hash && _requests == requests &&
            _session.CaptureReplay().Frames.Length == frames && !_session.Capture().Endgame.Sigils.Single(s => s.Id == id).Consumed &&
            Find<Control>("ExpeditionRoute").IsVisibleInTree());
        var preview = _session.PreviewSigil(id);
        Check("sigil_route_shows_each_authored_room_as_upcoming", preview.EncounterNames.Select((name, i) =>
            Find<Label>("ExpeditionRouteTitle" + i).Text == name && Find<Label>("ExpeditionRouteState" + i).Text == "UPCOMING").All(value => value));
        Check("tier_one_has_no_unsupported_attunement", !Descendants(_hud).OfType<Button>().Any(b => b.Name == "ExpeditionAttune" && b.IsVisibleInTree() && !b.Disabled));
        Check("board_owns_independent_modal_pause", PauseOwners.Contains("expedition-panel") && PauseOwners.Contains("session") && _sandbox.IsPaused);
        PressKey(Key.F); PressKey(Key.Period); await Frames();
        Check("board_blocks_interact_and_single_step", _session.StateHash == hash && _session.CaptureReplay().Frames.Length == frames);
        await CheckLayouts("sigil", "ExpeditionEnter");
        _hud.SetOpen(false); await Frames();
        Check("closing_board_releases_only_its_own_pause", !PauseOwners.Contains("expedition-panel") && PauseOwners.Contains("session") && _sandbox.IsPaused);
        _hud.ShowTab("Sigils"); await Frames();
    }

    private async Task CheckEntryConfirmation()
    {
        string hash = _session.StateHash; int requests = _requests;
        await Click("ExpeditionEnter");
        Check("sigil_entry_waits_for_explicit_confirmation", Confirmation.Visible && _session.StateHash == hash && _requests == requests);
        await Capture("expedition-entry-confirmation.png");
        await Cancel();
        Check("cancelled_entry_preserves_sigil_wallet_and_history", _session.StateHash == hash && _requests == requests && !PauseOwners.Contains("endgame-confirmation"));
        await Click("ExpeditionEnter"); _hud.ShowTab("Hunts"); await Frames();
        await StaleConfirmation("tab_change_invalidates_sigil_entry", hash, requests);
        _hud.ShowTab("Sigils"); await Frames();
        await Click("ExpeditionEnter"); _hud.SetOpen(false); await Frames();
        await StaleConfirmation("closing_board_invalidates_sigil_entry", hash, requests);
        _hud.ShowTab("Sigils"); await Frames();
        await Click("ExpeditionEnter"); _hud.Visible = false; await Frames();
        await StaleConfirmation("hiding_board_invalidates_sigil_entry", hash, requests);
        _hud.Visible = true; _hud.ShowTab("Sigils"); await Frames();
        await Click("ExpeditionEnter"); Execute(new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Stop)])); Refresh(); await Frames();
        await StaleConfirmation("new_state_revision_invalidates_sigil_entry", _session.StateHash, requests);
        await Click("ExpeditionEnter"); RestoreSavedSession(); await Frames();
        await StaleConfirmation("same_state_save_load_invalidates_sigil_entry", _session.StateHash, requests);
        Check("restored_board_reacquires_modal_pause", PauseOwners.Contains("expedition-panel") && PauseOwners.Contains("session") && _sandbox.IsPaused);
        await StartSelectedSigil("first");
    }

    private async Task StartSelectedSigil(string label)
    {
        _hud.ShowTab("Sigils"); await Frames();
        var sigil = _session.View.AvailableSigils.OrderBy(s => s.Id).First();
        await Click("ExpeditionSigil" + sigil.Id);
        int requests = _requests;
        await Click("ExpeditionEnter"); await ConfirmOnce(label + "_entry", requests);
        Check(label + "_entry_consumes_selected_sigil_and_starts_four_room_run", !_session.InHub &&
            _lastRequest?.Action == EndgameRuntimeAction.StartFracture && _session.Capture().Endgame.Sigils.Single(s => s.Id == sigil.Id).Consumed &&
            _session.RunView is { Kind: "Fracture", EncounterIndex: 0, EncounterCount: 4, AttemptsRemaining: 3 });
        _hud.ShowRun(); await Frames();
        Check(label + "_unwon_room_cannot_continue_or_retry", Find<Button>("ExpeditionContinue").Disabled &&
            !Descendants(_hud).OfType<Button>().Any(b => b.Name == "ExpeditionRetry" && b.IsVisibleInTree() && !b.Disabled));
        CheckRunRoute(label + "_initial_route");
        await Capture("expedition-" + label + "-route.png");
    }

    private async Task CheckDeathRetryAbandon()
    {
        _hud.SetOpen(false);
        for (int i = 0; i < 6000 && !_session.AwaitingRetry; i++)
        {
            Execute(new(EndgameRuntimeAction.Tick)); if (i % 180 == 0) await Frames(1);
        }
        Check("ordinary_idle_combat_consumes_a_real_attempt", _session.AwaitingRetry && _session.RunView is { Deaths: 1, AttemptsRemaining: 2, RewardPercent: 80 });
        Refresh(); _hud.ShowRun(); await Frames();
        Check("defeated_character_can_retry_but_cannot_continue", !Find<Button>("ExpeditionRetry").Disabled && Find<Button>("ExpeditionContinue").Disabled);
        CheckRunRoute("defeated_route");
        Check("run_metrics_report_actual_attempts_deaths_and_reduction", Find<Label>("ExpeditionAttempts").Text.Contains("2", StringComparison.Ordinal) &&
            Find<Label>("ExpeditionDeaths").Text.Contains("1", StringComparison.Ordinal) && Find<Label>("ExpeditionRewardPercent").Text.Contains("80%", StringComparison.Ordinal));
        await CheckLayouts("retry", "ExpeditionRetry");
        SaveAndCheck("expedition.defeated.save.json");
        int requests = _requests; long run = _session.RunView!.Id;
        await Click("ExpeditionRetry");
        Check("native_retry_preserves_run_and_spent_attempt", _requests == requests + 1 && _lastRequest?.Action == EndgameRuntimeAction.RetryEncounter &&
            _session.RunView is { Deaths: 1, AttemptsRemaining: 2, EncounterIndex: 0 } && _session.RunView.Id == run && !_session.AwaitingRetry &&
            _session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0);
        _hud.ShowRun(); await Frames();
        string hash = _session.StateHash; requests = _requests;
        await Click("ExpeditionAbandon");
        Check("abandonment_requires_explicit_confirmation", Confirmation.Visible && _session.StateHash == hash);
        await Capture("expedition-abandon-confirmation.png"); await Cancel();
        Check("cancelled_abandonment_preserves_run_and_attempts", _session.StateHash == hash && _requests == requests);
        await Click("ExpeditionAbandon"); _hud.ShowTab("Rewards"); await Frames();
        await StaleConfirmation("changing_tab_invalidates_abandonment", hash, requests);
        _hud.ShowRun(); await Frames();
        await Click("ExpeditionAbandon"); await ConfirmOnce("abandonment", requests);
        Check("confirmed_abandonment_returns_without_reward_or_sigil_refund", _session.InHub && _session.RunView?.Status == "Abandoned" &&
            _session.Capture().Endgame.Rewards.Count == 0 && _session.View.AvailableSigils.Length == 0 && _session.View.CanClaimRecoverySigil);
    }

    private async Task CompleteThroughBoard(string label)
    {
        long runId = _session.RunView!.Id;
        int count = _session.RunView.EncounterCount;
        for (int room = 0; room < count; room++)
        {
            _hud.SetOpen(false);
            for (int i = 0; i < 12000 && !_session.EncounterCleared; i++)
            {
                if (_session.AwaitingRetry) throw new InvalidDataException("Victory route exhausted an attempt in " + label);
                Execute(new(EndgameRuntimeAction.Tick, Commands: EndgameCombatSmoke.Commands(_session.Combat.View, _session.Room)));
                if (i % 180 == 0) await Frames(1);
            }
            Check(label + "_room_" + room + "_cleared_by_real_combat", _session.EncounterCleared && _session.RunView is { Status: "Active" });
            Refresh(); _hud.ShowRun(); await Frames();
            Check(label + "_room_" + room + "_route_matches_authoritative_position", _session.RunView!.EncounterIndex == room && Find<Control>("ExpeditionRoute").IsVisibleInTree() && !Find<Button>("ExpeditionContinue").Disabled);
            CheckRunRoute(label + "_cleared_route_" + room);
            int requests = _requests; int drops = _session.Combat.View.Loot.Count; string hash = _session.StateHash;
            await Capture("expedition-" + label + "-room-" + (room + 1) + ".png");
            await Click("ExpeditionContinue");
            if (room == count - 1)
                Check(label + "_finish_retains_floor_loot_without_departure_dialog", !Confirmation.Visible &&
                    _session.RunView?.Status == "Completed" && _session.Combat.View.Loot.Count == drops);
            if (Confirmation.Visible)
            {
                Check(label + "_room_" + room + "_loot_warning_is_read_only", drops > 0 && _session.StateHash == hash && _requests == requests && Confirmation.DialogText.Contains(drops.ToString(), StringComparison.Ordinal));
                if (room == 0)
                {
                    await Capture("expedition-" + label + "-loot-confirmation.png"); await Cancel();
                    Check(label + "_cancelled_loot_departure_keeps_ground_drops", _session.StateHash == hash && _session.Combat.View.Loot.Count == drops && _requests == requests);
                    await Click("ExpeditionContinue"); _hud.ShowTab("Rewards"); await Frames();
                    await StaleConfirmation(label + "_tab_change_invalidates_loot_departure", hash, requests);
                    _hud.ShowRun(); await Frames(); await Click("ExpeditionContinue");
                }
                await ConfirmOnce(label + "_room_" + room + "_advance", requests);
            }
            Check(label + "_room_" + room + "_native_advance_commits_once", _requests == requests + 1 && _lastRequest?.Action == EndgameRuntimeAction.AdvanceEncounter &&
                _session.RunView!.Id == runId && _session.RunView.EncounterIndex == room + 1);
        }
        Check(label + "_completed_run_commits_one_permanent_reward", _session.RunView?.Status == "Completed" &&
            _session.Capture().Endgame.Rewards.Values.Count(r => r.RunId == runId) == 1);
        Refresh(); _hud.ShowRun(); await Frames();
        CheckRunRoute(label + "_completed_route");
        var receipt = _session.Capture().Endgame.Rewards[runId];
        string resultText = Find<Label>("ExpeditionRewardReceipt").Text;
        Check(label + "_results_show_exact_committed_reward", resultText.Contains("#" + runId, StringComparison.Ordinal) &&
            resultText.Contains("+" + receipt.Materials + " common", StringComparison.Ordinal) && resultText.Contains("+" + receipt.Mastery + " mastery", StringComparison.Ordinal));
        await Capture("expedition-" + label + "-results.png");
        int returnRequests = _requests; string before = _session.StateHash;
        await Click("ExpeditionReturn");
        if (Confirmation.Visible)
        {
            Check(label + "_return_waits_for_ground_loot_confirmation", _session.StateHash == before && _requests == returnRequests);
            await ConfirmOnce(label + "_return", returnRequests);
        }
        Check(label + "_return_preserves_completed_rewards", _session.InHub && _requests == returnRequests + 1 &&
            _lastRequest?.Action == EndgameRuntimeAction.ReturnToHub && _session.Capture().Endgame.Rewards.Values.Count(r => r.RunId == runId) == 1);
    }

    private async Task CheckAttunement()
    {
        WalkToGate(); Refresh(); _hud.ShowTab("Sigils"); await Frames();
        var sigil = _session.View.AvailableSigils.Single(); await Click("ExpeditionSigil" + sigil.Id);
        string hash = _session.StateHash; int materials = _session.View.Materials, requests = _requests;
        var old = Find<OptionButton>("ExpeditionOldModifier"); var next = Find<OptionButton>("ExpeditionNewModifier");
        string oldId = old.GetItemMetadata(old.Selected).AsString(); string newId = next.GetItemMetadata(next.Selected).AsString();
        Check("earned_tier_two_attunement_previews_compatible_rule_replacement", oldId != newId && sigil.Modifiers.Contains(oldId) && !sigil.Modifiers.Contains(newId) && !Find<Button>("ExpeditionAttune").Disabled);
        await CheckLayouts("attunement", "ExpeditionAttune");
        Check("attunement_preview_leaves_sigil_materials_and_history_unchanged", _session.StateHash == hash && _requests == requests);
        await Click("ExpeditionAttune");
        var changed = _session.View.AvailableSigils.Single(s => s.Id == sigil.Id);
        Check("native_attunement_replaces_exact_rule_and_spends_five_once", _requests == requests + 1 && _lastRequest is { Action: EndgameRuntimeAction.AttuneSigil } &&
            _lastRequest.Id == oldId && _lastRequest.Value == newId && _session.View.Materials == materials - 5 &&
            !changed.Modifiers.Contains(oldId) && changed.Modifiers.Contains(newId) && changed.Seed == sigil.Seed && changed.Tier == sigil.Tier && !changed.Consumed);
        await Capture("expedition-attuned-sigil.png");
    }

    private async Task EarnFirstHunt()
    {
        _hud.SetOpen(false);
        for (int i = 0; i < 18000 && !EndgameRuntimeSmoke.Complete(_session, 3); i++)
        {
            Execute(EndgameRuntimeSmoke.Next(_session, 3)); if (i % 180 == 0) await Frames(1);
        }
        Check("ordinary_expeditions_unlock_first_known_hunt", EndgameRuntimeSmoke.Complete(_session, 3) && _session.View.UnlockedHunts.Contains("hunt.false_vael"));
        WalkToGate(); Refresh(); _hud.ShowTab("Hunts"); await Frames();
        int requests = _requests; string hash = _session.StateHash;
        await Click("ExpeditionHunthunt_false_vael");
        var hunt = _endgame.Capture().Hunts.Single(h => h.Id == "hunt.false_vael");
        Check("unlocked_hunt_previews_actual_mechanics_and_catalyst", !Find<Button>("ExpeditionEnter").Disabled &&
            hunt.Counterplay.All(c => VisibleText.Contains(c, StringComparison.Ordinal)) && _session.StateHash == hash && _requests == requests);
        await CheckLayouts("hunt", "ExpeditionEnter");
        await Click("ExpeditionEnter");
        Check("hunt_entry_requires_explicit_confirmation", Confirmation.Visible && _session.StateHash == hash && _requests == requests);
        await Cancel();
        Check("cancelled_hunt_entry_does_not_start_or_spend", _session.StateHash == hash && _requests == requests);
        await Click("ExpeditionEnter"); _hud.SelectHunt("hunt.ilyra_teeth"); await Frames();
        await StaleConfirmation("hunt_selection_invalidates_pending_entry", hash, requests);
        await Click("ExpeditionHunthunt_false_vael"); await Click("ExpeditionEnter"); await ConfirmOnce("hunt_entry", requests);
        Check("native_hunt_entry_starts_three_phases_with_two_attempts", _session.RunView is { Kind: "GodHunt", EncounterCount: 3, AttemptsRemaining: 2, EncounterIndex: 0 } && _lastRequest?.Id == hunt.Id);
    }

    private async Task CheckEarnedRewards()
    {
        var reward = _session.Capture().Endgame.Rewards.Values.Last();
        _hud.ShowTab("Rewards"); await Frames();
        string hash = _session.StateHash; int requests = _requests;
        Check("earned_hunt_catalyst_is_in_permanent_inventory", reward.Kind == "GodHunt" && reward.ContentId == "hunt.false_vael" &&
            reward.EvolutionMaterial == "material.vael_rib" && _session.View.Catalysts.GetValueOrDefault(reward.EvolutionMaterial) == reward.EvolutionCount && reward.EvolutionCount > 0);
        Check("rewards_screen_reports_earned_wallet_and_catalyst", VisibleText.Contains(_session.View.Materials.ToString(), StringComparison.Ordinal) && VisibleText.Contains("vael", StringComparison.OrdinalIgnoreCase));
        await Capture("expedition-earned-rewards.png");
        foreach (string tab in new[] { "Sigils", "Hunts", "Run", "Rewards" }) { await Click("ExpeditionTab" + tab); }
        Check("completed_reward_inspection_cannot_grant_again", _session.StateHash == hash && _requests == requests);
    }

    private async Task StaleConfirmation(string label, string hash, int requests)
    {
        bool hidden = !Confirmation.Visible;
        Confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
        Check(label, hidden && _session.StateHash == hash && _requests == requests);
    }

    private async Task Cancel()
    {
        Confirmation.EmitSignal(ConfirmationDialog.SignalName.Canceled); Confirmation.Hide(); await Frames();
    }

    private async Task ConfirmOnce(string label, int requests)
    {
        Check(label + "_has_visible_confirmation", Confirmation.Visible);
        // The viewport click opens the real native popup; the public window signal confirms it.
        Confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed); Confirmation.Hide(); await Frames();
        string hash = _session.StateHash;
        Check(label + "_submits_one_request", _requests == requests + 1);
        Confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Frames();
        Check(label + "_cannot_be_submitted_twice", _session.StateHash == hash && _requests == requests + 1);
    }

    private void RestoreSavedSession()
    {
        RecordReplay();
        string path = SaveAndCheck("expedition.restore-" + ++_saves + ".save.json");
        string hash = _session.StateHash;
        _session = EndgameRuntimeSaveStore.Load(path, _combatJson, _adventure, _progression, _campaign, _endgame).Session;
        _segmentCommands = 0; _sandbox.SetSession(_session.Combat); _hud.SessionRestored(); Refresh();
        Check("restore_" + _saves + "_preserves_exact_character_and_run", _session.StateHash == hash);
    }

    private string SaveAndCheck(string file)
    {
        string path = Path.Combine(_output, file);
        EndgameRuntimeSaveStore.Write(path, _combatJson, _adventure, _progression, _campaign, _endgame, _session.Capture());
        var loaded = EndgameRuntimeSaveStore.Load(path, _combatJson, _adventure, _progression, _campaign, _endgame).Session;
        Check(file + "_restores_exact_authoritative_state", loaded.StateHash == _session.StateHash); return path;
    }

    private void RecordReplay()
    {
        if (_segmentCommands == 0) return;
        var replay = _session.CaptureReplay();
        var result = EndgameRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, _endgame, replay);
        string file = "expedition.segment-" + (_replays.Count + 1) + ".replay.json";
        Check(file + "_matches_authoritative_state", result.Success && result.FinalHash == _session.StateHash && replay.Frames.Length == _segmentCommands);
        System.IO.File.WriteAllText(Path.Combine(_output, file), JsonData.Write(replay));
        _replays.Add(new(file, replay.Frames.Length, _session.StateHash));
    }

    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "ExpeditionClientSmokePassed" : "ExpeditionClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            commands = _commands,
            requests = _requests,
            replaySegments = _replays,
            events = _events,
            error,
            scope = "Fresh-character gates and a validated maintained phase-4 campaign archive feed the real EndgameRuntimeSession. Ordinary combat commands earn all new Sigils, deaths, retries, room rewards, tiers, hunt unlocks and catalysts. Native viewport clicks inspect cards, claim recovery, attune, enter, retry, advance, finish and return; no power or reward state is fabricated. Public native-window cancel/confirm signals follow real viewport button clicks because synthetic headless viewport input does not target OS popup windows. Checks cover read-only previews, independent modal pause, gated actions, secret hunt presentation, confirmation invalidation/repeated signals, compact/wide layouts, exact save/load and every executed command in verified bounded replay segments."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "expedition-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
