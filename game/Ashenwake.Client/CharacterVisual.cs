using Godot;

namespace Ashenwake.Client;

/// <summary>Articulated, cosmetic models. No physics bodies, root motion, or gameplay clocks.</summary>
public partial class CharacterVisual : Node3D
{
    private sealed record Limb(Node3D Node, string Motion, float Amplitude, Vector3 Rest, Vector3 Origin);
    private static readonly Dictionary<string, Mesh[]> MeshTemplates = new(StringComparer.Ordinal);
    private static readonly Dictionary<(string Color, SurfaceKind Kind, bool Emissive), StandardMaterial3D> SharedMaterials = [];
    private const int MeshTemplateLimit = 96, MaterialTemplateLimit = 256;
    private readonly List<Limb> _limbs = [];
    private readonly Node3D BodyRoot = new();
    private StandardMaterial3D Accent = null!;
    private StandardMaterial3D Main = null!, Dark = null!, Metal = null!, Bone = null!, Skin = null!, Glow = null!;
    private double _time;
    private float _stride, _windup, _recovery, _facing = -2.7f;
    public float Height { get; private set; } = 2.3f;
    public Color AccentColor => Accent.AlbedoColor;
    public Color BaseAccentColor { get; private set; }
    internal static (int Models, int Materials) CachedResourceCounts => (MeshTemplates.Count, SharedMaterials.Count);

    public static CharacterVisual Create(string definitionId, string role, string discipline = "", bool allied = false, CharacterAppearance? appearance = null)
    {
        var visual = new CharacterVisual(); visual.AddChild(visual.BodyRoot);
        visual._appearance = discipline.Length > 0 ? appearance : null;
        if (discipline.Length > 0) visual.BuildHumanoid(discipline);
        else if (allied) { visual.BuildHumanoid("Gravecaller"); visual.SetAccent(new("af9cff")); }
        else if (definitionId == "training.effigy") visual.BuildTrainingEffigy();
        else if (visual.TryBuildRoamingChampion(definitionId)) { }
        else visual.BuildMonster(definitionId, role.ToLowerInvariant().Replace(" elite", "", StringComparison.Ordinal));
        visual.ConfigureAnimation(definitionId, role, discipline, allied);
        string key = discipline.Length > 0 ? "hero:" + discipline.ToLowerInvariant() : allied ? "ally:gravecaller" :
            "monster:" + definitionId.ToLowerInvariant() + "/" + role.ToLowerInvariant().Replace(" elite", "", StringComparison.Ordinal);
        visual.Finish(visual._appearance is null ? key : key + ":cloth-base");
        if (visual._appearance is not null)
        {
            visual.BuildEquipment(visual._appearance);
            visual.BuildManifestations(visual._appearance.ManifestationMask);
            visual.BuildAnatomy(visual._appearance.AnatomyMask);
            visual.CaptureRigBounds();
        }
        return visual;
    }

    public static CharacterVisual CreateNpc(string npcId)
    {
        var visual = new CharacterVisual(); visual.AddChild(visual.BodyRoot);
        visual.BuildHumanoid("", npcId); visual.Finish("npc:" + npcId.ToLowerInvariant()); return visual;
    }

    public void SetAccent(Color color) => Accent.AlbedoColor = color;

    /// <param name="movement">World-space displacement per Core tick, repeated at render frequency.</param>
    public void Animate(double delta, Vector3 movement, bool windup = false, string state = "", bool paused = false, Vector3? facing = null)
    {
        if (paused) return;
        float dt = (float)Math.Clamp(delta, 0, .1);
        if (IsDying) { AnimateDeath(dt); return; }
        AdvanceCue(dt, windup);
        _time += dt;
        AnimateLocomotion(dt, movement, windup, state, facing);
        AnimateFamilyAnticipation();
        AnimateCue();
        BlendPoseRelease(dt, windup);
        AnimateManifestations();
    }

    private void Palette(string main, string accent, string dark, string metal, string bone, string skin, string glow)
    {
        Main = SharedMaterial(main); Dark = SharedMaterial(dark); Metal = SharedMaterial(metal, metallic: true);
        Bone = SharedMaterial(bone, kind: SurfaceKind.Bone); Skin = SharedMaterial(skin, kind: SurfaceKind.Skin); Glow = SharedMaterial(glow, emissive: true);
        Accent = SurfaceMaterials.Create(accent, SurfaceKind.Cloth);
    }
    private static StandardMaterial3D SharedMaterial(string color, bool metallic = false, bool emissive = false, SurfaceKind kind = SurfaceKind.Cloth)
    {
        var key = (color, metallic ? SurfaceKind.Metal : kind, emissive);
        if (SharedMaterials.TryGetValue(key, out var existing)) return existing;
        var material = SurfaceMaterials.Create(color, key.Item2, emissive);
        if (SharedMaterials.Count < MaterialTemplateLimit) SharedMaterials.Add(key, material);
        return material;
    }

