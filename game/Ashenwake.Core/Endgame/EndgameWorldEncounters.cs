using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    private WorldEncounterState? worldEncounters;
    private CombatSession? worldArena;
    public bool InWorldEncounter => worldEncounters?.Active is not null;
    public WorldEncounterDefinition? CurrentWorldEncounter => WorldEncounterCatalog.Find(worldEncounters?.Active?.Id);
    private bool NearWorld(Position position) => Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0 &&
        Position.DistanceSquared(a.Position, position) <= (long)WorldEncounterCatalog.InteractionRange * WorldEncounterCatalog.InteractionRange);
    private bool AtWorldSource(WorldEncounterDefinition d) => !InWorldEncounter && !InRoamingChampion && !InSecretChamber && !HasUnresolvedRegionalHunt && arena is null &&
        !Campaign.InHub && !Campaign.HasActiveExploration && Campaign.ActiveEncounterId == d.SourceEncounterId && Campaign.EncounterCleared && Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0);
    private IReadOnlyList<ExpeditionInteraction> WorldEncounterInteractions
    {
        get
        {
            if (worldEncounters?.Active is { } active && CurrentWorldEncounter is { } d)
            {
                var interactions = new List<ExpeditionInteraction> { new(d.Id + ".exit", "Leave · return to your exact campaign room", WorldEncounterCatalog.ExitPosition, WorldEncounterCatalog.InteractionRange) };
                if (active.Stage == "Foyer") interactions.Add(new(d.Id + ".choice", "Investigate " + d.Name, WorldEncounterCatalog.ChoicePosition, WorldEncounterCatalog.InteractionRange));
                if (active.Stage == "Victory") interactions.Add(new(d.Id + ".treasure", "Claim " + d.Name + " reward", WorldEncounterCatalog.TreasurePosition, WorldEncounterCatalog.InteractionRange));
                return interactions;
            }
            return WorldEncounterCatalog.Definitions.Where(AtWorldSource).Select(d => new ExpeditionInteraction(d.Id + ".enter", "Investigate " + d.Name + " · optional", d.EntrancePosition, WorldEncounterCatalog.InteractionRange)).ToArray();
        }
    }
    public WorldEncountersView WorldEncounters
    {
        get
        {
            var entries = WorldEncounterCatalog.Definitions.Where(d => AtWorldSource(d) || worldEncounters?.Discovered.Contains(d.Id) == true)
                .Select(d => new WorldEncounterEntryView(d.Id, d.Name, d.Kind, d.Act, d.SourceEncounterId, AtWorldSource(d), worldEncounters?.Completed.ContainsKey(d.Id) == true,
                    worldEncounters?.Claimed.Contains(d.Id) == true, AtWorldSource(d) && NearWorld(d.EntrancePosition))).ToArray();
            WorldEncounterRunView? run = null;
            if (worldEncounters?.Active is { } active && CurrentWorldEncounter is { } d)
                run = new(d.Id, d.Name, d.Kind, d.Act, active.Stage, d.Description, d.Counterplay, d.Id == "event.caravan" ? worldEncounters.CaravanPuzzleStep : 0,
                    active.Stage == "Foyer" ? WorldChoices(d.Id) : [], active.Stage == "Foyer" && NearWorld(WorldEncounterCatalog.ChoicePosition) && Production.CanClaimWorldEncounter,
                    active.Stage == "Victory" && NearWorld(WorldEncounterCatalog.TreasurePosition) && Production.CanClaimWorldEncounter, true, worldEncounters.Completed.GetValueOrDefault(d.Id, ""));
            return new(entries, run);
        }
    }
    private WorldEncounterChoiceView[] WorldChoices(string id) => id switch
    {
        "event.lantern" => [new("rescue", "Rescue the traveler · defeat the captors")],
        "event.shrine" => [new("accept", "Accept hunger · take 25% more damage for this fight · 30 materials")],
        "event.caravan" => [.. (worldEncounters?.CaravanPuzzleStep ?? 0) switch
        {
            0 => new WorldEncounterChoiceView[] { new("extinguish", "Extinguish the candle"), new("ring", "Ring the procession bell") },
            1 => [new("keep", "Keep the mourner's coin"), new("return", "Return the mourner's coin")],
            _ => [new("speak", "Speak the final farewell"), new("relight", "Relight the candle")]
        }, new("escort", "Challenge the spectral escort · harder battle · 30 materials")],
        _ => [new("stabilize", "Stabilize the Resonance Storm · 25 materials")]
    };
    public EndgameRuntimeResult EnterWorldEncounter(string id) => Execute(new(EndgameRuntimeAction.EnterWorldEncounter, Id: id));
    public EndgameRuntimeResult ChooseWorldEncounter(string choice) => Execute(new(EndgameRuntimeAction.ChooseWorldEncounter, Value: choice));
    public EndgameRuntimeResult ClaimWorldEncounterReward() => Execute(new(EndgameRuntimeAction.ClaimWorldEncounterReward));
    public EndgameRuntimeResult ExitWorldEncounter() => Execute(new(EndgameRuntimeAction.ExitWorldEncounter));
    private WorldEncounterState? CaptureWorldEncounters() => worldEncounters is null ? null : JsonData.Copy(worldEncounters with { Combat = worldArena?.Capture() });
    private void RestoreWorldEncounters(WorldEncounterState? state)
    { worldEncounters = state is null ? null : JsonData.Copy(state); worldArena = state?.Combat is null ? null : CombatSession.Restore(combatJson, state.Combat); }
    private ulong WorldSeed(long attempt) => unchecked(Campaign.Combat.Capture().Seed ^ (ulong)attempt * 0x574f524c44UL);
    private EndgameRuntimeResult ChangeWorldEncounter(EndgameRuntimeCommand command)
    {
        var active = worldEncounters?.Active;
        switch (command.Action)
        {
            case EndgameRuntimeAction.EnterWorldEncounter:
                var definition = WorldEncounterCatalog.Find(command.Id);
                if (definition is null || !AtWorldSource(definition) || !NearWorld(definition.EntrancePosition) || (worldEncounters?.AttemptSequence ?? 0) >= 1000000000 || Combat.Capture().Experiment is not null)
                    return Fail("Secure this campaign room, then approach its optional encounter.");
                worldEncounters ??= new();
                if (!worldEncounters.Discovered.Contains(definition.Id)) worldEncounters = worldEncounters with { Discovered = [.. worldEncounters.Discovered, definition.Id] };
                ulong seed = WorldSeed(worldEncounters.AttemptSequence + 1);
                string stage = worldEncounters.Claimed.Contains(definition.Id) ? "Claimed" : worldEncounters.Completed.ContainsKey(definition.Id) ? "Victory" : "Foyer";
                worldEncounters = worldEncounters with { AttemptSequence = worldEncounters.AttemptSequence + 1, Active = new(definition.Id, seed, stage) };
                worldArena = CombatSession.CreateEncounter(combatJson, seed, "worldarena.foyer", Campaign.Combat.Capture() with { Seed = seed, Rng = SeededRandom.Streams(seed) }, restoreAtAnchor: true);
                break;
            case EndgameRuntimeAction.ChooseWorldEncounter:
                if (active?.Stage != "Foyer" || !NearWorld(WorldEncounterCatalog.ChoicePosition) || !Production.CanClaimWorldEncounter || !WorldChoices(active.Id).Any(c => c.Id == command.Value))
                    return Fail("Approach the encounter with one free backpack slot and choose one of its offered actions.");
                if (active.Id == "event.caravan" && command.Value != "escort")
                {
                    string solution = worldEncounters!.CaravanPuzzleStep switch { 0 => "extinguish", 1 => "return", _ => "speak" };
                    if (command.Value != solution) return Fail("The procession remains restless. " + CurrentWorldEncounter!.Counterplay);
                    worldEncounters = worldEncounters with { CaravanPuzzleStep = worldEncounters.CaravanPuzzleStep + 1 };
                    if (worldEncounters.CaravanPuzzleStep == 3) CompleteWorldEncounter("Puzzle");
                }
                else
                {
                    worldArena = CombatSession.CreateEncounter(combatJson, active.Seed, CurrentWorldEncounter!.EncounterId, worldArena!.Capture(), restoreAtAnchor: true);
                    worldEncounters = worldEncounters! with { Active = active with { Stage = "Combat" } };
                }
                break;
            case EndgameRuntimeAction.ClaimWorldEncounterReward:
                if (active?.Stage != "Victory" || !NearWorld(WorldEncounterCatalog.TreasurePosition) || !Production.CanClaimWorldEncounter) return Fail("Complete the encounter and approach its reward with one free backpack slot.");
                Production.ReserveCampaignItemSequence(Math.Max(Campaign.Combat.Capture().NextObjectId, worldArena!.Capture().NextObjectId));
                var events = Production.GrantWorldEncounter(active.Id, worldEncounters!.Completed[active.Id]);
                Campaign.RefreshSecretRewardProjection(); worldArena = Production.ProjectCampaignCombat(worldArena);
                worldEncounters = worldEncounters with { Claimed = [.. worldEncounters.Claimed, active.Id], Active = active with { Stage = "Claimed" } };
                Tick++; return new(true, "", [], events);
            case EndgameRuntimeAction.ExitWorldEncounter:
                if (active is null) return Fail("No world encounter is active.");
                worldEncounters = worldEncounters! with { Active = null, Combat = null }; worldArena = null; break;
            default: return Fail("Unknown world encounter command.");
        }
        Tick++; return new(true, "", [], ["WorldEncounter:" + (worldEncounters!.Active?.Stage ?? "Exited")]);
    }
    private void CompleteWorldEncounter(string outcome)
    {
        var active = worldEncounters!.Active!;
        if (outcome == "Combat") worldArena!.SealWorldEncounterVictory();
        Production.RecordWorldEncounterCompletion(active.Id, outcome);
        var completed = new SortedDictionary<string, string>(worldEncounters.Completed, StringComparer.Ordinal) { [active.Id] = outcome };
        worldEncounters = worldEncounters with { Completed = completed, Active = active with { Stage = "Victory" } };
    }
    private EndgameRuntimeResult AdvanceWorldEncounter(CombatCommand[] commands)
    {
        var active = worldEncounters!.Active!;
        if (active.Stage == "Failed") return Fail("Leave this encounter to recover; failure pays no reward.");
        if (commands.Any(c => c.Kind is CombatCommandKind.Equip or CombatCommandKind.EquipFragment or CombatCommandKind.UnequipFragment or CombatCommandKind.SetMutation))
            return Fail("Leave this optional encounter before changing your build.");
        var events = worldArena!.Step(commands).ToArray(); Tick++;
        if (worldArena.LastDeathRecap is { } recap) LastDeathRecap = recap;
        if (worldArena.View.Actors.Single(a => a.Id == 1).Health <= 0)
            worldEncounters = worldEncounters with { Active = active with { Stage = "Failed" } };
        else if (active.Stage == "Combat" && !worldArena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) CompleteWorldEncounter("Combat");
        return new(true, "", events, active.Stage == worldEncounters.Active!.Stage ? [] : ["WorldEncounter:" + worldEncounters.Active.Stage]);
    }
    private void ValidateWorldEncounters()
    {
        if (worldEncounters is null) { Production.ValidateWorldEncounterRewards([], new(StringComparer.Ordinal)); return; }
        bool ValidIds(string[]? ids) => ids is not null && ids.Length <= WorldEncounterCatalog.Definitions.Count && ids.Distinct().Count() == ids.Length && ids.All(id => WorldEncounterCatalog.Find(id) is not null);
        if (worldEncounters.SchemaVersion != 1 || worldEncounters.AttemptSequence is < 0 or > 1000000000 || !ValidIds(worldEncounters.Discovered) || !ValidIds(worldEncounters.Claimed) ||
            worldEncounters.CaravanPuzzleStep is < 0 or > 3 || worldEncounters.Completed is null || worldEncounters.Completed.Count > WorldEncounterCatalog.Definitions.Count ||
            worldEncounters.Completed.Any(p => !worldEncounters.Discovered.Contains(p.Key) || p.Value != "Combat" && !(p.Key == "event.caravan" && p.Value == "Puzzle")) ||
            worldEncounters.Claimed.Any(id => !worldEncounters.Completed.ContainsKey(id)) ||
            worldEncounters.CaravanPuzzleStep > 0 && !worldEncounters.Discovered.Contains("event.caravan") ||
            (worldEncounters.CaravanPuzzleStep == 3) != (worldEncounters.Completed.GetValueOrDefault("event.caravan") == "Puzzle") ||
            worldEncounters.Discovered.Any(id => !Campaign.HasCompletedEncounter(WorldEncounterCatalog.Find(id)!.SourceEncounterId)))
            throw new InvalidDataException("Invalid world encounter discoveries or outcomes.");
        Production.ValidateWorldEncounterRewards(worldEncounters.Claimed, worldEncounters.Completed);
        if (worldEncounters.Active is not { } active)
        { if (worldArena is not null) throw new InvalidDataException("Inactive world encounter retained an arena."); return; }
        var d = WorldEncounterCatalog.Find(active.Id);
        if (d is null || worldEncounters.AttemptSequence == 0 || !worldEncounters.Discovered.Contains(active.Id) || worldArena is null || Campaign.InHub || Campaign.HasActiveExploration ||
            Campaign.ActiveEncounterId != d.SourceEncounterId || !Campaign.EncounterCleared || arena is not null || HasUnresolvedRegionalHunt || InSecretChamber || InRoamingChampion ||
            active.Stage is not ("Foyer" or "Combat" or "Victory" or "Claimed" or "Failed") || worldEncounters.Claimed.Contains(active.Id) != (active.Stage == "Claimed") ||
            worldEncounters.Completed.ContainsKey(active.Id) != (active.Stage is "Victory" or "Claimed"))
            throw new InvalidDataException("World encounter context differs from its secured campaign source.");
        var snapshot = worldArena.Capture();
        if (active.Seed != WorldSeed(worldEncounters.AttemptSequence) || snapshot.Seed != active.Seed || snapshot.Endgame is not null || snapshot.Experiment is not null || snapshot.Loot.Count != 0 ||
            (active.Stage == "Foyer" ? snapshot.EncounterId != "worldarena.foyer" : active.Stage is "Victory" or "Claimed" ? snapshot.EncounterId != d.EncounterId && snapshot.EncounterId != "worldarena.foyer" : snapshot.EncounterId != d.EncounterId))
            throw new InvalidDataException("World encounter arena differs from its content.");
        bool dead = worldArena.View.Actors.Single(a => a.Id == 1).Health <= 0;
        bool enemies = worldArena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        if (dead != (active.Stage == "Failed") || active.Stage is "Victory" or "Claimed" or "Foyer" && enemies || active.Stage == "Combat" && !enemies)
            throw new InvalidDataException("World encounter outcome differs from actual combat.");
        Production.ValidateCampaignCombat(snapshot);
    }
}
