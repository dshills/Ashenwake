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
        art.Flush();
        if (encounterId is not ("campaign.bell_saint" or "room.bell_sanctum")) return null;
        var bell = BellSanctuaryVisual.Create(x, z, bellPhase, bossDefeated);
        parent.AddChild(bell);
        return bell;
    }

    private static void MemorialRoad(EnvironmentBuilder art, float x, float z)
    {
        // The old gate is visibly broken rather than suggesting another usable exit.
        Vector3 gate = new(-x * .18f, 0, -z - 2.1f);
        Arch(art, gate, 5.6f, 3.2f, 5.6f, broken: true);
        art.Box(new Vector3(1.1f, .56f, 1.3f), gate + new Vector3(2.4f, .42f, .15f), Stone, new(0, 15, -13));
        art.Box(new Vector3(.75f, .43f, 1.2f), gate + new Vector3(1.4f, .26f, .3f), Pale, new(0, -24, 7));
        art.Box(new Vector3(1.6f, .38f, .65f), gate + new Vector3(.3f, .2f, -.1f), Weathered, new(0, 23, -7));
        FuneralCloth(art, gate + new Vector3(-2.7f, 3.95f, .43f), 1.12f, 2.35f);

        for (int i = 0; i < 6; i++)
        {
            float px = -x + 1.2f + i * (2 * x - 2.4f) / 5;
            float pz = -z - 4.4f - i % 2 * .5f;
            Grave(art, new(px, 0, pz), .85f + i % 3 * .12f, i % 2 == 0);
            if (i % 2 == 0) Candle(art, new(px + .67f, 0, pz + .3f));
        }
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
            Vector3 origin = new(i * x * .65f, 0, -z - 1.55f);
            Arch(art, origin, width, 3.3f, i == 0 ? 6.15f : 5.6f, broken: i == 1);
            if (i != 1)
            {
                art.Box(new Vector3(.32f, 1.65f, .4f), origin + new Vector3(0, 3.52f, -.32f), Pale);
                art.Torus(.54f, .68f, origin + new Vector3(0, 4.57f, -.32f), Weathered, new(90, 0, 0));
            }
            FuneralCloth(art, origin + new Vector3(-width * .5f, 3.55f, .5f), .9f, 2.25f);
        }
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
        LowFragments(art, x, z);
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
