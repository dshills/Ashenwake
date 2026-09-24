using Godot;

namespace Ashenwake.Client;

/// <summary>Purely cosmetic companion geometry; never creates combat actors, lights or collisions.</summary>
public partial class PetVisual : Node3D
{
    private Node3D _body = null!;
    private Node3D? _tail, _leftWing, _rightWing;
    private readonly List<Node3D> _legs = [];
    private string _petId = "";
    private double _time;

    public static PetVisual Create(string petId, string appearanceId)
    {
        var visual = new PetVisual { Name = "PetVisual", _petId = petId };
        visual._body = new Node3D { Name = "PetBody" }; visual.AddChild(visual._body);
        switch (petId)
        {
            case "pet.ashfox": visual.BuildFox(appearanceId == "ivory"); break;
            case "pet.gloammoth": visual.BuildMoth(appearanceId == "dusk"); break;
            case "pet.cinderbeetle": visual.BuildBeetle(appearanceId == "obsidian"); break;
        }
        return visual;
    }

    public void Animate(double delta, bool moving, bool reducedEffects)
    {
        _time += Math.Clamp(delta, 0, .1);
        float wave = reducedEffects ? 0 : MathF.Sin((float)_time * (moving ? 11 : 2));
        _body.Position = new(0, wave * (_petId == "pet.gloammoth" ? .035f : moving ? .025f : .007f), 0);
        for (int i = 0; i < _legs.Count; i++) _legs[i].Rotation = new(moving ? wave * (i % 2 == 0 ? .35f : -.35f) : 0, 0, 0);
        if (_tail is not null) _tail.Rotation = new(0, reducedEffects ? 0 : MathF.Sin((float)_time * 2.2f) * .13f, 0);
        if (_leftWing is not null && _rightWing is not null)
        {
            float flap = reducedEffects ? .12f : MathF.Sin((float)_time * 6) * .32f + .12f;
            _leftWing.Rotation = new(0, 0, -flap); _rightWing.Rotation = new(0, 0, flap);
        }
    }

    private void BuildFox(bool ivory)
    {
        var fur = Material(ivory ? "d6ccb5" : "777c82");
        var dark = Material(ivory ? "6c655f" : "303940");
        var cream = Material("eee3c6"); var ember = Material("efac65", true);
        Ellipsoid(_body, "Torso", new(0, .46f, -.02f), new(.29f, .29f, .48f), fur);
        Ellipsoid(_body, "ChestRuff", new(0, .5f, .29f), new(.26f, .3f, .21f), cream);
        Ellipsoid(_body, "Head", new(0, .75f, .39f), new(.245f, .25f, .245f), fur);
        Ellipsoid(_body, "Muzzle", new(0, .67f, .6f), new(.14f, .115f, .2f), cream);
        Ellipsoid(_body, "Nose", new(0, .704f, .776f), new(.064f, .047f, .044f), dark);
        foreach (float side in new[] { -1f, 1f })
        {
            var ear = Mesh(_body, "Ear", new CylinderMesh { TopRadius = .016f, BottomRadius = .126f, Height = .34f, RadialSegments = 24 }, new(side * .145f, 1.005f, .37f), fur);
            ear.Scale = new(1, 1, .55f); ear.Rotation = new(-.08f, 0, -side * .18f);
            Ellipsoid(_body, "InnerEar", new(side * .15f, .987f, .42f), new(.067f, .112f, .025f), dark);
            Ellipsoid(_body, "Eye", new(side * .172f, .796f, .557f), new(.043f, .047f, .026f), dark);
            Ellipsoid(_body, "EyeGlint", new(side * .173f, .802f, .582f), new(.025f, .03f, .012f), ember);
            foreach (float z in new[] { -.3f, .25f })
            {
                var leg = new Node3D { Name = "FoxLeg", Position = new(side * .19f, .33f, z) }; _body.AddChild(leg); _legs.Add(leg);
                Ellipsoid(leg, "Shin", new(0, -.095f, 0), new(.077f, .21f, .084f), fur);
                Ellipsoid(leg, "Paw", new(0, -.275f, .035f), new(.09f, .068f, .13f), dark);
            }
        }
        _tail = new Node3D { Name = "BushyTail", Position = new(0, .5f, -.37f) }; _body.AddChild(_tail);
        var tail = Ellipsoid(_tail, "TailFur", new(.07f, -.04f, -.33f), new(.2f, .21f, .44f), fur); tail.Rotation = new(.2f, -.14f, 0);
        Ellipsoid(_tail, "TailTip", new(.12f, .018f, -.68f), new(.145f, .16f, .24f), cream);
        Collar(_body, new(0, .49f, .425f), .055f, Material("ae8850", true));
    }

