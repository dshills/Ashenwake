using System.Text.Json.Serialization;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Combat;

public sealed record CombatSkill([property: JsonRequired] string Id, [property: JsonRequired] string Name, [property: JsonRequired] string Shape,
    [property: JsonRequired] DamageFamily Family, [property: JsonRequired] int Damage, [property: JsonRequired] int Range,
    [property: JsonRequired] int Windup, [property: JsonRequired] int Recovery, [property: JsonRequired] int Cooldown,
    [property: JsonRequired] int Cost, [property: JsonRequired] int Generate, [property: JsonRequired] int Radius, [property: JsonRequired] string Status, string Discipline = "Vanguard", string Behavior = "", string ResourceMode = "Spend");
public sealed record CombatEnemy([property: JsonRequired] string Id, [property: JsonRequired] string Role, [property: JsonRequired] int Health,
    [property: JsonRequired] int Damage, [property: JsonRequired] int Armor, [property: JsonRequired] int Speed, [property: JsonRequired] int Windup,
    [property: JsonRequired] int Recovery, [property: JsonRequired] int Range);
public sealed record CombatFragment([property: JsonRequired] string Id, [property: JsonRequired] string Name, [property: JsonRequired] AnatomySlot Slot,
    [property: JsonRequired] string Lineage, [property: JsonRequired] int Resonance, [property: JsonRequired] string Trigger,
    [property: JsonRequired] string Effect, [property: JsonRequired] string Description);
public sealed record CombatMutation([property: JsonRequired] string Id, [property: JsonRequired] string SkillId, [property: JsonRequired] string Name,
    [property: JsonRequired] string Shape, [property: JsonRequired] int DamagePercent, [property: JsonRequired] int Radius,
    [property: JsonRequired] int ExtraCost, [property: JsonRequired] string Description);
public sealed record CombatItemDefinition([property: JsonRequired] string Id, [property: JsonRequired] string Name, [property: JsonRequired] string Slot,
    [property: JsonRequired] int Damage, [property: JsonRequired] int Armor, [property: JsonRequired] int CriticalBasisPoints, string[]? CompatibleSlots = null, int Hands = 0, string[]? Disciplines = null);
