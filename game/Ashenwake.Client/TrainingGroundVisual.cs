using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

/// <summary>Cosmetic practice-yard edging outside the authoritative walkable room.</summary>
public static class TrainingGroundVisual
{
    public static Node3D Create(RoomDefinition room)
    {
        var root = new Node3D { Name = "TrainingGroundScenery" };
        var builder = new EnvironmentBuilder(root, "PracticeYard");
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        foreach (float side in new[] { -1f, 1f })
        {
            for (int i = -2; i <= 2; i++)
            {
                var post = new Vector3(i * x * .39f, .52f, side * (z + .4f));
                builder.Box(new(.22f, 1.04f, .22f), post, "71614a");
                builder.Box(new(.29f, .12f, .29f), post + new Vector3(0, .52f, 0), "af9468");
            }
            builder.Box(new(x * 1.8f, .11f, .09f), new(0, .63f, side * (z + .4f)), "927b55");
            builder.Box(new(.15f, .72f, z * 1.7f), new(side * (x + .4f), .36f, 0), "5b655f");
        }
        builder.Cylinder(2.4f, 2.4f, .006f, new(1.1f, .005f, 0), "6f7468");
        builder.Flush(); return root;
    }
}