    private void BuildMoth(bool dusk)
    {
        var velvet = Material(dusk ? "59506f" : "708898");
        var wing = Material(dusk ? "94759d" : "c5d7cf");
        var marking = Material(dusk ? "e0ab91" : "d8bd7b");
        var dark = Material("252e3e"); var glow = Material(dusk ? "edae85" : "bceadc", true);
        Ellipsoid(_body, "Abdomen", new(0, .63f, -.16f), new(.11f, .14f, .29f), velvet);
        Ellipsoid(_body, "ThoraxRuff", new(0, .68f, .09f), new(.17f, .17f, .17f), wing);
        Ellipsoid(_body, "Head", new(0, .71f, .23f), new(.13f, .12f, .12f), velvet);
        foreach (float side in new[] { -1f, 1f })
        {
            var pivot = new Node3D { Name = side < 0 ? "LeftWing" : "RightWing", Position = new(side * .08f, .68f, .03f) }; _body.AddChild(pivot);
            if (side < 0) _leftWing = pivot; else _rightWing = pivot;
            var upper = Ellipsoid(pivot, "UpperWing", new(side * .32f, 0, .12f), new(.4f, .034f, .29f), wing); upper.Rotation = new(0, side * -.28f, 0);
            var lower = Ellipsoid(pivot, "LowerWing", new(side * .235f, -.018f, -.26f), new(.28f, .032f, .29f), velvet); lower.Rotation = new(0, side * .3f, 0);
            Ellipsoid(pivot, "EyespotRing", new(side * .39f, .033f, .09f), new(.13f, .012f, .14f), marking);
            Ellipsoid(pivot, "Eyespot", new(side * .39f, .045f, .09f), new(.086f, .009f, .094f), dark);
            Ellipsoid(pivot, "EyespotPearl", new(side * .39f, .055f, .095f), new(.03f, .008f, .036f), glow);
            Ellipsoid(pivot, "TrailingMark", new(side * .26f, .022f, -.3f), new(.14f, .009f, .04f), marking);
            Ellipsoid(_body, "Eye", new(side * .085f, .75f, .32f), new(.036f, .04f, .025f), glow);
            Segment(_body, "Antenna", new(side * .07f, .79f, .27f), new(side * .19f, 1.01f, .35f), .012f, dark);
            Ellipsoid(_body, "AntennaTip", new(side * .19f, 1.01f, .35f), new(.022f, .046f, .023f), marking);
        }
    }

    private void BuildBeetle(bool obsidian)
    {
        var shell = Material(obsidian ? "39454e" : "9d794d", metallic: .65f);
        var ridge = Material(obsidian ? "958eaa" : "d8b373", metallic: .4f);
        var dark = Material("31303b"); var glow = Material("ffbd70", true);
        Ellipsoid(_body, "Undershell", new(0, .3f, -.08f), new(.31f, .19f, .42f), dark);
        foreach (float side in new[] { -1f, 1f })
        {
            Ellipsoid(_body, "WingCase", new(side * .15f, .37f, -.13f), new(.169f, .23f, .36f), shell);
            for (int i = 0; i < 3; i++)
            {
                float z = -.29f + i * .23f;
                var leg = new Node3D { Name = "BeetleLeg", Position = new(side * .22f, .25f, z) }; _body.AddChild(leg); _legs.Add(leg);
                Vector3 knee = new(side * .2f, -.06f, -.07f), foot = new(side * .26f, -.23f, .015f);
                Segment(leg, "UpperLeg", Vector3.Zero, knee, .026f, ridge); Segment(leg, "LowerLeg", knee, foot, .024f, dark);
                Ellipsoid(leg, "Knee", knee, new(.038f, .038f, .038f), shell);
            }
            Ellipsoid(_body, "Eye", new(side * .135f, .36f, .49f), new(.044f, .049f, .026f), glow);
            Segment(_body, "Antenna", new(side * .1f, .39f, .46f), new(side * .23f, .55f, .62f), .019f, ridge);
            Ellipsoid(_body, "AntennaTip", new(side * .23f, .55f, .62f), new(.035f, .04f, .035f), glow);
        }
        Ellipsoid(_body, "Pronotum", new(0, .32f, .22f), new(.25f, .19f, .19f), ridge);
        Ellipsoid(_body, "Head", new(0, .3f, .4f), new(.18f, .14f, .15f), shell);
        for (int i = 0; i < 3; i++) Ellipsoid(_body, "ShellRivet", new(0, .597f - i * .015f, -.08f - i * .12f), new(.025f, .015f, .025f), glow);
    }

    private static StandardMaterial3D Material(string color, bool glow = false, float metallic = 0)
        => new() { AlbedoColor = new(color), Roughness = metallic > 0 ? .38f : .8f, Metallic = metallic, EmissionEnabled = glow, Emission = new(color), EmissionEnergyMultiplier = glow ? .35f : 0 };
    private static MeshInstance3D Mesh(Node3D parent, string name, Mesh mesh, Vector3 position, Material material)
    {
        var node = new MeshInstance3D { Name = name, Mesh = mesh, Position = position, MaterialOverride = material }; parent.AddChild(node); return node;
    }
    private static MeshInstance3D Ellipsoid(Node3D parent, string name, Vector3 position, Vector3 radius, Material material)
    {
        var node = Mesh(parent, name, new SphereMesh { Radius = 1, Height = 2, RadialSegments = 24, Rings = 16 }, position, material); node.Scale = radius; return node;
    }
    private static void Collar(Node3D parent, Vector3 position, float radius, Material material)
        => Ellipsoid(parent, "CompanionCharm", position, new(radius, radius, radius * .4f), material);
    private static void Segment(Node3D parent, string name, Vector3 from, Vector3 to, float radius, Material material)
    {
        Vector3 direction = to - from;
        var node = Mesh(parent, name, new CapsuleMesh { Radius = radius, Height = direction.Length() + radius * 2, RadialSegments = 12, Rings = 4 }, (from + to) * .5f, material);
        node.Quaternion = new Quaternion(Vector3.Up, direction.Normalized());
    }
}
