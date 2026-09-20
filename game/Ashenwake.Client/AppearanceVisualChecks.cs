using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Scene-independent checks for cosmetic identity, isolation and bounded appearance resources.</summary>
public static partial class AppearanceVisualChecks
{
    private static readonly string[] Disciplines = ["Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden"];
    private static readonly string[] Slots = ["MainHand", "OffHand", "Head", "Chest", "Shoulders", "Gloves", "Belt", "Legs", "Boots"];
    private static readonly string[] Rarities = ["Common", "Tempered", "Rare", "Relic", "Legendary", "Godwrought"];
    private static readonly (int Bit, string Name)[] Manifestations =
    [(1, "ManifestationBurning"), (2, "ManifestationShadow"), (4, "ManifestationStone"), (8, "ManifestationRenewal")];
    private static readonly (int Bit, string Name)[] Anatomy =
    [(1, "AnatomyEyes"), (2, "AnatomyHeart"), (4, "AnatomySpine"), (8, "AnatomyArmsLeft"), (8, "AnatomyArmsRight")];

    public static void Run(Action<bool, string> require)
    {
        CheckEquipment(require);
        CheckManifestations(require);
        CheckAnatomy(require);
        CheckArmor(require);
        CheckLoot(require);
        require(CharacterVisual.CachedEquipmentResourceCount <= 160 && CharacterVisual.CachedResourceCounts.Models <= 96 &&
            CharacterVisual.CachedResourceCounts.Materials <= 256, "appearance_character_caches_are_bounded");
        require(LootVisual.CachedResourceCounts.Models <= LootVisual.MaximumModelTemplates && LootVisual.CachedResourceCounts.Materials == 12,
            "appearance_loot_caches_are_bounded");
    }

    private static CharacterAppearance Equipped(string discipline = "Vanguard", int mask = 0) => new(discipline,
        new("item.starter_mainhand"), new("item.starter_offhand"), new("item.starter_head"), new("item.starter_chest"), mask)
    { Shoulders = new("item.starter_shoulders"), Gloves = new("item.starter_gloves"), Belt = new("item.starter_belt"), Legs = new("item.starter_legs"), Boots = new("item.starter_boots") };
    private static CharacterVisual Hero(CharacterAppearance appearance)
        => CharacterVisual.Create("player." + appearance.Discipline.ToLowerInvariant(), "", appearance.Discipline, appearance: appearance);

