using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private void BuildArmor(CharacterAppearance appearance, string kind)
    {
        BuildSlot(_equipmentLeft, "EquipmentShoulders", "shoulders-left", appearance.Shoulders, kind, n => EquippedMantle(n, -1, appearance.Shoulders, kind));
        BuildSlot(_equipmentRight, "EquipmentShouldersRight", "shoulders-right", appearance.Shoulders, kind, n => EquippedMantle(n, 1, appearance.Shoulders, kind));
        BuildSlot(_equipmentLeft, "EquipmentGloves", "gloves-left", appearance.Gloves, kind, n => EquippedGlove(n, -1, appearance.Gloves, kind));
        BuildSlot(_equipmentRight, "EquipmentGlovesRight", "gloves-right", appearance.Gloves, kind, n => EquippedGlove(n, 1, appearance.Gloves, kind));
        BuildSlot(BodyRoot, "EquipmentBelt", "belt", appearance.Belt, kind, n => EquippedBelt(n, appearance.Belt, kind));
        BuildSlot(_equipmentLegLeft, "EquipmentLegs", "legs-left", appearance.Legs, kind, n => EquippedGreave(n, -1, appearance.Legs, kind));
        BuildSlot(_equipmentLegRight, "EquipmentLegsRight", "legs-right", appearance.Legs, kind, n => EquippedGreave(n, 1, appearance.Legs, kind));
        BuildSlot(_equipmentLegLeft, "EquipmentBoots", "boots-left", appearance.Boots, kind, n => EquippedBoot(n, -1, appearance.Boots, kind));
        BuildSlot(_equipmentLegRight, "EquipmentBootsRight", "boots-right", appearance.Boots, kind, n => EquippedBoot(n, 1, appearance.Boots, kind));
    }

    private static Material ArmorShell(string kind)
    {
        var palette = ArmorPalette.For(kind);
        return SharedMaterial(palette.Shell, metallic: palette.Metallic);
    }

    private void EquippedMantle(Node3D parent, int side, ItemAppearance item, string kind)
    {
        var palette = ArmorPalette.For(kind);
        Material shell = ArmorShell(kind), cloth = SharedMaterial(palette.Cloth), trim = SharedMaterial(palette.Trim);
        bool plate = palette.Metallic;
        // Wakeguard Mantle: layered funeral cloth, an engraved clasp and a protective crest.
        Orb(parent, new(side * .035f, -.025f, 0), new(plate ? .53f : .46f, .28f, .47f), shell);
        Box(parent, new(side * .16f, -.22f, .045f), new(.22f, .40f, .34f), cloth, new(0, 0, side * 12));
        for (int fold = 0; fold < 3; fold++)
        {
            float x = side * (.075f + fold * .073f);
            Box(parent, new(x, -.24f - fold * .025f, -.144f), new(.045f, .32f, .035f), cloth, new(0, 0, side * 8));
            Rod(parent, new(x, -.30f, -.17f), new(x, -.37f - fold * .025f, -.17f), .012f, trim);
        }
        if (kind is "gravecaller" or "warden")
            for (int i = 0; i < 3; i++)
                Cone(parent, new(side * (.025f + i * .075f), .09f, .05f), .06f, 0, .18f + i * .055f,
                    kind == "gravecaller" ? Bone : shell, new(0, 0, -side * (20 + i * 12)));
        else
            Box(parent, new(side * .06f, .10f, .02f), new(.31f, .06f, .28f), shell, new(0, 0, side * 12));
        Ring(parent, new(side * .06f, -.035f, -.245f), .052f, .077f, trim, new(90, 0, 0));
        Rod(parent, new(side * .06f, .01f, -.262f), new(side * .06f, -.08f, -.262f), .012f, trim);
        ArmorJewel(parent, new(side * .06f, -.035f, -.27f), item);
    }

    private void EquippedGlove(Node3D parent, int side, ItemAppearance item, string kind)
    {
        if (ItemKind(item) == "widows_last_echo") { EquippedWidowGlove(parent, side, item); return; }
        var palette = ArmorPalette.For(kind);
        Material leather = SharedMaterial(ArmorPalette.Leather), trim = SharedMaterial(palette.Trim);
        // Gravesoil Grips: soil-dark palms, wrapped cuffs and ivory knuckle guards.
        Cone(parent, new(side * .04f, -.47f, -.015f), .115f, .135f, .29f, ArmorShell(kind));
        Orb(parent, new(side * .04f, -.64f, -.045f), new(.22f, .23f, .24f), leather);
        Box(parent, new(side * .045f, -.48f, -.135f), new(.17f, .22f, .055f), SharedMaterial(palette.Cloth));
        for (int i = 0; i < 3; i++)
        {
            Box(parent, new(side * .04f, -.40f - i * .072f, -.173f), new(.18f, .022f, .025f), trim, new(0, 0, side * -12));
            Orb(parent, new(side * .04f + (i - 1) * .06f, -.63f, -.159f), new(.045f, .075f, .04f), Bone);
        }
        ArmorJewel(parent, new(side * .04f, -.46f, -.196f), item);
    }

    private void EquippedBelt(Node3D parent, ItemAppearance item, string kind)
    {
        var palette = ArmorPalette.For(kind);
        Material leather = SharedMaterial(ArmorPalette.Leather), trim = SharedMaterial(palette.Trim);
        // Last-Rite Girdle: a funerary seal, prayer strips and a sealed reliquary pouch.
        var band = Cone(parent, new(0, 1.055f, 0), .365f, .355f, .14f, leather); band.Scale = new(1, 1, 1.02f);
        Box(parent, new(0, 1.055f, -.386f), new(.23f, .18f, .065f), trim);
        Box(parent, new(0, 1.055f, -.425f), new(.14f, .115f, .025f), SharedMaterial(palette.Cloth));
        Rod(parent, new(-.04f, 1.055f, -.445f), new(.04f, 1.055f, -.445f), .013f, Bone);
        Rod(parent, new(0, 1.095f, -.445f), new(0, 1.01f, -.445f), .013f, Bone);
        for (int side = -1; side <= 1; side += 2)
        {
            Box(parent, new(side * .16f, .84f, -.35f), new(.09f, side < 0 ? .32f : .40f, .045f), SharedMaterial(palette.Cloth), new(0, 0, side * -8));
            for (int mark = 0; mark < 3; mark++)
                Box(parent, new(side * (.16f + mark * .008f), .92f - mark * .062f, -.382f), new(.045f, .016f, .02f), trim);
        }
        Box(parent, new(.35f, .94f, .08f), new(.15f, .22f, .23f), leather);
        Ring(parent, new(.433f, .98f, .08f), .025f, .043f, trim, new(0, 0, 90));
        ArmorJewel(parent, new(0, 1.055f, -.47f), item);
    }

    private void EquippedGreave(Node3D parent, int side, ItemAppearance item, string kind)
    {
        var palette = ArmorPalette.For(kind);
        Material shell = ArmorShell(kind), cloth = SharedMaterial(palette.Cloth), trim = SharedMaterial(palette.Trim);
        // Mourner's Greaves: overlapping knee plates and a hanging mourning ribbon.
        Cone(parent, new(0, -.17f, 0), .15f, .17f, .34f, cloth);
        Box(parent, new(side * .025f, -.20f, -.145f), new(.23f, .26f, .07f), shell, new(-8, 0, side * -4));
        Orb(parent, new(0, -.385f, -.08f), new(.30f, .25f, .28f), shell);
        for (int direction = -1; direction <= 1; direction += 2)
            Rod(parent, new(direction * .10f, -.355f, -.21f), new(0, -.43f, -.23f), .021f, trim);
        Box(parent, new(side * .155f, -.30f, .015f), new(.055f, .45f, .16f), cloth, new(0, 0, side * 6));
        Box(parent, new(side * .179f, -.47f, -.073f), new(.06f, .028f, .03f), Bone);
        ArmorJewel(parent, new(0, -.36f, -.24f), item);
    }

    private void EquippedBoot(Node3D parent, int side, ItemAppearance item, string kind)
    {
        if (ItemKind(item) == "pyrebound_treads") { EquippedPyreBoot(parent, side, item); return; }
        var palette = ArmorPalette.For(kind);
        Material leather = SharedMaterial(ArmorPalette.Leather), shell = ArmorShell(kind), ember = SharedMaterial(ArmorPalette.Ember);
        // Cindertrail Boots: reinforced toes and copper-ember seams, without a light or particle emitter.
        Cone(parent, new(0, -.615f, 0), .127f, .158f, .27f, leather);
        Box(parent, new(0, -.81f, -.09f), new(.28f, .20f, .44f), leather);
        Box(parent, new(0, -.84f, -.24f), new(.29f, .115f, .20f), shell);
        Box(parent, new(0, -.905f, -.09f), new(.29f, .035f, .45f), Dark);
        Ring(parent, new(0, -.50f, 0), .128f, .167f, shell);
        for (int i = 0; i < 3; i++)
            Rod(parent, new(-.06f, -.55f - i * .062f, -.135f), new(.06f, -.58f - i * .062f, -.135f), .015f, SharedMaterial(palette.Trim));
        Rod(parent, new(side * .151f, -.85f, -.28f), new(side * .151f, -.85f, .10f), .015f, ember);
        ArmorJewel(parent, new(side * .15f, -.58f, -.03f), item);
    }

    private void EquippedWidowGlove(Node3D parent, int side, ItemAppearance item)
    {
        Material silk = SharedMaterial("353049"), silver = SharedMaterial("a7adc4", metallic: true), echo = SharedMaterial("c9b5ef", emissive: true);
        Cone(parent, new(side * .04f, -.46f, -.015f), .105f, .15f, .33f, silk);
        Orb(parent, new(side * .04f, -.65f, -.045f), new(.20f, .23f, .23f), silk);
        // Crossed silver thread and a split silk cuff remain readable without particles.
        for (int i = 0; i < 3; i++)
        {
            float y = -.35f - i * .09f;
            Rod(parent, new(side * .04f - .09f, y, -.15f), new(side * .04f + .09f, y - .10f, -.15f), .013f, silver);
            Rod(parent, new(side * .04f + .09f, y, -.15f), new(side * .04f - .09f, y - .10f, -.15f), .013f, echo);
        }
        for (int i = -1; i <= 1; i += 2)
            Box(parent, new(side * .08f + i * .07f, -.47f, .08f), new(.055f, .40f, .025f), silk, new(0, 0, i * 13));
        Orb(parent, new(side * .04f, -.59f, -.17f), new(.075f, .115f, .045f), echo);
        ArmorJewel(parent, new(side * .04f, -.37f, -.174f), item);
    }

    private void EquippedPyreBoot(Node3D parent, int side, ItemAppearance item)
    {
        Material charred = SharedMaterial("343133", metallic: true), copper = SharedMaterial("bd7344", metallic: true), fire = SharedMaterial("ffa457", emissive: true);
        Cone(parent, new(0, -.60f, 0), .14f, .18f, .34f, charred);
        Box(parent, new(0, -.81f, -.10f), new(.31f, .23f, .49f), charred);
        Box(parent, new(0, -.91f, -.10f), new(.33f, .04f, .50f), copper);
        Box(parent, new(0, -.82f, -.29f), new(.28f, .14f, .17f), copper, new(-12, 0, 0));
        Ring(parent, new(0, -.435f, 0), .145f, .188f, copper);
        for (int edge = -1; edge <= 1; edge += 2)
        {
            Rod(parent, new(edge * .14f, -.84f, -.34f), new(edge * .14f, -.73f, -.11f), .020f, fire);
            Rod(parent, new(edge * .14f, -.73f, -.11f), new(edge * .10f, -.54f, -.135f), .020f, fire);
            Cone(parent, new(edge * .10f, -.51f, -.13f), .038f, 0, .17f, copper, new(0, 0, edge * -20));
        }
        Rod(parent, new(side * .166f, -.88f, -.31f), new(side * .166f, -.88f, .13f), .018f, fire);
        ArmorJewel(parent, new(0, -.60f, -.16f), item);
    }

    private static void ArmorJewel(Node3D parent, Vector3 at, ItemAppearance item)
    {
        if (RarityRank(item.Rarity) > 0) Orb(parent, at, new(.045f, .06f, .04f), RarityMaterial(item));
    }

    private static void EquippedRobeSkirt(Node3D parent, float radius, Material fabric, Material trim)
    {
        // A front opening exposes independently equipped knees/boots. Back and side panels retain the robe silhouette.
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        // Keep front/back normals separate. Sixteen extra back faces let the skirt batch with
        // the chest fabric without duplicating or changing the shared material's culling mode.
        surface.SetSmoothGroup(uint.MaxValue);
        Vector3 Point(float angle, bool bottom) => new(MathF.Sin(angle) * (bottom ? radius : .28f), bottom ? .18f : 1.13f,
            .035f + MathF.Cos(angle) * (bottom ? radius : .28f) * .76f);
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            surface.AddVertex(a); surface.AddVertex(b); surface.AddVertex(c);
            surface.AddVertex(c); surface.AddVertex(b); surface.AddVertex(a);
        }
        const int panels = 8;
        for (int i = 0; i < panels; i++)
        {
            float a = -Mathf.Pi * 2 / 3 + i * Mathf.Pi / 6, b = a + Mathf.Pi / 6;
            Triangle(Point(a, false), Point(a, true), Point(b, true));
            Triangle(Point(a, false), Point(b, true), Point(b, false));
            Rod(parent, Point(a, true), Point(b, true), .019f, trim);
        }
        surface.GenerateNormals();
        parent.AddChild(new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = fabric });
        foreach (float angle in new[] { -Mathf.Pi * 2 / 3, Mathf.Pi * 2 / 3 })
            Rod(parent, Point(angle, false), Point(angle, true), .018f, trim);
    }
}
