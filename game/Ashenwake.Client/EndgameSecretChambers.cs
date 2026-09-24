using Ashenwake.Core.Endgame;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private SecretChamberPanel _secretPanel = null!;
    private SecretChamberPresentation _secretPresentation = null!;
    private PanelContainer _secretObjective = null!;
    private Label _secretObjectiveText = null!;
    private string _secretSelection = "", _secretClue = "", _secretNotice = "";

    private void InitializeSecretChambers()
    {
        _secretPresentation = new SecretChamberPresentation(); AddChild(_secretPresentation);
        _secretPanel = new SecretChamberPanel(); _sandbox.AddOverlay(_secretPanel);
        _campaignHud.DiscoveriesRequested += () => OpenSecretChambers();
        _secretPanel.SelectionRequested += id => { _secretSelection = id; _secretClue = ""; RefreshSecretChambers(); };
        _secretPanel.ActionRequested += SecretAction;
        _secretPanel.VisibilityChangedByPlayer += open =>
        {
            if (open) { _stashPanel?.SetOpen(false); _championPanel?.SetOpen(false); _worldEncounterPanel?.SetOpen(false); _collection?.SetOpen(false); _wardrobe?.SetOpen(false); _bestiary?.SetOpen(false); _huntBoard?.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false); _character.Close(); CloseExperimentPanel(); }
            UpdatePanelVisibility();
        };
        _secretPanel.ModalChanged += open => _sandbox.SetModalPaused("secret-confirmation", open);
        _secretPanel.MenuRequested += action =>
        {
            _secretPanel.SetOpen(false);
            if (action == "aw_inventory") _character.ToggleInventory();
            else if (action == "aw_character") _character.Toggle();
            else if (action == "aw_experiment") ShowExperimentPanel();
            else if (_session.InRoamingChampion) OpenRoamingChampions();
            else if (_session.HasUnresolvedRegionalHunt) OpenRegionalHunts();
            else if (!_session.InSecretChamber)
            {
                if (action == "aw_endgame") _board.SetOpen(true);
                else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); }
            }
        };
        _secretObjective = new PanelContainer { Name = "SecretChamberObjective", MouseFilter = Control.MouseFilterEnum.Ignore };
        _secretObjective.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new("17212ce8"), BorderColor = new("a49673"), BorderWidthLeft = 3, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 10, ContentMarginBottom = 10 });
        _secretObjectiveText = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
        _secretObjectiveText.AddThemeFontSizeOverride("font_size", 14); _secretObjective.AddChild(_secretObjectiveText); _sandbox.AddOverlay(_secretObjective);
    }

    private void OpenSecretChambers(string id = "")
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true) return;
        if (id.Length > 0) _secretSelection = id;
        else if (_session.InSecretChamber) _secretSelection = _session.CurrentSecretChamber!.Id;
        RefreshSecretChambers(); _secretPanel.SetOpen(true);
    }

    private bool InteractSecretChamber(string id)
    {
        if (!id.StartsWith("secret.", StringComparison.Ordinal)) return false;
        var interaction = _session.Interactions.FirstOrDefault(i => i.ActionId == id);
        var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        if (interaction is null || CorePosition.DistanceSquared(player.Position, interaction.Position) > (long)interaction.Range * interaction.Range)
        { Notice("Move closer to inspect this detail."); return true; }
        var definition = SecretChamberCatalog.Definitions.FirstOrDefault(d => id.StartsWith(d.Id + ".", StringComparison.Ordinal));
        if (definition is null) return true;
        _secretClue = id.Contains(".puzzle.", StringComparison.Ordinal) ? id : "";
        OpenSecretChambers(definition.Id); return true;
    }

    private void SecretAction(string action, string id)
    {
        if (action == "approach") { _secretClue = ""; _sandbox.RequestWorldInteraction(id); return; }
        EndgameRuntimeCommand? command = null;
        if (action.StartsWith("choice_", StringComparison.Ordinal) && int.TryParse(action[7..], out int choice))
        {
            var clue = _session.SecretChambers.Entries.Select(e => e.Clue).FirstOrDefault(c => c?.Id == id);
            if (clue is not null && choice >= 0 && choice < clue.Choices.Length)
                command = new(EndgameRuntimeAction.ResolveSecretClue, Id: id, Value: clue.Choices[choice]);
        }
        else command = action switch
        {
            "enter" => new(EndgameRuntimeAction.EnterSecretChamber, Id: id),
            "challenge" => new(EndgameRuntimeAction.ChallengeSecretGuardian),
            "claim" => new(EndgameRuntimeAction.ClaimSecretTreasure),
            "exit" => new(EndgameRuntimeAction.ExitSecretChamber),
            _ => null
        };
        if (command is null) return;
        long before = _session.Tick; Apply(command);
        bool accepted = before != _session.Tick;
        if (accepted) _secretClue = "";
        if (!accepted || action.StartsWith("choice_", StringComparison.Ordinal) || action == "claim") OpenSecretChambers(_secretSelection);
    }

    private void RefreshSecretChambers()
    {
        if (_secretPanel is null) return;
        var view = _session.SecretChambers;
        var entries = view.Entries.Where(e => e.Known).Select(e =>
        {
            var d = SecretChamberCatalog.Find(e.Id)!;
            return new SecretChamberEntryDisplay(e.Id, e.Revealed ? e.Name : "An unusual " + (e.Act == 1 ? "bell" : e.Act == 2 ? "root wall" : "cold pipe"),
                _campaignDefinition.Acts.Single(a => a.Number == e.Act).Name,
                e.Claimed ? "Treasure claimed" : e.Defeated ? "Treasure awaits" : e.Revealed ? "Passage revealed" : "Mystery unsolved",
                e.Revealed ? d.Description : "Your observations suggest a hidden passage nearby.",
                e.Revealed ? EquipmentNames.For(d.RewardItemId) : "", d.Clues.Take(e.PuzzleStep).Select(c => c.Hint).ToArray());
        }).ToArray();
        var selected = view.Entries.FirstOrDefault(e => e.Id == _secretSelection);
        if (selected is null || !selected.Known && selected.Clue?.Id != _secretClue)
        { _secretSelection = entries.FirstOrDefault()?.Id ?? ""; selected = view.Entries.FirstOrDefault(e => e.Id == _secretSelection); }
        string heading = entries.FirstOrDefault(e => e.Id == _secretSelection)?.Name ?? "Unrecorded mysteries";
        string narrative = "Inspect unusual details as you explore. Hidden treasure is optional; your campaign can be completed without it.";
        var actions = new List<SecretChamberActionDisplay>();
        if (view.Run is { } run && run.Id == _secretSelection)
        {
            heading = run.Name;
            narrative = run.Stage switch
            {
                "Foyer" => "A quiet threshold. The guardian will awaken only when you challenge it.\n" + run.GuardianName + ": " + run.Counterplay,
                "Combat" => run.Counterplay + "\nYou may retreat at any time. The guardian resets if you leave before victory.",
                "Victory" => "The guardian has fallen. Claim the chamber's one-time treasure; it remains available if you leave now.",
                "Claimed" => "This character has already claimed the chamber's treasure. The passage remains open.",
                _ => "The guardian prevailed. Leave safely, prepare your build, and return to try again. Your campaign progress is preserved."
            };
            if (run.Stage == "Foyer") AddWorldAction("challenge", run.Id + ".challenge", "Challenge " + run.GuardianName, run.CanChallenge,
                "Awaken the guardian and its allies? You may retreat through the entrance at any time.");
            if (run.Stage == "Victory") AddWorldAction("claim", run.Id + ".treasure", "Claim hidden treasure", run.CanClaim);
            actions.Add(new("exit", run.Id, "Return through the passage", "Return to the exact campaign room you left.", run.CanExit,
                run.Stage == "Combat" ? "Retreat now? This attempt grants no treasure and the guardian will reset. Discovered clues remain recorded." : ""));
        }
        else if (selected is not null)
        {
            if (selected.Clue is { } clue)
            {
                if (_secretClue == clue.Id && clue.InReach)
                {
                    heading = clue.Prompt; narrative = clue.Hint;
                    for (int i = 0; i < clue.Choices.Length; i++) actions.Add(new("choice_" + i, clue.Id, clue.Choices[i]));
                }
                else { narrative = "There is more to inspect here. Approach the unusual detail to read its markings."; actions.Add(new("approach", clue.Id, clue.Prompt)); }
            }
            else if (selected.Revealed)
            {
                narrative = selected.Claimed ? "The passage remains open. Its one-time treasure has been claimed." : selected.Defeated ? "The guardian remains defeated. Return to claim the waiting treasure." : "Enter the hidden chamber's safe foyer. Challenge its guardian only when you are ready.";
                if (selected.Here) AddWorldAction("enter", selected.Id + ".enter", "Enter " + selected.Name, true);
                else narrative += "\nReturn to " + Readable(SecretChamberCatalog.Find(selected.Id)!.SourceEncounterId) + " to visit its revealed doorway.";
            }
            else narrative = "Return to the place where you found these markings to continue the puzzle.";
        }
        _secretPanel.SetView(new(entries, _secretSelection, heading, narrative, actions.ToArray(), _revision, _secretNotice));
        var current = _session.CurrentSecretChamber;
        _secretPresentation.Present(new(current?.Act ?? _session.Campaign.View.Act, _session.InSecretChamber, view.Run?.Stage ?? "", current?.Id ?? "", _session.Interactions.ToArray()));
        _secretObjective.Visible = SecretObjectiveVisible();
        if (view.Run is not null)
        {
            CombatHudLayout.Objective(_secretObjective, GetViewport().GetVisibleRect().Size);
            _secretObjectiveText.Text = view.Run.Name.ToUpperInvariant() + "\n" + (view.Run.Stage == "Combat" ? view.Run.Counterplay : view.Run.Stage == "Foyer" ? "A safe threshold. Challenge the guardian when ready." : view.Run.Stage == "Victory" ? "Guardian defeated. Collect the hidden treasure." : view.Run.Stage == "Claimed" ? "Treasure claimed. Return through the passage." : "Return through the passage to recover.") + "\nJ: chamber journal · Leave through the entrance at any time";
        }

        void AddWorldAction(string action, string interactionId, string label, bool enabled, string confirm = "")
        {
            var interaction = _session.Interactions.FirstOrDefault(i => i.ActionId == interactionId);
            bool near = interaction is not null && CorePosition.DistanceSquared(interaction.Position, _session.Combat.View.Actors.Single(a => a.Id == 1).Position) <= (long)interaction.Range * interaction.Range;
            if (!near && interaction is not null) actions.Add(new("approach", interactionId, "Approach: " + label));
            else actions.Add(new(action, action == "enter" ? selected!.Id : view.Run!.Id, label,
                !enabled ? "Make room in your permanent inventory before claiming treasure." : "", enabled, confirm));
        }
    }
    private bool SecretObjectiveVisible() => _session.InSecretChamber && _secretPanel?.IsOpen != true && _deathRecapHud?.IsOpen != true && _frontMenu?.IsOpen != true && _training is null;
}