    private static void CheckEquipment(Action<bool, string> require)
    {
        foreach (string discipline in Disciplines)
        {
            var appearance = Equipped(discipline);
            var dressed = Hero(appearance);
            var bare = Hero(new(discipline, ItemAppearance.Empty, ItemAppearance.Empty, ItemAppearance.Empty, ItemAppearance.Empty));
            try
            {
                require(dressed.AppearanceKey == appearance.Key && Slots.All(s => HasGeometry(Module(dressed, "Equipment" + s))),
                    "appearance_" + discipline + "_equipped_slots_have_identity_and_geometry");
                require(bare.AppearanceKey != dressed.AppearanceKey && Slots.All(s => Module(bare, "Equipment" + s) is null) && HasGeometry(bare),
                    "appearance_" + discipline + "_empty_slots_keep_character_without_gear");
                require(CosmeticOnly(dressed) && CosmeticOnly(bare), "appearance_" + discipline + "_has_no_gameplay_bodies");
                foreach (string slot in Slots)
                {
                    var changed = slot switch
                    {
                        "MainHand" => appearance with { MainHand = ItemAppearance.Empty },
                        "OffHand" => appearance with { OffHand = ItemAppearance.Empty },
                        "Head" => appearance with { Head = ItemAppearance.Empty },
                        "Chest" => appearance with { Chest = ItemAppearance.Empty },
                        "Shoulders" => appearance with { Shoulders = ItemAppearance.Empty },
                        "Gloves" => appearance with { Gloves = ItemAppearance.Empty },
                        "Belt" => appearance with { Belt = ItemAppearance.Empty },
                        "Legs" => appearance with { Legs = ItemAppearance.Empty },
                        _ => appearance with { Boots = ItemAppearance.Empty }
                    };
                    var unequipped = Hero(changed);
                    try
                    {
                        require(unequipped.AppearanceKey == changed.Key && changed.Key != appearance.Key && Module(unequipped, "Equipment" + slot) is null &&
                            Module(unequipped, "Equipment" + slot + "Right") is null &&
                            Slots.Where(s => s != slot).All(s => HasGeometry(Module(unequipped, "Equipment" + s))),
                            "appearance_" + discipline + "_unequip_" + slot + "_affects_only_its_slot");
                    }
                    finally { unequipped.Free(); }
                }
            }
            finally { dressed.Free(); bare.Free(); }
        }
        var weapons = new HashSet<ulong>();
        foreach (string id in new[] { "starter_mainhand", "ash_axe", "ashcleaver", "cinder_edge", "oath_hammer", "pilgrim_pike", "greatstaff" })
        {
            var appearance = Equipped() with { MainHand = new("item." + id, id == "ashcleaver" ? "Godwrought" : "Common") };
            var visual = Hero(appearance);
            try
            {
                var weapon = Module(visual, "EquipmentMainHand");
                require(visual.AppearanceKey == appearance.Key && HasGeometry(weapon) && weapons.Add(Geometry(weapon!)),
                    "appearance_weapon_" + id + "_has_distinct_geometry");
            }
            finally { visual.Free(); }
        }
        var chests = new HashSet<ulong>();
        foreach (string id in new[] { "starter_chest", "march_plate", "oath_plate", "ash_weave", "serath_shroud" })
        {
            var visual = Hero(Equipped() with { Chest = new("item." + id) });
            try
            {
                var chest = Module(visual, "EquipmentChest");
                // Materials are part of the authored distinction between traveler plate and March Plate.
                require(HasGeometry(chest) && chests.Add(Geometry(chest!, includeMaterials: true)), "appearance_chest_" + id + "_is_distinct");
            }
            finally { visual.Free(); }
        }
        var evolutions = new HashSet<ulong>();
        foreach (string evolution in new[] { "", "Awakened", "Serath", "Orrun" })
        {
            var appearance = Equipped() with { MainHand = new("item.ashcleaver", "Godwrought", evolution) };
            var visual = Hero(appearance);
            try
            {
                require(visual.AppearanceKey == appearance.Key && evolutions.Add(Geometry(Module(visual, "EquipmentMainHand")!)),
                    "appearance_ashcleaver_" + (evolution.Length == 0 ? "dormant" : evolution) + "_is_distinct");
            }
            finally { visual.Free(); }
        }
    }

    private static void CheckManifestations(Action<bool, string> require)
    {
        foreach (int mask in new[] { 0, 1, 2, 4, 8, 15 })
        {
            var visual = Hero(Equipped(mask: mask));
            try
            {
                require(Manifestations.All(m => (mask & m.Bit) != 0 ? HasGeometry(Module(visual, m.Name)) : Module(visual, m.Name) is null),
                    "appearance_manifestation_mask_" + mask + "_matches_visible_modules");
            }
            finally { visual.Free(); }
        }
        var first = Hero(Equipped(mask: 15)); var second = Hero(Equipped(mask: 15));
        try
        {
            var origin = first.Transform;
            var initialMaterials = MaterialState(first); var otherMaterials = MaterialState(second);
            var meshes = MeshIdentities(first); var cache = CharacterVisual.CachedEquipmentResourceCount;
            first.SetAccent(new("ed7663"));
            for (int i = 0; i < 30; i++) first.Animate(1.0 / 60, Vector3.Zero);
            require(SameMaterials(otherMaterials, MaterialState(second)) && !SameMaterials(initialMaterials, MaterialState(first)),
                "appearance_animated_accents_do_not_change_other_character");
            var animatedMaterials = MaterialState(first);
            require(initialMaterials.Any(p => animatedMaterials[p.Key].Emission != p.Value.Emission),
                "appearance_burning_fissures_pulse_on_owned_material");
            require(Module(first, "ManifestationShadow")!.Position.LengthSquared() > .000001f,
                "appearance_shadow_echo_animates_locally");
            var pausedPose = Pose(first); var pausedMaterials = MaterialState(first);
            first.Animate(.1, new(1, 0, 1), windup: true, paused: true);
            require(SamePose(pausedPose, Pose(first)) && SameMaterials(pausedMaterials, MaterialState(first)),
                "appearance_pause_freezes_all_poses_and_material_animation");
            first.SetReducedEffects(true);
            var reducedShadow = Module(first, "ManifestationShadow")!.Transform;
            var reducedMaterials = MaterialState(first);
            for (int i = 0; i < 60; i++) first.Animate(1.0 / 60, Vector3.Zero);
            require(reducedShadow.IsEqualApprox(Module(first, "ManifestationShadow")!.Transform) && SameMaterials(reducedMaterials, MaterialState(first)) &&
                Manifestations.All(m => HasGeometry(Module(first, m.Name))), "appearance_reduced_effects_keep_mutations_without_pulsing");
            require(first.Transform.IsEqualApprox(origin) && meshes.SequenceEqual(MeshIdentities(first)) && cache == CharacterVisual.CachedEquipmentResourceCount,
                "appearance_animation_preserves_root_and_mesh_resources");
            require(CosmeticOnly(first), "appearance_mutations_are_cosmetic");
        }
        finally { first.Free(); second.Free(); }
    }

