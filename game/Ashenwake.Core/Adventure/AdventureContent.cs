using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Adventure;

public sealed record AdventureRoom([property: JsonRequired] string Id, [property: JsonRequired] string Name,
    [property: JsonRequired] string[] Exits, [property: JsonRequired] string[] Encounters,
    [property: JsonRequired] bool Anchor, [property: JsonRequired] string Discovery);
public sealed record AnatomyFragment([property: JsonRequired] string Id, [property: JsonRequired] string Slot,
    [property: JsonRequired] string[] Tags, [property: JsonRequired] int Resonance);
public sealed record ManifestationDefinition([property: JsonRequired] string Id, [property: JsonRequired] int Threshold,
    [property: JsonRequired] string Benefit, [property: JsonRequired] string Complication);
public sealed record ConcordanceDefinition([property: JsonRequired] string Id, [property: JsonRequired] string[] Tags);
public sealed record AdventureDefinition
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    [JsonRequired] public string Version { get; init; } = "slice.1";
    [JsonRequired] public string Hub { get; init; } = "room.greyhaven";
    [JsonRequired] public string BossRoom { get; init; } = "room.bell_sanctum";
    [JsonRequired] public string RewardFragment { get; init; } = "fragment.heart_serath";
    [JsonRequired] public int AwakeningKills { get; init; } = 1000;
    [JsonRequired] public int RelicChancePercent { get; init; } = 20;
    [JsonRequired] public AdventureRoom[] Rooms { get; init; } = [];
    [JsonRequired] public AnatomyFragment[] Fragments { get; init; } = [];
    [JsonRequired] public ManifestationDefinition[] Manifestations { get; init; } = [];
    [JsonRequired] public ConcordanceDefinition[] Concordances { get; init; } = [];
}

/// <summary>Owns a private content snapshot. Editing a source definition never changes a running session.</summary>
public sealed class AdventureContent
{
    private readonly AdventureDefinition definition;
    public string Hash { get; }
    public AdventureDefinition Capture() => JsonData.Copy(definition);
    internal AdventureDefinition Data => definition;
    private AdventureContent(AdventureDefinition value) { Validate(value); definition = JsonData.Copy(value); Hash = JsonData.Hash(definition); }
    public static AdventureContent Parse(string json) => new(JsonData.Read<AdventureDefinition>(json));
    public static AdventureContent Create(AdventureDefinition definition) => new(definition);
    public static AdventureContent Default() => new(new()
    {
        Rooms =
        [
            new("room.greyhaven", "Greyhaven", ["room.ossuary"], [], true, "discovery.greyhaven"),
            new("room.ossuary", "The Ossuary", ["room.greyhaven", "room.cloister"], ["encounter.ossuary"], false, "discovery.false_history"),
            new("room.cloister", "Funeral Cloister", ["room.ossuary", "room.bell_sanctum", "room.greyhaven"], ["encounter.cloister"], true, "discovery.ritual"),
            new("room.bell_sanctum", "Sanctum of the Bell Saint", ["room.cloister", "room.greyhaven"], ["bell_saint.1", "bell_saint.2", "bell_saint.3"], false, "discovery.bell_saint")
        ],
        Fragments =
        [
            new("fragment.eye_vael", "Eyes", ["Flame"], 12),
            new("fragment.heart_serath", "Heart", ["Death", "Memory"], 18),
            new("fragment.nerve_ilyra", "Spine", ["Beast", "Hunger"], 14),
            new("fragment.orrun_bone", "Arms", ["Oath"], 20)
        ],
        Manifestations =
        [
            new("manifestation.burning_blood", 40, "Taking physical damage releases nearby fire.", "Healing potions restore 25% less health."),
            new("manifestation.stone_memory", 40, "Repeated identical attacks grant resistance up to 30%.", "Active resistance slows dodge recovery by 25%."),
            new("manifestation.whispering_shadow", 60, "Hidden enemies are revealed.", "Harmless false silhouettes appear away from real telegraphs."),
            new("manifestation.voracious_renewal", 60, "Consuming a corpse heals and grants temporary maximum life.", "Ordinary healing is 25% less effective near corpses.")
        ],
        Concordances = [new("concordance.funeral_flame", ["Flame", "Death"])]
    });

