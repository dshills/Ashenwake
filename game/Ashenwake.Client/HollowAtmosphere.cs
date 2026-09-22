using Godot;

namespace Ashenwake.Client;

/// <summary>Steady inset lights and a bounded group of suspended slate fragments outside each Hollow room.</summary>
public partial class HollowAtmosphere : Node3D
{
    private readonly List<OmniLight3D> _lights = [];
    private readonly Vector3[] _origins = new Vector3[8];
    private readonly List<MeshInstance3D> _renderers = [];
    private readonly Dictionary<ulong, Mesh> _meshes = [];
    private readonly Dictionary<ulong, Material> _materials = [];
    private MultiMeshInstance3D _fragments = null!;
    private MultiMesh _instances = null!;
    private double _time;
    private bool _released;

    public string Style { get; private set; } = "";
    public double MotionTime => _time;
    public int LightCapacity => _lights.Count;
    public int ActiveLightCount => _released ? 0 : _lights.Count(light => light.Visible);
    public int FragmentCapacity => _origins.Length;
    public int ActiveFragmentCount => _released || !_fragments.Visible ? 0 : _instances.VisibleInstanceCount;
    public static bool Supports(string style) => style is "hollow_rooms" or "hollow_memory" or "hollow_vault" or "hollow_breach";

    public static HollowAtmosphere Create(string style, float halfWidth, float halfDepth)
    {
        if (!Supports(style)) throw new ArgumentException("Unknown Hollow environment.", nameof(style));
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
        var room = new HollowAtmosphere { Name = "HollowAtmosphere", Style = style };
        room.Build(halfWidth, halfDepth);
        room.Animate(0, true, false, "High");
        return room;
    }

    public void Animate(double delta, bool paused, bool reducedEffects, string quality)
    {
        if (_released) return;
        if (reducedEffects) _time = 0;
        else if (!paused && double.IsFinite(delta) && delta > 0) _time = (_time + Math.Min(delta, .1)) % 36;
        bool high = GraphicsProfile.Normalize(quality) == "High";
        for (int i = 0; i < _lights.Count; i++) _lights[i].Visible = high || i < 2;
        _fragments.Visible = !reducedEffects;
        _instances.VisibleInstanceCount = high ? 8 : 4;
        // The first four instances alternate sides, so Performance retains both clusters.
        // Periods divide the bounded clock exactly; the loop never jumps or uses shader TIME.
        for (int i = 0; i < _origins.Length; i++)
        {
            float drift = (float)Math.Sin(_time * Math.Tau / 12 + i * .8);
            float turn = (float)Math.Sin(_time * Math.Tau / 18 + i * .5);
            Vector3 p = _origins[i] + new Vector3(turn * .075f, drift * .12f, 0);
            var rotation = new Vector3(i * .19f + turn * .06f, i * .7f, i * .17f + drift * .085f);
            float scale = .85f + i % 4 * .15f;
            _instances.SetInstanceTransform(i, new(Basis.FromEuler(rotation).Scaled(Vector3.One * scale), p));
        }
    }

