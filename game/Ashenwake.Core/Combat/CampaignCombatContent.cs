using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using System.Text.Json.Serialization;

namespace Ashenwake.Core.Combat;

public sealed record CampaignEnemyBehavior(string EnemyId, string Pattern);
public sealed record CampaignCombatSpawn(string EnemyId, Position Position, string[] Modifiers, bool Hidden = false);
public sealed record CampaignCombatEncounter(string Id, string Name, string Rule, int DurationTicks, CampaignCombatSpawn[] Spawns,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RoomDefinition? Room = null);
public sealed record CampaignCombatDefinition(int SchemaVersion, string Version, CombatEnemy[] Enemies, CampaignEnemyBehavior[] Behaviors, CampaignCombatEncounter[] Encounters);
public sealed record CombatHazardView(long Id, string Kind, Position Position, Position End, int Radius, long RemainingTicks, string ContentId, int SourceId);
public sealed record CampaignHazard(long Id, string Kind, Position Position, Position End, int Radius, long ResolveTick,
    string ContentId, int SourceId, int Damage, DamageFamily Family, string Status, long ActionId);
public sealed record CampaignActorState
{
    public string[] Modifiers { get; init; } = [];
    public long NextEliteTick { get; set; }
    public bool IsEcho { get; init; }
    public long ExpiresTick { get; init; }
    public bool MirrorUsed { get; set; }
    public bool ResurrectionUsed { get; set; }
    public int Empowerment { get; set; }
    public int Devoured { get; set; }
    public long GuardedUntil { get; set; }
}
public sealed record CampaignCombatState
{
    public long StartedTick { get; init; }
    public long RuleUntil { get; init; }
    public long NextHazardTick { get; set; }
    public int HazardCycle { get; set; }
    public int BossPhase { get; set; } = 1;
    public string SuppressedFragmentId { get; set; } = "";
    public long SuppressedUntil { get; set; }
    public long SuppressionReadyTick { get; set; }
    public SortedDictionary<int, CampaignActorState> Actors { get; init; } = [];
    public List<CampaignHazard> Hazards { get; init; } = [];
}

/// <summary>Composes campaign encounter data with the same validated skills, equipment and simulation used by Production.</summary>
public sealed class CampaignCombatContent
{
    public string CombatJson { get; }
    public string[] EncounterIds { get; }
    private CampaignCombatContent(string json, string[] ids) { CombatJson = json; EncounterIds = ids; }
    public static CampaignCombatContent Parse(string baseCombatJson, string campaignCombatJson)
    {
        var baseline = CombatContent.Parse(baseCombatJson);
        CampaignCombatDefinition campaign;
        try { campaign = JsonData.Read<CampaignCombatDefinition>(campaignCombatJson); }
        catch (System.Text.Json.JsonException ex) { throw new InvalidDataException("Invalid campaign combat JSON.", ex); }
        if (campaign?.Enemies is null) throw new InvalidDataException("Campaign combat definitions are missing.");
        var composed = baseline with { ContentVersion = baseline.ContentVersion + "+" + campaign.Version, Enemies = [.. baseline.Enemies, .. campaign.Enemies], Campaign = campaign };
        string json = JsonData.Write(composed); var parsed = CombatContent.Parse(json);
        return new(json, parsed.Campaign!.Encounters.Select(e => e.Id).ToArray());
    }
    public CombatSession CreateEncounter(string encounterId, ulong seed = 42, CombatSnapshot? previous = null, bool restoreAtAnchor = false)
        => CombatSession.CreateEncounter(CombatJson, seed, encounterId, previous, restoreAtAnchor);
    public CombatSession CreateClearedEncounter(string layoutEncounterId, ulong seed = 42, CombatSnapshot? previous = null, bool restoreAtAnchor = false)
        => CombatSession.CreateClearedEncounter(CombatJson, seed, layoutEncounterId, previous, restoreAtAnchor);
}

