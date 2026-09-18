using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private sealed class ActorPresentation
    {
        public required Node3D Root { get; init; }
        public required MeshInstance3D Body { get; init; }
        public required MeshInstance3D Tell { get; init; }
        public required Label3D Label { get; init; }
        public Vector3 Previous { get; set; }
        public Vector3 Current { get; set; }
    }
    private readonly Dictionary<int, ActorPresentation> _actors = [];
    private readonly Dictionary<string, MeshInstance3D> _effects = [];
    private readonly HashSet<string> _visibleEffects = [];
    private readonly List<MeshInstance3D> _occluders = [];
    private readonly Dictionary<string, AudioStreamWav> _tones = [];
    private readonly List<AudioStreamPlayer> _voices = [];
    private readonly HashSet<int> _floatingActors = [];
    private readonly HashSet<Node3D> _transientNodes = [];
    private readonly Color _ember = new("ffb56b"), _mint = new("79e0cb");
    private Camera3D _camera = null!;
    private Vector3 _cameraHome;
    private MeshInstance3D _targetMarker = null!;
    private MeshInstance3D _manifestationMarker = null!;
    private IReadOnlyList<string> _manifestations = [];
    private double _shake;
    private bool _reduceEffects, _reduceShake;
    private int _voiceIndex;

    private void BuildArena(int halfWidth, int halfDepth)
    {
        var environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("111722"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("96b2c0"),
                AmbientLightEnergy = .7f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        AddChild(environment);
        AddChild(new DirectionalLight3D
        {
            RotationDegrees = new(-65, -30, 0),
            LightColor = new Color("ffe0b0"),
            LightEnergy = 1.5f,
            ShadowEnabled = true
        });
        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = Math.Max(halfWidth, halfDepth) * .0023f + 10,
            Position = new(14, 20, 16),
            Current = true
        };
        AddChild(_camera); _camera.LookAt(Vector3.Zero); _cameraHome = _camera.Position;
        float width = halfWidth * .002f, depth = halfDepth * .002f;
        Box(new(width, .3f, depth), new(0, -.2f, 0), new("14202d"));
        for (int x = -halfWidth / 1000; x <= halfWidth / 1000; x += 2)
            Box(new(.024f, .012f, depth), new(x, -.042f, 0), new("43515c"));
        for (int z = -halfDepth / 1000; z <= halfDepth / 1000; z += 2)
            Box(new(width, .012f, .024f), new(0, -.042f, z), new("43515c"));
        Box(new(width, .36f, .2f), new(0, .03f, -depth / 2), new("69858c"));
        Box(new(.2f, .36f, depth), new(-width / 2, .03f, 0), new("69858c"));
        Box(new(width, .1f, .16f), new(0, .01f, depth / 2), new("78816f"));
        Box(new(.16f, .1f, depth), new(width / 2, .01f, 0), new("78816f"));
        _targetMarker = Disc(.7f, new Color(1, .85f, .4f, .22f));
        _targetMarker.Visible = false;
        _manifestationMarker = Disc(.85f, new Color(.9f, .6f, .25f, .4f)); _manifestationMarker.Visible = false;
        BuildAudio();
    }
    public void SetManifestationPresentation(IReadOnlyList<string> ids) => _manifestations = ids;

    private void AddObstacle(int minX, int minZ, int maxX, int maxZ)
    {
        var mesh = Box(new((maxX - minX) * .001f, 1.5f, (maxZ - minZ) * .001f),
            new((minX + maxX) * .0005f, .75f, (minZ + maxZ) * .0005f), new("677578"));
        _occluders.Add(mesh);
        Box(new((maxX - minX) * .001f + .12f, .13f, (maxZ - minZ) * .001f + .12f),
            mesh.Position + Vector3.Up * .76f, new("a89b7c"));
    }

    private void SynchronizeActor(int id, string name, string role, int x, int z, int health, int maxHealth,
        string status, bool telegraph, bool allied, int radius = 350)
    {
        Vector3 target = PositionOf(x, z);
        if (!_actors.TryGetValue(id, out var actor))
        {
            Color color = id == 1 ? _mint : allied ? new("af9cff") : role switch
            {
                "ranged" or "support" => new("dca3e6"),
                "armored" => new("dbbd83"),
                "anchor" => new("dba2f1"),
                "bellsaint" or "bell" => new("e4bd78"),
                "beast" => new("c96879"),
                _ => new("ed8870")
            };
            var root = new Node3D { Position = target }; AddChild(root);
            Mesh bodyMesh = role == "armored" ? new BoxMesh { Size = new(.85f, 1.5f, .85f) }
                : role == "bellsaint" ? new CylinderMesh { TopRadius = .35f, BottomRadius = .8f, Height = 2.8f }
                : role == "anchor" ? new CylinderMesh { TopRadius = .25f, BottomRadius = .5f, Height = 1.1f }
                : role == "bell" ? new SphereMesh { Radius = .45f, Height = .9f }
                : role == "beast" ? new BoxMesh { Size = new(1.2f, 1.8f, 1f) }
                : new CapsuleMesh { Radius = Math.Max(.22f, radius * .001f), Height = allied && id != 1 ? 1f : 1.4f };
            var body = new MeshInstance3D { Mesh = bodyMesh, Position = Vector3.Up * (role == "bellsaint" ? 1.4f : .75f), MaterialOverride = Material(color) };
            root.AddChild(body);
            var label = new Label3D
            {
                Position = Vector3.Up * (role == "bellsaint" ? 3.3f : 2.05f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 64,
                PixelSize = .012f,
                Modulate = color,
                OutlineSize = 6,
                NoDepthTest = true,
                Shaded = false
            };
            root.AddChild(label);
            var tell = Disc(1.15f, new Color(1, .22f, .08f, .38f), root);
            actor = new() { Root = root, Body = body, Tell = tell, Label = label, Previous = target, Current = target };
            _actors[id] = actor;
        }
        actor.Previous = actor.Current; actor.Current = target;
        actor.Root.Visible = health > 0;
        actor.Label.Text = id == 1 ? $"UNBOUND  {health}/{maxHealth}" : $"{name.ToUpperInvariant()}\n{health}/{maxHealth}{(status.Length > 0 ? "\n" + status : "")}";
        actor.Tell.Visible = telegraph && health > 0;
        if (telegraph) actor.Label.Text += "\n⚠ ATTACK INCOMING";
        actor.Body.Rotation = telegraph ? new(.12f, .2f, 0) : Vector3.Zero;
    }

    private void AnimatePresentation(double delta, double alpha, int selected)
    {
        foreach (var pair in _actors)
        {
            pair.Value.Root.Position = pair.Value.Previous.Lerp(pair.Value.Current, (float)alpha);
            // Keep the selected role/status readable even when several melee actors overlap.
            pair.Value.Label.Visible = pair.Key == selected || _mechanicLabels.Contains(pair.Key);
        }
        _targetMarker.Visible = selected > 0 && _actors.TryGetValue(selected, out var target) && target.Root.Visible;
        if (_targetMarker.Visible) _targetMarker.Position = _actors[selected].Root.Position + Vector3.Up * .04f;
        _shake = Math.Max(0, _shake - delta * 4);
        _camera.Position = _cameraHome + (_reduceShake ? Vector3.Zero : new Vector3(
            (float)(Math.Sin(Time.GetTicksMsec() * .081) * _shake * .12),
            (float)(Math.Cos(Time.GetTicksMsec() * .103) * _shake * .08), 0));
        if (_actors.TryGetValue(1, out var player))
        {
            bool burning = _manifestations.Contains("manifestation.burning_blood"), stone = _manifestations.Contains("manifestation.stone_memory");
            bool shadow = _manifestations.Contains("manifestation.whispering_shadow"), renewal = _manifestations.Contains("manifestation.voracious_renewal");
            var playerMaterial = (StandardMaterial3D)player.Body.MaterialOverride;
            playerMaterial.AlbedoColor = renewal ? new Color("b1db83") : shadow ? new Color("b6a1ed") : burning ? new Color("ffc072") : stone ? new Color("c6c6bc") : _mint;
            _manifestationMarker.Visible = (burning || stone || shadow || renewal) && player.Root.Visible;
            _manifestationMarker.Position = player.Root.Position + Vector3.Up * .06f;
            _manifestationMarker.Rotation = new(0, (float)(Time.GetTicksMsec() * .001), burning ? .12f : 0);
            ((StandardMaterial3D)_manifestationMarker.MaterialOverride).AlbedoColor = new Color(playerMaterial.AlbedoColor, .3f);
            Vector2 screen = _camera.UnprojectPosition(player.Root.Position + Vector3.Up);
            foreach (var occluder in _occluders)
            {
                bool overlaps = _camera.UnprojectPosition(occluder.Position).DistanceTo(screen) < 80;
                var mat = (StandardMaterial3D)occluder.MaterialOverride;
                mat.Transparency = overlaps ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
                mat.AlbedoColor = new Color(mat.AlbedoColor, overlaps ? .25f : 1);
            }
        }
    }

    private void BeginEffects() => _visibleEffects.Clear();
    private void PresentEffect(string id, int x, int z, float radius, Color color, bool projectile = false, bool silhouette = false)
    {
        _visibleEffects.Add(id);
        if (!_effects.TryGetValue(id, out var mesh))
        {
            mesh = silhouette ? new MeshInstance3D { Mesh = new CapsuleMesh { Radius = .3f, Height = 1.3f }, MaterialOverride = Material(color, true) }
                : projectile ? new MeshInstance3D { Mesh = new SphereMesh { Radius = .15f, Height = .3f }, MaterialOverride = Material(color) }
                : new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .025f }, MaterialOverride = Material(color, true) };
            AddChild(mesh); _effects[id] = mesh;
        }
        mesh.Position = PositionOf(x, z) + Vector3.Up * (projectile || silhouette ? .65f : .04f);
        ((StandardMaterial3D)mesh.MaterialOverride).AlbedoColor = color;
    }
    private void EndEffects()
    {
        foreach (var id in _effects.Keys.Where(id => !_visibleEffects.Contains(id)).ToArray())
        { _effects[id].QueueFree(); _effects.Remove(id); }
    }

    private void PresentCampaignWarning(long id, string kind, int x, int z, int endX, int endZ, int radius, long ticks)
    {
        Color color = ticks <= 12 ? new Color(1, .2f, .12f, .5f) : new Color(1, .58f, .12f, .35f);
        float width = radius * .001f;
        PresentEffect($"warning{id}-start", x, z, width, color);
        if (kind == "Circle")
        {
            string rimId = $"warning{id}-rim"; _visibleEffects.Add(rimId);
            if (!_effects.TryGetValue(rimId, out var rim))
            {
                rim = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = Math.Max(.02f, width - .06f), OuterRadius = width }, MaterialOverride = Material(new Color(1, .73f, .34f)) };
                _effects[rimId] = rim; AddChild(rim);
            }
            rim.Position = PositionOf(x, z) + Vector3.Up * .075f;
            return;
        }
        PresentEffect($"warning{id}-end", endX, endZ, width, color);
        string lineId = $"warning{id}-line"; _visibleEffects.Add(lineId);
        Vector3 start = PositionOf(x, z), end = PositionOf(endX, endZ), delta = end - start;
        if (!_effects.TryGetValue(lineId, out var line))
        {
            line = new MeshInstance3D { Mesh = new BoxMesh { Size = new(width * 2, .035f, delta.Length()) }, MaterialOverride = Material(color, true) };
            _effects[lineId] = line; AddChild(line);
        }
        line.Position = (start + end) / 2 + Vector3.Up * .08f;
        line.Rotation = new(0, Mathf.Atan2(delta.X, delta.Z), 0);
        ((StandardMaterial3D)line.MaterialOverride).AlbedoColor = color;
    }

    private void Feedback(int id, string text, string kind)
    {
        if (!_actors.TryGetValue(id, out var actor) || DisplayServer.GetName() == "headless") return;
        if (!_floatingActors.Add(id)) return;
        Color color = kind == "heal" ? _mint : kind == "death" ? new Color("f4d99f") : _ember;
        var label = new Label3D
        {
            Text = text,
            Position = actor.Root.Position + Vector3.Up * 1.4f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64,
            PixelSize = .014f,
            Modulate = color,
            OutlineSize = 6,
            NoDepthTest = true
        };
        AddChild(label); _transientNodes.Add(label);
        var tween = label.CreateTween();
        tween.TweenProperty(label, "position", label.Position + Vector3.Up * (_reduceEffects ? .5f : 1.4f), .65);
        tween.Parallel().TweenProperty(label, "modulate:a", 0f, .65);
        tween.TweenCallback(Callable.From(() => { _floatingActors.Remove(id); _transientNodes.Remove(label); label.QueueFree(); }));
        if (!_reduceEffects)
        {
            var pulse = Disc(.55f, new Color(color, .7f)); pulse.Position = actor.Root.Position + Vector3.Up * .4f;
            _transientNodes.Add(pulse);
            var burst = pulse.CreateTween(); burst.TweenProperty(pulse, "scale", new Vector3(2.4f, .1f, 2.4f), .2);
            burst.TweenCallback(Callable.From(() => { _transientNodes.Remove(pulse); pulse.QueueFree(); }));
        }
        if (id == 1 || kind == "death") _shake = kind == "death" ? .6 : .25;
        PlayTone(kind);
    }

    private void BuildAudio()
    {
        foreach (var pair in new[] { ("hit", 240d), ("death", 120d), ("heal", 660d), ("loot", 880d), ("tell", 440d), ("cast", 330d) })
        {
            const int rate = 22050, samples = 3307;
            var bytes = new byte[samples * 2];
            for (int i = 0; i < samples; i++)
            {
                double envelope = Math.Min(1, i / 110d) * (1 - i / (double)samples);
                short sample = (short)(Math.Sin(i * pair.Item2 * 2 * Math.PI / rate) * envelope * 7000);
                bytes[i * 2] = (byte)(sample & 255); bytes[i * 2 + 1] = (byte)((sample >> 8) & 255);
            }
            _tones[pair.Item1] = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = bytes };
        }
        for (int i = 0; i < 6; i++)
        { var voice = new AudioStreamPlayer { VolumeDb = -14 }; AddChild(voice); _voices.Add(voice); }
    }
    private void PlayTone(string kind)
    {
        if (DisplayServer.GetName() == "headless" || !_tones.TryGetValue(kind, out var stream)) return;
        var voice = _voices[_voiceIndex++ % _voices.Count]; voice.Stream = stream; voice.Play();
    }
    private void ClearPresentation()
    {
        foreach (var actor in _actors.Values) actor.Root.QueueFree(); _actors.Clear();
        foreach (var mesh in _effects.Values) mesh.QueueFree(); _effects.Clear();
        foreach (var node in _transientNodes) node.QueueFree(); _transientNodes.Clear();
        _floatingActors.Clear();
    }
    private MeshInstance3D Box(Vector3 size, Vector3 position, Color color)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = position, MaterialOverride = Material(color) };
        AddChild(mesh); return mesh;
    }
    private MeshInstance3D Disc(float radius, Color color, Node? parent = null)
    {
        var mesh = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = .018f },
            Position = Vector3.Up * .03f,
            MaterialOverride = Material(color, true),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        (parent ?? this).AddChild(mesh); return mesh;
    }
    private static StandardMaterial3D Material(Color color, bool translucent = false) => new()
    {
        AlbedoColor = color,
        Roughness = .9f,
        Transparency = translucent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled
    };
    private static Vector3 PositionOf(int x, int z) => new(x * .001f, 0, z * .001f);
}
