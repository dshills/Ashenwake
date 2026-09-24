using System.Text.Json.Serialization;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Endgame;

public sealed record PetAppearance(string Id, string Name);
public sealed record PetDefinition(string Id, string Species, string DefaultName, string SourceEncounterId, int Act, PetAppearance[] Appearances);
public sealed record PetCompanion([property: JsonRequired] string Id, [property: JsonRequired] string Name, [property: JsonRequired] string AppearanceId);
public sealed record PetState
{
    [JsonRequired]
    public int SchemaVersion { get; init; } = 1;
    [JsonRequired]
    public PetCompanion[] Rescued { get; init; } = [];
    [JsonRequired]
    public string SelectedPetId { get; init; } = "";
    [JsonRequired]
    public bool AutoGather { get; init; }
    [JsonRequired]
    public string[] CollectedMaterials { get; init; } = [];
}
public sealed record PetEntryView(string Id, string Species, string Name, string AppearanceId, PetAppearance[] Appearances,
    bool Rescued, bool Selected, string SourceEncounterId, int Act, string RescueHint);
public sealed record PetsView(bool Enabled, PetEntryView[] Entries, string SelectedPetId, bool AutoGather);

public static class PetCatalog
{
    public const int InteractionRange = 1800;
    public const int MaterialsPerCache = 5;
    private static readonly PetDefinition[] definitions =
    [
        new("pet.ashfox", "Ashen Fox", "Ember", "campaign.road", 1, [new("ash", "Ashen coat"), new("ivory", "Ivory coat")]),
        new("pet.gloammoth", "Gloam Moth", "Morrow", "campaign.living_ruins", 2, [new("moon", "Moonlit wings"), new("dusk", "Dusklit wings")]),
        new("pet.cinderbeetle", "Cinder Beetle", "Clinker", "campaign.cinder_pack", 3, [new("brass", "Brass shell"), new("obsidian", "Obsidian shell")])
    ];
    private static readonly string[] sources = ["campaign.road", "campaign.monastery", "campaign.bell_saint", "campaign.living_ruins", "campaign.plague_village", "campaign.rootheart",
        "campaign.cinder_pack", "campaign.extraction_floor", "campaign.furnace_spindle", "campaign.bone_causeway", "campaign.contract_hall", "campaign.covenant_warden",
        "campaign.repeating_rooms", "campaign.identity_memory", "campaign.breach_heart"];
    public static PetDefinition[] Definitions => definitions.Select(d => d with { Appearances = d.Appearances.ToArray() }).ToArray();
    public static PetDefinition? Find(string? id) => Definitions.FirstOrDefault(d => d.Id == id);
    public static string[] MaterialSources => sources.ToArray();
    public static string MaterialId(string encounterId) => "pet.materials." + encounterId;
    public static string? MaterialSource(string id) => sources.FirstOrDefault(s => MaterialId(s) == id);
    public static bool WithinReach(RoomDefinition room, Position from, Position target) =>
        Position.DistanceSquared(from, target) <= (long)InteractionRange * InteractionRange && new SpatialWorld(room).HasLineOfSight(from, target);
    public static Position RescuePosition(RoomDefinition room) => PositionNearSpawn(room, -1);
    public static Position CachePosition(RoomDefinition room) => PositionNearSpawn(room, 1);
    private static Position PositionNearSpawn(RoomDefinition room, int side)
    {
        var spatial = new SpatialWorld(room);
        foreach (var offset in new Position[] { new(side * 2600, 0), new(side * 1800, 1800), new(0, 2600), new(side * 1200, 0), new(0, 0) })
        {
            var at = new Position(room.PlayerSpawn.X + offset.X, room.PlayerSpawn.Z + offset.Z);
            if (spatial.CanOccupy(at, 400) && spatial.HasLineOfSight(room.PlayerSpawn, at)) return at;
        }
        return room.PlayerSpawn;
    }
    public static bool ValidName(string? name) => name is { Length: >= 1 and <= 24 } && name == name.Trim() &&
        name.All(c => !char.IsControl(c) && !char.IsSurrogate(c) && c is not '<' and not '>' and not '[' and not ']' &&
            char.GetUnicodeCategory(c) is not System.Globalization.UnicodeCategory.Format);
}
