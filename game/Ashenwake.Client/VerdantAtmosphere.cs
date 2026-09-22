using Godot;

namespace Ashenwake.Client;

/// <summary>Room-owned foliage and practical lights. Uses an explicit paused clock, no shader
/// TIME, no simulation RNG, and a single shared leaf mesh for every moving blade.</summary>
public partial class VerdantAtmosphere : Node3D
{
    private const int PlantCount = 12, LeavesPerPlant = 6;
    private MultiMeshInstance3D _foliage = null!;
    private MultiMesh _instances = null!;
    private ArrayMesh _leafMesh = null!;
    private StandardMaterial3D _leafMaterial = null!;
    private readonly List<OmniLight3D> _lights = [];
    private readonly List<(MeshInstance3D Node, Mesh Mesh, Material Material)> _fixtures = [];
    private float _halfWidth, _halfDepth;
    private double _time;
    private bool _released;

    public string Style { get; private set; } = "";
    public double MotionTime => _time;
    public int FoliageCapacity => PlantCount * LeavesPerPlant;
    public int ActiveFoliageCount => _released ? 0 : _instances.VisibleInstanceCount;
    public int LightCapacity => _lights.Count;
    public int ActiveLightCount => _released ? 0 : _lights.Count(light => light.Visible);

    public static bool Supports(string style) => style is "verdant_ruins" or "verdant_village" or "verdant_heart" or "verdant_hunt" or "verdant_shrine";

    public static VerdantAtmosphere Create(string style, float halfWidth, float halfDepth)
    {
        if (!Supports(style)) throw new ArgumentException("Unknown Verdant environment.", nameof(style));
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
        var room = new VerdantAtmosphere { Name = "VerdantAtmosphere", Style = style, _halfWidth = halfWidth, _halfDepth = halfDepth };
        room.Build(); room.Animate(0, true, false, "High");
        return room;
    }

    public void Animate(double delta, bool paused, bool reducedEffects, string quality)
    {
        if (_released) return;
        if (reducedEffects) _time = 0;
        else if (!paused && double.IsFinite(delta) && delta > 0) _time = (_time + Math.Min(delta, .1)) % 24;
        bool high = GraphicsProfile.Normalize(quality) == "High";
        _instances.VisibleInstanceCount = high ? FoliageCapacity : FoliageCapacity / 2;
        for (int i = 0; i < _lights.Count; i++) _lights[i].Visible = high || i < 2;
        // Light pools stay steady: wind movement supplies life without pulsing against
        // the poisonous blooms and other color-coded combat warnings.
        for (int plant = 0; plant < PlantCount; plant++)
        {
            int band = plant / 3;
            float fraction = (band + .5f) / 4;
            Vector3 anchor = (plant % 3) switch
            {
                0 => new(-_halfWidth + fraction * _halfWidth * 2, .16f, -_halfDepth - 1.9f),
                1 => new(-_halfWidth - 1.9f, .16f, -_halfDepth * .8f + fraction * _halfDepth * 1.5f),
                _ => new(_halfWidth + 1.9f, .16f, -_halfDepth * .8f + fraction * _halfDepth * 1.5f)
            };
            float wind = (float)(Math.Sin(_time * Math.Tau / 8) * .055 + Math.Sin(_time * Math.Tau / 12) * .025);
            for (int leaf = 0; leaf < LeavesPerPlant; leaf++)
            {
                float angle = leaf * Mathf.Tau / LeavesPerPlant + plant * .71f;
                float lean = .63f + leaf % 3 * .15f;
                var rotation = new Vector3(lean + wind * MathF.Cos(angle), angle, wind * .6f);
                float size = .78f + (plant + leaf * 3) % 5 * .08f;
                _instances.SetInstanceTransform(plant * LeavesPerPlant + leaf,
                    new Transform3D(Basis.FromEuler(rotation).Scaled(Vector3.One * size), anchor));
            }
        }
    }

    private void Build()
    {
        _leafMesh = BotanicalGeometry.Leaf(1.05f, .28f, .18f);
        _leafMaterial = SurfaceMaterials.Create("ffffff", SurfaceKind.Cloth);
        _leafMaterial.VertexColorUseAsAlbedo = true;
        _leafMaterial.VertexColorIsSrgb = true;
        _instances = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = _leafMesh,
            InstanceCount = FoliageCapacity
        };
        for (int i = 0; i < FoliageCapacity; i++)
            _instances.SetInstanceColor(i, new Color(i % 3 == 0 ? "71865a" : i % 3 == 1 ? "42634e" : "587453"));
        _foliage = new MultiMeshInstance3D { Name = "WindFoliage", Multimesh = _instances, MaterialOverride = _leafMaterial };
        AddChild(_foliage);

