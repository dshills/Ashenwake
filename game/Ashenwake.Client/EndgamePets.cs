using Ashenwake.Core.Endgame;

namespace Ashenwake.Client;

public partial class EndgameDirector
{
    private PetPanel _pets = null!;
    private PetPresentation _petPresentation = null!;
    private string _petNotice = "";
    private long _petPanelRevision = -1;

    private void InitializePets()
    {
        _petPresentation = new PetPresentation(); AddChild(_petPresentation); _petPresentation.Attach(_sandbox);
        _pets = new PetPanel(); _sandbox.AddOverlay(_pets);
        _campaignHud.PetsRequested += OpenPets; _board.PetsRequested += OpenPets;
        _pets.SelectRequested += id => PetAction(new(EndgameRuntimeAction.SelectPet, Id: id));
        _pets.DismissRequested += () => PetAction(new(EndgameRuntimeAction.DismissPet));
        _pets.RenameRequested += (id, name) => PetAction(new(EndgameRuntimeAction.RenamePet, Id: id, Value: name));
        _pets.AppearanceRequested += (id, appearance) => PetAction(new(EndgameRuntimeAction.SetPetAppearance, Id: id, Value: appearance));
        _pets.AutoGatherRequested += enabled => PetAction(new(EndgameRuntimeAction.SetPetAutoGather, Value: enabled ? "true" : "false"));
        _pets.MenuRequested += action =>
        {
            _pets.SetOpen(false);
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
    }
    private void EnablePets()
    {
        if (_smoke || _echoesSmoke || !_hasActiveCharacter || _training is not null || _session.Pets.Enabled ||
            _session.Combat.View.Actors.All(a => a.Id != 1 || a.Health <= 0) || _session.Combat.Capture().Experiment is not null) return;
        var result = ExecuteActive(new(EndgameRuntimeAction.EnablePets));
        if (!result.Success) _petNotice = result.Reason;
    }
    private void OpenPets()
    {
        if (!_hasActiveCharacter || _training is not null || _frontMenu.IsOpen || _deathRecapHud?.IsOpen == true) return;
        _bestiary.SetOpen(false); _wardrobe.SetOpen(false); _collection.SetOpen(false);
        _huntBoard.SetOpen(false); _secretPanel.SetOpen(false); _stashPanel.SetOpen(false); _championPanel.SetOpen(false);
        _worldEncounterPanel.SetOpen(false); _openingGuide.SetOpen(false); _campaignHud.SetOpen(false); _board.SetOpen(false);
        _character.Close(); CloseExperimentPanel(); PresentPets(); _pets.SetOpen(true);
    }
    private void PetAction(EndgameRuntimeCommand command)
    {
        Safely(() =>
        {
            var result = ExecuteActive(command);
            _petNotice = result.Success ? command.Action == EndgameRuntimeAction.RescuePet ? "Companion rescued. Your first companion follows you automatically." : "Companion choices saved." : result.Reason;
            if (result.Success)
            {
                _revision++; Observe(result); _sandbox.AdoptSession(_session.Combat); Refresh();
                try { Save(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                { _petNotice = "Companion changes are active but could not be saved. Try saving again before quitting. " + ex.Message; }
                if (command.Action == EndgameRuntimeAction.RescuePet) _sandbox.Notify("Rescued " + _session.Pets.Entries.Single(e => e.Id == command.Id).Name + " · J → Companions");
            }
            if (!result.Success) Notice(result.Reason);
            PresentPets();
        });
    }
    private bool InteractPet(string id)
    {
        if (!id.StartsWith("pet.", StringComparison.Ordinal)) return false;
        if (id.EndsWith(".rescue", StringComparison.Ordinal)) PetAction(new(EndgameRuntimeAction.RescuePet, Id: id[..^7]));
        else PetAction(new(EndgameRuntimeAction.CollectPetMaterials, Id: id));
        return true;
    }
    private void PresentPets()
    {
        var view = _session.Pets; int act = _session.Campaign.Capture().Campaign.HighestActVisited;
        _pets.SetView(new(view.Entries.Select(e =>
        {
            bool known = e.Rescued || act >= e.Act;
            string description = e.Id switch
            {
                "pet.ashfox" => "A soot-soft wanderer with lantern-bright eyes. Once freed from the roadside wreckage, it never forgets a kind hand.",
                "pet.gloammoth" => "A velvet-winged creature drawn to quiet light. It trades the tangled roots for the safety of your shadow.",
                _ => "A small furnace scavenger with a polished shell. Its curious antennae are always searching for something worth carrying home."
            };
            return new PetEntryDisplay(e.Id, known ? e.Species : "Undiscovered companion", known ? e.Name : "", known ? description : "Explore to find creatures in need of a home.",
                e.Rescued ? "Rescued in " + _campaignDefinition.Acts.Single(a => a.Number == e.Act).Name + ". Ready to travel with you." : known ? e.RescueHint : "Another companion awaits in a later region.", known, e.Rescued, e.Selected, e.AppearanceId,
                e.Appearances.Select(a => a.Id).ToArray(), e.Appearances.Select(a => a.Name).ToArray());
        }).ToArray(), view.AutoGather, _petNotice));
        _petPanelRevision = _revision;
    }
    private void RefreshPets(string context)
    {
        if (_pets is null) return;
        var player = _session.Combat.View.Actors.Single(a => a.Id == 1);
        _petPresentation.Present(_session.Pets, _session.Room, player.Position, context, _session.PetInteractions.ToArray(),
            _hasActiveCharacter && _training is null && !_frontMenu.IsOpen && player.Health > 0);
        if (_pets.IsOpen && _petPanelRevision != _revision) PresentPets();
    }
    private void ResetPets()
    { _pets?.ResetSelection(); _petPresentation?.Reset(); _petNotice = ""; _petPanelRevision = -1; }
}
