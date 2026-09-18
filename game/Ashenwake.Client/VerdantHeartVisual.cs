using Godot;

namespace Ashenwake.Client;

/// <summary>
/// A corpse-flower beyond the arena wall. Root count and victory come from Core; the flower never
/// invents health phases. All geometry is created once, and only thirteen cosmetic joints move.
/// </summary>
public partial class VerdantHeartVisual : Node3D
{
    private const float BloomDuration = 3.4f;
    private readonly Node3D _sculpture = new() { Name = "CorpseFlowerSculpture" };
    private readonly Node3D _heart = new() { Name = "RootheartSeed", Position = new(0, 2.8f, .35f) };
    private readonly Node3D _growth = new() { Name = "QuietNewGrowth" };
    private readonly Node3D[] _petals = new Node3D[8];
    private readonly Node3D[] _feedingRoots = new Node3D[3];
    private readonly StandardMaterial3D _heartMaterial = new()
    {
        AlbedoColor = new("a46a65"),
        Roughness = .74f,
        EmissionEnabled = true,
        Emission = new("b86e57"),
        EmissionEnergyMultiplier = .35f
    };
    private bool _initialized;
    private bool _reducedEffects;
    private float _clock;
    private float _bloomAge = BloomDuration;

    public int LivingRoots { get; private set; }
    public bool Defeated { get; private set; }
    public bool IsTransitioning => Defeated && _bloomAge < BloomDuration;
    public int ArticulatedPartCount => _petals.Length + _feedingRoots.Length + 2;
    public int TransientCapacity => 0;
    public float VictoryProgress => Defeated ? Mathf.SmoothStep(0, 1, _bloomAge / BloomDuration) : 0;
    public Transform3D HeartPose => _heart.Transform;

    public static VerdantHeartVisual Create(float halfWidth, float halfDepth, int livingRoots = 3, bool defeated = false)
    {
        var visual = new VerdantHeartVisual { Name = "VerdantHeartVisual" };
        visual._sculpture.Position = new(0, 0, -halfDepth - 2.4f);
        visual.AddChild(visual._sculpture);
        visual.Build();
        // The largest frond stays within the reserved central four-and-a-half metre silhouette.
        // Width never scales the rig into the authoritative fighting area.
        visual.SetState(livingRoots, defeated);
        return visual;
    }

    public void SetState(int livingRoots, bool defeated)
    {
        livingRoots = Math.Clamp(livingRoots, 0, _feedingRoots.Length);
        if (_initialized && LivingRoots == livingRoots && Defeated == defeated) return;
        if (_initialized && defeated && !Defeated && !_reducedEffects) _bloomAge = 0;
        else if (!_initialized || !defeated || _reducedEffects) _bloomAge = BloomDuration;
        LivingRoots = livingRoots;
        Defeated = defeated;
        _initialized = true;
        ApplyPose();
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        _reducedEffects = reducedEffects;
        if (!_initialized || paused) return;
        if (reducedEffects)
        {
            // Consume an outstanding bloom, so disabling reduced effects cannot replay a victory.
            _bloomAge = BloomDuration;
            ApplyPose();
            return;
        }
        float step = (float)Math.Clamp(delta, 0, .1);
        _clock += step;
        _bloomAge = Math.Min(BloomDuration, _bloomAge + step);
        ApplyPose();
    }

    private void ApplyPose()
    {
        float victory = VictoryProgress;
        float exposure = (3 - LivingRoots) / 3f;
        float breath = !Defeated && !_reducedEffects ? Mathf.Sin(_clock * 1.35f) : 0;
        _heart.Scale = Vector3.One * (1 + breath * .016f - victory * .17f);
        _heart.Position = new(0, 2.8f - victory * .17f, .35f);
        _heart.RotationDegrees = new(0, 0, -6 * victory);
        Color alive = new("a46a65");
        Color settled = new("c3bc83");
        _heartMaterial.AlbedoColor = alive.Lerp(settled, victory);
        _heartMaterial.Emission = new Color("b86e57").Lerp(new Color("c5b575"), victory);
        _heartMaterial.EmissionEnergyMultiplier = Mathf.Lerp(.28f + exposure * .2f + breath * .035f, .18f, victory);
        for (int i = 0; i < _petals.Length; i++)
        {
            float asymmetric = i % 2 == 0 ? 2 : -3;
            // The hinges open away from the seed as real feeding roots are destroyed.
            float opening = 49 - exposure * 13 - victory * 67 + asymmetric + breath * .55f;
            _petals[i].RotationDegrees = new(opening, 0, i * 45 + 22.5f);
        }
        for (int i = 0; i < _feedingRoots.Length; i++)
        {
            bool severed = i >= LivingRoots;
            float wilt = severed ? 1 : Defeated ? victory : 0;
            _feedingRoots[i].Scale = new(1, 1 - wilt * .58f, 1);
            _feedingRoots[i].RotationDegrees = new(wilt * -9, 0, (i - 1) * wilt * 8);
        }
        _growth.Visible = Defeated;
        _growth.Scale = new(1, .05f + victory * .95f, 1);
    }

