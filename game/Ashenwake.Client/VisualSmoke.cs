using System.Text.Json;
using Ashenwake.Core.Content;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

/// <summary>Isolated rendering diagnostics for cosmetic rigs; never creates a gameplay session or writes user saves.</summary>
public partial class VisualSmoke : Node3D
{
    private sealed record Exhibit(string Name, string Id, string Role = "", string Discipline = "", bool Npc = false);
    private readonly Dictionary<string, bool> _checks = [];
    private readonly Dictionary<string, object> _models = [];
    private readonly List<string> _captures = [];
    private string _output = "";
    private bool _writeReport;
    private Camera3D _camera = null!;
    private Node3D _lineup = null!;
    private Control _captions = null!;
    private Label _title = null!, _subtitle = null!;

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--visual-smoke") || _output.Length == 0)
                throw new InvalidDataException("Visual smoke requires --visual-smoke --output=<fresh-artifact-directory>.");
            if (Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Visual smoke requires a fresh artifact directory.");
            Directory.CreateDirectory(_output);
            _writeReport = true;
            Engine.MaxFps = 60;
            CheckCatalogs();
            CheckAnimationAndIsolation();
            BuildGallery();
            await Show("THE UNBOUND", "Five disciplines. Five distinct silhouettes.", "character-lineup.png", 10.2f, 3.0f,
            [
                new("VANGUARD", "player.vanguard", Discipline: "Vanguard"),
                new("VEILWALKER", "player.veilwalker", Discipline: "Veilwalker"),
                new("ARCANIST", "player.arcanist", Discipline: "Arcanist"),
                new("GRAVECALLER", "player.gravecaller", Discipline: "Gravecaller"),
                new("WARDEN", "player.warden", Discipline: "Warden")
            ]);
            await Show("WHAT WAITS IN THE ASH", "Broken armor, hungry bone, living flame, and the things beyond the bell.", "monster-lineup.png", 14.8f, 3.25f,
            [
                new("ASH GHOUL", "enemy.ash_ghoul", "Melee"),
                new("FUNERAL GUARD", "enemy.funeral_guard", "Armored"),
                new("MEMORY ARCHER", "enemy.memory_archer", "Ranged"),
                new("CINDER PRIEST", "enemy.cinder_priest", "Support"),
                new("EMBERLING", "enemy.emberling", "Rusher"),
                new("BELL SAINT", "boss.bell_saint", "BellSaint"),
                new("ANTLER BEAST", "boss.antler", "Beast")
            ]);
            await Show("THE FIRST HUNT", "Enemies of the road and monastery.", "opening-monsters.png", 9.8f, 3.25f,
            [
                new("ASH GHOUL", "enemy.ash_ghoul", "Melee"),
                new("FUNERAL GUARD", "enemy.funeral_guard", "Armored"),
                new("MEMORY ARCHER", "enemy.memory_archer", "Ranged"),
                new("CINDER PRIEST", "enemy.cinder_priest", "Support")
            ]);
            await Show("THE PEOPLE OF GREYHAVEN", "Familiar faces at the edge of the breach.", "greyhaven-lineup.png", 10.2f, 3.0f,
            [
                new("MARA VEY\nAnatomy", "npc.mara", Npc: true),
                new("TORREN\nThe forge", "npc.torren", Npc: true),
                new("SISTER CAEL\nRituals", "npc.cael", Npc: true),
                new("ORIS\nCartography", "npc.oris", Npc: true),
                new("KESH\nScavenging", "npc.kesh", Npc: true)
            ]);
            Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private void CheckCatalogs()
    {
        var definitions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string catalog in new[] { "combat", "campaign-combat", "endgame-combat" })
        {
            using var document = JsonDocument.Parse(FileAccess.GetFileAsString($"res://{catalog}.json"));
            foreach (var enemy in document.RootElement.GetProperty("enemies").EnumerateArray())
                definitions[enemy.GetProperty("id").GetString()!] = enemy.GetProperty("role").GetString()!;
        }
        foreach (var definition in definitions)
        {
            var visual = CharacterVisual.Create(definition.Key, definition.Value);
            try { InspectGeometry(definition.Key, visual); }
            finally { visual.Free(); }
        }
        foreach (string discipline in new[] { "Vanguard", "Veilwalker", "Arcanist", "Gravecaller", "Warden" })
        {
            var visual = CharacterVisual.Create("player." + discipline.ToLowerInvariant(), "", discipline);
            try { InspectGeometry(discipline, visual); }
            finally { visual.Free(); }
        }
        foreach (string npc in new[] { "npc.mara", "npc.torren", "npc.cael", "npc.oris", "npc.kesh" })
        {
            var visual = CharacterVisual.CreateNpc(npc);
            try { InspectGeometry(npc, visual); }
            finally { visual.Free(); }
        }
        Check("all_catalog_enemies_and_disciplines_build", definitions.Count > 30 && _models.Count == definitions.Count + 10);
    }

