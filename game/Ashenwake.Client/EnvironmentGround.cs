using Ashenwake.Core.Content;
using Godot;

namespace Ashenwake.Client;

public static class EnvironmentGround
{
    public static string Style(bool inHub, string encounterId, string? explorationId = null, int act = 0) => inHub ? "greyhaven" : explorationId == "event.wake_hunt" ? "verdant_hunt" : encounterId switch
    {
        "campaign.road" or "room.ossuary" => "road",
        "campaign.monastery" or "room.cloister" => "monastery",
        "campaign.bell_saint" or "room.bell_sanctum" => "sanctum",
        "campaign.living_ruins" => "verdant_ruins",
        "campaign.plague_village" => "verdant_village",
        "campaign.rootheart" => "verdant_heart",
        "exploration.antler_hunt" => "verdant_hunt",
        "clear" when act == 2 => "verdant_ruins",
        _ => "default"
    };

    /// <summary>All ground tops stay below Y=0, below gameplay telegraphs and interaction rings.</summary>
    public static void Build(Node3D parent, RoomDefinition room, string style)
    {
        if (style is "verdant_ruins" or "verdant_village" or "verdant_heart" or "verdant_hunt")
        { VerdantGround.Build(parent, room, style); return; }
        var b = new EnvironmentBuilder(parent, "AuthoredGround");
        float x = room.HalfWidth * .001f, z = room.HalfDepth * .001f;
        bool hub = style == "greyhaven", road = style == "road", sanctum = style == "sanctum";
        string soil = hub ? "303b38" : "252e30";
        string[] stone = hub ? ["535e59", "5b645e", "626a61", "485750"] : ["49575b", "536164", "5a6667", "424f55"];
        b.Box(new(x * 2 + 9, .4f, z * 2 + 9), new(0, -.29f, 0), soil);
        // Offset rows and restrained variations replace the diagnostic grid. They never alter collision.
        const float stepX = 1.18f, stepZ = .86f;
        int row = 0;
        for (float pz = -z + .46f; pz < z; pz += stepZ, row++)
        {
            int column = 0;
            for (float px = -x + .62f + row % 2 * .36f; px < x - .3f; px += stepX, column++)
            {
                int variation = (row * 17 + column * 13) % 11;
                bool path = Math.Abs(pz) < 2.7f || Math.Abs(px + 4.5f) < 1.4f;
                if (road && !path && variation < 6) continue;
                if (!hub && variation == 0) continue;
                float width = Math.Min(stepX - .065f, (x - px) * 2 - .03f);
                float depth = Math.Min(stepZ - .065f, (z - pz) * 2 - .03f);
                b.Box(new(width, .065f, depth), new(px, -.052f, pz), stone[variation % stone.Length], new(0, variation % 3 - 1, 0));
                if (!hub && !path && variation == 7)
                    b.Box(new(width * .58f, .007f, depth * .36f), new(px, -.015f, pz), "384b43", new(0, 17, 0));
            }
        }
        // Low contrasting coping marks the exact navigable edge, with the outlying scenery behind it.
        string edge = hub ? "828577" : "777f7c";
        b.Box(new(x * 2, .045f, .16f), new(0, -.027f, -z), edge);
        b.Box(new(x * 2, .045f, .16f), new(0, -.027f, z), edge);
        b.Box(new(.16f, .045f, z * 2), new(-x, -.027f, 0), edge);
        b.Box(new(.16f, .045f, z * 2), new(x, -.027f, 0), edge);
        if (sanctum)
        {
            // A worn, muted ritual inlay; high-saturation combat circles remain visually dominant.
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.Tau / 48;
                b.Box(new(.49f, .007f, .065f), new(Mathf.Sin(angle) * 4.1f, -.005f, Mathf.Cos(angle) * 4.1f), "7d8175", new(0, i * 7.5f, 0));
            }
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.Tau / 8;
                b.Box(new(.18f, .007f, .8f), new(Mathf.Sin(angle) * 4.8f, -.005f, Mathf.Cos(angle) * 4.8f), "626f6b", new(0, i * 45, 0));
            }
        }
        b.Flush();
    }
}
