using Ashenwake.Core.Campaign;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    private PetState? pets;
    private bool AtPetSource(string source) => pets is not null && !InWorldEncounter && !InRoamingChampion && !InSecretChamber && !HasUnresolvedRegionalHunt &&
        arena is null && !Campaign.InHub && !Campaign.HasActiveExploration && Campaign.ActiveEncounterId == source && Campaign.EncounterCleared &&
        Campaign.HasCompletedEncounter(source) && Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0);
    private bool NearPet(Position at) => Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0 &&
        PetCatalog.WithinReach(Room, a.Position, at));
    public PetsView Pets => new(pets is not null, PetCatalog.Definitions.Select(d =>
    {
        var owned = pets?.Rescued.FirstOrDefault(p => p.Id == d.Id);
        return new PetEntryView(d.Id, d.Species, owned?.Name ?? d.DefaultName, owned?.AppearanceId ?? d.Appearances[0].Id,
            d.Appearances, owned is not null, pets?.SelectedPetId == d.Id, d.SourceEncounterId, d.Act,
            "Secure " + (d.Act switch { 1 => "the Road", 2 => "the Living Ruins", _ => "the Cinder Pack" }) + " in Act " + d.Act + ", then approach the stranded companion.");
    }).ToArray(), pets?.SelectedPetId ?? "", pets?.AutoGather ?? false);
    public IReadOnlyList<ExpeditionInteraction> PetInteractions
    {
        get
        {
            if (pets is null) return [];
            var result = PetCatalog.Definitions.Where(d => !pets.Rescued.Any(p => p.Id == d.Id) && AtPetSource(d.SourceEncounterId))
                .Select(d => new ExpeditionInteraction(d.Id + ".rescue", "Rescue " + d.DefaultName + " · " + d.Species, PetCatalog.RescuePosition(Room), PetCatalog.InteractionRange)).ToList();
            if (PetCatalog.MaterialSources.Contains(Campaign.ActiveEncounterId) && AtPetSource(Campaign.ActiveEncounterId) &&
                !pets.CollectedMaterials.Contains(PetCatalog.MaterialId(Campaign.ActiveEncounterId)))
                result.Add(new(PetCatalog.MaterialId(Campaign.ActiveEncounterId), "Gather scattered supplies · 5 materials", PetCatalog.CachePosition(Room), PetCatalog.InteractionRange));
            return result;
        }
    }
    private PetState? CapturePets() => pets is null ? null : JsonData.Copy(pets);
    private void RestorePets(PetState? state) => pets = state is null ? null : JsonData.Copy(state);
    private EndgameRuntimeResult ChangePets(EndgameRuntimeCommand command)
    {
        if (!Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0) || Combat.Capture().Experiment is not null)
            return Fail("Companions are unavailable while defeated or practicing an experimental build.");
        if (command.Action == EndgameRuntimeAction.EnablePets)
        { pets ??= new(); Tick++; return new(true, "", [], ["PetsEnabled"]); }
        if (pets is null) return Fail("Enable companions before managing a pet.");
        var definition = PetCatalog.Find(command.Id);
        var owned = pets.Rescued.FirstOrDefault(p => p.Id == command.Id);
        switch (command.Action)
        {
            case EndgameRuntimeAction.RescuePet:
                if (definition is null || owned is not null || !AtPetSource(definition.SourceEncounterId) || !NearPet(PetCatalog.RescuePosition(Room)))
                    return Fail("Secure the companion's campaign room, then approach it along an unobstructed path.");
                pets = pets with
                {
                    Rescued = [.. pets.Rescued, new(definition.Id, definition.DefaultName, definition.Appearances[0].Id)],
                    SelectedPetId = pets.Rescued.Length == 0 ? definition.Id : pets.SelectedPetId
                };
                break;
            case EndgameRuntimeAction.SelectPet:
                if (owned is null) return Fail("Rescue this companion before selecting it.");
                pets = pets with { SelectedPetId = owned.Id }; break;
            case EndgameRuntimeAction.DismissPet:
                pets = pets with { SelectedPetId = "" }; break;
            case EndgameRuntimeAction.RenamePet:
                string name = command.Value?.Trim() ?? "";
                if (owned is null || !PetCatalog.ValidName(name)) return Fail("Use a plain companion name with 1–24 characters.");
                pets = pets with { Rescued = pets.Rescued.Select(p => p.Id == owned.Id ? p with { Name = name } : p).ToArray() }; break;
            case EndgameRuntimeAction.SetPetAppearance:
                if (owned is null || definition is null || !definition.Appearances.Any(a => a.Id == command.Value)) return Fail("Choose one of this rescued companion's appearances.");
                pets = pets with { Rescued = pets.Rescued.Select(p => p.Id == owned.Id ? p with { AppearanceId = command.Value } : p).ToArray() }; break;
            case EndgameRuntimeAction.SetPetAutoGather:
                if (command.Value is not ("true" or "false")) return Fail("Choose whether the companion gathers nearby materials.");
                pets = pets with { AutoGather = command.Value == "true" }; break;
            case EndgameRuntimeAction.CollectPetMaterials:
                if (!CanGatherPetMaterials(command.Id)) return Fail("Approach an unclaimed supply cache in a secured campaign room with material capacity.");
                var events = CollectPetMaterials(command.Id); Tick++; return new(true, "", [], events);
            default: return Fail("Unknown companion command.");
        }
        Tick++; return new(true, "", [], ["PetChanged:" + command.Action + ":" + command.Id]);
    }
    private bool CanGatherPetMaterials(string id) => pets is not null && PetCatalog.MaterialSource(id) is { } source && AtPetSource(source) &&
        !pets.CollectedMaterials.Contains(id) && NearPet(PetCatalog.CachePosition(Room)) && Production.CanCollectPetMaterials;
    private string[] CollectPetMaterials(string id)
    {
        var events = Production.GrantPetMaterials(id);
        pets = pets! with { CollectedMaterials = [.. pets!.CollectedMaterials, id] };
        return events;
    }
    private string[] AutoGatherPetMaterials(EndgameRuntimeCommand command)
    {
        if (pets is not { AutoGather: true } || pets.SelectedPetId == "" ||
            !(command.Action == EndgameRuntimeAction.Tick || command.Action == EndgameRuntimeAction.Campaign && command.Campaign?.Action == CampaignRuntimeAction.Tick)) return [];
        string id = PetCatalog.MaterialId(Campaign.ActiveEncounterId);
        return CanGatherPetMaterials(id) ? CollectPetMaterials(id) : [];
    }
    private void ValidatePets()
    {
        if (pets is null) { Production.ValidatePetMaterialReceipts([]); return; }
        if (pets.SchemaVersion != 1 || pets.Rescued is null || pets.CollectedMaterials is null || pets.SelectedPetId is null || pets.Rescued.Length > 3 ||
            pets.Rescued.Any(p => p is null || PetCatalog.Find(p.Id) is not { } d || !Campaign.HasCompletedEncounter(d.SourceEncounterId) ||
                !PetCatalog.ValidName(p.Name) || !d.Appearances.Any(a => a.Id == p.AppearanceId)) ||
            pets.Rescued.Select(p => p.Id).Distinct().Count() != pets.Rescued.Length || pets.SelectedPetId != "" && !pets.Rescued.Any(p => p.Id == pets.SelectedPetId) ||
            pets.CollectedMaterials.Length > 15 || pets.CollectedMaterials.Distinct().Count() != pets.CollectedMaterials.Length ||
            pets.CollectedMaterials.Any(id => PetCatalog.MaterialSource(id) is not { } source || !Campaign.HasCompletedEncounter(source)))
            throw new InvalidDataException("Invalid permanent companion ledger.");
        Production.ValidatePetMaterialReceipts(pets.CollectedMaterials);
    }
}