    private void Build(float x, float z)
    {
        var fixtures = new EnvironmentBuilder(this, "HollowInsetLights");
        bool memory = Style is "hollow_memory" or "hollow_vault";
        // The first pair lights the principal sealed facade and survives Performance mode.
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 p = Style switch
            {
                "hollow_rooms" => new(side * 1.978f, 1.25f, -z - 1.66f),
                "hollow_memory" => new(side * 2.4f, 1.30f, -z - (side < 0 ? 2.07f : 2.87f)),
                "hollow_vault" => new(side * 2.48f, 1.2f, -z - 1.73f),
                _ => new(side * 4.85f, 1.25f, -z - 3.10f)
            };
            Lamp(fixtures, p, 0, memory);
        }
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 p = Style switch
            {
                "hollow_rooms" => new(side * (x + .78f), 1.1f, -z * .65f),
                "hollow_memory" => new(side * (x + .91f), 1.1f, -z * .62f),
                "hollow_vault" => new(side * (x + .72f), 1.1f, -z * .68f),
                _ => new(side * (x + 1.45f), 1.1f, -z * .61f + .60f)
            };
            Lamp(fixtures, p, -side * 90, memory);
            // Broken plinths place the suspended masonry in the architecture, away from exits.
            Vector3 plinth = new(side * (x + 2.4f), 0, z * .84f);
            fixtures.Box(new(.93f, .18f, 1.02f), plinth + Vector3.Up * .09f, "4e586a", surface: SurfaceKind.Stone);
            fixtures.Box(new(.58f, .24f, .66f), plinth + new Vector3(side * .09f, .28f, -.06f), "737e91", new(0, side * 13, 0), surface: SurfaceKind.Stone);
            fixtures.Branch(plinth + new Vector3(-side * .22f, .38f, -.10f), plinth + new Vector3(-side * .26f, .66f, -.13f), .11f, .055f, "a0a8bc", SurfaceKind.Stone);
        }
        fixtures.Flush();
        Record(this);
        var shard = FragmentMesh();
        var material = SurfaceMaterials.Create(memory ? "8b829a" : "8291a2", SurfaceKind.Stone);
        shard.SurfaceSetMaterial(0, material);
        _meshes.Add(shard.GetInstanceId(), shard); _materials.Add(material.GetInstanceId(), material);
        _instances = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = shard, InstanceCount = _origins.Length };
        _fragments = new MultiMeshInstance3D { Name = "SuspendedSlate", Multimesh = _instances, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_fragments);
        for (int i = 0; i < _origins.Length; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            int rank = i / 2;
            float angle = rank * 2.4f;
            _origins[i] = new(side * (x + 2.4f) + MathF.Cos(angle) * .40f, 1.15f + rank * .62f,
                z * .84f + MathF.Sin(angle) * .32f);
        }
    }

    private void Lamp(EnvironmentBuilder b, Vector3 p, float yaw, bool memory)
    {
        var basis = Basis.FromEuler(Vector3.Up * Mathf.DegToRad(yaw));
        Vector3 At(float x, float y, float z) => p + basis * new Vector3(x, y, z);
        b.Box(new(.48f, .74f, .18f), At(0, 0, -.08f), "4e586a", new(0, yaw, 0), surface: SurfaceKind.Stone);
        b.Box(new(.30f, .52f, .08f), At(0, 0, .035f), "242833", new(0, yaw, 0), surface: SurfaceKind.Stone);
        foreach (float side in new[] { -1f, 1f })
            b.Branch(At(side * .205f, -.32f, .08f), At(side * .205f, .32f, .08f), .035f, .025f, "a0a8bc", SurfaceKind.Stone);
        b.Box(new(.48f, .10f, .28f), At(0, .35f, .025f), "737e91", new(0, yaw, 0), surface: SurfaceKind.Stone);
        b.Box(new(.40f, .08f, .25f), At(0, -.35f, .025f), "737e91", new(0, yaw, 0), surface: SurfaceKind.Stone);
        // Small inset spectral glass is steady; it never flashes like a warning or carries an exit anchor.
        b.Cylinder(.07f, .095f, .38f, At(0, 0, .09f), memory ? "b0a6bf" : "8cabad", glow: true, surface: SurfaceKind.Stone);
        var light = new OmniLight3D
        {
            Name = "MemorialLight" + _lights.Count,
            Position = At(0, 0, .22f),
            LightColor = new(memory ? "d5c4ee" : Style == "hollow_breach" ? "a7dce5" : "bbd9ed"),
            LightEnergy = memory ? 1.20f : 1.35f,
            OmniRange = 4.8f,
            OmniAttenuation = 1.5f,
            LightSpecular = .45f,
            ShadowEnabled = false
        };
        AddChild(light); _lights.Add(light);
    }

    private static ArrayMesh FragmentMesh()
    {
        Vector3[] vertices = [new(-.09f, -.06f, -.075f), new(.14f, -.025f, -.045f), new(.1f, .045f, .08f),
            new(-.07f, .02f, .10f), new(-.04f, .34f, .015f), new(.025f, -.29f, .02f)];
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int edge = 0; edge < 4; edge++)
        {
            Triangle(vertices[edge], vertices[(edge + 1) % 4], vertices[4]);
            Triangle(vertices[(edge + 1) % 4], vertices[edge], vertices[5]);
        }
        surface.Index(); surface.GenerateTangents();
        return surface.Commit();
        void Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            if ((b - a).Cross(c - a).Dot((a + b + c) / 3) > 0) (b, c) = (c, b);
            Vector3 normal = -(b - a).Cross(c - a).Normalized();
            Vector3 guide = Math.Abs(normal.Y) < .9f ? Vector3.Up : Vector3.Right;
            Vector3 u = guide.Cross(normal).Normalized(), v = normal.Cross(u);
            foreach (var point in new[] { a, b, c })
            {
                surface.SetNormal(normal); surface.SetUV(new(point.Dot(u), point.Dot(v))); surface.AddVertex(point);
            }
        }
    }

    private void Record(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is MeshInstance3D renderer)
            {
                _renderers.Add(renderer); _meshes.TryAdd(renderer.Mesh.GetInstanceId(), renderer.Mesh);
                for (int surface = 0; surface < renderer.Mesh.GetSurfaceCount(); surface++)
                    if (renderer.Mesh.SurfaceGetMaterial(surface) is { } material) _materials.TryAdd(material.GetInstanceId(), material);
            }
            Record(child);
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _released) return;
        _released = true;
        if (GodotObject.IsInstanceValid(_fragments)) _fragments.Multimesh = null;
        if (GodotObject.IsInstanceValid(_instances)) { _instances.Mesh = null; _instances.Dispose(); }
        foreach (var renderer in _renderers) if (GodotObject.IsInstanceValid(renderer)) renderer.Mesh = null;
        foreach (var mesh in _meshes.Values) mesh.Dispose();
        foreach (var material in _materials.Values) material.Dispose();
        _renderers.Clear(); _meshes.Clear(); _materials.Clear();
    }
}
