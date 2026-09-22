using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private void BuildTrainingEffigy()
    {
        Palette("ab926b", "60887b", "483e32", "827562", "d5bc87", "ab926b", "e4bd75");
        Height = 2.3f;
        Cone(BodyRoot, new(0, .12f, 0), .55f, .45f, .24f, Dark);
        Rod(BodyRoot, new(0, .16f, 0), new(0, 1.98f, 0), .09f, Dark);
        Box(BodyRoot, new(0, 1.32f, 0), new(.73f, .78f, .37f), Main);
        Box(BodyRoot, new(0, 1.13f, -.21f), new(.79f, .15f, .08f), Accent);
        Box(BodyRoot, new(0, 1.57f, -.21f), new(.79f, .08f, .08f), Dark);
        Rod(BodyRoot, new(-.82f, 1.6f, 0), new(.82f, 1.6f, 0), .09f, Dark);
        foreach (float side in new[] { -1f, 1f })
        {
            Box(BodyRoot, new(side * .58f, 1.5f, 0), new(.34f, .31f, .3f), Main);
            Box(BodyRoot, new(side * .71f, 1.5f, 0), new(.06f, .36f, .35f), Accent);
        }
        Orb(BodyRoot, new(0, 1.99f, 0), new(.43f, .49f, .4f), Main);
        Box(BodyRoot, new(0, 1.98f, -.2f), new(.35f, .08f, .04f), Dark);
        Ring(BodyRoot, new(0, 1.36f, -.22f), .14f, .20f, Accent, new(90, 0, 0));
        Orb(BodyRoot, new(0, 1.36f, -.23f), new(.10f, .10f, .04f), Metal);
    }
}
