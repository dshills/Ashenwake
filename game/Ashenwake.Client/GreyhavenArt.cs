using Godot;

namespace Ashenwake.Client;

/// <summary>Greyhaven's inhabited perimeter; all architecture sits beyond the simulation's walkable bounds.</summary>
public static class GreyhavenArt
{
    private const string Stone = "64706d";
    private const string StoneLight = "87918a";
    private const string StoneDark = "424e4e";
    private const string Timber = "55473b";
    private const string TimberLight = "8c7457";
    private const string Iron = "303b40";
    private const string Bronze = "ad8a57";
    private const string Cloth = "397e7a";
    private const string ClothLight = "629a8c";
    private const string Warm = "f5be65";

    public static void Build(Node3D parent, float x, float z, int hubStage)
    {
        var b = new EnvironmentBuilder(parent, "GreyhavenArchitecture");
        float street = -z - 3.05f;
        float forgeX = Mathf.Min(x * .22f, 3f);
        float lodgeX = -Mathf.Min(x * .60f, 7.2f);
        float healerX = Mathf.Min(x * .71f, 8.5f);

        // Unequal rooflines and gaps make the settlement a cluster of inhabited places.
        House(b, new(forgeX, 0, street - .3f), 4.4f, 3.6f, 2.9f, "485c62", -.85f, false);
        House(b, new(lodgeX, 0, street - .55f), 4.7f, 4f, 3.75f, "405c64", -.3f, true);
        House(b, new(healerX, 0, street + .1f), 3.5f, 3.45f, 2.65f, "586b60", -.65f, true);

        Forge(b, new(forgeX + 1.2f, 0, -z - .97f));
        Chimney(b, new(forgeX - 1.38f, 0, street - .1f), 5.5f);
        Chimney(b, new(lodgeX + 1.3f, 0, street - 1f), 6.2f);
        ShrineCourt(b, new(lodgeX, 0, -z - 1.05f));
        WorkshopYard(b, new(forgeX, 0, -z - 1.15f));

        // A covered apothecary window, hanging herbs and stacked pots identify the smaller house.
        float healerFront = street + .1f + 1.725f;
        b.Box(new(1.35f, .15f, .72f), new(healerX + .6f, 1.07f, healerFront + .25f), TimberLight);
        b.Box(new(1.75f, .12f, .85f), new(healerX + .55f, 2.55f, healerFront + .23f), Cloth, new(9, 0, 0));
        for (int i = 0; i < 3; i++)
        {
            float herbX = healerX + .03f + i * .43f;
            b.Beam(new(herbX, 2.43f, healerFront + .36f), new(herbX, 2.14f, healerFront + .36f), .025f, TimberLight);
            b.Cylinder(.13f, .075f, .35f, new(herbX, 2.01f, healerFront + .36f), i == 1 ? "817b79" : "607e65");
            b.Cylinder(.14f, .17f, .24f, new(herbX, 1.26f, healerFront + .3f), i == 1 ? "ac8060" : "647e7c");
        }

        // The restored funerary gateway faces across the plaza without closing its walkable edge.
        Gate(b, new(-x - 1.1f, 0, -z * .22f));
        Shrine(b, new(-x - 1.45f, 0, -z * .74f));
        LanternPost(b, new(-x - .72f, 0, z * .37f), 2.6f);
        LanternPost(b, new(x + .72f, 0, -z * .65f), 2.6f);

        Barrel(b, new(forgeX - 2.4f, 0, -z - 1.1f), .43f, 1.1f);
        Barrel(b, new(forgeX - 3.05f, 0, -z - 1.75f), .36f, .85f);
        Woodpile(b, new(forgeX - 1.25f, 0, -z - .72f));
        Crate(b, new(lodgeX + 2.95f, .42f, -z - 1.05f), new(.8f, .84f, .78f));
        Crate(b, new(lodgeX + 3.15f, 1.08f, -z - 1.1f), new(.56f, .48f, .54f));
        Barrel(b, new(healerX + 2.22f, 0, -z - 1.55f), .39f, .92f);
        MarketQuay(b, x, z);
        Wayposts(b, x);
        Hillside(b, x, z);

        // Keep the camera-facing edges below knee height so the plaza stays legible.
        Bench(b, new(-x * .52f, 0, z + .78f));
        b.Box(new(1.35f, .28f, .64f), new(x + .83f, .14f, z * .33f), StoneDark);
        b.Box(new(1.48f, .1f, .72f), new(x + .86f, .33f, z * .33f), StoneLight);
        for (int i = 0; i < Mathf.Clamp(hubStage, 0, 3); i++)
        {
            float storageZ = z * .12f + i * 1.32f;
            Crate(b, new(-x - .83f, .36f, storageZ), new(.82f, .72f, .78f));
            b.Box(new(.9f, .04f, .8f), new(-x - .83f, .74f, storageZ), i % 2 == 0 ? Cloth : ClothLight);
        }

        b.Flush();
        ScenicTerrain.Build(parent.GetNode<Node3D>("GreyhavenArchitecture"),
        [
            new(new(-x - 4.7f, -.2f, -z * .58f), new(6.2f, 4.1f, 8), -9, 1),
            new(new(-x - 5.7f, -.3f, -z - 4.4f), new(8, 6.4f, 8), 12, 2),
            new(new(-x * .56f, -.3f, -z - 9.7f), new(9.5f, 6.6f, 7.3f), -12, 3),
            new(new(x * .15f, -.3f, -z - 10.6f), new(8.5f, 4.7f, 6.9f), 8, 4),
            new(new(x * .81f, -.3f, -z - 8.8f), new(7.4f, 3.1f, 5.8f), 18, 5),
            new(new(-x - 2.1f, -.15f, z * .62f), new(2.8f, .6f, 3.2f), -11, 6),
            new(new(x + 1.35f, -.12f, -z * .43f), new(1.7f, .35f, 2.8f), 8, 7)
        ], StoneDark, Stone, StoneLight,
        [
            new(new(0, -.92f, -z - 3.25f), new(x * 2 + 3.8f, .86f, 6.1f)),
            new(new(-x - 2.0f, -.92f, -z * .22f), new(3.6f, .86f, z * 1.7f)),
            new(new(x * .23f, -.57f, z + 1.0f), new(x * .77f, .51f, 1.55f)),
            new(new(-x * .52f, -.57f, z + .91f), new(3.35f, .51f, 1.2f)),
            new(new(x + 1.50f, -.69f, z * .64f), new(2.9f, .63f, 4.0f)),
            new(new(x + .91f, -.57f, z * .33f), new(1.78f, .51f, 1.35f)),
            new(new(x + .92f, -.57f, -1.9f), new(1.65f, .51f, 1.5f)),
            new(new(x + .92f, -.57f, 1.9f), new(1.65f, .51f, 1.5f))
        ]);
    }

