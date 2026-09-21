using Godot;

namespace Ashenwake.Client;

/// <summary>
/// Serath's funeral architecture frames Act I without adding collision or entering the playable room.
/// The south and east edges stay low so the camera always has a clear view into combat.
/// </summary>
public static class GreyMarchArt
{
    private const string Stone = "87928b";
    private const string Pale = "c0c1a5";
    private const string Weathered = "4c6669";
    private const string Dark = "33474d";
    private const string Cloth = "657879";
    private const string Wood = "4e4a45";
    private const string Iron = "535650";
    private const string Bronze = "ad8b57";
    private const string Gold = "dec895";

    public static BellSanctuaryVisual? Build(Node3D parent, float x, float z, string encounterId, int bellPhase, bool bossDefeated = false)
    {
        var art = new EnvironmentBuilder(parent, "GreyMarchArchitecture");
        bool sanctum = encounterId is "campaign.bell_saint" or "room.bell_sanctum";
        bool monastery = encounterId is "campaign.monastery" or "room.cloister";
        switch (encounterId)
        {
            case "campaign.bell_saint":
            case "room.bell_sanctum":
                Sanctuary(art, x, z);
                break;
            case "campaign.monastery":
            case "room.cloister":
                Monastery(art, x, z);
                break;
            default:
                MemorialRoad(art, x, z);
                break;
        }
        ExitApproach(art, x);
        PeripheralScrub(art, x, z, monastery || sanctum);
        art.Flush();
        BuildLandscape(parent.GetNode<Node3D>("GreyMarchArchitecture"), x, z, monastery, sanctum);
        if (encounterId is not ("campaign.bell_saint" or "room.bell_sanctum")) return null;
        var bell = BellSanctuaryVisual.Create(x, z, bellPhase, bossDefeated);
        parent.AddChild(bell);
        return bell;
    }

    private static void MemorialRoad(EnvironmentBuilder art, float x, float z)
    {
        // The old gate is visibly broken rather than suggesting another usable exit.
        Vector3 gate = new(-x * .63f, 0, -z - 2.7f);
        Arch(art, gate, 5.6f, 3.2f, 5.6f, broken: true);
        art.Box(new Vector3(1.1f, .56f, 1.3f), gate + new Vector3(2.4f, .42f, .15f), Stone, new(0, 15, -13));
        art.Box(new Vector3(.75f, .43f, 1.2f), gate + new Vector3(1.4f, .26f, .3f), Pale, new(0, -24, 7));
        art.Box(new Vector3(1.6f, .38f, .65f), gate + new Vector3(.3f, .2f, -.1f), Weathered, new(0, 23, -7));
        FuneralCloth(art, gate + new Vector3(-2.7f, 3.95f, .43f), 1.12f, 2.35f);

        for (int i = 0; i < 6; i++)
        {
            float px = -x * .28f + i * x * .23f;
            float pz = -z - 2.35f - i % 3 * 1.15f - i * .13f;
            Grave(art, new(px, 0, pz), .85f + i % 3 * .12f, i % 2 == 0);
            if (i % 2 == 0) Candle(art, new(px + .67f, 0, pz + .3f));
        }
        // An interrupted aqueduct and cut bank give the road an oblique, receding skyline.
        BrokenWall(art, new(x * .10f, 0, -z - 7.1f), 7.4f, 2.4f, -14);
        Arch(art, new(-x - 3.8f, 0, -z - 1.8f), 4.2f, 3.1f, 4.9f, 28, true);
        FallenColumn(art, new(-x - 2.4f, .1f, -z * .18f), 2.7f, 19);
        Memorial(art, new(x * .7f, 0, -z - 2f), 1.1f);
        Memorial(art, new(-x - 1.65f, 0, -z * .45f), .9f);
        DeadTree(art, new(-x - 3.4f, 0, z * .24f), 1.05f);
        DeadTree(art, new(x * .78f, 0, -z - 5.5f), 1.18f);
        DeadTree(art, new(-x * .75f, 0, -z - 5.2f), .8f);
        for (int i = 0; i < 4; i++)
        {
            Grave(art, new(-x - 1.5f, 0, -z + 1.4f + i * (2 * z - 2.8f) / 3), .76f, i % 2 == 0);
            // Weathered roadside fragments mark a boundary without forming a foreground wall.
            art.Box(new Vector3(.8f, .24f + i % 2 * .08f, 1.35f), new(x + 1.1f, .12f, -z * .6f + i * z * .4f), Weathered, new(0, i * 13 - 20, 0));
        }
        LowFragments(art, x, z);
    }

