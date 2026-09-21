using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Exploration;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using System.Text.Json.Serialization;

namespace Ashenwake.Core.Campaign;

public enum CampaignRuntimeAction { Tick, EnterAct, AdvanceEncounter, ReturnToHub, Choose, BeginExploration, TrackClue, LeaveExploration, Production, RevisitEncounter, InteractOpening, EnableExplorationMap, InteractVerdant, InteractCinder }
public sealed record CampaignRuntimeCommand(CampaignRuntimeAction Action, int Act = 0, string Id = "", string Value = "", CombatCommand[]? Commands = null, ProductionCommand? Production = null);
public sealed record CampaignRuntimeResult(bool Success, string Reason, CombatEvent[] CombatEvents, string[] WorldEvents);
public sealed record CampaignRuntimeSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "campaign-runtime.1";
    public long Tick { get; init; }
    public string ActiveEncounterId { get; init; } = "hub";
    public string ExplorationReturnEncounter { get; init; } = "";
    public CampaignState Campaign { get; init; } = null!;
    public ProductionSnapshot Production { get; init; } = null!;
    public CombatSnapshot Combat { get; init; } = null!;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SortedDictionary<string, CombatSnapshot>? ClearedRooms { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LocalMapAtlasState? ExplorationMap { get; init; }
}
public sealed record CampaignRuntimeFrame(CampaignRuntimeCommand Command, string StateHash, string EventHash);
public sealed record CampaignRuntimeReplay(int SchemaVersion, CampaignRuntimeSnapshot Initial, CampaignRuntimeFrame[] Frames);

/// <summary>Actual campaign combat drives the narrative ledger and the shared permanent character owner.</summary>
public sealed partial class CampaignRuntimeSession
{
    private readonly string combatJson;
    private readonly AdventureContent adventure;
    private readonly ProgressionContent policy;
    public RoomDefinition Room => Combat.Room;
    private CampaignSession story;
    private CombatSession arena;
    private string explorationReturnEncounter = "";
    private readonly List<CampaignRuntimeFrame> frames = [];
    private CampaignRuntimeSnapshot initial = null!;
    public CampaignContent Content { get; }
    public ProductionSession Production { get; private set; }
    public CombatSession Combat => InHub ? Production.Combat : arena;
    public long Tick { get; private set; }
    public string ActiveEncounterId { get; private set; } = "hub";
    public bool InHub => story.CurrentState.InHub;
    public bool EncounterCleared => InHub || ActiveEncounterId == "clear" || story.CurrentState.CompletedEncounters.Contains(ActiveEncounterId) ||
        (ActiveEncounterId == CryptEncounter || HasVerdantExploration && ActiveEncounterId is ShrineEncounter or HuntEncounter || HasCinderExploration && ActiveEncounterId is FoundryEncounter or StormEncounter) && !arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
    public CampaignView View => story.ViewForResonance(Production.View.Resonance);
    public CampaignView StoryView => View;
    public IReadOnlyList<string> WorldEvents { get; private set; } = [];
    public string StateHash => JsonData.Hash(Capture());
    public IReadOnlyList<ExpeditionInteraction> Interactions
    {
        get
        {
            if (InHub)
            {
                var residents = story.CurrentState.RescuedResidents;
                return Production.Interactions.Where(i => i.ActionId switch
                {
                    "npc.mara" or "service.mara" => true,
                    "npc.torren" or "service.torren" => residents.Contains("Torren Bale"),
                    "npc.cael" => residents.Contains("Sister Cael"),
                    "npc.oris" => residents.Contains("Oris Fen"),
                    "npc.kesh" => residents.Contains("Kesh"),
                    "hub.workshops" => residents.Contains("Sister Cael") && residents.Contains("Oris Fen") && residents.Contains("Kesh"),
                    _ => false
                }).ToArray();
            }
            if (OpeningInteractions() is { } opening) return opening;
            if (VerdantInteractions() is { } verdant) return verdant;
            if (CinderInteractions() is { } cinder) return cinder;
            var current = story.CurrentState.Exploration;
            if (current is null) return [];
            var definition = Content.Data.Exploration.Single(e => e.Id == current.Id);
            if (definition.Kind != "Hunt" || current.TrackedClues >= definition.Clues.Length) return [];
            Position[] positions = [new(-4200, -2500), new(0, 4500), new(4800, -1800)];
            return [new(definition.Clues[current.TrackedClues], "Track " + definition.Clues[current.TrackedClues].Replace("clue.", "", StringComparison.Ordinal).Replace('_', ' '), positions[current.TrackedClues % positions.Length], 2200)];
        }
    }
    private CampaignRuntimeSession(string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent content, ProductionSession production, CampaignSession story, CombatSession arena)
    { this.combatJson = combatJson; this.adventure = adventure; this.policy = policy; Content = content; Production = production; this.story = story; this.arena = arena; }

