using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private OpeningGuidancePanel _openingGuide = null!;
    private OpeningGuidanceMemory? _openingGuidanceMemory;
    private string _openingGuidancePath = "", _openingGuidanceNotice = "", _openingGuidanceKey = "";
    private bool _openingGuidanceCanWrite;
    private CombatView? _openingGuidanceBefore;

    private void InitializeOpeningGuidance()
    {
        _openingGuide = new OpeningGuidancePanel(); _sandbox.AddOverlay(_openingGuide); _openingGuide.Attach(_sandbox);
        _openingGuide.HintAllowed = OpeningHintAllowed;
        _campaignHud.OpeningGuideRequested += OpenOpeningGuide;
        _openingGuide.DismissRequested += id => ChangeOpeningGuidance(memory => OpeningGuidance.Dismiss(memory, id));
        _openingGuide.EnabledRequested += enabled => ChangeOpeningGuidance(memory => OpeningGuidance.SetEnabled(memory, enabled));
        _openingGuide.ActionRequested += OpeningGuideAction;
        _openingGuide.VisibilityChangedByPlayer += _ => UpdatePanelVisibility();
        _openingGuide.MenuRequested += action =>
        {
            if (action == "aw_inventory") _character.ToggleInventory();
            else if (action == "aw_character") _character.Toggle();
            else if (action == "aw_experiment") ShowExperimentPanel();
            else if (action == "aw_endgame") _board.ShowRun();
            else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); }
        };
    }
    private bool OpeningHintAllowed() => _hasActiveCharacter && !_smoke && !_echoesSmoke && _training is null &&
        !_sandbox.IsPaused && !_frontMenu.IsOpen && !_campaignHud.IsOpen && !_character.IsOpen && !_board.IsOpen &&
        _collection?.IsOpen != true && _stashPanel?.IsOpen != true && _huntBoard?.IsOpen != true && _secretPanel?.IsOpen != true &&
        _championPanel?.IsOpen != true && _deathRecapHud?.IsOpen != true && _openingGuide?.IsOpen != true;

    private void OpenOpeningGuide()
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true ||
            _session.InRoamingChampion || _session.InSecretChamber || _session.HasUnresolvedRegionalHunt || _session.Combat.View.Endgame is not null) return;
        _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel();
        _collection.SetOpen(false); _stashPanel.SetOpen(false); _huntBoard.SetOpen(false); _secretPanel.SetOpen(false); _championPanel.SetOpen(false);
        RefreshOpeningGuidance(true); _openingGuide.SetOpen(true);
    }
    private void RefreshOpeningGuidance(bool force = false)
    {
        if (_openingGuide is null || !_hasActiveCharacter) return;
        string path = Path.Combine(_output, ActiveCharacterFilename);
        if (_openingGuidanceMemory is null || _openingGuidancePath != path)
        {
            string character = _session.Production.Capture().Progression.Character.CharacterId;
            var loaded = OpeningGuidanceStore.Load(path, character);
            _openingGuidanceMemory = loaded.Memory; _openingGuidancePath = path;
            _openingGuidanceCanWrite = loaded.CanWrite; _openingGuidanceNotice = loaded.Notice; _openingGuidanceKey = "";
        }
        var combat = _session.Combat.View;
        var player = combat.Actors.Single(a => a.Id == 1);
        bool nearby = _session.Interactions.Any(i => Ashenwake.Core.Simulation.Position.DistanceSquared(player.Position, i.Position) <= (long)i.Range * i.Range);
        string warnings = string.Join(',', combat.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.TelegraphTicks > 0).Select(a => a.Role));
        bool supportWarning = combat.CampaignHazards?.Any(h => h.RemainingTicks > 0 && CombatSession.IsSupportHazard(h.ContentId)) == true;
        bool damageWarning = combat.CampaignHazards?.Any(h => h.RemainingTicks > 0 && !CombatSession.IsSupportHazard(h.ContentId)) == true;
        string key = $"{_revision}:{_session.InHub}:{_session.Campaign.ActiveEncounterId}:{combat.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)}:{combat.Loot.Count}:{player.Health > 0}:{supportWarning}:{damageWarning}:{warnings}:{nearby}";
        if (force || key != _openingGuidanceKey)
        {
            _openingGuidanceKey = key;
            _openingGuide.SetView(OpeningGuidance.Project(_session, _openingGuidanceMemory), _openingGuidanceMemory.Enabled, _openingGuidanceNotice);
        }
        _openingGuide.SetHintVisible(true);
    }
    private void ChangeOpeningGuidance(Func<OpeningGuidanceMemory, OpeningGuidanceMemory> change)
    {
        if (_openingGuidanceMemory is null) return;
        var changed = change(_openingGuidanceMemory);
        if (changed.Enabled == _openingGuidanceMemory.Enabled && changed.Dismissed.SequenceEqual(_openingGuidanceMemory.Dismissed) && changed.Completed.SequenceEqual(_openingGuidanceMemory.Completed)) return;
        _openingGuidanceMemory = changed;
        if (_openingGuidanceCanWrite)
        {
            try { _openingGuidanceMemory = OpeningGuidanceStore.Write(_openingGuidancePath, changed); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { _openingGuidanceCanWrite = false; _openingGuidanceNotice = "Guidance preferences remain available this session. Existing files are preserved: " + ex.Message; }
        }
        _openingGuidanceKey = "";
        RefreshOpeningGuidance(true);
    }
    private void CompleteOpeningGuidance(string id)
    { if (_openingGuidanceMemory?.Completed.Contains(id) == false) ChangeOpeningGuidance(memory => OpeningGuidance.Complete(memory, id)); }
    private void BeginOpeningGuidanceObservation()
    {
        _openingGuidanceBefore = _openingGuidanceMemory is not null && _hasActiveCharacter && !_smoke && !_echoesSmoke &&
            !_session.InRoamingChampion && !_session.InSecretChamber && !_session.HasUnresolvedRegionalHunt && _session.Combat.View.Endgame is null
            ? _session.Combat.View : null;
    }
    private void ObserveOpeningGuidance(EndgameRuntimeResult result)
    {
        var before = _openingGuidanceBefore; _openingGuidanceBefore = null;
        if (!result.Success || _openingGuidanceMemory is null || _smoke || _echoesSmoke) return;
        if (before is not null && result.WorldEvents.Length == 0 && before.Actors.Single(a => a.Id == 1).Position != _session.Combat.View.Actors.Single(a => a.Id == 1).Position)
            CompleteOpeningGuidance("hint.move");
        if (result.WorldEvents.Any(e => e.StartsWith("Dialogue:", StringComparison.Ordinal) || e.StartsWith("ServiceOpened:", StringComparison.Ordinal))) CompleteOpeningGuidance("hint.interact");
        if (result.CombatEvents.Any(e => e.Kind == "Dodged" && e.ActorId == 1)) CompleteOpeningGuidance("hint.dodge");
        if (result.CombatEvents.Any(e => e.Kind == "LootPickedUp" && e.ActorId == 1)) CompleteOpeningGuidance("hint.loot");
        if (before is not null && result.CombatEvents.Any(e => e.Kind == "StatusApplied" && e.ActorId == 1 && e.ContentId is "Staggered" or "Frozen" or "Terrified" &&
            !result.CombatEvents.Any(resolved => resolved.ActorId == e.TargetId && resolved.Kind is "AbilityResolved" or "EliteAbilityResolved" or "CampaignHazardResolved") &&
            (before.Actors.Any(a => a.Id == e.TargetId && a.Faction == CombatFaction.Enemy && a.TelegraphTicks > 0) || before.CampaignHazards?.Any(h => h.SourceId == e.TargetId && h.RemainingTicks > 0) == true)))
            CompleteOpeningGuidance("hint.interrupt");
    }
    private void OpeningGuideAction(string action, string target)
    {
        if (_openingGuidanceMemory is null || !_hasActiveCharacter || _training is not null || _frontMenu.IsOpen) return;
        var view = OpeningGuidance.Project(_session, _openingGuidanceMemory);
        var card = view.Services.Concat(view.Steps).Concat(view.Basics).FirstOrDefault(c => c.Action == action && c.Target == target && action.Length > 0);
        if (card is null) return;
        if (view.Services.Contains(card)) ChangeOpeningGuidance(memory => OpeningGuidance.Dismiss(memory, card.Id));
        _openingGuide.SetOpen(false);
        switch (action)
        {
            case "inspect_gear": _character.InspectOpeningReward(); break;
            case "inspect_anatomy": _campaignHud.OpenAnatomyReward(); break;
            case "stash": OpenPersonalStash(); break;
            case "hunts": OpenRegionalHunts(); break;
            case "service":
            case "training":
                if (_session.InHub && _session.Interactions.Any(i => i.ActionId == target)) _sandbox.RequestWorldInteraction(target);
                else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); _campaignHud.Notice("Return to Greyhaven to visit this service. Review the route and any remaining loot before travelling."); }
                break;
            default: _campaignHud.Visible = true; _campaignHud.SetOpen(true); break;
        }
    }
    private void ResetOpeningGuidance()
    {
        _openingGuide?.Reset(); _openingGuidanceMemory = null; _openingGuidanceBefore = null;
        _openingGuidancePath = _openingGuidanceNotice = _openingGuidanceKey = "";
    }
}
