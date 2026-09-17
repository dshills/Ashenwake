using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Content;

public static class JsonData
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidDataException("JSON document is null.");
    public static T Copy<T>(T value) => Read<T>(Write(value));
    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Write(value))));
}

public sealed record AbilityDefinition([property: JsonRequired] string Id, [property: JsonRequired] string NameKey, [property: JsonRequired] int Damage, [property: JsonRequired] int Range, [property: JsonRequired] int WindupTicks, [property: JsonRequired] int CooldownTicks);
public sealed record EnemyDefinition([property: JsonRequired] string Id, [property: JsonRequired] string NameKey, [property: JsonRequired] int Health, [property: JsonRequired] string LootTableId);
public sealed record ItemDefinition([property: JsonRequired] string Id, [property: JsonRequired] string NameKey, [property: JsonRequired] int MinDamage, [property: JsonRequired] int MaxDamage);
public sealed record FragmentDefinition([property: JsonRequired] string Id, [property: JsonRequired] string NameKey, [property: JsonRequired] string Slot, [property: JsonRequired] string Trigger, [property: JsonRequired] string StatusId);
public sealed record StatusDefinition([property: JsonRequired] string Id, [property: JsonRequired] string NameKey, [property: JsonRequired] string Effect, [property: JsonRequired] int Damage, [property: JsonRequired] int DurationTicks, [property: JsonRequired] int IntervalTicks);
public sealed record LootEntry([property: JsonRequired] string ItemId, [property: JsonRequired] int Weight);
public sealed record LootTableDefinition([property: JsonRequired] string Id, [property: JsonRequired] LootEntry[] Entries);
public sealed record RoomDefinition([property: JsonRequired] int HalfWidth, [property: JsonRequired] int HalfDepth, [property: JsonRequired] Position PlayerSpawn, [property: JsonRequired] Position EnemySpawn, [property: JsonRequired] Bounds[] Obstacles);

public sealed record ContentDocument
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    [JsonRequired]
    public string ContentVersion { get; init; } = "0.1.0";
    [JsonRequired]
    public SortedDictionary<string, string> Strings { get; init; } = [];
    [JsonRequired]
    public AbilityDefinition[] Abilities { get; init; } = [];
    [JsonRequired]
    public EnemyDefinition[] Enemies { get; init; } = [];
    [JsonRequired]
    public ItemDefinition[] Items { get; init; } = [];
    [JsonRequired]
    public FragmentDefinition[] Fragments { get; init; } = [];
    [JsonRequired]
    public StatusDefinition[] Statuses { get; init; } = [];
    [JsonRequired]
    public LootTableDefinition[] LootTables { get; init; } = [];
    [JsonRequired]
    public RoomDefinition Room { get; init; } = new(8000, 6000, new(-2000, 0), new(0, 0), []);
    [JsonRequired]
    public string StartingAbility { get; init; } = "skill.ember_strike";
    [JsonRequired]
    public string StartingEnemy { get; init; } = "enemy.ash_ghoul";
    [JsonRequired]
    public string StartingItem { get; init; } = "item.ash_iron";
    [JsonRequired]
    public string StartingFragment { get; init; } = "fragment.vael.ember";
}

public sealed record ContentBundle(int BundleVersion, string Hash, ContentDocument Content);

public static class ContentCompiler
{
    public static ContentBundle Compile(string source)
    {
        var document = JsonData.Read<ContentDocument>(source);
        Validate(document);
        // Authoring order is preserved and forms part of the immutable content contract.
        return new(1, JsonData.Hash(document), document);
    }

    public static ContentBundle LoadBundle(string json)
    {
        var bundle = JsonData.Read<ContentBundle>(json);
        if (bundle.BundleVersion != 1) throw new InvalidDataException("Unsupported content bundle version.");
        Validate(bundle.Content);
        if (bundle.Hash != JsonData.Hash(bundle.Content)) throw new InvalidDataException("Content bundle hash mismatch.");
        return bundle;
    }

