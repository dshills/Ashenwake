using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

/// <summary>Owns the complete local journey. Only observed combat victories may advance an endgame run.</summary>
public sealed partial class EndgameRuntimeSession
{
    private readonly string combatJson;
    private readonly AdventureContent adventure;
    private readonly ProgressionContent policy;
    private readonly CampaignContent campaignContent;
    private readonly EndgameCombatContent combatContent;
    private EndgameSession ledger;
    private CombatSession? arena;
    private EndgameCombatManifest? manifest;
    private bool cleared;
    private bool awaitingRetry;
    private long operationSequence;
    private readonly List<EndgameRuntimeFrame> frames = [];
    private EndgameRuntimeSnapshot initial = null!;
    public CampaignRuntimeSession Campaign { get; private set; }
    public ProductionSession Production => Campaign.Production;
    public CombatSession Combat => arena ?? Campaign.Combat;
    public EndgameContent Content { get; }
    public RoomDefinition Room => Combat.Room;
    public long Tick { get; private set; }
    public bool InHub => arena is null && Campaign.InHub;
    public bool EncounterCleared => arena is null ? Campaign.EncounterCleared : cleared;
    public bool AwaitingRetry => awaitingRetry;
    public IReadOnlyList<string> WorldEvents { get; private set; } = [];
    public string StateHash => JsonData.Hash(Capture());
    private EndgameState State => ledger.CurrentState;
    private int ArenaIndex => Math.Min(State.Run!.EncounterIndex, manifest!.Rooms.Length - 1);
    private bool CampaignComplete => Campaign.View.Ending?.FracturesUnlocked == true;
    private bool CanRecover => InHub && State.Unlocked && State.Run?.Status != "Active" && State.Sigils.All(s => s.Consumed) && State.Sigils.Length < 10000;
    public IReadOnlyList<ExpeditionInteraction> Interactions => arena is not null ? [] : !InHub || !State.Unlocked ? Campaign.Interactions :
        [.. Campaign.Interactions, new("endgame.gate", "Fractures · Sigils and God Hunts", new(6500, 0), 2600)];
    public EndgameRunView? RunView
    {
        get
        {
            var run = State.Run; if (run is null) return null;
            var hunt = run.Kind == "GodHunt" ? Content.Data.Hunts.Single(h => h.Id == run.ContentId) : null;
            var sigil = run.Kind == "Fracture" ? State.Sigils.Single(s => s.Id == run.SigilId) : null;
            int count = hunt?.Phases.Length ?? 4;
            string[] rules = run.Modifiers.Select(id => Content.Data.Modifiers.Single(m => m.Id == id).Rule).ToArray();
            string[] counters = hunt is null ? run.Modifiers.Select(id => Content.Data.Modifiers.Single(m => m.Id == id).Counterplay).ToArray() : [hunt.Counterplay[Math.Min(run.EncounterIndex, count - 1)]];
            return new(run.Id, run.Kind, hunt?.Name ?? run.ContentId + " Fracture", sigil?.Region ?? "", run.Tier,
                run.EncounterIndex, count, run.AttemptsRemaining, run.Deaths, Math.Max(40, 100 - 20 * run.Deaths), run.Status,
                arena is not null && cleared, awaitingRetry, rules, counters, run.InheritedEliteModifiers.ToArray(), arena?.View.Loot.Count ?? 0,
                arena is not null && run.Status == "Active" && cleared, arena is not null && run.Status == "Active" && awaitingRetry,
                arena is not null && run.Status == "Active");
        }
    }
    public EndgameRuntimeView View
    {
        get
        {
            var view = ledger.View;
            return new(view.Unlocked, InHub, view.HighestClearedTier, view.AvailableSigils, view.UnlockedHunts,
                Production.ProgressionView.Materials, Production.EndgameCatalysts, CanRecover, RunView,
                State.Rewards.Values.Count(r => r.Kind == "Fracture"), State.Rewards.Values.Count(r => r.Kind == "GodHunt"));
        }
    }
    private EndgameRuntimeSession(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent content, CampaignRuntimeSession journey, EndgameSession ledger)
    {
        this.combatJson = combatJson; this.adventure = adventure; this.policy = EndgameProgression.Resolve(policy);
        campaignContent = campaign; Content = content; Campaign = journey; this.ledger = ledger;
        combatContent = EndgameCombatContent.FromComposed(combatJson);
        if (combatContent.PolicyHash != content.Hash) throw new InvalidDataException("Endgame combat and runtime policy identities differ.");
    }
    public static EndgameRuntimeSession Create(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ulong seed = 42, string discipline = "Vanguard", LocalProfileState? profile = null)
    {
        var journey = CampaignRuntimeSession.Create(combatJson, adventure, EndgameProgression.Resolve(policy), campaign, seed, discipline, profile);
        journey.Production.InitializeEndgame();
        var session = new EndgameRuntimeSession(combatJson, adventure, policy, campaign, endgame, journey, EndgameSession.Create(endgame));
        session.ValidateState(); session.initial = session.Capture(); return session;
    }
    public static EndgameRuntimeSession Restore(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, EndgameRuntimeSnapshot snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.RulesVersion != "endgame-runtime.1" || snapshot.Campaign is null || snapshot.Endgame is null ||
            snapshot.Tick is < 0 or > 1000000000 || snapshot.OperationSequence is < 0 or > 1000000000)
            throw new InvalidDataException("Unsupported or invalid endgame runtime state.");
        var journey = CampaignRuntimeSession.Restore(combatJson, adventure, EndgameProgression.Resolve(policy), campaign, snapshot.Campaign);
        var session = new EndgameRuntimeSession(combatJson, adventure, policy, campaign, endgame, journey, EndgameSession.Restore(endgame, snapshot.Endgame))
        {
            Tick = snapshot.Tick,
            operationSequence = snapshot.OperationSequence,
            manifest = snapshot.Manifest is null ? null : JsonData.Copy(snapshot.Manifest),
            arena = snapshot.Combat is null ? null : CombatSession.Restore(combatJson, snapshot.Combat),
            cleared = snapshot.EncounterCleared,
            awaitingRetry = snapshot.AwaitingRetry
        };
        session.RestoreExplorationMap(snapshot.ExplorationMap);
        session.ValidateState(); session.initial = session.Capture(); return session;
    }
    internal static EndgameRuntimeSession ImportCampaign(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, CampaignRuntimeSnapshot snapshot)
    {
        var journey = CampaignRuntimeSession.Restore(combatJson, adventure, EndgameProgression.Resolve(policy), campaign, snapshot);
        journey.Production.InitializeEndgame();
        var session = new EndgameRuntimeSession(combatJson, adventure, policy, campaign, endgame, journey, EndgameSession.Create(endgame)) { Tick = snapshot.Tick };
        session.RefreshUnlock(); session.ValidateState(); session.initial = session.Capture(); return session;
    }
    public EndgameRuntimeSnapshot Capture() => new()
    {
        Tick = Tick,
        OperationSequence = operationSequence,
        Campaign = Campaign.Capture(),
        Endgame = ledger.Capture(),
        Combat = arena?.Capture(),
        Manifest = manifest is null ? null : JsonData.Copy(manifest),
        AwaitingRetry = awaitingRetry,
        EncounterCleared = cleared,
        ExplorationMap = explorationMap?.Capture()
    };
    public EndgameRuntimeReplay CaptureReplay() => JsonData.Copy(new EndgameRuntimeReplay(1, initial, frames.ToArray()));
    public FracturePreview PreviewSigil(long id)
    {
        var sigil = State.Sigils.FirstOrDefault(s => s.Id == id && !s.Consumed) ?? throw new InvalidDataException("Sigil is unavailable.");
        var preview = combatContent.CreateFractureManifest(sigil, State.NextRunId);
        return new(sigil.Id, sigil.Seed, sigil.Region, sigil.Tier, sigil.BossFamily, sigil.RewardTendency, preview.Rooms.Length, 3,
            sigil.Modifiers.Select(m => Content.Data.Modifiers.Single(d => d.Id == m).Rule).ToArray(),
            sigil.Modifiers.Select(m => Content.Data.Modifiers.Single(d => d.Id == m).Counterplay).ToArray(), preview.Rooms.Select(r => r.Name).ToArray(),
            preview.Inheritance.Where(i => i.Selected).Select(i => i.Candidate).ToArray(), preview.Inheritance.Where(i => !i.Selected).Select(i => i.Candidate + ": " + i.Reason).ToArray(), JsonData.Hash(preview));
    }
    public EndgameRuntimeResult Step(params CombatCommand[] commands) => Execute(new(EndgameRuntimeAction.Tick, Commands: commands));
    public EndgameRuntimeResult ClaimRecoverySigil() => Execute(new(EndgameRuntimeAction.ClaimRecoverySigil));
    public EndgameRuntimeResult AttuneSigil(long id, string oldModifier, string newModifier) => Execute(new(EndgameRuntimeAction.AttuneSigil, id, oldModifier, newModifier));
    public EndgameRuntimeResult StartFracture(long id) => Execute(new(EndgameRuntimeAction.StartFracture, id));
    public EndgameRuntimeResult StartGodHunt(string id) => Execute(new(EndgameRuntimeAction.StartGodHunt, Id: id));
    public EndgameRuntimeResult AdvanceEncounter() => Execute(new(EndgameRuntimeAction.AdvanceEncounter));
    public EndgameRuntimeResult RetryEncounter() => Execute(new(EndgameRuntimeAction.RetryEncounter));
    public EndgameRuntimeResult Abandon() => Execute(new(EndgameRuntimeAction.Abandon));
    public EndgameRuntimeResult ReturnToHub() => Execute(new(EndgameRuntimeAction.ReturnToHub));
    public EndgameRuntimeResult ExecuteCampaign(CampaignRuntimeCommand command) => Execute(new(EndgameRuntimeAction.Campaign, Campaign: command));
    public EndgameRuntimeResult ExecuteProduction(ProductionCommand command) => Execute(new(EndgameRuntimeAction.Production, Production: command));

    public EndgameRuntimeResult Execute(EndgameRuntimeCommand command, bool recordReplay = true)
    {
        if (command is null || !Enum.IsDefined(command.Action)) throw new InvalidDataException("Unknown endgame runtime command.");
        if (Tick >= 1000000000 || operationSequence >= 1000000000 || !Production.ExternalReceiptCapacityAvailable)
            return new(false, "Endgame archive capacity requires an explicit migration.", [], []);
        if (recordReplay && frames.Count >= 1800) { initial = Capture(); frames.Clear(); }
        var rollback = command.Action == EndgameRuntimeAction.Tick ? null : Capture(); EndgameRuntimeResult result;
        try
        {
            result = command.Action == EndgameRuntimeAction.Tick ? Advance(command.Commands ?? []) : Change(command);
            if (result.Success) { operationSequence++; RefreshUnlock(); RevealExplorationMap(); }
            else if (rollback is not null) RestoreFields(rollback);
        }
        catch { if (rollback is not null) RestoreFields(rollback); throw; }
        WorldEvents = result.WorldEvents;
        if (recordReplay) frames.Add(new(JsonData.Copy(command), StateHash, JsonData.Hash(result)));
        return result;
    }
    private void RestoreFields(EndgameRuntimeSnapshot state)
    {
        Campaign = CampaignRuntimeSession.Restore(combatJson, adventure, policy, campaignContent, state.Campaign);
        ledger = EndgameSession.Restore(Content, state.Endgame); arena = state.Combat is null ? null : CombatSession.Restore(combatJson, state.Combat);
        manifest = state.Manifest is null ? null : JsonData.Copy(state.Manifest); cleared = state.EncounterCleared; awaitingRetry = state.AwaitingRetry;
        Tick = state.Tick; operationSequence = state.OperationSequence;
        RestoreExplorationMap(state.ExplorationMap);
    }
    private void RefreshUnlock()
    {
        if (!State.Unlocked && CampaignComplete) Require(ledger.Unlock("campaign.completed"));
    }
    private EndgameRuntimeResult Advance(CombatCommand[] commands)
    {
        if (commands.Length > 64 || commands.Any(c => c is null)) throw new InvalidDataException("Invalid endgame command batch.");
        if (arena is null)
        {
            var outcome = Campaign.Execute(new(CampaignRuntimeAction.Tick, Commands: commands), recordReplay: false);
            if (outcome.Success) Tick++; return new(outcome.Success, outcome.Reason, outcome.CombatEvents, outcome.WorldEvents);
        }
        if (awaitingRetry || State.Run!.Status is "Failed" or "Abandoned") return Fail("Retry the current encounter or return to Greyhaven.");
        if (commands.Any(c => c.Kind is CombatCommandKind.Equip or CombatCommandKind.EquipFragment or CombatCommandKind.UnequipFragment or CombatCommandKind.SetMutation))
            return Fail("Use permanent build services in Greyhaven.");
        Tick++; var before = arena.View.Actors; var events = arena.Step(commands).ToArray();
        var messages = new List<string>(Production.ReconcileEndgameCombat(arena, before, events, State.Run!.Id, State.Run.Deaths, ArenaIndex, Tick));
        if (messages.Count > 0 || events.Any(e => e.Kind is "LootPickedUp" or "LootDropped" or "AbilityStarted")) arena = Production.ProjectCampaignCombat(arena.Capture());
        else arena.ApplyAdventureBuild(Production.Combat.Build);
        if (arena.View.Actors.Single(a => a.Id == 1).Health <= 0)
        {
            var death = ledger.PlayerDied(); Require(death); messages.AddRange(death.Events);
            Production.ClearCampaignEffects(); arena.ApplyAdventureBuild(Production.Combat.Build); awaitingRetry = State.Run.Status == "Active"; cleared = false;
        }
        else if (!cleared && !arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            arena.SealEndgameVictory(); cleared = true; messages.Add("EndgameRoomCleared:" + ArenaIndex);
        }
        return new(true, "", events, messages.ToArray());
    }
    private EndgameRuntimeResult Change(EndgameRuntimeCommand command)
    {
        var messages = new List<string>();
        if (command.Action == EndgameRuntimeAction.EnableExplorationMap)
        {
            explorationMap ??= new();
            var enabled = Campaign.Execute(new(CampaignRuntimeAction.EnableExplorationMap), recordReplay: false);
            return new(enabled.Success, enabled.Reason, enabled.CombatEvents, enabled.WorldEvents);
        }
        if (command.Action is EndgameRuntimeAction.Campaign or EndgameRuntimeAction.Production)
        {
            if (arena is not null) return Fail("Finish or abandon the endgame run before changing the campaign or permanent build.");
            var nested = command.Action == EndgameRuntimeAction.Campaign ? command.Campaign ?? throw new InvalidDataException("Missing campaign action.") :
                new CampaignRuntimeCommand(CampaignRuntimeAction.Production, Production: command.Production ?? throw new InvalidDataException("Missing production action."));
            var outcome = Campaign.Execute(nested, recordReplay: false); if (outcome.Success) Tick++;
            return new(outcome.Success, outcome.Reason, outcome.CombatEvents, outcome.WorldEvents);
        }
        if (!State.Unlocked) return Fail("Complete this character's campaign before entering the Fractures.");
        if (command.Action is EndgameRuntimeAction.StartFracture or EndgameRuntimeAction.StartGodHunt &&
            (State.Rewards.Count >= 10000 || State.CompletedRooms.Count > 39996 || State.NextRunId == long.MaxValue || !Production.CanReserveEndgameRun))
            return Fail("Endgame history is full; preserve this save for an explicit archive migration.");
        switch (command.Action)
        {
            case EndgameRuntimeAction.ClaimRecoverySigil:
                if (!CanRecover || !NearGate()) return Fail("Visit the Fracture gate without an available sigil or active run to claim a tier-one recovery sigil.");
                Require(ledger.AwardSigil("recovery." + State.NextSigilId, SeedFor(State.NextSigilId), 1), messages); break;
            case EndgameRuntimeAction.AttuneSigil:
                if (!InHub || !NearGate()) return Fail("Attune sigils at Greyhaven's Fracture gate.");
                var attuned = ledger.AttuneSigil("attune." + operationSequence, command.SigilId, command.Id, command.Value);
                if (!attuned.Success) return Fail(attuned.Reason); messages.AddRange(attuned.Events);
                var spent = Production.SpendEndgameMaterials("endgame.attune." + operationSequence, 5, new { command.SigilId, Old = command.Id, New = command.Value });
                if (!spent.Success) return Fail(spent.Reason); messages.AddRange(spent.Events); break;
            case EndgameRuntimeAction.StartFracture:
                if (!InHub || !NearGate()) return Fail("Enter a Fracture from Greyhaven's gate.");
                var sigil = State.Sigils.FirstOrDefault(s => s.Id == command.SigilId && !s.Consumed);
                if (sigil is null) return Fail("Sigil is unavailable.");
                manifest = combatContent.CreateFractureManifest(sigil, State.NextRunId);
                Require(ledger.StartFracture(sigil.Id), messages); StartRoom(restoreAtAnchor: true); break;
            case EndgameRuntimeAction.StartGodHunt:
                if (!InHub || !NearGate()) return Fail("Enter a God Hunt from Greyhaven's gate.");
                if (!ledger.View.UnlockedHunts.Contains(command.Id)) return Fail("God Hunt is locked.");
                ulong huntSeed = SeedFor(State.NextRunId); manifest = combatContent.CreateHuntManifest(command.Id, huntSeed, State.NextRunId);
                Require(ledger.StartGodHunt(command.Id, huntSeed), messages); StartRoom(restoreAtAnchor: true); break;
            case EndgameRuntimeAction.AdvanceEncounter:
                if (arena is null || !cleared || State.Run?.Status != "Active") return Fail("Win the active encounter before advancing.");
                var run = State.Run; int index = run.EncounterIndex;
                if (index < manifest!.Rooms.Length - 1) DiscloseDrops(messages);
                string? candidate = manifest!.Inheritance.FirstOrDefault(i => i.RoomIndex == index)?.Candidate;
                var completion = ledger.CompleteEncounter($"run.{run.Id}.encounter.{index}", candidate); Require(completion, messages);
                messages.AddRange(Production.GrantEndgameRoom(run.Id, index, run.Tier));
                if (completion.Reward is { } reward)
                {
                    messages.AddRange(Production.GrantEndgameReward(reward));
                    arena = Production.ProjectCampaignCombat(arena.Capture());
                }
                else StartRoom(restoreAtAnchor: false);
                break;
            case EndgameRuntimeAction.RetryEncounter:
                if (arena is null || !awaitingRetry || State.Run?.Status != "Active") return Fail("A remaining attempt and a defeated character are required to retry.");
                StartRoom(restoreAtAnchor: true); messages.Add("EndgameAttemptRestarted"); break;
            case EndgameRuntimeAction.Abandon:
                if (arena is null || State.Run?.Status != "Active") return Fail("No active endgame run can be abandoned.");
                DiscloseDrops(messages); Require(ledger.Abandon(), messages); ReturnArenaToHub(); break;
            case EndgameRuntimeAction.ReturnToHub:
                if (arena is null || State.Run?.Status == "Active") return Fail("Finish or explicitly abandon the active run first.");
                DiscloseDrops(messages); ReturnArenaToHub(); messages.Add("EndgameReturnedToGreyhaven"); break;
            default: return Fail("Unsupported endgame action.");
        }
        Tick++; return new(true, "", [], messages.ToArray());
    }
    private bool NearGate() => InHub && Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, new(6500, 0)) <= 2600L * 2600;
    private ulong SeedFor(long sequence) => unchecked(Campaign.Capture().Production.Expedition.Combat.Seed ^ (ulong)sequence * 0xD1B54A32D192ED03UL);
    private void StartRoom(bool restoreAtAnchor)
    {
        Production.ClearCampaignEffects(); var previous = arena?.Capture() ?? Production.Combat.Capture();
        arena = combatContent.CreateEncounter(manifest!, State.Run!.EncounterIndex, State.Run.Deaths, previous, restoreAtAnchor);
        arena = Production.ProjectCampaignCombat(arena.Capture()); Production.ReserveCampaignItemSequence(arena.Capture().NextObjectId);
        cleared = false; awaitingRetry = false;
    }
    private void ReturnArenaToHub()
    {
        Production.ReturnCampaignToHub(arena!.Capture()); arena = null; manifest = null; cleared = false; awaitingRetry = false;
        if (explorationMap is not null) explorationMap = new();
    }
    private void DiscloseDrops(List<string> events)
    { if (arena!.View.Loot.Count > 0) events.Add("GroundLootLeftBehind:" + arena.View.Loot.Count); }
    private static EndgameRuntimeResult Fail(string reason) => new(false, reason, [], []);
    private static void Require(EndgameResult result, List<string>? messages = null)
    { if (!result.Success) throw new InvalidDataException("Endgame transaction rejected: " + result.Reason); messages?.AddRange(result.Events); }
    private void ValidateState()
    {
        if (State.Unlocked != CampaignComplete || !Production.HasEndgameInventory) throw new InvalidDataException("Endgame unlock/permanent inventory differs from the completed campaign.");
        Production.ValidateEndgameLedger(State);
        if (arena is null)
        {
            if (manifest is not null || awaitingRetry || cleared || State.Run?.Status == "Active") throw new InvalidDataException("Inactive endgame has a live arena or run.");
            return;
        }
        if (!Campaign.InHub || !State.Unlocked || manifest is null || State.Run is not { } run) throw new InvalidDataException("Endgame arena lacks a hub/run/manifest.");
        combatContent.ValidateManifest(manifest);
        var expected = run.Kind == "Fracture" ? combatContent.CreateFractureManifest(State.Sigils.Single(s => s.Id == run.SigilId), run.Id) : combatContent.CreateHuntManifest(run.ContentId, run.Seed, run.Id);
        if (JsonData.Hash(expected) != JsonData.Hash(manifest)) throw new InvalidDataException("Endgame manifest differs from its immutable source.");
        var combat = arena.Capture(); var context = combat.Endgame;
        bool dead = arena.View.Actors.Single(a => a.Id == 1).Health <= 0;
        if (context is null || context.Manifest.RunId != run.Id || context.EncounterIndex != ArenaIndex || context.Attempt != run.Deaths - (dead ? 1 : 0) ||
            JsonData.Hash(context.Manifest) != JsonData.Hash(manifest)) throw new InvalidDataException("Endgame arena context differs from its run.");
        Production.ValidateCampaignCombat(combat);
        bool won = !arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        if (awaitingRetry != (dead && run.Status == "Active") || cleared != (won && !dead) || run.Status == "Failed" && !dead || run.Status == "Abandoned")
            throw new InvalidDataException("Endgame retry/victory flags differ from actual combat.");
        var inherited = manifest.Inheritance.Where(i => i.Selected && i.RoomIndex < run.EncounterIndex).Select(i => i.Candidate).ToArray();
        if (!run.InheritedEliteModifiers.SequenceEqual(inherited)) throw new InvalidDataException("Inherited boss traits differ from completed rooms.");
        for (int i = 0; i < run.EncounterIndex; i++)
            if (!Production.HasEndgameRoom(run.Id, i, run.Tier)) throw new InvalidDataException("Completed room lacks its permanent reward receipt.");
    }
}
