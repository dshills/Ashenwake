using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private static void EquippedUnswornCrown(Node3D parent, ItemAppearance item)
    {
        Material gold = SharedMaterial("b99754", metallic: true), oath = SharedMaterial("ffe2a0", emissive: true);
        // The missing central oath leaves a visible fracture between two jagged halves.
        for (int side = -1; side <= 1; side += 2)
        {
            Rod(parent, new(side * .045f, 2.17f, -.24f), new(side * .24f, 2.17f, -.14f), .045f, gold);
            Rod(parent, new(side * .24f, 2.17f, -.14f), new(side * .22f, 2.17f, .17f), .045f, gold);
            for (int tooth = 0; tooth < 3; tooth++)
            {
                float x = side * (.09f + tooth * .075f), z = -.235f + tooth * .065f;
                Cone(parent, new(x, 2.29f + tooth * .025f, z), .06f, 0, .25f + tooth * .05f, gold, new(0, 0, side * -15));
            }
            Rod(parent, new(side * .052f, 2.14f, -.27f), new(side * .09f, 2.25f, -.265f), .018f, oath);
        }
        Ring(parent, new(0, 2.17f, .045f), .20f, .235f, gold);
        ArmorJewel(parent, new(0, 2.18f, -.255f), item);
    }

    private void EquippedAmulet(Node3D parent, ItemAppearance item)
    {
        bool witness = ItemKind(item) == "last_witness";
        Material chain = SharedMaterial(witness ? "a6a0bb" : "bb9f65", metallic: true);
        Material glow = SharedMaterial(witness ? "beafff" : "e0c181", emissive: witness);
        for (int side = -1; side <= 1; side += 2)
        {
            Rod(parent, new(side * .14f, 1.76f, -.19f), new(side * .20f, 1.60f, -.37f), .015f, chain);
            Rod(parent, new(side * .20f, 1.60f, -.37f), new(0, 1.38f, -.47f), .015f, chain);
        }
        if (!witness)
        {
            Ring(parent, new(0, 1.38f, -.47f), .05f, .08f, chain, new(90, 0, 0));
            Orb(parent, new(0, 1.38f, -.49f), new(.075f, .11f, .035f), glow);
            return;
        }
        // A wide spectral eye is legible above every chest plate and robe.
        foreach (int side in new[] { -1, 1 })
        {
            Rod(parent, new(side * .17f, 1.39f, -.49f), new(0, 1.48f, -.51f), .022f, chain);
            Rod(parent, new(side * .17f, 1.39f, -.49f), new(0, 1.30f, -.51f), .022f, chain);
        }
        Orb(parent, new(0, 1.39f, -.515f), new(.12f, .135f, .045f), glow);
        Box(parent, new(0, 1.39f, -.545f), new(.018f, .092f, .015f), Dark);
        Rod(parent, new(0, 1.29f, -.49f), new(0, 1.20f, -.47f), .014f, chain);
    }

    private static void EquippedHourGreave(Node3D parent, int side, ItemAppearance item)
    {
        Material slate = SharedMaterial("354350", metallic: true), silver = SharedMaterial("a2babd", metallic: true), hour = SharedMaterial("97eee6", emissive: true);
        Cone(parent, new(0, -.16f, 0), .15f, .17f, .34f, slate);
        TaperedBox(parent, new(0, -.30f, -.145f), new(.30f, .44f, .11f), silver, .76f);
        TaperedBox(parent, new(0, -.30f, -.21f), new(.22f, .36f, .035f), slate, .76f);
        // Paired crystal hourglasses keep their identity when all particles are disabled.
        for (int edge = -1; edge <= 1; edge += 2)
        {
            Rod(parent, new(edge * .082f, -.155f, -.239f), new(-edge * .068f, -.44f, -.239f), .014f, hour);
            Rod(parent, new(-.085f, -.16f - (edge + 1) * .14f, -.242f), new(.085f, -.16f - (edge + 1) * .14f, -.242f), .016f, silver);
        }
        Orb(parent, new(0, -.29f, -.25f), new(.045f, .06f, .025f), hour);
        Box(parent, new(side * .155f, -.29f, .02f), new(.05f, .40f, .15f), slate, new(0, 0, side * 8));
        ArmorJewel(parent, new(0, -.145f, -.25f), item);
    }
}
