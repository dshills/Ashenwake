using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    private RegionalHuntState? regionalHunts;
    private CombatSession? regionalArena;
    public bool InRegionalHunt => regionalHunts?.Run?.Away == true;
    public bool HasUnresolvedRegionalHunt => regionalHunts?.Run?.Stage is "Tracking" or "Combat" or "Victory" or "Failed";
    public RegionalHuntContract? CurrentRegionalHunt => regionalHunts?.Run is { } run ? RegionalHuntCatalog.Find(run.ContractId) : null;
    private bool NearRegionalBoard => InHub && NearRegional(RegionalHuntCatalog.BoardPosition, RegionalHuntCatalog.BoardRange);
    private bool NearRegional(Position at, int range) => Combat.View.Actors.Any(p => p.Id == 1 && p.Health > 0 && Position.DistanceSquared(p.Position, at) <= (long)range * range);
    private ExpeditionInteraction? NextRegionalClue => InRegionalHunt && regionalHunts?.Run is { Stage: "Tracking" } run && CurrentRegionalHunt is { } contract
        ? new(contract.Id + ".clue." + (run.TrackedClues + 1), contract.Clues[run.TrackedClues], RegionalHuntCatalog.CluePosition(run.TrackedClues), 1800) : null;
    private IReadOnlyList<ExpeditionInteraction> RegionalInteractions => NextRegionalClue is { } clue ? [clue] : [];
    public RegionalHuntBoardView RegionalHunts
    {
        get
        {
            var contracts = RegionalHuntCatalog.Contracts.Select(c =>
            {
                bool unlocked = Campaign.HasCompletedAct(c.Act);
                return new RegionalHuntContractView(c.Id, unlocked ? c.Name : "Undiscovered regional hunt", c.Act,
                    unlocked ? c.Region : "Act " + c.Act, unlocked ? c.Description : "A new contract appears after completing this act.",
                    unlocked ? c.Counterplay : "Discover this hunt to learn its counterplay.", unlocked ? c.RewardItemId : "", unlocked ? c.Materials : 0,
                    unlocked, unlocked ? "Repeatable · claim the reward at Greyhaven's hunt board after victory." : "Complete Act " + c.Act + ".",
                    regionalHunts?.Rewards.Count(r => r.ContractId == c.Id) ?? 0);
            }).ToArray();
            RegionalHuntRunView? view = null;
            if (regionalHunts?.Run is { } run && CurrentRegionalHunt is { } c)
            {
                view = new(run.Id, run.ContractId, c.Name, c.Act, run.Stage, run.TrackedClues,
                    run.Stage == "Tracking" ? c.Clues[run.TrackedClues] : "", c.Counterplay,
                    NextRegionalClue is { } clue && NearRegional(clue.Position, clue.Range),
                    !run.Away && run.Stage == "Victory" && NearRegionalBoard && Production.CanClaimRegionalHunt,
                    run.Away && run.Stage is "Victory" or "Failed", HasUnresolvedRegionalHunt);
            }
            return new(contracts, view, NearRegionalBoard, NearRegionalBoard && !HasUnresolvedRegionalHunt && arena is null && Production.CanClaimRegionalHunt && (regionalHunts?.NextRunId ?? 1) <= 1000);
        }
    }
    public EndgameRuntimeResult StartRegionalHunt(string id) => Execute(new(EndgameRuntimeAction.StartRegionalHunt, Id: id));
    public EndgameRuntimeResult TrackRegionalHuntClue(string id) => Execute(new(EndgameRuntimeAction.TrackRegionalHuntClue, Id: id));
    public EndgameRuntimeResult ClaimRegionalHuntReward() => Execute(new(EndgameRuntimeAction.ClaimRegionalHuntReward));
    public EndgameRuntimeResult AbandonRegionalHunt() => Execute(new(EndgameRuntimeAction.AbandonRegionalHunt));
    public EndgameRuntimeResult ReturnRegionalHunt() => Execute(new(EndgameRuntimeAction.ReturnRegionalHunt));
    private RegionalHuntState? CaptureRegionalHunts() => regionalHunts is null ? null : JsonData.Copy(regionalHunts with { Combat = regionalArena?.Capture() });
    private void RestoreRegionalHunts(RegionalHuntState? source)
    {
        regionalHunts = source is null ? null : JsonData.Copy(source);
        regionalArena = source?.Combat is null ? null : CombatSession.Restore(combatJson, source.Combat);
    }
    private EndgameRuntimeResult ChangeRegionalHunt(EndgameRuntimeCommand command)
    {
        var run = regionalHunts?.Run;
        switch (command.Action)
        {
            case EndgameRuntimeAction.StartRegionalHunt:
                var contract = RegionalHuntCatalog.Find(command.Id);
                if (contract is null || !RegionalHunts.CanStart || !Campaign.HasCompletedAct(contract.Act) || Combat.Capture().Experiment is not null)
                    return Fail("Complete the contract's act, resolve any current hunt, keep one free inventory slot, and approach Greyhaven's hunt board.");
                long id = regionalHunts?.NextRunId ?? 1;
                ulong seed = SeedFor(id) ^ 0x524547494F4E414CUL;
                regionalHunts = (regionalHunts ?? new()) with { NextRunId = id + 1, Run = new(id, contract.Id, seed, "Tracking", 0, true) };
                Production.ClearCampaignEffects();
                var source = Production.Combat.Capture() with { Seed = seed, Rng = SeededRandom.Streams(seed) };
                regionalArena = CombatSession.CreateEncounter(combatJson, seed, "regional.trail", source, restoreAtAnchor: true);
                break;
            case EndgameRuntimeAction.TrackRegionalHuntClue:
                var clue = NextRegionalClue;
                if (run is null || clue is null || clue.ActionId != command.Id || !NearRegional(clue.Position, clue.Range)) return Fail("Approach the next clue on this hunt's trail.");
                int count = run.TrackedClues + 1;
                regionalHunts = regionalHunts! with { Run = run with { TrackedClues = count, Stage = count == 3 ? "Combat" : "Tracking" } };
                if (count == 3)
                    regionalArena = CombatSession.CreateEncounter(combatJson, run.Seed, CurrentRegionalHunt!.EncounterId, regionalArena!.Capture(), restoreAtAnchor: true);
                break;
            case EndgameRuntimeAction.ReturnRegionalHunt:
                if (run is not { Away: true, Stage: "Victory" or "Failed" }) return Fail("Defeat the quarry, or explicitly abandon the hunt before returning.");
                ReturnRegionalArena();
                if (run.Stage == "Failed")
                { regionalHunts = regionalHunts! with { Run = run with { Stage = "Abandoned", Away = false }, Combat = null }; regionalArena = null; }
                break;
            case EndgameRuntimeAction.AbandonRegionalHunt:
                if (!HasUnresolvedRegionalHunt || run is null) return Fail("No unresolved regional hunt can be abandoned.");
                if (run.Away) ReturnRegionalArena();
                regionalHunts = regionalHunts! with { Run = run with { Stage = "Abandoned", Away = false }, Combat = null }; regionalArena = null;
                break;
            case EndgameRuntimeAction.ClaimRegionalHuntReward:
                if (run is not { Stage: "Victory", Away: false } || !NearRegionalBoard || !Production.CanClaimRegionalHunt)
                    return Fail("Return victorious to Greyhaven's hunt board with inventory space to claim this contract.");
                var receipt = new RegionalHuntReceipt(run.Id, run.ContractId, run.Seed);
                var messages = Production.GrantRegionalHunt(receipt);
                regionalHunts = regionalHunts! with { Run = run with { Stage = "Claimed" }, Rewards = [.. regionalHunts.Rewards, receipt], Combat = null }; regionalArena = null;
                Tick++; return new(true, "", [], messages);
            default: return Fail("Unknown regional hunt command.");
        }
        Tick++; return new(true, "", [], ["RegionalHunt:" + regionalHunts!.Run!.Stage]);
    }
    private EndgameRuntimeResult AdvanceRegionalHunt(CombatCommand[] commands)
    {
        if (regionalHunts?.Run is not { Away: true } run || regionalArena is null) return Fail("No regional hunt arena is active.");
        if (run.Stage == "Failed") return Fail("Return to Greyhaven after defeat; the contract paid no reward.");
        if (commands.Any(c => c.Kind is CombatCommandKind.Equip or CombatCommandKind.EquipFragment or CombatCommandKind.UnequipFragment or CombatCommandKind.SetMutation))
            return Fail("Resolve the hunt before changing the permanent build.");
        var events = regionalArena.Step(commands).ToArray(); Tick++;
        if (regionalArena.LastDeathRecap is { } recap) LastDeathRecap = recap;
        var player = regionalArena.View.Actors.Single(a => a.Id == 1);
        string stage = run.Stage;
        if (player.Health <= 0) stage = "Failed";
        else if (stage == "Combat" && !regionalArena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        { regionalArena.SealRegionalHuntVictory(); stage = "Victory"; }
        regionalHunts = regionalHunts with { Run = run with { Stage = stage } };
        return new(true, "", events, stage == run.Stage ? [] : ["RegionalHunt:" + stage]);
    }
    private void ReturnRegionalArena()
    {
        var hub = Production.Combat.Capture();
        Production.ReturnCampaignToHub(regionalArena!.Capture() with { Seed = hub.Seed, Rng = hub.Rng });
        regionalHunts = regionalHunts! with { Run = regionalHunts.Run! with { Away = false } };
    }
    private void ValidateRegionalHunts()
    {
        if (regionalHunts is null) { Production.ValidateRegionalHuntRewards([]); return; }
        if (regionalHunts.SchemaVersion != 1 || regionalHunts.NextRunId is < 2 or > 1001 || regionalHunts.Rewards is null || regionalHunts.Rewards.Length > 1000 ||
            regionalHunts.Rewards.Any(r => r is null || r.RunId < 1 || r.RunId >= regionalHunts.NextRunId || RegionalHuntCatalog.Find(r.ContractId) is null) ||
            regionalHunts.Rewards.Select(r => r.RunId).Distinct().Count() != regionalHunts.Rewards.Length ||
            regionalHunts.Run is not { } run || run.Id != regionalHunts.NextRunId - 1 || RegionalHuntCatalog.Find(run.ContractId) is not { } contract ||
            !Campaign.HasCompletedAct(contract.Act) || run.TrackedClues is < 0 or > 3 ||
            run.Stage is not ("Tracking" or "Combat" or "Victory" or "Failed" or "Claimed" or "Abandoned")) throw new InvalidDataException("Invalid regional hunt ledger.");
        Production.ValidateRegionalHuntRewards(regionalHunts.Rewards);
        if (regionalHunts.Rewards.Any(r => r.RunId == run.Id) != (run.Stage == "Claimed") ||
            run.Stage == "Claimed" && regionalHunts.Rewards.Single(r => r.RunId == run.Id) != new RegionalHuntReceipt(run.Id, run.ContractId, run.Seed))
            throw new InvalidDataException("Regional hunt claim differs from its run.");
        if (run.Stage is "Claimed" or "Abandoned")
        { if (run.Away || regionalArena is not null) throw new InvalidDataException("Resolved hunt retained an arena."); return; }
        if (!Campaign.InHub || arena is not null || regionalArena is null || State.Run?.Status == "Active" || regionalArena.Capture().Experiment is not null)
            throw new InvalidDataException("Regional hunt conflicts with another journey.");
        var snapshot = regionalArena.Capture();
        if (snapshot.Seed != run.Seed || snapshot.EncounterId != (run.TrackedClues < 3 ? "regional.trail" : contract.EncounterId) ||
            snapshot.Endgame is not null || snapshot.Loot.Count != 0 || run.Stage == "Tracking" && (!run.Away || run.TrackedClues >= 3) ||
            run.Stage == "Combat" && (!run.Away || run.TrackedClues != 3) || run.Stage == "Victory" && run.TrackedClues != 3)
            throw new InvalidDataException("Regional hunt arena differs from its stage.");
        bool dead = regionalArena.View.Actors.Single(a => a.Id == 1).Health <= 0;
        bool won = !regionalArena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        if (dead != (run.Stage == "Failed") || run.Stage == "Victory" && !won || run.Stage == "Combat" && won)
            throw new InvalidDataException("Regional hunt result differs from observed combat.");
        Production.ValidateCampaignCombat(snapshot);
    }
}
