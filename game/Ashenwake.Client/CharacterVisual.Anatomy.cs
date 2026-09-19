using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    /// <summary>Installed fragments leave small physical marks even before a Manifestation becomes active.</summary>
    private void BuildAnatomy(int mask)
    {
        // Each fragment shares one module template across disciplines and loadouts. These static accents sit
        // outside the clothing envelope, so changing gear does not hide an implant or create new cache entries.
        if ((mask & 1) != 0)
            BuildModule(BodyRoot, "AnatomyEyes", "anatomy/vael-eyes", n =>
            {
                Material socket = SharedMaterial("594c40", metallic: true), lens = SharedMaterial("ffd79a", emissive: true);
                Ring(n, new(.078f, 2.032f, -.318f), .040f, .060f, socket, new(90, 0, 0));
                Orb(n, new(.078f, 2.032f, -.331f), new(.07f, .049f, .026f), lens);
                Rod(n, new(.035f, 2.085f, -.309f), new(.15f, 2.06f, -.294f), .013f, lens);
                Rod(n, new(.11f, 2.002f, -.31f), new(.17f, 1.95f, -.266f), .011f, lens);
            });
        if ((mask & 2) != 0)
            BuildModule(BodyRoot, "AnatomyHeart", "anatomy/serath-heart", n =>
            {
                Material setting = SharedMaterial("493832", metallic: true), gold = SharedMaterial("c99464", metallic: true);
                Material ember = SharedMaterial("f6b474", emissive: true), core = SharedMaterial("ffe7bc", emissive: true);
                Box(n, new(0, 1.455f, -.459f), new(.245f, .245f, .045f), setting, new(0, 0, 45));
                Ring(n, new(0, 1.455f, -.493f), .095f, .123f, gold, new(90, 0, 0));
                Orb(n, new(0, 1.47f, -.519f), new(.14f, .19f, .064f), ember);
                Orb(n, new(0, 1.49f, -.552f), new(.048f, .082f, .025f), core);
                Cone(n, new(0, 1.31f, -.50f), .047f, 0, .12f, ember, new(0, 0, 180));
                for (int side = -1; side <= 1; side += 2)
                {
                    Rod(n, new(side * .115f, 1.455f, -.489f), new(side * .18f, 1.51f, -.427f), .014f, gold);
                    Rod(n, new(side * .18f, 1.51f, -.427f), new(side * .21f, 1.60f, -.392f), .011f, ember);
                }
            });
        if ((mask & 4) != 0)
            BuildModule(BodyRoot, "AnatomySpine", "anatomy/ilyra-spine", n =>
            {
                Material nerve = SharedMaterial("689f92", metallic: true), lumen = SharedMaterial("ade5c5", emissive: true);
                Rod(n, new(0, 1.14f, .545f), new(0, 1.76f, .545f), .023f, nerve);
                for (int vertebra = 0; vertebra < 6; vertebra++)
                {
                    float y = 1.17f + vertebra * .104f;
                    Box(n, new(0, y, .55f), new(.09f, .046f, .051f), nerve, new(0, 0, 45));
                    Orb(n, new(0, y, .584f), new(.031f, .047f, .024f), lumen);
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    Rod(n, new(0, 1.75f, .54f), new(side * .135f, 1.78f, .16f), .017f, nerve);
                    Rod(n, new(side * .135f, 1.78f, .16f), new(side * .14f, 1.735f, -.22f), .017f, nerve);
                    Orb(n, new(side * .14f, 1.735f, -.242f), new(.052f, .07f, .036f), lumen);
                }
            });
        if ((mask & 8) != 0)
        {
            BuildArmAnatomy(_equipmentLeft, -1);
            BuildArmAnatomy(_equipmentRight, 1);
        }
    }

    private static void BuildArmAnatomy(Node3D joint, int side)
    {
        string hand = side < 0 ? "Left" : "Right";
        BuildModule(joint, "AnatomyArms" + hand, "anatomy/orrun-arms-" + hand.ToLowerInvariant(), n =>
        {
            Material bone = SharedMaterial("d5d1b5"), seam = SharedMaterial("b3d8cf", emissive: true);
            for (int plate = 0; plate < 3; plate++)
            {
                float y = -.32f - plate * .095f;
                Box(n, new(side * .055f, y, -.185f), new(.165f, .063f, .072f), bone, new(0, 0, side * -12));
            }
            Rod(n, new(side * .045f, -.30f, -.232f), new(side * .074f, -.54f, -.227f), .012f, seam);
            Orb(n, new(side * .075f, -.566f, -.206f), new(.058f, .066f, .037f), seam);
        });
    }
}
