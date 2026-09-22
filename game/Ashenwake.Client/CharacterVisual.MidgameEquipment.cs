using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private void EquippedRing(Node3D parent, int side, ItemAppearance item)
    {
        if (ItemKind(item) != "rotwake_signet") return;
        Material bronze = SharedMaterial("7d8854", metallic: true), venom = SharedMaterial("b4d879", emissive: true);
        // The signet sits over the glove; its short thorn chain remains visible at game scale.
        Ring(parent, new(side * .08f, -.65f, -.17f), .038f, .058f, bronze, new(90, 0, 0));
        Orb(parent, new(side * .08f, -.65f, -.215f), new(.10f, .105f, .04f), venom);
        for (int thorn = 0; thorn < 3; thorn++)
        {
            float y = -.58f + thorn * .065f;
            Rod(parent, new(side * .08f, y, -.185f), new(side * .13f, y + .06f, -.185f), .013f, bronze);
            Cone(parent, new(side * .15f, y + .035f, -.185f), .025f, 0, .095f, bronze, new(0, 0, side * -45));
        }
    }

    private void EquippedMourningMantle(Node3D parent, int side, ItemAppearance item)
    {
        Material iron = SharedMaterial("42495a", metallic: true), cloth = SharedMaterial("292f41"), soul = SharedMaterial("99d9cf", emissive: true);
        TaperedBox(parent, new(side * .035f, -.03f, 0), new(.49f, .25f, .44f), iron, .82f, new(0, 0, side * 9));
        TailoredCape(parent, new(side * .16f, -.07f, .11f), .27f, .55f, cloth);
        // Three ivory organ pipes, each with a dark mouth and a thin spectral reed.
        for (int pipe = 0; pipe < 3; pipe++)
        {
            float x = side * (-.03f + pipe * .115f), height = .28f + pipe * .09f;
            Cone(parent, new(x, height * .4f, .04f), .05f, .06f, height, Bone);
            Ring(parent, new(x, height * .9f, .04f), .033f, .054f, iron);
            Box(parent, new(x, .03f, -.019f), new(.03f, .085f, .015f), soul);
            Box(parent, new(x, -.21f - pipe * .025f, -.23f), new(.042f, .26f, .023f), Bone, new(0, 0, side * 7));
        }
        Ring(parent, new(side * .06f, -.055f, -.255f), .062f, .087f, Bone, new(90, 0, 0));
        Orb(parent, new(side * .06f, -.055f, -.273f), new(.07f, .09f, .035f), soul);
        ArmorJewel(parent, new(side * .06f, -.14f, -.273f), item);
    }

    private void EquippedFurnaceBelt(Node3D parent, ItemAppearance item)
    {
        Material iron = SharedMaterial("383335", metallic: true), copper = SharedMaterial("bb7850", metallic: true), ember = SharedMaterial("ffc06c", emissive: true);
        var band = Cone(parent, new(0, 1.055f, 0), .37f, .36f, .18f, iron); band.Scale = new(1, 1, 1.02f);
        Box(parent, new(0, 1.055f, -.40f), new(.33f, .28f, .10f), copper);
        Box(parent, new(0, 1.055f, -.46f), new(.255f, .20f, .035f), iron);
        Orb(parent, new(0, 1.045f, -.49f), new(.19f, .14f, .028f), ember);
        for (int bar = -1; bar <= 1; bar++)
            Rod(parent, new(bar * .062f, .955f, -.518f), new(bar * .062f, 1.155f, -.518f), .018f, iron);
        for (int side = -1; side <= 1; side += 2)
        {
            Box(parent, new(side * .25f, .95f, -.32f), new(.13f, .29f, .10f), iron, new(0, 0, side * -12));
            for (int vent = 0; vent < 3; vent++)
                Box(parent, new(side * .25f, 1.025f - vent * .065f, -.379f), new(.075f, .022f, .018f), copper);
            Ring(parent, new(side * .38f, 1.055f, -.03f), .045f, .071f, copper, new(0, 0, 90));
        }
        ArmorJewel(parent, new(0, 1.205f, -.45f), item);
    }
}
