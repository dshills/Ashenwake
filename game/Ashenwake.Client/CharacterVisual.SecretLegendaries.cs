using Godot;

namespace Ashenwake.Client;

public partial class CharacterVisual
{
    private static void EquippedGriefShield(Node3D parent, ItemAppearance item)
    {
        Material iron = SharedMaterial("434753", metallic: true), silver = SharedMaterial("b4bacb", metallic: true), silence = SharedMaterial("c7d9df", emissive: true);
        TaperedBox(parent, new(-.08f, -.41f, -.25f), new(.68f, .90f, .13f), iron, .64f);
        TaperedBox(parent, new(-.08f, -.40f, -.33f), new(.52f, .73f, .045f), silver, .64f);
        TaperedBox(parent, new(-.08f, -.40f, -.36f), new(.44f, .64f, .025f), iron, .64f);
        // An empty bell and a hairline fracture carry its story without an animated effect.
        Cone(parent, new(-.08f, -.37f, -.40f), .15f, .055f, .24f, silver);
        Ring(parent, new(-.08f, -.25f, -.40f), .04f, .06f, silver, new(90, 0, 0));
        Rod(parent, new(-.12f, -.50f, -.425f), new(-.04f, -.62f, -.425f), .012f, silence);
        Rod(parent, new(-.04f, -.62f, -.425f), new(-.09f, -.73f, -.425f), .012f, silence);
        ArmorJewel(parent, new(-.08f, -.17f, -.40f), item);
    }
    private static void EquippedWidowthorn(Node3D parent, ItemAppearance item)
    {
        Material bark = SharedMaterial("384638"), edge = SharedMaterial("9aaa80", metallic: true), venom = SharedMaterial("a9d66b", emissive: true);
        Rod(parent, new(.10f, -.86f, -.07f), new(.10f, .15f, -.07f), .048f, bark);
        TaperedBox(parent, new(.10f, .33f, -.07f), new(.16f, 1.06f, .07f), edge, .13f);
        for (int thorn = 0; thorn < 4; thorn++)
        {
            float y = -.02f + thorn * .19f;
            Cone(parent, new(.21f, y, -.07f), .067f, 0, .27f, bark, new(0, 0, -58));
            Rod(parent, new(.055f, y, -.115f), new(.13f, y + .18f, -.115f), .013f, venom);
        }
        Rod(parent, new(-.09f, -.28f, -.07f), new(.30f, -.20f, -.07f), .04f, bark);
        ArmorJewel(parent, new(.10f, -.27f, -.13f), item);
    }
    private static void EquippedEmberwake(Node3D parent, int side, ItemAppearance item)
    {
        Material ash = SharedMaterial("363c43", metallic: true), copper = SharedMaterial("a76a42", metallic: true), ember = SharedMaterial("f6ab62", emissive: true), cloth = SharedMaterial("373239");
        TailoredCape(parent, new(side * .14f, -.05f, .12f), .29f, .53f, cloth);
        for (int layer = 0; layer < 3; layer++)
        {
            float y = .035f - layer * .14f;
            TaperedBox(parent, new(side * (.04f + layer * .035f), y, 0), new(.54f - layer * .065f, .18f, .46f), ash, .78f, new(0, 0, side * 12));
            Rod(parent, new(side * .07f - .18f, y - .05f, -.235f), new(side * .07f + .18f, y - .05f, -.235f), .015f, copper);
            Box(parent, new(side * .06f, y, -.25f), new(.09f, .035f, .022f), ember);
        }
        Ring(parent, new(side * .05f, .16f, 0), .075f, .12f, copper);
        ArmorJewel(parent, new(side * .06f, -.10f, -.28f), item);
    }
}
