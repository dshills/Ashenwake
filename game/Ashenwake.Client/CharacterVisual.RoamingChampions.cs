using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    // Cosmetic aliases are supplied only by the champion encounter renderer; ordinary enemies retain their own models.
    private bool TryBuildRoamingChampion(string definitionId)
    {
        switch (definitionId)
        {
            case "champion.pilgrim": RoamingPilgrim(); return true;
            case "champion.rootwidow": RoamingRootwidow(); return true;
            case "champion.tithekeeper": RoamingTithekeeper(); return true;
            case "champion.nest": RoamingNest(); return true;
            default: return false;
        }
    }
    private void RoamingPilgrim()
    {
        Palette("555264", "b4a07c", "25232e", "a49c83", "d9c9a7", "797684", "dfd4b6"); Height = 3.45f;
        var torso = Joint(BodyRoot, new(0, 1.35f, 0), "sway", .25f);
        Cone(torso, new(0, .04f, .1f), .62f, .35f, 1.18f, Main);
        Box(torso, new(0, .58f, .08f), new(.83f, .56f, .57f), Dark, new(12, 0, 0));
        MonsterSkull(torso, new(0, 1.12f, -.24f), .55f, false);
        Cone(torso, new(0, 1.32f, .02f), .43f, .1f, .83f, Main, new(-12, 0, 0));
        // The burden is taller than its bearer: a split bronze bell strapped to a timber yoke.
        Box(torso, new(0, 1.02f, .5f), new(1.85f, .15f, .2f), Metal);
        foreach (float side in new[] { -1f, 1f })
        {
            Rod(torso, new(side * .7f, 1.03f, .5f), new(side * .56f, -.32f, .5f), .08f, Metal);
            var leg = Joint(BodyRoot, new(side * .27f, .91f, 0), side < 0 ? "left_leg" : "right_leg", .62f);
            Rod(leg, Vector3.Zero, new(side * .03f, -.7f, -.03f), .15f, Dark);
            Box(leg, new(side * .03f, -.82f, -.15f), new(.33f, .18f, .48f), Metal);
            var arm = Joint(torso, new(side * .5f, .59f, -.07f), side < 0 ? "left_arm" : "right_arm", .65f);
            Rod(arm, Vector3.Zero, new(side * .21f, -.71f, -.2f), .12f, Main);
            Orb(arm, new(side * .21f, -.74f, -.2f), new(.25f, .24f, .22f), Bone);
            for (int i = 0; i < 7; i++) Ring(arm, new(side * .21f, -.85f - i * .13f, -.2f), .047f, .077f, Metal, new(90, i % 2 * 90, 0));
            Cone(arm, new(side * .21f, -1.68f, -.2f), .2f, .07f, .24f, Metal);
        }
        Cone(torso, new(0, .13f, .66f), .78f, .38f, 1.05f, Metal);
        Ring(torso, new(0, -.37f, .66f), .65f, .81f, Accent);
        Box(torso, new(.12f, .07f, 1.34f), new(.035f, .85f, .035f), Dark, new(0, 0, -12));
        Cone(torso, new(0, -.46f, .66f), .12f, .09f, .23f, Bone);
    }
    private void RoamingRootwidow()
    {
        Palette("465a45", "bc667c", "202b29", "718164", "d4c8a0", "71865e", "b1df84"); Height = 2.7f;
        var body = Joint(BodyRoot, new(0, 1.05f, 0), "sway", .42f);
        Orb(body, new(0, .04f, .48f), new(1.56f, 1.15f, 1.85f), Main);
        Orb(body, new(0, .16f, -.49f), new(.8f, .72f, .87f), Skin);
        MonsterSkull(body, new(0, .49f, -.9f), .53f, true);
        for (int i = 0; i < 5; i++)
        {
            float angle = i * Mathf.Tau / 5;
            var bloom = new Vector3(Mathf.Cos(angle) * .52f, .59f + (i % 2) * .13f, .48f + Mathf.Sin(angle) * .52f);
            Orb(body, bloom, new(.5f, .58f, .55f), Accent);
            Cone(body, bloom + new Vector3(0, .24f, 0), .12f, 0, .39f, Bone);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 3; i++)
            {
                float z = -.65f + i * .61f;
                var leg = Joint(body, new(side * .49f, .03f, z), side < 0 ? "left_leg" : "right_leg", .3f + i * .08f);
                var knee = new Vector3(side * .74f, .35f, (i - 1) * .23f);
                Rod(leg, Vector3.Zero, knee, .11f, Main);
                Rod(leg, knee, new(side * 1.1f, -.98f, (i - 1) * .38f), .073f, Bone);
                Cone(leg, knee, .12f, 0, .35f, Bone, new(0, 0, -side * 24));
            }
            Rod(body, new(side * .17f, .21f, -.88f), new(side * .38f, -.23f, -1.3f), .07f, Bone);
            Orb(body, new(side * .2f, .6f, -1.07f), new(.1f, .1f, .07f), Glow);
        }
    }
    private void RoamingTithekeeper()
    {
        Palette("514e4b", "db914e", "202932", "89928f", "cfc1a0", "70605a", "ffb65d"); Height = 3.4f;
        var body = Joint(BodyRoot, new(0, 1.3f, 0), "sway", .14f);
        Box(body, new(0, .48f, .1f), new(1.25f, 1.15f, .8f), Metal);
        Box(body, new(0, .46f, -.34f), new(.87f, .79f, .1f), Dark);
        for (int i = 0; i < 4; i++) Box(body, new(0, .18f + i * .18f, -.407f), new(.66f, .06f, .035f), Glow);
        Box(body, new(0, 1.12f, 0), new(.57f, .48f, .53f), Main);
        Box(body, new(0, 1.14f, -.29f), new(.35f, .045f, .025f), Glow);
        Cone(body, new(0, 1.41f, 0), .37f, .18f, .22f, Metal);
        foreach (float side in new[] { -1f, 1f })
        {
            var leg = Joint(BodyRoot, new(side * .38f, .95f, .07f), side < 0 ? "left_leg" : "right_leg", .48f);
            Box(leg, new(0, -.35f, 0), new(.42f, .75f, .44f), Main);
            Box(leg, new(0, -.81f, -.16f), new(.53f, .26f, .68f), Metal);
            var arm = Joint(body, new(side * .85f, .78f, 0), side < 0 ? "left_arm" : "right_arm", .66f);
            Box(arm, Vector3.Zero, new(.61f, .44f, .77f), Metal, new(0, 0, -side * 10));
            Box(arm, new(side * .06f, -.57f, -.06f), new(.42f, .88f, .45f), Main);
            Box(arm, new(side * .06f, -1.01f, -.11f), new(.48f, .35f, .58f), Metal);
            Cone(body, new(side * .46f, 1.1f, .57f), .19f, .19f, 1.37f, Dark);
            Ring(body, new(side * .46f, 1.77f, .57f), .15f, .23f, Metal);
            Cone(body, new(side * .46f, 1.77f, .57f), .145f, .145f, .018f, Glow);
        }
        // Broad taxman's hammer balances the paired furnace chimneys.
        var weapon = Joint(body, new(.9f, -.26f, -.25f), "right_arm", .5f);
        Rod(weapon, new(0, -.68f, 0), new(0, .45f, 0), .07f, Dark);
        Box(weapon, new(0, .38f, 0), new(1f, .46f, .45f), Metal);
        Box(weapon, new(0, .38f, -.24f), new(.68f, .13f, .025f), Accent);
    }
    private void RoamingNest()
    {
        Palette("3c5342", "98566a", "27302c", "67735b", "c8bea0", "708760", "a4ce77"); Height = 1.25f;
        var root = Joint(BodyRoot, new(0, .25f, 0), "sway", .3f);
        Orb(root, new(0, .18f, 0), new(1.1f, .65f, 1.1f), Main);
        for (int i = 0; i < 5; i++)
        {
            float angle = i * Mathf.Tau / 5;
            var at = new Vector3(Mathf.Cos(angle) * .36f, .32f, Mathf.Sin(angle) * .36f);
            Orb(root, at, new(.36f, .55f, .36f), Accent);
            Rod(root, at, at * new Vector3(2, 0, 2) + new Vector3(0, -.22f, 0), .075f, Skin);
        }
        Orb(root, new(0, .6f, 0), new(.29f, .38f, .29f), Glow);
    }
}