    private void Build()
    {
        var bed = new EnvironmentBuilder(_sculpture, "CorpseFlowerBed");
        bed.Cylinder(1.75f, 1.4f, .32f, new(0, .16f, 0), "3b493b");
        bed.Cylinder(1.3f, .85f, .5f, new(0, .5f, -.15f), "566347");
        bed.Cylinder(.53f, .28f, 2.2f, new(0, 1.64f, -.35f), "465444", new(-8, 0, 0));
        for (int side = -1; side <= 1; side += 2)
        {
            bed.Beam(new(side * 1.3f, .23f, -.5f), new(side * 1.58f, 1.25f, -.52f), .19f, "84906b");
            bed.Beam(new(side * 1.58f, 1.25f, -.52f), new(side * .78f, 2.35f, -.55f), .15f, "aab18b");
            bed.Beam(new(side * .78f, 2.35f, -.55f), new(side * .48f, 2.47f, -.52f), .11f, "c0bea0");
            for (int i = 0; i < 3; i++)
            {
                float x = side * (.7f + i * .34f);
                bed.Beam(new(x, .24f, .23f + i * .2f), new(x + side * .5f, .055f, .7f + i * .2f), .16f - i * .025f, "566347");
            }
        }
        bed.Flush();
        _sculpture.AddChild(_heart);
        var seedMesh = new SphereMesh { Radius = .69f, Height = 1.62f, RadialSegments = 14, Rings = 8 };
        _heart.AddChild(new MeshInstance3D { Name = "LivingSeed", Mesh = seedMesh, MaterialOverride = _heartMaterial, Scale = new(1, 1, .8f) });
        var seed = new EnvironmentBuilder(_heart, "SeedVeins");
        for (int i = -2; i <= 2; i++)
        {
            float x = i * .19f;
            seed.Beam(new(x * .68f, -.61f, .3f), new(x * 1.5f, -.06f, .54f - Math.Abs(i) * .045f), .055f, "d0b393");
            seed.Beam(new(x * 1.5f, -.06f, .54f - Math.Abs(i) * .045f), new(x * .78f, .63f, .32f), .044f, "d0b393");
        }
        seed.Cylinder(.19f, .26f, .19f, new(0, .82f, 0), "6d7250");
        seed.Flush();
        for (int i = 0; i < _petals.Length; i++)
        {
            var petal = new Node3D { Name = "HeartPetal" + i, Position = new(0, 2.8f, .14f) };
            _petals[i] = petal; _sculpture.AddChild(petal);
            var leaf = new EnvironmentBuilder(petal, "FoldedLeaf");
            leaf.Cylinder(.17f, .26f, .88f, new(0, .7f, 0), "65734d");
            leaf.Cylinder(.38f, .22f, .85f, new(0, 1.32f, .035f), "7c8557");
            leaf.Cylinder(.22f, .015f, .66f, new(0, 2.06f, .02f), "a1a56b");
            // Broad, flattened petal volumes give a leaf silhouette rather than eight round branches.
            leaf.Box(new(.48f, .98f, .055f), new(0, 1.28f, .26f), i % 2 == 0 ? "8f7462" : "87665d", new(0, 0, -3));
            leaf.Beam(new(0, .43f, .19f), new(0, 2.14f, .22f), .055f, "b9b88a");
            for (int vein = 0; vein < 3; vein++)
            {
                float y = .9f + vein * .31f;
                float width = .24f - vein * .045f;
                leaf.Beam(new(0, y - .18f, .22f), new(width, y, .24f), .035f, "b9b88a");
                leaf.Beam(new(0, y - .18f, .22f), new(-width, y, .24f), .035f, "b9b88a");
            }
            leaf.Flush();
        }
        for (int i = 0; i < _feedingRoots.Length; i++)
        {
            var root = new Node3D { Name = "FeedingRoot" + i };
            _feedingRoots[i] = root; _sculpture.AddChild(root);
            float side = i - 1;
            float z = i == 1 ? -.25f : .63f;
            Vector3 lower = new(side * 1.28f, .18f, z);
            Vector3 elbow = new(side * 1.15f, 1.68f, z + .15f);
            Vector3 tip = new(side * .44f, 2.85f + (i == 1 ? .4f : 0), .8f);
            var wood = new EnvironmentBuilder(root, "VascularStalk");
            wood.Beam(lower, elbow, .3f, "596348");
            wood.Beam(elbow, tip, .23f, "747d52");
            wood.Beam(lower + Vector3.Back * .14f, elbow + Vector3.Back * .14f, .065f, "be9e74");
            wood.Beam(elbow + Vector3.Back * .14f, tip + Vector3.Back * .12f, .055f, "be9e74");
            wood.Cylinder(.19f, .035f, .5f, tip + new Vector3(side * .09f, .17f, 0), "bcc18b", new(0, 0, -side * 35));
            wood.Flush();
        }
        _sculpture.AddChild(_growth);
        var shoots = new EnvironmentBuilder(_growth, "SettledShoots");
        for (int i = 0; i < 7; i++)
        {
            float x = (i - 3) * .37f;
            float y = .42f + (i % 3) * .1f;
            float z = .82f + (i % 2) * .22f;
            shoots.Beam(new(x, .12f, z), new(x, y, z), .035f, "789465");
            shoots.Cylinder(.17f, .02f, .29f, new(x - .07f, y, z), "b7c087", new(0, 0, 43));
            shoots.Cylinder(.13f, .01f, .23f, new(x + .065f, y - .03f, z), "8eaa75", new(0, 0, -49));
            shoots.Cylinder(.08f, .105f, .06f, new(x, y + .17f, z), "d7c997");
        }
        shoots.Flush();
    }
}