public sealed partial class CombatSession
{
    public static IReadOnlyList<string> EliteModifiers { get; } = Array.AsReadOnly(new[] { "Mirrorborn", "Gravewake", "Stormbound", "Devourer", "Null", "Hunter", "Martyr", "Riftborn" });
    private static readonly string[] CampaignPatterns = ["SonicLane", "MemoryArrow", "VenomPod", "Swarm", "PoisonBurst", "ForgeSweep", "HeatVent", "Fault", "OathMark", "ShadowDouble", "CausalEcho", "Rootheart", "Furnace", "Covenant", "Breach", "Bell", "Antler", "Root", "SupportFire"];
    private static readonly string[] CampaignRules = ["Ambush", "SonicLanes", "Bell", "PoisonLanes", "Quarantine", "Rootheart", "Cinder", "Conveyor", "Furnace", "Faults", "OathZones", "Covenant", "Shadows", "CausalEchoes", "Breach", "Storm", "Memory", "Hunt"];
    public static void ValidateEliteModifiers(IReadOnlyList<string> modifiers)
    {
        if (modifiers is null || modifiers.Count > 2 || modifiers.Distinct().Count() != modifiers.Count || modifiers.Any(m => !EliteModifiers.Contains(m)) ||
            modifiers.Contains("Mirrorborn") && modifiers.Contains("Gravewake") || modifiers.Contains("Null") && modifiers.Contains("Hunter") || modifiers.Contains("Devourer") && modifiers.Contains("Martyr"))
            throw new InvalidDataException("Invalid or incompatible elite modifier combination.");
    }
    internal static void ValidateCampaignContent(CombatContent content)
    {
        var campaign = content.Campaign; if (campaign is null) return;
        if (campaign.SchemaVersion != 1 || string.IsNullOrWhiteSpace(campaign.Version) || campaign.Enemies is null || campaign.Behaviors is null || campaign.Encounters is not { Length: > 0 and <= 64 } || campaign.Behaviors.Length > 64)
            throw new InvalidDataException("Invalid campaign combat registry.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var behavior in campaign.Behaviors)
            if (behavior is null || !ids.Add(behavior.EnemyId) || !content.Enemies.Any(e => e.Id == behavior.EnemyId) || !CampaignPatterns.Contains(behavior.Pattern)) throw new InvalidDataException("Invalid campaign enemy behavior.");
        ids.Clear();
        foreach (var encounter in campaign.Encounters)
        {
            if (encounter is null || string.IsNullOrWhiteSpace(encounter.Id) || !ids.Add(encounter.Id) || !(encounter.Id.StartsWith("campaign.", StringComparison.Ordinal) || encounter.Id.StartsWith("exploration.", StringComparison.Ordinal)) || encounter.Id.Length > 100 || !encounter.Id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_') || string.IsNullOrWhiteSpace(encounter.Name) || !CampaignRules.Contains(encounter.Rule) || encounter.DurationTicks is < 0 or > 9000 || encounter.Spawns is not { Length: > 0 and <= 32 })
                throw new InvalidDataException("Invalid campaign encounter.");
            var room = encounter.Room ?? content.Room;
            if (encounter.Room is not null) ValidateCampaignRoom(room);
            var space = new SpatialWorld(room);
            var navigation = encounter.Room is null ? null : new CombatRoomNavigation(room);
            var positions = new List<Position> { room.PlayerSpawn };
            foreach (var spawn in encounter.Spawns)
            {
                if (spawn is null || !content.Enemies.Any(e => e.Id == spawn.EnemyId) || !space.CanOccupy(spawn.Position, ActorRadius) || positions.Any(p => Position.DistanceSquared(p, spawn.Position) < 4L * ActorRadius * ActorRadius)) throw new InvalidDataException("Invalid campaign spawn.");
                if (navigation is not null && !navigation.TryWaypoint(room.PlayerSpawn, spawn.Position, out _)) throw new InvalidDataException("Campaign spawn cannot be reached from the room entrance.");
                ValidateEliteModifiers(spawn.Modifiers); positions.Add(spawn.Position);
            }
        }
        foreach (var enemy in campaign.Enemies)
            if (enemy is null || !content.Enemies.Contains(enemy)) throw new InvalidDataException("Campaign enemy definitions differ from the composed registry.");
    }
}
