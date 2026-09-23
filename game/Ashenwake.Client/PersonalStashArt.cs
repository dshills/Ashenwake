using Godot;

namespace Ashenwake.Client;

/// <summary>The player's chest is decorative; Core owns its location, access and contents.</summary>
internal static class PersonalStashArt
{
    public static void Build(Node3D parent)
    {
        var chest = new EnvironmentBuilder(parent, "PersonalStashChest");
        chest.Box(new(1.65f, .12f, 1.2f), new(0, .06f, 0), "454b50");
        chest.Box(new(1.42f, .66f, .91f), new(0, .45f, 0), "55473a", surface: SurfaceKind.Wood);
        chest.Box(new(1.5f, .18f, .99f), new(0, .87f, 0), "73604a", surface: SurfaceKind.Wood);
        foreach (float x in new[] { -.55f, .55f })
        {
            chest.Box(new(.10f, .83f, 1.01f), new(x, .49f, 0), "727b80", surface: SurfaceKind.Metal);
            chest.Box(new(.18f, .11f, .22f), new(x, .22f, -.57f), "464e55", surface: SurfaceKind.Metal);
            foreach (float y in new[] { .27f, .69f })
                chest.Cylinder(.032f, .032f, .025f, new(x, y, -.52f), "bbac83", new(90, 0, 0), surface: SurfaceKind.Metal);
        }
        chest.Box(new(.20f, .27f, .065f), new(0, .68f, -.53f), "c1a66d", surface: SurfaceKind.Metal);
        chest.Box(new(.035f, .083f, .02f), new(0, .68f, -.575f), "292d33");
        chest.Torus(.11f, .145f, new(.81f, .49f, 0), "8f999e", new(0, 0, 90), surface: SurfaceKind.Metal);
        chest.Torus(.11f, .145f, new(-.81f, .49f, 0), "8f999e", new(0, 0, 90), surface: SurfaceKind.Metal);
        // Four small brass tabs echo the four storage compartments in the journal.
        for (int i = 0; i < 4; i++) chest.Box(new(.14f, .055f, .025f), new(-.30f + i * .20f, .40f, -.48f), "b6a178", surface: SurfaceKind.Metal);
        chest.Flush();
    }
}