    private static void Monastery(EnvironmentBuilder art, float x, float z)
    {
        float width = Math.Min(5.2f, x * .48f);
        for (int i = -1; i <= 1; i++)
        {
            Vector3 origin = new((i == 0 ? -.06f : i * .65f) * x, 0, -z - (i == 0 ? 3.05f : i < 0 ? 2.1f : 1.55f));
            Arch(art, origin, width, i == 1 ? 2.8f : 3.3f, i == 0 ? 6.15f : i < 0 ? 5.6f : 4.9f, broken: i == 1);
            if (i != 1)
            {
                art.Box(new Vector3(.32f, 1.65f, .4f), origin + new Vector3(0, 3.52f, -.32f), Pale);
                art.Torus(.54f, .68f, origin + new Vector3(0, 4.57f, -.32f), Weathered, new(90, 0, 0));
            }
            FuneralCloth(art, origin + new Vector3(-width * .5f, 3.55f, .5f), .9f, 2.25f);
        }
        RuinedCloister(art, x, z);
        // A rear burial aisle gives depth through the open tracery.
        for (int i = 0; i < 5; i++)
        {
            float px = -x * .82f + i * x * .41f;
            Sarcophagus(art, new(px, 0, -z - 4.1f));
            if (i % 2 == 0) Memorial(art, new(px, 0, -z - 5.45f), .8f);
        }
        foreach (float pz in new[] { -z * .48f, z * .22f })
        {
            Arch(art, new(-x - 1.6f, 0, pz), 4.4f, 3.25f, 5.55f, 90);
            Sarcophagus(art, new(-x - 3.1f, 0, pz));
            Candle(art, new(-x - .9f, 0, pz + 1.65f));
        }
        art.Box(new Vector3(1.5f, 1.2f, 1.2f), new(-x - 1.2f, .6f, -z - 1.4f), Weathered);
        art.Box(new Vector3(1.75f, .22f, 1.45f), new(-x - 1.2f, 1.29f, -z - 1.4f), Pale);
        for (int i = 0; i < 4; i++)
        {
            float pz = -z * .7f + i * z * .47f;
            art.Box(new Vector3(1.2f, .34f, 1.8f), new(x + 1.05f, .17f, pz), Weathered);
            art.Box(new Vector3(1.35f, .12f, 1.92f), new(x + 1.05f, .4f, pz), Stone);
        }
        LowFragments(art, x, z);
    }

    private static void Sanctuary(EnvironmentBuilder art, float x, float z)
    {
        Vector3 shrine = new(0, 0, -z - 3.3f);
        Arch(art, shrine, 7.7f, 5.5f, 9.15f);
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 pillar = shrine + new Vector3(side * 3.85f, 0, 0);
            art.Box(new Vector3(1.2f, 1.55f, 1.35f), pillar + Vector3.Up * .775f, Weathered);
            art.Box(new Vector3(1.45f, .22f, 1.55f), pillar + Vector3.Up * 1.63f, Pale);
            art.Box(new Vector3(.28f, 4.45f, .24f), pillar + new Vector3(side * .59f, 3.35f, .38f), Pale);
            art.Cylinder(.33f, 0, 1.25f, pillar + Vector3.Up * 6.2f, Pale);
            FuneralCloth(art, pillar + new Vector3(side * .12f, 4.4f, .7f), 1.15f, 2.6f);
            Arch(art, new(side * Math.Max(7.7f, x * .68f), 0, -z - 1.75f), 4.4f, 3.8f, 6.45f, broken: side > 0);
            Memorial(art, new(side * (x - .8f), 0, -z - 3.6f), 1.1f);
            // Ritual lights live on the peripheral memorials, never over a combat telegraph.
            var ritualBase = new Vector3(side * (x - 1.2f), 0, -z - 1.35f);
            art.Cylinder(.65f, .5f, .35f, ritualBase + Vector3.Up * .175f, Weathered);
            art.Torus(.36f, .43f, ritualBase + Vector3.Up * .42f, Bronze);
            Candle(art, ritualBase + new Vector3(0, .35f, 0));
        }

