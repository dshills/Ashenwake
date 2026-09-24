using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    private readonly ulong roamingCharacterSeed;
    private RoamingChampionState? roamingChampions;
    private CombatSession? roamingArena;
    public bool InRoamingChampion => roamingChampions?.Active is not null;
    public RoamingChampionDefinition? CurrentRoamingChampion => roamingChampions?.Active is { } active ? RoamingChampionCatalog.Find(active.Id) : null;
    private string RoamingSource(RoamingChampionDefinition definition) => RoamingChampionCatalog.SourceEncounter(definition, roamingCharacterSeed);
    private bool NearRoaming(Position position) => Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0 &&
        Position.DistanceSquared(a.Position, position) <= (long)RoamingChampionCatalog.InteractionRange * RoamingChampionCatalog.InteractionRange);
    private bool AtRoamingSource(RoamingChampionDefinition definition) => !InWorldEncounter && !InRoamingChampion && !InSecretChamber && arena is null && !HasUnresolvedRegionalHunt &&
        !Campaign.InHub && Campaign.ActiveEncounterId == RoamingSource(definition) && Campaign.EncounterCleared &&
        !Campaign.HasActiveExploration && Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0);
    private ulong RoamingArenaSeed(string id, long sequence) => unchecked(roamingCharacterSeed ^ (ulong)RoamingChampionCatalog.Find(id)!.Act * 0x4348414D50494F4EUL ^ (ulong)sequence * 0xD1B54A32D192ED03UL);
    private IReadOnlyList<ExpeditionInteraction> RoamingInteractions
    {
        get
        {
            if (roamingChampions?.Active is { } active && CurrentRoamingChampion is { } definition)
            {
                var list = new List<ExpeditionInteraction> { new(definition.Id + ".exit", "Leave the challenge · return to your exact campaign room", RoamingChampionCatalog.ExitPosition, RoamingChampionCatalog.InteractionRange) };
                if (active.Stage == "Foyer") list.Add(new(definition.Id + ".challenge", "Challenge " + definition.Name, RoamingChampionCatalog.ChallengePosition, RoamingChampionCatalog.InteractionRange));
                if (active.Stage == "Victory") list.Add(new(definition.Id + ".treasure", "Claim the champion's signature equipment", RoamingChampionCatalog.TreasurePosition, RoamingChampionCatalog.InteractionRange));
                return list;
            }
            var source = RoamingChampionCatalog.Definitions.FirstOrDefault(AtRoamingSource);
            return source is null ? [] : [new(source.Id + ".sighting", "Inspect signs of " + source.Name, RoamingChampionCatalog.SightingPosition, RoamingChampionCatalog.InteractionRange)];
        }
    }
    public RoamingChampionsView RoamingChampions
    {
        get
        {
            var entries = RoamingChampionCatalog.Definitions.Where(d => AtRoamingSource(d) || roamingChampions?.Discovered.Contains(d.Id) == true).Select(d =>
                new RoamingChampionEntryView(d.Id, d.Name, d.Act, RoamingSource(d), AtRoamingSource(d),
                    roamingChampions?.Discovered.Contains(d.Id) == true, roamingChampions?.Defeated.Contains(d.Id) == true,
                    roamingChampions?.Claimed.Contains(d.Id) == true, AtRoamingSource(d) && NearRoaming(RoamingChampionCatalog.SightingPosition),
                    d.Description, d.Counterplay, d.RewardItemId)).ToArray();
            RoamingChampionRunView? run = null;
            if (roamingChampions?.Active is { } active && CurrentRoamingChampion is { } d)
                run = new(d.Id, d.Name, d.Act, active.Stage, d.Counterplay,
                    active.Stage == "Foyer" && NearRoaming(RoamingChampionCatalog.ChallengePosition) && Production.CanClaimRoamingChampionReward,
                    active.Stage == "Victory" && NearRoaming(RoamingChampionCatalog.TreasurePosition) && Production.CanClaimRoamingChampionReward, true);
            return new(entries, run);
        }
    }
    public EndgameRuntimeResult EnterRoamingChampion(string id) => Execute(new(EndgameRuntimeAction.EnterRoamingChampion, Id: id));
    public EndgameRuntimeResult ChallengeRoamingChampion() => Execute(new(EndgameRuntimeAction.ChallengeRoamingChampion));
    public EndgameRuntimeResult ClaimRoamingChampionReward() => Execute(new(EndgameRuntimeAction.ClaimRoamingChampionReward));
    public EndgameRuntimeResult ExitRoamingChampion() => Execute(new(EndgameRuntimeAction.ExitRoamingChampion));
    private string[] DiscoverRoamingChampions()
    {
        var definition = RoamingChampionCatalog.Definitions.FirstOrDefault(d => AtRoamingSource(d) && NearRoaming(RoamingChampionCatalog.SightingPosition) && roamingChampions?.Discovered.Contains(d.Id) != true);
        if (definition is null) return [];
        roamingChampions ??= new();
        roamingChampions = roamingChampions with { Discovered = [.. roamingChampions.Discovered, definition.Id] };
        return ["RoamingChampionDiscovered:" + definition.Id];
    }
    private RoamingChampionState? CaptureRoamingChampions() => roamingChampions is null ? null : JsonData.Copy(roamingChampions with { Combat = roamingArena?.Capture() });
    private void RestoreRoamingChampions(RoamingChampionState? state)
    { roamingChampions = state is null ? null : JsonData.Copy(state); roamingArena = state?.Combat is null ? null : CombatSession.Restore(combatJson, state.Combat); }
    private EndgameRuntimeResult ChangeRoamingChampion(EndgameRuntimeCommand command)
    {
        var active = roamingChampions?.Active;
        switch (command.Action)
        {
            case EndgameRuntimeAction.EnterRoamingChampion:
                var definition = RoamingChampionCatalog.Find(command.Id);
                if (definition is null || !AtRoamingSource(definition) || !NearRoaming(RoamingChampionCatalog.SightingPosition) ||
                    (roamingChampions?.AttemptSequence ?? 0) >= 1000000000 || Combat.Capture().Experiment is not null)
                    return Fail("Secure the champion's campaign room and approach its signs before investigating.");
                roamingChampions ??= new();
                if (!roamingChampions.Discovered.Contains(definition.Id)) roamingChampions = roamingChampions with { Discovered = [.. roamingChampions.Discovered, definition.Id] };
                long sequence = roamingChampions.AttemptSequence + 1;
                ulong seed = RoamingArenaSeed(definition.Id, sequence);
                string stage = roamingChampions.Claimed.Contains(definition.Id) ? "Claimed" : roamingChampions.Defeated.Contains(definition.Id) ? "Victory" : "Foyer";
                roamingChampions = roamingChampions with { AttemptSequence = sequence, Active = new(definition.Id, seed, stage) };
                roamingArena = CombatSession.CreateEncounter(combatJson, seed, "championarena.foyer", Campaign.Combat.Capture() with { Seed = seed, Rng = SeededRandom.Streams(seed) }, restoreAtAnchor: true);
                break;
            case EndgameRuntimeAction.ChallengeRoamingChampion:
                if (active?.Stage != "Foyer" || !NearRoaming(RoamingChampionCatalog.ChallengePosition) || !Production.CanClaimRoamingChampionReward)
                    return Fail("Approach the challenge standard with one free backpack slot. You may leave safely and visit your stash first.");
                roamingArena = CombatSession.CreateEncounter(combatJson, active.Seed, CurrentRoamingChampion!.EncounterId, roamingArena!.Capture(), restoreAtAnchor: true);
                roamingChampions = roamingChampions! with { Active = active with { Stage = "Combat" } }; break;
            case EndgameRuntimeAction.ClaimRoamingChampionReward:
                if (active?.Stage != "Victory" || !NearRoaming(RoamingChampionCatalog.TreasurePosition) || !Production.CanClaimRoamingChampionReward)
                    return Fail("Defeat the champion, then approach its treasure with one free backpack slot.");
                Production.ReserveCampaignItemSequence(Math.Max(Campaign.Combat.Capture().NextObjectId, roamingArena!.Capture().NextObjectId));
                var events = Production.GrantRoamingChampion(active.Id);
                Campaign.RefreshSecretRewardProjection(); roamingArena = Production.ProjectCampaignCombat(roamingArena);
                roamingChampions = roamingChampions! with { Claimed = [.. roamingChampions.Claimed, active.Id], Active = active with { Stage = "Claimed" } };
                Tick++; return new(true, "", [], events);
            case EndgameRuntimeAction.ExitRoamingChampion:
                if (active is null) return Fail("No roaming champion challenge is active.");
                roamingChampions = roamingChampions! with { Active = null, Combat = null }; roamingArena = null; break;
            default: return Fail("Unknown roaming champion command.");
        }
        Tick++; return new(true, "", [], ["RoamingChampion:" + (roamingChampions!.Active?.Stage ?? "Exited")]);
    }
    private EndgameRuntimeResult AdvanceRoamingChampion(CombatCommand[] commands)
    {
        var active = roamingChampions!.Active!;
        if (active.Stage == "Failed") return Fail("Leave the challenge to recover; the champion paid no reward.");
        if (commands.Any(c => c.Kind is CombatCommandKind.Equip or CombatCommandKind.EquipFragment or CombatCommandKind.UnequipFragment or CombatCommandKind.SetMutation))
            return Fail("Leave the champion's challenge before changing your build.");
        var events = roamingArena!.Step(commands).ToArray(); Tick++;
        if (roamingArena.LastDeathRecap is { } recap) LastDeathRecap = recap;
        string stage = active.Stage;
        if (roamingArena.View.Actors.Single(a => a.Id == 1).Health <= 0) stage = "Failed";
        else if (stage == "Combat" && !roamingArena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            roamingArena.SealRoamingChampionVictory(); stage = "Victory";
            Production.RecordRoamingChampionVictory(active.Id);
            roamingChampions = roamingChampions with { Defeated = [.. roamingChampions.Defeated, active.Id] };
        }
        roamingChampions = roamingChampions with { Active = active with { Stage = stage } };
        return new(true, "", events, stage == active.Stage ? [] : ["RoamingChampion:" + stage]);
    }
    private void ValidateRoamingChampions()
    {
        if (roamingChampions is null) { Production.ValidateRoamingChampionRewards([], []); return; }
        bool ValidIds(string[]? ids) => ids is not null && ids.Length <= 3 && ids.Distinct().Count() == ids.Length && ids.All(id => RoamingChampionCatalog.Find(id) is not null);
        if (roamingChampions.SchemaVersion != 1 || roamingChampions.AttemptSequence is < 0 or > 1000000000 ||
            !ValidIds(roamingChampions.Discovered) || !ValidIds(roamingChampions.Defeated) || !ValidIds(roamingChampions.Claimed) ||
            roamingChampions.Defeated.Any(id => !roamingChampions.Discovered.Contains(id)) || roamingChampions.Claimed.Any(id => !roamingChampions.Defeated.Contains(id)) ||
            roamingChampions.Discovered.Any(id => !Campaign.HasCompletedEncounter(RoamingSource(RoamingChampionCatalog.Find(id)!))))
            throw new InvalidDataException("Invalid roaming champion discoveries.");
        Production.ValidateRoamingChampionRewards(roamingChampions.Claimed, roamingChampions.Defeated);
        if (roamingChampions.Active is not { } active)
        { if (roamingArena is not null) throw new InvalidDataException("Inactive champion challenge retained an arena."); return; }
        var definition = RoamingChampionCatalog.Find(active.Id);
        if (definition is null || roamingChampions.AttemptSequence == 0 || !roamingChampions.Discovered.Contains(active.Id) || roamingArena is null || Campaign.InHub ||
            Campaign.ActiveEncounterId != RoamingSource(definition) || !Campaign.EncounterCleared || Campaign.HasActiveExploration || arena is not null || HasUnresolvedRegionalHunt || InSecretChamber ||
            active.Stage is not ("Foyer" or "Combat" or "Victory" or "Failed" or "Claimed") || roamingChampions.Claimed.Contains(active.Id) != (active.Stage == "Claimed") ||
            roamingChampions.Defeated.Contains(active.Id) != (active.Stage is "Victory" or "Claimed"))
            throw new InvalidDataException("Champion challenge context differs from its secured campaign source.");
        var snapshot = roamingArena.Capture();
        if (active.Seed != RoamingArenaSeed(active.Id, roamingChampions.AttemptSequence) || snapshot.Seed != active.Seed || snapshot.Endgame is not null || snapshot.Experiment is not null || snapshot.Loot.Count != 0 ||
            (active.Stage is "Foyer" ? snapshot.EncounterId != "championarena.foyer" : active.Stage is "Victory" or "Claimed" ? snapshot.EncounterId != definition.EncounterId && snapshot.EncounterId != "championarena.foyer" : snapshot.EncounterId != definition.EncounterId))
            throw new InvalidDataException("Champion arena differs from its encounter.");
        bool dead = roamingArena.View.Actors.Single(a => a.Id == 1).Health <= 0;
        bool enemies = roamingArena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        if (dead != (active.Stage == "Failed") || active.Stage is "Victory" or "Claimed" or "Foyer" && enemies || active.Stage == "Combat" && !enemies)
            throw new InvalidDataException("Champion outcome differs from actual combat.");
        Production.ValidateCampaignCombat(snapshot);
    }
}
