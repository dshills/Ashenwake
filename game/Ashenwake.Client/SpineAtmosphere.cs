using Godot;

namespace Ashenwake.Client;

/// <summary>Room-owned reading lights and folded banners outside the court's playable rectangle.</summary>
public partial class SpineAtmosphere : Node3D
{
    private readonly List<OmniLight3D> _lights = [];
    private readonly List<Node3D> _bannerJoints = [];
    private readonly List<MeshInstance3D> _renderers = [];
    private readonly Dictionary<ulong, Mesh> _meshes = [];
    private readonly Dictionary<ulong, Material> _materials = [];
    private bool _released;
    private double _time;

    public string Style { get; private set; } = "";
    public double MotionTime => _time;
    public int LightCapacity => _lights.Count;
    public int ActiveLightCount => _released ? 0 : _lights.Count(light => light.Visible);
    public int BannerCount => _bannerJoints.Count / 2;
    public static bool Supports(string style) => style is "spine_causeway" or "spine_hall" or "spine_archive" or "spine_memory" or "spine_warden";

    public static SpineAtmosphere Create(string style, float halfWidth, float halfDepth)
    {
        if (!Supports(style)) throw new ArgumentException("Unknown Spine environment.", nameof(style));
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
        var room = new SpineAtmosphere { Name = "SpineAtmosphere", Style = style };
        room.Build(halfWidth, halfDepth);
        room.Animate(0, true, false, "High");
        return room;
    }

    public void Animate(double delta, bool paused, bool reducedEffects, string quality)
    {
        if (_released) return;
        if (reducedEffects) _time = 0;
        else if (!paused && double.IsFinite(delta) && delta > 0) _time = (_time + Math.Min(delta, .1)) % 24;
        bool high = GraphicsProfile.Normalize(quality) == "High";
        for (int i = 0; i < _lights.Count; i++) _lights[i].Visible = high || i < 2;
        // Commensurate periods make the bounded clock wrap continuously. No TIME shader,
        // light flicker or mesh rebuilding; Reduced Effects settles even while paused.
        for (int i = 0; i < _bannerJoints.Count; i++)
            _bannerJoints[i].Rotation = new((float)(Math.Sin(_time * Math.Tau / (i % 2 == 0 ? 12 : 8)) * (i % 2 == 0 ? .045 : .085)), 0, 0);
    }

