using Ashenwake.Core.Endgame;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private RoamingChampionPanel _championPanel = null!;
    private RoamingChampionPresentation _championPresentation = null!;
    private PanelContainer _championObjective = null!;
    private Label _championObjectiveText = null!;
    private string _championSelection = "", _championNotice = "";

    private void InitializeRoamingChampions()
    {
        _championPresentation = new RoamingChampionPresentation(); AddChild(_championPresentation); _championPresentation.Attach(_sandbox);
        _championPanel = new RoamingChampionPanel(); _sandbox.AddOverlay(_championPanel);
        _campaignHud.ChampionsRequested += () => OpenRoamingChampions();
        _championPanel.SelectionRequested += id => { _championSelection = id; RefreshRoamingChampions(); };
        _championPanel.ActionRequested += ChampionAction;
        _championPanel.ModalChanged += open => _sandbox.SetModalPaused("champion-confirmation", open);
        _championPanel.VisibilityChangedByPlayer += open =>
        {
            if (open) { _stashPanel.SetOpen(false); _collection.SetOpen(false); _secretPanel.SetOpen(false); _huntBoard.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel(); }
            UpdatePanelVisibility();
        };
        _championPanel.MenuRequested += action =>
        {
            _championPanel.SetOpen(false); _worldEncounterPanel?.SetOpen(false);
            if (action == "aw_inventory") _character.ToggleInventory();
            else if (action == "aw_character") _character.Toggle();
            else if (action == "aw_experiment") ShowExperimentPanel();
            else if (_session.InRoamingChampion) OpenRoamingChampions();
            else if (_session.InSecretChamber) OpenSecretChambers();
            else if (_session.HasUnresolvedRegionalHunt) OpenRegionalHunts();
            else if (action == "aw_endgame" || _session.Combat.View.Endgame is not null) _board.ShowRun();
            else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); }
        };
        _championObjective = new PanelContainer { Name = "RoamingChampionObjective", MouseFilter = Control.MouseFilterEnum.Ignore };
        _championObjective.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new("17212ce8"), BorderColor = new("c3a576"), BorderWidthLeft = 3, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 });
        _championObjectiveText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        _championObjectiveText.AddThemeFontSizeOverride("font_size", 14); _championObjective.AddChild(_championObjectiveText); _sandbox.AddOverlay(_championObjective);
    }
    private void OpenRoamingChampions(string id = "")
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true) return;
        if (id.Length > 0) _championSelection = id;
        else if (_session.InRoamingChampion) _championSelection = _session.CurrentRoamingChampion!.Id;
        RefreshRoamingChampions(); _championPanel.SetOpen(true);
    }
    private bool InteractRoamingChampion(string id)
    {
        if (!id.StartsWith("champion.", StringComparison.Ordinal)) return false;
        var target = _session.Interactions.FirstOrDefault(i => i.ActionId == id);
        var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        if (target is null || CorePosition.DistanceSquared(target.Position, player.Position) > (long)target.Range * target.Range)
        { Notice("Move closer to inspect this champion's refuge."); return true; }
        var definition = RoamingChampionCatalog.Definitions.FirstOrDefault(d => id.StartsWith(d.Id + ".", StringComparison.Ordinal));
        if (definition is not null) OpenRoamingChampions(definition.Id);
        return true;
    }
    private void ChampionAction(string action, string id)
    {
        if (action == "approach") { _sandbox.RequestWorldInteraction(id); return; }
        EndgameRuntimeCommand? command = action switch
        {
            "enter" => new(EndgameRuntimeAction.EnterRoamingChampion, Id: id),
            "challenge" => new(EndgameRuntimeAction.ChallengeRoamingChampion),
            "claim" => new(EndgameRuntimeAction.ClaimRoamingChampionReward),
            "exit" => new(EndgameRuntimeAction.ExitRoamingChampion),
            _ => null
        };
        if (command is null) return;
        long before = _session.Tick; Apply(command);
        if (_session.Tick == before || action == "claim") OpenRoamingChampions(_championSelection);
    }
    private void RefreshRoamingChampions()
    {
        if (_championPanel is null) return;
        var view = _session.RoamingChampions;
        if (view.Run is { } currentRun) _championSelection = currentRun.Id;
        var entries = view.Entries.Where(e => e.Discovered).Select(e => new RoamingChampionEntryDisplay(e.Id, e.Name,
            _campaignDefinition.Acts.Single(a => a.Number == e.Act).Name,
            e.Claimed ? "Treasure claimed" : e.Defeated ? "Treasure awaits" : "Champion sighted", e.Description, e.Counterplay, EquipmentNames.For(e.RewardItemId))).ToArray();
        var selected = view.Entries.FirstOrDefault(e => e.Id == _championSelection && e.Discovered);
        if (selected is null) { _championSelection = entries.FirstOrDefault()?.Id ?? ""; selected = view.Entries.FirstOrDefault(e => e.Id == _championSelection); }
        string heading = selected?.Name ?? "Unrecorded champions";
        string narrative = "Explore side areas after securing a regional encounter. Named champions are optional; inspect their tactics before choosing a challenge.";
        var actions = new List<RoamingChampionActionDisplay>();
        if (view.Run is { } run)
        {
            heading = run.Name;
            narrative = run.Stage switch
            {
                "Foyer" => "The champion waits beyond this safe threshold. It attacks only after you accept the challenge.",
                "Combat" => "Retreat is always available. An unfinished fight resets when you leave.",
                "Victory" => "The champion has fallen. Claim its signature equipment. Your victory and unclaimed treasure persist if you leave.",
                "Claimed" => "This character has claimed the champion's signature treasure. Return to your regional route when ready.",
                _ => "The champion prevailed. Return to the preserved campaign room, prepare your build, and try again. No treasure was awarded."
            };
            if (run.Stage == "Foyer") AddWorldAction("challenge", run.Id + ".challenge", "Challenge " + run.Name, run.CanChallenge,
                "Accept this optional fight? You may retreat at any time. Leaving an unfinished fight resets the champion.");
            if (run.Stage == "Victory") AddWorldAction("claim", run.Id + ".treasure", "Claim signature treasure", run.CanClaim);
            actions.Add(new("exit", run.Id, "Return to the region", "Return to the campaign room you left, preserving its ground loot and progress.", run.CanExit,
                run.Stage == "Combat" ? "Retreat now? The unfinished fight resets and awards no treasure." : ""));
        }
        else if (selected is not null)
        {
            narrative = selected.Claimed ? "Its signature treasure has already been claimed." : selected.Defeated ? "Victory is recorded. Return to claim its waiting treasure." : "Inspect the refuge before accepting a fight. Each champion holds one signature treasure for this character.";
            if (selected.Here) AddWorldAction("enter", selected.Id + ".sighting", "Enter the champion's refuge", true);
            else narrative += "\nReturn to " + (_combat.Campaign?.Encounters.FirstOrDefault(e => e.Id == selected.SourceEncounterId)?.Name ?? Readable(selected.SourceEncounterId)) + " to find the recorded sighting.";
        }
        _championPanel.SetView(new(entries, _championSelection, heading, narrative, actions.ToArray(), _revision, _championNotice, view.Run?.Id ?? ""));
        var current = _session.CurrentRoamingChampion;
        _championPresentation.Present(new(current?.Act ?? _session.Campaign.View.Act, _session.InRoamingChampion,
            view.Run?.Stage ?? "", current?.Id ?? "", _session.Interactions.ToArray()));
        _championObjective.Visible = ChampionObjectiveVisible();
        if (view.Run is { } active)
        {
            CombatHudLayout.Objective(_championObjective, GetViewport().GetVisibleRect().Size);
            _championObjectiveText.Text = active.Name.ToUpperInvariant() + "\n" + (active.Stage == "Combat" ? active.Counterplay : active.Stage == "Foyer" ? "Inspect the champion and accept its challenge when ready." : active.Stage == "Victory" ? "Champion defeated. Collect its signature treasure." : active.Stage == "Claimed" ? "Treasure claimed. Return to the region." : "Return to the region to recover.") + "\nJ: champion journal · Retreat through the return marker";
        }
        void AddWorldAction(string action, string interactionId, string label, bool enabled, string confirm = "")
        {
            var target = _session.Interactions.FirstOrDefault(i => i.ActionId == interactionId);
            bool near = target is not null && CorePosition.DistanceSquared(target.Position, _session.Combat.View.Actors.Single(a => a.Id == 1).Position) <= (long)target.Range * target.Range;
            if (!near && target is not null) actions.Add(new("approach", interactionId, "Approach: " + label));
            else actions.Add(new(action, action == "enter" ? selected!.Id : view.Run!.Id, label,
                enabled ? "" : "Make room in your backpack before accepting the challenge or claiming treasure.", enabled, confirm));
        }
    }
    private bool ChampionObjectiveVisible() => _session.InRoamingChampion && _championPanel?.IsOpen != true && _deathRecapHud?.IsOpen != true && _frontMenu?.IsOpen != true && _training is null;
}
