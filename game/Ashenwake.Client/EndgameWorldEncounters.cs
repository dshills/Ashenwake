using Ashenwake.Core.Endgame;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private WorldEncounterPanel _worldEncounterPanel = null!;
    private WorldEventPresentation _worldEncounterPresentation = null!;
    private PanelContainer _worldEncounterObjective = null!;
    private Label _worldEncounterObjectiveText = null!;
    private string _worldEncounterSelection = "", _worldEncounterNotice = "";

    private void InitializeWorldEncounters()
    {
        _worldEncounterPresentation = new WorldEventPresentation(); AddChild(_worldEncounterPresentation); _worldEncounterPresentation.Attach(_sandbox);
        _worldEncounterPanel = new WorldEncounterPanel(); _sandbox.AddOverlay(_worldEncounterPanel);
        _campaignHud.WorldEncountersRequested += () => { _worldEncounterNotice = ""; OpenWorldEncounters(); };
        _worldEncounterPanel.SelectionRequested += id => { _worldEncounterSelection = id; RefreshWorldEncounters(); };
        _worldEncounterPanel.ActionRequested += WorldEncounterAction;
        _worldEncounterPanel.ModalChanged += open => _sandbox.SetModalPaused("world-encounter-confirmation", open);
        _worldEncounterPanel.VisibilityChangedByPlayer += open =>
        {
            if (open)
            {
                _openingGuide?.SetOpen(false); _stashPanel?.SetOpen(false); _collection?.SetOpen(false); _wardrobe?.SetOpen(false); _bestiary?.SetOpen(false); _secretPanel?.SetOpen(false);
                _championPanel?.SetOpen(false); _huntBoard?.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel();
            }
            UpdatePanelVisibility();
        };
        _worldEncounterPanel.MenuRequested += action =>
        {
            _worldEncounterPanel.SetOpen(false);
            if (action == "aw_inventory") _character.ToggleInventory();
            else if (action == "aw_character") _character.Toggle();
            else if (action == "aw_experiment") ShowExperimentPanel();
            else if (_session.InWorldEncounter) OpenWorldEncounters();
            else if (_session.InRoamingChampion) OpenRoamingChampions();
            else if (_session.InSecretChamber) OpenSecretChambers();
            else if (_session.HasUnresolvedRegionalHunt) OpenRegionalHunts();
            else if (action == "aw_endgame" || _session.Combat.View.Endgame is not null) _board.ShowRun();
            else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); }
        };
        _worldEncounterObjective = new PanelContainer { Name = "WorldEncounterObjective", MouseFilter = Control.MouseFilterEnum.Ignore };
        _worldEncounterObjective.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("17212ce8"),
            BorderColor = new("82bfc1"),
            BorderWidthLeft = 3,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        });
        _worldEncounterObjectiveText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        _worldEncounterObjectiveText.AddThemeFontSizeOverride("font_size", 14); _worldEncounterObjective.AddChild(_worldEncounterObjectiveText); _sandbox.AddOverlay(_worldEncounterObjective);
    }

    private static bool IsWorldEncounterInteraction(string id) => WorldEncounterCatalog.Definitions.Any(d => id.StartsWith(d.Id + ".", StringComparison.Ordinal));
    private void OpenWorldEncounters(string id = "")
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true) return;
        if (id.Length > 0) _worldEncounterSelection = id;
        else if (_session.InWorldEncounter) _worldEncounterSelection = _session.CurrentWorldEncounter!.Id;
        RefreshWorldEncounters(); _worldEncounterPanel.SetOpen(true);
    }
    private bool InteractWorldEncounter(string id)
    {
        if (!IsWorldEncounterInteraction(id)) return false;
        var definition = WorldEncounterCatalog.Definitions.FirstOrDefault(d => id.StartsWith(d.Id + ".", StringComparison.Ordinal));
        if (definition is null) return false;
        var interaction = _session.Interactions.FirstOrDefault(i => i.ActionId == id);
        if (interaction is null || !NearWorldEncounter(interaction.Position, interaction.Range))
        { Notice("Move closer to inspect this encounter."); return true; }
        _worldEncounterNotice = ""; OpenWorldEncounters(definition.Id); return true;
    }
    private bool NearWorldEncounter(CorePosition position, int range) =>
        CorePosition.DistanceSquared(_session.Combat.View.Actors.Single(a => a.Id == 1).Position, position) <= (long)range * range;
    private void WorldEncounterAction(string action, string id)
    {
        if (action == "approach") { _sandbox.RequestWorldInteraction(id); return; }
        EndgameRuntimeCommand? command = action switch
        {
            "enter" => new(EndgameRuntimeAction.EnterWorldEncounter, Id: id),
            "choose" => new(EndgameRuntimeAction.ChooseWorldEncounter, Value: id),
            "claim" => new(EndgameRuntimeAction.ClaimWorldEncounterReward),
            "exit" => new(EndgameRuntimeAction.ExitWorldEncounter),
            _ => null
        };
        if (command is null) return;
        long before = _session.Tick; Apply(command);
        if (before == _session.Tick || action == "claim" || _session.WorldEncounters.Run?.Stage is "Foyer" or "Victory") OpenWorldEncounters(_worldEncounterSelection);
    }
    private void RefreshWorldEncounters()
    {
        if (_worldEncounterPanel is null) return;
        var view = _session.WorldEncounters;
        var entries = view.Entries.Select(e =>
        {
            var definition = WorldEncounterCatalog.Find(e.Id)!;
            return new WorldEncounterEntryDisplay(e.Id, e.Name, _campaignDefinition.Acts.Single(a => a.Number == e.Act).Name + " · " + e.Kind,
                e.Claimed ? "Treasure claimed" : e.Completed ? "Treasure awaits" : e.Here ? "Nearby" : "Discovered",
                definition.Description, EquipmentNames.For(definition.RewardItemId),
                e.Completed ? new[] { e.Id switch
                {
                    "event.lantern" => "The traveler survived. Their lantern marks the road home, and their hidden cache is yours.",
                    "event.caravan" => "The procession has finished its last journey. Its mourners can finally rest.",
                    "event.shrine" => "You endured the shrine's hunger. Its bargain leaves no permanent wound.",
                    _ => "The storm has been stabilized. Its temporary resonance has faded."
                } } : []);
        }).ToArray();
        var selected = view.Entries.FirstOrDefault(e => e.Id == _worldEncounterSelection);
        if (selected is null) { _worldEncounterSelection = entries.FirstOrDefault()?.Id ?? ""; selected = view.Entries.FirstOrDefault(e => e.Id == _worldEncounterSelection); }
        string heading = selected?.Name ?? "Stories waiting along the road";
        string narrative = "Explore secured campaign rooms for optional travelers, cursed objects, and Resonance Storms. Your campaign never requires these encounters.";
        var actions = new List<WorldEncounterActionDisplay>();
        if (view.Run is { } run && run.Id == _worldEncounterSelection)
        {
            heading = run.Name;
            narrative = run.Stage switch
            {
                "Foyer" => run.Description + "\n\n" + run.Counterplay + (run.Id == "event.caravan" ? $"\nKeepsakes laid to rest: {run.PuzzleStep}/3." : "") + "\nChoose when ready. You can leave safely.",
                "Combat" => run.Counterplay + (run.Id.StartsWith("storm.", StringComparison.Ordinal) ? "\nResonance surge: +25% fragment-effect damage while the storm is active." : "") + "\nRetreating resets an unfinished fight. No reward is granted for retreat.",
                "Victory" => "The encounter is complete. Claim your equipment and materials from the cache. The reward remains available if you leave to make inventory space.",
                "Claimed" => "Your treasure and discovery are recorded. Return to the campaign through the passage.",
                _ => "This attempt ended without a reward. Return to your campaign room, prepare, and try again. Earned campaign progress is preserved."
            };
            if (run.Stage == "Foyer")
            {
                var interaction = _session.Interactions.FirstOrDefault(i => i.ActionId == run.Id + ".choice");
                if (interaction is not null && !NearWorldEncounter(interaction.Position, interaction.Range)) actions.Add(new("approach", interaction.ActionId, "Approach the encounter"));
                else foreach (var choice in run.Choices)
                    {
                        bool startsFight = choice.Id is "rescue" or "escort" or "accept" or "stabilize";
                        string confirm = startsFight ? run.Id == "event.shrine" ? "Accept the shrine's hunger? You take 25% more damage for this fight. Leaving removes the penalty. " + run.Description
                            : "Begin this optional fight? " + run.Counterplay + " You may retreat through the entrance at any time." : "";
                        actions.Add(new("choose", choice.Id, choice.Label, run.CanChoose ? "" : "Keep one free backpack slot before accepting this encounter.", run.CanChoose, confirm));
                    }
            }
            if (run.Stage == "Victory") AddWorldAction("claim", run.Id + ".treasure", "Claim encounter treasure", run.CanClaim);
            actions.Add(new("exit", run.Id, "Return to the campaign", "Your source room and its ground loot are preserved.", run.CanExit,
                run.Stage == "Combat" ? "Retreat now? This unfinished fight resets and grants no reward. Its temporary effects end when you leave." : ""));
        }
        else if (selected is not null)
        {
            var definition = WorldEncounterCatalog.Find(selected.Id)!;
            narrative = definition.Description + "\n\n" + definition.Counterplay;
            if (selected.Here) AddWorldAction("enter", selected.Id + ".enter", "Inspect " + selected.Name, selected.CanEnter);
            else narrative += "\nReturn to " + Readable(definition.SourceEncounterId) + " to revisit this encounter.";
        }
        _worldEncounterPanel.SetView(new(entries, _worldEncounterSelection, heading, narrative, actions.ToArray(), _revision, _worldEncounterNotice));
        var current = _session.CurrentWorldEncounter;
        _worldEncounterPresentation.Present(new(current?.Act ?? _session.Campaign.View.Act, _session.InWorldEncounter, view.Run?.Stage ?? "", current?.Id ?? "", _session.Interactions.ToArray()));
        _worldEncounterObjective.Visible = WorldEncounterObjectiveVisible();
        if (view.Run is { } active)
        {
            CombatHudLayout.Objective(_worldEncounterObjective, GetViewport().GetVisibleRect().Size);
            _worldEncounterObjectiveText.Text = active.Name.ToUpperInvariant() + "\n" + (active.Stage switch
            {
                "Combat" => active.Counterplay,
                "Foyer" => "A quiet threshold. Inspect the encounter and choose when ready.",
                "Victory" => "Encounter complete. Collect the waiting treasure.",
                "Claimed" => "Treasure claimed. Return through the passage.",
                _ => "Attempt ended. Return through the passage to recover."
            }) + (active.Stage == "Combat" && active.Id.StartsWith("storm.", StringComparison.Ordinal) ? "\nRESONANCE SURGE · +25% fragment-effect damage" : "") + "\nJ: encounter journal · You may leave at any time";
        }
        void AddWorldAction(string action, string interactionId, string label, bool enabled)
        {
            var target = _session.Interactions.FirstOrDefault(i => i.ActionId == interactionId);
            if (target is not null && !NearWorldEncounter(target.Position, target.Range)) actions.Add(new("approach", interactionId, "Approach: " + label));
            else actions.Add(new(action, action == "enter" ? selected!.Id : view.Run!.Id, label,
                enabled ? "" : action == "claim" ? "Make room in your backpack, then approach the treasure." : "Approach this encounter in its secured campaign room.", enabled));
        }
    }
    private bool WorldEncounterObjectiveVisible() => _session.InWorldEncounter && _worldEncounterPanel?.IsOpen != true && _deathRecapHud?.IsOpen != true && _frontMenu?.IsOpen != true && _training is null;
    private static string WorldEncounterStyle(WorldEncounterDefinition definition) => definition.Act switch
    {
        2 => "verdant_hunt",
        3 => "cinder_storm",
        4 => "spine_causeway",
        5 => "hollow_rooms",
        _ => definition.Id == "event.caravan" ? "monastery" : definition.Id == "event.shrine" ? "crypt" : "road"
    };
}