    private void InspectGeometry(string id, CharacterVisual visual)
    {
        var nodes = Descendants(visual).OfType<Node3D>().ToArray();
        var meshes = nodes.OfType<MeshInstance3D>().ToArray();
        var materials = meshes.SelectMany(Materials).Select(m => m.GetInstanceId()).Distinct().ToArray();
        Check("finite_geometry_" + id, meshes.Length > 0 && meshes.All(m => m.Mesh is not null && Finite(m.Mesh.GetAabb().Position) &&
            Finite(m.Mesh.GetAabb().Size) && m.Mesh.GetAabb().Size.LengthSquared() > .000001f) &&
            nodes.All(n => Finite(n.Position) && Finite(n.Scale) && Finite(n.Rotation)) && float.IsFinite(visual.Height) && visual.Height is > .3f and < 12);
        Check("bounded_geometry_" + id, meshes.Length <= 100 && materials.Length is > 0 and <= 7);
        Check("cosmetic_nodes_only_" + id, !Descendants(visual).Any(n => n is CollisionObject3D or CollisionShape3D or NavigationRegion3D));
        _models[id] = new { meshes = meshes.Length, materials = materials.Length, height = visual.Height };
    }

    private void CheckAnimationAndIsolation()
    {
        var first = CharacterVisual.Create("player.vanguard", "", "Vanguard");
        var second = CharacterVisual.Create("player.vanguard", "", "Vanguard");
        try
        {
            var firstMeshes = Descendants(first).OfType<MeshInstance3D>().ToArray();
            var secondMeshes = Descendants(second).OfType<MeshInstance3D>().ToArray();
            Check("matching_actors_share_mesh_resources", firstMeshes.Select(m => m.Mesh.GetInstanceId()).SequenceEqual(secondMeshes.Select(m => m.Mesh.GetInstanceId())));
            Check("matching_actors_share_base_materials", firstMeshes.SelectMany(Materials).Select(m => m.GetInstanceId()).Intersect(secondMeshes.SelectMany(Materials).Select(m => m.GetInstanceId())).Distinct().Count() >= 3);
            first.Position = new(2, 0, -3);
            Vector3 origin = first.Position;
            var before = Pose(first);
            for (int i = 0; i < 12; i++) first.Animate(1.0 / 60, Vector3.Forward);
            Check("walking_animates_joints_without_root_motion", first.Position == origin && ChangedNodes(before, Pose(first)) >= 3);
            var walking = Pose(first);
            for (int i = 0; i < 12; i++) first.Animate(1.0 / 60, Vector3.Zero, windup: true);
            Check("windup_changes_the_visible_pose", first.Position == origin && ChangedNodes(walking, Pose(first)) >= 3);
            var paused = Pose(first);
            for (int i = 0; i < 12; i++) first.Animate(.1, Vector3.Right, paused: true, facing: Vector3.Left);
            Check("paused_pose_is_stable", !Changed(paused, Pose(first)) && first.Position == origin);
            var originalColors = Colors(second);
            var firstColors = Colors(first);
            Color accent = new("ff4f92");
            first.SetAccent(accent);
            Check("accent_changes_visible_material", first.AccentColor == accent && !firstColors.SequenceEqual(Colors(first)));
            Check("accent_is_isolated_to_one_instance", second.AccentColor != accent && originalColors.SequenceEqual(Colors(second)));
        }
        finally { first.Free(); second.Free(); }
    }

