using System.Diagnostics.CodeAnalysis;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Campaign;

public sealed record ExplorationState
{
    public string Id { get; init; } = "";
    public int ReturnAct { get; init; }
    public int RemainingTicks { get; set; }
    public int TrackedClues { get; set; }
}
public sealed record CampaignState
{
    public int SchemaVersion { get; init; } = 1;
    public string ContentHash { get; init; } = "";
    public int CurrentAct { get; set; } = 1;
    public int HighestActVisited { get; set; } = 1;
    public bool InHub { get; set; } = true;
    public int Deaths { get; set; }
    public int EarnedExperience { get; set; }
    public int EarnedMaterials { get; set; }
    public SortedSet<string> CompletedEncounters { get; set; } = [];
    public SortedSet<int> CompletedActs { get; set; } = [];
    public SortedDictionary<string, string> Choices { get; set; } = [];
    public SortedSet<string> WorldFlags { get; set; } = [];
    public SortedSet<string> RescuedResidents { get; set; } = ["Mara Vey"];
    public SortedSet<string> Discoveries { get; set; } = [];
    public SortedSet<string> CompletedExploration { get; set; } = [];
    public ExplorationState? Exploration { get; set; }
}
public sealed record CampaignEnding(string Id, string Summary, string[] Alliances, string[] SurvivingLeaders,
    string GreyhavenCondition, string ResonanceRelationship, bool FracturesUnlocked);
public sealed record CampaignView(int Act, string Region, string Objective, string Revelation, string Anchor,
    int[] AvailableActs, string? EncounterId, string[] WorldFlags, string[] PendingConsequences,
    string[] Residents, string[] ExplorationRules, string[] HubReactions, CampaignEnding? Ending);
public sealed record CampaignResult(bool Success, string Reason, string[] Events, int Experience = 0, int Materials = 0);

