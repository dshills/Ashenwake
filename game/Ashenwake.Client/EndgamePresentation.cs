using Ashenwake.Core.Combat;
using Godot;
using System.Globalization;
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
    private PanelContainer? _rulePanel;
    private CombatEndgameView? _view;
    private Sandbox? _sandbox;
    public Node3D? GetMechanismVisual(int id) => _mechanisms.GetValueOrDefault(id);
    public void AttachOverlay(Sandbox sandbox)
    {
        _sandbox = sandbox;
        _rulePanel = new PanelContainer { Name = "EndgameRulePanel", MouseFilter = Control.MouseFilterEnum.Ignore };
        _rulePanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("101c28e8"),
            BorderColor = new("746954"),
            BorderWidthLeft = 2,
            ContentMarginLeft = 10,
            ContentMarginRight = 10,
            ContentMarginTop = 6,
            ContentMarginBottom = 6
        });
        _rules = new Label { Name = "EndgameRuleCues", MouseFilter = Control.MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _rules.AddThemeFontSizeOverride("font_size", 12); _rules.AddThemeColorOverride("font_color", new Color("f2d4a2"));
        _rulePanel.AddChild(_rules); sandbox.AddOverlay(_rulePanel); sandbox.AttachEndgameCues(this, _rulePanel);
        _rulePanel.Visible = false;
    }
    internal void LayoutOverlay(float objectiveBottom)
    {
        if (_rulePanel is null) return;
        _rulePanel.Position = new(22, Math.Max(196, objectiveBottom + 6));
        _rulePanel.Size = new(Math.Min(565, GetViewport().GetVisibleRect().Size.X - 322), 0);
    }
    public override void _ExitTree()
    {
        if (_sandbox is not null && GodotObject.IsInstanceValid(_sandbox)) _sandbox.DetachEndgameCues(this);
        if (_rulePanel is not null && GodotObject.IsInstanceValid(_rulePanel)) _rulePanel.QueueFree();
    }
    public void Show(CombatEndgameView? view, CorePosition player, bool obscureRules)
    {
        string context = view?.ContextKey ?? "";
        if (_context != context) { Clear(); _context = context; }
        _view = view;
        if (_rules is not null && _rulePanel is not null)
        {
            _rulePanel.Visible = view is not null && !obscureRules;
            if (view is not null) _rules.Text = RuleText(view);
            LayoutOverlay(192);
        }
        if (view is null) return;
        var ids = view.Hazards.Select(h => h.Id).ToHashSet();
        foreach (long id in _hazards.Keys.Where(id => !ids.Contains(id)).ToArray()) { RemoveVisual(_hazards[id].Root); _hazards.Remove(id); }
        foreach (var hazard in view.Hazards) Hazard(hazard);
        var mechanisms = view.Mechanisms.Select(m => m.Id).ToHashSet();
        foreach (int id in _mechanisms.Keys.Where(id => !mechanisms.Contains(id)).ToArray()) { RemoveVisual(_mechanisms[id]); _mechanisms.Remove(id); }
        foreach (var mechanism in view.Mechanisms)
        {
            if (!_mechanisms.TryGetValue(mechanism.Id, out var node))
            {
                node = new Node3D { Name = "EndgameMechanism_" + mechanism.Id }; AddChild(node); _mechanisms.Add(mechanism.Id, node);
                float markerRadius = mechanism.Kind == "CoolingGap" ? mechanism.Radius * .001f : .65f;
                Mesh(new TorusMesh { InnerRadius = markerRadius - .08f, OuterRadius = markerRadius }, new(0, .08f, 0), new("98ead0"), node);
                if (mechanism.Kind == "CoolingGap") Mesh(new CylinderMesh { TopRadius = markerRadius, BottomRadius = markerRadius, Height = .02f }, new(0, .055f, 0), new Color(.25f, .85f, .7f, .17f), node);
                Mesh(new CylinderMesh { TopRadius = .22f, BottomRadius = .4f, Height = .55f }, new(0, .28f, 0), new("b9ba90"), node);
                var caption = Label("", new(0, 1.15f, 0), new("e2f2cb")); caption.Name = "MechanismLabel_" + mechanism.Id; node.AddChild(caption);
                var marker = Mesh(new TorusMesh { InnerRadius = .85f, OuterRadius = .96f }, new(0, .1f, 0), new("ffe297"), node);
                marker.Name = "PriorityMarker";
            }
            node.Position = Point(mechanism.Position);
            var label = node.GetChildren().OfType<Label3D>().Single();
            bool near = CorePosition.DistanceSquared(player, mechanism.Position) <= (long)mechanism.Radius * mechanism.Radius;
            bool priority = view.BossCue?.PriorityMechanismIds.Contains(mechanism.Id) == true;
            string instruction = mechanism.Kind switch
            {
                "CoolingGap" => "SOLAR PROTECTION INSIDE",
                "AnchorGlyph" => "STABLE REFERENCE",
                _ when view.BossCue is null => "INACTIVE",
                _ when mechanism.Used => "COMPLETE",
                _ when mechanism.Available => priority ? "◆ USE TO EXPOSE" : near ? "CLICK TO USE" : "APPROACH TO USE",
                "SilentBell" => "WINDOW OPEN",
                "OathPlinth" => "CARRY A TERM FIRST",
                "BrokenTerm" => "DEPOSIT CARRIED TERM",
                _ => ""
            };
            label.Text = mechanism.Prompt.ToUpperInvariant() + "\n" + instruction;
            label.Modulate = priority ? new Color("ffe297") : mechanism.Available || mechanism.Kind is "CoolingGap" or "AnchorGlyph" ? new Color("d8eec0") : new Color("9aa5a7");
            node.GetNode<MeshInstance3D>("PriorityMarker").Visible = priority;

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
            var root = new Node3D { Name = "EndgameWarning_" + hazard.Id }; AddChild(root);
            var fill = Mesh(line ? new BoxMesh { Size = new(radius * 2, .03f, Math.Max(.01f, delta.Length())) }
                : new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .028f }, new(0, .1f, 0), color, root);
            MeshInstance3D? rim = null, capStart = null, capEnd = null;
            if (line)
            {
                capStart = Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .028f }, Vector3.Zero, color, root);
                capEnd = Mesh(new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .028f }, Vector3.Zero, color, root);
            }
            else rim = Mesh(new TorusMesh { InnerRadius = Math.Max(.02f, radius - .06f), OuterRadius = radius }, new(0, .13f, 0), new("ffd16f"), root);
            var label = Label("", new(0, .55f, 0), new("ffe4bd")); label.Name = "EndgameHazard_" + hazard.Id; root.AddChild(label);
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
        string order = hazard.SequenceCount > 0 ? $" {hazard.SequenceIndex}/{hazard.SequenceCount}" : hazard.LaneIndex > 0 ? $" · LANE {hazard.LaneIndex}" : "";
        string timing = active ? hazard.ContentId == "endgame.elite_scar" ? "DANGER · PERSISTENT" : "DANGER" : Seconds(hazard.RemainingTicks);
        visual.Label.Text = HazardName(hazard.ContentId) + order + "\n" + timing;
    }
    internal void LayoutLabels(Camera3D camera, Rect2 safe, List<Rect2> occupied)
    {
        if (_view is not { } view) return;
        foreach (var mechanism in view.Mechanisms.OrderBy(m => view.BossCue?.PriorityMechanismIds.Contains(m.Id) == true ? 0 : 1).ThenBy(m => m.Id))
            if (_mechanisms.TryGetValue(mechanism.Id, out var node))
                CombatLabelLayout.Place(camera, node.GetNode<Label3D>("MechanismLabel_" + mechanism.Id), node.GlobalPosition + Vector3.Up * 1.15f, safe, occupied);
        foreach (var hazard in view.Hazards.OrderBy(h => h.Stage == "Warning" ? 0 : 1).ThenBy(h => h.RemainingTicks).ThenBy(h => h.Id))
            if (_hazards.TryGetValue(hazard.Id, out var visual))
                CombatLabelLayout.Place(camera, visual.Label, visual.Root.GlobalPosition + Vector3.Up * .55f, safe, occupied);
    }
    internal static string HazardName(string id) => id switch
    {
        "endgame.healing_echo" => "HEALING ECHO · MOVE AWAY",
        "endgame.elite_scar" => "ELITE SCAR · KEEP CLEAR",
        "hunt.vael.vent" => "FORGE VENT",
        "hunt.vael.rebuild" => "LIMB FIRE CHANNEL",
        "hunt.vael.solar" => "SOLAR LINE",
        "hunt.ilyra.jaw_trail" => "ROOT TRAIL",
        "hunt.ilyra.surfacing_jaw" => "SURFACING JAW",
        "hunt.ilyra.brood_channel" => "BROOD CHANNEL",
        "hunt.ilyra.seed_pulse" => "SEED PULSE",
        "hunt.serath.procession" => "MEMORY PROCESSION",
        "hunt.marked_repetition" => "MARKED REPETITION",
        "hunt.serath.bell_pulse" => "BELL PULSE",
        "hunt.orrun.numbered_fault" => "FAULT",
        "hunt.orrun.carried_term" => "TERM STRIKE",
        "hunt.orrun.oathless_slam" => "OATHLESS SLAM",
        "hunt.nhal.absent_lane" => "ABSENT SPACE",
        "hunt.nhal.remembered_rhythm" => "REVERSED RHYTHM",
        _ => "BOSS STRIKE"
    };
    private static string Seconds(long ticks) => (Math.Ceiling(ticks / 3d) / 10).ToString("F1", CultureInfo.InvariantCulture) + "s";
    private static string RuleText(CombatEndgameView view)
    {
        var lines = new List<string>();
        if (view.HuntId.Length > 0) lines.Add(view.Counterplay);
        foreach (string rule in view.RuleIds)
        {
            string text = rule switch
            {
                "fracture.burning_haste" => $"FEVERED CINDERS · {view.HastedActorIds?.Length ?? 0} moving faster · keep distance",
                "fracture.healing_echoes" => $"HEALING ECHOES · {view.Hazards.Count(h => h.ContentId == "endgame.healing_echo")} incoming · leave healing spot",
                "fracture.elite_hazards" => $"ELITE SCARS · {view.Hazards.Count(h => h.ContentId == "endgame.elite_scar")} present · keep clear",
                "fracture.fragment_overcharge" => view.OverchargeActive ? "DIVINE SURGE · +50% fragment power · " + Seconds(view.OverchargeRemainingTicks) : "DIVINE SURGE · next in " + Seconds(view.NextOverchargeTicks),
                "fracture.resistance_inversion" => $"UNEQUAL SHELTER · +{(view.HighestResistanceBasisPoints / 400d).ToString("F1", CultureInfo.InvariantCulture)}% damage · {Family(view.LowestResistanceFamily)} {(view.LowestResistanceBasisPoints / 100d).ToString("F1", CultureInfo.InvariantCulture)}% resist",
                "fracture.inherited_boss" => view.EncounterIndex < view.EncounterCount - 1 ? "INHERITANCE · elite traits await in final room" : "INHERITED · " + string.Join(" / ", view.BossModifiers.Select(TraitCounter)),
                _ => ""
            };
            if (text.Length > 0) lines.Add(text);
        }
        if (view.BossCue is not null && view.BossModifiers.Length > 0 && !view.RuleIds.Contains("fracture.inherited_boss"))
            lines.Add("BOSS TRAITS · " + string.Join(" / ", view.BossModifiers.Select(TraitCounter)));
        return string.Join('\n', lines);
    }
    internal static string TraitCounter(string trait) => trait switch
    {
        "Stormbound" => "STORMBOUND: dodge links",
        "Null" => "NULL: leave marked field",
        "Riftborn" => "RIFTBORN: dodge rift strike",
        "Hunter" => "HUNTER: stationary here",
        "Devourer" => "DEVOURER: kill allies first",
        "Gravewake" => "GRAVEWAKE: revives one ally",
        "Martyr" => "MARTYR: kill allies first",
        "Dirgebound" => "DIRGEBOUND: interrupt ward",
        "Mirrorborn" => "MIRRORBORN: makes copies",
        _ => trait.ToUpperInvariant()
    };
    private void Clear()
    {
        foreach (var visual in _hazards.Values) RemoveVisual(visual.Root); _hazards.Clear();
        foreach (var marker in _mechanisms.Values) RemoveVisual(marker); _mechanisms.Clear();
    }
    private void RemoveVisual(Node node) { RemoveChild(node); node.QueueFree(); }
    private static Vector3 Point(CorePosition value) => new(value.X * .001f, 0, value.Z * .001f);
    private static string Family(DamageFamily family) => family switch
    { DamageFamily.PhysicalSlash => "Slash", DamageFamily.PhysicalCrush => "Crush", DamageFamily.PhysicalPierce => "Pierce", _ => family.ToString() };
    private static Label3D Label(string text, Vector3 position, Color color) => new()
    { Text = text, Position = position, FontSize = 40, PixelSize = .0105f, OutlineSize = 8, Modulate = color, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
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
