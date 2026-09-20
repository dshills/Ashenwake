using System.Reflection;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Godot;

namespace Ashenwake.Client;

/// <summary>Visual Journey checks use the smoke's earned campaign and isolated save files.</summary>
public partial class JourneySmoke
{
    private int _navigationRequests;
    private readonly List<JourneyReplayEvidence> _journeyReplaySegments = [];
    private sealed record JourneyReplayEvidence(string File, int Frames, string StateHash);
    private HashSet<string> JourneyPauseOwners => (HashSet<string>)(typeof(Sandbox)
        .GetField("_modalPauses", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(_sandbox)
        ?? throw new MissingFieldException("_modalPauses"));

    private T JourneyControl<T>(string name) where T : Node => Descendants(_hud).OfType<T>().Single(n => n.Name == name);
    private static string JourneyText(Node root) => string.Join("\n", Descendants(root)
        .Select(n => n is Label label ? label.Text : n is Button button ? button.Text : ""));
    private string JournalText => JourneyText(JourneyControl<Control>("JourneyJournalContent"));
    private string RegionText(int number)
    {
        var button = JourneyControl<Button>("JourneyRegion" + number);
        return button.Text + " " + button.TooltipText;
    }

    private async Task CheckJourneyOpening()
    {
        string hash = _session.StateHash; int frames = _session.CaptureReplay().Frames.Length;
        Check("journey_has_hub_and_five_inspectable_regions", Enumerable.Range(0, 6).All(i =>
            JourneyControl<Button>("JourneyRegion" + i) is { Disabled: false } region && region.IsVisibleInTree()));
        Check("journey_marks_current_hub", _hud.SelectedJourneyRegion == 0 &&
            (RegionText(0).Contains("here", StringComparison.OrdinalIgnoreCase) || RegionText(0).Contains("current", StringComparison.OrdinalIgnoreCase)));
        await ClickNode(JourneyControl<Button>("JourneyRegion5"));
        Check("locked_region_is_inspectable_without_travel", _hud.SelectedJourneyRegion == 5 &&
            JourneyControl<Button>("JourneyTravel").Disabled && RegionText(5).Contains("locked", StringComparison.OrdinalIgnoreCase));
        Check("locked_region_inspection_masks_future_revelations", _campaign.Capture().Acts.All(act =>
            !JourneyText(_hud).Contains(act.Revelation, StringComparison.Ordinal)));
        await ClickNode(JourneyControl<Button>("JourneyRegion1"));
        Check("first_region_preview_exposes_explicit_travel", !JourneyControl<Button>("JourneyTravel").Disabled &&
            JourneyControl<Button>("JourneyTravel").Text.StartsWith("Travel to Act 1", StringComparison.Ordinal));
        Check("region_inspection_preserves_world_and_replay", _session.StateHash == hash &&
            _session.CaptureReplay().Frames.Length == frames && _navigationRequests == 0);
        Check("journey_holds_its_modal_pause", _sandbox.IsPaused && JourneyPauseOwners.Contains("journey-panel") &&
            !JourneyPauseOwners.Contains("divine-anatomy"));
        JourneyKey(Key.F); JourneyKey(Key.Period); await Settle();
        Check("journey_interact_and_single_step_are_blocked", _session.StateHash == hash &&
            _session.CaptureReplay().Frames.Length == frames && _navigationRequests == 0);

        _sandbox.SetModalPaused("journey-smoke-external", true);
        await Click("Close");
        Check("closing_journey_preserves_other_pause_owners", _sandbox.IsPaused &&
            !JourneyPauseOwners.Contains("journey-panel") && JourneyPauseOwners.Contains("journey-smoke-external"));
        await Click("Journey map & anatomy"); _sandbox.SetModalPaused("journey-smoke-external", false); await Settle();
        Check("reopening_journey_reacquires_only_its_pause", JourneyPauseOwners.Contains("journey-panel") &&
            !JourneyPauseOwners.Contains("journey-smoke-external") && _sandbox.IsPaused);
        await CheckJourneyLayouts();
        await Capture("journey-greyhaven-map.png");

        await Click("Journal");
        Check("journal_offers_four_categories", new[] { "Objectives", "Discoveries", "Choices", "People" }.All(category =>
            JourneyControl<Button>("JourneyJournal" + category).IsVisibleInTree()));
        foreach (string category in new[] { "Objectives", "Discoveries", "Choices", "People" })
        {
            await ClickNode(JourneyControl<Button>("JourneyJournal" + category));
            Check("fresh_" + category.ToLowerInvariant() + "_journal_masks_unrevealed_story", _campaign.Capture().Acts.All(act =>
                !JournalText.Contains(act.Revelation, StringComparison.Ordinal)) && _campaign.Capture().Choices.All(choice =>
                !JournalText.Contains(choice.Prompt, StringComparison.Ordinal) && choice.Outcomes.All(outcome =>
                    !JournalText.Contains(outcome.Text, StringComparison.Ordinal))));
        }
        Check("people_journal_contains_known_mara", JournalText.Contains("Mara Vey", StringComparison.Ordinal));
        Check("journal_inspection_preserves_world_and_replay", _session.StateHash == hash &&
            _session.CaptureReplay().Frames.Length == frames && _navigationRequests == 0);
        await Click("Map");
    }

    private async Task CheckJourneyLayouts()
    {
        foreach (var size in new[] { new Vector2I(1280, 800), new Vector2I(1280, 720), new Vector2I(780, 800) })
        {
            GetWindow().Size = GetWindow().ContentScaleSize = size; await Frames(6);
            var viewport = GetViewport().GetVisibleRect();
            var panel = JourneyControl<Control>("CampaignPanel");
            var map = JourneyControl<Control>("JourneyMap");
            var travel = JourneyControl<Button>("JourneyTravel");
            Check("journey_map_and_actions_fit_" + size.X + "x" + size.Y, viewport.Encloses(panel.GetGlobalRect()) &&
                viewport.Encloses(map.GetGlobalRect()) && viewport.Encloses(travel.GetGlobalRect()) &&
                viewport.Encloses(FindButton("Close").GetGlobalRect()) && Enumerable.Range(0, 6).All(i =>
                    map.GetGlobalRect().Encloses(JourneyControl<Button>("JourneyRegion" + i).GetGlobalRect())));
            await Capture("journey-layout-" + size.X + "x" + size.Y + ".png");
        }
        GetWindow().Size = GetWindow().ContentScaleSize = new(1280, 800); await Frames(6);
    }

    private async Task CheckRoadTravelConfirmation()
    {
        string hash = _session.StateHash; int frames = _session.CaptureReplay().Frames.Length, requests = _navigationRequests;
        int loot = _session.Combat.View.Loot.Count;
        await ClickNode(FindButton("Continue onward"));
        var dialog = JourneyControl<ConfirmationDialog>("JourneyTravelConfirmation");
        Check("loot_departure_requires_explicit_confirmation", dialog.Visible && _session.StateHash == hash &&
            dialog.DialogText.Contains(loot.ToString(), StringComparison.Ordinal));
        await Capture("journey-loot-confirmation.png");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide(); await Settle();
        Check("cancelled_loot_departure_keeps_room_rewards_and_history", _session.StateHash == hash &&
            _session.Combat.View.Loot.Count == loot && _session.CaptureReplay().Frames.Length == frames && _navigationRequests == requests);

        await ClickNode(FindButton("Continue onward"));
        _hud.SelectJourneyRegion(0); await Settle(); bool selectionCanceled = !dialog.Visible;
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Settle();
        Check("region_selection_invalidates_pending_loot_departure", selectionCanceled &&
            _session.StateHash == hash && _navigationRequests == requests);

        await ClickNode(JourneyControl<Button>("JourneyRegion1"));
        await ClickNode(FindButton("Continue onward"));
        _hud.SetOpen(false); await Settle(); bool closeCanceled = !dialog.Visible;
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Settle();
        Check("closing_journey_invalidates_pending_loot_departure", closeCanceled &&
            _session.StateHash == hash && _navigationRequests == requests && !JourneyPauseOwners.Contains("journey-panel"));
        _hud.SetOpen(true); await Settle();

        await ClickNode(FindButton("Continue onward"));
        RecordJourneyReplay("journey.before-restore.replay.json");
        string save = Path.Combine(_output, "journey.road.save.json");
        CampaignRuntimeSaveStore.Write(save, _combatJson, _adventure, _progression, _campaign, _session.Capture());
        _session = CampaignRuntimeSaveStore.Load(save, _combatJson, _adventure, _progression, _campaign).Session;
        _sandbox.SetSession(_session.Combat); Refresh(); _hud.AnatomySessionRestored(); await Settle();
        bool restoredCanceled = !dialog.Visible;
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Settle();
        Check("loading_same_journey_invalidates_pending_loot_departure", restoredCanceled &&
            _session.StateHash == hash && _session.Combat.View.Loot.Count == loot && _navigationRequests == requests);
        Check("restored_journey_reacquires_modal_pause", JourneyPauseOwners.Contains("journey-panel") && _sandbox.IsPaused);
        await ClickNode(FindButton("Continue onward"));
        Check("restored_journey_can_request_fresh_travel_confirmation", dialog.Visible);
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.Hide(); await Settle();
        Check("confirmed_loot_departure_submits_exactly_once", _navigationRequests == requests + 1 &&
            _session.ActiveEncounterId == "campaign.monastery" && !JourneyPauseOwners.Contains("journey-panel"));
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Settle();
        Check("consumed_loot_confirmation_cannot_repeat_departure", _navigationRequests == requests + 1 &&
            _session.ActiveEncounterId == "campaign.monastery");
    }

    private async Task CheckEarnedJourneyJournal(string choiceId, string outcomeId)
    {
        string hash = _session.StateHash; int frames = _session.CaptureReplay().Frames.Length, requests = _navigationRequests;
        var content = _campaign.Capture(); var choice = content.Choices.Single(c => c.Id == choiceId);
        string selected = choice.Outcomes.Single(o => o.Id == outcomeId).Text;
        _hud.OpenJourneyJournal("Choices"); await Settle();
        Check("journal_records_actual_committed_choice", JournalText.Contains(selected, StringComparison.Ordinal) &&
            _session.Capture().Campaign.Choices.GetValueOrDefault(choiceId) == outcomeId);
        Check("journal_hides_unreached_choice_prompts_and_outcomes", content.Choices.Where(c => c.Act > 1).All(c =>
            !JournalText.Contains(c.Prompt, StringComparison.Ordinal) && c.Outcomes.All(o => !JournalText.Contains(o.Text, StringComparison.Ordinal))));
        await Capture("journey-journal-choices.png");
        await ClickNode(JourneyControl<Button>("JourneyJournalDiscoveries"));
        Check("discoveries_reveal_earned_monastery_testimony", JournalText.Contains(content.Acts.Single(a => a.Number == 1).Revelation, StringComparison.Ordinal));
        Check("discoveries_mask_future_regions_testimony", content.Acts.Where(a => a.Number > 1).All(a =>
            !JournalText.Contains(a.Revelation, StringComparison.Ordinal)));
        await Capture("journey-journal-discoveries.png");
        await ClickNode(JourneyControl<Button>("JourneyJournalPeople"));
        Check("people_journal_tracks_actual_rescues", _session.View.Residents.All(person => JournalText.Contains(person, StringComparison.Ordinal)) &&
            _session.View.Residents.Contains("Torren Bale") && _session.View.Residents.Contains("Sister Cael"));
        await ClickNode(JourneyControl<Button>("JourneyJournalObjectives"));
        Check("journal_objectives_have_current_region", JournalText.Contains(_session.View.Region, StringComparison.OrdinalIgnoreCase));
        Check("earned_journal_is_a_read_only_projection", _session.StateHash == hash &&
            _session.CaptureReplay().Frames.Length == frames && _navigationRequests == requests);
    }

    private async Task CheckStoryChoiceCancellation(string outcome, ConfirmationDialog dialog)
    {
        string hash = _session.StateHash; int frames = _session.CaptureReplay().Frames.Length, requests = _navigationRequests;
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); dialog.Hide(); await Settle();
        Check("canceling_story_confirmation_keeps_choice_uncommitted", _session.StateHash == hash &&
            _navigationRequests == requests && _session.CaptureReplay().Frames.Length == frames);
        await Click(outcome);
        _hud.OpenJourneyJournal("Choices"); await Settle(); bool canceled = !dialog.Visible;
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); await Settle();
        Check("changing_journey_tab_invalidates_pending_story_choice", canceled && _session.StateHash == hash && _navigationRequests == requests);
        await Click("Story"); await Click(outcome);
        Check("story_choice_can_be_confirmed_after_fresh_inspection", dialog.Visible);
    }

    private async Task CheckCompletedJourneyMap()
    {
        string hash = _session.StateHash; int requests = _navigationRequests;
        Check("journey_map_marks_earned_first_region_complete", _session.Capture().Campaign.CompletedActs.Contains(1) &&
            RegionText(1).Contains("complete", StringComparison.OrdinalIgnoreCase));
        Check("journey_map_exposes_newly_unlocked_region", _session.View.AvailableActs.Contains(2) &&
            !System.Text.RegularExpressions.Regex.IsMatch(RegionText(2), @"\blocked\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        await ClickNode(JourneyControl<Button>("JourneyRegion2"));
        Check("next_region_selection_only_previews_destination", _hud.SelectedJourneyRegion == 2 &&
            _session.StateHash == hash && _navigationRequests == requests && !JourneyControl<Button>("JourneyTravel").Disabled);
        Check("next_region_has_named_travel_action", JourneyControl<Button>("JourneyTravel").Text.StartsWith("Travel to Act 2", StringComparison.Ordinal));
        await Capture("journey-first-region-complete.png");
        await ClickNode(JourneyControl<Button>("JourneyTravel"));
        var dialog = JourneyControl<ConfirmationDialog>("JourneyTravelConfirmation");
        Check("new_region_travel_preserves_boss_loot_until_confirmed", dialog.Visible && _session.StateHash == hash &&
            _session.Combat.View.Loot.Count > 0 && _navigationRequests == requests);
        dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed); dialog.Hide(); await Settle();
        Check("new_region_travel_uses_single_authoritative_request", _navigationRequests == requests + 1 &&
            _session.Capture().Campaign.CurrentAct == 2);
    }

    private async Task CheckJourneySaveReplay()
    {
        string hash = _session.StateHash;
        string save = Path.Combine(_output, "journey.save.json");
        CampaignRuntimeSaveStore.Write(save, _combatJson, _adventure, _progression, _campaign, _session.Capture());
        var loaded = CampaignRuntimeSaveStore.Load(save, _combatJson, _adventure, _progression, _campaign).Session;
        Check("journey_save_restores_exact_campaign_rewards_and_choices", loaded.StateHash == hash &&
            JsonData.Hash(loaded.Capture().Campaign) == JsonData.Hash(_session.Capture().Campaign));
        RecordJourneyReplay("journey.replay.json");
        await Settle();
    }

    private void RecordJourneyReplay(string file)
    {
        var replay = _session.CaptureReplay();
        var result = CampaignRuntimeReplayRunner.Run(_combatJson, _adventure, _progression, _campaign, replay);
        Check(file + "_matches_authoritative_state", result.Success && result.FinalHash == _session.StateHash);
        System.IO.File.WriteAllText(Path.Combine(_output, file), JsonData.Write(replay));
        _journeyReplaySegments.Add(new(file, replay.Frames.Length, _session.StateHash));
    }

    private void JourneyKey(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
    }
}
