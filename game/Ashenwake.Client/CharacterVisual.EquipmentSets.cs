using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    // Partner pieces repeat a readable material and emblem, without lights or gameplay emitters.
    private static void EquippedLanternCrown(Node3D parent)
    {
        Material ivory = SharedMaterial("dcd9bf", metallic: true), gilt = SharedMaterial("82794e", metallic: true), light = SharedMaterial("91dfee", emissive: true);
        Ring(parent, new(0, 2.18f, .015f), .205f, .26f, ivory);
        Ring(parent, new(0, 2.13f, .015f), .212f, .247f, gilt);
        for (int side = -1; side <= 1; side += 2)
        {
            Cone(parent, new(side * .215f, 2.29f, -.045f), .065f, .024f, .29f, ivory, new(0, 0, side * -17));
            Rod(parent, new(side * .21f, 2.19f, -.12f), new(side * .12f, 2.38f, -.20f), .026f, gilt);
            Orb(parent, new(side * .215f, 2.435f, -.045f), new(.065f, .09f, .065f), light);
        }
        VigilLantern(parent, new(0, 2.30f, -.27f), .72f, ivory, gilt, light);
    }

    private static void EquippedUnburiedVigil(Node3D parent)
    {
        Material cloth = SharedMaterial("293f50"), ivory = SharedMaterial("dcd9bf", metallic: true), gilt = SharedMaterial("82794e", metallic: true), light = SharedMaterial("91dfee", emissive: true);
        var torso = Cone(parent, new(0, 1.36f, 0), .30f, .395f, .65f, cloth); torso.Scale = new(1, 1, .76f);
        TaperedBox(parent, new(0, 1.39f, -.30f), new(.51f, .48f, .08f), ivory, .74f);
        TailoredCape(parent, new(0, 1.69f, .30f), .69f, 1.14f, cloth);
        for (int side = -1; side <= 1; side += 2)
        {
            for (int rib = 0; rib < 3; rib++)
                Rod(parent, new(side * .045f, 1.55f - rib * .12f, -.37f), new(side * (.245f - rib * .025f), 1.49f - rib * .12f, -.32f), .027f, gilt);
            TaperedBox(parent, new(side * .21f, .93f, -.19f), new(.18f, .40f, .07f), ivory, .75f, new(0, 0, side * 9));
            Rod(parent, new(side * .23f, .77f, -.25f), new(side * .17f, 1.09f, -.25f), .015f, light);
        }
        VigilLantern(parent, new(0, 1.43f, -.42f), 1, ivory, gilt, light);
    }

    private static void VigilLantern(Node3D parent, Vector3 at, float scale, Material ivory, Material gilt, Material light)
    {
        Orb(parent, at, new Vector3(.12f, .20f, .075f) * scale, light);
        for (int side = -1; side <= 1; side += 2)
            Rod(parent, at + new Vector3(side * .075f, -.12f, -.045f) * scale, at + new Vector3(side * .075f, .12f, -.045f) * scale, .014f * scale, gilt);
        Cone(parent, at + new Vector3(0, .13f, 0) * scale, .105f * scale, .035f * scale, .08f * scale, ivory);
        Cone(parent, at + new Vector3(0, -.13f, 0) * scale, .06f * scale, .095f * scale, .06f * scale, ivory);
    }

    private static void EquippedThornmotherMantle(Node3D parent, int side)
    {
        Material bark = SharedMaterial("4a3935"), leaf = SharedMaterial("52634b"), vein = SharedMaterial("a0b66b"), venom = SharedMaterial("b5e582", emissive: true);
        Orb(parent, new(side * .05f, -.04f, 0), new(.51f, .25f, .46f), bark);
        for (int i = 0; i < 3; i++)
        {
            float x = side * (.015f + i * .085f), y = .045f - i * .11f;
            var blade = Orb(parent, new(x, y, -.055f), new(.24f, .34f, .15f), leaf); blade.RotationDegrees = new(18, side * 12, side * -42);
            Rod(parent, new(x, y + .07f, -.14f), new(x + side * .08f, y - .10f, -.14f), .013f, vein);
            Cone(parent, new(side * (.11f + i * .075f), .13f + i * .018f, .04f), .063f, 0, .28f + i * .04f, bark, new(0, 0, side * -35));
        }
        Orb(parent, new(side * .08f, -.10f, -.255f), new(.10f, .15f, .07f), venom);
        Ring(parent, new(side * .08f, -.10f, -.225f), .075f, .09f, bark, new(90, 0, 0));
    }

    private static void EquippedGravegardenGrasp(Node3D parent, int side)
    {
        Material bark = SharedMaterial("4a3935"), leaf = SharedMaterial("52634b"), vein = SharedMaterial("a0b66b"), venom = SharedMaterial("b5e582", emissive: true);
        Cone(parent, new(side * .04f, -.46f, -.015f), .11f, .145f, .34f, bark);
        Orb(parent, new(side * .04f, -.65f, -.04f), new(.23f, .23f, .24f), bark);
        for (int i = 0; i < 3; i++)
        {
            var blade = Orb(parent, new(side * .045f, -.36f - i * .10f, -.15f), new(.22f, .18f, .055f), leaf); blade.RotationDegrees = new(0, 0, side * -23);
            Rod(parent, new(side * .04f - .08f, -.34f - i * .10f, -.19f), new(side * .04f + .08f, -.39f - i * .10f, -.19f), .012f, vein);
            Cone(parent, new(side * .04f + (i - 1) * .065f, -.61f, -.175f), .03f, 0, .12f, bark, new(-35, 0, 0));
        }
        Orb(parent, new(side * .04f, -.46f, -.205f), new(.07f, .105f, .045f), venom);
    }

    private static void EquippedCinderpilgrimGirdle(Node3D parent)
    {
        Material charred = SharedMaterial("342e31"), brass = SharedMaterial("ad8050", metallic: true), ember = SharedMaterial("ffab5a", emissive: true);
        var band = Cone(parent, new(0, 1.055f, 0), .365f, .355f, .15f, charred); band.Scale = new(1, 1, 1.02f);
        Ring(parent, new(0, 1.055f, -.395f), .095f, .145f, brass, new(90, 0, 0));
        Orb(parent, new(0, 1.055f, -.42f), new(.13f, .13f, .045f), ember);
        for (int side = -1; side <= 1; side += 2)
        {
            TaperedBox(parent, new(side * .245f, 1.05f, -.29f), new(.19f, .18f, .07f), brass, .8f, new(0, side * -30, 0));
            TailoredCape(parent, new(side * .22f, 1.01f, .10f), .16f, .47f, charred);
            Rod(parent, new(side * .23f, .60f, .15f), new(side * .24f, .83f, .13f), .018f, ember);
            for (int i = 0; i < 2; i++)
                Rod(parent, new(side * (.10f + i * .06f), 1.08f, -.355f), new(side * (.12f + i * .07f), .98f, -.36f), .012f, ember);
        }
    }

    private static void EquippedEndlessEmberBoot(Node3D parent, int side)
    {
        Material charred = SharedMaterial("342e31"), brass = SharedMaterial("ad8050", metallic: true), ember = SharedMaterial("ffab5a", emissive: true);
        Cone(parent, new(0, -.615f, 0), .13f, .17f, .32f, charred);
        TaperedBox(parent, new(0, -.81f, -.11f), new(.31f, .22f, .49f), charred, .85f);
        TaperedBox(parent, new(0, -.81f, -.28f), new(.30f, .13f, .17f), brass, .9f, new(-10, 0, 0));
        TaperedBox(parent, new(0, -.905f, -.11f), new(.32f, .04f, .50f), brass, .95f);
        Ring(parent, new(0, -.48f, 0), .13f, .18f, brass);
        Ring(parent, new(0, -.62f, -.155f), .046f, .07f, brass, new(90, 0, 0));
        Orb(parent, new(0, -.62f, -.174f), new(.074f, .074f, .028f), ember);
        for (int i = 0; i < 3; i++)
        {
            float z = -.28f + i * .13f;
            Rod(parent, new(side * .166f, -.86f, z), new(side * .166f, -.74f + i * .025f, z + .035f), .017f, ember);
        }
    }
}
