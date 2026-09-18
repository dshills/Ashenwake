using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    // Cache independent slot meshes, never complete loadout combinations. Materials remain separate.
    private const int EquipmentTemplateLimit = 160;
    private static readonly Dictionary<string, Mesh[]> EquipmentTemplates = new(StringComparer.Ordinal);
    private CharacterAppearance? _appearance;
    private Node3D _equipmentLeft = null!, _equipmentRight = null!;
    public string AppearanceKey => _appearance?.Key ?? "";
    internal static int CachedEquipmentResourceCount => EquipmentTemplates.Count;

    private void BuildEquippedBody()
    {
        Height = 2.3f;
        HumanLeg(-1, false); HumanLeg(1, false);
        Box(BodyRoot, new(0, .96f, 0), new(.48f, .25f, .3f), Dark);
        var shirt = Cone(BodyRoot, new(0, 1.34f, 0), .27f, .34f, .64f, Main);
        shirt.Scale = new(1, 1, .65f);
        Box(BodyRoot, new(0, 1.03f, -.025f), new(.56f, .11f, .36f), Dark);
        Box(BodyRoot, new(0, 1.03f, -.215f), new(.11f, .10f, .045f), Metal);
        Cone(BodyRoot, new(0, 1.77f, 0), .105f, .105f, .16f, Skin);
        _equipmentLeft = HumanArm(-1, false); _equipmentRight = HumanArm(1, false);
        HumanFace();
    }

    private void BuildEquipment(CharacterAppearance appearance)
    {
        string kind = appearance.Discipline.ToLowerInvariant();
        BuildSlot(BodyRoot, "EquipmentChest", "chest", appearance.Chest, kind, n => EquippedChest(n, appearance.Chest, kind));
        if (appearance.Chest.DefinitionId.Length > 0)
        {
            BuildSlot(_equipmentLeft, "EquipmentChestLeft", "chest-left", appearance.Chest, kind, n => EquippedShoulder(n, -1, appearance.Chest, kind));
            BuildSlot(_equipmentRight, "EquipmentChestRight", "chest-right", appearance.Chest, kind, n => EquippedShoulder(n, 1, appearance.Chest, kind));
        }
        BuildSlot(BodyRoot, "EquipmentHead", "head", appearance.Head, kind, n => EquippedHead(n, appearance.Head, kind));
        BuildSlot(_equipmentLeft, "EquipmentOffHand", "offhand", appearance.OffHand, kind, n => EquippedOffHand(n, appearance.OffHand, kind));
        BuildSlot(_equipmentRight, "EquipmentMainHand", "mainhand", appearance.MainHand, kind, n => EquippedWeapon(n, appearance.MainHand, kind));
    }

    private void BuildSlot(Node3D parent, string name, string slot, ItemAppearance item, string kind, Action<Node3D> build)
    {
        if (item.DefinitionId.Length == 0) return;
        string key = $"{slot}/{kind}/{ItemKind(item)}/{RarityRank(item.Rarity)}/{EvolutionKind(item)}";
        BuildModule(parent, name, key, build);
    }

    private static Node3D BuildModule(Node3D parent, string name, string key, Action<Node3D> build)
    {
        var module = new Node3D { Name = name }; parent.AddChild(module); build(module);
        EquipmentTemplates.TryGetValue(key, out var cached);
        var generated = new List<Mesh>(); int groupIndex = 0;
        Batch(module, generated, cached, ref groupIndex);
        if (cached is null && EquipmentTemplates.Count < EquipmentTemplateLimit) EquipmentTemplates.Add(key, generated.ToArray());
        return module;
    }

    private static string ItemKind(ItemAppearance item)
    {
        string id = item.DefinitionId.ToLowerInvariant();
        return id.StartsWith("item.", StringComparison.Ordinal) ? id[5..] : id;
    }

    private static string EvolutionKind(ItemAppearance item) => item.Evolution.ToLowerInvariant() switch
    { "serath" => "serath", "orrun" => "orrun", "awakened" => "awakened", _ => "" };

    private static int RarityRank(string rarity) => rarity.ToLowerInvariant() switch
    { "tempered" => 1, "rare" => 2, "relic" => 3, "legendary" => 4, "godwrought" => 5, _ => 0 };

    private static StandardMaterial3D RarityMaterial(ItemAppearance item) => SharedMaterial(item.Rarity.ToLowerInvariant() switch
    { "tempered" => "83b891", "rare" => "719fef", "relic" => "bc8feb", "legendary" => "e5b666", "godwrought" => "ff8050", _ => "9eafb0" }, emissive: true);

    private void EquippedWeapon(Node3D parent, ItemAppearance item, string kind)
    {
        string weapon = ItemKind(item);
        if (weapon == "starter_mainhand") weapon = kind switch
        { "veilwalker" => "dagger", "arcanist" => "staff", "gravecaller" => "bone_staff", "warden" => "pilgrim_pike", _ => "sword" };
        switch (weapon)
        {
            case "ash_axe":
            case "ashcleaver":
                EquippedAxe(parent, item, weapon == "ashcleaver"); break;
            case "oath_hammer":
                Rod(parent, new(.1f, -.92f, -.07f), new(.1f, .24f, -.07f), .047f, Dark);
                Box(parent, new(.1f, .29f, -.07f), new(.61f, .33f, .31f), Metal);
                Box(parent, new(.1f, .29f, -.24f), new(.21f, .23f, .04f), Accent);
                for (int side = -1; side <= 1; side += 2)
                    Box(parent, new(.1f + side * .29f, .29f, -.07f), new(.065f, .39f, .37f), Bone);
                break;
            case "pilgrim_pike":
                Rod(parent, new(.075f, -1.53f, -.075f), new(.075f, .92f, -.075f), .037f, Dark);
                Cone(parent, new(.075f, 1.08f, -.075f), .12f, 0, .44f, Metal);
                Box(parent, new(.18f, .74f, -.075f), new(.22f, .29f, .045f), Accent);
                Ring(parent, new(.075f, .84f, -.075f), .055f, .08f, Bone);
                break;
            case "greatstaff":
            case "staff":
            case "bone_staff":
                bool bone = weapon == "bone_staff";
                bool great = weapon == "greatstaff";
                Rod(parent, new(.075f, -1.53f, -.075f), new(.075f, .68f, -.075f), great ? .061f : .043f, bone ? Bone : Dark);
                Ring(parent, new(.075f, .85f, -.075f), great ? .23f : .14f, great ? .29f : .19f, bone ? Bone : Metal, new(90, 0, 0));
                Orb(parent, new(.075f, .85f, -.075f), great ? new(.28f, .34f, .28f) : new(.19f, .24f, .19f), Glow);
                Cone(parent, new(.075f, .54f, -.075f), .085f, .085f, .18f, Metal);
                if (great)
                    for (int side = -1; side <= 1; side += 2)
                        Cone(parent, new(.075f + side * .2f, .62f, -.075f), .09f, 0, .46f, Metal, new(0, 0, side * -25));
                break;
            case "cinder_edge":
                HumanSword(parent, .11f, .75f);
                Box(parent, new(.15f, .16f, -.052f), new(.19f, .4f, .052f), Metal, new(0, 0, -13));
                Rod(parent, new(.17f, -.4f, -.089f), new(.25f, .36f, -.089f), .017f, SharedMaterial("ef9a66", emissive: true));
                break;
            default:
                HumanSword(parent, .11f, weapon == "dagger" ? .39f : .82f); break;
        }
        int rank = RarityRank(item.Rarity);
        if (rank > 0)
        {
            Ring(parent, new(.105f, -.53f, -.07f), .048f, .078f, Metal);
            Orb(parent, new(.105f, -.53f, -.139f), new(.075f, .11f, .06f), RarityMaterial(item));
        }
    }

    private void EquippedAxe(Node3D parent, ItemAppearance item, bool godwrought)
    {
        string evolution = EvolutionKind(item);
        Material edge = godwrought ? SharedMaterial(evolution == "orrun" ? "9dcdd8" : "ff995b", emissive: true) : Metal;
        Material head = godwrought ? SharedMaterial(evolution == "orrun" ? "65777f" : "7b4235", metallic: true) : Metal;
        Rod(parent, new(.11f, -1.12f, -.07f), new(.11f, .54f, -.07f), godwrought ? .065f : .05f, godwrought ? Bone : Dark);
        Box(parent, new(.11f, .35f, -.07f), new(.22f, .37f, .19f), head);
        Box(parent, new(.36f, .3f, -.07f), new(.44f, .47f, .12f), head, new(0, 0, -12));
        Box(parent, new(.56f, .32f, -.07f), new(.065f, .52f, .13f), edge, new(0, 0, -12));
        Cone(parent, new(.34f, .58f, -.07f), .20f, 0, .32f, head, new(0, 0, -55));
        if (!godwrought) return;
        // An exposed ivory spine and three glowing ribs distinguish the named weapon at game scale.
        for (int rib = 0; rib < 3; rib++)
        {
            float y = .15f + rib * .15f;
            Rod(parent, new(.10f, y, -.19f), new(.42f, y + .05f, -.16f), .033f, Bone);
            Orb(parent, new(.20f, y + .02f, -.206f), new(.065f, .07f, .055f), edge);
        }
        Cone(parent, new(-.05f, .32f, -.07f), .14f, 0, .42f, Bone, new(0, 0, 90));
        Orb(parent, new(.11f, .66f, -.07f), new(.22f, .24f, .2f), Bone);
        Box(parent, new(.11f, .67f, -.174f), new(.15f, .035f, .025f), edge);
        if (evolution == "awakened")
        {
            Ring(parent, new(.11f, -.14f, -.07f), .08f, .115f, edge);
            Rod(parent, new(.11f, -.44f, -.14f), new(.11f, .14f, -.14f), .021f, edge);
        }
        if (evolution == "serath")
            for (int i = 0; i < 3; i++)
                Cone(parent, new(.31f + i * .11f, .63f - i * .07f, -.07f), .073f, 0, .32f + i * .05f, edge, new(0, 0, -20 - i * 12));
        else if (evolution == "orrun")
        {
            Box(parent, new(.16f, .25f, -.07f), new(.22f, .58f, .28f), head);
            Ring(parent, new(.11f, -.14f, -.07f), .09f, .145f, edge);
            Box(parent, new(.13f, .3f, -.225f), new(.08f, .35f, .035f), edge);
        }
    }

    private void EquippedOffHand(Node3D parent, ItemAppearance item, string kind)
    {
        if (kind == "vanguard")
        {
            Box(parent, new(-.08f, -.41f, -.245f), new(.6f, .78f, .10f), Metal, new(0, -10, 0));
            Box(parent, new(-.08f, -.4f, -.31f), new(.47f, .62f, .055f), Main, new(0, -10, 0));
            Box(parent, new(-.08f, -.4f, -.348f), new(.065f, .52f, .025f), Accent);
            Box(parent, new(-.08f, -.4f, -.348f), new(.36f, .065f, .025f), Accent);
            Orb(parent, new(-.08f, -.4f, -.385f), new(.16f, .16f, .075f), RarityRank(item.Rarity) > 0 ? RarityMaterial(item) : Metal);
        }
        else if (kind == "veilwalker") HumanSword(parent, -.10f, .39f);
        else if (kind == "gravecaller")
        {
            Orb(parent, new(-.06f, -.62f, -.16f), new(.27f, .30f, .25f), Bone);
            Box(parent, new(-.06f, -.60f, -.29f), new(.18f, .06f, .025f), Dark);
            Orb(parent, new(-.06f, -.61f, -.31f), new(.06f, .04f, .03f), Glow);
        }
        else if (kind == "warden")
        {
            Ring(parent, new(-.08f, -.49f, -.13f), .16f, .22f, Dark, new(90, 0, 0));
            Orb(parent, new(-.08f, -.49f, -.13f), new(.23f, .30f, .19f), Accent);
            for (int side = -1; side <= 1; side += 2)
                Cone(parent, new(-.08f + side * .17f, -.27f, -.13f), .065f, 0, .27f, Bone, new(0, 0, side * -20));
        }
        else
        {
            Orb(parent, new(-.06f, -.51f, -.2f), new(.24f, .24f, .24f), Glow);
            Ring(parent, new(-.06f, -.51f, -.2f), .17f, .2f, Metal, new(45, 0, 20));
        }
        if (RarityRank(item.Rarity) >= 2)
            Orb(parent, new(-.06f, -.73f, -.21f), new(.09f, .13f, .09f), RarityMaterial(item));
    }

    private void EquippedHead(Node3D parent, ItemAppearance item, string kind)
    {
        switch (kind)
        {
            case "vanguard":
                Orb(parent, new(0, 2.02f, 0), new(.49f, .52f, .46f), Metal);
                Box(parent, new(0, 2.03f, -.236f), new(.35f, .06f, .035f), Dark);
                Box(parent, new(0, 1.94f, -.246f), new(.055f, .2f, .055f), Main);
                Box(parent, new(0, 2.28f, .015f), new(.10f, .17f, .34f), Accent); break;
            case "veilwalker":
            case "gravecaller":
            case "warden":
                Orb(parent, new(0, 2.025f, .025f), new(.56f, .61f, .49f), kind == "veilwalker" ? Main : Dark);
                Orb(parent, new(0, 1.99f, -.2f), new(.34f, .37f, .15f), kind == "gravecaller" ? Bone : kind == "warden" ? Skin : Dark);
                Box(parent, new(-.074f, 2.025f, -.28f), new(.05f, .03f, .018f), kind == "veilwalker" ? Glow : Dark);
                Box(parent, new(.074f, 2.025f, -.28f), new(.05f, .03f, .018f), kind == "veilwalker" ? Glow : Dark);
                if (kind == "veilwalker") Box(parent, new(0, 1.9f, -.281f), new(.32f, .1f, .03f), Accent);
                if (kind == "gravecaller") Box(parent, new(0, 1.915f, -.283f), new(.16f, .07f, .025f), Dark);
                if (kind == "warden")
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Rod(parent, new(side * .15f, 2.21f, .01f), new(side * .30f, 2.49f, .035f), .046f, Bone);
                        Rod(parent, new(side * .30f, 2.49f, .035f), new(side * .38f, 2.67f, -.055f), .025f, Bone);
                        Rod(parent, new(side * .26f, 2.4f, .03f), new(side * .43f, 2.48f, -.065f), .024f, Bone);
                    }
                break;
            default:
                Cone(parent, new(0, 2.20f, .02f), .25f, .18f, .18f, Main);
                Box(parent, new(0, 2.17f, -.235f), new(.13f, .16f, .065f), Metal);
                Orb(parent, new(0, 2.17f, -.28f), new(.065f, .09f, .045f), Glow); break;
        }
        int rank = RarityRank(item.Rarity);
        if (rank > 0)
            Orb(parent, new(0, 2.20f, -.285f), new(.09f, .10f, .055f), RarityMaterial(item));
        if (rank >= 3)
            for (int side = -1; side <= 1; side += 2)
                Cone(parent, new(side * .22f, 2.26f, .01f), .065f, 0, .29f, Metal, new(0, 0, side * -25));
    }

    private void EquippedChest(Node3D parent, ItemAppearance item, string kind)
    {
        string chest = ItemKind(item);
        bool plate = chest is "march_plate" or "oath_plate" || (chest == "starter_chest" && kind == "vanguard");
        bool robe = chest is "ash_weave" or "serath_shroud" || (chest == "starter_chest" && kind is "arcanist" or "gravecaller");
        Material fabric = chest switch
        { "ash_weave" => SharedMaterial("473e50"), "serath_shroud" => SharedMaterial("743f3b"), "oath_plate" => SharedMaterial("c2ae7c", metallic: true), "march_plate" => SharedMaterial("899398", metallic: true), _ => Main };
        Material trim = chest == "serath_shroud" ? SharedMaterial("e69b61", emissive: true) : Accent;
        var torso = Cone(parent, new(0, 1.37f, 0), .29f, plate ? .40f : .37f, .62f, plate && chest == "starter_chest" ? Metal : fabric);
        torso.Scale = new(1, 1, .75f);
        Box(parent, new(0, 1.38f, -.3f), new(plate ? .43f : .36f, .46f, .065f), plate ? Main : fabric);
        Rod(parent, new(0, 1.6f, -.345f), new(0, 1.19f, -.345f), .03f, trim);
        if (plate)
        {
            Box(parent, new(0, .86f, -.22f), new(.30f, .39f, .06f), trim);
            for (int side = -1; side <= 1; side += 2)
                Box(parent, new(side * .28f, .98f, 0), new(.17f, .26f, .32f), fabric, new(0, 0, side * 13));
        }
        if (robe)
        {
            var skirt = Cone(parent, new(0, .74f, .035f), chest == "serath_shroud" ? .51f : .46f, .28f, 1.2f, fabric);
            skirt.Scale = new(1, 1, .76f);
            Box(parent, new(0, .81f, -.33f), new(.12f, 1.1f, .045f), trim);
            Ring(parent, new(0, .17f, .035f), .37f, .44f, Metal);
        }
        float capeLength = robe ? 1.26f : kind == "veilwalker" ? 1.15f : .80f;
        Box(parent, new(0, 1.68f - capeLength * .5f, .33f), new(chest == "serath_shroud" ? .73f : .62f, capeLength, .065f), (chest == "starter_chest" && kind is "vanguard" or "warden") ? Accent : fabric, new(10, 0, 0));
        if (chest == "oath_plate")
        {
            Box(parent, new(0, 1.41f, -.354f), new(.29f, .055f, .035f), Metal);
            Ring(parent, new(0, 1.42f, -.37f), .07f, .105f, fabric, new(90, 0, 0));
        }
        if (kind == "gravecaller" && chest == "starter_chest")
            for (int side = -1; side <= 1; side += 2)
                for (int rib = 0; rib < 3; rib++)
                    Rod(parent, new(side * .055f, 1.54f - rib * .115f, -.355f), new(side * (.245f - rib * .027f), 1.49f - rib * .115f, -.29f), .026f, Bone);
        if (chest is "ash_weave" or "serath_shroud")
            for (int side = -1; side <= 1; side += 2)
            {
                Rod(parent, new(side * .27f, 1.58f, -.22f), new(side * .08f, 1.04f, -.31f), .028f, trim);
                Cone(parent, new(side * .34f, 1.66f, .12f), .11f, 0, .38f, chest == "serath_shroud" ? Bone : Metal, new(0, 0, side * -30));
            }
        if (RarityRank(item.Rarity) > 0)
            Orb(parent, new(0, 1.55f, -.39f), new(.13f, .16f, .07f), RarityMaterial(item));
    }

    private void EquippedShoulder(Node3D parent, int side, ItemAppearance item, string kind)
    {
        string chest = ItemKind(item);
        bool plate = chest is "march_plate" or "oath_plate" || (chest == "starter_chest" && kind == "vanguard");
        Material surface = chest switch
        { "oath_plate" => SharedMaterial("c2ae7c", metallic: true), "ash_weave" => SharedMaterial("473e50"), "serath_shroud" => SharedMaterial("743f3b"), _ => plate ? Metal : Main };
        Orb(parent, new(side * .02f, -.025f, 0), plate ? new(.48f, .29f, .49f) : new(.38f, .23f, .40f), surface);
        if (plate)
            Box(parent, new(side * .055f, -.47f, -.025f), new(.24f, .27f, .25f), surface);
        if (chest == "oath_plate" || chest == "serath_shroud")
            Cone(parent, new(side * .04f, .12f, 0), .12f, 0, .34f, chest == "serath_shroud" ? Bone : surface, new(0, 0, side * -28));
        if (RarityRank(item.Rarity) >= 3)
            Orb(parent, new(side * .10f, .085f, -.12f), new(.1f, .12f, .085f), RarityMaterial(item));
    }
}