        // A timber yoke, bronze collar and separate dark mouth make the bell legible from above.
        art.Box(new Vector3(5.9f, .52f, .55f), shrine + new Vector3(0, 7.55f, .12f), Wood);
        art.Box(new Vector3(.3f, .76f, .68f), shrine + new Vector3(-2.4f, 7.5f, .12f), Iron);
        art.Box(new Vector3(.3f, .76f, .68f), shrine + new Vector3(2.4f, 7.5f, .12f), Iron);
        Arch(art, new(-x - 1.6f, 0, -z * .32f), 4.8f, 3.8f, 6.5f, 90, true);
        Sarcophagus(art, new(-x - 1.6f, 0, z * .52f));
        // Distant buttresses frame the bell rather than closing a new wall around the arena.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 p = new(side * x * .67f, 0, -z - 7.1f);
            art.Box(new(1.25f, 4.1f, 1.6f), p + Vector3.Up * 2.05f, Dark);
            art.Box(new(1.6f, .23f, 1.9f), p + Vector3.Up * 4.2f, Weathered);
            art.Cylinder(.66f, .12f, 1.8f, p + Vector3.Up * 5.12f, Stone);
            BrokenWall(art, p + new Vector3(side * 2.6f, 0, -.75f), 3.2f, 2.7f, side * 12);
        }
        LowFragments(art, x, z);
    }

    private static void RuinedCloister(EnvironmentBuilder art, float x, float z)
    {
        // The rear aisle sits on a raised terrace, visible through offset foreground arches.
        Vector3 rear = new(-x * .15f, 0, -z - 6.1f);
        art.Box(new(x * 1.55f, .65f, 2.5f), rear + Vector3.Up * .325f, Dark);
        art.Box(new(x * 1.58f, .16f, 2.65f), rear + Vector3.Up * .72f, Weathered);
        BrokenWall(art, rear + new Vector3(0, .80f, -1.1f), x * 1.5f, 2.45f, 0);
        for (int bay = 0; bay < 3; bay++)
        {
            Vector3 pillar = rear + new Vector3(-x * .53f + bay * x * .47f, .80f, -.7f);
            float height = bay == 2 ? 2.3f : 4.05f;
            art.Box(new(.64f, height, .82f), pillar + Vector3.Up * height * .5f, Stone);
            art.Box(new(.91f, .24f, 1.1f), pillar + Vector3.Up * height, Pale);
            if (bay != 2)
                art.Beam(pillar + Vector3.Up * height, pillar + new Vector3(x * .20f, height + 1.05f, 0), .36f, Weathered);
        }
        BrokenWall(art, new(-x - 3.1f, 0, -z * .08f), z * .9f, 1.8f, 90);
        FallenColumn(art, new(-x - 2.6f, .1f, z * .46f), 3.7f, 18);
        for (int stone = 0; stone < 4; stone++)
            art.Box(new(.70f, .18f + stone % 2 * .12f, 1.15f), new(x + 1.1f + stone % 2 * .27f, .17f, -z * .60f + stone * 1.55f),
                stone % 2 == 0 ? Stone : Weathered, new(0, stone * 23 - 18, 0));
    }

    private static void BrokenWall(EnvironmentBuilder art, Vector3 center, float length, float height, float yaw)
    {
        int columns = Math.Max(3, Mathf.CeilToInt(length / 1.05f));
        float width = length / columns;
        Vector3 At(float px, float py, float pz = 0) => center + new Vector3(px, py, pz).Rotated(Vector3.Up, Mathf.DegToRad(yaw));
        for (int column = 0; column < columns; column++)
        {
            if (column == columns / 2) continue;
            float top = height * (column % 3 == 0 ? .52f : column % 3 == 1 ? 1 : .76f);
            float px = -length * .5f + (column + .5f) * width;
            art.Box(new(width - .045f, top, .67f), At(px, top * .5f), column % 2 == 0 ? Weathered : Stone, new(0, yaw, 0));
            art.Box(new(width + .06f, .13f, .81f), At(px, top + .065f), Pale, new(0, yaw + column % 2 * 4, 0));
            if (column % 2 == 0)
                art.Box(new(width * .76f, .08f, .035f), At(px, Math.Min(.78f, top * .6f), .35f), Dark, new(0, yaw, 0));
        }
    }

    private static void FallenColumn(EnvironmentBuilder art, Vector3 p, float length, float yaw)
    {
        art.Cylinder(.35f, .29f, length, p + Vector3.Up * .37f, Stone, new(90, yaw, 0));
        for (int ring = -1; ring <= 1; ring++)
        {
            Vector3 at = p + new Vector3(0, .37f, ring * length * .37f).Rotated(Vector3.Up, Mathf.DegToRad(yaw));
            art.Torus(.27f, .39f, at, Pale, new(90, yaw, 0));
        }
    }

    private static void ExitApproach(EnvironmentBuilder art, float x)
    {
        // These low paired milestones frame the actual eastbound waypoint at (x - 1.4, 0).
        // No header or scenery crosses its clear approach, and both posts are beyond the Core bounds.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 p = new(x + 1.05f, 0, side * 1.9f);
            art.Box(new(.77f, .20f, .85f), p + Vector3.Up * .10f, Weathered);
            art.Box(new(.42f, side < 0 ? 1.15f : .76f, .48f), p + Vector3.Up * (side < 0 ? .775f : .58f), Stone, new(0, 0, side < 0 ? 0 : -7));
            art.Box(new(.035f, .38f, .20f), p + new Vector3(-.235f, .84f, 0), Dark);
            art.Torus(.105f, .14f, p + new Vector3(-.265f, .87f, 0), Bronze, new(0, 0, 90));
        }
    }

    private static void PeripheralScrub(EnvironmentBuilder art, float x, float z, bool enclosed)
    {
        // Small bramble silhouettes and roots break up the stone edge without disguising safe floor.
        for (int clump = 0; clump < 7; clump++)
        {
            Vector3 p = clump < 4 ? new(-x - 1.1f, 0, -z * .76f + clump * z * .46f)
                : new(-x * .58f + (clump - 4) * x * .56f, 0, -z - 1.02f);
            float height = enclosed ? .45f : .72f;
            for (int twig = -1; twig <= 1; twig++)
            {
                art.Beam(p + new Vector3(twig * .13f, 0, 0), p + new Vector3(twig * .27f, height - Math.Abs(twig) * .13f, -.12f), .045f, Wood);
                art.Beam(p + new Vector3(twig * .17f, height * .45f, -.04f), p + new Vector3(twig * .35f + .12f, height * .68f, .16f), .028f, Wood);
            }
        }
    }

    private static void BuildLandscape(Node3D architecture, float x, float z, bool monastery, bool sanctum)
    {
        ScenicRock[] rocks = sanctum ?
        [
            new(new(-x * .91f, -.3f, -z - 8.7f), new(7.5f, 5.4f, 6.6f), -13, 1),
            new(new(x * .92f, -.3f, -z - 9.4f), new(8.2f, 6.4f, 6.8f), 14, 3),
            new(new(-x - 4.9f, -.2f, -z * .34f), new(6.4f, 3.8f, 8.4f), 9, 5),
            new(new(x + 1.75f, -.1f, z * .48f), new(2.4f, .45f, 3), -12, 7)
        ] : monastery ?
        [
            new(new(-x - 5.1f, -.3f, -z - 3.5f), new(7.5f, 5.3f, 8.6f), -9, 2),
            new(new(-x * .53f, -.4f, -z - 10.3f), new(10.5f, 5.2f, 6.9f), 7, 4),
            new(new(x * .60f, -.3f, -z - 10.6f), new(9.8f, 3.7f, 7.1f), -12, 6),
            new(new(-x - 2.5f, -.1f, z * .64f), new(3.2f, .75f, 3.2f), 8, 1),
            new(new(x + 1.65f, -.1f, -z * .43f), new(2, .35f, 2.9f), 9, 3)
        ] :
        [
            new(new(-x - 4.9f, -.2f, -z * .36f), new(7.1f, 3.9f, 8), -8, 1),
            new(new(-x - 5.7f, -.3f, -z - 5.3f), new(8.5f, 6.2f, 8.3f), 11, 3),
            new(new(-x * .34f, -.3f, -z - 9.6f), new(9.9f, 5.8f, 6.8f), -14, 5),
            new(new(x * .63f, -.3f, -z - 8.9f), new(9.2f, 2.8f, 6.4f), 13, 7),
            new(new(-x - 2.7f, -.1f, z * .62f), new(3.5f, .62f, 3), -9, 2),
            new(new(x + 1.75f, -.1f, -z * .52f), new(2.4f, .39f, 3), -12, 4)
        ];
        ScenicTerrain.Build(architecture, rocks, Dark, Weathered, Stone,
        [
            new(new(-x * .08f, -.95f, -z - 4.1f), new(x * 2 + 3.2f, .88f, 7.6f)),
            new(new(-x - 2.4f, -.95f, -z * .09f), new(4.5f, .88f, z * 1.96f)),
            new(new(x + 1.12f, -.48f, -z * .56f), new(1.85f, .41f, 4.1f)),
            new(new(x + 1.12f, -.48f, z * .59f), new(1.85f, .41f, 4.2f)),
            new(new(x + 1.05f, -.48f, -1.9f), new(1.80f, .41f, 1.45f)),
            new(new(x + 1.05f, -.48f, 1.9f), new(1.80f, .41f, 1.45f)),
            new(new(-x * .50f, -.48f, z + 1.0f), new(x * .95f, .41f, 1.20f)),
            new(new(x * .57f, -.48f, z + 1.0f), new(x * .82f, .41f, 1.20f))
        ]);
    }

    private static void Arch(EnvironmentBuilder art, Vector3 origin, float width, float spring, float peak, float yaw = 0, bool broken = false)
    {
        Vector3 At(float px, float py, float pz = 0) => origin + new Vector3(px, py, pz).Rotated(Vector3.Up, Mathf.DegToRad(yaw));
        Vector3 rotation = new(0, yaw, 0);
        foreach (float side in new[] { -1f, 1f })
        {
            float height = broken && side > 0 ? spring * .64f : spring;
            art.Box(new Vector3(.8f, height, .8f), At(side * width * .5f, height * .5f), Stone, rotation);
            art.Box(new Vector3(1.12f, .28f, 1.15f), At(side * width * .5f, .14f), Weathered, rotation);
            art.Box(new Vector3(.96f, .22f, .97f), At(side * width * .5f, height), Pale, rotation);
            for (float py = .83f; py < height - .25f; py += .88f)
                art.Box(new Vector3(.83f, .055f, .84f), At(side * width * .5f, py), Weathered, rotation);
            if (broken && side > 0) continue;
            Vector3 low = At(side * width * .5f, spring);
            Vector3 middle = At(side * width * .32f, spring + (peak - spring) * .59f);
            Vector3 tip = At(broken ? -.42f : 0, broken ? peak - .28f : peak);
            art.Beam(low, middle, .62f, Stone);
            art.Beam(middle, tip, .59f, Pale);
            art.Beam(low + new Vector3(0, -.13f, 0), middle + new Vector3(0, -.13f, 0), .29f, Weathered);
        }
        if (!broken) art.Box(new Vector3(.55f, .75f, .8f), At(0, peak), Pale, rotation);
    }

    private static void FuneralCloth(EnvironmentBuilder art, Vector3 top, float width, float length)
    {
        art.Beam(top + new Vector3(-width * .6f, .08f, -.08f), top + new Vector3(width * .6f, .08f, -.08f), .065f, Iron);
        for (int i = 0; i < 4; i++)
        {
            float strip = length - (i == 1 ? .24f : i == 3 ? .48f : 0);
            float px = (i - 1.5f) * width * .25f;
            art.Box(new Vector3(width * .252f, strip, .035f), top + new Vector3(px, -strip * .5f, i % 2 * .028f), i % 2 == 0 ? Cloth : Weathered);
        }
        art.Box(new Vector3(.09f, length * .45f, .05f), top + new Vector3(0, -length * .36f, .065f), Pale);
        art.Torus(width * .12f, width * .18f, top + new Vector3(0, -length * .37f, .072f), Pale, new(90, 0, 0));
    }

    private static void Grave(EnvironmentBuilder art, Vector3 origin, float scale, bool tall)
    {
        art.Box(new Vector3(1.04f, .16f, 1.55f) * scale, origin + Vector3.Up * .08f * scale, Weathered);
        art.Box(new Vector3(.85f, .1f, 1.4f) * scale, origin + Vector3.Up * .2f * scale, Stone);
        art.Box(new Vector3(.66f, tall ? 1.15f : .72f, .22f) * scale, origin + new Vector3(0, tall ? .75f : .54f, -.48f) * scale, Stone);
        art.Box(new Vector3(.78f, .17f, .34f) * scale, origin + new Vector3(0, tall ? 1.37f : .96f, -.48f) * scale, Pale);
        art.Box(new Vector3(.055f, .4f, .03f) * scale, origin + new Vector3(0, .76f, -.36f) * scale, Pale);
        art.Box(new Vector3(.23f, .045f, .03f) * scale, origin + new Vector3(0, .81f, -.36f) * scale, Pale);
    }

    private static void Memorial(EnvironmentBuilder art, Vector3 origin, float scale)
    {
        art.Box(new Vector3(1.05f, .35f, 1.1f) * scale, origin + Vector3.Up * .175f * scale, Weathered);
        art.Box(new Vector3(.84f, .14f, .9f) * scale, origin + Vector3.Up * .42f * scale, Pale);
        art.Cylinder(.35f * scale, .22f * scale, 1.5f * scale, origin + Vector3.Up * 1.23f * scale, Stone);
        art.Cylinder(.26f * scale, .16f * scale, .46f * scale, origin + Vector3.Up * 2.16f * scale, Pale);
        art.Box(new Vector3(.24f, .28f, .06f) * scale, origin + new Vector3(0, 2.17f, .21f) * scale, Dark);
        art.Beam(origin + new Vector3(-.24f, 1.77f, .08f) * scale, origin + new Vector3(-.09f, 1.3f, .38f) * scale, .2f * scale, Pale);
        art.Beam(origin + new Vector3(.24f, 1.77f, .08f) * scale, origin + new Vector3(.09f, 1.3f, .38f) * scale, .2f * scale, Pale);
        art.Torus(.43f * scale, .49f * scale, origin + new Vector3(0, 2.2f, -.11f) * scale, Bronze, new(90, 0, 0));
    }

    private static void Sarcophagus(EnvironmentBuilder art, Vector3 origin)
    {
        art.Box(new Vector3(1.25f, .55f, 2.25f), origin + Vector3.Up * .275f, Weathered);
        art.Box(new Vector3(1.45f, .19f, 2.46f), origin + Vector3.Up * .625f, Pale);
        art.Box(new Vector3(.45f, .13f, 1.25f), origin + new Vector3(0, .79f, .15f), Stone);
        art.Cylinder(.22f, .22f, .14f, origin + new Vector3(0, .79f, -.66f), Stone);
        foreach (float side in new[] { -1f, 1f })
            art.Box(new Vector3(.06f, .2f, .86f), origin + new Vector3(side * .637f, .31f, 0), Dark);
    }

    private static void DeadTree(EnvironmentBuilder art, Vector3 origin, float scale)
    {
        Vector3 At(float px, float py, float pz) => origin + new Vector3(px, py, pz) * scale;
        art.Cylinder(.31f * scale, .13f * scale, 2.8f * scale, At(0, 1.4f, 0), Wood, new(0, 0, -7));
        art.Beam(At(.15f, 2.2f, 0), At(-.6f, 4.2f, -.2f), .19f * scale, Wood);
        art.Beam(At(-.32f, 3.37f, -.14f), At(-1.1f, 3.7f, .42f), .1f * scale, Wood);
        art.Beam(At(.1f, 1.9f, 0), At(1.35f, 3.25f, .6f), .16f * scale, Wood);
        art.Beam(At(.92f, 2.82f, .4f), At(1.1f, 3.85f, .72f), .09f * scale, Wood);
        art.Beam(At(.08f, 2.4f, 0), At(.4f, 3.5f, -1.1f), .13f * scale, Wood);
        art.Beam(At(.34f, 3.3f, -.9f), At(.96f, 3.57f, -1.33f), .07f * scale, Wood);
        FuneralCloth(art, At(-.93f, 3.62f, .44f), .4f * scale, 1.23f * scale);
    }

    private static void Candle(EnvironmentBuilder art, Vector3 origin)
    {
        art.Cylinder(.19f, .16f, .16f, origin + Vector3.Up * .08f, Iron);
        art.Cylinder(.085f, .07f, .4f, origin + Vector3.Up * .34f, Pale);
        art.Cylinder(.055f, 0, .17f, origin + Vector3.Up * .625f, Gold, glow: true);
    }

    private static void LowFragments(EnvironmentBuilder art, float x, float z)
    {
        for (int i = 0; i < 7; i++)
        {
            float px = -x * .87f + i * x * .29f;
            art.Box(new Vector3(1.2f + i % 2 * .4f, .18f + i % 3 * .06f, .58f), new(px, .1f, z + 1f), i % 2 == 0 ? Weathered : Stone, new(0, i % 3 * 13 - 15, 0));
        }
    }
}