    private static void CheckAnatomy(Action<bool, string> require)
    {
        require(CharacterAppearance.AnatomyFragments(null) == 0 && CharacterAppearance.AnatomyFragments(["fragment.unknown"]) == 0 &&
            CharacterAppearance.AnatomyFragments(["fragment.orrun_bone", "fragment.eye_vael", "fragment.heart_serath", "fragment.nerve_ilyra", "fragment.eye_vael"]) == 15,
            "appearance_anatomy_identity_uses_known_equipped_fragment_set");
        foreach (string discipline in Disciplines)
        {
            var empty = Equipped(discipline);
            var implanted = empty with { AnatomyMask = 15 };
            var bare = Hero(empty); var all = Hero(implanted); var heart = Hero(empty with { AnatomyMask = 2 });
            try
            {
                require(all.AppearanceKey == implanted.Key && all.AppearanceKey != bare.AppearanceKey && Anatomy.All(m => HasGeometry(Module(all, m.Name))) &&
                    Anatomy.All(m => Module(bare, m.Name) is null), "appearance_" + discipline + "_implants_change_identity_and_geometry");
                require(HasGeometry(Module(heart, "AnatomyHeart")) && Anatomy.Where(m => m.Bit != 2).All(m => Module(heart, m.Name) is null) &&
                    Manifestations.All(m => Module(heart, m.Name) is null) && GeometryExtent(Module(heart, "AnatomyHeart")!, minimumZ: true) < -.54f,
                    "appearance_" + discipline + "_first_heart_crest_is_outside_armor_before_manifestations");
                require(CosmeticOnly(all) && CosmeticOnly(heart), "appearance_" + discipline + "_implants_are_cosmetic_only");
                foreach (int bit in new[] { 1, 2, 4, 8 })
                {
                    var removed = Hero(implanted with { AnatomyMask = 15 & ~bit });
                    try
                    {
                        require(removed.AppearanceKey != all.AppearanceKey && Anatomy.All(m => (m.Bit & bit) != 0 ? Module(removed, m.Name) is null : HasGeometry(Module(removed, m.Name))) &&
                            Slots.All(s => HasGeometry(Module(removed, "Equipment" + s))),
                            "appearance_" + discipline + "_remove_anatomy_" + bit + "_preserves_other_implants_and_gear");
                    }
                    finally { removed.Free(); }
                }
            }
            finally { bare.Free(); all.Free(); heart.Free(); }
        }
        var first = Hero(Equipped() with { AnatomyMask = 15 });
        var second = Hero(Equipped() with { AnatomyMask = 15 });
        try
        {
            require(Anatomy.All(m => MeshIdentities(Module(first, m.Name)!).SequenceEqual(MeshIdentities(Module(second, m.Name)!))) &&
                Anatomy.Select(m => Geometry(Module(first, m.Name)!)).Distinct().Count() == Anatomy.Length,
                "appearance_anatomy_modules_are_distinct_and_share_cached_geometry");
            require(GeometryExtent(Module(first, "AnatomyEyes")!, minimumZ: true) < -.32f &&
                GeometryExtent(Module(first, "AnatomySpine")!, minimumZ: false) > .58f &&
                GeometryExtent(Module(first, "AnatomyArmsRight")!, minimumZ: true) < -.23f,
                "appearance_eye_spine_and_arm_marks_extend_past_clothing");
            var meshes = MeshIdentities(first); var cache = CharacterVisual.CachedEquipmentResourceCount;
            var materialState = MaterialState(first); var root = first.Transform;
            first.SetReducedEffects(true);
            for (int tick = 0; tick < 60; tick++) first.Animate(1.0 / 60, new(1, 0, 0));
            require(meshes.SequenceEqual(MeshIdentities(first)) && cache == CharacterVisual.CachedEquipmentResourceCount &&
                SameMaterials(materialState, MaterialState(first)) && root.IsEqualApprox(first.Transform),
                "appearance_implants_do_not_add_animation_resources_or_change_root_motion");
        }
        finally { first.Free(); second.Free(); }
    }

