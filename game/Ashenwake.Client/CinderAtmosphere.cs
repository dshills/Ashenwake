using Godot;

namespace Ashenwake.Client;

/// <summary>Bounded practical light and pump flywheels around Cinder's authored rooms.
/// Every fixture is outside the fighting floor; time and resources belong to this room.</summary>
public partial class CinderAtmosphere : Node3D
{
    private readonly List<OmniLight3D> _lights = [];
    private readonly List<Node3D> _wheels = [];
    private readonly List<(MeshInstance3D Node, Mesh Mesh, Material Material)> _resources = [];
    private bool _released;
    private double _time;

    public string Style { get; private set; } = "";
    public double MotionTime => _time;
    public int LightCapacity => _lights.Count;
    public int ActiveLightCount => _released ? 0 : _lights.Count(light => light.Visible);
    public int MechanismCount => _wheels.Count;

    public static bool Supports(string style) => style is "cinder_fields" or "cinder_extraction" or "cinder_furnace" or "cinder_foundry" or "cinder_storm";

    public static CinderAtmosphere Create(string style, float halfWidth, float halfDepth)
    {
        if (!Supports(style)) throw new ArgumentException("Unknown Cinder environment.", nameof(style));
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
        var room = new CinderAtmosphere { Name = "CinderAtmosphere", Style = style };
        room.Build(halfWidth, halfDepth);
        room.Animate(0, true, false, "High");
        return room;
    }

    public void Animate(double delta, bool paused, bool reducedEffects, string quality)
    {
        if (_released) return;
        if (reducedEffects) _time = 0;
        else if (!paused && double.IsFinite(delta) && delta > 0) _time = (_time + Math.Min(delta, .1)) % 40;
        bool high = GraphicsProfile.Normalize(quality) == "High";
        for (int i = 0; i < _lights.Count; i++) _lights[i].Visible = high || i < 2;
        // Light stays steady in both modes, so decorative heat cannot resemble a vent tell.
        for (int i = 0; i < _wheels.Count; i++)
            _wheels[i].Rotation = new(0, 0, (float)(_time * Math.Tau / 40) * (i % 2 == 0 ? 1 : -1));
    }

    private void Build(float x, float z)
    {
        var fixtures = new EnvironmentBuilder(this, "CinderPracticalFixtures");
        if (Style == "cinder_fields")
        {
            Light("OpenForge", new(x * .57f, .92f, -z - 1.85f), "ffc18b", 2.05f, 5.7f);
            Light("FoundryWindow", new(-x * .57f + 4.3f * .24f, 4.6f * .74f, -z - 1.60f), "ffd4a0", 1.45f, 4.4f);
            Wheel(new(x + 2.3f, 2.4f * .61f, -z * .49f + .92f));
            CanalLights(x, z);
        }
        else if (Style is "cinder_extraction" or "cinder_furnace")
        {
            bool furnace = Style == "cinder_furnace";
            foreach (float side in new[] { -1f, 1f })
            {
                var pump = new Vector3(side * (x + (furnace ? 2.55f : 2.45f)), 0, furnace ? -z * .38f : z * .4f);
                float height = furnace ? 3.55f : 1.55f;
                Wheel(pump + new Vector3(0, height * .61f, .92f));
                Lamp(fixtures, pump + new Vector3(side * .63f, height * .72f, .87f), false);
            }
            if (furnace) CanalLights(x, z);
            else
                foreach (float side in new[] { -1f, 1f })
                    Lamp(fixtures, new(side * x * .61f, 4.05f, -z - 3.15f), false);
        }
        else if (Style == "cinder_foundry")
        {
            // The locked forge remains cold. These small shelter lamps illuminate its
            // doors and bedrolls without suggesting an active furnace or an open passage.
            foreach (float side in new[] { -1f, 1f }) Lamp(fixtures, new(side * 2.77f, 2.35f, -z - .90f), true);
            foreach (float side in new[] { -1f, 1f }) Lamp(fixtures, new(side * (x + 1.35f), .94f, z * .18f), true);
        }
        else
        {
            // Steady shielded collector lamps; no decorative lightning, flashes or motion.
            foreach (float side in new[] { -1f, 1f }) Lamp(fixtures, new(side * x * .64f, 2.18f, -z - 2.55f), true);
            CanalLights(x, z);
        }
        fixtures.Flush();
        void Record(Node root)
        {
            foreach (var child in root.GetChildren())
            {
                if (child is MeshInstance3D mesh) _resources.Add((mesh, mesh.Mesh, mesh.Mesh.SurfaceGetMaterial(0)));
                Record(child);
            }
        }
        Record(this);
    }

    private void CanalLights(float x, float z)
    {
        foreach (float side in new[] { -1f, 1f })
            Light("LavaBank", new(side * (x + 3.75f), .40f, side * z * .30f), "eb976b", Style == "cinder_storm" ? .7f : 1.1f, 4.2f);
    }

    private void Lamp(EnvironmentBuilder b, Vector3 p, bool quiet)
    {
        b.Branch(p + new Vector3(0, .50f, -.17f), p + Vector3.Up * .40f, .028f, .022f, "727477", SurfaceKind.Metal);
        b.Branch(p + Vector3.Up * .40f, p + Vector3.Up * .18f, .012f, .012f, "41454b", SurfaceKind.Metal);
        b.Cylinder(.12f, .12f, .24f, p, quiet ? "bba88d" : "dcb181", glow: true, surface: SurfaceKind.Metal);
        b.Cylinder(.20f, .08f, .13f, p + Vector3.Up * .19f, "41454b", surface: SurfaceKind.Metal);
        b.Cylinder(.16f, .16f, .06f, p - Vector3.Up * .16f, "41454b", surface: SurfaceKind.Metal);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi / 2;
            Vector3 radial = new(MathF.Sin(a) * .135f, 0, MathF.Cos(a) * .135f);
            b.Branch(p + radial - Vector3.Up * .14f, p + radial + Vector3.Up * .14f, .013f, .013f, "a07753", SurfaceKind.Metal);
        }
        Light("InspectionLamp", p + Vector3.Back * .13f, quiet ? "e3d1b5" : "ffd0a0", quiet ? .95f : 1.45f, quiet ? 4 : 4.7f);
    }

    private void Wheel(Vector3 position)
    {
        var wheel = new Node3D { Name = "PumpFlywheel" + _wheels.Count, Position = position };
        AddChild(wheel); _wheels.Add(wheel);
        var b = new EnvironmentBuilder(wheel, "SpokedFlywheel");
        b.Torus(.20f, .245f, Vector3.Zero, "a07753", new(90, 0, 0), surface: SurfaceKind.Metal);
        b.Cylinder(.070f, .070f, .10f, Vector3.Zero, "727477", new(90, 0, 0), surface: SurfaceKind.Metal);
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.Tau / 5;
            b.Branch(Vector3.Zero, new(MathF.Sin(a) * .215f, MathF.Cos(a) * .215f, 0), .019f, .015f, "727477", SurfaceKind.Metal);
        }
        b.Flush();
    }

    private void Light(string name, Vector3 position, string color, float energy, float range)
    {
        var light = new OmniLight3D
        {
            Name = name + _lights.Count,
            Position = position,
            LightColor = new(color),
            LightEnergy = energy,
            LightSpecular = .65f,
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
        foreach (var (node, mesh, material) in _resources)
        {
            if (GodotObject.IsInstanceValid(node)) node.Mesh = null;
            mesh.Dispose(); material.Dispose();
        }
        _resources.Clear();
    }
}
