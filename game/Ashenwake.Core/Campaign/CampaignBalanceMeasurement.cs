using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Production;

namespace Ashenwake.Core.Campaign;

public sealed record CampaignBalanceObservation(string RoomId, int Act, bool InHub, bool LiveEnemies, bool HasLoot,
    int Resource, bool HeatSaturated, string[] ResourceLimitedSkills, int Level, long Experience, int Materials, int AvailablePassivePoints, string[] AvailableSkills)
{
    public static CampaignBalanceObservation Capture(CampaignRuntimeSession session)
    {
        var combat = session.Combat.View; var story = session.View; var progress = session.Production.ProgressionView;
        string room = session.InHub ? "hub" : session.ActiveEncounterId == "clear" ? story.EncounterId ?? "clear.act." + story.Act : session.ActiveEncounterId;
        return new(room, story.Act, session.InHub, combat.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0), combat.Loot.Count > 0,
            combat.Resource, combat.Discipline == "Arcanist" && combat.Resource == combat.MaxResource,
            combat.Skills.Where(skill => skill.Available && skill.RemainingTicks == 0 &&
                (combat.Discipline == "Arcanist" && skill.ResourceMode == "Heat" ? combat.Resource + skill.Cost > combat.MaxResource : skill.Cost > combat.Resource))
                .Select(skill => skill.Id).Order(StringComparer.Ordinal).ToArray(), progress.Level, progress.Experience, progress.Materials, progress.AvailablePassivePoints,
            combat.Skills.Where(s => s.Available).Select(s => s.Id).Order(StringComparer.Ordinal).ToArray());
    }
}