public sealed record CombatLoadout([property: JsonRequired] string Id, [property: JsonRequired] string Name, [property: JsonRequired] string[] ItemIds, [property: JsonRequired] string[] FragmentIds, [property: JsonRequired] string[] MutationIds, [property: JsonRequired] string Purpose);
public sealed record CombatContent
{
    [JsonRequired] public int SchemaVersion { get; init; }
    [JsonRequired] public string ContentVersion { get; init; } = "";
    [JsonRequired] public RoomDefinition Room { get; init; } = null!;
    [JsonRequired] public CombatSkill[] Skills { get; init; } = [];
    [JsonRequired] public CombatEnemy[] Enemies { get; init; } = [];
    [JsonRequired] public CombatFragment[] Fragments { get; init; } = [];
    [JsonRequired] public CombatMutation[] Mutations { get; init; } = [];
    [JsonRequired] public CombatItemDefinition[] Items { get; init; } = [];
    [JsonRequired] public CombatLoadout[] Loadouts { get; init; } = [];
    public CombatEncounterDefinition[] Encounters { get; init; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CampaignCombatDefinition? Campaign { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public EndgameCombatDefinition? Endgame { get; init; }
    [JsonIgnore] public string Identity => _legacyHash != "" ? _legacyHash : JsonData.Hash(this);
    private string _legacyHash = "";
    public static CombatContent Parse(string json)
    {
        CombatContent content;
        try { content = JsonData.Read<CombatContent>(json); }
        catch (System.Text.Json.JsonException ex) { throw new InvalidDataException("Invalid combat JSON: " + ex.Message, ex); }
        content.Validate();
        var source = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        if (!source["skills"]!.AsArray().Any(s => s!["discipline"] is not null))
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(JsonData.Write(content))!;
            if (source["encounters"] is null) node.AsObject().Remove("encounters");
            // Old bundles predate these optional fields. Remove only defaults that
            // deserialization introduced; an explicitly authored rule still belongs
            // to the identity, even when the bundle omits discipline declarations.
            void RemoveIntroducedDefaults(string collection, string[] fields)
            {
                for (int index = 0; index < node[collection]!.AsArray().Count; index++)
                    foreach (string field in fields)
                        if (!source[collection]![index]!.AsObject().ContainsKey(field)) node[collection]![index]!.AsObject().Remove(field);
            }
            RemoveIntroducedDefaults("skills", ["discipline", "behavior", "resourceMode"]);
            RemoveIntroducedDefaults("items", ["compatibleSlots", "hands", "disciplines"]);
            content._legacyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
        return content;
    }
    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(ContentVersion)) throw new InvalidDataException("Combat content requires schema 1 and a version.");
        if (Room is null || Room.Obstacles is null || Skills is null || Enemies is null || Fragments is null || Mutations is null || Items is null || Loadouts is null ||
            Skills.Any(x => x is null) || Enemies.Any(x => x is null) || Fragments.Any(x => x is null) || Mutations.Any(x => x is null) || Items.Any(x => x is null) || Loadouts.Any(x => x is null))
            throw new InvalidDataException("Combat definitions cannot be null.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Check([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string reason) { if (!valid) throw new InvalidDataException(reason); }
        void Id(string id, string prefix) { Check(!string.IsNullOrEmpty(id) && id.StartsWith(prefix + ".", StringComparison.Ordinal) && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_') && ids.Add(id), "Invalid or duplicated combat ID: " + id); }
        Check(Room.HalfWidth is >= 8000 and <= 100000 && Room.HalfDepth is >= 8000 and <= 100000 && Room.Obstacles.Length <= 100, "Combat room bounds invalid.");
        foreach (var b in Room.Obstacles) Check(b.MinX < b.MaxX && b.MinZ < b.MaxZ && b.MinX >= -Room.HalfWidth && b.MaxX <= Room.HalfWidth && b.MinZ >= -Room.HalfDepth && b.MaxZ <= Room.HalfDepth, "Invalid combat obstacle.");
        Check(new Simulation.SpatialWorld(Room).CanOccupy(Room.PlayerSpawn, CombatSession.ActorRadius), "Player spawn obstructed.");
        Check(Skills.Length >= 6 && Enemies.Length >= 3 && Items.Length >= 1, "Sandbox requires six skills, three enemy roles, and an item.");
        foreach (var s in Skills)
        {
            Id(s.Id, "skill"); Check(!string.IsNullOrWhiteSpace(s.Name) && ValidShape(s.Shape) && Enum.IsDefined(s.Family) && s.Damage is >= 0 and <= 1000 && s.Range is >= 0 and <= 20000 && s.Windup is >= 1 and <= 180 && s.Recovery is >= 1 and <= 180 && s.Cooldown >= s.Windup + s.Recovery && s.Cooldown <= 3000 && s.Cost is >= 0 and <= 100 && s.Generate is >= 0 and <= 100 && s.Radius is >= 0 and <= 10000 && (s.Status == "" || CombatSession.StatusIds.Contains(s.Status)) && (CombatSession.Disciplines.Contains(s.Discipline) || s.Discipline == "Echo") && s.ResourceMode is "Spend" or "Heat" && s.Behavior is "" or "Vent" or "Vanish" or "Companion" or "Procession" or "Leech" or "ConsumeMarked" or "DetonatePoison" or "Regenerate", "Invalid skill: " + s.Id);
        }
        Check(Skills.Where(s => s.Discipline != "Echo").GroupBy(s => s.Discipline).All(g => g.Count() == 6) && Skills.Count(s => s.Discipline == "Vanguard") == 6, "Each authored discipline must define six skills.");
        foreach (var e in Enemies) { Id(e.Id, e.Id.StartsWith("boss.", StringComparison.Ordinal) ? "boss" : "enemy"); Check(e.Role is "Melee" or "Ranged" or "Armored" or "Support" or "Rusher" or "BellSaint" or "Anchor" or "Bell" or "Beast" && e.Health is > 0 and <= 100000 && e.Damage is > 0 and <= 1000 && e.Armor is >= 0 and <= 7500 && e.Speed is > 0 and <= 500 && e.Windup is >= 6 and <= 180 && e.Recovery is >= 6 and <= 300 && e.Range is >= 1000 and <= 20000, "Invalid enemy: " + e.Id); }
        Check(new[] { "Melee", "Ranged", "Armored" }.All(role => Enemies.Any(e => e.Role == role)), "Sandbox requires Melee, Ranged, and Armored enemy roles.");
        Check(new[] { "enemy.ash_ghoul", "enemy.cinder_acolyte", "enemy.furnace_brute", "enemy.cinder_priest", "enemy.emberling", "enemy.bell_saint", "enemy.ritual_anchor", "enemy.bell_beast", "enemy.broken_bell" }.All(id => Enemies.Any(e => e.Id == id)), "Missing required slice encounter enemy definition.");
        Check(Items.Any(i => i.Id != "item.ashcleaver"), "Ordinary loot requires at least one non-Godwrought item.");
        foreach (var f in Fragments) { Id(f.Id, "fragment"); Check(Enum.IsDefined(f.Slot) && !string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.Lineage) && !string.IsNullOrWhiteSpace(f.Description) && f.Resonance is >= 0 and <= 100 && (f.Trigger, f.Effect) is ("CritHit", "Burning") or ("DotDeath", "Spirit") or ("SummonHit", "Poisoned") or ("DamageTaken", "Barrier") or ("CritHit", "Heat") or ("EliteDeath", "CaptureEcho") or ("Stationary", "SeismicCharge") or ("Dodge", "Barrier"), "Unsupported fragment trigger/effect: " + f.Id); }
        foreach (var m in Mutations) { Id(m.Id, "mutation"); Check(Skills.Any(s => s.Id == m.SkillId) && ValidShape(m.Shape) && !string.IsNullOrWhiteSpace(m.Name) && !string.IsNullOrWhiteSpace(m.Description) && m.DamagePercent is >= 1 and <= 500 && m.Radius is >= 0 and <= 10000 && m.ExtraCost is >= 0 and <= 100 && Skills.Single(s => s.Id == m.SkillId).Cost + m.ExtraCost <= 100, "Invalid mutation: " + m.Id); }
        foreach (var item in Items) { Id(item.Id, "item"); Check(!string.IsNullOrWhiteSpace(item.Name) && CombatSession.EquipmentSlots.Contains(item.Slot) && (item.CompatibleSlots is null || item.CompatibleSlots.Length > 0 && item.CompatibleSlots.All(CombatSession.EquipmentSlots.Contains)) && item.Hands is >= 0 and <= 2 && (item.Disciplines is null || item.Disciplines.All(CombatSession.Disciplines.Contains)) && item.Damage is >= 0 and <= 200 && item.Armor is >= 0 and <= 5000 && item.CriticalBasisPoints is >= 0 and <= 5000, "Invalid item: " + item.Id); }
        CombatSession.ValidateAuthoredEncounters(this);
        CombatSession.ValidateCampaignContent(this);
        CombatSession.ValidateEndgameContent(this);
        foreach (var loadout in Loadouts)
        {
            Id(loadout.Id, "loadout");
            Check(!string.IsNullOrWhiteSpace(loadout.Name) && !string.IsNullOrWhiteSpace(loadout.Purpose) && loadout.ItemIds is not null && loadout.FragmentIds is not null && loadout.MutationIds is not null, "Invalid loadout: " + loadout.Id);
            Check(loadout.ItemIds!.Length == 3 && loadout.ItemIds.All(id => Items.Any(i => i.Id == id)) && loadout.FragmentIds!.All(id => Fragments.Any(f => f.Id == id)) && loadout.MutationIds!.All(id => Mutations.Any(m => m.Id == id)), "Unknown loadout reference: " + loadout.Id);
            Check(loadout.ItemIds.Select(id => Items.Single(i => i.Id == id).Slot).Distinct().Count() == 3 && loadout.FragmentIds!.Select(id => Fragments.Single(f => f.Id == id).Slot).Distinct().Count() == loadout.FragmentIds.Length && loadout.MutationIds!.Select(id => Mutations.Single(m => m.Id == id).SkillId).Distinct().Count() == loadout.MutationIds.Length, "Conflicting loadout slots: " + loadout.Id);
        }
    }
    internal static bool ValidShape(string shape) => shape is "Melee" or "Projectile" or "Area" or "Dash" or "Guard" or "Summon" or "Command";
}
