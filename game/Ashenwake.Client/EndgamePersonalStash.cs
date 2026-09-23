using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private PersonalStashPanel _stashPanel = null!;
    private string _stashSessionKey = Guid.NewGuid().ToString("N"), _stashNotice = "";
    private (long Revision, bool AtChest, bool CanUse, bool CanApproach)? _stashViewKey;

    private void InitializePersonalStash()
    {
        _stashPanel = new PersonalStashPanel(); _sandbox.AddOverlay(_stashPanel);
        _character.StashRequested += OpenPersonalStash;
        _stashPanel.DepositRequested += (item, tab) => StashAction(new(ProductionAction.StoreItem, ItemId: item, Id: tab));
        _stashPanel.RetrieveRequested += item => StashAction(new(ProductionAction.RetrieveItem, ItemId: item));
        _stashPanel.MoveRequested += (item, tab) => StashAction(new(ProductionAction.MoveStashedItem, ItemId: item, Id: tab));
        _stashPanel.RenameTabRequested += (tab, name) => StashAction(new(ProductionAction.RenameStashTab, Id: tab, Value: name));
        _stashPanel.ApproachRequested += () =>
        {
            if (!_session.InHub || !_session.Interactions.Any(i => i.ActionId == PersonalStashCatalog.InteractionId)) return;
            _stashPanel.SetOpen(false); _sandbox.RequestWorldInteraction(PersonalStashCatalog.InteractionId);
        };
        _stashPanel.VisibilityChangedByPlayer += open =>
        {
            if (open)
            {
                _character.Close(); _collection.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false);
                _huntBoard.SetOpen(false); _secretPanel.SetOpen(false); CloseExperimentPanel();
            }
            UpdatePanelVisibility();
        };
        _stashPanel.MenuRequested += action =>
        {
            _stashPanel.SetOpen(false);
            if (action == "aw_inventory") _character.ToggleInventory();
            else if (action == "aw_character") _character.Toggle();
            else if (action == "aw_experiment") ShowExperimentPanel();
            else if (_session.InRoamingChampion) OpenRoamingChampions();
            else if (_session.InSecretChamber) OpenSecretChambers();
            else if (_session.HasUnresolvedRegionalHunt) OpenRegionalHunts();
            else if (action == "aw_endgame" || _session.Combat.View.Endgame is not null) _board.ShowRun();
            else { _campaignHud.Visible = true; _campaignHud.SetOpen(true); }
        };
    }
    private void OpenPersonalStash()
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true) return;
        _stashNotice = ""; RefreshPersonalStash(_session.Capture(), true); _stashPanel.SetOpen(true);
    }
    private bool InteractPersonalStash(string id)
    {
        if (id != PersonalStashCatalog.InteractionId) return false;
        var chest = _session.Interactions.FirstOrDefault(i => i.ActionId == id);
        if (chest is null || CorePosition.DistanceSquared(chest.Position, _session.Combat.View.Actors.Single(a => a.Id == 1).Position) > (long)chest.Range * chest.Range)
        { Notice("Approach your chest beside Torren to use the stash."); return true; }
        OpenPersonalStash(); return true;
    }
    private void StashAction(ProductionCommand command)
    {
        if (_stashPanel?.IsOpen != true || _training is not null) return;
        Permanent(command); RefreshPersonalStash(_session.Capture(), true);
    }
    private void RefreshPersonalStash(EndgameRuntimeSnapshot snapshot, bool force = false)
    {
        if (_stashPanel is null || !_hasActiveCharacter || !force && !_stashPanel.IsOpen) return;
        var chest = _session.Interactions.FirstOrDefault(i => i.ActionId == PersonalStashCatalog.InteractionId);
        bool approach = _session.InHub && chest is not null && _session.Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0);
        bool atChest = approach && CorePosition.DistanceSquared(chest!.Position, _session.Combat.View.Actors.Single(a => a.Id == 1).Position) <= (long)chest.Range * chest.Range;
        var view = _session.Stash;
        var key = (_revision, atChest, view.CanUse, approach);
        if (!force && _stashViewKey == key) return;
        _stashViewKey = key;
        var items = view.Backpack.Concat(view.Stored).Select(entry =>
        {
            var item = entry.Item; var definition = _productionDefinition.Items.Single(d => d.Id == item.DefinitionId);
            string numbers = $"Base damage {item.BaseDamage} · armor {item.BaseArmor} · critical {item.BaseCriticalBasisPoints / 100d:0.##}%";
            string rolls = item.Affixes.Count == 0 ? "Affixes: none" : "Affixes: " + string.Join(" · ", item.Affixes.Select(p => Readable(p.Key) + " " + p.Value));
            return new PersonalStashItemDisplay(item.Id, item.DefinitionId, definition.Slots[0], item.Rarity,
                EquipmentNames.For(item.DefinitionId), item.Rarity + " · " + string.Join(" / ", definition.Slots),
                numbers + "\n" + rolls + "\n\n" + EquipmentDetails.Inspect(item, _productionDefinition),
                item.IsFavorite, item.IsLocked, entry.Equipped, entry.TabId,
                entry.References.Select(r => r.Kind + ": " + r.Name).ToArray(),
                !view.CanUse ? view.Requirement : entry.Equipped ? "Unequip this item at Torren before storing it." : "",
                !view.CanUse ? view.Requirement : view.BackpackCount >= view.BackpackCapacity ? "Your backpack is full. Store an item first." : "");
        }).ToArray();
        _stashPanel.SetView(new(_stashSessionKey, _revision, snapshot.Campaign.Production.Progression.Character.Discipline,
            atChest, view.CanUse, approach && !atChest, view.Requirement, view.BackpackCapacity,
            view.Tabs.Select(t => new PersonalStashTabDisplay(t.Id, t.Name, t.Capacity)).ToArray(), items, _stashNotice));
    }
    private void ResetPersonalStash()
    {
        _stashPanel?.SessionRestored(); _stashSessionKey = Guid.NewGuid().ToString("N"); _stashViewKey = null; _stashNotice = "";
    }
}