    internal static Vector3[] SmokeAnchors(float x, float z)
        => [new(Mathf.Min(x * .22f, 3f) - 1.38f, 5.58f, -z - 3.15f),
            new(-Mathf.Min(x * .60f, 7.2f) + 1.3f, 6.28f, -z - 4.05f)];

    private static void ShrineCourt(EnvironmentBuilder b, Vector3 p)
    {
        // The pale, candle-lined frontage visually extends Mara's western court.
        for (int step = 0; step < 3; step++)
            b.Box(new(4.5f - step * .34f, .11f, .30f), p + new Vector3(0, .055f + step * .105f, -step * .30f), StoneLight);
        foreach (float side in new[] { -1f, 1f })
        {
            b.Cylinder(.31f, .26f, .65f, p + new Vector3(side * 1.86f, .325f, -.30f), Stone);
            b.Torus(.24f, .32f, p + new Vector3(side * 1.86f, .69f, -.30f), Bronze);
            for (int candle = 0; candle < 3; candle++)
            {
                Vector3 at = p + new Vector3(side * 1.86f + (candle - 1) * .13f, .83f, -.30f);
                b.Cylinder(.035f, .035f, .23f, at, StoneLight);
                b.Cylinder(.028f, 0, .085f, at + Vector3.Up * .16f, Warm, glow: true);
            }
        }
        b.Torus(.45f, .53f, p + new Vector3(0, 4.1f, -.98f), Bronze, new(90, 0, 0));
        b.Box(new(.095f, 1.30f, .09f), p + new Vector3(0, 4.10f, -.96f), StoneLight);
        b.Box(new(.72f, .095f, .09f), p + new Vector3(0, 4.18f, -.96f), StoneLight);
    }

