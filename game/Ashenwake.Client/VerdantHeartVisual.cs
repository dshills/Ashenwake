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
    private readonly StandardMaterial3D _heartMaterial = CreateHeartMaterial();
    private readonly List<(MeshInstance3D Node, Mesh Mesh, Material Material)> _renderParts = [];
    private bool _initialized;
    private bool _released;
    private bool _reducedEffects;
    private double _clock;
    private float _bloomAge = BloomDuration;

    public int LivingRoots { get; private set; }
    public bool Defeated { get; private set; }
    public bool IsTransitioning => Defeated && _bloomAge < BloomDuration;
    public int ArticulatedPartCount => _petals.Length + _feedingRoots.Length + 2;
    public int TransientCapacity => 0;
    public float VictoryProgress => Defeated ? Mathf.SmoothStep(0, 1, _bloomAge / BloomDuration) : 0;
    public Transform3D HeartPose => _heart.Transform;
    internal double MotionTime => _clock;

    public static VerdantHeartVisual Create(float halfWidth, float halfDepth, int livingRoots = 3, bool defeated = false)
    {
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
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
        if (!_initialized) return;
        if (reducedEffects)
        {
            // Preferences apply even while paused. Consume the bloom so restoring full
            // effects cannot replay a victory, and return breathing to its neutral pose.
            _clock = 0;
            _bloomAge = BloomDuration;
            ApplyPose();
            return;
        }
        if (paused) return;
        float step = double.IsFinite(delta) ? (float)Math.Clamp(delta, 0, .1) : 0;
        _clock = (_clock + step) % (Math.Tau / 1.35);
        _bloomAge = Math.Min(BloomDuration, _bloomAge + step);
        ApplyPose();
    }

    private void ApplyPose()
    {
        float victory = VictoryProgress;
        float exposure = (3 - LivingRoots) / 3f;
        float breath = !Defeated && !_reducedEffects ? (float)Math.Sin(_clock * 1.35) : 0;
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

    private static StandardMaterial3D CreateHeartMaterial()
    {
        var material = SurfaceMaterials.Create("a46a65", SurfaceKind.Skin);
        material.Roughness = .74f;
        material.EmissionEnabled = true;
        material.Emission = new("b86e57");
        material.EmissionEnergyMultiplier = .35f;
        return material;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _released) return;
        _released = true;
        // The seed lobes share resources within this rig. Dispose each owned resource
        // once; grain textures belong to the factory and survive room replacement.
        var meshes = _renderParts.Select(part => part.Mesh).DistinctBy(mesh => mesh.GetInstanceId()).ToArray();
        var materials = _renderParts.Select(part => part.Material).Append(_heartMaterial).DistinctBy(material => material.GetInstanceId()).ToArray();
        foreach (var part in _renderParts)
            if (GodotObject.IsInstanceValid(part.Node)) { part.Node.Mesh = null; part.Node.MaterialOverride = null; }
        foreach (var mesh in meshes) mesh.Dispose();
        foreach (var material in materials) material.Dispose();
        _renderParts.Clear();
    }

    private void Build()
    {
        var bed = new EnvironmentBuilder(_sculpture, "CorpseFlowerBed");
        bed.Cylinder(1.75f, 1.4f, .32f, new(0, .16f, 0), "3b493b", surface: SurfaceKind.Earth);
        bed.Cylinder(1.3f, .85f, .5f, new(0, .5f, -.15f), "566347", surface: SurfaceKind.Wood);
        bed.Branch(new(0, .45f, -.15f), new(0, 2.65f, -.45f), .42f, .25f, "465444");
        for (int side = -1; side <= 1; side += 2)
        {
            bed.Branch(new(side * 1.3f, .23f, -.5f), new(side * 1.58f, 1.25f, -.52f), .14f, .11f, "84906b");
            bed.Branch(new(side * 1.58f, 1.25f, -.52f), new(side * .78f, 2.35f, -.55f), .11f, .07f, "aab18b");
            bed.Branch(new(side * .78f, 2.35f, -.55f), new(side * .48f, 2.47f, -.52f), .07f, .018f, "aab18b");
            for (int i = 0; i < 3; i++)
            {
                float x = side * (.7f + i * .34f);
                var elbow = new Vector3(x + side * .33f, .12f, .52f + i * .2f);
                bed.Branch(new(x, .3f, .23f + i * .2f), elbow, .16f - i * .025f, .08f, "566347");
                bed.Branch(elbow, new(x + side * .57f, .055f, .7f + i * .2f), .08f, .018f, "566347");
            }
        }
        bed.Flush();
        _sculpture.AddChild(_heart);
        var seedMesh = new SphereMesh { Radius = .65f, Height = 1.78f, RadialSegments = 24, Rings = 16 };
        _heart.AddChild(new MeshInstance3D { Name = "LivingSeed", Mesh = seedMesh, MaterialOverride = _heartMaterial, Scale = new(1, 1, .86f) });
        var lobeMesh = new SphereMesh { Radius = .28f, Height = 1.15f, RadialSegments = 16, Rings = 10 };
        for (int side = -1; side <= 1; side += 2)
            _heart.AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "LeftSeedLobe" : "RightSeedLobe",
                Mesh = lobeMesh,
                MaterialOverride = _heartMaterial,
                Position = new(side * .4f, -.1f, -.06f),
                Scale = new(.94f, 1, 1.1f),
                RotationDegrees = new(0, 0, side * -14)
            });
        var seed = new EnvironmentBuilder(_heart, "SeedVeins");
        for (int i = -2; i <= 2; i++)
        {
            if (i == 0) continue;
            float x = i * .17f;
            var lower = new Vector3(x * .64f, -.68f, .29f);
            var belly = new Vector3(x * 1.45f, -.16f, .55f - Math.Abs(i) * .065f);
            var shoulder = new Vector3(x * 1.23f, .3f, .53f - Math.Abs(i) * .06f);
            seed.Branch(lower, belly, .038f, .027f, "d0b393", SurfaceKind.Bone);
            seed.Branch(belly, shoulder, .027f, .023f, "d0b393", SurfaceKind.Bone);
            seed.Branch(shoulder, new(x * .62f, .73f, .23f), .023f, .009f, "d0b393", SurfaceKind.Bone);
        }
        seed.Branch(new(0, -.67f, .32f), new(-.035f, -.15f, .563f), .024f, .04f, "57463d", SurfaceKind.Skin);
        seed.Branch(new(-.035f, -.15f, .563f), new(.022f, .28f, .53f), .04f, .025f, "57463d", SurfaceKind.Skin);
        seed.Branch(new(.022f, .28f, .53f), new(0, .73f, .27f), .025f, .007f, "57463d", SurfaceKind.Skin);
        for (int i = -1; i <= 1; i++)
            seed.Leaf(.39f, .22f, -.1f, new(i * .12f, .66f, -.035f), "8d9465", new(-16, 0, i * -30), SurfaceKind.Skin);
        for (int side = -1; side <= 1; side += 2)
            for (int pore = 0; pore < 3; pore++)
                seed.Torus(.025f, .044f, new(side * (.32f + pore * .042f), -.37f + pore * .17f, .46f - pore * .015f),
                    "765549", new(90, side * -20, 0), surface: SurfaceKind.Skin);
        seed.Flush();
        for (int i = 0; i < _petals.Length; i++)
        {
            var petal = new Node3D { Name = "HeartPetal" + i, Position = new(0, 2.8f, .14f) };
            _petals[i] = petal; _sculpture.AddChild(petal);
            var leaf = new EnvironmentBuilder(petal, "FoldedLeaf");
            leaf.Leaf(2.18f, 1.04f, .24f, new(0, .13f, 0), "65734d");
            leaf.Leaf(2.06f, .87f, .25f, new(0, .23f, .04f), i % 2 == 0 ? "967366" : "865e5c", surface: SurfaceKind.Skin);
            // A raised tapered midrib and branching veins give the folded blades physical
            // depth. Follow the authored Leaf curve so the veins never float off the petal.
            Vector3 Spine(float t) => new(.87f * .035f * MathF.Sin(t * Mathf.Pi), .23f + 2.06f * t, .077f + .25f * t * t);
            for (int segment = 0; segment < 5; segment++)
            {
                float t = segment * .2f;
                leaf.Branch(Spine(t), Spine(t + .2f), .041f - t * .031f, .035f - t * .031f, "b9b88a", SurfaceKind.Bone);
            }
            for (int vein = 0; vein < 3; vein++)
            {
                float t = .3f + vein * .2f;
                float width = vein == 0 ? .36f : vein == 1 ? .31f : .19f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var tip = Spine(t + .12f) + new Vector3(side * width, 0, .045f);
                    leaf.Branch(Spine(t), tip, .022f, .006f, "b9b88a", SurfaceKind.Bone);
                }
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
            wood.Branch(lower, elbow, .2f, .145f, "596348");
            wood.Branch(elbow, tip, .145f, .06f, "747d52");
            wood.Branch(lower + Vector3.Back * .16f, elbow + Vector3.Back * .14f, .045f, .032f, "be9e74");
            wood.Branch(elbow + Vector3.Back * .14f, tip + Vector3.Back * .075f, .032f, .008f, "be9e74");
            wood.Leaf(.47f, .28f, -.05f, tip, "bcc18b", new(0, 0, -side * 35), SurfaceKind.Skin);
            wood.Flush();
        }
        _sculpture.AddChild(_growth);
        var shoots = new EnvironmentBuilder(_growth, "SettledShoots");
        for (int i = 0; i < 7; i++)
        {
            float x = (i - 3) * .37f;
            float y = .42f + (i % 3) * .1f;
            float z = .82f + (i % 2) * .22f;
            shoots.Branch(new(x, .12f, z), new(x, y + .04f, z), .026f, .013f, "789465");
            shoots.Leaf(.36f, .19f, .06f, new(x, y - .05f, z), "b7c087", new(0, 0, 48));
            shoots.Leaf(.29f, .16f, .055f, new(x, y - .09f, z), "8eaa75", new(0, 0, -52));
            shoots.Leaf(.13f, .1f, .025f, new(x, y + .06f, z), "d7c997", surface: SurfaceKind.Skin);
        }
        shoots.Flush();
        void RecordResources(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is MeshInstance3D mesh)
                    _renderParts.Add((mesh, mesh.Mesh, mesh.MaterialOverride ?? mesh.Mesh.SurfaceGetMaterial(0)));
                RecordResources(child);
            }
        }
        RecordResources(this);
    }
}
