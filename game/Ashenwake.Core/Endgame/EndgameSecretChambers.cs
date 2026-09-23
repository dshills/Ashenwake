using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed partial class EndgameRuntimeSession
{
    private SecretChamberState? secretChambers;
    private CombatSession? secretArena;
    public bool InSecretChamber => secretChambers?.Active is not null;
    public SecretChamberDefinition? CurrentSecretChamber => secretChambers?.Active is { } active ? SecretChamberCatalog.Find(active.Id) : null;
    private bool NearSecret(Position position) => Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0 &&
        Position.DistanceSquared(a.Position, position) <= (long)SecretChamberCatalog.InteractionRange * SecretChamberCatalog.InteractionRange);
    private bool AtSecretSource(SecretChamberDefinition definition) => !InRoamingChampion && !InSecretChamber && arena is null && !HasUnresolvedRegionalHunt &&
        !Campaign.InHub && Campaign.ActiveEncounterId == definition.SourceEncounterId && Campaign.EncounterCleared &&
        !Campaign.HasActiveExploration && Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0);
    private IReadOnlyList<ExpeditionInteraction> SecretInteractions
    {
        get
        {
            if (secretChambers?.Active is { } active && CurrentSecretChamber is { } definition)
            {
                var list = new List<ExpeditionInteraction> { new(definition.Id + ".exit", "Leave the chamber · return to your exact campaign room", SecretChamberCatalog.ExitPosition, SecretChamberCatalog.InteractionRange) };
                if (active.Stage == "Foyer") list.Add(new(definition.Id + ".challenge", "Challenge " + definition.GuardianName, SecretChamberCatalog.ChallengePosition, SecretChamberCatalog.InteractionRange));
                if (active.Stage == "Victory") list.Add(new(definition.Id + ".treasure", "Claim the hidden treasure", SecretChamberCatalog.TreasurePosition, SecretChamberCatalog.InteractionRange));
                return list;
            }
            var source = SecretChamberCatalog.Definitions.FirstOrDefault(AtSecretSource);
            if (source is null) return [];
            int step = secretChambers?.PuzzleProgress.GetValueOrDefault(source.Id) ?? 0;
            if (step == 3) return [new(source.Id + ".enter", "Enter " + source.Name, source.EntrancePosition, SecretChamberCatalog.InteractionRange)];
            var clue = source.Clues[step];
            return [new(clue.Id, clue.Prompt, clue.Position, SecretChamberCatalog.InteractionRange)];
        }
    }
    public SecretChambersView SecretChambers
    {
        get
        {
            var entries = SecretChamberCatalog.Definitions.Where(d => AtSecretSource(d) || secretChambers?.PuzzleProgress.ContainsKey(d.Id) == true).Select(d =>
            {
                int step = secretChambers?.PuzzleProgress.GetValueOrDefault(d.Id) ?? 0; bool here = AtSecretSource(d);
                SecretClueView? clue = here && step < 3 ? new(d.Clues[step].Id, d.Clues[step].Prompt, d.Clues[step].Hint, d.Clues[step].Choices.ToArray(), NearSecret(d.Clues[step].Position)) : null;
                return new SecretChamberEntryView(d.Id, step == 3 ? d.Name : "An unmarked passage", d.Act, step > 0 || here && NearSecret(d.Clues[step].Position), step == 3,
                    secretChambers?.Claimed.Contains(d.Id) == true, step, here, clue, secretChambers?.Defeated.Contains(d.Id) == true);
            }).ToArray();
            SecretChamberRunView? run = null;
            if (secretChambers?.Active is { } active && CurrentSecretChamber is { } d)
                run = new(d.Id, d.Name, d.GuardianName, d.Act, active.Stage, d.Counterplay,
                    active.Stage == "Foyer" && NearSecret(SecretChamberCatalog.ChallengePosition) && Production.CanClaimSecretTreasure,
                    active.Stage == "Victory" && NearSecret(SecretChamberCatalog.TreasurePosition) && Production.CanClaimSecretTreasure, true);
            return new(entries, run);
        }
    }
    public EndgameRuntimeResult ResolveSecretClue(string id, string choice) => Execute(new(EndgameRuntimeAction.ResolveSecretClue, Id: id, Value: choice));
    public EndgameRuntimeResult EnterSecretChamber(string id) => Execute(new(EndgameRuntimeAction.EnterSecretChamber, Id: id));
    public EndgameRuntimeResult ChallengeSecretGuardian() => Execute(new(EndgameRuntimeAction.ChallengeSecretGuardian));
    public EndgameRuntimeResult ClaimSecretTreasure() => Execute(new(EndgameRuntimeAction.ClaimSecretTreasure));
    public EndgameRuntimeResult ExitSecretChamber() => Execute(new(EndgameRuntimeAction.ExitSecretChamber));
    private SecretChamberState? CaptureSecretChambers() => secretChambers is null ? null : JsonData.Copy(secretChambers with { Combat = secretArena?.Capture() });
    private void RestoreSecretChambers(SecretChamberState? state)
    { secretChambers = state is null ? null : JsonData.Copy(state); secretArena = state?.Combat is null ? null : CombatSession.Restore(combatJson, state.Combat); }
    private EndgameRuntimeResult ChangeSecretChamber(EndgameRuntimeCommand command)
    {
        var active = secretChambers?.Active;
        switch (command.Action)
        {
            case EndgameRuntimeAction.ResolveSecretClue:
                var source = SecretChamberCatalog.Definitions.FirstOrDefault(AtSecretSource);
                if (source is null) return Fail("Secure the room and inspect its next hidden clue.");
                int step = secretChambers?.PuzzleProgress.GetValueOrDefault(source.Id) ?? 0;
                if (step >= 3) return Fail("This hidden entrance has already been revealed.");
                var clue = source.Clues[step];
                if (command.Id != clue.Id || !NearSecret(clue.Position)) return Fail("Approach the next clue before choosing a response.");
                if (command.Value != clue.Solution) return Fail("The mechanism remains still. " + clue.Hint);
                secretChambers ??= new(); secretChambers.PuzzleProgress[source.Id] = step + 1;
                Tick++; return new(true, "", [], [step == 2 ? "SecretEntranceRevealed:" + source.Id : "SecretClueResolved:" + clue.Id]);
            case EndgameRuntimeAction.EnterSecretChamber:
                var definition = SecretChamberCatalog.Find(command.Id);
                if (definition is null || !AtSecretSource(definition) || secretChambers?.PuzzleProgress.GetValueOrDefault(definition.Id) != 3 ||
                    !NearSecret(definition.EntrancePosition) || secretChambers.AttemptSequence >= 1000000000 || Combat.Capture().Experiment is not null)
                    return Fail("Reveal this entrance and approach it in the secured campaign room.");
                ulong seed = unchecked(Combat.Capture().Seed ^ (ulong)(secretChambers.AttemptSequence + 1) * 0x534543524554UL);
                string stage = secretChambers.Claimed.Contains(definition.Id) ? "Claimed" : secretChambers.Defeated.Contains(definition.Id) ? "Victory" : "Foyer";
                secretChambers = secretChambers with { AttemptSequence = secretChambers.AttemptSequence + 1, Active = new(definition.Id, seed, stage) };
                secretArena = CombatSession.CreateEncounter(combatJson, seed, "secretarena.foyer", Campaign.Combat.Capture() with { Seed = seed, Rng = SeededRandom.Streams(seed) }, restoreAtAnchor: true);
                break;
            case EndgameRuntimeAction.ChallengeSecretGuardian:
                if (active?.Stage != "Foyer" || !NearSecret(SecretChamberCatalog.ChallengePosition) || !Production.CanClaimSecretTreasure) return Fail("Approach the guardian's seal with one free inventory slot. You may leave safely to make space before accepting its challenge.");
                secretArena = CombatSession.CreateEncounter(combatJson, active.Seed, CurrentSecretChamber!.EncounterId, secretArena!.Capture(), restoreAtAnchor: true);
                secretChambers = secretChambers! with { Active = active with { Stage = "Combat" } }; break;
            case EndgameRuntimeAction.ClaimSecretTreasure:
                if (active?.Stage != "Victory" || !NearSecret(SecretChamberCatalog.TreasurePosition) || !Production.CanClaimSecretTreasure) return Fail("Defeat the guardian, then approach its treasure with inventory space.");
                Production.ReserveCampaignItemSequence(Math.Max(Campaign.Combat.Capture().NextObjectId, secretArena!.Capture().NextObjectId));
                var events = Production.GrantSecretChamber(active.Id);
                Campaign.RefreshSecretRewardProjection(); secretArena = Production.ProjectCampaignCombat(secretArena);
                secretChambers = secretChambers! with { Claimed = [.. secretChambers.Claimed, active.Id], Active = active with { Stage = "Claimed" } };
                Tick++; return new(true, "", [], events);
            case EndgameRuntimeAction.ExitSecretChamber:
                if (active is null) return Fail("No hidden chamber is active.");
                secretChambers = secretChambers! with { Active = null, Combat = null }; secretArena = null; break;
            default: return Fail("Unknown hidden chamber command.");
        }
        Tick++; return new(true, "", [], ["SecretChamber:" + (secretChambers!.Active?.Stage ?? "Exited")]);
    }
    private EndgameRuntimeResult AdvanceSecretChamber(CombatCommand[] commands)
    {
        var active = secretChambers!.Active!;
        if (active.Stage == "Failed") return Fail("Leave the chamber to recover; its guardian paid no reward.");
        if (commands.Any(c => c.Kind is CombatCommandKind.Equip or CombatCommandKind.EquipFragment or CombatCommandKind.UnequipFragment or CombatCommandKind.SetMutation))
            return Fail("Leave the hidden chamber before changing your build.");
        var events = secretArena!.Step(commands).ToArray(); Tick++;
        if (secretArena.LastDeathRecap is { } recap) LastDeathRecap = recap;
        string stage = active.Stage;
        if (secretArena.View.Actors.Single(a => a.Id == 1).Health <= 0) stage = "Failed";
        else if (stage == "Combat" && !secretArena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            secretArena.SealSecretChamberVictory(); stage = "Victory";
            Production.RecordSecretGuardianVictory(active.Id);
            secretChambers = secretChambers with { Defeated = [.. secretChambers.Defeated, active.Id] };
        }
        secretChambers = secretChambers with { Active = active with { Stage = stage } };
        return new(true, "", events, stage == active.Stage ? [] : ["SecretChamber:" + stage]);
    }
    private void ValidateSecretChambers()
    {
        if (secretChambers is null) { Production.ValidateSecretChamberRewards([], []); return; }
        if (secretChambers.SchemaVersion != 1 || secretChambers.AttemptSequence is < 0 or > 1000000000 || secretChambers.PuzzleProgress is null || secretChambers.PuzzleProgress.Count > 3 ||
            secretChambers.PuzzleProgress.Any(p => SecretChamberCatalog.Find(p.Key) is null || p.Value is < 1 or > 3) || secretChambers.Defeated is null || secretChambers.Defeated.Length > 3 || secretChambers.Defeated.Distinct().Count() != secretChambers.Defeated.Length ||
            secretChambers.Defeated.Any(id => id is null || secretChambers.PuzzleProgress.GetValueOrDefault(id) != 3) ||
            secretChambers.Claimed is null || secretChambers.Claimed.Length > 3 ||
            secretChambers.Claimed.Distinct().Count() != secretChambers.Claimed.Length || secretChambers.Claimed.Any(id => !secretChambers.Defeated.Contains(id)))
            throw new InvalidDataException("Invalid secret chamber discoveries.");
        Production.ValidateSecretChamberRewards(secretChambers.Claimed, secretChambers.Defeated);
        foreach (string id in secretChambers.PuzzleProgress.Keys)
            if (!Campaign.HasCompletedEncounter(SecretChamberCatalog.Find(id)!.SourceEncounterId)) throw new InvalidDataException("Secret discovery precedes its secured source room.");
        if (secretChambers.Active is not { } active)
        { if (secretArena is not null) throw new InvalidDataException("Inactive secret chamber retained an arena."); return; }
        var definition = SecretChamberCatalog.Find(active.Id);
        if (definition is null || secretChambers.AttemptSequence == 0 || secretChambers.PuzzleProgress.GetValueOrDefault(active.Id) != 3 || secretArena is null || Campaign.InHub ||
            Campaign.ActiveEncounterId != definition.SourceEncounterId || !Campaign.EncounterCleared || arena is not null || HasUnresolvedRegionalHunt || InRoamingChampion ||
            active.Stage is not ("Foyer" or "Combat" or "Victory" or "Failed" or "Claimed") || secretChambers.Claimed.Contains(active.Id) != (active.Stage == "Claimed") ||
            secretChambers.Defeated.Contains(active.Id) != (active.Stage is "Victory" or "Claimed"))
            throw new InvalidDataException("Secret chamber context differs from its campaign source.");
        var snapshot = secretArena.Capture();
        if (active.Seed != unchecked(Campaign.Combat.Capture().Seed ^ (ulong)secretChambers.AttemptSequence * 0x534543524554UL) || snapshot.Seed != active.Seed || snapshot.Endgame is not null || snapshot.Experiment is not null || snapshot.Loot.Count != 0 ||
            (active.Stage is "Foyer" ? snapshot.EncounterId != "secretarena.foyer" : active.Stage is "Victory" or "Claimed" ? snapshot.EncounterId != definition.EncounterId && snapshot.EncounterId != "secretarena.foyer" : snapshot.EncounterId != definition.EncounterId))
            throw new InvalidDataException("Secret chamber arena differs from its guardian.");
        bool dead = secretArena.View.Actors.Single(a => a.Id == 1).Health <= 0;
        bool enemies = secretArena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        if (dead != (active.Stage == "Failed") || active.Stage is "Victory" or "Claimed" or "Foyer" && enemies || active.Stage == "Combat" && !enemies)
            throw new InvalidDataException("Secret chamber outcome differs from actual combat.");
        Production.ValidateCampaignCombat(snapshot);
    }
}