    private Node3D Joint(Node parent, Vector3 at, string motion, float amplitude = 1)
    {
        var joint = new Node3D { Position = at }; parent.AddChild(joint);
        _limbs.Add(new(joint, motion, amplitude, joint.Rotation, joint.Position)); return joint;
    }

    private static MeshInstance3D Box(Node parent, Vector3 at, Vector3 size, Material mat, Vector3? rotationDegrees = null)
        => Part(parent, PolishedBoxMesh(size), at, mat, rotationDegrees);
    private static MeshInstance3D Orb(Node parent, Vector3 at, Vector3 scale, Material mat)
    {
        var mesh = Part(parent, OrganicSphereMesh, at, mat);
        mesh.Scale = scale; return mesh;
    }
    private static MeshInstance3D Cone(Node parent, Vector3 at, float bottomRadius, float topRadius, float height, Material mat, Vector3? rotationDegrees = null)
        => Part(parent, RoundedConeMesh(bottomRadius, topRadius, height), at, mat, rotationDegrees);
    private static MeshInstance3D Rod(Node parent, Vector3 start, Vector3 end, float radius, Material mat)
    {
        var direction = end - start;
        var mesh = Cone(parent, (start + end) * .5f, radius, radius * .82f, Math.Max(.001f, direction.Length()), mat);
        if (direction.LengthSquared() > .000001f) mesh.Quaternion = new Quaternion(Vector3.Up, direction.Normalized());
        return mesh;
    }
    private static MeshInstance3D Ring(Node parent, Vector3 at, float inner, float outer, Material mat, Vector3? rotationDegrees = null)
        => Part(parent, RoundedRingMesh(inner, outer), at, mat, rotationDegrees);
    private static MeshInstance3D Part(Node parent, Mesh mesh, Vector3 at, Material mat, Vector3? rotationDegrees = null)
    {
        var part = new MeshInstance3D { Mesh = mesh, Position = at, MaterialOverride = mat, RotationDegrees = rotationDegrees ?? Vector3.Zero };
        parent.AddChild(part); return part;
    }

    private void Finish(string key)
    {
        BaseAccentColor = AccentColor;
        // Combine static pieces within each joint/material so detail does not cost a draw per rivet.
        MeshTemplates.TryGetValue(key, out var cached);
        var generated = new List<Mesh>(); int groupIndex = 0;
        Batch(BodyRoot, generated, cached, ref groupIndex);
        if (cached is null && MeshTemplates.Count < MeshTemplateLimit) MeshTemplates.Add(key, generated.ToArray());
        CaptureRigBounds();
        BodyRoot.Rotation = new(0, _facing, 0);
    }

    private void CaptureRigBounds()
    {
        Height = Math.Max(Height, Top(BodyRoot, Transform3D.Identity));
        for (int i = 0; i < _limbs.Count; i++) _limbs[i] = _limbs[i] with { Rest = _limbs[i].Node.Rotation };
        _deathFromRotations = new Vector3[_limbs.Count];
        _deathFromPositions = new Vector3[_limbs.Count];
    }
    private static float Top(Node3D node, Transform3D parentTransform)
    {
        Transform3D transform = parentTransform * node.Transform;
        float top = 0;
        if (node is MeshInstance3D mesh)
            for (int i = 0; i < 8; i++) top = Math.Max(top, (transform * mesh.Mesh.GetAabb().GetEndpoint(i)).Y);
        foreach (var child in node.GetChildren().OfType<Node3D>()) top = Math.Max(top, Top(child, transform));
        return top;
    }
    internal static void Batch(Node3D parent, List<Mesh> generated, IReadOnlyList<Mesh>? cached, ref int groupIndex)
    {
        foreach (var child in parent.GetChildren().OfType<Node3D>().Where(n => n is not MeshInstance3D).ToArray())
            Batch(child, generated, cached, ref groupIndex);
        foreach (var group in parent.GetChildren().OfType<MeshInstance3D>().GroupBy(m => m.MaterialOverride).ToArray())
        {
            var pieces = group.ToArray();
            Mesh geometry;
            if (cached is not null) geometry = cached[groupIndex];
            else if (pieces.Length == 1) geometry = pieces[0].Mesh;
            else
            {
                using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
                foreach (var piece in pieces) surface.AppendFrom(piece.Mesh, 0, piece.Transform);
                geometry = surface.Commit();
            }
            groupIndex++; if (cached is null) generated.Add(geometry);
            if (pieces.Length == 1) { pieces[0].Mesh = geometry; continue; }
            var mesh = new MeshInstance3D { Mesh = geometry, MaterialOverride = group.Key };
            parent.AddChild(mesh);
            foreach (var piece in pieces) { parent.RemoveChild(piece); piece.Free(); }
        }
    }
}
