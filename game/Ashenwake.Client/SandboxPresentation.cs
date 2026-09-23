using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    private sealed class ActorPresentation
    {
        public required Node3D Root { get; init; }
        public required CharacterVisual Body { get; init; }
        public required string VisualKey { get; init; }
        public required MeshInstance3D Tell { get; init; }
        public required Label3D Label { get; init; }
        public required Node3D HealthBar { get; init; }
        public required Sprite3D HealthFill { get; init; }
        public required Sprite3D HealthTrack { get; init; }
        public required Sprite3D BarrierStrip { get; init; }
        public bool Enemy { get; set; }
        public string Name { get; set; } = "";
        public string HealthDetail { get; set; } = "";
        public string ConditionDetail { get; set; } = "";
        public Vector3 Previous { get; set; }
        public Vector3 Current { get; set; }
        public int Health { get; set; }
        public bool AuthoredVisible { get; set; } = true;
        public Vector3? Facing { get; set; }
        public long LastAttackTick { get; set; } = -1;
        public bool Windup { get; set; }
        public string State { get; set; } = "";
    }
    private readonly Dictionary<int, ActorPresentation> _actors = [];
    private readonly Dictionary<string, MeshInstance3D> _effects = [];
    private readonly HashSet<string> _visibleEffects = [];
    private readonly List<MeshInstance3D> _occluders = [];
    private readonly Dictionary<MeshInstance3D, Vector3> _occluderCenters = [];
    private readonly Dictionary<string, AudioStreamWav> _tones = [];
    private readonly List<AudioStreamPlayer> _voices = [];
    private readonly HashSet<int> _floatingActors = [];
    private readonly HashSet<Node3D> _transientNodes = [];
    private readonly Color _ember = new("ffb56b"), _mint = new("79e0cb");
    private Camera3D _camera = null!;
    private Vector3 _cameraHome;
    private Vector3 _cameraFollow;
    private MeshInstance3D _targetMarker = null!;
    private MeshInstance3D _manifestationMarker = null!;
    private IReadOnlyList<string> _manifestations = [];
    private double _shake;
    private bool _reduceEffects, _reduceShake;
    private int _voiceIndex;

    private void BuildArena(int halfWidth, int halfDepth)
    {
        VerdantAmbience.Prewarm();
        CinderAmbience.Prewarm();
        SpineAmbience.Prewarm();
        HollowAmbience.Prewarm();
        _worldEnvironment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("111722"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("96b2c0"),
                AmbientLightEnergy = .4f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        GraphicsProfile.TrackEnvironment(_worldEnvironment);
        AddChild(_worldEnvironment);
        _sun = new DirectionalLight3D
        {
            RotationDegrees = new(-65, -30, 0),
            LightColor = new Color("ffe0b0"),
            LightEnergy = 1.05f,
            ShadowEnabled = true
        };
        AddChild(_sun);
        AddChild(new DirectionalLight3D
        {
            Name = "CoolRimLight",
            RotationDegrees = new(-24, 145, 0),
            LightColor = new("8faeca"),
            LightEnergy = .16f,
            LightSpecular = .4f
        });
        ApplyGraphicsQuality();
        _camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = DefaultCameraSize(halfWidth, halfDepth),
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

    private void AddObstacle(int minX, int minZ, int maxX, int maxZ)
    {
        var mesh = Box(new((maxX - minX) * .001f, 1.5f, (maxZ - minZ) * .001f),
            new((minX + maxX) * .0005f, .75f, (minZ + maxZ) * .0005f), new("677578"));
        _occluders.Add(mesh);
        Box(new((maxX - minX) * .001f + .12f, .13f, (maxZ - minZ) * .001f + .12f),
            mesh.Position + Vector3.Up * .76f, new("a89b7c"));
    }

    private void SynchronizeActor(int id, string name, string role, int x, int z, int health, int maxHealth, int barrier,
        string status, bool telegraph, bool allied, string definitionId, string state, bool windingUp, string? mechanic = null)
    {
        Vector3 target = PositionOf(x, z);
        if (definitionId is "boss.bell_saint" && _view.BossPhase >= 3) definitionId = "enemy.bell_beast";
        if (id != 1 && !allied)
        {
            (definitionId, name) = (_session.EncounterId, definitionId) switch
            {
                ("championarena.pilgrim", "enemy.funeral_guard") => ("champion.pilgrim", "The Bell-Torn Pilgrim"),
                ("championarena.rootwidow", "enemy.bloom_carrier") => ("champion.rootwidow", "Widow of the Root"),
                ("championarena.rootwidow", "enemy.feeding_root") => ("champion.nest", "Poisonous Root Nest"),
                ("championarena.tithekeeper", "enemy.forge_sentinel") => ("champion.tithekeeper", "The Cinder Tithekeeper"),
                _ => (definitionId, name)
            };
        }
        string discipline = id == 1 ? _view.Discipline : "";
        CharacterAppearance? appearance = id == 1 ? _playerAppearance : null;
        string visualKey = $"{definitionId}/{role}/{discipline}/{allied}/{appearance?.Key}";
        if (_actors.TryGetValue(id, out var previous) && (previous.VisualKey != visualKey || previous.Body.IsDying && health > 0))
        { RemoveChild(previous.Root); previous.Root.QueueFree(); _actors.Remove(id); }
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
            var root = new Node3D { Name = $"Actor{id}", Position = target }; AddChild(root);
            if (id == 1) _cameraFollow = target;
            var body = CharacterVisual.Create(definitionId, role, discipline, allied, appearance);
            root.AddChild(body);
            var label = new Label3D
            {
                Position = Vector3.Up * (body.Height + .4f),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 40,
                PixelSize = .011f,
                VerticalAlignment = VerticalAlignment.Bottom,
                Modulate = color,
                OutlineSize = 8,
                NoDepthTest = true,
                Shaded = false
            };
            root.AddChild(label);
            var tell = Disc(1.15f, new Color(1, .22f, .08f, .38f), root);
            var bar = CreateCombatHealthBar(root, color);
            actor = new()
            {
                Root = root,
                Body = body,
                VisualKey = visualKey,
                Tell = tell,
                Label = label,
                HealthBar = bar.Root,
                HealthTrack = bar.Track,
                HealthFill = bar.Fill,
                BarrierStrip = bar.Barrier,
                Previous = target,
                Current = target,
                Health = health
            };
            _actors[id] = actor;
        }
        actor.Previous = actor.Current; actor.Current = target;
        actor.Windup = windingUp; actor.State = state;
        if (actor.Health > 0 && health <= 0) actor.Body.React("death");
        actor.Health = health;
        actor.Root.Visible = health > 0 || actor.Body.IsDying && !actor.Body.DeathFinished;
        actor.Label.Position = Vector3.Up * (actor.Body.Height + (role == "bellsaint" && _view.BossPhase == 2 ? 1.2f : .52f));
        SynchronizeCombatReadability(actor, id, name, health, maxHealth, barrier, status, telegraph, allied, mechanic);
        actor.Tell.Visible = telegraph && health > 0;
    }

    private void AnimatePresentation(double delta, double alpha, int selected)
    {
        foreach (var pair in _actors)
        {
            pair.Value.Root.Position = pair.Value.Previous.Lerp(pair.Value.Current, (float)alpha);
            Vector3? facing = pair.Value.Facing;
            if (facing is null && pair.Value.Windup && _actors.TryGetValue(pair.Key == 1 ? selected : 1, out var opponent))
                facing = opponent.Current - pair.Value.Current;
            pair.Value.Body.SetReducedEffects(_reduceEffects);
            pair.Value.Body.Animate(delta, pair.Value.Current - pair.Value.Previous, pair.Value.Windup, pair.Value.State, IsPaused, facing);
            pair.Value.Root.Visible = pair.Value.AuthoredVisible && (pair.Value.Health > 0 || pair.Value.Body.IsDying && !pair.Value.Body.DeathFinished);
        }
        UpdateCombatReadability(selected);
        foreach (var loot in _lootVisuals.Values) loot.Animate(delta, IsPaused, _reduceEffects);
        _targetMarker.Visible = selected > 0 && _actors.TryGetValue(selected, out var target) && target.Root.Visible && target.Health > 0;
        if (_targetMarker.Visible) _targetMarker.Position = _actors[selected].Root.Position + Vector3.Up * .04f;
        if (_actors.TryGetValue(1, out var focus))
            _cameraFollow = _cameraFollow.Lerp(focus.Root.Position, 1 - MathF.Exp(-(float)delta * 8));
        AdvanceCombatFeedback(delta);
        if (!IsPaused) _shake = Math.Max(0, _shake - delta * 4);
        _camera.Position = _cameraHome + _cameraFollow + (_reduceShake ? Vector3.Zero : new Vector3(
            (float)(Math.Sin(_cosmeticTime * 81) * _shake * .12),
            (float)(Math.Cos(_cosmeticTime * 103) * _shake * .08), 0));
        if (_actors.TryGetValue(1, out var player))
        {
            bool burning = _manifestations.Contains("manifestation.burning_blood"), stone = _manifestations.Contains("manifestation.stone_memory");
            bool shadow = _manifestations.Contains("manifestation.whispering_shadow"), renewal = _manifestations.Contains("manifestation.voracious_renewal");
            Color accent = renewal ? new Color("b1db83") : shadow ? new Color("b6a1ed") : burning ? new Color("ffc072") : stone ? new Color("c6c6bc") : player.Body.BaseAccentColor;
            player.Body.SetAccent(accent);
            _manifestationMarker.Visible = (burning || stone || shadow || renewal) && player.Root.Visible;
            _manifestationMarker.Position = player.Root.Position + Vector3.Up * .06f;
            _manifestationMarker.Rotation = new(0, (float)(_cosmeticTime), burning ? .12f : 0);
            ((StandardMaterial3D)_manifestationMarker.MaterialOverride).AlbedoColor = new Color(accent, .3f);
            Vector2 screen = _camera.UnprojectPosition(player.Root.Position + Vector3.Up);
            float fadeRadius = GetViewport().GetVisibleRect().Size.Y * .1f;
            foreach (var occluder in _occluders)
            {
                Vector3 center = _occluderCenters.GetValueOrDefault(occluder, occluder.GetAabb().GetCenter());
                bool overlaps = _camera.UnprojectPosition(occluder.GlobalTransform * center).DistanceTo(screen) < fadeRadius;
                var mat = (StandardMaterial3D)occluder.MaterialOverride;
                mat.Transparency = overlaps ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
                mat.AlbedoColor = new Color(mat.AlbedoColor, overlaps ? .25f : 1);
            }
        }
        LayoutCampaignWarningLabels();
        LayoutEndgameCombatLabels();
    }

    private static float DefaultCameraSize(int halfWidth, int halfDepth)
        => Math.Clamp(Math.Max(halfWidth, halfDepth) * .0012f + 10, 20, 30);

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

    private void PresentCampaignWarning(CombatHazardView hazard)
    {
        long id = hazard.Id, ticks = hazard.RemainingTicks;
        string kind = hazard.Kind, contentId = hazard.ContentId;
        int x = hazard.Position.X, z = hazard.Position.Z, endX = hazard.End.X, endZ = hazard.End.Z, radius = hazard.Radius;
        if (contentId == "elite.dirgebound") { PresentDirgeWarning(id, x, z, radius, ticks); return; }
        if (IsMidgameSupport(contentId)) { PresentMidgameSupport(id, x, z, radius, ticks, contentId); return; }
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
            PresentMidgameHazardLabel(rim, hazard, CampaignCircleLabelPosition(hazard));
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
        PresentMidgameHazardLabel(line, hazard, (start + end) / 2 + Vector3.Up * .4f);
        string order = contentId switch { "rule.fault.1" => "1", "rule.fault.2" => "2", "rule.fault.3" => "3", _ => "" };
        if (order.Length == 0) return;
        // These numbers belong to the authoritative warning, including its reversed memory
        // ordering. A child label shares the warning's lifetime and survives reduced effects.
        string labelName = "FaultSequence_" + id;
        var sequence = line.GetNodeOrNull<Label3D>(labelName);
        if (sequence is null)
        {
            sequence = new Label3D
            {
                Name = labelName,
                Text = order,
                FontSize = 64,
                PixelSize = .013f,
                OutlineSize = 12,
                Modulate = new("fff3d4"),
                OutlineModulate = new("201c26"),
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true
            };
            line.AddChild(sequence);
        }
        Vector3 inset = delta.LengthSquared() > .0001f ? delta.Normalized() * Math.Min(1f, delta.Length() * .25f) : Vector3.Zero;
        sequence.GlobalPosition = start + inset + Vector3.Up * .3f;
    }

    private void Feedback(int id, string text, string kind)
    {
        if (!_actors.TryGetValue(id, out var actor) || DisplayServer.GetName() == "headless") return;
        Color color = kind == "heal" ? _mint : kind == "death" ? new Color("f4d99f") : _ember;
        if (kind == "death" && _floatingLabels.FirstOrDefault(label => label.ActorId == id) is { } existing)
        {
            existing.Node.Text = text; existing.Color = color; existing.Age = 0;
            existing.Node.Modulate = color; existing.Node.Position = existing.Origin; return;
        }
        if (_floatingActors.Count >= 32 || !_floatingActors.Add(id)) return;
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
        _floatingLabels.Add(new FloatingLabel(id, label, label.Position, color));
    }

    private void BuildAudio()
    {
        ClientAudio.EnsureBuses();
        _openingAudio = new OpeningAudio { Name = "OpeningAudio" }; AddChild(_openingAudio);
        _combatEffects = new CombatEffects { Name = "CombatEffects" }; AddChild(_combatEffects);
        foreach (string cue in CombatAudio.CueNames)
            _tones[cue] = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = 22050, Data = CombatAudio.CreateSamples(cue) };
        for (int i = 0; i < 8; i++)
        {
            var voice = new AudioStreamPlayer { Name = "CombatVoice" + i, Bus = ClientAudio.EffectsBus, VolumeDb = -12 };
            AddChild(voice); _voices.Add(voice);
        }
    }
    private void PlayTone(string kind)
    {
        if (DisplayServer.GetName() == "headless" || !_tones.TryGetValue(kind, out var stream)) return;
        if (_lastSounds.TryGetValue(kind, out double last) && _cosmeticTime - last < .07) return;
        _lastSounds[kind] = _cosmeticTime;
        // Leave two channels for important boss and incoming-attack cues.
        bool important = kind is "bell" or "chain" or "victory" or "tell";
        if (important || kind is "loot_legendary" or "loot_godwrought") _openingAudio.Emphasize(kind == "victory" ? 2.5f : 1);
        int slot = important ? 6 + _importantVoiceIndex++ % 2 : _voiceIndex++ % 6;
        var voice = _voices[slot]; voice.Stream = stream; voice.StreamPaused = IsPaused; voice.Play();
    }
    private void ClearPresentation()
    {
        ResetMouseMovement();
        ClearCombatReadability();
        ClearLootVisuals();
        foreach (var actor in _actors.Values) { RemoveChild(actor.Root); actor.Root.QueueFree(); }
        _actors.Clear();
        foreach (var mesh in _effects.Values) mesh.QueueFree(); _effects.Clear();
        _warningLabelAnchors.Clear();
        foreach (var node in _transientNodes) node.QueueFree(); _transientNodes.Clear();
        _floatingActors.Clear(); _floatingLabels.Clear(); _combatEffects?.Clear(); _lastSounds.Clear();
        if (_legendaryReadiness is not null) { _legendaryReadiness.QueueFree(); _legendaryReadiness = null; }
        if (_legendaryTrigger is not null) { _legendaryTrigger.QueueFree(); _legendaryTrigger = null; }
        _legendaryTriggerText = _legendaryTriggerPower = ""; _legendaryTriggerUntil = 0;
        _lastOathChargeCue = -30;
        _lootDropCues.Reset(_session.View.Loot);
        foreach (var voice in _voices) voice.Stop();
        _openingAudio?.Reset(_session.View);
        _shake = 0;
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