public sealed class CampaignBalanceRoom
{
    public string RoomId { get; init; } = "";
    public int Act { get; init; }
    public int Visits { get; set; }
    public long EntryCommand { get; init; }
    public long ExitCommand { get; set; }
    public int Attempts { get; set; }
    public int Deaths { get; set; }
    public bool Completed { get; set; }
    public long CombatTicks { get; set; }
    public long TravelTicks { get; set; }
    public long LootTicks { get; set; }
    public long MenuCommands { get; set; }
    public long DamageTaken { get; set; }
    public long ZeroResourceCombatTicks { get; set; }
    public long HeatSaturatedCombatTicks { get; set; }
    public long ResourceLimitedCombatTicks { get; set; }
    public SortedDictionary<string, int> ResourceLimitedSkills { get; } = new(StringComparer.Ordinal);
    public int Potions { get; set; }
    public int EntryLevel { get; init; }
    public int ExitLevel { get; set; }
    public long EntryExperience { get; init; }
    public long ExitExperience { get; set; }
    public long ExperienceEarned { get; set; }
    public int EntryMaterials { get; init; }
    public int ExitMaterials { get; set; }
    public long MaterialsEarned { get; set; }
    public long MaterialsSpent { get; set; }
    public int EntryAvailablePassivePoints { get; init; }
    public int ExitAvailablePassivePoints { get; set; }
    public SortedDictionary<string, int> SkillsActivated { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> RejectedCommands { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> WorldActions { get; } = new(StringComparer.Ordinal);
    public List<CampaignBalanceGear> GearGrants { get; } = [];
    public double CombatSeconds => CombatTicks / 30.0;
    public double TravelSeconds => TravelTicks / 30.0;
    public double LootSeconds => LootTicks / 30.0;
}
public sealed record CampaignBalanceGear(long Command, int Act, string RoomId, long Id, string DefinitionId, ItemRarity Rarity,
    string Source, int DefinitionOccurrence, long[] EarlierOwnedIds);
public sealed record CampaignBalanceUnlock(long Command, int Act, string RoomId, int Level, long Experience, string SkillId);

/// <summary>Read-only measurement. Gameplay commands and authoritatively earned inventory remain owned by the session.</summary>
public sealed class CampaignBalanceMeasurement
{
    private readonly SortedDictionary<string, CampaignBalanceRoom> rooms = new(StringComparer.Ordinal);
    private readonly HashSet<long> ownedIds = [];
    private readonly Dictionary<string, List<long>> definitions = new(StringComparer.Ordinal);
    private readonly HashSet<string> availableSkills = new(StringComparer.Ordinal);
    private string priorRoom = "";
    private bool priorLive;
    public List<CampaignBalanceGear> GearHistory { get; } = [];
    public List<CampaignBalanceUnlock> AbilityUnlocks { get; } = [];
    public List<object> BuildChanges { get; } = [];
    public IReadOnlyList<CampaignBalanceRoom> Rooms => rooms.Values.ToArray();
    public long Commands { get; private set; }

    public CampaignBalanceMeasurement(CampaignRuntimeSession session)
    {
        var first = CampaignBalanceObservation.Capture(session);
        ObserveInventory(session, first, "starting", null);
        foreach (string skill in first.AvailableSkills) { availableSkills.Add(skill); AbilityUnlocks.Add(new(0, first.Act, first.RoomId, first.Level, first.Experience, skill)); }
    }
    public CampaignBalanceRoom Observe(CampaignBalanceObservation before, CampaignRuntimeCommand command,
        CampaignRuntimeResult result, CampaignRuntimeSession session)
    {
        Commands++;
        string key = before.Act + ":" + before.RoomId;
        if (!rooms.TryGetValue(key, out var row))
        {
            row = new()
            {
                RoomId = before.RoomId,
                Act = before.Act,
                EntryCommand = Commands,
                EntryLevel = before.Level,
                EntryExperience = before.Experience,
                EntryMaterials = before.Materials,
                EntryAvailablePassivePoints = before.AvailablePassivePoints
            };
            rooms.Add(key, row);
        }
        if (priorRoom != key) { row.Visits++; if (before.LiveEnemies) row.Attempts++; }
        else if (before.LiveEnemies && !priorLive) row.Attempts++;
        priorRoom = key; priorLive = before.LiveEnemies;
        if (command.Action == CampaignRuntimeAction.Tick)
        {
            if (!before.InHub && before.LiveEnemies)
            {
                row.CombatTicks++; if (before.Resource == 0) row.ZeroResourceCombatTicks++;
                if (before.HeatSaturated) row.HeatSaturatedCombatTicks++;
                if (before.ResourceLimitedSkills.Length > 0) row.ResourceLimitedCombatTicks++;
                foreach (string skill in before.ResourceLimitedSkills) row.ResourceLimitedSkills[skill] = row.ResourceLimitedSkills.GetValueOrDefault(skill) + 1;
            }
            else if (before.HasLoot && command.Commands?.Any(c => c.Kind == CombatCommandKind.Pickup) == true) row.LootTicks++;
            else row.TravelTicks++;
        }
        else row.MenuCommands++;
        foreach (var e in result.CombatEvents)
        {
            if (e.Kind == "DamageApplied" && e.TargetId == 1) row.DamageTaken += e.Amount;
            if (e.Kind == "AbilityStarted" && e.ActorId == 1) row.SkillsActivated[e.ContentId] = row.SkillsActivated.GetValueOrDefault(e.ContentId) + 1;
            if (e.Kind == "CommandRejected") row.RejectedCommands[e.ContentId] = row.RejectedCommands.GetValueOrDefault(e.ContentId) + 1;
            if (e.Kind == "Healed" && e.TargetId == 1 && e.ContentId == "potion") row.Potions++;
        }
        foreach (string e in result.WorldEvents)
        {
            string kind = e.Split(':')[0]; row.WorldActions[kind] = row.WorldActions.GetValueOrDefault(kind) + 1;
            if (kind == "CampaignAnchorRespawn") { row.Deaths++; if (session.ActiveEncounterId == before.RoomId && !session.EncounterCleared) row.Attempts++; }
            if (kind is "CampaignEncounterCompleted" or "ExplorationCompleted") row.Completed = true;
        }
        var after = CampaignBalanceObservation.Capture(session);
        row.ExitCommand = Commands; row.ExitLevel = after.Level; row.ExitExperience = after.Experience; row.ExitMaterials = after.Materials;
        row.ExitAvailablePassivePoints = after.AvailablePassivePoints;
        row.ExperienceEarned += Math.Max(0, after.Experience - before.Experience);
        row.MaterialsEarned += Math.Max(0, after.Materials - before.Materials);
        row.MaterialsSpent += Math.Max(0, before.Materials - after.Materials);
        foreach (string skill in after.AvailableSkills)
            if (availableSkills.Add(skill)) AbilityUnlocks.Add(new(Commands, before.Act, before.RoomId, after.Level, after.Experience, skill));
        if (result.CombatEvents.Any(e => e.Kind == "LootPickedUp") || result.WorldEvents.Any(e => e.StartsWith("ItemGranted:", StringComparison.Ordinal)))
            ObserveInventory(session, before, result.CombatEvents.Any(e => e.Kind == "LootPickedUp") ? "pickup" : "grant", row);
        if (command.Action == CampaignRuntimeAction.Production && command.Production?.Action is ProductionAction.Equip or ProductionAction.Passive)
            BuildChanges.Add(new { command = Commands, before.Act, before.RoomId, production = command.Production, result.Success });
        return row;
    }
    private void ObserveInventory(CampaignRuntimeSession session, CampaignBalanceObservation context, string source, CampaignBalanceRoom? row)
    {
        foreach (var item in session.Production.Capture().Progression.Character.Items.OrderBy(i => i.Id))
        {
            if (!ownedIds.Add(item.Id)) continue;
            if (!definitions.TryGetValue(item.DefinitionId, out var prior)) { prior = []; definitions.Add(item.DefinitionId, prior); }
            var grant = new CampaignBalanceGear(Commands, context.Act, context.RoomId, item.Id, item.DefinitionId, item.Rarity, source, prior.Count + 1, prior.ToArray());
            prior.Add(item.Id); GearHistory.Add(grant); row?.GearGrants.Add(grant);
        }
    }
    public object[] ActSummaries() => rooms.Values.GroupBy(r => r.Act).OrderBy(g => g.Key).Select(g => (object)new
    {
        act = g.Key,
        combatTicks = g.Sum(r => r.CombatTicks),
        travelTicks = g.Sum(r => r.TravelTicks),
        lootTicks = g.Sum(r => r.LootTicks),
        menuCommands = g.Sum(r => r.MenuCommands),
        deaths = g.Sum(r => r.Deaths),
        attempts = g.Sum(r => r.Attempts),
        damageTaken = g.Sum(r => r.DamageTaken),
        potions = g.Sum(r => r.Potions),
        zeroResourceCombatTicks = g.Sum(r => r.ZeroResourceCombatTicks),
        heatSaturatedCombatTicks = g.Sum(r => r.HeatSaturatedCombatTicks),
        resourceLimitedCombatTicks = g.Sum(r => r.ResourceLimitedCombatTicks),
        entryLevel = g.MinBy(r => r.EntryCommand)!.EntryLevel,
        exitLevel = g.MaxBy(r => r.ExitCommand)!.ExitLevel,
        entryExperience = g.MinBy(r => r.EntryCommand)!.EntryExperience,
        exitExperience = g.MaxBy(r => r.ExitCommand)!.ExitExperience,
        entryMaterials = g.MinBy(r => r.EntryCommand)!.EntryMaterials,
        exitMaterials = g.MaxBy(r => r.ExitCommand)!.ExitMaterials,
        entryAvailablePassivePoints = g.MinBy(r => r.EntryCommand)!.EntryAvailablePassivePoints,
        exitAvailablePassivePoints = g.MaxBy(r => r.ExitCommand)!.ExitAvailablePassivePoints,
        skillsActivated = SumCounters(g.SelectMany(r => r.SkillsActivated)),
        rejectedCommands = SumCounters(g.SelectMany(r => r.RejectedCommands)),
        resourceLimitedSkills = SumCounters(g.SelectMany(r => r.ResourceLimitedSkills)),
        experienceEarned = g.Sum(r => r.ExperienceEarned),
        materialsEarned = g.Sum(r => r.MaterialsEarned),
        materialsSpent = g.Sum(r => r.MaterialsSpent),
        roomsCompleted = g.Count(r => r.Completed),
        gearGrants = g.Sum(r => r.GearGrants.Count),
        duplicateDefinitionGrants = g.Sum(r => r.GearGrants.Count(i => i.DefinitionOccurrence > 1))
    }).ToArray();

    private static SortedDictionary<string, int> SumCounters(IEnumerable<KeyValuePair<string, int>> values)
        => new(values.GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Sum(p => p.Value)), StringComparer.Ordinal);
}