    public static ProgressionContent ResolvePolicy(ProgressionContent policy, CampaignContent campaign)
    {
        var definition = policy.Capture();
        return ProgressionContent.Create(definition with { DiscoveryIds = definition.DiscoveryIds.Concat(campaign.Data.Acts.Select(a => "discovery." + a.Id)).Concat(campaign.Data.Exploration.Select(e => e.Discovery)).Distinct().Order(StringComparer.Ordinal).ToArray() });
    }
    public static CampaignRuntimeSession Create(string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign, ulong seed = 42, string discipline = "Vanguard", LocalProfileState? profile = null)
    {
        var resolved = ResolvePolicy(policy, campaign); var production = ProductionSession.Create(combatJson, adventure, resolved, seed, discipline, profile);
        var story = CampaignSession.Create(campaign);
        var session = new CampaignRuntimeSession(combatJson, adventure, resolved, campaign, production, story, production.Combat);
        production.GrantCampaignOutcome("campaign.begin", 0, 0, [], ["Mara Vey"], []);
        session.ValidateRegistry(); session.initial = session.Capture(); return session;
    }
    public static CampaignRuntimeSession Restore(string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign, CampaignRuntimeSnapshot snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.RulesVersion != "campaign-runtime.1" || snapshot.Tick is < 0 or > 1000000000 || snapshot.Campaign is null || snapshot.Production is null || snapshot.Combat is null)
            throw new InvalidDataException("Unsupported or malformed campaign runtime snapshot.");
        var resolved = ResolvePolicy(policy, campaign); var production = ProductionSession.Restore(combatJson, adventure, resolved, snapshot.Production);
        var story = CampaignSession.Restore(campaign, snapshot.Campaign); var arena = CombatSession.Restore(combatJson, snapshot.Combat);
        var session = new CampaignRuntimeSession(combatJson, adventure, resolved, campaign, production, story, arena)
        { Tick = snapshot.Tick, ActiveEncounterId = snapshot.ActiveEncounterId, explorationReturnEncounter = snapshot.ExplorationReturnEncounter };
        session.RestoreClearedRooms(snapshot.ClearedRooms);
        session.RestoreExplorationMap(snapshot.ExplorationMap);
        session.ValidateRegistry(); session.ValidateState(); session.initial = session.Capture(); return session;
    }
    internal static CampaignRuntimeSession ImportProduction(string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign, ProductionSnapshot source)
    {
        var resolved = ResolvePolicy(policy, campaign); var permanentContent = ProductionContent.Resolve(combatJson, resolved, adventure);
        var world = ProductionContent.ResolveAdventure(combatJson, adventure);
        var rebound = source with
        {
            Expedition = source.Expedition with { AdventureHash = world.Hash, Combat = source.Expedition.Combat with { ContentHash = CombatContent.Parse(combatJson).Identity } },
            Progression = source.Progression with { Character = source.Progression.Character with { ContentHash = permanentContent.Hash } }
        };
        var production = ProductionSession.Restore(combatJson, adventure, resolved, rebound);
        var session = new CampaignRuntimeSession(combatJson, adventure, resolved, campaign, production, CampaignSession.Create(campaign), production.Combat);
        production.GrantCampaignOutcome("campaign.begin", 0, 0, [], ["Mara Vey"], []);
        session.ValidateRegistry(); session.ValidateState(); session.initial = session.Capture(); return session;
    }
    public CampaignRuntimeSnapshot Capture() => new() { Tick = Tick, ActiveEncounterId = ActiveEncounterId, ExplorationReturnEncounter = explorationReturnEncounter, Campaign = story.Capture(), Production = Production.Capture(), Combat = Combat.Capture(), ClearedRooms = clearedRooms.Count == 0 ? null : JsonData.Copy(clearedRooms), ExplorationMap = explorationMap?.Capture() };
    public CampaignRuntimeReplay CaptureReplay() => JsonData.Copy(new CampaignRuntimeReplay(1, initial, frames.ToArray()));
    public CampaignRuntimeResult Step(params CombatCommand[] commands) => Execute(new(CampaignRuntimeAction.Tick, Commands: commands));
    public CampaignRuntimeResult EnterAct(int act) => Execute(new(CampaignRuntimeAction.EnterAct, Act: act));
    public CampaignRuntimeResult AdvanceEncounter() => Execute(new(CampaignRuntimeAction.AdvanceEncounter));
    public CampaignRuntimeResult ReturnToHub() => Execute(new(CampaignRuntimeAction.ReturnToHub));
    public CampaignRuntimeResult Choose(string choice, string outcome) => Execute(new(CampaignRuntimeAction.Choose, Id: choice, Value: outcome));
    public CampaignRuntimeResult BeginExploration(string id) => Execute(new(CampaignRuntimeAction.BeginExploration, Id: id));
    public CampaignRuntimeResult TrackClue(string id) => Execute(new(CampaignRuntimeAction.TrackClue, Id: id));
    public CampaignRuntimeResult LeaveExploration() => Execute(new(CampaignRuntimeAction.LeaveExploration));
    public CampaignRuntimeResult ExecuteProduction(ProductionCommand command) => Execute(new(CampaignRuntimeAction.Production, Production: command));

    public CampaignRuntimeResult Execute(CampaignRuntimeCommand command, bool recordReplay = true)
    {
        if (command is null || !Enum.IsDefined(command.Action)) throw new InvalidDataException("Unknown campaign runtime command.");
        if (Tick >= 1000000000 || !Production.ExternalReceiptCapacityAvailable) return new(false, "Campaign archive capacity requires an explicit migration.", [], []);
        if (recordReplay && frames.Count >= 1800) { initial = Capture(); frames.Clear(); }
        var rollback = command.Action == CampaignRuntimeAction.Tick ? null : Capture(); CampaignRuntimeResult result;
        try
        {
            result = command.Action == CampaignRuntimeAction.Tick ? Advance(command.Commands ?? []) : ChangeWorld(command);
            if (result.Success) RevealExplorationMap();
            else if (rollback is not null) RestoreFields(rollback);
        }
        catch { if (rollback is not null) RestoreFields(rollback); throw; }
        WorldEvents = result.WorldEvents;
        if (recordReplay) frames.Add(new(JsonData.Copy(command), StateHash, JsonData.Hash(result)));
        return result;
    }
    private void RestoreFields(CampaignRuntimeSnapshot snapshot)
    {
        Production = ProductionSession.Restore(combatJson, adventure, policy, snapshot.Production);
        story = CampaignSession.Restore(Content, snapshot.Campaign); arena = CombatSession.Restore(combatJson, snapshot.Combat);
        Tick = snapshot.Tick; ActiveEncounterId = snapshot.ActiveEncounterId; explorationReturnEncounter = snapshot.ExplorationReturnEncounter;
        RestoreClearedRooms(snapshot.ClearedRooms);
        RestoreExplorationMap(snapshot.ExplorationMap);
    }
    private CampaignRuntimeResult Advance(CombatCommand[] commands)
    {
        if (commands.Length > 64 || commands.Any(c => c is null)) throw new InvalidDataException("Invalid campaign combat command batch.");
        if (commands.Any(c => c.Kind is CombatCommandKind.Equip or CombatCommandKind.EquipFragment or CombatCommandKind.UnequipFragment or CombatCommandKind.SetMutation))
            return new(false, "Use permanent build services in Greyhaven.", [], []);
        Tick++;
        if (InHub)
        {
            var result = Production.Execute(new(ProductionAction.Expedition, new(ExpeditionAction.Tick, Commands: commands)), recordReplay: false);
            return new(result.Success, result.Reason, result.CombatEvents, result.WorldEvents);
        }
        var before = arena.View.Actors; var events = arena.Step(commands).ToArray(); var messages = new List<string>();
        messages.AddRange(Production.ReconcileCampaignCombat(arena, before, events, Tick));
        if (messages.Count > 0 || events.Any(e => e.Kind is "LootPickedUp" or "LootDropped" or "AbilityStarted")) arena = Production.ProjectCampaignCombat(arena.Capture());
        else arena.ApplyAdventureBuild(Production.Combat.Build);
        var state = story.CurrentState;
        if (arena.View.Actors.Single(a => a.Id == 1).Health <= 0)
        {
            bool cryptDeath = ActiveEncounterId == CryptEncounter;
            string? verdantReturn = VerdantBranchReturn();
            string? cinderReturn = CinderBranchReturn();
            if (HasVerdantExploration || HasCinderExploration) CacheOpeningRoom(restoreDeadPlayer: true);
            var died = story.PlayerDied(); Require(died); messages.AddRange(died.Events); explorationReturnEncounter = "";
            Production.ClearCampaignEffects();
            if (cryptDeath) ResumeOpeningRoom("campaign.road", restoreAtAnchor: true);
            else if (verdantReturn is not null) ResumeOpeningRoom(verdantReturn, restoreAtAnchor: true);
            else if (cinderReturn is not null) ResumeOpeningRoom(cinderReturn, restoreAtAnchor: true);
            else StartExpectedEncounter(restoreAtAnchor: true);
            messages.Add("CampaignCombatRestoredAtAnchor");
        }
        else if (state.Exploration is { } exploration)
        {
            var definition = Content.Data.Exploration.Single(e => e.Id == exploration.Id);
            bool tracking = definition.Kind == "Hunt" && exploration.TrackedClues < definition.Clues.Length;
            if (!tracking && !arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
            {
                if (HasVerdantExploration && definition.Id == HuntEvent)
                {
                    var completion = story.CompleteExploration(HuntEncounter); Require(completion); messages.AddRange(completion.Events);
                    messages.AddRange(Award("campaign.exploration." + HuntEvent, completion));
                    explorationReturnEncounter = "";
                }
                else if (HasCinderExploration && definition.Id == StormEvent)
                {
                    var completion = story.CompleteExploration(StormEncounter); Require(completion); messages.AddRange(completion.Events);
                    messages.AddRange(Award("campaign.exploration." + StormEvent, completion));
                    // Keep historical save return passages until the player physically leaves the won storm.
                }
                // Retain the cleared scoped arena until its visible loot has been picked up. Its timer no longer expires after victory.
                else if (arena.View.Loot.Count == 0 && definition.Id != CryptEvent && !(HasVerdantExploration && definition.Id is ShrineEvent or HuntEvent) && !(HasCinderExploration && definition.Id is FoundryEvent or StormEvent))
                {
                    var completion = story.CompleteExploration(definition.EncounterId); Require(completion); messages.AddRange(completion.Events);
                    messages.AddRange(Award("campaign.exploration." + definition.Id, completion)); EndExplorationArena();
                }
            }
            else if (definition.Kind == "Storm")
            {
                var elapsed = story.AdvanceTicks(1); Require(elapsed); messages.AddRange(elapsed.Events);
                if (story.CurrentState.Exploration is null) EndExplorationArena();
            }
        }
        else if (!EncounterCleared && !arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            var completion = story.CompleteEncounter(ActiveEncounterId); Require(completion); messages.AddRange(completion.Events);
            messages.AddRange(Award("campaign.encounter." + ActiveEncounterId, completion));
        }
        return new(true, "", events, messages.ToArray());
    }
    private CampaignRuntimeResult ChangeWorld(CampaignRuntimeCommand command)
    {
        var state = story.CurrentState; var messages = new List<string>(); CampaignResult result;
        switch (command.Action)
        {
            case CampaignRuntimeAction.EnableExplorationMap:
                explorationMap ??= new(); return new(true, "", [], []);
            case CampaignRuntimeAction.InteractOpening: return InteractOpening(command.Id);
            case CampaignRuntimeAction.InteractVerdant: return InteractVerdant(command.Id);
            case CampaignRuntimeAction.InteractCinder: return InteractCinder(command.Id);
            case CampaignRuntimeAction.RevisitEncounter: return HasCinderExploration && state.CurrentAct == 3 ? RevisitCinderPassage(command.Id) : HasVerdantExploration && state.CurrentAct == 2 ? RevisitVerdantPassage(command.Id) : RevisitOpeningRoom(command.Id);
            case CampaignRuntimeAction.Production:
                if (!state.InHub || command.Production is null) return Failed("Permanent services require Greyhaven.");
                if (command.Production.Action == ProductionAction.Expedition && command.Production.Expedition?.Action is not (ExpeditionAction.Interact or ExpeditionAction.InstallFragment or ExpeditionAction.Manifestation))
                    return Failed("Campaign travel uses the campaign map.");
                if (command.Production.Expedition?.Id == "dungeon.replay") return Failed("Campaign regions keep their own completion history.");
                string specialist = command.Production.Action switch
                {
                    ProductionAction.Equip or ProductionAction.Unequip or ProductionAction.Discard => "service.torren",
                    ProductionAction.Craft => command.Production.Crafting?.Service switch
                    { CraftingService.Tempering => "service.torren", CraftingService.Rebinding => "npc.oris", CraftingService.Engraving => "hub.workshops", CraftingService.Extraction => "npc.kesh", CraftingService.Purification => "npc.cael", _ => "service.mara" },
                    ProductionAction.Expedition when command.Production.Expedition?.Action == ExpeditionAction.Interact => command.Production.Expedition.Id,
                    _ => "service.mara"
                };
                if (!Interactions.Any(i => i.ActionId == specialist)) return Failed("Rescue this specialist before using their Greyhaven workshop.");
                var permanent = Production.Execute(command.Production, recordReplay: false);
                return new(permanent.Success, permanent.Reason, permanent.CombatEvents, permanent.WorldEvents);
            case CampaignRuntimeAction.EnterAct:
                if (!state.InHub && (!EncounterCleared || state.Exploration is not null)) return Failed("Clear this encounter or return to Greyhaven before changing regions.");
                CacheOpeningRoom();
                result = story.EnterAct(command.Act); if (!result.Success) return Failed(result.Reason);
                explorationReturnEncounter = "";
                if (state.InHub) arena = CombatSession.Restore(combatJson, Production.Combat.Capture());
                messages.AddRange(result.Events); messages.AddRange(Award("campaign.visit." + command.Act, result)); ReportUncollectedLoot(messages); StartExpectedEncounter(restoreAtAnchor: true); break;
            case CampaignRuntimeAction.AdvanceEncounter:
                if (state.InHub || state.Exploration is not null || !EncounterCleared) return Failed("Complete the active encounter first.");
                var expected = View.EncounterId;
                if (expected is null) return Failed("This region is complete; choose another unlocked region or return to Greyhaven.");
                if (HasOpeningExploration && state.CurrentAct == 1 && OpeningRooms.Contains(ActiveEncounterId) &&
                    !((ActiveEncounterId, expected) is ("campaign.road", "campaign.monastery") or ("campaign.monastery", "campaign.bell_saint")))
                    return Failed("Follow the adjoining cleared passages to reach the next encounter.");
                if (HasVerdantExploration && state.CurrentAct == 2 && !CanAdvanceVerdant(expected))
                    return Failed("Follow the adjoining forward passage to reach the next encounter.");
                if (HasCinderExploration && state.CurrentAct == 3 && !CanAdvanceCinder(expected))
                    return Failed("Follow the adjoining forward passage to reach the next encounter.");
                if (!ChoiceAllows(expected)) return Failed("Resolve this region's central choice before the final confrontation.");
                ReportUncollectedLoot(messages); StartEncounter(expected, restoreAtAnchor: true); messages.Add("CampaignEncounterEntered:" + expected); break;
            case CampaignRuntimeAction.ReturnToHub:
                CacheOpeningRoom();
                ReportUncollectedLoot(messages); var previous = Combat.Capture(); result = story.ReturnToHub(); Require(result); messages.AddRange(result.Events);
                Production.ReturnCampaignToHub(previous); ActiveEncounterId = "hub"; explorationReturnEncounter = ""; arena = Production.Combat; break;
            case CampaignRuntimeAction.Choose:
                if (!EncounterCleared) return Failed("Secure the area before making this choice.");
                result = story.Choose(command.Id, command.Value); if (!result.Success) return Failed(result.Reason); messages.AddRange(result.Events); break;
            case CampaignRuntimeAction.BeginExploration:
                if (command.Id == CryptEvent) return InteractOpening("opening.crypt.enter");
                if (HasVerdantExploration && command.Id == ShrineEvent) return InteractVerdant("verdant.shrine.enter");
                if (HasVerdantExploration && command.Id == HuntEvent) return InteractVerdant("verdant.hunt.enter");
                if (HasCinderExploration && command.Id == FoundryEvent) return InteractCinder("cinder.foundry.enter");
                if (HasCinderExploration && command.Id == StormEvent) return InteractCinder("cinder.storm.enter");
                if (!EncounterCleared) return Failed("Secure the area before exploring.");
                string returnTo = ActiveEncounterId; result = story.BeginExploration(command.Id); if (!result.Success) return Failed(result.Reason);
                explorationReturnEncounter = returnTo; messages.AddRange(result.Events);
                var eventDefinition = Content.Data.Exploration.Single(e => e.Id == command.Id);
                ReportUncollectedLoot(messages); StartEncounter(eventDefinition.Kind == "Hunt" ? "clear" : eventDefinition.EncounterId, restoreAtAnchor: true); break;
            case CampaignRuntimeAction.TrackClue:
                var point = Interactions.FirstOrDefault(i => i.ActionId == command.Id);
                if (point is null || Position.DistanceSquared(arena.View.Actors.Single(a => a.Id == 1).Position, point.Position) > (long)point.Range * point.Range) return Failed("Move within reach of the hunt's next clue.");
                result = story.TrackClue(command.Id); if (!result.Success) return Failed(result.Reason); messages.AddRange(result.Events);
                var hunt = story.CurrentState.Exploration!; var huntDefinition = Content.Data.Exploration.Single(e => e.Id == hunt.Id);
                if (hunt.TrackedClues == huntDefinition.Clues.Length)
                {
                    if (HasVerdantExploration && hunt.Id == HuntEvent) RevealVerdantHunt();
                    else StartEncounter(huntDefinition.EncounterId, restoreAtAnchor: true);
                }
                break;
            case CampaignRuntimeAction.LeaveExploration:
                if (ActiveEncounterId == CryptEncounter) return InteractOpening("opening.crypt.return");
                if (HasVerdantExploration && ActiveEncounterId == ShrineEncounter) return InteractVerdant("verdant.shrine.return");
                if (HasVerdantExploration && (ActiveEncounterId == HuntEncounter || state.Exploration?.Id == HuntEvent)) return InteractVerdant("verdant.hunt.return");
                if (HasCinderExploration && ActiveEncounterId == FoundryEncounter) return InteractCinder("cinder.foundry.return");
                if (HasCinderExploration && ActiveEncounterId == StormEncounter) return InteractCinder("cinder.storm.return");
                if (state.Exploration is null) return Failed("No exploration context is active.");
                var leavingDefinition = Content.Data.Exploration.Single(e => e.Id == state.Exploration.Id);
                bool completed = ActiveEncounterId == leavingDefinition.EncounterId && !arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0) &&
                    (leavingDefinition.Kind != "Hunt" || state.Exploration.TrackedClues == leavingDefinition.Clues.Length);
                if (completed)
                {
                    result = story.CompleteExploration(leavingDefinition.EncounterId); Require(result); messages.AddRange(result.Events);
                    messages.AddRange(Award("campaign.exploration." + leavingDefinition.Id, result));
                }
                else { result = story.EnterAct(state.CurrentAct); Require(result); messages.AddRange(result.Events); }
                ReportUncollectedLoot(messages); EndExplorationArena(); break;
            default: return Failed("Unknown campaign world command.");
        }
        return new(true, "", [], messages.ToArray());
    }
    private string[] Award(string receipt, CampaignResult result)
    {
        var state = story.CurrentState; var unlocks = new List<string>();
        if (state.CompletedActs.Contains(5)) unlocks.AddRange(["profile.fractures", "profile.god_hunts"]);
        if (state.CompletedExploration.Contains("event.wake_hunt")) unlocks.Add("profile.secret_hunt");
        Production.ReserveCampaignItemSequence(arena.Capture().NextObjectId);
        var events = Production.GrantCampaignOutcome(receipt, result.Experience, result.Materials, state.Discoveries.ToArray(), state.RescuedResidents.ToArray(), unlocks.ToArray(), state.CompletedActs.Contains(1) ? "fragment.heart_serath" : "");
        if (!InHub) arena = Production.ProjectCampaignCombat(arena.Capture()); return events;
    }
    private bool ChoiceAllows(string encounter)
    {
        var state = story.CurrentState; var act = Content.Data.Acts[state.CurrentAct - 1];
        return encounter != act.Encounters[^1].Id || state.Choices.ContainsKey(act.RequiredChoice);
    }
    private void StartExpectedEncounter(bool restoreAtAnchor)
    {
        string? expected = View.EncounterId;
        if (HasCinderExploration && story.CurrentState.CurrentAct == 3 && expected is null)
        { ResumeOpeningRoom("campaign.cinder_pack", restoreAtAnchor); return; }
        if (HasCinderExploration && story.CurrentState.CurrentAct == 3 && expected == "campaign.furnace_spindle" && !ChoiceAllows(expected))
        { ResumeOpeningRoom("campaign.extraction_floor", restoreAtAnchor); return; }
        if (HasVerdantExploration && story.CurrentState.CurrentAct == 2 && expected is null)
        { ResumeOpeningRoom("campaign.living_ruins", restoreAtAnchor); return; }
        if (HasVerdantExploration && story.CurrentState.CurrentAct == 2 && expected == "campaign.rootheart" && !ChoiceAllows(expected))
        { ResumeOpeningRoom("campaign.plague_village", restoreAtAnchor); return; }
        if (HasOpeningExploration && story.CurrentState.CurrentAct == 1 && expected is null)
        { ResumeOpeningRoom("campaign.road", restoreAtAnchor); return; }
        if (HasOpeningExploration && story.CurrentState.CurrentAct == 1 && expected == "campaign.bell_saint" && !ChoiceAllows(expected))
        { ResumeOpeningRoom("campaign.monastery", restoreAtAnchor); return; }
        StartEncounter(expected is not null && ChoiceAllows(expected) ? expected : "clear", restoreAtAnchor);
    }
    private void StartEncounter(string id, bool restoreAtAnchor)
    {
        CacheOpeningRoom();
        Production.ClearCampaignEffects(); var source = Production.ProjectCampaignCombat(arena.Capture()).Capture();
        if (clearedRooms.ContainsKey(id)) { ResumeOpeningRoom(id, restoreAtAnchor); return; }
        arena = CombatSession.CreateEncounter(combatJson, source.Seed, id, source, restoreAtAnchor); ActiveEncounterId = id;
    }
    private void EndExplorationArena()
    {
        string? cinderReturn = CinderBranchReturn();
        string prior = explorationReturnEncounter; explorationReturnEncounter = "";
        if (cinderReturn is not null)
        {
            ResumeOpeningRoom(cinderReturn, restoreAtAnchor: true);
            return;
        }
        StartEncounter("clear", restoreAtAnchor: true);
        ActiveEncounterId = prior == "" ? "clear" : prior;
    }
    private void ReportUncollectedLoot(List<string> messages)
    {
        int count = Combat.View.Loot.Count;
        if (count > 0) messages.Add((!InHub && IsRetainedRoom(ActiveEncounterId) && EncounterCleared
            ? "GroundLootRetained:" : "GroundLootLeftBehind:") + count);
    }
    private static CampaignRuntimeResult Failed(string reason) => new(false, reason, [], []);
    private static void Require(CampaignResult result) { if (!result.Success) throw new InvalidDataException("Authoritative campaign transition rejected: " + result.Reason); }
    private void ValidateRegistry()
    {
        var registry = CombatContent.Parse(combatJson).Campaign ?? throw new InvalidDataException("Campaign runtime requires the composed authored combat registry.");
        foreach (var encounter in Content.Data.Acts.SelectMany(a => a.Encounters))
        {
            var actual = registry.Encounters.FirstOrDefault(e => e.Id == encounter.Id);
            if (actual is null || !encounter.EnemyIds.All(id => actual.Spawns.Any(s => s.EnemyId == id)) || !encounter.EliteModifiers.All(id => actual.Spawns.Any(s => s.Modifiers.Contains(id))))
                throw new InvalidDataException("Narrative enemy/elite declarations differ from authored combat: " + encounter.Id);
        }
        foreach (var exploration in Content.Data.Exploration)
            if (!registry.Encounters.Any(e => e.Id == exploration.EncounterId && e.Rule == exploration.Kind && e.DurationTicks == exploration.DurationTicks))
                throw new InvalidDataException("Exploration lifetime or scoped rule differs from authored combat: " + exploration.Id);
    }
    private void ValidateState()
    {
        var state = story.CurrentState;
        foreach (var encounter in Content.Data.Acts.SelectMany(a => a.Encounters))
            if ((state.CompletedEncounters.Contains(encounter.Id) != Production.ContainsCampaignReceipt("campaign.encounter." + encounter.Id) || state.CompletedEncounters.Contains(encounter.Id) && !Production.HasCampaignReward("campaign.encounter." + encounter.Id, encounter.Experience, encounter.Materials)))
                throw new InvalidDataException("Campaign completion differs from its bound permanent reward receipt.");
        foreach (var exploration in Content.Data.Exploration)
            if ((state.CompletedExploration.Contains(exploration.Id) != Production.ContainsCampaignReceipt("campaign.exploration." + exploration.Id) || state.CompletedExploration.Contains(exploration.Id) && !Production.HasCampaignReward("campaign.exploration." + exploration.Id, 0, exploration.Materials)))
                throw new InvalidDataException("Exploration completion differs from its bound permanent reward receipt.");
        if (!state.Discoveries.All(Production.Capture().Progression.Profile.Discoveries.Contains)) throw new InvalidDataException("Campaign discoveries are missing from the shared profile.");
        if (Production.View.RoomId != "room.greyhaven") throw new InvalidDataException("Campaign permanent owner must remain at its dormant Greyhaven boundary.");
        Production.ValidateCampaignCombat(Combat.Capture());
        ValidateClearedRooms();
        if (state.InHub)
        {
            if (ActiveEncounterId != "hub" || explorationReturnEncounter != "" || arena.EncounterId != "hub" || JsonData.Hash(arena.Capture()) != JsonData.Hash(Production.Combat.Capture())) throw new InvalidDataException("Invalid campaign hub projection.");
            return;
        }
        if (state.Exploration is { } active)
        {
            var definition = Content.Data.Exploration.Single(e => e.Id == active.Id);
            string expected = definition.Kind == "Hunt" && active.TrackedClues < definition.Clues.Length ? "clear" : definition.EncounterId;
            if (ActiveEncounterId != expected || arena.EncounterId != expected || explorationReturnEncounter == "" || (explorationReturnEncounter != "clear" && !state.CompletedEncounters.Contains(explorationReturnEncounter))) throw new InvalidDataException("Exploration combat context does not match its scoped story state.");
            ValidateVerdantExploration(definition.Id, expected);
            ValidateCinderExploration(definition.Id);
        }
        else
        {
            if (ValidateCompletedCinderRoom()) return;
            if (explorationReturnEncounter != "") throw new InvalidDataException("An inactive exploration retained its return context.");
            if (ActiveEncounterId == CryptEncounter && state.CurrentAct == 1 && state.CompletedExploration.Contains(CryptEvent))
            {
                if (!EncounterCleared || arena.EncounterId != CryptEncounter && !(arena.EncounterId == "clear" && arena.Capture().RoomEncounterId == CryptEncounter))
                    throw new InvalidDataException("Completed crypt arena is inconsistent.");
                return;
            }
            if (ValidateCompletedVerdantRoom()) return;
            var act = Content.Data.Acts[state.CurrentAct - 1];
            bool cleared = ActiveEncounterId == "clear" || state.CompletedEncounters.Contains(ActiveEncounterId);
            if (ActiveEncounterId != "clear" && !act.Encounters.Any(e => e.Id == ActiveEncounterId)) throw new InvalidDataException("Arena belongs to another campaign act.");
            if (!cleared && (View.EncounterId != ActiveEncounterId || !ChoiceAllows(ActiveEncounterId) || arena.EncounterId != ActiveEncounterId)) throw new InvalidDataException("Campaign encounter order/choice gate differs from combat.");
            if (cleared && arena.EncounterId != ActiveEncounterId && arena.EncounterId != "clear") throw new InvalidDataException("Cleared arena identity is inconsistent.");
            if (cleared && arena.Capture().RoomEncounterId is { } retainedRoom && retainedRoom != ActiveEncounterId)
                throw new InvalidDataException("Cleared arena layout differs from its active room.");
            if (cleared && arena.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) throw new InvalidDataException("Completed encounter retained living enemies.");
        }

    }
}