/// <summary>Five-act greybox narrative and exploration contracts; encounter completion must come from authoritative combat.</summary>
public sealed class CampaignSession
{
    private readonly CampaignContent content;
    private CampaignState state;
    private CampaignDefinition Data => content.Data;
    private CampaignSession(CampaignContent content, CampaignState state) { Validate(content, state); this.content = content; this.state = JsonData.Copy(state); }
    public static CampaignSession Create(CampaignContent content) => new(content, new() { ContentHash = content.Hash });
    public static CampaignSession Restore(CampaignContent content, CampaignState state) => new(content, state);
    public CampaignState Capture() => JsonData.Copy(state);
    internal CampaignState CurrentState => state;
    public string StateHash => JsonData.Hash(state);
    private IEnumerable<ChoiceOutcome> Outcomes(CampaignState value) => value.Choices.Select(pair => Data.Choices.Single(c => c.Id == pair.Key).Outcomes.Single(o => o.Id == pair.Value));
    private void ApplyConsequences(CampaignState next, List<string> events)
    {
        foreach (var outcome in Outcomes(next))
        {
            foreach (var flag in outcome.ImmediateFlags) if (next.WorldFlags.Add(flag)) events.Add("WorldChanged:" + flag);
            if (outcome.DueAct <= next.HighestActVisited)
                foreach (var flag in outcome.DelayedFlags) if (next.WorldFlags.Add(flag)) events.Add("ConsequenceArrived:" + flag);
        }
    }
    public CampaignView View => ViewForResonance(0);
    public CampaignView ViewForResonance(int resonance)
    {
        var act = Data.Acts[state.CurrentAct - 1];
        var encounter = act.Encounters.FirstOrDefault(e => !state.CompletedEncounters.Contains(e.Id));
        var choice = Data.Choices.Single(c => c.Id == act.RequiredChoice);
        bool choiceReady = state.CompletedEncounters.Contains(choice.RequiredEncounter) && !state.Choices.ContainsKey(choice.Id);
        var reactions = new List<string>
        {
            resonance >= 40 ? "Mara: Your changed body does not settle what kind of person you will be." : "Mara: We can face this without giving every part of ourselves away.",
            state.RescuedResidents.Contains("Torren Bale") ? "Torren: The forge is open. Bring me something worth keeping." : "The forge remains cold; Torren is missing."
        };
        if (state.WorldFlags.Contains("city.hard_winter")) reactions.Add("Kesh: The heat caravans cost us dearly, but the roads are still open.");
        if (state.WorldFlags.Contains("compact.supplies_arrive")) reactions.Add("Kesh: Compact medicine reached the clinic. Mara wants to know what it cost the seal.");
        if (state.WorldFlags.Contains("ilyra.envoys_arrive")) reactions.Add("Sister Cael: The village sent living medicine. Ask the envoys what tending it will require.");
        if (state.WorldFlags.Contains("greyhaven.refugees_arrive")) reactions.Add("Torren: The freed debtors are helping rebuild. We need another roof before the rain.");
        if (state.CompletedActs.Contains(4)) reactions.Add("Oris: These mountains were locks. Every map needs a new legend.");
        string[] rules = state.Exploration is null ? [] : Data.Exploration.Single(e => e.Id == state.Exploration.Id).Rules.ToArray();
        string objective = state.InHub ? "Speak with the residents and choose an unlocked region." : state.Exploration is not null
            ? "Complete " + Data.Exploration.Single(e => e.Id == state.Exploration.Id).Name : choiceReady ? choice.Prompt
            : encounter is null ? state.CurrentAct == 5 ? "Return to Greyhaven and begin the work beyond the campaign." : "Return to Greyhaven or travel to the next act." : encounter.Counterplay;
        return new(state.CurrentAct, state.InHub ? "Greyhaven" : act.Name, objective, state.CompletedEncounters.Contains(act.Encounters[^2].Id) ? act.Revelation : "Explore the region and recover its testimony.", act.Anchor,
            Enumerable.Range(1, Math.Min(5, state.CompletedActs.Count + 1)).ToArray(),
            state.InHub ? null : state.Exploration is null ? encounter?.Id : Data.Exploration.Single(e => e.Id == state.Exploration.Id).EncounterId,
            state.WorldFlags.ToArray(), Outcomes(state).Where(o => o.DueAct > state.HighestActVisited).SelectMany(o => o.DelayedFlags).Distinct().ToArray(),
            state.RescuedResidents.ToArray(), rules, reactions.ToArray(), Ending(resonance));
    }
    public CampaignEnding? Ending(int resonance)
    {
        if (!state.CompletedActs.Contains(5)) return null;
        string id = state.WorldFlags.Contains("ending.shared_stewardship") ? "ending.shared_stewardship" : "ending.guarded_transition";
        var conditions = new List<string> { "Greyhaven is rebuilt around its rescued specialists." };
        if (state.WorldFlags.Contains("greyhaven.shared_watch")) conditions.Add("Its fragment is held by a public watch.");
        if (state.WorldFlags.Contains("greyhaven.refugees_arrive")) conditions.Add("Freed debtors have become new residents.");
        if (state.WorldFlags.Contains("city.hard_winter")) conditions.Add("Heat and medicine remain rationed while replacement systems grow.");
        if (state.WorldFlags.Contains("compact.supplies_arrive")) conditions.Add("Compact supplies sustain the clinic while seal damage demands attention.");
        return new(id,
            "The immediate collapse is prevented. Nhal is still beyond the breach, and Edrath must build a future that does not depend on harvesting dead gods.",
            Outcomes(state).SelectMany(o => o.Allies).Distinct().Order(StringComparer.Ordinal).ToArray(),
            state.RescuedResidents.Where(r => r != "Pale Child").ToArray(), string.Join(" ", conditions),
            resonance >= 40 ? "The protagonist accepts visible transformation while retaining responsibility for its consequences." : "The protagonist preserves a restrained relationship with divine power.", true);
    }
    private CampaignResult Change(Func<CampaignState, List<string>, string?> mutate)
    {
        var next = Capture(); var events = new List<string>(); string? error = mutate(next, events);
        if (error is not null) return new(false, error, []);
        ApplyConsequences(next, events); Validate(content, next);
        int xp = next.EarnedExperience - state.EarnedExperience, materials = next.EarnedMaterials - state.EarnedMaterials;
        state = next; return new(true, "", events.ToArray(), xp, materials);
    }
    public CampaignResult EnterAct(int number) => Change((next, events) =>
    {
        if (number is < 1 or > 5 || number > next.CompletedActs.Count + 1) return "Act is not unlocked.";
        EndExploration(next, events, "travel"); next.CurrentAct = number; next.HighestActVisited = Math.Max(next.HighestActVisited, number); next.InHub = false;
        next.Discoveries.Add("discovery." + Data.Acts[number - 1].Id); events.Add("ActEntered:" + Data.Acts[number - 1].Id); return null;
    });
    public CampaignResult ReturnToHub() => Change((next, events) => { EndExploration(next, events, "hub"); next.InHub = true; events.Add("ReturnedToGreyhaven"); return null; });
    public CampaignResult CompleteEncounter(string encounterId) => Change((next, events) =>
    {
        if (next.InHub || next.Exploration is not null) return "Campaign encounter is not active.";
        var act = Data.Acts[next.CurrentAct - 1]; var expected = act.Encounters.FirstOrDefault(e => !next.CompletedEncounters.Contains(e.Id));
        if (expected is null || expected.Id != encounterId) return "Encounter is out of order or its reward was already granted.";
        bool boss = expected.Id == act.Encounters[^1].Id;
        if (boss && !next.Choices.ContainsKey(act.RequiredChoice)) return "Resolve this act's central choice before its final confrontation.";
        next.CompletedEncounters.Add(encounterId); next.EarnedExperience += expected.Experience; next.EarnedMaterials += expected.Materials;
        if (expected.RescuedResident != "") { next.RescuedResidents.Add(expected.RescuedResident); events.Add("ResidentRescued:" + expected.RescuedResident); }
        if (boss) { next.CompletedActs.Add(act.Number); events.Add("ActCompleted:" + act.Id); if (act.Number == 5) events.Add("EndgameUnlocked"); }
        events.Add("CampaignEncounterCompleted:" + encounterId); return null;
    });
    public CampaignResult Choose(string choiceId, string outcomeId) => Change((next, events) =>
    {
        var choice = Data.Choices.FirstOrDefault(c => c.Id == choiceId);
        if (choice is null || next.InHub || next.CurrentAct != choice.Act || !next.CompletedEncounters.Contains(choice.RequiredEncounter) || next.Exploration is not null) return "Choice is not available here yet.";
        if (!choice.Outcomes.Any(o => o.Id == outcomeId)) return "Unknown choice outcome.";
        if (next.Choices.TryGetValue(choiceId, out string? existing)) return existing == outcomeId ? null : "This consequential choice is already committed.";
        next.Choices[choiceId] = outcomeId; events.Add("ChoiceCommitted:" + choiceId + ":" + outcomeId); return null;
    });
    public CampaignResult BeginExploration(string eventId) => Change((next, events) =>
    {
        var definition = Data.Exploration.FirstOrDefault(e => e.Id == eventId);
        if (definition is null || next.InHub || definition.Act != next.CurrentAct || next.Exploration is not null || next.CompletedExploration.Contains(eventId)) return "Exploration event is unavailable, already active, or completed.";
        next.Exploration = new() { Id = eventId, ReturnAct = next.CurrentAct, RemainingTicks = definition.DurationTicks };
        events.Add("ExplorationStarted:" + eventId); return null;
    });
    public CampaignResult TrackClue(string clueId) => Change((next, events) =>
    {
        if (next.Exploration is null) return "No hunt is active.";
        var definition = Data.Exploration.Single(e => e.Id == next.Exploration.Id);
        if (definition.Kind != "Hunt" || next.Exploration.TrackedClues >= definition.Clues.Length || definition.Clues[next.Exploration.TrackedClues] != clueId) return "Follow the hunt's next tracking clue.";
        next.Exploration.TrackedClues++; events.Add("HuntClueTracked:" + clueId); return null;
    });
    public CampaignResult CompleteExploration(string encounterId) => Change((next, events) =>
    {
        if (next.Exploration is null) return "No exploration encounter is active.";
        var definition = Data.Exploration.Single(e => e.Id == next.Exploration.Id);
        if (definition.EncounterId != encounterId || (definition.Kind == "Hunt" && next.Exploration.TrackedClues != definition.Clues.Length)) return "The exploration encounter or tracking objective is incomplete.";
        if (!next.CompletedExploration.Add(definition.Id)) return "Exploration reward already granted.";
        next.Discoveries.Add(definition.Discovery); next.EarnedMaterials += definition.Materials;
        events.Add("ExplorationCompleted:" + definition.Id); EndExploration(next, events, "completed"); return null;
    });
    public CampaignResult AdvanceTicks(int ticks) => Change((next, events) =>
    {
        if (ticks is < 0 or > 1_000_000) return "Invalid world tick advance.";
        if (next.Exploration is { RemainingTicks: > 0 } active)
        { active.RemainingTicks = Math.Max(0, active.RemainingTicks - ticks); if (active.RemainingTicks == 0) EndExploration(next, events, "expired"); }
        return null;
    });
    public CampaignResult PlayerDied() => Change((next, events) =>
    {
        if (next.InHub) return "Greyhaven is safe.";
        next.Deaths = checked(next.Deaths + 1); EndExploration(next, events, "death");
        events.Add("CampaignAnchorRespawn:" + Data.Acts[next.CurrentAct - 1].Anchor); return null;
    });
    private static void EndExploration(CampaignState next, List<string> events, string reason)
    {
        if (next.Exploration is null) return;
        next.CurrentAct = next.Exploration.ReturnAct; events.Add("ExplorationEnded:" + next.Exploration.Id + ":" + reason); next.Exploration = null;
    }