    private void BuildGallery()
    {
        GetWindow().Size = new(1440, 900);
        GetWindow().ContentScaleSize = new(1440, 900);
        var lighting = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new("111a25"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new("a8bfd0"),
                AmbientLightEnergy = .4f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        GraphicsProfile.TrackEnvironment(lighting);
        AddChild(lighting);
        var key = new DirectionalLight3D { RotationDegrees = new(-48, -35, 0), LightColor = new("ffe2b1"), LightEnergy = 1.1f, ShadowEnabled = true };
        AddChild(key);
        AddChild(new DirectionalLight3D { RotationDegrees = new(-20, 145, 0), LightColor = new("80bfc9"), LightEnergy = .3f });
        GraphicsProfile.Apply(GetViewport(), lighting.Environment, key, "High", false);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new(200, 200) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new("070e15"), Roughness = .95f },
            Position = new(0, -.06f, 0)
        });
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Position = new(-2, 5.8f, -17), Current = true };
        AddChild(_camera); _camera.LookAt(new(0, 1.25f, 0));
        _lineup = new Node3D(); AddChild(_lineup);
        var canvas = new CanvasLayer(); AddChild(canvas);
        var overlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); canvas.AddChild(overlay);
        _title = Text(overlay, "", new(74, 60), new(1292, 56), 34, new("e7ebed"));
        _subtitle = Text(overlay, "", new(76, 116), new(1288, 40), 19, new("a6becb"));
        Text(overlay, "ASHENWAKE  /  CHARACTER ART PASS", new(76, 831), new(1250, 28), 14, new("7e9caa"));
        _captions = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; overlay.AddChild(_captions);
    }

    private async Task Show(string title, string subtitle, string filename, float size, float spacing, Exhibit[] exhibits)
    {
        foreach (Node child in _lineup.GetChildren()) child.Free();
        foreach (Node child in _captions.GetChildren()) child.Free();
        _title.Text = title; _subtitle.Text = subtitle; _camera.Size = size;
        var models = new List<(Exhibit Exhibit, CharacterVisual Visual)>();
        for (int i = 0; i < exhibits.Length; i++)
        {
            var exhibit = exhibits[i];
            var visual = exhibit.Npc ? CharacterVisual.CreateNpc(exhibit.Id) : CharacterVisual.Create(exhibit.Id, exhibit.Role, exhibit.Discipline);
            visual.Position = new((i - (exhibits.Length - 1) * .5f) * spacing, 0, 0);
            _lineup.AddChild(visual);
            for (int frame = 0; frame < 45; frame++) visual.Animate(1.0 / 60, Vector3.Zero, facing: new Vector3(.22f, 0, -1));
            _lineup.AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = .95f, BottomRadius = .98f, Height = .06f, RadialSegments = 48 },
                Position = visual.Position + new Vector3(0, -.035f, 0),
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new("243846"), Roughness = .9f }
            });
            models.Add((exhibit, visual));
        }
        await Settle();
        foreach (var (exhibit, visual) in models)
        {
            var point = _camera.UnprojectPosition(visual.Position);
            var label = Text(_captions, exhibit.Name, new(point.X - 100, point.Y + 26), new(200, 56), 16, new("cfdee2"));
            label.HorizontalAlignment = HorizontalAlignment.Center;
        }
        await Settle();
        if (!OS.GetCmdlineUserArgs().Contains("--capture-visuals") || DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.GetHeight() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok);
        _captures.Add(filename);
    }

    private static Label Text(Node parent, string text, Vector2 position, Vector2 size, int fontSize, Color color)
    {
        var label = new Label { Text = text, Position = position, Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize); label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label); return label;
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static IEnumerable<Node> Descendants(Node node) => node.GetChildren().SelectMany(child => new[] { child }.Concat(Descendants(child)));
    private static Transform3D[] Pose(CharacterVisual visual) => Descendants(visual).OfType<Node3D>().Select(n => n.Transform).ToArray();
    private static bool Changed(Transform3D[] first, Transform3D[] second) => first.Length != second.Length || first.Where((t, i) => !t.IsEqualApprox(second[i])).Any();
    private static int ChangedNodes(Transform3D[] first, Transform3D[] second) => first.Zip(second, (a, b) => !a.IsEqualApprox(b)).Count(changed => changed);
    private static IEnumerable<Material> Materials(MeshInstance3D mesh) => Enumerable.Range(0, mesh.Mesh.GetSurfaceCount()).Select(mesh.GetActiveMaterial).Where(m => m is not null);
    private static Color[] Colors(CharacterVisual visual) => Descendants(visual).OfType<MeshInstance3D>().SelectMany(Materials).OfType<StandardMaterial3D>().SelectMany(m => new[] { m.AlbedoColor, m.Emission }).ToArray();
    private async Task Settle() { for (int i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Visual check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "VisualClientSmokePassed" : "VisualClientSmokeFailed",
            passed,
            checks = _checks,
            models = _models,
            captures = _captures,
            cache = new { meshTemplates = CharacterVisual.CachedResourceCounts.Models, baseMaterials = CharacterVisual.CachedResourceCounts.Materials },
            error,
            scope = "Instantiates every catalog enemy, all player disciplines, and five NPCs; checks finite geometry, bounded mesh/material counts, cosmetic-only nodes, animation, pause, and independent accent materials. Captures use the same rigs as gameplay; visual readability requires inspection."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "visual-smoke.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