    private static void WorkshopYard(EnvironmentBuilder b, Vector3 p)
    {
        // Long work surfaces, stacked slate and hanging tools distinguish the forge from the shrine.
        b.Box(new(2.1f, .14f, .57f), p + new Vector3(-1.3f, 1.10f, -.25f), TimberLight);
        foreach (float side in new[] { -1f, 1f })
            b.Box(new(.14f, 1.03f, .43f), p + new Vector3(-1.3f + side * .84f, .52f, -.25f), Timber);
        for (int tool = 0; tool < 3; tool++)
        {
            Vector3 at = p + new Vector3(-2.0f + tool * .49f, 1.40f, -.57f);
            b.Beam(at, at + new Vector3(.09f, .76f, 0), .055f, TimberLight);
            b.Box(new(.30f, .16f, .15f), at + new Vector3(.09f, .66f, 0), Iron);
        }
        for (int slab = 0; slab < 3; slab++)
            b.Box(new(.75f, .085f, .66f), p + new Vector3(-2.65f, .14f + slab * .1f, -.15f), StoneDark, new(0, slab * 9, 0));
        b.Beam(p + new Vector3(.28f, 3.7f, -.85f), p + new Vector3(1.05f, 3.7f, -.85f), .10f, Timber);
        b.Box(new(.72f, .49f, .08f), p + new Vector3(.80f, 3.28f, -.82f), TimberLight);
        b.Box(new(.44f, .12f, .035f), p + new Vector3(.80f, 3.29f, -.765f), Iron);
        b.Box(new(.15f, .18f, .04f), p + new Vector3(.80f, 3.17f, -.76f), Iron);
    }