        var b = new EnvironmentBuilder(this, "VerdantLightFixtures");
        float x = _halfWidth, z = _halfDepth;
        if (Style == "verdant_village")
        {
            Lantern(b, new(-x * .59f + 1.15f, 2.12f, -z - 1.34f));
            Lantern(b, new(x * .57f + 1.36f, 2.38f, -z - 1.69f));
            Fungi(b, new(-x - 1.15f, 0, -z * .42f));
            Fungi(b, new(x + 1.15f, 0, z * .18f));
        }
        else
        {
            Fungi(b, new(-x - 1.15f, 0, -z * .42f));
            Fungi(b, new(x * .38f, 0, -z - 1.15f));
            Fungi(b, new(x + 1.15f, 0, z * .18f));
            Fungi(b, new(-x * .48f, 0, -z - 1.35f));
        }
        b.Flush();
        foreach (var node in GetNode<Node3D>("VerdantLightFixtures").GetChildren().OfType<MeshInstance3D>())
            _fixtures.Add((node, node.Mesh, node.Mesh.SurfaceGetMaterial(0)));
    }

    private void Lantern(EnvironmentBuilder b, Vector3 p)
    {
        b.Branch(p + new Vector3(0, .52f, -.24f), p + Vector3.Up * .42f, .038f, .029f, "655944");
        b.Branch(p + Vector3.Up * .42f, p + Vector3.Up * .16f, .014f, .014f, "5e6655", SurfaceKind.Metal);
        b.Cylinder(.19f, .13f, .27f, p, "e5ba79", glow: true);
        b.Cylinder(.23f, .12f, .13f, p + Vector3.Up * .2f, "655944", surface: SurfaceKind.Wood);
        b.Cylinder(.20f, .20f, .06f, p - Vector3.Up * .17f, "655944", surface: SurfaceKind.Wood);
        foreach (float side in new[] { -1f, 1f })
            b.Branch(p + new Vector3(side * .15f, -.16f, .05f), p + new Vector3(side * .12f, .17f, .05f), .018f, .018f, "9a9470");
        Light(p, "ffd09a", 1.8f, 5.5f);
    }

    private void Fungi(EnvironmentBuilder b, Vector3 p)
    {
        string glow = Style == "verdant_heart" ? "aac7ad" : "92bfac";
        for (int i = 0; i < 3; i++)
        {
            var at = p + new Vector3(i * .25f - .25f, 0, i % 2 * .23f);
            float height = i == 1 ? .56f : .32f, radius = i == 1 ? .26f : .17f;
            b.Branch(at, at + new Vector3(.04f, height, -.02f), .044f, .031f, "9a9470", SurfaceKind.Bone);
            b.Cylinder(radius, radius * .72f, .036f, at + Vector3.Up * height, glow, glow: true);
            b.Cylinder(radius * 1.08f, radius * .36f, .17f, at + Vector3.Up * (height + .09f), "58796a", surface: SurfaceKind.Skin);
            b.Cylinder(radius * .54f, radius * .25f, .028f, at + Vector3.Up * (height + .187f), "799782", surface: SurfaceKind.Skin);
        }
        Light(p + Vector3.Up * .5f, glow, .85f, 3.8f);
    }

    private void Light(Vector3 position, string color, float energy, float range)
    {
        var light = new OmniLight3D
        {
            Name = "VerdantPractical" + _lights.Count,
            Position = position,
            LightColor = new(color),
            LightEnergy = energy,
            LightSpecular = .5f,
            OmniRange = range,
            OmniAttenuation = 1.5f,
            ShadowEnabled = false
        };
        AddChild(light); _lights.Add(light);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _released) return;
        _released = true;
        // Only resources created by this room are released; SurfaceMaterials' seven
        // shared texture sets stay owned by their factory.
        if (GodotObject.IsInstanceValid(_foliage)) { _foliage.Multimesh = null; _foliage.MaterialOverride = null; }
        foreach (var (node, mesh, material) in _fixtures)
        {
            if (GodotObject.IsInstanceValid(node)) node.Mesh = null;
            mesh.Dispose(); material.Dispose();
        }
        _fixtures.Clear();
        _instances?.Dispose(); _leafMesh?.Dispose(); _leafMaterial?.Dispose();
    }
}
