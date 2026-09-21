using Godot;

namespace Ashenwake.Client;

/// <summary>Single-use builder: Flush merges each palette into one static mesh and releases its authoring resources.</summary>
public sealed class EnvironmentBuilder
{
    private readonly Node3D _root;
    private bool _finished;
    private readonly Dictionary<(string Color, bool Glow, SurfaceKind Surface), List<(Mesh Mesh, Transform3D Transform)>> _parts = [];
    private readonly Dictionary<(float Inner, float Outer), TorusMesh> _rings = [];
    private readonly Dictionary<float, CylinderMesh> _cylinders = [];
    private readonly BoxMesh _box = new() { Size = Vector3.One };

    public EnvironmentBuilder(Node3D parent, string name)
    { _root = new Node3D { Name = name }; parent.AddChild(_root); }

    public void Box(Vector3 size, Vector3 position, string color, Vector3 rotationDegrees = default, bool glow = false, SurfaceKind? surface = null)
        => Part(_box, size, position, color, rotationDegrees, glow, surface);

    public void Cylinder(float bottomRadius, float topRadius, float height, Vector3 position, string color, Vector3 rotationDegrees = default, bool glow = false, SurfaceKind? surface = null)
    {
        EnsureOpen();
        float ratio = topRadius / bottomRadius;
        if (!_cylinders.TryGetValue(ratio, out var mesh))
            _cylinders[ratio] = mesh = new CylinderMesh { BottomRadius = 1, TopRadius = ratio, Height = 1, RadialSegments = 10, Rings = 1 };
        Part(mesh, new(bottomRadius, height, bottomRadius), position, color, rotationDegrees, glow, surface);
    }

    public void Torus(float innerRadius, float outerRadius, Vector3 position, string color, Vector3 rotationDegrees = default, bool glow = false, SurfaceKind? surface = null)
    {
        EnsureOpen();
        if (!_rings.TryGetValue((innerRadius, outerRadius), out var mesh))
            _rings[(innerRadius, outerRadius)] = mesh = new TorusMesh { InnerRadius = innerRadius, OuterRadius = outerRadius, Rings = 16, RingSegments = 6 };
        Part(mesh, Vector3.One, position, color, rotationDegrees, glow, surface);
    }

    public void Beam(Vector3 from, Vector3 to, float thickness, string color, SurfaceKind? surface = null)
    {
        EnsureOpen();
        Vector3 direction = to - from;
        if (direction.LengthSquared() < .00001f) return;
        var rotation = new Quaternion(Vector3.Up, direction.Normalized());
        Add(_box, new Transform3D(new Basis(rotation).ScaledLocal(new(thickness, direction.Length(), thickness)), (from + to) * .5f), color, false, surface);
    }

    private void Part(Mesh mesh, Vector3 size, Vector3 position, string color, Vector3 rotationDegrees, bool glow, SurfaceKind? surface)
        => Add(mesh, new Transform3D(Basis.FromEuler(rotationDegrees * (Mathf.Pi / 180)).ScaledLocal(size), position), color, glow, surface);

    private void Add(Mesh mesh, Transform3D transform, string color, bool glow, SurfaceKind? surface)
    {
        EnsureOpen();
        var key = (color, glow, surface ?? SurfaceMaterials.EnvironmentKind(color));
        if (!_parts.TryGetValue(key, out var parts)) _parts[key] = parts = [];
        parts.Add((mesh, transform));
    }

    private void EnsureOpen()
    {
        if (_finished) throw new InvalidOperationException("EnvironmentBuilder is single-use; create a new builder after Flush.");
    }

    public void Flush()
    {
        EnsureOpen();
        _finished = true;
        foreach (var (key, parts) in _parts)
        {
            using var surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var (mesh, transform) in parts) surface.AppendFrom(mesh, 0, transform);
            var material = SurfaceMaterials.Create(key.Color, key.Surface, key.Glow, worldScale: true);
            material.EmissionEnergyMultiplier = key.Glow ? 1.3f : 0;
            surface.SetMaterial(material);
            _root.AddChild(new MeshInstance3D { Name = "Stonework_" + key.Color + (key.Glow ? "_lit" : ""), Mesh = surface.Commit() });
        }
        _parts.Clear();
        // Authoring primitives are temporary; only the merged arrays remain attached to the scene.
        _box.Dispose();
        foreach (var mesh in _rings.Values) mesh.Dispose();
        foreach (var mesh in _cylinders.Values) mesh.Dispose();
        _rings.Clear(); _cylinders.Clear();
    }
}