    private static void MarketQuay(EnvironmentBuilder b, float x, float z)
    {
        // Open, low market tables align with Oris and Kesh across the southern plaza.
        foreach (float px in new[] { x * .05f, x * .43f })
        {
            Vector3 at = new(px, 0, z + 1.05f);
            b.Box(new(2.75f, .15f, .78f), at + Vector3.Up * .72f, TimberLight);
            b.Box(new(2.70f, .035f, .74f), at + Vector3.Up * .815f, Cloth);
            foreach (float side in new[] { -1f, 1f })
                b.Box(new(.13f, .66f, .53f), at + new Vector3(side * 1.12f, .33f, 0), Timber);
            for (int basket = -1; basket <= 1; basket++)
            {
                Vector3 goods = at + new Vector3(basket * .72f, .93f, 0);
                b.Cylinder(.27f, .31f, .22f, goods, Timber);
                for (int fruit = 0; fruit < 3; fruit++)
                    b.Cylinder(.09f, .065f, .13f, goods + new Vector3((fruit - 1) * .13f, .17f, fruit % 2 * .10f), basket == 0 ? "ac8060" : "607e65");
            }
        }
        Vector3 cart = new(x + 1.55f, 0, z * .64f);
        b.Box(new(1.7f, .18f, 2.8f), cart + Vector3.Up * .65f, Timber);
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.10f, .47f, 2.82f), cart + new Vector3(side * .80f, .93f, 0), TimberLight);
            foreach (float end in new[] { -.82f, .82f })
            {
                b.Torus(.31f, .40f, cart + new Vector3(side * .92f, .42f, end), Iron, new(0, 0, 90));
                b.Beam(cart + new Vector3(side * .94f, .13f, end), cart + new Vector3(side * .94f, .72f, end), .065f, TimberLight);
            }
        }
        Crate(b, cart + new Vector3(0, 1.08f, -.58f), new(.78f, .67f, .75f));
        b.Cylinder(.31f, .31f, 1.07f, cart + new Vector3(.12f, 1.05f, .50f), ClothLight, new(90, 0, 0));
    }

    private static void Wayposts(EnvironmentBuilder b, float x)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 at = new(x + .92f, 0, side * 1.9f);
            b.Box(new(.67f, .24f, .72f), at + Vector3.Up * .12f, StoneDark);
            b.Box(new(.36f, 1.24f, .40f), at + Vector3.Up * .83f, Stone);
            b.Box(new(.47f, .11f, .52f), at + Vector3.Up * 1.50f, StoneLight);
            b.Box(new(.035f, .53f, .26f), at + new Vector3(-.19f, 1.06f, 0), Cloth);
            b.Torus(.105f, .14f, at + new Vector3(-.215f, 1.07f, 0), Bronze, new(0, 0, 90));
        }
    }

    private static void Hillside(EnvironmentBuilder b, float x, float z)
    {
        // Broken terraces lead the eye toward the western chapel instead of outlining a rectangle.
        for (int tier = 0; tier < 3; tier++)
        {
            b.Box(new(5.8f - tier * .85f, .42f, 1.10f), new(-x - 3.2f, .2f + tier * .44f, -z - .8f - tier * .9f), StoneDark, new(0, -14, 0));
            b.Box(new(5.95f - tier * .85f, .10f, 1.2f), new(-x - 3.2f, .45f + tier * .44f, -z - .8f - tier * .9f), StoneLight, new(0, -14, 0));
        }
        foreach (Vector3 tree in new[] { new Vector3(-x - 3.0f, 0, -z * .55f), new Vector3(-x * .84f, 1.8f, -z - 6.8f), new Vector3(x * .73f, .4f, -z - 6.4f) })
        {
            b.Cylinder(.16f, .095f, 2.3f, tree + Vector3.Up * 1.15f, Timber);
            for (int crown = 0; crown < 3; crown++)
                b.Cylinder(1.05f - crown * .24f, .06f, 1.25f, tree + Vector3.Up * (1.68f + crown * .66f), crown % 2 == 0 ? "607e65" : "586b60", surface: SurfaceKind.Earth);
        }
    }

    private static void House(EnvironmentBuilder b, Vector3 center, float width, float depth, float wallHeight,
        string roofColor, float doorOffset, bool upperWindow)
    {
        float front = center.Z + depth * .5f;
        b.Box(new(width + .24f, .38f, depth + .18f), center + new Vector3(0, .19f, 0), StoneDark);
        b.Box(new(width, wallHeight, depth), center + new Vector3(0, wallHeight * .5f + .19f, 0), Stone);
        b.Box(new(width + .14f, .16f, depth + .14f), center + new Vector3(0, .47f, 0), StoneLight);
        float eave = wallHeight + .22f;
        float half = width * .5f + .25f;
        float rise = half * .69f;
        float pitch = Mathf.RadToDeg(Mathf.Atan2(rise, half));
        float roofLength = Mathf.Sqrt(half * half + rise * rise);

        // Stepped infill and exposed rafters give the gables a crafted, worn silhouette.
        for (int row = 0; row < 5; row++)
        {
            float h = (row + .5f) * rise / 5f;
            b.Box(new(width * (1f - h / rise), rise / 5f, depth), center + new Vector3(0, eave + h, 0), Timber);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(roofLength + .16f, .2f, depth + .65f), center + new Vector3(side * half * .5f, eave + rise * .5f, 0), roofColor, new(0, 0, -side * pitch));
            b.Beam(new(center.X + side * half, eave, front + .37f), new(center.X, eave + rise + .04f, front + .37f), .18f, TimberLight);
            b.Box(new(.18f, wallHeight + .22f, .16f), new(center.X + side * (width * .5f - .11f), wallHeight * .5f + .25f, front + .035f), Timber);
            b.Box(new(.14f, .12f, depth + .78f), center + new Vector3(side * half, eave + .035f, 0), TimberLight);
            // A few broad seams read as slate courses without hundreds of individual roof tiles.
            for (int course = 1; course <= 3; course++)
            {
                float along = course / 4f;
                b.Box(new(.065f, .045f, depth + .67f), center + new Vector3(side * half * along, eave + rise * (1 - along) + .14f, 0), "6e7d7d", new(0, 0, -side * pitch));
            }
        }
        b.Box(new(.22f, .22f, depth + .84f), center + new Vector3(0, eave + rise + .07f, 0), Iron);
        b.Box(new(width + .3f, .18f, .2f), new(center.X, eave, front + .06f), TimberLight);
        b.Box(new(.17f, rise + .1f, .14f), new(center.X, eave + rise * .5f, front + .1f), TimberLight);
        for (int i = 0; i < 3; i++)
        {
            float brickX = center.X - width * .38f + i * width * .36f;
            b.Box(new(.55f, .25f, .08f), new(brickX, .83f + (i % 2) * .45f, front + .065f), StoneLight);
        }

        float doorX = center.X + doorOffset;
        b.Box(new(.92f, 1.92f, .09f), new(doorX, 1.24f, front + .055f), Iron);
        b.Box(new(.72f, 1.65f, .1f), new(doorX, 1.19f, front + .12f), Timber);
        for (int plank = -1; plank <= 1; plank++)
            b.Box(new(.025f, 1.62f, .025f), new(doorX + plank * .23f, 1.19f, front + .18f), TimberLight);
        b.Box(new(.91f, .16f, .2f), new(doorX, 2.18f, front + .15f), StoneLight);
        b.Box(new(.1f, .11f, .06f), new(doorX + .23f, 1.15f, front + .21f), Bronze);
        b.Box(new(1.25f, .16f, .43f), new(doorX, .18f, front + .18f), StoneLight);
        Window(b, new(center.X + width * .3f, 1.82f, front + .08f), .76f, .9f);
        if (upperWindow) Window(b, new(center.X, eave + rise * .32f, front + .11f), .58f, .68f);
        Lantern(b, new(doorX - .72f, 2.03f, front + .23f));
    }

    private static void Window(EnvironmentBuilder b, Vector3 p, float width, float height)
    {
        b.Box(new(width + .17f, height + .17f, .1f), p, Timber);
        b.Box(new(width, height, .035f), p + new Vector3(0, 0, .07f), Warm, glow: true);
        b.Box(new(.07f, height + .04f, .065f), p + new Vector3(0, 0, .1f), Iron);
        b.Box(new(width + .04f, .07f, .065f), p + new Vector3(0, 0, .1f), Iron);
        b.Box(new(width + .24f, .12f, .25f), p + new Vector3(0, -height * .5f - .1f, .05f), StoneLight);
    }

    private static void Forge(EnvironmentBuilder b, Vector3 p)
    {
        b.Box(new(1.35f, .32f, .92f), p + new Vector3(0, .16f, 0), StoneDark);
        b.Box(new(1.1f, .86f, .76f), p + new Vector3(0, .72f, -.04f), Iron);
        b.Box(new(.75f, .49f, .035f), p + new Vector3(0, .7f, .36f), "f08b46", glow: true);
        b.Box(new(.45f, .16f, .08f), p + new Vector3(0, .5f, .42f), "f7cc76", glow: true);
        foreach (float side in new[] { -1f, 1f })
            b.Box(new(.25f, 1.05f, .89f), p + new Vector3(side * .59f, .81f, 0), Stone);
        b.Box(new(1.48f, .27f, 1f), p + new Vector3(0, 1.42f, 0), StoneLight);
        b.Cylinder(.53f, .2f, .85f, p + new Vector3(0, 1.98f, 0), Iron);
        b.Cylinder(.18f, .18f, 1.2f, p + new Vector3(0, 2.98f, 0), Iron);
        b.Cylinder(.23f, .23f, .12f, p + new Vector3(0, 3.59f, 0), Bronze);

        var anvil = p + new Vector3(1.44f, 0, .02f);
        b.Cylinder(.38f, .33f, .6f, anvil + new Vector3(0, .3f, 0), Timber);
        b.Box(new(.55f, .14f, .34f), anvil + new Vector3(0, .68f, 0), Iron);
        b.Box(new(.28f, .2f, .26f), anvil + new Vector3(0, .83f, 0), Iron);
        b.Box(new(.79f, .17f, .38f), anvil + new Vector3(0, 1.01f, 0), "758082");
        b.Cylinder(.15f, .035f, .4f, anvil + new Vector3(.53f, 1.02f, 0), "758082", new(0, 0, -90));
        b.Beam(anvil + new Vector3(-.27f, 1.1f, -.08f), anvil + new Vector3(.16f, 1.13f, .13f), .065f, TimberLight);
        b.Box(new(.18f, .16f, .16f), anvil + new Vector3(.17f, 1.18f, .13f), Bronze);
    }

    private static void Chimney(EnvironmentBuilder b, Vector3 p, float height)
    {
        b.Box(new(.72f, height, .83f), p + Vector3.Up * (height * .5f), StoneDark);
        for (int row = 0; row < 4; row++)
            b.Box(new(.81f, .13f, .92f), p + Vector3.Up * (height - row * .44f), row == 0 ? StoneLight : Stone);
        b.Box(new(.52f, .045f, .63f), p + Vector3.Up * (height + .075f), Iron);
    }

    private static void Gate(EnvironmentBuilder b, Vector3 p)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.83f, .34f, .83f), p + new Vector3(0, .17f, side * 1.65f), StoneDark);
            b.Box(new(.59f, 3.55f, .62f), p + new Vector3(0, 1.95f, side * 1.65f), Stone);
            b.Box(new(.88f, .24f, .91f), p + new Vector3(0, 3.74f, side * 1.65f), StoneLight);
            b.Box(new(.72f, .21f, .75f), p + new Vector3(0, 1.26f, side * 1.65f), StoneLight);
            b.Box(new(.045f, 1.76f, .58f), p + new Vector3(.35f, 2.26f, side * 1.65f), Cloth);
            b.Box(new(.06f, .08f, .6f), p + new Vector3(.38f, 2.98f, side * 1.65f), Bronze);
            b.Box(new(.055f, .55f, .08f), p + new Vector3(.39f, 2.21f, side * 1.65f), ClothLight);
        }
        b.Box(new(.53f, .3f, 4.25f), p + new Vector3(0, 4.03f, 0), Timber);
        b.Beam(p + new Vector3(0, 3.58f, -1.65f), p + new Vector3(0, 4.27f, 0), .18f, TimberLight);
        b.Beam(p + new Vector3(0, 3.58f, 1.65f), p + new Vector3(0, 4.27f, 0), .18f, TimberLight);
        b.Torus(.23f, .3f, p + new Vector3(.035f, 3.6f, 0), Bronze, new(0, 0, 90));
    }

    private static void Shrine(EnvironmentBuilder b, Vector3 p)
    {
        b.Box(new(1.45f, .25f, 1.35f), p + new Vector3(0, .125f, 0), StoneDark);
        b.Box(new(.8f, 1.36f, .87f), p + new Vector3(0, .92f, 0), Stone);
        b.Box(new(1.04f, .19f, 1.06f), p + new Vector3(0, 1.65f, 0), StoneLight);
        b.Cylinder(.28f, .2f, .75f, p + new Vector3(0, 2.1f, 0), ClothLight);
        b.Cylinder(.22f, .08f, .3f, p + new Vector3(0, 2.61f, 0), StoneLight);
        b.Torus(.22f, .27f, p + new Vector3(0, 2.65f, -.13f), Bronze, new(90, 0, 0));
        for (int i = 0; i < 3; i++)
        {
            var candle = p + new Vector3(.51f, 1.83f + (i % 2) * .07f, -.29f + i * .27f);
            b.Cylinder(.04f, .04f, .18f, candle, "d3c4a1");
            b.Cylinder(.035f, 0, .11f, candle + Vector3.Up * .14f, Warm, glow: true);
        }
    }

    private static void LanternPost(EnvironmentBuilder b, Vector3 p, float height)
    {
        b.Cylinder(.23f, .18f, .2f, p + Vector3.Up * .1f, StoneLight);
        b.Cylinder(.07f, .055f, height, p + Vector3.Up * (height * .5f), Iron);
        b.Beam(p + Vector3.Up * height, p + new Vector3(0, height, .39f), .08f, Bronze);
        Lantern(b, p + new Vector3(0, height - .38f, .39f));
    }

    private static void Lantern(EnvironmentBuilder b, Vector3 p)
    {
        b.Box(new(.23f, .34f, .22f), p, Warm, glow: true);
        b.Cylinder(.23f, .065f, .16f, p + Vector3.Up * .24f, Iron);
        b.Box(new(.3f, .08f, .28f), p - Vector3.Up * .21f, Iron);
        foreach (float side in new[] { -1f, 1f })
            b.Box(new(.035f, .41f, .27f), p + new Vector3(side * .125f, 0, 0), Iron);
        b.Beam(p + Vector3.Up * .3f, p + new Vector3(0, .43f, -.19f), .04f, Iron);
    }

    private static void Barrel(EnvironmentBuilder b, Vector3 p, float radius, float height)
    {
        b.Cylinder(radius * .84f, radius, height * .5f, p + Vector3.Up * (height * .25f), TimberLight);
        b.Cylinder(radius, radius * .84f, height * .5f, p + Vector3.Up * (height * .75f), TimberLight);
        for (int i = 0; i < 3; i++)
            b.Torus(radius * .83f, radius * .91f, p + Vector3.Up * (height * (.12f + i * .37f)), Iron);
        b.Cylinder(radius * .79f, radius * .79f, .06f, p + Vector3.Up * (height + .02f), Timber);
    }

    private static void Crate(EnvironmentBuilder b, Vector3 p, Vector3 size)
    {
        b.Box(size, p, Timber);
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(size.X + .04f, .09f, size.Z + .04f), p + Vector3.Up * (side * size.Y * .4f), TimberLight);
            b.Box(new(.09f, size.Y, .035f), p + new Vector3(side * size.X * .4f, 0, size.Z * .52f), TimberLight);
        }
        b.Beam(p + new Vector3(-size.X * .4f, -size.Y * .4f, size.Z * .53f), p + new Vector3(size.X * .4f, size.Y * .4f, size.Z * .53f), .08f, TimberLight);
    }

    private static void Woodpile(EnvironmentBuilder b, Vector3 p)
    {
        for (int row = 0; row < 2; row++)
            for (int log = 0; log < 4 - row; log++)
            {
                var center = p + new Vector3(-.46f + log * .3f + row * .15f, .14f + row * .24f, 0);
                b.Cylinder(.145f, .135f, .79f, center, Timber, new(90, 0, 0));
                b.Cylinder(.11f, .11f, .035f, center + new Vector3(0, 0, .4f), TimberLight, new(90, 0, 0));
            }
    }

    private static void Bench(EnvironmentBuilder b, Vector3 p)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(new(.24f, .46f, .49f), p + new Vector3(side * 1.1f, .23f, 0), StoneDark);
            b.Box(new(2.9f, .1f, .24f), p + new Vector3(0, .49f, side * .145f), TimberLight);
        }
    }

}