    private void Build(float x, float z)
    {
        bool memory = Style == "spine_memory";
        var fixtures = new EnvironmentBuilder(this, "SpinePracticalFixtures");
        // The first pair survives Performance mode and illuminates the room's actual focal props.
        foreach (float side in new[] { -1f, 1f })
        {
            if (Style == "spine_causeway")
                Light("BoneDwellingHearth", new(side * (x * .64f + .87f), 2.34f, -z - 2.15f), "ffd6a1", 1.8f, 5.2f);
            else
            {
                Vector3 p = Style switch
                {
                    "spine_archive" => new(side * x * .64f, 1.20f, -z - .92f),
                    "spine_hall" => new(side * 1.22f, 1.46f, -z - 1.01f),
                    "spine_memory" => new(side * 2.18f, 1.18f, -z - 2.35f),
                    _ => new(side * (x + 1.15f), 1.65f, -z * .51f)
                };
                if (Style is "spine_memory" or "spine_warden")
                {
                    fixtures.Cylinder(.18f, .27f, .14f, new(p.X, .07f, p.Z), "6d6c5d", surface: SurfaceKind.Metal);
                    fixtures.Branch(new(p.X, .14f, p.Z), p - Vector3.Up * .26f, .07f, .045f, "9c9070", SurfaceKind.Metal);
                }
                Lamp(fixtures, p, memory);
            }
        }
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 p = new(side * (x + 1.7f), 0, z * .86f);
            fixtures.Cylinder(.26f, .33f, .22f, p + Vector3.Up * .11f, memory ? "e5d7b5" : "7f908f", surface: SurfaceKind.Bone);
            fixtures.Branch(p + Vector3.Up * .20f, p + Vector3.Up * 3.35f, .055f, .040f, "6d6c5d", SurfaceKind.Metal);
            fixtures.Branch(p + new Vector3(-.47f, 3.02f, 0), p + new Vector3(.47f, 3.02f, 0), .025f, .025f, "9c9070", SurfaceKind.Metal);
            Lamp(fixtures, p + new Vector3(0, 3.48f, 0), memory);
            Banner(p + Vector3.Up * 2.98f, memory);
        }
        fixtures.Flush();
        Record(this);
    }

    private void Banner(Vector3 p, bool memory)
    {
        var upper = new Node3D { Name = "BannerUpper" + BannerCount, Position = p };
        var lower = new Node3D { Name = "BannerHem" + BannerCount, Position = new(0, -.76f, 0) };
        AddChild(upper); upper.AddChild(lower); _bannerJoints.Add(upper); _bannerJoints.Add(lower);
        var cloth = SurfaceMaterials.Create(memory ? "b6a57a" : "566878", SurfaceKind.Cloth);
        cloth.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        AddPanel(upper, false); AddPanel(lower, true);
        void AddPanel(Node3D parent, bool hem)
        {
            using var surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles); surface.SetMaterial(cloth);
            const int columns = 8;
            for (int col = 0; col < columns; col++)
            {
                float a = col / (float)columns, b = (col + 1) / (float)columns;
                Vector3 Point(float u, bool bottom) => new((u - .5f) * .72f,
                    bottom ? -.76f + (hem && !memory ? (col % 3 == 0 ? .10f : 0) : 0) : 0,
                    MathF.Sin(u * Mathf.Tau * 2) * .045f);
                Vector3 tl = Point(a, false), tr = Point(b, false), bl = Point(a, true), br = Point(b, true);
                Vertex(tl, new(a, 0)); Vertex(bl, new(a, 1)); Vertex(tr, new(b, 0));
                Vertex(tr, new(b, 0)); Vertex(bl, new(a, 1)); Vertex(br, new(b, 1));
            }
            surface.GenerateNormals(); surface.GenerateTangents(); surface.Index();
            parent.AddChild(new MeshInstance3D { Name = "FoldedCloth", Mesh = surface.Commit() });
            void Vertex(Vector3 vertex, Vector2 uv) { surface.SetUV(uv); surface.AddVertex(vertex); }
        }
    }

    private void Lamp(EnvironmentBuilder b, Vector3 p, bool memory)
    {
        b.Cylinder(.19f, .24f, .075f, p - Vector3.Up * .26f, "6d6c5d", surface: SurfaceKind.Metal);
        b.Cylinder(.052f, .075f, .22f, p - Vector3.Up * .14f, "9c9070", surface: SurfaceKind.Metal);
        b.Cylinder(.12f, .12f, .21f, p + Vector3.Up * .07f, "b58b52", glow: true, surface: SurfaceKind.Metal);
        b.Cylinder(.20f, .08f, .10f, p + Vector3.Up * .22f, "6d6c5d", surface: SurfaceKind.Metal);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi / 2;
            Vector3 radial = new(MathF.Sin(a) * .13f, 0, MathF.Cos(a) * .13f);
            b.Branch(p + radial - Vector3.Up * .04f, p + radial + Vector3.Up * .20f, .012f, .012f, "9c9070", SurfaceKind.Metal);
        }
        Light("WitnessLamp", p + Vector3.Back * .13f, memory ? "ffe0a6" : "ffd8af", memory ? 1.35f : 1.65f, 4.4f);
    }

    private void Light(string name, Vector3 p, string color, float energy, float range)
    {
        var light = new OmniLight3D
        {
            Name = name + _lights.Count,
            Position = p,
            LightColor = new(color),
            LightEnergy = energy,
            LightSpecular = .5f,
            OmniRange = range,
            OmniAttenuation = 1.5f,
            ShadowEnabled = false
        };
        AddChild(light); _lights.Add(light);
    }

    private void Record(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is MeshInstance3D renderer)
            {
                _renderers.Add(renderer); _meshes.TryAdd(renderer.Mesh.GetInstanceId(), renderer.Mesh);
                for (int i = 0; i < renderer.Mesh.GetSurfaceCount(); i++)
                    if (renderer.Mesh.SurfaceGetMaterial(i) is { } material) _materials.TryAdd(material.GetInstanceId(), material);
            }
            Record(child);
        }
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _released) return;
        _released = true;
        foreach (var renderer in _renderers) if (GodotObject.IsInstanceValid(renderer)) renderer.Mesh = null;
        foreach (var mesh in _meshes.Values) mesh.Dispose();
        foreach (var material in _materials.Values) material.Dispose();
        _renderers.Clear(); _meshes.Clear(); _materials.Clear();
    }
}