    private static float GeometryExtent(Node node, bool minimumZ)
    {
        float result = minimumZ ? float.PositiveInfinity : float.NegativeInfinity;
        void Visit(Node current, Transform3D parent)
        {
            var transform = current is Node3D spatial ? parent * spatial.Transform : parent;
            if (current is MeshInstance3D mesh)
                foreach (var vertex in mesh.Mesh.GetFaces())
                {
                    float z = (transform * vertex).Z;
                    result = minimumZ ? Math.Min(result, z) : Math.Max(result, z);
                }
            foreach (Node child in current.GetChildren()) Visit(child, transform);
        }
        Visit(node, Transform3D.Identity); return result;
    }

    private static void CheckLoot(Action<bool, string> require)
    {
        (string Id, string Slot, string Category)[] catalog =
        [
            ("ash_axe", "MainHand", "Axe"), ("ashcleaver", "MainHand", "Ashcleaver"), ("cinder_edge", "MainHand", "Saber"),
            ("oath_hammer", "MainHand", "Hammer"), ("pilgrim_pike", "MainHand", "Pike"), ("greatstaff", "MainHand", "Greatstaff"),
            ("starter_offhand", "OffHand", "Shield"), ("focus", "OffHand", "Focus"), ("ash_weave", "Chest", "Robe"),
            ("march_plate", "Chest", "Plate"), ("starter_head", "Head", "Helmet"), ("starter_shoulders", "Shoulders", "Shoulders"),
            ("starter_gloves", "Gloves", "Gloves"), ("starter_belt", "Belt", "Belt"), ("starter_legs", "Legs", "Legs"),
            ("starter_boots", "Boots", "Boots"), ("starter_ring1", "Ring1", "Ring"), ("starter_ring2", "Ring2", "Ring"),
            ("ember_lens", "Amulet", "Amulet"), ("unknown", "Unknown", "Parcel")
        ];
        var categoryGeometry = new Dictionary<string, ulong>();
        foreach (var entry in catalog)
        {
            var item = Item(entry.Id, entry.Slot); var visual = LootVisual.Create(item);
            try
            {
                require(visual.Category == entry.Category && visual.AppearanceKey == LootVisual.KeyFor(item) && HasGeometry(Module(visual, "LootModel")) && CosmeticOnly(visual),
                    "appearance_loot_" + entry.Id + "_shows_its_category");
                categoryGeometry.TryAdd(visual.Category, Geometry(Module(visual, "LootModel")!));
            }
            finally { visual.Free(); }
        }
        require(categoryGeometry.Values.Distinct().Count() == categoryGeometry.Count, "appearance_loot_categories_have_distinct_silhouettes");
        var cues = new HashSet<ulong>(); var colors = new HashSet<Color>(); var glyphs = new HashSet<string>();
        foreach (string rarity in Rarities)
        {
            var visual = LootVisual.Create(Item("ash_axe", "MainHand", rarity));
            try
            {
                require(visual.RarityTier == Array.IndexOf(Rarities, rarity) + 1 && cues.Add(Geometry(Module(visual, "LootRarity")!)) &&
                    colors.Add(LootVisual.RarityColor(rarity)) && glyphs.Add(LootVisual.RarityGlyph(rarity)),
                    "appearance_loot_" + rarity + "_has_color_and_geometry_cues");
            }
            finally { visual.Free(); }
        }
        var first = LootVisual.Create(Item("ashcleaver", "MainHand", "Godwrought"));
        var second = LootVisual.Create(Item("ashcleaver", "MainHand", "Godwrought"));
        try
        {
            var originalRoot = first.Transform; var originalModel = Module(first, "LootModel")!.Transform;
            var meshes = MeshIdentities(first); var cache = LootVisual.CachedResourceCounts;
            var untouchedMaterials = MaterialState(second);
            first.SetHighlighted(true);
            var selection = Module(first, "LootSelection")!;
            Materials(selection).Single().AlbedoColor = new("fffefe");
            require(selection.Visible && !Module(second, "LootSelection")!.Visible && SameMaterials(untouchedMaterials, MaterialState(second)),
                "appearance_loot_selection_is_independent");
            for (int i = 0; i < 30; i++) first.Animate(1.0 / 60, false, false);
            require(!originalModel.IsEqualApprox(Module(first, "LootModel")!.Transform) && first.Transform.IsEqualApprox(originalRoot),
                "appearance_loot_motion_never_moves_pickup_position");
            var paused = Pose(first); first.Animate(.1, true, false);
            require(SamePose(paused, Pose(first)), "appearance_loot_motion_pauses");
            first.Animate(.1, false, true); var reduced = Pose(first);
            for (int i = 0; i < 30; i++) first.Animate(1.0 / 60, false, true);
            require(SamePose(reduced, Pose(first)) && Module(first, "LootModel")!.Visible, "appearance_loot_reduced_effects_remain_static_and_visible");
            require(meshes.SequenceEqual(MeshIdentities(first)) && cache == LootVisual.CachedResourceCounts,
                "appearance_loot_animation_reuses_resources");
            require(first.AppearanceKey == LootVisual.KeyFor(Item("ashcleaver", "MainHand", "Godwrought") with { Id = 999, Damage = 100 }),
                "appearance_loot_identity_ignores_instance_and_stat_rolls");
        }
        finally { first.Free(); second.Free(); }
    }

