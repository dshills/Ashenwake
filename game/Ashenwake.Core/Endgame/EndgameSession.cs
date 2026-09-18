using System.Diagnostics.CodeAnalysis;
using Ashenwake.Core.Content;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed record EndgameRun
{
    public long Id { get; init; }
    public string Kind { get; init; } = "Fracture";
    public string ContentId { get; init; } = "";
    public long SigilId { get; init; }
    public int Tier { get; init; }
    public ulong Seed { get; init; }
    public int EncounterIndex { get; set; }
    public int AttemptsRemaining { get; set; } = 3;
    public int Deaths { get; set; }
    public string Status { get; set; } = "Active";
    public string[] Modifiers { get; init; } = [];
    public string[] InheritedEliteModifiers { get; set; } = [];
    public int PersistentHazards { get; set; }
    public int HealingEchoes { get; set; }
    public SortedDictionary<string, string> RuleReceipts { get; set; } = [];
}
public sealed record EndgameReward(long RunId, string Kind, string ContentId, int Tier, int Deaths, int Materials, int Mastery, string EvolutionMaterial, int EvolutionCount, long SigilId = 0);
public sealed record EndgameRoomReceipt(long RunId, string Kind, string ContentId, long SigilId, int Tier, int EncounterIndex);
public sealed record EndgameState
{
    public int SchemaVersion { get; init; } = 1;
    public string ContentHash { get; init; } = "";
    public bool Unlocked { get; set; }
    public long NextSigilId { get; set; } = 1;
    public long NextRunId { get; set; } = 1;
    public FractureSigil[] Sigils { get; set; } = [];
    public EndgameRun? Run { get; set; }
    public SortedDictionary<long, EndgameReward> Rewards { get; set; } = [];
    public SortedDictionary<string, EndgameRoomReceipt> CompletedRooms { get; set; } = [];
    public SortedDictionary<string, string> OperationReceipts { get; set; } = [];
}
public sealed record FractureRuleView(int BurningEnemySpeedPercent, int DamageBonusBasisPoints, int LowestResistancePenaltyBasisPoints,
    int FragmentPowerPercent, int HostileHealingEchoes, int PersistentEliteHazards, string[] BossModifiers);
public sealed record EndgameView(bool Unlocked, int HighestClearedTier, string[] UnlockedHunts, long? RunId,
    string RunStatus, string? EncounterId, int AttemptsRemaining, string[] Rules, string[] Counterplay, int TotalMaterialsAwarded,
    IReadOnlyDictionary<string, int> TotalEvolutionMaterialsAwarded, FractureSigil[] AvailableSigils);
public sealed record EndgameResult(bool Success, string Reason, string[] Events, EndgameReward? Reward = null);

