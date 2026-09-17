using System.Text.Json.Serialization;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Combat;

public sealed record CombatSkill([property: JsonRequired] string Id, [property: JsonRequired] string Name, [property: JsonRequired] string Shape,
    [property: JsonRequired] DamageFamily Family, [property: JsonRequired] int Damage, [property: JsonRequired] int Range,
    [property: JsonRequired] int Windup, [property: JsonRequired] int Recovery, [property: JsonRequired] int Cooldown,
    [property: JsonRequired] int Cost, [property: JsonRequired] int Generate, [property: JsonRequired] int Radius, [property: JsonRequired] string Status);
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
    [property: JsonRequired] int Damage, [property: JsonRequired] int Armor, [property: JsonRequired] int CriticalBasisPoints);
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
    public static CombatContent Parse(string json)
    {
        CombatContent content;
        try { content = JsonData.Read<CombatContent>(json); }
        catch (System.Text.Json.JsonException ex) { throw new InvalidDataException("Invalid combat JSON: " + ex.Message, ex); }
        content.Validate(); return content;
    }
    public void Validate()
    {
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(ContentVersion)) throw new InvalidDataException("Combat content requires schema 1 and a version.");
        if (Room is null || Room.Obstacles is null || Skills is null || Enemies is null || Fragments is null || Mutations is null || Items is null ||
            Skills.Any(x => x is null) || Enemies.Any(x => x is null) || Fragments.Any(x => x is null) || Mutations.Any(x => x is null) || Items.Any(x => x is null))
            throw new InvalidDataException("Combat definitions cannot be null.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Check(bool valid, string reason) { if (!valid) throw new InvalidDataException(reason); }
        void Id(string id, string prefix) { Check(!string.IsNullOrEmpty(id) && id.StartsWith(prefix + ".", StringComparison.Ordinal) && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_') && ids.Add(id), "Invalid or duplicated combat ID: " + id); }
        Check(Room.HalfWidth is >= 8000 and <= 100000 && Room.HalfDepth is >= 8000 and <= 100000 && Room.Obstacles.Length <= 100, "Combat room bounds invalid.");
        foreach (var b in Room.Obstacles) Check(b.MinX < b.MaxX && b.MinZ < b.MaxZ && b.MinX >= -Room.HalfWidth && b.MaxX <= Room.HalfWidth && b.MinZ >= -Room.HalfDepth && b.MaxZ <= Room.HalfDepth, "Invalid combat obstacle.");
        Check(new Simulation.SpatialWorld(Room).CanOccupy(Room.PlayerSpawn, CombatSession.ActorRadius), "Player spawn obstructed.");
        Check(Skills.Length == 6 && Enemies.Length >= 3 && Items.Length >= 1, "Sandbox requires six skills, three enemy roles, and an item.");
        foreach (var s in Skills)
        {
            Id(s.Id, "skill"); Check(!string.IsNullOrWhiteSpace(s.Name) && ValidShape(s.Shape) && Enum.IsDefined(s.Family) && s.Damage is >= 0 and <= 1000 && s.Range is >= 0 and <= 20000 && s.Windup is >= 1 and <= 180 && s.Recovery is >= 1 and <= 180 && s.Cooldown >= s.Windup + s.Recovery && s.Cooldown <= 3000 && s.Cost is >= 0 and <= 100 && s.Generate is >= 0 and <= 100 && s.Radius is >= 0 and <= 10000 && s.Status is "" or "Burning" or "Poisoned" or "Staggered" or "Vulnerable", "Invalid skill: " + s.Id);
        }
        foreach (var e in Enemies) { Id(e.Id, "enemy"); Check(e.Role is "Melee" or "Ranged" or "Armored" && e.Health is > 0 and <= 100000 && e.Damage is > 0 and <= 1000 && e.Armor is >= 0 and <= 7500 && e.Speed is > 0 and <= 500 && e.Windup is >= 6 and <= 180 && e.Recovery is >= 6 and <= 300 && e.Range is >= 1000 and <= 20000, "Invalid enemy: " + e.Id); }
        Check(new[] { "Melee", "Ranged", "Armored" }.All(role => Enemies.Any(e => e.Role == role)), "Sandbox requires Melee, Ranged, and Armored enemy roles.");
        foreach (var f in Fragments) { Id(f.Id, "fragment"); Check(Enum.IsDefined(f.Slot) && !string.IsNullOrWhiteSpace(f.Name) && !string.IsNullOrWhiteSpace(f.Lineage) && !string.IsNullOrWhiteSpace(f.Description) && f.Resonance is >= 0 and <= 100 && (f.Trigger, f.Effect) is ("CritHit", "Burning") or ("DotDeath", "Spirit") or ("SummonHit", "Poisoned"), "Unsupported fragment trigger/effect: " + f.Id); }
        foreach (var m in Mutations) { Id(m.Id, "mutation"); Check(Skills.Any(s => s.Id == m.SkillId) && ValidShape(m.Shape) && !string.IsNullOrWhiteSpace(m.Name) && !string.IsNullOrWhiteSpace(m.Description) && m.DamagePercent is >= 1 and <= 500 && m.Radius is >= 0 and <= 10000 && m.ExtraCost is >= 0 and <= 100 && Skills.Single(s => s.Id == m.SkillId).Cost + m.ExtraCost <= 100, "Invalid mutation: " + m.Id); }
        foreach (var item in Items) { Id(item.Id, "item"); Check(!string.IsNullOrWhiteSpace(item.Name) && item.Slot is "MainHand" or "Chest" or "Amulet" && item.Damage is >= 0 and <= 200 && item.Armor is >= 0 and <= 5000 && item.CriticalBasisPoints is >= 0 and <= 5000, "Invalid item: " + item.Id); }
    }
    internal static bool ValidShape(string shape) => shape is "Melee" or "Projectile" or "Area" or "Dash" or "Guard";
}
