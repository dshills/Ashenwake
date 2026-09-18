using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Recognizable, cosmetic ground items. Position, visibility and pickup remain authoritative.</summary>
public partial class LootVisual : Node3D
{
    public const int MaximumModelTemplates = 19;
    private enum ItemStyle { Axe, Ashcleaver, Saber, Hammer, Pike, Greatstaff, Shield, Focus, Robe, Plate, Helmet, Shoulders, Gloves, Belt, Legs, Boots, Ring, Amulet, Parcel }
    private enum Surface { Metal, Dark, Leather, Cloth, Bone, Ember, Rarity }
    private sealed record ModelPart(Mesh Mesh, Surface Surface);
    private static readonly Dictionary<ItemStyle, ModelPart[]> Models = [];
    private static readonly StandardMaterial3D[] Materials =
    [
        Material("a9bbc0", metallic: true), Material("35414a", metallic: true), Material("75513c"),
        Material("687d91"), Material("ddd1ad"), Material("ff7148", emissive: true)
    ];
    private static readonly string[] RarityColors = ["bdc6c8", "7dcbae", "6cafff", "b98bf3", "ffd172", "ff8559"];
    private static readonly StandardMaterial3D[] RarityMaterials = RarityColors.Select(c => Material(c, emissive: true)).ToArray();
    private readonly Node3D _model = new() { Name = "LootModel" };
    private Node3D _selection = null!;
    private double _time;
    private float _restYaw;
    public string AppearanceKey { get; private set; } = "";
    public string Category { get; private set; } = "";
    public int RarityTier { get; private set; }
    internal static (int Models, int Materials) CachedResourceCounts => (Models.Count, Materials.Length + RarityMaterials.Length);

    public static string KeyFor(CombatItem item) => $"{item.DefinitionId}/{item.Slot}/{item.Rarity}";
    public static Color RarityColor(string rarity) => new(RarityColors[RarityIndex(rarity)]);
    public static string RarityGlyph(string rarity) => RarityIndex(rarity) switch
    { 1 => "II", 2 => "III", 3 => "IV", 4 => "V", 5 => "VI", _ => "I" };

    public static LootVisual Create(CombatItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var style = Classify(item);
        var visual = new LootVisual
        {
            Name = "LootVisual",
            AppearanceKey = KeyFor(item),
            Category = style.ToString(),
            RarityTier = RarityIndex(item.Rarity) + 1
        };
        visual.AddChild(visual._model);
        visual._restYaw = style is ItemStyle.Robe or ItemStyle.Plate or ItemStyle.Helmet ? -.24f : -.52f;
        if (!Models.TryGetValue(style, out var parts)) { parts = BuildModel(style); Models.Add(style, parts); }
        foreach (var part in parts)
            visual._model.AddChild(new MeshInstance3D
            {
                Mesh = part.Mesh,
                MaterialOverride = part.Surface == Surface.Rarity ? RarityMaterials[visual.RarityTier - 1] : Materials[(int)part.Surface]
            });
        visual.BuildRarityMarkers();
        visual.RestPose();
        return visual;
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        if (paused) return;
        if (reducedEffects) { RestPose(); return; }
        _time += Math.Clamp(delta, 0, .1);
        _model.Position = new(0, .10f + MathF.Sin((float)_time * 1.7f) * .018f, 0);
        _model.Rotation = new(0, _restYaw + MathF.Sin((float)_time * .65f) * .10f, 0);
    }

    public void SetHighlighted(bool selected) => _selection.Visible = selected;

    private void RestPose()
    { _model.Position = new(0, .10f, 0); _model.Rotation = new(0, _restYaw, 0); }

    private void BuildRarityMarkers()
    {
        var markers = new Node3D { Name = "LootRarity" }; AddChild(markers);
        // Hollow rings leave the floor and combat warnings visible. Counted pips encode rarity without color.
        var rarity = RarityMaterials[RarityTier - 1];
        RarityMarkerMeshes[RarityTier - 1] ??= BuildMarkerMesh(RarityTier);
        Add(markers, RarityMarkerMeshes[RarityTier - 1]!, Vector3.Zero, Vector3.One, rarity);
        _selection = new Node3D { Name = "LootSelection", Visible = false }; AddChild(_selection);
        // This material belongs only to this drop; selection can never tint another item.
        var selected = Material("fff2ce", emissive: true);
        SelectionMesh ??= BuildMarkerMesh(0);
        Add(_selection, SelectionMesh, Vector3.Zero, Vector3.One, selected);
    }

    private static MeshInstance3D Add(Node parent, Mesh mesh, Vector3 at, Vector3 scale, Material material)
    {
        var part = new MeshInstance3D { Mesh = mesh, Position = at, Scale = scale, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(part); return part;
    }

    private static StandardMaterial3D Material(string color, bool metallic = false, bool emissive = false) => new()
    {
        AlbedoColor = new(color),
        Roughness = metallic ? .52f : .82f,
        Metallic = metallic ? .5f : 0,
        EmissionEnabled = emissive,
        Emission = new(color),
        EmissionEnergyMultiplier = emissive ? .55f : 0
    };

    private static int RarityIndex(string rarity) => rarity switch
    { "Tempered" => 1, "Rare" => 2, "Relic" => 3, "Legendary" => 4, "Godwrought" => 5, _ => 0 };

    private static ItemStyle Classify(CombatItem item)
    {
        string id = item.DefinitionId.ToLowerInvariant();
        // Named art variants are explicit. Unmapped content receives its slot's generic model.
        ItemStyle? authored = (item.Slot, id) switch
        {
            ("MainHand", "item.ashcleaver") => ItemStyle.Ashcleaver,
            ("MainHand", "item.ash_axe") => ItemStyle.Axe,
            ("MainHand", "item.oath_hammer") => ItemStyle.Hammer,
            ("MainHand", "item.pilgrim_pike") => ItemStyle.Pike,
            ("MainHand", "item.greatstaff") => ItemStyle.Greatstaff,
            ("OffHand", "item.focus" or "item.tome") => ItemStyle.Focus,
            ("Chest", "item.ash_weave" or "item.serath_shroud") => ItemStyle.Robe,
            _ => null
        };
        if (authored.HasValue) return authored.Value;
        return item.Slot switch
        {
            "MainHand" => ItemStyle.Saber,
            "OffHand" => ItemStyle.Shield,
            "Chest" => ItemStyle.Plate,
            "Head" => ItemStyle.Helmet,
            "Shoulders" => ItemStyle.Shoulders,
            "Gloves" => ItemStyle.Gloves,
            "Belt" => ItemStyle.Belt,
            "Legs" => ItemStyle.Legs,
            "Boots" => ItemStyle.Boots,
            "Ring1" or "Ring2" => ItemStyle.Ring,
            "Amulet" => ItemStyle.Amulet,
            _ => ItemStyle.Parcel
        };
    }
}