    public static void Validate(ContentDocument doc)
    {
        var errors = new List<string>();
        if (doc is null) throw new InvalidDataException("Content is null.");
        if (doc.SchemaVersion != 1) errors.Add("schemaVersion must be 1.");
        if (string.IsNullOrWhiteSpace(doc.ContentVersion)) errors.Add("contentVersion is required.");
        if (doc.Strings is null || doc.Abilities is null || doc.Enemies is null || doc.Items is null ||
            doc.Fragments is null || doc.Statuses is null || doc.LootTables is null || doc.Room is null)
            throw new InvalidDataException("Content collections and room must not be null.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id, string prefix, string? key = null)
        {
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith(prefix + ".", StringComparison.Ordinal) ||
                id.Any(c => !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_')))
                errors.Add($"Invalid {prefix} ID '{id}'.");
            else if (!ids.Add(id)) errors.Add($"Duplicate ID '{id}'.");
            if (key is not null && (!doc.Strings.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
                errors.Add($"{id}: missing localization key '{key}'.");
            if (key is null && prefix != "loot") errors.Add($"{id}: nameKey is required.");
        }
        void Check(bool valid, string error) { if (!valid) errors.Add(error); }
        // Reject null entries before dereferencing any definitions or references.
        if (doc.Abilities.Any(x => x is null) || doc.Enemies.Any(x => x is null) || doc.Items.Any(x => x is null) ||
            doc.Fragments.Any(x => x is null) || doc.Statuses.Any(x => x is null) || doc.LootTables.Any(x => x is null))
            throw new InvalidDataException("Content definition arrays must not contain null.");
        foreach (var a in doc.Abilities)
        {
            Id(a.Id, "skill", a.NameKey);
            Check(a.Damage is > 0 and <= 10000 && a.Range is > 0 and <= 20000 && a.WindupTicks is >= 1 and <= 300 &&
                a.CooldownTicks >= a.WindupTicks && a.CooldownTicks <= 3000, $"{a.Id}: invalid damage, range, windup, or cooldown.");
        }
        foreach (var e in doc.Enemies)
        {
            Id(e.Id, "enemy", e.NameKey);
            Check(e.Health is > 0 and <= 1000000, $"{e.Id}: health out of range.");
            Check(doc.LootTables.Any(t => t.Id == e.LootTableId), $"{e.Id}: unknown loot table '{e.LootTableId}'.");
        }
        foreach (var i in doc.Items)
        {
            Id(i.Id, "item", i.NameKey);
            Check(i.MinDamage >= 0 && i.MaxDamage >= i.MinDamage && i.MaxDamage <= 10000, $"{i.Id}: invalid damage range.");
        }
        foreach (var f in doc.Fragments)
        {
            Id(f.Id, "fragment", f.NameKey);
            Check(f.Slot == "Arms" && f.Trigger == "OnHit", $"{f.Id}: Phase 0 supports only Arms/OnHit fragments.");
            Check(doc.Statuses.Any(s => s.Id == f.StatusId), $"{f.Id}: unknown status '{f.StatusId}'.");
        }
        foreach (var s in doc.Statuses)
        {
            Id(s.Id, "effect", s.NameKey);
            Check(s.Effect == "ApplyDamage", $"{s.Id}: unknown effect '{s.Effect}'.");
            Check(s.Damage is > 0 and <= 10000 && s.IntervalTicks is >= 1 and <= 300 && s.DurationTicks >= s.IntervalTicks &&
                s.DurationTicks <= 3000, $"{s.Id}: invalid status damage/duration/interval.");
        }
        foreach (var t in doc.LootTables)
        {
            Id(t.Id, "loot");
            if (t.Entries is null || t.Entries.Length == 0 || t.Entries.Any(e => e is null))
            { errors.Add($"{t.Id}: loot entries must be nonempty and nonnull."); continue; }
            foreach (var e in t.Entries)
            {
                Check(e.Weight is > 0 and <= 1000000, $"{t.Id}: loot weight must be 1..1000000.");
                Check(doc.Items.Any(i => i.Id == e.ItemId), $"{t.Id}: unknown item '{e.ItemId}'.");
            }
            Check(t.Entries.Sum(e => (long)e.Weight) <= int.MaxValue, $"{t.Id}: total loot weight exceeds supported range.");
        }
        Check(doc.Abilities.Any(a => a.Id == doc.StartingAbility), "Unknown startingAbility.");
        Check(doc.Enemies.Any(e => e.Id == doc.StartingEnemy), "Unknown startingEnemy.");
        Check(doc.Items.Any(i => i.Id == doc.StartingItem), "Unknown startingItem.");
        Check(doc.Fragments.Any(f => f.Id == doc.StartingFragment), "Unknown startingFragment.");
        Check(doc.Room.HalfWidth is >= 3000 and <= 100000 && doc.Room.HalfDepth is >= 3000 and <= 100000,
            "Room dimensions must be 3000..100000 millimeters.");
        if (doc.Room.Obstacles is null) errors.Add("Room obstacles must not be null.");
        else
        {
            foreach (var b in doc.Room.Obstacles)
                Check(b.MinX < b.MaxX && b.MinZ < b.MaxZ && b.MinX >= -doc.Room.HalfWidth && b.MaxX <= doc.Room.HalfWidth &&
                    b.MinZ >= -doc.Room.HalfDepth && b.MaxZ <= doc.Room.HalfDepth, "Invalid room obstacle bounds.");
            var spatial = new SpatialWorld(doc.Room);
            Check(spatial.CanOccupy(doc.Room.PlayerSpawn, SimulationWorld.ActorRadius), "Player spawn is obstructed.");
            Check(spatial.CanOccupy(doc.Room.EnemySpawn, SimulationWorld.ActorRadius), "Enemy spawn is obstructed.");
            if (spatial.CanOccupy(doc.Room.PlayerSpawn, SimulationWorld.ActorRadius) && spatial.CanOccupy(doc.Room.EnemySpawn, SimulationWorld.ActorRadius))
                Check(Position.DistanceSquared(doc.Room.PlayerSpawn, doc.Room.EnemySpawn) >= 4L * SimulationWorld.ActorRadius * SimulationWorld.ActorRadius,
                    "Player and enemy spawns overlap.");
        }
        if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }
}
