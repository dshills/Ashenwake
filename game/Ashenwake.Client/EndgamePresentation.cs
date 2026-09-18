using Ashenwake.Core.Combat;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Renders exact endgame warning/active geometry and interactable mechanism positions from Core.</summary>
public partial class EndgamePresentation : Node3D
{
    private sealed record HazardMeshes(Node3D Root, MeshInstance3D Fill, MeshInstance3D? Rim, MeshInstance3D? Start, MeshInstance3D? End, Label3D Label);
    private readonly Dictionary<long, HazardMeshes> _hazards = [];
    private readonly Dictionary<int, Node3D> _mechanisms = [];
    private string _context = "";
    private Label? _rules;
    public void AttachOverlay(Sandbox sandbox)
    {
        _rules = new Label { Position = new(32, 204), Size = new(540, 83), MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _rules.AddThemeFontSizeOverride("font_size", 13); _rules.AddThemeColorOverride("font_color", new Color("f2d4a2")); sandbox.AddOverlay(_rules);
    }
    public void Show(CombatEndgameView? view, CorePosition player, bool obscureRules)
    {
        string context = view?.ContextKey ?? "";
        if (_context != context) { Clear(); _context = context; }
        if (_rules is not null)
        {
            _rules.Visible = view is not null && !obscureRules;
            if (view is not null)
            {
                var lines = new List<string>();
                if (view.Counterplay.Length > 0) lines.Add(view.Counterplay);
                if (view.RuleIds.Contains("fracture.fragment_overcharge")) lines.Add(view.OverchargeActive
                    ? $"DIVINE SURGE · +50% fragment power · {view.OverchargeRemainingTicks / 30d:F1}s"
                    : $"Next divine surge in {view.NextOverchargeTicks / 30d:F1}s");
                if (view.RuleIds.Contains("fracture.resistance_inversion")) lines.Add($"Unequal shelter · +{view.HighestResistanceBasisPoints / 400d:F1}% damage from {Family(view.HighestResistanceFamily)}\nExposed {Family(view.LowestResistanceFamily)} resistance: {view.LowestResistanceBasisPoints / 100d:F1}%");
                _rules.Text = string.Join('\n', lines);
            }
        }
        if (view is null) return;
        var ids = view.Hazards.Select(h => h.Id).ToHashSet();
        foreach (long id in _hazards.Keys.Where(id => !ids.Contains(id)).ToArray()) { _hazards[id].Root.QueueFree(); _hazards.Remove(id); }
        foreach (var hazard in view.Hazards) Hazard(hazard);
        var mechanisms = view.Mechanisms.Select(m => m.Id).ToHashSet();
        foreach (int id in _mechanisms.Keys.Where(id => !mechanisms.Contains(id)).ToArray()) { _mechanisms[id].QueueFree(); _mechanisms.Remove(id); }
        foreach (var mechanism in view.Mechanisms)
        {
            if (!_mechanisms.TryGetValue(mechanism.Id, out var node))
            {
                node = new Node3D(); AddChild(node); _mechanisms.Add(mechanism.Id, node);
                float markerRadius = mechanism.Kind == "CoolingGap" ? mechanism.Radius * .001f : .65f;
                Mesh(new TorusMesh { InnerRadius = markerRadius - .08f, OuterRadius = markerRadius }, new(0, .08f, 0), new("98ead0"), node);
                if (mechanism.Kind == "CoolingGap") Mesh(new CylinderMesh { TopRadius = markerRadius, BottomRadius = markerRadius, Height = .02f }, new(0, .055f, 0), new Color(.25f, .85f, .7f, .17f), node);
                Mesh(new CylinderMesh { TopRadius = .22f, BottomRadius = .4f, Height = .55f }, new(0, .28f, 0), new("b9ba90"), node);
                node.AddChild(Label("", new(0, 1.15f, 0), new("e2f2cb")));
            }
            node.Position = Point(mechanism.Position);
            var label = node.GetChildren().OfType<Label3D>().Single();
            bool near = CorePosition.DistanceSquared(player, mechanism.Position) <= (long)mechanism.Radius * mechanism.Radius;
            label.Text = mechanism.Prompt + (mechanism.Kind == "CoolingGap" ? "\nSolar protection inside" : mechanism.Kind == "AnchorGlyph" ? "\nStable reference" : mechanism.Available ? near ? "\n[F] Interact" : "\nApproach marker" : "\nComplete");
            label.Modulate = mechanism.Available || mechanism.Kind is "CoolingGap" or "AnchorGlyph" ? new Color("d8eec0") : new Color("7e8b8d");
        }
    }
    private void Hazard(EndgameHazardView hazard)
    {
        float radius = hazard.Radius * .001f;
        bool line = hazard.Kind == "Line", active = hazard.Stage == "Active";
        Color color = active ? new Color(1, .23f, .14f, .4f) : new Color(1, .7f, .16f, .2f);
        var start = Point(hazard.Position); var end = Point(hazard.End); var delta = end - start;
        if (!_hazards.TryGetValue(hazard.Id, out var visual))
        {
            var root = new Node3D(); AddChild(root);
            var fill = Mesh(line ? new BoxMesh { Size = new(radius * 2, .03f, Math.Max(.01f, delta.Length())) }
                : new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .028f }, new(0, .1f, 0), color, root);
            MeshInstance3D? rim = null, capStart = null, capEnd = null;
            if (line)
            {
                capStart = Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .028f }, Vector3.Zero, color, root);
                capEnd = Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .028f }, Vector3.Zero, color, root);
            }
            else rim = Mesh(new TorusMesh { InnerRadius = Math.Max(.02f, radius - .06f), OuterRadius = radius }, new(0, .13f, 0), new("ffd16f"), root);
            var label = Label("", new(0, .55f, 0), new("ffe4bd")); root.AddChild(label);
            visual = new(root, fill, rim, capStart, capEnd, label); _hazards.Add(hazard.Id, visual);
        }
        visual.Root.Position = line ? (start + end) * .5f : start;
        visual.Fill.Rotation = line ? new(0, MathF.Atan2(delta.X, delta.Z), 0) : Vector3.Zero;
        if (visual.Fill.Mesh is BoxMesh box) box.Size = new(radius * 2, .03f, Math.Max(.01f, delta.Length()));
        if (visual.Start is not null) visual.Start.Position = start - visual.Root.Position + Vector3.Up * .1f;
        if (visual.End is not null) visual.End.Position = end - visual.Root.Position + Vector3.Up * .1f;
        foreach (var mesh in new[] { visual.Fill, visual.Start, visual.End }.Where(m => m is not null))
            ((StandardMaterial3D)mesh!.MaterialOverride).AlbedoColor = color;
        if (visual.Rim is not null) ((StandardMaterial3D)visual.Rim.MaterialOverride).AlbedoColor = active ? new("ff643e") : new("ffd16f");
        visual.Label.Text = (hazard.Sequence > 0 ? hazard.Sequence + " · " : "") + (active ? "DANGER" : $"{hazard.RemainingTicks / 30d:F1}s");
    }
    private void Clear()
    {
        foreach (var visual in _hazards.Values) visual.Root.QueueFree(); _hazards.Clear();
        foreach (var marker in _mechanisms.Values) marker.QueueFree(); _mechanisms.Clear();
    }
    private static Vector3 Point(CorePosition value) => new(value.X * .001f, 0, value.Z * .001f);
    private static string Family(DamageFamily family) => family switch
    { DamageFamily.PhysicalSlash => "Slash", DamageFamily.PhysicalCrush => "Crush", DamageFamily.PhysicalPierce => "Pierce", _ => family.ToString() };
    private static Label3D Label(string text, Vector3 position, Color color) => new()
    { Text = text, Position = position, FontSize = 40, PixelSize = .015f, OutlineSize = 4, Modulate = color, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
    private static MeshInstance3D Mesh(Mesh shape, Vector3 position, Color color, Node parent)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = shape,
            Position = position,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Transparency = color.A < 1 ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }
        };
        parent.AddChild(mesh); return mesh;
    }
}
