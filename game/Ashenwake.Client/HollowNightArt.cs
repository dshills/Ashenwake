using Godot;

namespace Ashenwake.Client;

/// <summary>Repeated, sealed facades surround the arena; their displaced shadows never occupy the playable floor.</summary>
public static class HollowNightArt
{
    private const string Void = "242833";
    private const string Shadow = "333846";
    private const string Slate = "4e586a";
    private const string Stone = "737e91";
    private const string Edge = "a0a8bc";
    private const string Cyan = "8cabad";
    private const string Memory = "786f8e";
    private const string MemoryEdge = "b0a6bf";
    private const string Inlay = "5d627b";

    public static void Build(Node3D parent, float halfWidth, float halfDepth, string encounterId)
    {
        var b = new EnvironmentBuilder(parent, "HollowNightArchitecture");
        switch (encounterId)
        {
            case "campaign.identity_memory": RememberedGallery(b, halfWidth, halfDepth); break;
            case "campaign.breach_heart": BreachChamber(b, halfWidth, halfDepth); break;
            default: RepeatingGallery(b, halfWidth, halfDepth); break;
        }
        Perimeter(b, halfWidth, halfDepth);
        b.Flush();
    }

    private static void RepeatingGallery(EnvironmentBuilder b, float x, float z)
    {
        // Three receding copies have a closed stone backing. They are distant architecture, never usable doors.
        for (int copy = 2; copy >= 0; copy--)
        {
            float height = 5.35f + copy * .48f;
            var p = new Vector3(copy * .17f, 0, -z - 2.2f - copy * 1.6f);
            BlindArch(b, p, 0, 4.6f + copy * .9f, height, copy == 0 ? Stone : Slate, copy == 0 ? Edge : Stone);
            b.Box(new(.14f, height * .56f, .065f), p + new Vector3(.31f, height * .45f, .51f), Cyan);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 3; i++)
            {
                float height = 4.75f - i * .45f;
                var p = new Vector3(side * (4.8f + i * 2.55f), 0, -z - 2.8f - i * .33f);
                BlindArch(b, p, 0, 2.25f, height, Stone, Edge);
                // A second outline is visibly displaced from the masonry that should cast it.
                DisplacedOutline(b, p + new Vector3(side * .2f, .21f, .52f), 0, 1.27f, height * .64f);
            }
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector3(side * (x + 1.32f), 0, -z * .65f + i * z * .59f);
                float yaw = side * -90;
                BlindArch(b, p, yaw, 2.05f, 3.9f - i * .62f, Slate, Stone);
                DisplacedOutline(b, p, yaw, 1.14f, 2.1f - i * .32f);
            }
        }
    }

    private static void RememberedGallery(EnvironmentBuilder b, float x, float z)
    {
        // The two halves disagree about the room's position. Inset records suggest a remembered identity without fake actors.
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 3; i++)
            {
                float height = 5.9f - i * .6f;
                var p = new Vector3(side * (2.4f + i * 2.85f), .18f * (i % 2), -z - 2.65f - (side > 0 ? .8f : 0));
                RecordFacade(b, p, 0, 2.35f, height, side > 0);
            }
            for (int i = 0; i < 3; i++)
            {
                float height = 3.95f - i * .67f;
                var p = new Vector3(side * (x + 1.45f), 0, -z * .62f + i * z * .56f);
                RecordFacade(b, p, side * -90, 1.85f, height, side > 0);
            }
            // Truncated, sideways ledges remain far outside the arena, with no stairway leading out of it.
            var shelf = new Vector3(side * (x + 3.25f), 0, -z * .12f);
            for (int i = 0; i < 4; i++)
                b.Box(new(1.4f - i * .16f, .16f, 2.6f - i * .27f), shelf + new Vector3(side * i * .1f, .6f + i * .73f, -i * .13f), i % 2 == 0 ? Slate : Memory);
        }
        // An incomplete, split memorial occupies the north skyline, with solid backing rather than an apparent exit.
        var center = new Vector3(0, 0, -z - 3.3f);
        b.Box(new(2.75f, 4.8f, .55f), center + Vector3.Up * 2.4f, Shadow);
        b.Box(new(.85f, 4.32f, .25f), center + new Vector3(-.68f, 2.52f, .43f), Memory);
        b.Box(new(.85f, 4.32f, .25f), center + new Vector3(.68f, 2.19f, .64f), Stone);
        foreach (float side in new[] { -1f, 1f })
        {
            float offset = side > 0 ? -.33f : 0;
            b.Beam(center + new Vector3(side * .2f, 4.56f + offset, .81f), center + new Vector3(side * 1.05f, 3.56f + offset, .81f), .14f, MemoryEdge);
            b.Beam(center + new Vector3(side * 1.05f, 3.56f + offset, .81f), center + new Vector3(side * .2f, 2.56f + offset, .81f), .14f, MemoryEdge);
            b.Box(new(.1f, 1.58f, .07f), center + new Vector3(side * .2f, 1.49f + offset, .81f), Inlay);
        }
    }

    private static void BreachChamber(EnvironmentBuilder b, float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            // The middle 7.6 metres are reserved for the animated breach and its binding apparatus.
            for (int i = 0; i < 3; i++)
            {
                float height = 6.25f - i * .81f;
                var p = new Vector3(side * (4.85f + i * 2.3f), 0, -z - 3.7f - i * .24f);
                Obelisk(b, p, height, i == 0);
                if (i < 2)
                {
                    var end = new Vector3(side * (4.85f + (i + 1) * 2.3f), height - .6f, p.Z - .24f);
                    b.Beam(p + new Vector3(0, height - 1.42f, -.23f), end, .21f, Slate);
                    b.Beam(p + new Vector3(0, 1.37f, -.23f), end with { Y = 1.37f }, .12f, Inlay);
                }
            }
            for (int i = 0; i < 3; i++)
            {
                float height = 3.8f - i * .64f;
                var p = new Vector3(side * (x + 1.45f), 0, -z * .61f + i * z * .56f);
                Obelisk(b, p, height, false);
                b.Box(new(.48f, .1f, .72f), p + new Vector3(-side * .17f, height * .49f, 0), Cyan);
            }
        }
    }

    private static void BlindArch(EnvironmentBuilder b, Vector3 p, float yaw, float width, float height, string stone, string edge)
    {
        Block(b, p, yaw, new(width + .38f, .26f, 1.1f), new(0, .13f, 0), Slate);
        Block(b, p, yaw, new(width * .85f, height * .8f, .36f), new(0, height * .4f + .22f, -.16f), Void);
        Block(b, p, yaw, new(width * .65f, height * .69f, .06f), new(0, height * .4f + .22f, .054f), Shadow);
        foreach (float side in new[] { -1f, 1f })
        {
            Block(b, p, yaw, new(.31f, height * .7f, .76f), new(side * width * .43f, height * .35f + .23f, 0), stone);
            Block(b, p, yaw, new(.075f, height * .67f, .065f), new(side * width * .42f, height * .35f + .28f, .425f), edge);
            Beam(b, p, yaw, new(side * width * .43f, height * .7f + .23f, 0), new(side * width * .27f, height * .93f, 0), .32f, stone);
            Beam(b, p, yaw, new(side * width * .27f, height * .93f, 0), new(0, height, 0), .32f, edge);
            Block(b, p, yaw, new(.5f, .18f, .88f), new(side * width * .43f, height * .28f, 0), edge);
        }
        Block(b, p, yaw, new(width * .74f, .18f, .42f), new(0, .42f, .2f), stone);
        // A closed inset crosses the dark recess, explicitly sealing the arch.
        Block(b, p, yaw, new(width * .6f, .12f, .07f), new(0, height * .47f, .108f), Inlay);
        Block(b, p, yaw, new(.15f, height * .54f, .08f), new(0, height * .43f, .111f), Slate);
    }

    private static void DisplacedOutline(EnvironmentBuilder b, Vector3 p, float yaw, float width, float height)
    {
        foreach (float side in new[] { -1f, 1f })
        {
            Block(b, p, yaw, new(.11f, height, .025f), new(side * width * .5f + .13f, height * .5f + .49f, .224f), Inlay);
            Beam(b, p, yaw, new(side * width * .5f + .13f, height + .49f, .224f), new(.13f, height + .89f, .224f), .09f, Inlay);
        }
    }

    private static void RecordFacade(EnvironmentBuilder b, Vector3 p, float yaw, float width, float height, bool displaced)
    {
        Block(b, p, yaw, new(width + .25f, .26f, 1.05f), new(0, .13f, 0), Slate);
        Block(b, p, yaw, new(width, height, .64f), new(0, height * .5f + .24f, 0), displaced ? Memory : Stone);
        Block(b, p, yaw, new(width * .7f, height * .8f, .035f), new(.07f, height * .52f + .24f, .34f), Shadow);
        foreach (float side in new[] { -1f, 1f })
            Block(b, p, yaw, new(.12f, height * .94f, .09f), new(side * width * .44f, height * .5f + .24f, .36f), MemoryEdge);
        for (int row = 0; row < 4; row++)
        {
            float y = .8f + row * height * .2f;
            float offset = displaced && row % 2 == 0 ? width * .13f : 0;
            Block(b, p, yaw, new(width * .38f, .095f, .04f), new(offset, y, .38f), Inlay);
            Block(b, p, yaw, new(.075f, height * .09f, .045f), new(offset - width * .17f, y + height * .045f, .384f), MemoryEdge);
            Block(b, p, yaw, new(.075f, height * .09f, .045f), new(offset + width * .17f, y - height * .045f, .384f), MemoryEdge);
        }
        Block(b, p, yaw, new(width + .1f, .16f, .84f), new(displaced ? .12f : -.12f, height + .37f, 0), MemoryEdge);
    }

    private static void Obelisk(EnvironmentBuilder b, Vector3 p, float height, bool inner)
    {
        b.Box(new(1.6f, .29f, 1.34f), p + Vector3.Up * .145f, Slate);
        b.Box(new(.89f, height, .73f), p + Vector3.Up * (height * .5f + .22f), Shadow);
        b.Box(new(.29f, height * .88f, .84f), p + new Vector3(-.37f, height * .48f + .22f, 0), Stone);
        b.Box(new(.29f, height * .76f, .84f), p + new Vector3(.37f, height * .42f + .22f, 0), Slate);
        b.Box(new(.095f, height * .71f, .075f), p + new Vector3(-.08f, height * .53f + .22f, .423f), inner ? Cyan : Edge);
        for (int i = 0; i < 3; i++)
        {
            float py = height * (.21f + i * .25f);
            b.Box(new(1.23f, .16f, 1.01f), p + new Vector3(0, py, 0), Inlay);
            b.Box(new(.43f, .075f, .065f), p + new Vector3(.03f, py + .2f, .425f), Stone);
        }
        b.Box(new(.82f, .22f, .87f), p + new Vector3(-.21f, height + .39f, 0), Edge);
    }

    private static void Perimeter(EnvironmentBuilder b, float x, float z)
    {
        // Segmented low coping makes the real room edge legible; the foreground never masks players or warnings.
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = 0; i < 16; i++)
            {
                float px = -x * .95f + i * x * 1.9f / 15, pz = -z * .95f + i * z * 1.9f / 15;
                b.Box(new(x * 1.9f / 16 - .045f, .19f, .35f), new(px, .095f, side * (z + .46f)), Slate);
                b.Box(new(.35f, .22f, z * 1.9f / 16 - .045f), new(side * (x + .46f), .11f, pz), Slate);
                if (i % 3 == 0)
                {
                    b.Box(new(.16f, .06f, .32f), new(px, .22f, side * (z + .46f)), Stone);
                    b.Box(new(.32f, .06f, .16f), new(side * (x + .46f), .25f, pz), Stone);
                }
            }
        }
    }

    private static Vector3 Rotate(Vector3 value, float yaw) => new Basis(Vector3.Up, Mathf.DegToRad(yaw)) * value;
    private static void Block(EnvironmentBuilder b, Vector3 p, float yaw, Vector3 size, Vector3 local, string color)
        => b.Box(size, p + Rotate(local, yaw), color, new(0, yaw, 0));
    private static void Beam(EnvironmentBuilder b, Vector3 p, float yaw, Vector3 from, Vector3 to, float thickness, string color)
        => b.Beam(p + Rotate(from, yaw), p + Rotate(to, yaw), thickness, color);
}