/// <summary>Deterministic optional expeditions with consumed sigils, finite attempts, and atomic reward receipts.</summary>
public sealed class EndgameSession
{
    private readonly EndgameContent content;
    private EndgameState state;
    private EndgameDefinition Data => content.Data;
    private EndgameSession(EndgameContent content, EndgameState state) { Validate(content, state); this.content = content; this.state = JsonData.Copy(state); }
    public static EndgameSession Create(EndgameContent content, bool campaignCompleted = false) => new(content, new() { ContentHash = content.Hash, Unlocked = campaignCompleted });
    public static EndgameSession Restore(EndgameContent content, EndgameState state) => new(content, state);
    public EndgameState Capture() => JsonData.Copy(state);
    internal EndgameState CurrentState => state;
    public string StateHash => JsonData.Hash(state);
    private static int HighestTier(EndgameState value) => value.Rewards.Values.Where(r => r.Kind == "Fracture").Select(r => r.Tier).DefaultIfEmpty(0).Max();
    private bool HuntUnlocked(EndgameState value, GodHuntDefinition hunt) => value.Unlocked && HighestTier(value) >= hunt.RequiredTier &&
        (!hunt.Secret || Data.Hunts.Where(h => !h.Secret).All(h => value.Rewards.Values.Any(r => r.Kind == "GodHunt" && r.ContentId == h.Id)));
    private static int TotalMaterialsAwarded(EndgameState value) => value.Rewards.Values.Sum(r => r.Materials);
    private int EncounterCount(EndgameRun run) => run.Kind == "Fracture" ? 4 : Data.Hunts.Single(h => h.Id == run.ContentId).Phases.Length;
    private static string EncounterId(EndgameRun run) => $"run.{run.Id}.encounter.{run.EncounterIndex}";
    public EndgameView View
    {
        get
        {
            var run = state.Run; var hunt = run?.Kind == "GodHunt" ? Data.Hunts.Single(h => h.Id == run.ContentId) : null;
            bool active = run?.Status == "Active";
            var rules = active ? run!.Modifiers.Select(id => Data.Modifiers.Single(m => m.Id == id).Rule).ToArray() : [];
            var counters = active ? hunt is null ? run!.Modifiers.Select(id => Data.Modifiers.Single(m => m.Id == id).Counterplay).ToArray() : [hunt.Counterplay[run!.EncounterIndex]] : [];
            var materials = new SortedDictionary<string, int>();
            foreach (var reward in state.Rewards.Values.Where(r => r.EvolutionMaterial != "")) materials[reward.EvolutionMaterial] = materials.GetValueOrDefault(reward.EvolutionMaterial) + reward.EvolutionCount;
            return new(state.Unlocked, HighestTier(state), Data.Hunts.Where(h => HuntUnlocked(state, h)).Select(h => h.Id).ToArray(), run?.Id,
                run?.Status ?? "None", active ? EncounterId(run!) : null, active ? run!.AttemptsRemaining : 0, rules, counters,
                TotalMaterialsAwarded(state), materials, JsonData.Copy(state.Sigils.Where(s => !s.Consumed).ToArray()));
        }
    }
    public FractureRuleView RulesAt(long tick, int highestResistanceBasisPoints = 0)
    {
        if (tick < 0 || highestResistanceBasisPoints is < 0 or > 9000) throw new ArgumentOutOfRangeException(nameof(tick));
        var run = state.Run; bool active = run?.Status == "Active";
        bool Has(string id) => active && run!.Modifiers.Contains(id);
        return new(Has("fracture.burning_haste") ? 125 : 100,
            Has("fracture.resistance_inversion") ? highestResistanceBasisPoints / 4 : 0,
            Has("fracture.resistance_inversion") ? 1500 : 0,
            Has("fracture.fragment_overcharge") && tick % 120 < 30 ? 150 : 100,
            active ? run!.HealingEchoes : 0, active ? run!.PersistentHazards : 0,
            active && run!.EncounterIndex == EncounterCount(run) - 1 ? run.InheritedEliteModifiers.ToArray() : []);
    }
    private EndgameResult Change(Func<EndgameState, List<string>, string?> mutate)
    {
        var next = Capture(); var events = new List<string>(); string? error = mutate(next, events);
        if (error is not null) return new(false, error, []);
        Validate(content, next);
        var reward = next.Rewards.Values.FirstOrDefault(r => !state.Rewards.ContainsKey(r.RunId)); state = next;
        return new(true, "", events.ToArray(), reward);
    }
    private EndgameResult Operation(string id, object payload, Func<EndgameState, List<string>, string?> mutate)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 120) return new(false, "A bounded operation ID is required.", []);
        string hash = JsonData.Hash(payload);
        if (state.OperationReceipts.TryGetValue(id, out string? prior)) return prior == hash ? new(true, "Operation already committed.", []) : new(false, "Operation ID payload mismatch.", []);
        if (state.OperationReceipts.Count >= 100000) return new(false, "Endgame operation archive capacity reached.", []);
        return Change((next, events) => { string? error = mutate(next, events); if (error is null) next.OperationReceipts[id] = hash; return error; });
    }
    public EndgameResult Unlock(string operationId) => Operation(operationId, new { Action = "Unlock" }, (next, events) =>
    { next.Unlocked = true; events.Add("EndgameUnlocked"); return null; });
    public EndgameResult AwardSigil(string operationId, ulong seed, int tier) => Operation(operationId, new { Action = "AwardSigil", seed, tier }, (next, events) =>
    {
        if (!next.Unlocked || tier is < 1 or > 10 || tier > HighestTier(next) + 1 || next.Sigils.Length >= 10000 || next.NextSigilId == long.MaxValue) return "Sigil tier is locked or inventory is full.";
        var sigil = EndgameContent.GenerateSigil(content, next.NextSigilId++, seed, tier); next.Sigils = [.. next.Sigils, sigil]; events.Add("SigilAwarded:" + sigil.Id); return null;
    });
    public EndgameResult AttuneSigil(string operationId, long sigilId, string oldModifier, string newModifier) => Operation(operationId, new { Action = "Attune", sigilId, oldModifier, newModifier }, (next, events) =>
    {
        var sigil = next.Sigils.FirstOrDefault(s => s.Id == sigilId);
        if (next.Run?.Status == "Active" || sigil is null || sigil.Consumed || !sigil.Modifiers.Contains(oldModifier)) return "An unconsumed sigil and existing modifier are required outside a run.";
        var changed = sigil with { Modifiers = sigil.Modifiers.Select(id => id == oldModifier ? newModifier : id).ToArray() };
        try { EndgameContent.ValidateSigil(content, changed); } catch (InvalidDataException ex) { return ex.Message; }
        if (oldModifier == newModifier) return "Choose a different modifier.";
        // The runtime debits the canonical permanent wallet in the same transaction.
        next.Sigils = next.Sigils.Select(s => s.Id == sigilId ? changed : s).ToArray(); events.Add("SigilAttuned:" + sigilId); return null;
    });
    public EndgameResult StartFracture(long sigilId) => Change((next, events) =>
    {
        var sigil = next.Sigils.FirstOrDefault(s => s.Id == sigilId);
        if (!next.Unlocked || sigil is null || sigil.Consumed || next.Run?.Status == "Active" || next.Rewards.Count >= 10000 || next.CompletedRooms.Count > 39996 || next.NextRunId == long.MaxValue) return "Sigil is unavailable, an expedition is active, or the archive is full.";
        next.Sigils = next.Sigils.Select(s => s.Id == sigilId ? s with { Consumed = true } : s).ToArray();
        next.Run = new() { Id = next.NextRunId++, Kind = "Fracture", ContentId = sigil.BossFamily, SigilId = sigil.Id, Tier = sigil.Tier, Seed = sigil.Seed, Modifiers = sigil.Modifiers.ToArray() };
        events.Add("FractureStarted:" + next.Run.Id); return null;
    });
    public EndgameResult StartGodHunt(string huntId, ulong seed) => Change((next, events) =>
    {
        var hunt = Data.Hunts.FirstOrDefault(h => h.Id == huntId);
        if (hunt is null || !HuntUnlocked(next, hunt) || next.Run?.Status == "Active" || next.Rewards.Count >= 10000 || next.CompletedRooms.Count > 39996 || next.NextRunId == long.MaxValue) return "God Hunt is locked, an expedition is active, or the archive is full.";
        next.Run = new() { Id = next.NextRunId++, Kind = "GodHunt", ContentId = huntId, Tier = hunt.RequiredTier, Seed = seed, AttemptsRemaining = 2 };
        events.Add("GodHuntStarted:" + huntId); return null;
    });
    public EndgameResult CompleteEncounter(string encounterId, string? inheritedCandidate = null) => Change((next, events) =>
    {
        var run = next.Run;
        if (run is null || run.Status != "Active" || encounterId != EncounterId(run)) return "Expedition encounter is absent, already completed, or out of order.";
        bool boss = run.EncounterIndex == EncounterCount(run) - 1;
        if (!boss && run.Modifiers.Contains("fracture.inherited_boss"))
        {
            if (inheritedCandidate is null) return "An authoritative elite candidate is required for inherited-boss rooms.";
            string candidate = inheritedCandidate;
            if (!CombatSession.EliteModifiers.Contains(candidate)) return "Unknown inherited elite candidate.";
            try
            {
                var inherited = run.InheritedEliteModifiers.Append(candidate).ToArray();
                CombatSession.ValidateEliteModifiers(inherited); run.InheritedEliteModifiers = inherited;
                events.Add("BossModifierInherited:" + candidate);
            }
            catch (InvalidDataException) { events.Add("BossModifierSkipped:" + candidate); }
        }
        next.CompletedRooms.Add($"{run.Id}.{run.EncounterIndex}", new(run.Id, run.Kind, run.ContentId, run.SigilId, run.Tier, run.EncounterIndex));
        run.EncounterIndex++; run.HealingEchoes = 0; run.PersistentHazards = 0; run.RuleReceipts.Clear();
        events.Add("EndgameEncounterCompleted:" + encounterId);
        if (!boss) return null;
        run.Status = "Completed";
        int baseReward = 20 + run.Tier * 5; int materials = baseReward * Math.Max(40, 100 - 20 * run.Deaths) / 100;
        var sigil = run.Kind == "Fracture" ? next.Sigils.Single(s => s.Id == run.SigilId) : null;
        var hunt = run.Kind == "GodHunt" ? Data.Hunts.Single(h => h.Id == run.ContentId) : null;
        if (sigil?.RewardTendency == "Materials") materials += 10;
        int mastery = sigil?.RewardTendency == "Mastery" ? 100 + run.Tier * 10 : 25;
        string evolution = hunt?.EvolutionMaterial ?? (sigil?.RewardTendency == "Godwrought" ? "material.divine_catalyst" : "");
        next.Rewards.Add(run.Id, new(run.Id, run.Kind, run.ContentId, run.Tier, run.Deaths, materials, mastery, evolution, evolution == "" ? 0 : 1, run.SigilId));
        if (sigil is not null && next.Sigils.Length < 10000 && next.NextSigilId < long.MaxValue)
        {
            var rewardSigil = EndgameContent.GenerateSigil(content, next.NextSigilId++, unchecked(run.Seed + 0xD1B54A32D192ED03UL), Math.Min(10, run.Tier + 1));
            next.Sigils = [.. next.Sigils, rewardSigil]; events.Add("SigilAwarded:" + rewardSigil.Id);
        }
        events.Add("EndgameRewardCommitted:" + run.Id); return null;
    });
    public EndgameResult PlayerDied() => Change((next, events) =>
    {
        var run = next.Run; if (run is null || run.Status != "Active") return "No expedition is active.";
        run.AttemptsRemaining--; run.Deaths++; run.HealingEchoes = 0; run.PersistentHazards = 0; run.RuleReceipts.Clear();
        if (run.AttemptsRemaining == 0) run.Status = "Failed";
        events.Add(run.Status == "Failed" ? "ExpeditionFailed" : "ExpeditionAttemptConsumed"); return null;
    });
    public EndgameResult Abandon() => Change((next, events) =>
    {
        var run = next.Run; if (run is null || run.Status != "Active") return "No expedition is active.";
        run.Status = "Abandoned"; run.PersistentHazards = 0; run.HealingEchoes = 0; run.RuleReceipts.Clear(); events.Add("ExpeditionAbandoned"); return null;
    });
    public EndgameResult ObserveRuleEvent(string eventId, string kind, int amount = 0) => Change((next, events) =>
    {
        var run = next.Run;
        if (run is null || run.Status != "Active" || string.IsNullOrWhiteSpace(eventId) || eventId.Length > 120 || amount is < 0 or > 100000 || kind is not ("PlayerHealed" or "EliteDied")) return "Invalid expedition rule event.";
        string hash = JsonData.Hash(new { kind, amount });
        if (run.RuleReceipts.TryGetValue(eventId, out var previous)) return previous == hash ? null : "Rule event ID payload mismatch.";
        if (run.RuleReceipts.Count >= 1024) return "Encounter rule-event budget exhausted.";
        run.RuleReceipts[eventId] = hash;
        if (kind == "PlayerHealed" && amount > 0 && run.Modifiers.Contains("fracture.healing_echoes") && run.HealingEchoes < 8)
        { run.HealingEchoes++; events.Add("DelayedHealingEchoSpawned"); }
        if (kind == "EliteDied" && run.Modifiers.Contains("fracture.elite_hazards") && run.PersistentHazards < 8)
        { run.PersistentHazards++; events.Add("PersistentEliteHazardSpawned"); }
        return null;
    });

    public static void Validate(EndgameContent content, EndgameState s)
    {
        static void Check([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Check(s is not null && s.SchemaVersion == 1 && s.ContentHash == content.Hash && s.NextRunId > 0 && s.NextSigilId > 0, "Invalid endgame schema/content/counters.");
        Check(s.Sigils is not null && s.Sigils.Length <= 10000 && s.Rewards is not null && s.Rewards.Count <= 10000 && s.CompletedRooms is not null && s.CompletedRooms.Count <= 40000 && s.OperationReceipts is not null && s.OperationReceipts.Count <= 100000, "Invalid endgame collections.");
        foreach (var sigil in s.Sigils) EndgameContent.ValidateSigil(content, sigil);
        Check(s.Sigils.Select(i => i.Id).Distinct().Count() == s.Sigils.Length && s.Sigils.All(i => i.Id < s.NextSigilId), "Duplicate sigil identity.");
        Check(s.Unlocked || (s.Sigils.Length == 0 && s.Run is null && s.Rewards.Count == 0 && s.CompletedRooms.Count == 0), "Locked endgame has expedition progress.");
        foreach (var pair in s.CompletedRooms)
        {
            var room = pair.Value;
            Check(room is not null && pair.Key == $"{room.RunId}.{room.EncounterIndex}" && room.RunId > 0 && room.RunId < s.NextRunId && room.Tier is >= 1 and <= 10 && room.EncounterIndex is >= 0 and <= 3, "Invalid completed-room identity.");
            var sigil = room.Kind == "Fracture" ? s.Sigils.FirstOrDefault(i => i.Id == room.SigilId) : null;
            var hunt = room.Kind == "GodHunt" ? content.Data.Hunts.FirstOrDefault(h => h.Id == room.ContentId) : null;
            Check(room.Kind == "Fracture" ? sigil is not null && sigil.Consumed && sigil.Tier == room.Tier && sigil.BossFamily == room.ContentId :
                room.Kind == "GodHunt" && room.SigilId == 0 && hunt is not null && hunt.RequiredTier == room.Tier && room.EncounterIndex < hunt.Phases.Length, "Completed room has an invalid immutable source.");
        }
        foreach (var group in s.CompletedRooms.Values.GroupBy(r => r.RunId))
        {
            var source = group.First(); var indices = group.Select(r => r.EncounterIndex).Order().ToArray();
            Check(indices.SequenceEqual(Enumerable.Range(0, indices.Length)) && group.All(r => r.Kind == source.Kind && r.ContentId == source.ContentId && r.SigilId == source.SigilId && r.Tier == source.Tier), "Completed rooms are not a contiguous single-source run.");
            if (indices.Length == (source.Kind == "Fracture" ? 4 : 3)) Check(s.Rewards.ContainsKey(source.RunId), "Completed final room lacks a final reward.");
        }
        Check(s.CompletedRooms.Values.Where(r => r.Kind == "Fracture").GroupBy(r => r.SigilId).All(g => g.Select(r => r.RunId).Distinct().Count() == 1), "A consumed Sigil produced rooms in multiple runs.");
        foreach (var pair in s.Rewards)
        {
            var reward = pair.Value;
            Check(reward is not null && pair.Key == reward.RunId && reward.RunId > 0 && reward.RunId < s.NextRunId && reward.Tier is >= 1 and <= 10 && reward.Deaths is >= 0 and <= 2 && reward.Materials is >= 1 and <= 100 && reward.Mastery is >= 25 and <= 200 && reward.EvolutionCount is >= 0 and <= 1, "Invalid endgame reward receipt.");
            var sigil = reward.Kind == "Fracture" ? s.Sigils.FirstOrDefault(i => i.Id == reward.SigilId) : null;
            var hunt = reward.Kind == "GodHunt" ? content.Data.Hunts.FirstOrDefault(h => h.Id == reward.ContentId) : null;
            Check(reward.Kind == "Fracture" ? sigil is not null && sigil.Consumed && sigil.Tier == reward.Tier && sigil.BossFamily == reward.ContentId : reward.Kind == "GodHunt" && reward.SigilId == 0 && hunt is not null && hunt.RequiredTier == reward.Tier && reward.Deaths < 2, "Unknown/mismatched reward source.");
            int expectedMaterials = (20 + reward.Tier * 5) * Math.Max(40, 100 - 20 * reward.Deaths) / 100 + (sigil?.RewardTendency == "Materials" ? 10 : 0);
            int expectedMastery = sigil?.RewardTendency == "Mastery" ? 100 + reward.Tier * 10 : 25;
            string expectedEvolution = hunt?.EvolutionMaterial ?? (sigil?.RewardTendency == "Godwrought" ? "material.divine_catalyst" : "");
            Check(reward.Materials == expectedMaterials && reward.Mastery == expectedMastery && reward.EvolutionMaterial == expectedEvolution && reward.EvolutionCount == (expectedEvolution == "" ? 0 : 1), "Reward does not match its immutable source and deaths.");
            Check(s.CompletedRooms.Values.Count(r => r.RunId == reward.RunId) == (reward.Kind == "Fracture" ? 4 : 3) &&
                s.CompletedRooms.Values.Where(r => r.RunId == reward.RunId).All(r => r.Kind == reward.Kind && r.ContentId == reward.ContentId && r.SigilId == reward.SigilId && r.Tier == reward.Tier), "Final reward lacks its complete room history.");
        }
        var rewardedSigils = s.Rewards.Values.Where(r => r.Kind == "Fracture").Select(r => r.SigilId).ToArray();
        Check(rewardedSigils.Distinct().Count() == rewardedSigils.Length, "A consumed sigil rewarded more than one run.");
        Check(s.OperationReceipts.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Key.Length <= 120 && p.Value is { Length: 64 } && p.Value.All(Uri.IsHexDigit)), "Invalid endgame operation receipt.");
        if (s.Run is not { } run) return;
        Check(run.Id > 0 && run.Id == s.NextRunId - 1 && run.Tier is >= 1 and <= 10 && run.Modifiers is not null && run.InheritedEliteModifiers is not null && run.RuleReceipts is not null && run.RuleReceipts.Count <= 1024 && run.Status is "Active" or "Completed" or "Failed" or "Abandoned", "Invalid expedition identity/status.");
        int attempts, encounters;
        if (run.Kind == "Fracture")
        {
            var sigil = s.Sigils.FirstOrDefault(i => i.Id == run.SigilId);
            Check(sigil is not null && sigil.Consumed && sigil.Tier == run.Tier && sigil.Seed == run.Seed && sigil.BossFamily == run.ContentId && sigil.Modifiers.SequenceEqual(run.Modifiers), "Expedition does not match its consumed sigil.");
            attempts = 3; encounters = 4;
        }
        else
        {
            var hunt = content.Data.Hunts.FirstOrDefault(h => h.Id == run.ContentId);
            Check(run.Kind == "GodHunt" && run.SigilId == 0 && hunt is not null && hunt.RequiredTier == run.Tier && run.Modifiers.Length == 0 && run.InheritedEliteModifiers.Length == 0 && HighestTier(s) >= hunt.RequiredTier && (!hunt.Secret || content.Data.Hunts.Where(h => !h.Secret).All(h => s.Rewards.Values.Any(r => r.Kind == "GodHunt" && r.ContentId == h.Id))), "Invalid/locked God Hunt.");
            attempts = 2; encounters = hunt.Phases.Length;
        }
        Check(run.AttemptsRemaining >= 0 && run.Deaths >= 0 && run.AttemptsRemaining + run.Deaths == attempts && run.EncounterIndex >= 0 && run.EncounterIndex <= encounters && run.PersistentHazards is >= 0 and <= 8 && run.HealingEchoes is >= 0 and <= 8, "Invalid attempts/encounter/hazard counters.");
        Check(s.CompletedRooms.Values.Count(r => r.RunId == run.Id) == run.EncounterIndex, "Current run differs from completed-room receipts.");
        Check(run.Status == "Completed" ? run.EncounterIndex == encounters && s.Rewards.ContainsKey(run.Id) && run.AttemptsRemaining > 0 : run.EncounterIndex < encounters && !s.Rewards.ContainsKey(run.Id), "Run completion and reward must commit together.");
        if (run.Status == "Completed")
        {
            var reward = s.Rewards[run.Id];
            Check(reward.Kind == run.Kind && reward.ContentId == run.ContentId && reward.SigilId == run.SigilId && reward.Tier == run.Tier && reward.Deaths == run.Deaths, "Completed run does not match its reward receipt.");
        }
        Check((run.Status == "Failed") == (run.AttemptsRemaining == 0), "Failed run must exhaust its attempts.");
        Check(run.Status == "Active" || (run.PersistentHazards == 0 && run.HealingEchoes == 0 && run.RuleReceipts.Count == 0), "Inactive expedition leaked scoped rule state.");
        Check(run.InheritedEliteModifiers.Length <= Math.Min(2, run.EncounterIndex) && (run.Modifiers.Contains("fracture.inherited_boss") || run.InheritedEliteModifiers.Length == 0), "Invalid inherited boss modifiers.");
        CombatSession.ValidateEliteModifiers(run.InheritedEliteModifiers);
        Check((run.Modifiers.Contains("fracture.elite_hazards") || run.PersistentHazards == 0) && (run.Modifiers.Contains("fracture.healing_echoes") || run.HealingEchoes == 0) && run.RuleReceipts.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Key.Length <= 120 && p.Value is { Length: 64 } && p.Value.All(Uri.IsHexDigit)), "Invalid expedition rule state.");
    }
}