    public static void Validate(AdventureDefinition d)
    {
        static void Require([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Require(d is not null, "Adventure definition is null.");
        Require(d!.SchemaVersion == 1 && !string.IsNullOrWhiteSpace(d.Version), "Unsupported adventure content version.");
        Require(d.Rooms is not null && d.Fragments is not null && d.Manifestations is not null && d.Concordances is not null, "Adventure arrays are required.");
        Require(d.Rooms!.Length is > 0 and <= 128 && d.Fragments!.Length <= 128 && d.Manifestations!.Length <= 32 && d.Concordances!.Length <= 64, "Adventure content bounds exceeded.");
        Require(d.AwakeningKills == 1000 && d.RelicChancePercent is >= 0 and <= 100, "Canonical awakening requires 1000 kills; reward chance must be 0..100.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Id(string id) => Require(!string.IsNullOrWhiteSpace(id) && id.Length <= 100 && ids.Add(id), $"Invalid or duplicate adventure ID: {id}.");
        foreach (var room in d.Rooms)
        {
            Require(room is not null, "Null adventure room."); Id(room!.Id);
            Require(!string.IsNullOrWhiteSpace(room.Name) && !string.IsNullOrWhiteSpace(room.Discovery), "Room name/discovery required.");
            Require(room.Exits is not null && room.Encounters is not null, "Room exits/encounters required.");
            Require(room.Exits!.Distinct().Count() == room.Exits.Length && room.Encounters!.Distinct().Count() == room.Encounters.Length, "Duplicate room references.");
            foreach (var encounter in room.Encounters) Id(encounter);
        }
        Require(d.Rooms.Any(r => r.Id == d.Hub && r.Anchor && r.Encounters.Length == 0), "Hub must be an anchor without combat.");
        Require(d.Rooms.Any(r => r.Id == d.BossRoom && r.Encounters.SequenceEqual(new[] { "bell_saint.1", "bell_saint.2", "bell_saint.3" })), "Bell Saint requires three ordered phases.");
        foreach (var room in d.Rooms)
            Require(room.Exits.All(id => id != room.Id && d.Rooms.Any(r => r.Id == id)), "Unknown/self room exit.");
        var reachable = new HashSet<string> { d.Hub };
        for (int i = 0; i < d.Rooms.Length; i++)
            foreach (var room in d.Rooms.Where(r => reachable.Contains(r.Id))) foreach (var next in room.Exits) reachable.Add(next);
        Require(reachable.Count == d.Rooms.Length, "Unreachable adventure room.");
        var returning = new HashSet<string> { d.Hub };
        for (int i = 0; i < d.Rooms.Length; i++)
            foreach (var room in d.Rooms.Where(r => r.Exits.Any(returning.Contains))) returning.Add(room.Id);
        Require(returning.Count == d.Rooms.Length, "An adventure room has no route back to the hub.");
        foreach (var fragment in d.Fragments!)
        {
            Require(fragment is not null, "Null fragment."); Id(fragment!.Id);
            Require(new[] { "Mind", "Eyes", "Heart", "Spine", "Arms", "Legs" }.Contains(fragment.Slot), "Unknown anatomy slot.");
            Require(fragment.Tags is not null && fragment.Tags.Length > 0 && fragment.Tags.All(t => !string.IsNullOrWhiteSpace(t)) && fragment.Tags.Distinct().Count() == fragment.Tags.Length && fragment.Resonance is >= 0 and <= 100, "Invalid fragment tags/resonance.");
        }
        Require(d.Fragments.Any(f => f.Id == d.RewardFragment), "Missing boss reward fragment.");
        foreach (var manifestation in d.Manifestations!)
        {
            Require(manifestation is not null, "Null manifestation."); Id(manifestation!.Id);
            Require(manifestation.Threshold is > 0 and <= 600 && !string.IsNullOrWhiteSpace(manifestation.Benefit) && !string.IsNullOrWhiteSpace(manifestation.Complication), "Manifestations require threshold, benefit, and complication.");
        }
        foreach (var concordance in d.Concordances!)
        {
            Require(concordance is not null, "Null concordance."); Id(concordance!.Id);
            Require(concordance.Tags is not null && concordance.Tags.Length >= 2 && concordance.Tags.Distinct().Count() == concordance.Tags.Length && concordance.Tags.All(t => d.Fragments.Any(f => f.Tags.Contains(t))), "Unknown/duplicate concordance tags.");
        }
    }
}
