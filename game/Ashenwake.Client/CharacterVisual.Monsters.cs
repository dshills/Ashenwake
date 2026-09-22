using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    // Visual identity is selected from authored content ids. These meshes do not
    // supply collision, attack timing, targeting, or any other combat rules.
    private void BuildMonster(string definitionId, string role)
    {
        var id = definitionId.ToLowerInvariant();
        Palette("665858", "a55e4e", "24202b", "887268", "d8c9a2", "99918a", "ffc878");
        if (id.Contains("bell_saint"))
            MonsterBellSaint();
        else if (id.Contains("rootheart") || id.Contains("ilyra"))
            MonsterPlant(true);
        else if (id.Contains("furnace_spindle") || id.Contains("vael"))
            MonsterForgeBoss();
        else if (id.Contains("covenant_warden") || id.Contains("orrun"))
            MonsterGuard("oath", true);
        else if (id.Contains("breach_heart") || id.Contains("nhal"))
            MonsterBreachBoss();
        else if (id.Contains("serath"))
            MonsterBellSaint();
        else if (id.Contains("antler") || id.Contains("bell_beast") || role == "beast")
            MonsterBeast(id.Contains("antler"));
        else if (id.Contains("memory_archer"))
            MonsterArcher();
        else if (id.Contains("carnivorous_vine") || id.Contains("feeding_root") || id.Contains("brood_root"))
            MonsterPlant(false);
        else if (id.Contains("needle_swarm"))
            MonsterInsectSwarm();
        else if (id.Contains("bloom_carrier"))
            MonsterGhoul(true, false);
        else if (id.Contains("emberling"))
            MonsterEmberling();
        else if (id.Contains("hound") || role == "rusher")
            MonsterHound();
        else if (id.Contains("broken_bell") || role == "bell")
            MonsterBrokenBell();
        else if (role == "anchor")
            MonsterAnchor(id);
        else if (role == "armored")
            MonsterGuard(id.Contains("forge") || id.Contains("furnace") ? "forge" :
                id.Contains("bone") || id.Contains("oath") ? "oath" : "funeral", id.Contains("giant"));
        else if (role == "support" || role == "ranged")
            MonsterCaster(id.Contains("breach") || id.Contains("memory") ? "shadow" :
                id.Contains("contract") ? "oath" : "cinder");
        else if (role == "bellsaint")
            MonsterBellSaint();
        else
            MonsterGhoul(false, id.Contains("shadow") || id.Contains("echo"));
    }

    private void MonsterGhoul(bool bloom, bool shadow)
    {
        Palette(shadow ? "403448" : bloom ? "586341" : "777469", shadow ? "b49cdb" : bloom ? "c66573" : "a85942",
            "25262b", "66635b", "d7c9a4", shadow ? "756780" : "a3a28c", shadow ? "d4b6ff" : "ffb973");
        Height = bloom ? 2.6f : 2.2f;
        var body = Joint(BodyRoot, new(0, 1.05f, 0), "sway", 0.5f);
        Orb(body, new(0, 0.43f, -0.1f), new(0.79f, 0.93f, 0.65f), Main);
        Orb(body, new(0, 0.73f, 0.08f), new(0.7f, 0.58f, 0.67f), Skin);
        TaperedBox(body, new(0, 0.06f, 0), new(0.48f, 0.3f, 0.44f), Dark, .75f);
        // Scapulae and a torn burial wrap reinforce the bent, hungry silhouette.
        for (int side = -1; side <= 1; side += 2)
        {
            TaperedBox(body, new(side * .25f, .78f, .215f), new(.24f, .32f, .11f), Bone, .42f, new(-20, 0, side * -24));
            Rod(body, new(side * .075f, .79f, -.32f), new(side * .37f, .79f, -.12f), .052f, Bone);
            TaperedBox(body, new(side * .13f, -.18f - (side > 0 ? .035f : 0), -.18f), new(.22f, .42f, .065f),
                Main, .48f, new(-8, 0, side * -11));
        }
        for (var i = 0; i < 3; i++)
        {
            var y = 0.28f + i * 0.17f;
            Rod(body, new(-0.31f, y + 0.08f, -0.31f), new(0, y, -0.43f), 0.045f, Bone);
            Rod(body, new(0.31f, y + 0.08f, -0.31f), new(0, y, -0.43f), 0.045f, Bone);
        }
        Rod(body, new(0, 0.3f, -0.44f), new(0, 0.77f, -0.34f), 0.05f, Bone);
        var head = Joint(body, new(0, 0.86f, -0.4f), "jaw", 0.25f);
        MonsterSkull(head, Vector3.Zero, 0.55f, true);
        for (var side = -1; side <= 1; side += 2)
        {
            var leg = Joint(BodyRoot, new(side * 0.23f, 1.04f, 0), side < 0 ? "left_leg" : "right_leg", 0.85f);
            Rod(leg, Vector3.Zero, new(side * 0.08f, -0.46f, 0.15f), 0.13f, Main);
            Orb(leg, new(side * 0.08f, -0.46f, 0.15f), new(0.27f, 0.25f, 0.28f), Bone);
            Rod(leg, new(side * 0.08f, -0.46f, 0.15f), new(side * 0.03f, -0.89f, -0.1f), 0.09f, Bone);
            Box(leg, new(side * 0.03f, -0.96f, -0.21f), new(0.24f, 0.13f, 0.46f), Skin);
            var arm = Joint(body, new(side * 0.44f, 0.7f, -0.06f), side < 0 ? "left_arm" : "right_arm", 0.9f);
            Orb(arm, Vector3.Zero, new(0.33f, 0.37f, 0.32f), Skin);
            Rod(arm, new(0, -0.08f, 0), new(side * 0.18f, -0.51f, -0.08f), 0.11f, Main);
            Rod(arm, new(side * 0.18f, -0.51f, -0.08f), new(side * 0.24f, -0.91f, -0.42f), 0.085f, Bone);
            Orb(arm, new(side * 0.24f, -0.91f, -0.42f), new(0.22f, 0.29f, 0.22f), Skin);
            for (var claw = 0; claw < 3; claw++)
                Rod(arm, new(side * 0.24f + (claw - 1) * 0.075f, -1f, -0.46f),
                    new(side * 0.24f + (claw - 1) * 0.09f, -1.21f, -0.69f), 0.025f, Bone);
        }
        if (bloom)
        {
            for (var i = 0; i < 5; i++)
            {
                var angle = i * Mathf.Tau / 5;
                var at = new Vector3(Mathf.Cos(angle) * 0.31f, 0.95f + Mathf.Sin(angle) * 0.18f, 0.21f);
                Orb(body, at, new(0.44f, 0.55f, 0.37f), Accent);
                Cone(body, at + new Vector3(0, 0.25f, 0), 0.13f, 0.02f, 0.32f, Bone);
            }
        }
        else
            for (var i = 0; i < 3; i++)
                Cone(body, new(0, 0.42f + i * 0.2f, 0.24f), 0.09f, 0, 0.31f, Bone, new(35, 0, 0));
    }

    private void MonsterSkull(Node parent, Vector3 at, float size, bool openJaw)
    {
        Orb(parent, at, new(size * 0.86f, size, size * 0.8f), Bone);
        Box(parent, at + new Vector3(0, -size * 0.07f, -size * 0.365f),
            new(size * 0.64f, size * 0.22f, size * 0.14f), Dark);
        for (var side = -1; side <= 1; side += 2)
        {
            Box(parent, at + new Vector3(side * size * 0.19f, -size * 0.015f, -size * 0.45f),
                new(size * 0.15f, size * 0.105f, size * 0.07f), Glow);
            TaperedBox(parent, at + new Vector3(side * size * .18f, size * .095f, -size * .36f),
                new(size * .35f, size * .16f, size * .21f), Bone, .74f, new(0, 0, side * -13));
            TaperedBox(parent, at + new Vector3(side * size * .285f, -size * .22f, -size * .28f),
                new(size * .19f, size * .23f, size * .20f), Bone, .57f, new(0, 0, side * 15));
        }
        TaperedBox(parent, at + new Vector3(0, -size * .195f, -size * .424f),
            new(size * .105f, size * .16f, size * .055f), Dark, .18f);
        Box(parent, at + new Vector3(0, -size * 0.29f, -size * 0.32f),
            new(size * 0.49f, size * 0.18f, size * 0.24f), openJaw ? Dark : Bone);
        Box(parent, at + new Vector3(0, -size * (openJaw ? 0.46f : 0.4f), -size * 0.3f),
            new(size * 0.48f, size * 0.13f, size * 0.28f), Bone);
    }

    private void MonsterGuard(string kind, bool giant)
    {
        var forge = kind == "forge";
        var oath = kind == "oath";
        Palette(forge ? "443f3d" : oath ? "7e827a" : "505963", forge ? "db783d" : oath ? "a49568" : "8c4856",
            "25282e", forge ? "746358" : "9a9690", "e2d8b9", "a6aaa1", forge ? "ffae59" : "d7eedb");
        Height = giant ? 3.7f : 2.65f;
        var scale = giant ? 1.35f : 1;
        var root = new Node3D { Scale = Vector3.One * scale };
        BodyRoot.AddChild(root);
        var body = Joint(root, new(0, 1.16f, 0), "sway", 0.22f);
        Cone(body, new(0, 0.08f, 0), 0.46f, 0.34f, 0.56f, Dark);
        TaperedBox(body, new(0, 0.64f, 0), new(0.82f, 0.85f, 0.51f), Main, .72f);
        TaperedBox(body, new(0, 0.66f, -0.285f), new(0.64f, 0.48f, 0.14f), Metal, .78f);
        TaperedBox(body, new(0, 0.37f, -0.27f), new(0.52f, 0.18f, 0.14f), Metal, .84f);
        Box(body, new(0, 0.6f, -0.365f), new(0.105f, 0.56f, 0.025f), Accent);
        Box(body, new(0, 0.63f, -0.375f), new(0.42f, 0.095f, 0.03f), Accent);
        Box(body, new(0, 0.19f, -0.035f), new(0.76f, 0.13f, 0.56f), Metal);
        for (int side = -1; side <= 1; side += 2)
        {
            TaperedBox(body, new(side * .29f, .025f, -.15f), new(.23f, .38f, .30f), Main, .8f, new(0, 0, side * 13));
            Rod(body, new(side * .08f, .91f, -.29f), new(side * .30f, .82f, -.33f), .026f, Accent);
        }
        TaperedBox(body, new(0, -0.02f, -0.33f), new(0.32f, 0.55f, 0.11f), Accent, .64f);
        Cone(body, new(0, 1.12f, 0), 0.29f, 0.24f, 0.43f, Metal);
        Box(body, new(0, 1.13f, -0.27f), new(0.39f, 0.115f, 0.09f), Dark);
        Box(body, new(0, 1.13f, -0.324f), new(0.31f, 0.043f, 0.027f), Glow);
        Box(body, new(0, 1.03f, -0.30f), new(0.09f, 0.25f, 0.1f), Metal);
        if (oath)
        {
            Cone(body, new(-0.24f, 1.49f, 0), 0.11f, 0, 0.56f, Bone, new(0, 0, 25));
            Cone(body, new(0.24f, 1.49f, 0), 0.11f, 0, 0.56f, Bone, new(0, 0, -25));
        }
        else
            Box(body, new(0, 1.42f, 0.05f), new(0.12f, 0.25f, 0.45f), Accent);
        for (var side = -1; side <= 1; side += 2)
        {
            var leg = Joint(root, new(side * 0.26f, 1.06f, 0), side < 0 ? "left_leg" : "right_leg", 0.7f);
            Box(leg, new(0, -0.25f, 0), new(0.31f, 0.49f, 0.35f), Main);
            Box(leg, new(0, -0.5f, -0.065f), new(0.35f, 0.24f, 0.38f), Metal);
            Box(leg, new(0, -0.72f, 0), new(0.29f, 0.42f, 0.34f), Main);
            Box(leg, new(0, -0.94f, -0.115f), new(0.37f, 0.21f, 0.58f), Metal);
            var arm = Joint(body, new(side * 0.57f, 0.9f, 0), side < 0 ? "left_arm" : "right_arm", 0.45f);
            TaperedBox(arm, new(0, .005f, 0), new(.55f, .24f, .54f), Metal, .82f, new(0, 0, side * 12));
            TaperedBox(arm, new(side * .035f, -.17f, .01f), new(.45f, .17f, .48f), Main, .85f, new(0, 0, side * 12));
            Box(arm, new(0, -0.29f, 0), new(0.27f, 0.45f, 0.3f), Main);
            Box(arm, new(0, -0.64f, -0.075f), new(0.29f, 0.32f, 0.33f), Metal);
            Orb(arm, new(0, -0.87f, -0.09f), new(0.24f, 0.24f, 0.24f), Dark);
            if (side < 0)
            {
                TaperedBox(arm, new(-0.12f, -0.55f, -0.35f), new(0.57f, 0.94f, 0.12f), Metal, .64f);
                TaperedBox(arm, new(-0.12f, -0.54f, -0.425f), new(.46f, .79f, .04f), Main, .64f);
                Box(arm, new(-0.12f, -0.55f, -0.43f), new(0.44f, 0.08f, 0.04f), Metal);
                Box(arm, new(-0.12f, -0.55f, -0.44f), new(0.08f, 0.75f, 0.04f), Accent);
            }
            else
            {
                Rod(arm, new(0, -1.56f, -0.12f), new(0, 0.77f, -0.12f), 0.046f, Dark);
                Cone(arm, new(0, 0.94f, -0.12f), 0.14f, 0, 0.56f, Metal);
                if (forge || giant)
                    Box(arm, new(0, 0.61f, -0.12f), new(0.72f, 0.39f, 0.32f), Metal);
                else
                    Box(arm, new(0.13f, 0.59f, -0.12f), new(0.33f, 0.4f, 0.11f), Metal, new(0, 0, -25));
            }
        }
    }

    private void MonsterCaster(string kind)
    {
        var shadow = kind == "shadow";
        var oath = kind == "oath";
        Palette(shadow ? "514667" : oath ? "6b6552" : "824539", shadow ? "bc9dda" : oath ? "dfc88e" : "d68d55",
            "232333", "847c73", "d5c8ae", "a09b92", shadow ? "d8b7ff" : oath ? "ece2af" : "ffb768");
        Height = 2.7f;
        var body = Joint(BodyRoot, new(0, 1.1f, 0), "sway", 0.45f);
        Cone(body, new(0, -0.47f, 0.02f), 0.48f, 0.23f, 1.2f, Main);
        Cone(body, new(0, 0.38f, 0), 0.3f, 0.44f, 0.78f, Main);
        Box(body, new(0, 0.04f, -0.29f), new(0.17f, 1.54f, 0.05f), Accent);
        Box(body, new(0, 0.15f, -0.05f), new(0.6f, 0.1f, 0.54f), Dark);
        Orb(body, new(0, 0.96f, 0.025f), new(0.7f, 0.73f, 0.7f), Main);
        Box(body, new(0, 0.92f, -0.30f), new(0.4f, 0.43f, 0.16f), Dark);
        MonsterSkull(body, new(0, 0.9f, -0.33f), 0.36f, false);
        Cone(body, new(0, 1.38f, 0.07f), 0.23f, 0, 0.42f, Main, new(12, 0, 0));
        for (var side = -1; side <= 1; side += 2)
        {
            var arm = Joint(body, new(side * 0.42f, 0.67f, 0), side < 0 ? "left_arm" : "right_arm", 0.65f);
            Rod(arm, Vector3.Zero, new(side * 0.16f, -0.45f, -0.21f), 0.17f, Main);
            Cone(arm, new(side * 0.16f, -0.51f, -0.21f), 0.22f, 0.13f, 0.38f, Main);
            Orb(arm, new(side * 0.16f, -0.72f, -0.26f), new(0.21f, 0.23f, 0.22f), Bone);
            if (side > 0)
            {
                Rod(arm, new(0.17f, -1.65f, -0.3f), new(0.17f, 0.57f, -0.3f), 0.052f, Metal);
                Ring(arm, new(0.17f, 0.68f, -0.3f), 0.2f, 0.25f, Metal, new(90, 0, 0));
                Orb(arm, new(0.17f, 0.69f, -0.3f), new(0.28f, 0.34f, 0.25f), Glow);
                if (!oath)
                    Cone(arm, new(0.17f, 0.88f, -0.3f), 0.11f, 0, 0.4f, Accent, new(0, 0, -12));
            }
            else
            {
                Box(arm, new(-0.15f, -0.66f, -0.35f), new(0.39f, 0.12f, 0.35f), Bone, new(-20, 0, 0));
                Box(arm, new(-0.15f, -0.73f, -0.34f), new(0.44f, 0.07f, 0.4f), Dark, new(-20, 0, 0));
            }
        }
        for (var side = -1; side <= 1; side += 2)
            Box(BodyRoot, new(side * 0.22f, 0.08f, -0.1f), new(0.26f, 0.17f, 0.41f), Dark);
    }

    private void MonsterArcher()
    {
        Palette("666f72", "819c91", "283137", "9eaaa0", "e0d5b7", "919b91", "ccf5d8");
        Height = 2.55f;
        var body = Joint(BodyRoot, new(0, 1.05f, 0), "sway", 0.35f);
        Cone(body, new(0, 0.04f, 0.1f), 0.37f, 0.25f, 0.55f, Main);
        Orb(body, new(0, 0.6f, 0), new(0.62f, 0.9f, 0.4f), Dark);
        for (var i = 0; i < 4; i++)
            Box(body, new(0, 0.36f + i * 0.15f, -0.2f), new(0.48f - i * 0.035f, 0.055f, 0.055f), Bone);
        Rod(body, new(0, 0.28f, -0.23f), new(0, 0.86f, -0.23f), 0.043f, Bone);
        Orb(body, new(0, 1.12f, 0.04f), new(0.65f, 0.64f, 0.55f), Main);
        MonsterSkull(body, new(0, 1.1f, -0.16f), 0.48f, false);
        for (int side = -1; side <= 1; side += 2)
        {
            TaperedBox(body, new(side * .23f, 1.08f, -.12f), new(.12f, .47f, .15f), Main, .65f, new(0, 0, side * -14));
            TaperedBox(body, new(side * .31f, .78f, .03f), new(.25f, .23f, .38f), Main, .64f, new(0, 0, side * 21));
        }
        TailoredCape(body, new(0, 1.09f, .3f), .46f, 1.22f, Main);
        Rod(body, new(-.28f, .91f, -.13f), new(.22f, .19f, -.24f), .04f, Accent);
        var quiver = new Vector3(-0.23f, 0.62f, 0.33f);
        Cone(body, quiver, 0.15f, 0.19f, 0.66f, Dark, new(0, 0, 18));
        for (var i = 0; i < 3; i++)
        {
            var start = quiver + new Vector3((i - 1) * 0.07f, 0.05f, 0);
            Rod(body, start, start + new Vector3(-0.2f, 0.8f, 0), 0.018f, Bone);
            Box(body, start + new Vector3(-0.2f, 0.72f, 0), new(0.1f, 0.17f, 0.025f), Accent, new(0, 0, 18));
        }
        for (var side = -1; side <= 1; side += 2)
        {
            var leg = Joint(BodyRoot, new(side * 0.19f, 1.01f, 0), side < 0 ? "left_leg" : "right_leg", 0.75f);
            Rod(leg, Vector3.Zero, new(0, -0.44f, 0.025f), 0.08f, Bone);
            Orb(leg, new(0, -0.45f, 0.025f), new(0.22f, 0.2f, 0.22f), Main);
            Rod(leg, new(0, -0.48f, 0.025f), new(0, -0.85f, 0), 0.065f, Bone);
            Box(leg, new(0, -0.92f, -0.09f), new(0.22f, 0.16f, 0.37f), Main);
            var arm = Joint(body, new(side * 0.35f, 0.85f, 0), side < 0 ? "left_arm" : "right_arm", 0.4f);
            Rod(arm, Vector3.Zero, new(side * 0.14f, -0.27f, -0.12f), 0.065f, Bone);
            Rod(arm, new(side * 0.14f, -0.27f, -0.12f), new(side * 0.22f, -0.35f, -0.42f), 0.055f, Bone);
            Orb(arm, new(side * 0.22f, -0.35f, -0.42f), new(0.17f, 0.17f, 0.17f), Bone);
            if (side > 0)
            {
                Vector3[] bow = [new(0.22f, 0.39f, -0.4f), new(0.22f, 0.06f, -0.69f),
                    new(0.22f, -0.35f, -0.76f), new(0.22f, -0.76f, -0.69f), new(0.22f, -1.09f, -0.4f)];
                for (var i = 0; i < bow.Length - 1; i++)
                    Rod(arm, bow[i], bow[i + 1], 0.045f, Metal);
                Rod(arm, bow[0], bow[^1], 0.009f, Bone);
                Rod(arm, new(0.22f, -0.35f, -0.25f), new(0.22f, -0.35f, -1.06f), 0.023f, Bone);
                Cone(arm, new(0.22f, -0.35f, -1.12f), 0.072f, 0, 0.2f, Glow, new(-90, 0, 0));
            }
        }
    }

    private void MonsterBellSaint()
    {
        Palette("605954", "b19055", "26262c", "c4a66b", "e7d4aa", "a5a399", "ffe7a4");
        Height = 4.25f;
        var body = Joint(BodyRoot, new(0, 1.7f, 0), "sway", 0.26f);
        // A recognizable cathedral bell encloses the priest's torso. A dark
        // skirt and clapper read as the hollow opening from the isometric view.
        Cone(body, new(0, 0.22f, 0), 0.83f, 0.42f, 1.68f, Metal);
        Cone(body, new(0, -0.71f, 0), 1.02f, 0.81f, 0.2f, Accent);
        Cone(body, new(0, -0.81f, 0), 0.88f, 0.88f, 0.12f, Dark);
        Ring(body, new(0, -0.58f, 0), 0.75f, 0.86f, Accent);
        Ring(body, new(0, 0.94f, 0), 0.35f, 0.43f, Accent);
        // Broad cast ribs and inset lancets give the bell architectural weight.
        // These remain on its existing torso joint and use the same three metal/dark batches.
        for (int side = -1; side <= 1; side += 2)
        {
            Rod(body, new(side * .23f, .86f, -.415f), new(side * .49f, -.48f, -.66f), .039f, Accent);
            Rod(body, new(side * .44f, .77f, .21f), new(side * .78f, -.47f, .29f), .035f, Accent);
            Rod(body, new(side * .31f, .37f, -.555f), new(side * .34f, -.12f, -.695f), .036f, Dark);
            Rod(body, new(side * .31f, .37f, -.555f), new(side * .39f, .22f, -.58f), .030f, Dark);
            TaperedBox(body, new(side * .32f, -.60f, -.75f), new(.15f, .21f, .045f), Metal, .62f);
        }
        Box(body, new(0, 0.25f, -0.68f), new(0.12f, 1.22f, 0.055f), Dark);
        Box(body, new(0, 0.59f, -0.59f), new(0.5f, 0.11f, 0.055f), Dark);
        Cone(body, new(0, -1.16f, 0.05f), 0.52f, 0.24f, 0.84f, Main);
        Rod(body, new(0, -0.7f, -0.29f), new(0, -1.32f, -0.29f), 0.045f, Metal);
        Orb(body, new(0, -1.42f, -0.29f), new(0.25f, 0.3f, 0.25f), Accent);
        Rod(body, new(0, 0.9f, 0), new(0, 1.32f, 0), 0.14f, Bone);
        MonsterSkull(body, new(0, 1.45f, -0.09f), 0.57f, true);
        Cone(body, new(0, 1.83f, 0.035f), 0.33f, 0.12f, 0.49f, Accent);
        Ring(body, new(0, 2.11f, 0.08f), 0.23f, 0.3f, Metal, new(90, 0, 0));
        for (var side = -1; side <= 1; side += 2)
        {
            var arm = Joint(body, new(side * 0.53f, 0.79f, 0.02f), side < 0 ? "left_arm" : "right_arm", 0.6f);
            Orb(arm, Vector3.Zero, new(0.43f, 0.35f, 0.41f), Accent);
            Rod(arm, new(0, -0.02f, 0), new(side * 0.42f, -0.46f, -0.03f), 0.1f, Bone);
            Orb(arm, new(side * 0.42f, -0.46f, -0.03f), new(0.23f, 0.23f, 0.23f), Metal);
            Rod(arm, new(side * 0.42f, -0.46f, -0.03f), new(side * 0.56f, -1.04f, -0.29f), 0.075f, Bone);
            Box(arm, new(side * 0.56f, -1.13f, -0.29f), new(0.22f, 0.27f, 0.19f), Bone);
            for (var finger = 0; finger < 3; finger++)
                Rod(arm, new(side * 0.56f + (finger - 1) * 0.07f, -1.21f, -0.3f),
                    new(side * 0.56f + (finger - 1) * 0.09f, -1.49f, -0.41f), 0.023f, Bone);
            for (var i = 0; i < 4; i++)
                Ring(arm, new(side * 0.75f, -0.12f - i * 0.23f, 0.09f), 0.10f, 0.135f, Metal,
                    new(90, i % 2 * 90, 0));
            MonsterSmallBell(arm, new(side * 0.75f, -1.14f, 0.09f), 0.32f);
            var leg = Joint(BodyRoot, new(side * 0.24f, 0.8f, 0.09f), side < 0 ? "left_leg" : "right_leg", 0.4f);
            Rod(leg, Vector3.Zero, new(0, -0.55f, 0), 0.07f, Bone);
            Box(leg, new(0, -0.7f, -0.12f), new(0.25f, 0.18f, 0.43f), Dark);
        }
    }

    private void MonsterSmallBell(Node parent, Vector3 at, float size)
    {
        Cone(parent, at, size, size * 0.42f, size * 1.5f, Metal);
        Ring(parent, at + new Vector3(0, -size * 0.75f, 0), size * 0.87f, size * 1.09f, Accent);
        Orb(parent, at + new Vector3(0, -size * 0.8f, 0), Vector3.One * size * 0.41f, Dark);
    }

    private void MonsterBeast(bool antler)
    {
        Palette(antler ? "5e5548" : "635654", "a8675c", "29282d", "8d7f6c", "e1d4b4", "90897b", "f3b28f");
        Height = antler ? 3.65f : 3.25f;
        var body = Joint(BodyRoot, new(0, 1.45f, 0), "sway", 0.4f);
        Orb(body, new(0, 0.37f, 0.12f), new(1.39f, 1.57f, 1.06f), Main);
        Orb(body, new(0, 0.81f, -0.12f), new(1.61f, 0.85f, 0.97f), Skin);
        Orb(body, new(0, 0.25f, -0.41f), new(0.72f, 0.95f, 0.27f), Accent);
        for (var i = 0; i < 4; i++)
            Box(body, new(0, 0.05f + i * 0.17f, -0.54f), new(0.67f - i * 0.05f, 0.065f, 0.08f), Bone);
        var head = Joint(body, new(0, 1.15f, -0.44f), "jaw", 0.4f);
        Orb(head, new(0, 0.04f, 0), new(0.72f, 0.68f, 0.72f), Bone);
        Box(head, new(0, -0.19f, -0.43f), new(0.48f, 0.22f, 0.68f), Dark);
        Orb(head, new(0, -0.05f, -0.38f), new(0.48f, 0.29f, 0.62f), Bone);
        Box(head, new(0, -0.33f, -0.4f), new(0.48f, 0.11f, 0.65f), Bone);
        for (var side = -1; side <= 1; side += 2)
        {
            Orb(head, new(side * 0.26f, 0.12f, -0.25f), new(0.16f, 0.12f, 0.14f), Glow);
            Cone(head, new(side * 0.2f, -0.2f, -0.58f), 0.067f, 0, 0.27f, Bone, new(180, 0, 0));
            if (antler)
            {
                Rod(head, new(side * 0.26f, 0.26f, 0), new(side * 0.66f, 0.67f, 0.03f), 0.09f, Bone);
                Rod(head, new(side * 0.66f, 0.67f, 0.03f), new(side * 0.93f, 1.03f, -0.08f), 0.055f, Bone);
                Rod(head, new(side * 0.54f, 0.55f, 0.02f), new(side * 0.5f, 1.0f, -0.04f), 0.045f, Bone);
                Rod(head, new(side * 0.75f, 0.79f, 0), new(side * 1.03f, 0.86f, -0.03f), 0.033f, Bone);
            }
            else
                Cone(head, new(side * 0.38f, 0.48f, 0.08f), 0.15f, 0, 0.65f, Metal, new(-10, 0, -side * 24));
            var arm = Joint(body, new(side * 0.81f, 0.81f, -0.03f), side < 0 ? "left_arm" : "right_arm", 0.95f);
            Rod(arm, Vector3.Zero, new(side * 0.27f, -0.67f, -0.01f), 0.27f, Main);
            Orb(arm, new(side * 0.27f, -0.68f, -0.01f), new(0.44f, 0.43f, 0.44f), Skin);
            Rod(arm, new(side * 0.27f, -0.68f, -0.01f), new(side * 0.24f, -1.33f, -0.34f), 0.21f, Main);
            Orb(arm, new(side * 0.24f, -1.43f, -0.37f), new(0.45f, 0.43f, 0.48f), Skin);
            for (var i = 0; i < 3; i++)
                Rod(arm, new(side * 0.24f + (i - 1) * 0.15f, -1.58f, -0.49f),
                    new(side * 0.24f + (i - 1) * 0.17f, -1.91f, -0.63f), 0.05f, Bone);
            Cone(arm, new(side * 0.04f, 0.17f, 0), 0.15f, 0, 0.44f, Bone, new(0, 0, -side * 30));
            var leg = Joint(BodyRoot, new(side * 0.41f, 1.39f, 0.18f), side < 0 ? "left_leg" : "right_leg", 0.75f);
            Rod(leg, Vector3.Zero, new(side * 0.06f, -0.61f, 0.23f), 0.22f, Main);
            Rod(leg, new(side * 0.06f, -0.61f, 0.23f), new(0, -1.16f, -0.04f), 0.13f, Bone);
            Box(leg, new(0, -1.3f, -0.21f), new(0.4f, 0.21f, 0.65f), Dark);
        }
        for (var i = 0; i < 4; i++)
            Cone(body, new(0, 0.17f + i * 0.24f, 0.56f), 0.13f, 0, 0.38f, Bone, new(45, 0, 0));
    }

    private void MonsterHound()
    {
        Palette("62565c", "b16a52", "26282d", "aaa08e", "ddc8a3", "8d817e", "ffc68d");
        Height = 1.7f;
        var body = Joint(BodyRoot, new(0, 0.93f, 0.03f), "sway", 0.55f);
        Orb(body, Vector3.Zero, new(0.75f, 0.7f, 1.3f), Main);
        Orb(body, new(0, 0.15f, -0.48f), new(0.78f, 0.77f, 0.76f), Skin);
        Orb(body, new(0, 0.24f, -0.94f), new(0.53f, 0.48f, 0.69f), Bone);
        Box(body, new(0, 0.11f, -1.28f), new(0.37f, 0.14f, 0.4f), Dark);
        Box(body, new(0, -0.02f, -1.16f), new(0.35f, 0.11f, 0.56f), Bone);
        Ring(body, new(0, 0.14f, -0.66f), 0.28f, 0.36f, Metal, new(90, 0, 0));
        for (var side = -1; side <= 1; side += 2)
        {
            Orb(body, new(side * 0.235f, 0.35f, -1.05f), new(0.11f, 0.1f, 0.16f), Glow);
            Cone(body, new(side * 0.23f, 0.58f, -0.85f), 0.11f, 0, 0.35f, Main, new(0, 0, -side * 20));
            for (var front = 0; front < 2; front++)
            {
                var leg = Joint(body, new(side * 0.31f, -0.1f, front == 0 ? -0.43f : 0.47f),
                    (side < 0) != (front == 0) ? "left_leg" : "right_leg", 1.1f);
                Rod(leg, Vector3.Zero, new(side * 0.07f, -0.37f, 0.1f), 0.11f, Main);
                Rod(leg, new(side * 0.07f, -0.37f, 0.1f), new(0, -0.73f, -0.04f), 0.065f, Bone);
                Box(leg, new(0, -0.77f, -0.12f), new(0.23f, 0.14f, 0.35f), Dark);
            }
        }
        Rod(body, new(0, 0.02f, 0.56f), new(0, 0.29f, 1.02f), 0.075f, Main);
        Rod(body, new(0, 0.29f, 1.02f), new(0.14f, 0.46f, 1.22f), 0.045f, Bone);
        for (var i = 0; i < 3; i++)
            Cone(body, new(0, 0.43f, -0.12f + i * 0.24f), 0.09f, 0, 0.31f, Bone, new(25, 0, 0));
    }

    private void MonsterEmberling()
    {
        Palette("503a39", "b15339", "30262c", "9d694a", "e4c18c", "8a5c4a", "ffd680");
        Height = 1.65f;
        var body = Joint(BodyRoot, new(0, 0.68f, 0), "sway", 1.1f);
        Orb(body, new(0, 0.07f, 0.05f), new(0.83f, 0.9f, 0.65f), Main);
        Orb(body, new(0, 0.04f, -0.3f), new(0.42f, 0.45f, 0.19f), Glow);
        Orb(body, new(0, 0.56f, -0.1f), new(0.54f, 0.49f, 0.44f), Main);
        Box(body, new(0, 0.58f, -0.33f), new(0.39f, 0.11f, 0.045f), Dark);
        Box(body, new(0, 0.4f, -0.31f), new(0.24f, 0.07f, 0.05f), Glow);
        for (var side = -1; side <= 1; side += 2)
        {
            Orb(body, new(side * 0.13f, 0.6f, -0.36f), new(0.1f, 0.11f, 0.07f), Glow);
            Cone(body, new(side * 0.24f, 0.81f, -0.02f), 0.11f, 0, 0.39f, Bone, new(0, 0, -side * 30));
            var leg = Joint(BodyRoot, new(side * 0.19f, 0.64f, 0), side < 0 ? "left_leg" : "right_leg", 1.2f);
            Rod(leg, Vector3.Zero, new(side * 0.09f, -0.32f, 0.1f), 0.1f, Main);
            Rod(leg, new(side * 0.09f, -0.32f, 0.1f), new(side * 0.06f, -0.52f, -0.07f), 0.075f, Bone);
            Box(leg, new(side * 0.06f, -0.58f, -0.13f), new(0.22f, 0.12f, 0.3f), Dark);
            var arm = Joint(body, new(side * 0.43f, 0.3f, 0), side < 0 ? "left_arm" : "right_arm", 1.2f);
            Rod(arm, Vector3.Zero, new(side * 0.22f, -0.23f, -0.16f), 0.09f, Main);
            Cone(arm, new(side * 0.25f, -0.38f, -0.21f), 0.115f, 0, 0.26f, Bone, new(180, 0, 0));
        }
        for (var i = 0; i < 3; i++)
            Cone(body, new((i - 1) * 0.21f, 0.62f, 0.25f), 0.1f, 0, 0.36f, Accent, new(15, 0, (i - 1) * -20));
    }

    private void MonsterInsectSwarm()
    {
        Palette("68733e", "c9a061", "292d29", "7e835b", "d8d7a2", "82945a", "e7cf80");
        Height = 1.25f;
        for (var insect = 0; insect < 3; insect++)
        {
            var body = Joint(BodyRoot, new((insect - 1) * 0.48f, insect == 1 ? 0.67f : 0.43f,
                insect == 1 ? -0.25f : 0.18f), "sway", 1.2f + insect * 0.2f);
            Orb(body, new(0, 0, 0.12f), new(0.41f, 0.32f, 0.63f), Main);
            Orb(body, new(0, 0.02f, -0.24f), new(0.29f, 0.29f, 0.29f), Dark);
            Box(body, new(0, 0.08f, -0.385f), new(0.24f, 0.07f, 0.04f), Glow);
            for (var side = -1; side <= 1; side += 2)
            {
                for (var leg = 0; leg < 3; leg++)
                {
                    var z = -0.15f + leg * 0.16f;
                    Rod(body, new(side * 0.12f, 0, z), new(side * 0.31f, -0.1f, z + (leg - 1) * 0.12f), 0.025f, Bone);
                    Rod(body, new(side * 0.31f, -0.1f, z + (leg - 1) * 0.12f),
                        new(side * 0.36f, -0.38f, z + (leg - 1) * 0.19f), 0.018f, Dark);
                }
                Cone(body, new(side * 0.1f, -0.02f, -0.48f), 0.065f, 0, 0.31f, Bone, new(-90, 0, side * 15));
            }
            Cone(body, new(0, 0.27f, 0.2f), 0.08f, 0, 0.25f, Accent, new(20, 0, 0));
        }
    }

    private void MonsterPlant(bool boss)
    {
        Palette("536642", "b96b6d", "263e35", "7b7760", "dbd9a8", "7e9155", "f4ce84");
        Height = boss ? 3.9f : 2.45f;
        var root = new Node3D { Scale = Vector3.One * (boss ? 1.45f : 1) };
        BodyRoot.AddChild(root);
        var body = Joint(root, new(0, 0.2f, 0), "sway", 0.7f);
        for (var i = 0; i < 5; i++)
        {
            var angle = i * Mathf.Tau / 5;
            var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var elbow = direction * 0.56f + new Vector3(0, 0.08f, 0);
            Rod(body, new(0, 0.5f, 0), elbow, 0.13f, Main);
            Rod(body, elbow, direction * 0.87f + new Vector3(0, -0.12f, 0), 0.07f, Dark);
        }
        Rod(body, Vector3.Zero, new(-0.15f, 0.9f, 0.04f), 0.19f, Main);
        Rod(body, new(-0.15f, 0.9f, 0.04f), new(0.04f, 1.51f, -0.1f), 0.14f, Main);
        Orb(body, new(0, 1.66f, -0.18f), new(0.87f, 0.83f, 0.86f), Skin);
        Orb(body, new(0, 1.74f, -0.46f), new(0.68f, 0.66f, 0.39f), Dark);
        Orb(body, new(0, 1.72f, -0.64f), new(0.38f, 0.37f, 0.09f), Accent);
        for (var i = 0; i < 7; i++)
        {
            var angle = i * Mathf.Tau / 7;
            var at = new Vector3(Mathf.Cos(angle) * 0.48f, 1.74f + Mathf.Sin(angle) * 0.48f, -0.28f);
            Orb(body, at, new(0.44f, 0.47f, 0.25f), Accent);
            var tooth = new Vector3(Mathf.Cos(angle) * 0.27f, 1.74f + Mathf.Sin(angle) * 0.27f, -0.69f);
            Cone(body, tooth, 0.06f, 0, 0.2f, Bone, new(0, 0, i * 360f / 7 + 90));
        }
        for (var side = -1; side <= 1; side += 2)
        {
            var arm = Joint(body, new(side * 0.18f, 0.89f, 0), side < 0 ? "left_arm" : "right_arm", 0.8f);
            Rod(arm, Vector3.Zero, new(side * 0.61f, 0.28f, -0.12f), 0.09f, Main);
            Rod(arm, new(side * 0.61f, 0.28f, -0.12f), new(side * 0.73f, 0.72f, -0.18f), 0.055f, Dark);
            Orb(arm, new(side * 0.46f, 0.08f, 0), new(0.48f, 0.23f, 0.28f), Skin);
            Cone(arm, new(side * 0.73f, 0.8f, -0.18f), 0.1f, 0, 0.3f, Bone);
        }
        if (boss)
        {
            Orb(body, new(0, 0.62f, -0.19f), new(0.55f, 0.75f, 0.4f), Accent);
            Orb(body, new(0, 0.68f, -0.39f), new(0.29f, 0.33f, 0.16f), Glow);
            for (var side = -1; side <= 1; side += 2)
                Rod(body, new(side * 0.21f, 0.94f, -0.19f), new(side * 0.23f, 0.41f, -0.24f), 0.055f, Bone);
        }
    }

    private void MonsterAnchor(string id)
    {
        var shadow = id.Contains("absence") || id.Contains("memory");
        Palette(shadow ? "4a3e5a" : "625961", shadow ? "a997bf" : "a37991", "252532",
            "9a8d80", "d9cdb7", "a49a9c", shadow ? "dac3fa" : "f2cae8");
        Height = 2.05f;
        Cone(BodyRoot, new(0, 0.12f, 0), 0.43f, 0.34f, 0.24f, Dark);
        var body = Joint(BodyRoot, new(0, 0.3f, 0), "sway", 0.2f);
        Cone(body, new(0, 0.46f, 0), 0.27f, 0.14f, 0.95f, Main);
        Box(body, new(0, 0.57f, -0.21f), new(0.07f, 0.72f, 0.04f), Accent);
        MonsterSkull(body, new(0, 1.01f, 0), 0.42f, true);
        Ring(body, new(0, 1.14f, 0.06f), 0.36f, 0.42f, Metal, new(90, 0, 0));
        Orb(body, new(0, 0.59f, -0.25f), new(0.18f, 0.22f, 0.17f), Glow);
        for (var side = -1; side <= 1; side += 2)
        {
            Rod(body, new(side * 0.14f, 0.84f, 0), new(side * 0.38f, 0.53f, -0.03f), 0.06f, Bone);
            Rod(body, new(side * 0.38f, 0.53f, -0.03f), new(side * 0.3f, 1.03f, -0.21f), 0.045f, Bone);
            Cone(body, new(side * 0.3f, 1.19f, -0.21f), 0.11f, 0, 0.28f, Glow);
            for (var i = 0; i < 3; i++)
                Ring(body, new(side * 0.3f, 0.14f + i * 0.17f, 0.04f), 0.065f, 0.088f, Metal,
                    new(90, i % 2 * 90, 0));
        }
    }

    private void MonsterBrokenBell()
    {
        Palette("74654b", "b69258", "352d2a", "bea473", "d8c49a", "928573", "ffe7a6");
        Height = 1.5f;
        var body = Joint(BodyRoot, new(0, 0.8f, 0), "sway", 1.2f);
        MonsterSmallBell(body, Vector3.Zero, 0.49f);
        Ring(body, new(0, 0.52f, 0), 0.14f, 0.2f, Metal, new(90, 0, 0));
        Box(body, new(0, 0.05f, -0.41f), new(0.05f, 0.57f, 0.045f), Dark, new(0, 0, -15));
        Orb(body, new(0, -0.44f, 0), new(0.24f, 0.2f, 0.24f), Glow);
    }

    private void MonsterForgeBoss()
    {
        Palette("434345", "cc753f", "242730", "938376", "cbbc96", "736d63", "ffcf7e");
        Height = 4.0f;
        var body = Joint(BodyRoot, new(0, 1.5f, 0), "sway", 0.22f);
        Cone(body, new(0, 0.55f, 0), 0.63f, 0.79f, 1.54f, Main);
        Orb(body, new(0, 0.53f, -0.63f), new(0.81f, 0.79f, 0.22f), Glow);
        Ring(body, new(0, 0.53f, -0.74f), 0.31f, 0.43f, Metal, new(90, 0, 0));
        for (var i = -1; i <= 1; i++)
            Box(body, new(i * 0.17f, 0.54f, -0.77f), new(0.055f, 0.7f, 0.07f), Dark);
        Box(body, new(0, -0.29f, 0), new(0.81f, 0.36f, 0.62f), Metal);
        Cone(body, new(0, 1.55f, -0.035f), 0.37f, 0.26f, 0.6f, Metal);
        Box(body, new(0, 1.57f, -0.35f), new(0.52f, 0.15f, 0.075f), Dark);
        Box(body, new(0, 1.57f, -0.39f), new(0.43f, 0.045f, 0.04f), Glow);
        for (var side = -1; side <= 1; side += 2)
        {
            Cone(body, new(side * 0.46f, 1.4f, 0.34f), 0.15f, 0.2f, 1.0f, Metal);
            Cone(body, new(side * 0.46f, 1.91f, 0.34f), 0.145f, 0.145f, 0.035f, Dark);
            Cone(body, new(side * 0.46f, 2.14f, 0.34f), 0.11f, 0, 0.46f, Accent, new(0, 0, side * 10));
            var arm = Joint(body, new(side * 0.82f, 0.98f, 0), side < 0 ? "left_arm" : "right_arm", 0.55f);
            Orb(arm, Vector3.Zero, new(0.59f, 0.62f, 0.62f), Metal);
            Rod(arm, new(0, -0.05f, 0), new(side * 0.22f, -0.6f, 0), 0.22f, Main);
            Ring(arm, new(side * 0.22f, -0.55f, 0), 0.17f, 0.26f, Accent);
            Box(arm, new(side * 0.22f, -0.94f, -0.05f), new(0.49f, 0.58f, 0.48f), Metal);
            Box(arm, new(side * 0.22f, -1.27f, -0.08f), new(0.43f, 0.24f, 0.49f), Dark);
            if (side > 0)
            {
                Rod(arm, new(0.22f, -1.77f, -0.17f), new(0.22f, 0.2f, -0.17f), 0.07f, Metal);
                Box(arm, new(0.22f, 0.22f, -0.17f), new(0.94f, 0.55f, 0.48f), Main);
                Box(arm, new(0.22f, 0.22f, -0.43f), new(0.59f, 0.13f, 0.04f), Accent);
            }
            var leg = Joint(BodyRoot, new(side * 0.37f, 1.28f, 0), side < 0 ? "left_leg" : "right_leg", 0.55f);
            Box(leg, new(0, -0.35f, 0), new(0.43f, 0.68f, 0.5f), Main);
            Orb(leg, new(0, -0.68f, -0.05f), new(0.46f, 0.37f, 0.52f), Metal);
            Box(leg, new(0, -0.95f, 0), new(0.36f, 0.48f, 0.47f), Metal);
            Box(leg, new(0, -1.15f, -0.19f), new(0.49f, 0.23f, 0.78f), Main);
        }
    }

    private void MonsterBreachBoss()
    {
        Palette("4e4263", "9a7eae", "262132", "8b7a98", "d2c1d9", "7a648f", "ebc8ff");
        Height = 4.1f;
        var body = Joint(BodyRoot, new(0, 1.55f, 0), "sway", 0.7f);
        Cone(body, new(0, -0.68f, 0.08f), 0.23f, 0.66f, 1.54f, Main);
        Orb(body, new(0, 0.41f, 0), new(1.11f, 1.38f, 0.65f), Dark);
        Orb(body, new(0, 0.5f, -0.34f), new(0.52f, 0.63f, 0.17f), Accent);
        Ring(body, new(0, 0.5f, -0.42f), 0.21f, 0.31f, Metal, new(90, 0, 0));
        Orb(body, new(0, 0.5f, -0.48f), new(0.15f, 0.35f, 0.065f), Glow);
        MonsterSkull(body, new(0, 1.41f, -0.07f), 0.7f, true);
        Ring(body, new(0, 1.48f, 0.1f), 0.72f, 0.79f, Accent, new(90, 0, 0));
        for (var side = -1; side <= 1; side += 2)
        {
            Cone(body, new(side * 0.25f, 2.01f, 0.03f), 0.13f, 0, 0.7f, Bone, new(0, 0, -side * 17));
            for (var pair = 0; pair < 2; pair++)
            {
                var arm = Joint(body, new(side * 0.47f, 0.88f - pair * 0.49f, pair * 0.15f),
                    side < 0 ? "left_arm" : "right_arm", 0.7f + pair * 0.2f);
                Rod(arm, Vector3.Zero, new(side * 0.48f, -0.42f, -0.07f), 0.1f, Main);
                Rod(arm, new(side * 0.48f, -0.42f, -0.07f), new(side * 0.62f, -1.03f, -0.35f), 0.065f, Bone);
                Orb(arm, new(side * 0.62f, -1.12f, -0.35f), new(0.23f, 0.3f, 0.22f), Bone);
                for (var i = 0; i < 3; i++)
                    Rod(arm, new(side * 0.62f + (i - 1) * 0.075f, -1.18f, -0.36f),
                        new(side * 0.65f + (i - 1) * 0.1f, -1.48f, -0.49f), 0.022f, Bone);
            }
        }
        for (var i = 0; i < 3; i++)
            Box(body, new(0, 0.18f + i * 0.22f, -0.32f), new(0.99f - i * 0.08f, 0.05f, 0.1f), Metal);
    }
}