    public static void Validate(CampaignContent content, CampaignState s)
    {
        static void Check([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Check(s is not null && s.SchemaVersion == 1 && s.ContentHash == content.Hash, "Campaign schema/content mismatch.");
        var d = content.Data;
        Check(s.CurrentAct is >= 1 and <= 5 && s.HighestActVisited >= s.CurrentAct && s.HighestActVisited <= 5 && s.Deaths >= 0 && s.EarnedExperience >= 0 && s.EarnedMaterials >= 0, "Invalid campaign counters.");
        Check(s.CompletedEncounters is not null && s.CompletedActs is not null && s.Choices is not null && s.WorldFlags is not null && s.RescuedResidents is not null && s.Discoveries is not null && s.CompletedExploration is not null, "Null campaign collection.");
        Check(s.CompletedActs.SequenceEqual(Enumerable.Range(1, s.CompletedActs.Count)) && s.CompletedActs.Count <= 5 && s.HighestActVisited <= s.CompletedActs.Count + 1, "Campaign acts were skipped.");
        Check(s.CompletedEncounters.All(id => d.Acts.SelectMany(a => a.Encounters).Any(e => e.Id == id)), "Unknown completed campaign encounter.");
        foreach (var act in d.Acts)
        {
            bool gap = false;
            foreach (var encounter in act.Encounters)
            { if (!s.CompletedEncounters.Contains(encounter.Id)) gap = true; else Check(!gap && act.Number <= s.HighestActVisited, "Campaign encounters were skipped."); }
            Check(s.CompletedActs.Contains(act.Number) == s.CompletedEncounters.Contains(act.Encounters[^1].Id), "Act completion/reward boundary mismatch.");
            Check(!s.CompletedActs.Contains(act.Number) || s.Choices.ContainsKey(act.RequiredChoice), "Completed act is missing its central choice.");
        }
        foreach (var pair in s.Choices)
            Check(d.Choices.Any(c => c.Id == pair.Key && s.CompletedEncounters.Contains(c.RequiredEncounter) && c.Outcomes.Any(o => o.Id == pair.Value)), "Unknown/unavailable saved choice.");
        var outcomes = s.Choices.Select(pair => d.Choices.Single(c => c.Id == pair.Key).Outcomes.Single(o => o.Id == pair.Value)).ToArray();
        var flags = outcomes.SelectMany(o => o.ImmediateFlags.Concat(o.DueAct <= s.HighestActVisited ? o.DelayedFlags : [])).ToHashSet();
        Check(s.WorldFlags.SetEquals(flags), "World flags do not match committed choices/delayed consequences.");
        var residents = d.Acts.SelectMany(a => a.Encounters).Where(e => s.CompletedEncounters.Contains(e.Id) && e.RescuedResident != "").Select(e => e.RescuedResident).Append("Mara Vey").ToHashSet();
        Check(s.RescuedResidents.SetEquals(residents), "Rescued residents do not match campaign events.");
        Check(s.CompletedExploration.All(id => d.Exploration.Any(e => e.Id == id && e.Act <= s.HighestActVisited)), "Unknown/unreachable completed exploration.");
        Check(s.Discoveries.All(id => d.Acts.Take(s.HighestActVisited).Any(a => "discovery." + a.Id == id) || d.Exploration.Any(e => e.Discovery == id && s.CompletedExploration.Contains(e.Id))), "Unknown or unearned discovery.");
        Check(d.Acts.Where(a => a.Encounters.Any(e => s.CompletedEncounters.Contains(e.Id))).All(a => s.Discoveries.Contains("discovery." + a.Id)) &&
            (s.InHub || s.Discoveries.Contains("discovery." + d.Acts[s.CurrentAct - 1].Id)) &&
            (s.HighestActVisited == 1 || s.Discoveries.Contains("discovery." + d.Acts[s.HighestActVisited - 1].Id)), "Visited campaign acts are missing their discovery history.");
        Check(s.EarnedExperience == d.Acts.SelectMany(a => a.Encounters).Where(e => s.CompletedEncounters.Contains(e.Id)).Sum(e => e.Experience) && s.EarnedMaterials == d.Acts.SelectMany(a => a.Encounters).Where(e => s.CompletedEncounters.Contains(e.Id)).Sum(e => e.Materials) + d.Exploration.Where(e => s.CompletedExploration.Contains(e.Id)).Sum(e => e.Materials), "Campaign reward totals do not match completed outcomes.");
        if (s.Exploration is { } active)
        {
            var definition = d.Exploration.FirstOrDefault(e => e.Id == active.Id);
            Check(definition is not null && !s.InHub && active.ReturnAct == s.CurrentAct && definition.Act == s.CurrentAct && !s.CompletedExploration.Contains(active.Id) && active.TrackedClues >= 0 && active.TrackedClues <= definition.Clues.Length, "Invalid exploration context.");
            Check(definition.Kind == "Storm" ? active.RemainingTicks > 0 && active.RemainingTicks <= definition.DurationTicks : active.RemainingTicks == 0, "Invalid exploration lifetime.");
        }
    }
}