    private static CombatItem Item(string id, string slot, string rarity = "Common") => new(1, "item." + id, id, slot, rarity, 0, 0, 0);
    private static IEnumerable<Node> Descendants(Node node)
    { yield return node; foreach (Node child in node.GetChildren()) foreach (Node nested in Descendants(child)) yield return nested; }
    private static Node3D? Module(Node node, string name) => Descendants(node).OfType<Node3D>().FirstOrDefault(n => n.Name == name);
    private static bool HasGeometry(Node? node) => node is not null && Descendants(node).OfType<MeshInstance3D>().Any(m => m.Mesh is not null && m.Mesh.GetSurfaceCount() > 0);
    private static bool CosmeticOnly(Node node) => !Descendants(node).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D);
    private static ulong[] MeshIdentities(Node node) => Descendants(node).OfType<MeshInstance3D>().Select(m => m.Mesh.GetInstanceId()).ToArray();
    private static Transform3D[] Pose(Node node) => Descendants(node).OfType<Node3D>().Select(n => n.Transform).ToArray();
    private static bool SamePose(Transform3D[] a, Transform3D[] b) => a.Length == b.Length && a.Zip(b).All(p => p.First.IsEqualApprox(p.Second));
    private static StandardMaterial3D[] Materials(Node node) => Descendants(node).OfType<MeshInstance3D>().Select(m => m.MaterialOverride).OfType<StandardMaterial3D>().Distinct().ToArray();
    private static Dictionary<ulong, (Color Color, float Emission)> MaterialState(Node node) => Materials(node).ToDictionary(m => m.GetInstanceId(), m => (m.AlbedoColor, m.EmissionEnergyMultiplier));
    private static bool SameMaterials(Dictionary<ulong, (Color Color, float Emission)> a, Dictionary<ulong, (Color Color, float Emission)> b)
        => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var value) && p.Value == value);

    private static ulong Geometry(Node node, bool includeMaterials = false)
    {
        ulong hash = 14695981039346656037;
        void Number(float value) { hash = unchecked((hash ^ BitConverter.SingleToUInt32Bits(value)) * 1099511628211); }
        void Visit(Node current, Transform3D parent)
        {
            var transform = current is Node3D spatial ? parent * spatial.Transform : parent;
            if (current is MeshInstance3D mesh)
            {
                foreach (var vertex in mesh.Mesh.GetFaces())
                { var p = transform * vertex; Number(p.X); Number(p.Y); Number(p.Z); }
                if (includeMaterials && mesh.MaterialOverride is StandardMaterial3D material)
                { Number(material.AlbedoColor.R); Number(material.AlbedoColor.G); Number(material.AlbedoColor.B); }
            }
            foreach (Node child in current.GetChildren()) Visit(child, transform);
        }
        Visit(node, Transform3D.Identity); return hash;
    }
}
