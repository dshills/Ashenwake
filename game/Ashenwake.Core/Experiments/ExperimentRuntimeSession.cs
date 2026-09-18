using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Experiments;

/// <summary>An optional local contract; the enclosed endgame remains the authority for every ordinary item, reward, and victory.</summary>
public sealed class ExperimentRuntimeSession
{
    private readonly string combatJson;
    private readonly AdventureContent adventure;
    private readonly ProgressionContent policy;
    private readonly CampaignContent campaign;
    private readonly EndgameContent endgame;
    private ExperimentRun? run;
    private SortedDictionary<string, ExperimentCosmeticReceipt> cosmetics = [];
    private SortedDictionary<long, ExperimentEntry> entries = [];
    private long sequence;
    private ExperimentSnapshot initial = null!;
    private readonly List<ExperimentFrame> frames = [];
    public EndgameRuntimeSession Endgame { get; private set; }
    public ExperimentContent Content { get; }
    public ProductionSession Production => Endgame.Production;
    public CombatSession Combat => Endgame.Combat;
    public long Tick => Endgame.Tick;
    public bool InHub => Endgame.InHub;
    public string StateHash => JsonData.Hash(Capture());
    public IReadOnlyList<string> WorldEvents { get; private set; } = [];
    public IReadOnlyList<ExpeditionInteraction> Interactions => Endgame.Interactions;
    public ExperimentView View => new(Content.Capture().Name, Content.Admission, InHub && Endgame.View.Unlocked && Content.AcceptingEntries && Endgame.View.AvailableSigils.Length > 0,
        "Borrow the Mind socket for one elite memory. Your owned Mind effect is suppressed until the loan ends; permanent anatomy and Resonance remain unchanged.",
        $"Bind the memory nearby, then use Echo Storm within {Content.Capture().EchoLifetimeTicks} ticks. Casting warns of a Storm field beneath you: move or dodge before it activates.",
        run is null ? null : JsonData.Copy(run), Combat.BorrowedMemory, cosmetics.Keys.ToArray(), entries.Values.ToArray());
    private ExperimentRuntimeSession(string combatJson, AdventureContent adventure, ProgressionContent policy, CampaignContent campaign,
        EndgameContent endgame, ExperimentContent content, EndgameRuntimeSession session)
    { this.combatJson = combatJson; this.adventure = adventure; this.policy = policy; this.campaign = campaign; this.endgame = endgame; Content = content; Endgame = session; }
    public static ExperimentRuntimeSession Create(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent content, ulong seed = 42, string discipline = "Vanguard", LocalProfileState? profile = null)
        => FromEndgame(combatJson, adventure, policy, campaign, endgame, content, EndgameRuntimeSession.Create(combatJson, adventure, policy, campaign, endgame, seed, discipline, profile).Capture());
    public static ExperimentRuntimeSession FromEndgame(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent content, EndgameRuntimeSnapshot snapshot)
    {
        var session = EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, snapshot);
        if (!session.InHub || snapshot.Combat?.Experiment is not null) throw new InvalidDataException("Opt into experiments from an ordinary Greyhaven save.");
        var result = new ExperimentRuntimeSession(combatJson, adventure, policy, campaign, endgame, content, session);
        result.Validate(); result.initial = result.Capture(); return result;
    }
    public static ExperimentRuntimeSession Restore(string combatJson, AdventureContent adventure, ProgressionContent policy,
        CampaignContent campaign, EndgameContent endgame, ExperimentContent content, ExperimentSnapshot snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.RulesVersion != "experiment-runtime.1" || snapshot.ContentHash != content.Hash || snapshot.Endgame is null || snapshot.Cosmetics is null || snapshot.Entries is null || snapshot.OperationSequence is < 0 or > 1000000000)
            throw new InvalidDataException("Experiment snapshot identity or bounds differ.");
        var session = new ExperimentRuntimeSession(combatJson, adventure, policy, campaign, endgame, content, EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, snapshot.Endgame))
        { run = snapshot.Run is null ? null : JsonData.Copy(snapshot.Run), cosmetics = JsonData.Copy(snapshot.Cosmetics), entries = JsonData.Copy(snapshot.Entries), sequence = snapshot.OperationSequence };
        session.Validate(); session.initial = session.Capture(); return session;
    }
    public ExperimentSnapshot Capture() => new() { ContentHash = Content.Hash, OperationSequence = sequence, Endgame = Endgame.Capture(), Run = run is null ? null : JsonData.Copy(run), Cosmetics = JsonData.Copy(cosmetics), Entries = JsonData.Copy(entries) };
    public ExperimentReplay CaptureReplay() => JsonData.Copy(new ExperimentReplay(1, initial, frames.ToArray(), Content.Admission));
    public ExperimentResult StartContract(long sigilId, ExperimentChoice choice = ExperimentChoice.BorrowMind) => Execute(new(ExperimentAction.StartContract, sigilId, Choice: choice));
    public ExperimentResult BindMemory(int sourceActorId) => Execute(new(ExperimentAction.BindMemory, SourceActorId: sourceActorId));
    public ExperimentResult ReleaseMemory() => Execute(new(ExperimentAction.ReleaseMemory));
    public ExperimentResult Step(params CombatCommand[] commands) => Execute(new(ExperimentAction.Tick, Commands: commands));
    public ExperimentResult ExecuteEndgame(EndgameRuntimeCommand command) => Execute(new(ExperimentAction.Endgame, Endgame: command));
    public ExperimentResult Execute(ExperimentCommand command, bool recordReplay = true)
    {
        if (command is null || !Enum.IsDefined(command.Action) || !Enum.IsDefined(command.Choice)) throw new InvalidDataException("Invalid experiment command.");
        if (sequence >= 1000000000) return Fail("Experiment operation capacity requires migration.");
        if (recordReplay && frames.Count >= 1800) { initial = Capture(); frames.Clear(); }
        var rollback = command.Action == ExperimentAction.Tick ? null : Capture(); ExperimentResult result;
        try
        {
            result = Change(command);
            if (result.Success) { ObserveOutcome(); sequence++; }
            else if (rollback is not null) RestoreFields(rollback);
        }
        catch { if (rollback is not null) RestoreFields(rollback); throw; }
        WorldEvents = result.WorldEvents;
        if (recordReplay) frames.Add(new(JsonData.Copy(command), StateHash, JsonData.Hash(result)));
        return result;
    }
    private void RestoreFields(ExperimentSnapshot source)
    { Endgame = EndgameRuntimeSession.Restore(combatJson, adventure, policy, campaign, endgame, source.Endgame); run = source.Run; cosmetics = source.Cosmetics; entries = source.Entries; sequence = source.OperationSequence; }
    private static ExperimentResult Fail(string reason) => new(false, reason, [], []);
    private static ExperimentResult Translate(EndgameRuntimeResult result) => new(result.Success, result.Reason, result.CombatEvents, result.WorldEvents);
    private ExperimentResult Change(ExperimentCommand command)
    {
        switch (command.Action)
        {
            case ExperimentAction.StartContract:
                if (!InHub || !Endgame.View.Unlocked) return Fail("Enter Borrowed Memory from the Greyhaven Fracture gate.");
                if (entries.Count >= 10000) return Fail("Experiment entry history requires an explicit migration.");
                if (command.Choice == ExperimentChoice.BorrowMind && !Content.AcceptingEntries) return Fail("Borrowed Memory is retired; existing runs may finish, but new entries are closed.");
                var started = Endgame.Execute(new(EndgameRuntimeAction.StartFracture, command.SigilId), recordReplay: false);
                if (!started.Success) return Translate(started);
                entries.Add(Endgame.RunView!.Id, new(Endgame.RunView.Id, command.SigilId, command.Choice, Content.Hash, Tick, "Active"));
                run = null;
                if (command.Choice == ExperimentChoice.BorrowMind)
                {
                    Combat.BeginBorrowedMemory(Content.Capture());
                    run = new() { RunId = Endgame.RunView!.Id, SigilId = command.SigilId, ContentHash = Content.Hash, StartedTick = Tick };
                }
                return new(true, "", started.CombatEvents, [.. started.WorldEvents, command.Choice == ExperimentChoice.BorrowMind ? "ExperimentMindBorrowed" : "ExperimentMindKept"]);
            case ExperimentAction.BindMemory:
                if (run?.Status != "Active" || !Combat.BindBorrowedMemory(command.SourceActorId)) return Fail("Stand near the offered elite memory and finish the current action before binding it.");
                return new(true, "", [], ["ExperimentMemoryBound"]);
            case ExperimentAction.ReleaseMemory:
                if (run?.Status != "Active" || !Combat.ReleaseBorrowedMemory()) return Fail("No borrowed Mind effect is active.");
                return new(true, "", [], ["ExperimentMemoryReleased"]);
            case ExperimentAction.Tick:
                return Translate(Endgame.Execute(new(EndgameRuntimeAction.Tick, Commands: command.Commands ?? []), recordReplay: false));
            case ExperimentAction.Endgame:
                var nested = command.Endgame ?? throw new InvalidDataException("Missing endgame command.");
                var beforeRoom = Combat.Capture().Endgame?.EncounterIndex;
                bool transition = nested.Action is EndgameRuntimeAction.AdvanceEncounter or EndgameRuntimeAction.RetryEncounter or EndgameRuntimeAction.Abandon or EndgameRuntimeAction.ReturnToHub;
                var memory = transition ? Combat.DetachBorrowedMemory() : null;
                var changed = Endgame.Execute(nested, recordReplay: false);
                if (changed.Success && nested.Action is EndgameRuntimeAction.StartFracture or EndgameRuntimeAction.StartGodHunt) run = null;
                if (memory is not null && changed.Success && !InHub && Endgame.RunView?.Id == memory.RunId)
                    Combat.RestoreBorrowedMemory(memory, beforeRoom != Combat.Capture().Endgame?.EncounterIndex || nested.Action == EndgameRuntimeAction.RetryEncounter);
                return Translate(changed);
            default: throw new InvalidDataException("Unknown experiment command.");
        }
    }
    private void ObserveOutcome()
    {
        if (Endgame.RunView is { } current && entries.TryGetValue(current.Id, out var entry))
            entries[current.Id] = entry with { Outcome = current.Status };
        if (run is null) return;
        var memory = Combat.CaptureBorrowedMemory();
        if (memory is not null)
        {
            run.SourceActorId = memory.SourceActorId; run.SourceRoom = memory.SourceRoom; run.BoundTick = memory.BoundTick; run.UsedTick = memory.UsedTick; run.EchoActionId = memory.EchoActionId;
        }
        if (Endgame.RunView?.Id != run.RunId) { if (run.Status == "Active") throw new InvalidDataException("Contract lost its authoritative Fracture."); return; }
        run.Status = Endgame.RunView.Status;
        if (run.Status == "Completed" && run.EchoActionId > 0)
        {
            string cosmetic = Content.Capture().CosmeticId;
            cosmetics.TryAdd(cosmetic, new(cosmetic, Content.Hash, run.RunId, run.SourceActorId, run.EchoActionId, Tick));
        }
    }
    private void Validate()
    {
        var state = Endgame.Capture(); var memory = state.Combat?.Experiment;
        if (state.Campaign.Combat.Experiment is not null || state.Campaign.Production.Expedition.Combat.Experiment is not null || cosmetics.Count > 1 || entries.Count > 10000 || entries.Values.Any(e => e is null) || entries.Values.Select(e => e.SigilId).Distinct().Count() != entries.Count)
            throw new InvalidDataException("Experiment state escaped its Fracture scope.");
        foreach (var (id, entry) in entries)
            if (entry is null || id != entry.RunId || id <= 0 || id >= state.Endgame.NextRunId || !Enum.IsDefined(entry.Choice) || entry.ContentHash != Content.Hash || entry.StartedTick < 0 || entry.StartedTick > Tick ||
                entry.Outcome is not ("Active" or "Completed" or "Failed" or "Abandoned") || !state.Endgame.Sigils.Any(s => s.Id == entry.SigilId && s.Consumed) ||
                entry.Outcome == "Completed" && (!state.Endgame.Rewards.TryGetValue(id, out var earned) || earned.Kind != "Fracture") ||
                entry.Outcome == "Active" && state.Endgame.Run?.Id != id || state.Endgame.Run?.Id == id && (state.Endgame.Run.SigilId != entry.SigilId || state.Endgame.Run.Status != entry.Outcome))
                throw new InvalidDataException("Experiment entry history differs from consumed Sigils and authoritative outcomes.");
        if (run is null) { if (memory is not null) throw new InvalidDataException("Orphan memory context."); }
        else
        {
            var authoritative = state.Endgame.Run;
            if (!entries.TryGetValue(run.RunId, out var selected) || selected.Choice != ExperimentChoice.BorrowMind || selected.StartedTick != run.StartedTick || run.ContentHash != Content.Hash || authoritative is null || authoritative.Id != run.RunId || authoritative.Kind != "Fracture" || authoritative.SigilId != run.SigilId || run.Status != authoritative.Status || run.StartedTick < 0 || run.StartedTick > Tick ||
                run.SourceRoom is < 0 or > 3 || run.SourceActorId < 0 || run.BoundTick < 0 || run.BoundTick > Combat.Tick || run.UsedTick < 0 || run.UsedTick > Combat.Tick || run.EchoActionId < 0 || run.EchoActionId > 0 && (run.SourceActorId == 0 || run.BoundTick == 0))
                throw new InvalidDataException("Experiment contract differs from its authoritative Fracture.");
            if (!InHub && (memory is null || memory.ContentHash != Content.Hash || memory.RunId != run.RunId || memory.SourceActorId != run.SourceActorId || memory.SourceRoom != run.SourceRoom || memory.BoundTick != run.BoundTick || memory.UsedTick != run.UsedTick || memory.EchoActionId != run.EchoActionId))
                throw new InvalidDataException("Borrowed memory and contract receipts differ.");
            if (InHub && memory is not null) throw new InvalidDataException("Borrowed memory escaped into Greyhaven.");
        }
        foreach (var (id, receipt) in cosmetics)
            if (receipt is null || id != Content.Capture().CosmeticId || receipt.Id != id || receipt.ContentHash != Content.Hash || receipt.SourceActorId <= 0 || receipt.EchoActionId <= 0 || receipt.CompletedTick < 0 || receipt.CompletedTick > Tick ||
                !entries.TryGetValue(receipt.RunId, out var choice) || choice.Choice != ExperimentChoice.BorrowMind || choice.Outcome != "Completed" ||
                !state.Endgame.Rewards.TryGetValue(receipt.RunId, out var reward) || reward.Kind != "Fracture") throw new InvalidDataException("Cosmetic receipt lacks its actual completed borrowed Fracture.");
        if (run is { Status: "Completed", EchoActionId: > 0 } && !cosmetics.ContainsKey(Content.Capture().CosmeticId)) throw new InvalidDataException("Completed memory contract lacks its cosmetic receipt.");
    }
}
