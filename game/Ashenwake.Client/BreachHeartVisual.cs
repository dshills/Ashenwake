using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>
/// A breach held inside a ruined observatory beyond the arena wall. Phase, channel and warning
/// cues follow the current combat projection; cosmetic motion never predicts a combat event.
/// The final motion contains the surviving wound instead of depicting a restored god.
/// </summary>
public partial class BreachHeartVisual : Node3D
{
    private const float ContainmentDuration = 3.4f;
    private static readonly Color FirstPhase = new("a7a2ca");
    private static readonly Color SecondPhase = new("91b8c5");
    private static readonly Color ThirdPhase = new("c0a8c5");
    private static readonly Color Contained = new("7b939f");
    private static readonly Color DormantChannel = new("3b3b4e");
    private static readonly Color ActiveChannel = new("b0c1c9");
    private readonly Node3D[] _rings = new Node3D[3];
    private readonly Node3D[] _shutters = new Node3D[6];
    private readonly Node3D[] _channels = new Node3D[3];
    private readonly StandardMaterial3D[] _channelMaterials = new StandardMaterial3D[3];
    private readonly MeshInstance3D[] _shards = new MeshInstance3D[16];
    private readonly Node3D _heart = new() { Name = "UnclosedBreach" };
    private readonly Node3D _echoCue = new() { Name = "AnnouncedBreachEcho" };
    private readonly Node3D _returnCue = new() { Name = "AnnouncedReturningEcho" };
    private readonly Node3D _sweepCue = new() { Name = "AnnouncedSealSweep" };
    private readonly StandardMaterial3D _ringMaterial = LuminousMetal("a7a2ca", "a7a2ca", .28f, .68f);
    private readonly StandardMaterial3D _heartMaterial = LuminousMetal("49405c", "76639c", .28f, .35f);
    private readonly OmniLight3D _apertureLight = new()
    {
        Name = "BreachApertureLight",
        Position = new(0, 3.63f, 1.65f),
        ShadowEnabled = false,
        LightSpecular = .4f,
        OmniAttenuation = 1.5f
    };
    private readonly List<(MeshInstance3D Node, Mesh Mesh, Material Material)> _renderParts = [];
    private bool _initialized;
    private bool _released;
    private bool _reducedEffects;
    private int _channelMask;
    private double _clock;
    private float _containmentAge = ContainmentDuration;
    private float _initialOpening;
    private Vector3 _initialHeartRotation, _initialHeartScale;
    private readonly Vector3[] _initialRingRotations = new Vector3[3], _initialRingScales = new Vector3[3];
    private readonly float[] _initialChannelEmissions = new float[3];
    private float _initialRingEmission, _initialHeartEmission, _initialLightEnergy, _initialLightRange;
    private Color _initialRingColor, _initialLightColor;

    public int Phase { get; private set; }
    public int LivingChannels { get; private set; }
    public bool Shielded { get; private set; }
    public bool EchoWarning { get; private set; }
    public bool ReturningEchoWarning { get; private set; }
    public bool SweepWarning { get; private set; }
    public bool Defeated { get; private set; }
    public bool IsTransitioning => Defeated && _containmentAge < ContainmentDuration;
    public float VictoryProgress => Defeated ? Mathf.SmoothStep(0, 1, _containmentAge / ContainmentDuration) : 0;
    public int ArticulatedPartCount => _rings.Length + _shutters.Length + _channels.Length + 1;
    public int TransientCapacity => _shards.Length;
    internal double MotionTime => _clock;
    public int ActiveTransientCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _shards.Length; i++)
                if (_shards[i].Visible) count++;
            return count;
        }
    }

    public static BreachHeartVisual Create(float halfWidth, float halfDepth, CombatView combat, bool defeated = false)
    {
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
        var visual = new BreachHeartVisual
        {
            Name = "BreachHeartVisual",
            Position = new(0, 0, -halfDepth - 3.8f),
            Scale = new(Math.Clamp((halfWidth - 1) / 3.8f, .5f, 1), 1, 1)
        };
        visual.Build();
        visual.SetState(combat, defeated);
        return visual;
    }

    public void SetState(CombatView combat, bool defeated = false)
    {
        CombatActorView? boss = null;
        int living = 0;
        for (int i = 0; i < combat.Actors.Count; i++)
        {
            var actor = combat.Actors[i];
            // Mirrorborn may share the definition; the original encounter boss has the first id.
            if (actor.DefinitionId == "boss.breach_heart" && (boss is null || actor.Id < boss.Id)) boss = actor;
            if (actor.DefinitionId == "enemy.seal_channel" && actor.Health > 0) living++;
        }
        // Stable actor-id ordering keeps each seal attached to the same channel after a death.
        // Include dead channels in the ordering; compacting only survivors would move the lights.
        int channelMask = 0, previousId = -1;
        for (int slot = 0; slot < _channels.Length; slot++)
        {
            CombatActorView? channel = null;
            for (int i = 0; i < combat.Actors.Count; i++)
            {
                var actor = combat.Actors[i];
                if (actor.DefinitionId != "enemy.seal_channel" || actor.Id <= previousId) continue;
                if (channel is null || actor.Id < channel.Id) channel = actor;
            }
            if (channel is null) break;
            previousId = channel.Id;
            if (channel.Health > 0) channelMask |= 1 << slot;
        }
        defeated |= boss is { Health: <= 0 };
        bool shielded = !defeated && boss?.Shielded == true;
        bool echo = false, returning = false, sweep = false;
        if (!defeated && boss is not null && combat.CampaignHazards is { } hazards)
            for (int i = 0; i < hazards.Count; i++)
            {
                var hazard = hazards[i];
                if (hazard.SourceId != boss.Id || hazard.RemainingTicks <= 0) continue;
                if (hazard.ContentId == "campaign.breach_echo") echo = true;
                else if (hazard.ContentId == "campaign.returning_echo") returning = true;
                else if (hazard.ContentId == "campaign.seal_sweep") sweep = true;
            }
        if (_initialized && Phase == combat.BossPhase && LivingChannels == living && _channelMask == channelMask &&
            Defeated == defeated && Shielded == shielded && EchoWarning == echo && ReturningEchoWarning == returning && SweepWarning == sweep) return;
        if (_initialized && defeated && !Defeated && !_reducedEffects)
        {
            _containmentAge = 0;
            _initialOpening = Shielded ? 0 : 1;
            _initialHeartRotation = _heart.RotationDegrees;
            _initialHeartScale = _heart.Scale;
            for (int i = 0; i < _rings.Length; i++)
            {
                _initialRingRotations[i] = _rings[i].RotationDegrees;
                _initialRingScales[i] = _rings[i].Scale;
                _initialChannelEmissions[i] = _channelMaterials[i].EmissionEnergyMultiplier;
            }
            _initialRingEmission = _ringMaterial.EmissionEnergyMultiplier;
            _initialHeartEmission = _heartMaterial.EmissionEnergyMultiplier;
            _initialRingColor = _ringMaterial.AlbedoColor;
            _initialLightEnergy = _apertureLight.LightEnergy;
            _initialLightRange = _apertureLight.OmniRange;
            _initialLightColor = _apertureLight.LightColor;
        }
        else if (!_initialized || !defeated || _reducedEffects) _containmentAge = ContainmentDuration;
        Phase = combat.BossPhase;
        LivingChannels = living;
        Shielded = shielded;
        EchoWarning = echo;
        ReturningEchoWarning = returning;
        SweepWarning = sweep;
        Defeated = defeated;
        _channelMask = channelMask;
        _initialized = true;
        ApplyPose();
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        if (!_initialized || _released) return;
        _reducedEffects = reducedEffects;
        if (reducedEffects)
        {
            // Settle immediately, including while paused, so re-enabling motion cannot replay it.
            _clock = 0;
            _containmentAge = ContainmentDuration;
            ApplyPose();
            return;
        }
        if (paused) return;
        float step = double.IsFinite(delta) ? (float)Math.Clamp(delta, 0, .1) : 0;
        if (!Defeated) _clock = (_clock + step) % (Math.Tau / 1.35);
        _containmentAge = Math.Min(ContainmentDuration, _containmentAge + step);
        ApplyPose();
    }

    private void ApplyPose()
    {
        float victory = VictoryProgress;
        int phase = Math.Clamp(Phase, 1, 3);
        float breath = !Defeated && !_reducedEffects ? (float)Math.Sin(_clock * 1.35) : 0;
        float opening = Defeated ? Mathf.Lerp(_initialOpening, 0, victory) : Shielded ? 0 : 1;
        Color phaseColor = phase == 3 ? ThirdPhase : phase == 2 ? SecondPhase : FirstPhase;
        _ringMaterial.AlbedoColor = Defeated ? _initialRingColor.Lerp(Contained, victory) : phaseColor;
        _ringMaterial.Emission = _ringMaterial.AlbedoColor;
        _ringMaterial.EmissionEnergyMultiplier = Defeated ? Mathf.Lerp(_initialRingEmission, .1f, victory) : .24f + phase * .07f + breath * .025f;
        _heartMaterial.EmissionEnergyMultiplier = Defeated ? Mathf.Lerp(_initialHeartEmission, .1f, victory) : .25f + phase * .1f + breath * .045f;
        _apertureLight.LightColor = Defeated ? _initialLightColor.Lerp(Contained, victory) : phaseColor;
        _apertureLight.LightEnergy = Defeated ? Mathf.Lerp(_initialLightEnergy, .05f, victory) :
            (.5f + phase * .18f) * (Shielded ? .68f : 1) + Math.Clamp(LivingChannels, 0, 3) * .05f;
        _apertureLight.OmniRange = Defeated ? Mathf.Lerp(_initialLightRange, 2.5f, victory) : 3.8f + phase * .3f + (Shielded ? 0 : .4f);
        _heart.Position = new(0, 3.63f, .5f);
        _heart.RotationDegrees = Defeated ? _initialHeartRotation.Lerp(Vector3.Zero, victory) : new(0, 0, (phase - 1) * 18 + breath * 2);
        _heart.Scale = Defeated ? _initialHeartScale.Lerp(new(.22f, .59f, .5f), victory) :
            new(.67f + phase * .12f + breath * .025f, 1.32f + phase * .025f + breath * .035f, .5f);
        for (int i = 0; i < _rings.Length; i++)
        {
            float side = i == 1 ? -1 : 1;
            _rings[i].Position = new(0, 3.63f, .36f + i * .11f);
            _rings[i].RotationDegrees = Defeated ? _initialRingRotations[i].Lerp(new(0, 0, i * 30), victory) :
                new(side * (phase - 1) * (i + 1) * 9, side * (phase - 1) * 11, i * 30 + side * ((phase - 1) * 17 + breath * (i + 1)));
            _rings[i].Scale = Defeated ? _initialRingScales[i].Lerp(Vector3.One * .87f, victory) : Vector3.One * (1 + (phase - 1) * .025f);
        }
        for (int i = 0; i < _shutters.Length; i++)
        {
            float angle = i * Mathf.Tau / _shutters.Length;
            float radius = 1.25f + opening * .55f;
            _shutters[i].Position = new(Mathf.Sin(angle) * radius, 3.63f + Mathf.Cos(angle) * radius, 1.05f);
            _shutters[i].RotationDegrees = new(0, 0, -i * 60 + opening * 24);
        }
        for (int i = 0; i < _channels.Length; i++)
        {
            bool alive = (_channelMask & (1 << i)) != 0;
            _channels[i].Position = new((i - 1) * 2.3f, 1.05f, .68f);
            _channels[i].RotationDegrees = new(0, 0, alive ? 0 : 30);
            _channelMaterials[i].AlbedoColor = alive ? ActiveChannel : DormantChannel;
            _channelMaterials[i].EmissionEnergyMultiplier = alive ? Defeated ? Mathf.Lerp(_initialChannelEmissions[i], .12f, victory) : .38f + breath * .025f : 0;
        }
        _echoCue.Visible = EchoWarning;
        _returnCue.Visible = ReturningEchoWarning;
        _sweepCue.Visible = SweepWarning;
        bool containing = Defeated && _containmentAge < ContainmentDuration && !_reducedEffects;
        for (int i = 0; i < _shards.Length; i++)
        {
            var shard = _shards[i];
            float age = _containmentAge - i * .06f;
            shard.Visible = containing && age is >= 0 and < 2.1f;
            if (!shard.Visible) continue;
            float life = age / 2.1f;
            float angle = i * 2.399f + life * .5f;
            float radius = Mathf.Lerp(2.18f, .27f, life);
            shard.Position = new(Mathf.Sin(angle) * radius, 3.63f + Mathf.Cos(angle) * radius, 1.37f);
            shard.RotationDegrees = new(0, 0, i * 47 + life * 90);
            shard.Scale = Vector3.One * (1 - life * .85f);
        }
    }

    private static StandardMaterial3D LuminousMetal(string color, string glow, float energy, float roughness = .68f)
    {
        var material = SurfaceMaterials.Create(color, SurfaceKind.Metal);
        material.Metallic = .25f; material.Roughness = roughness;
        material.EmissionEnabled = true; material.Emission = new(glow); material.EmissionEnergyMultiplier = energy;
        return material;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _released) return;
        _released = true;
        var meshes = _renderParts.Select(part => part.Mesh).DistinctBy(mesh => mesh.GetInstanceId()).ToArray();
        var materials = _renderParts.Select(part => part.Material).Append(_ringMaterial).Append(_heartMaterial)
            .DistinctBy(material => material.GetInstanceId()).ToArray();
        foreach (var part in _renderParts)
            if (GodotObject.IsInstanceValid(part.Node)) { part.Node.Mesh = null; part.Node.MaterialOverride = null; }
        foreach (var mesh in meshes) mesh.Dispose();
        foreach (var material in materials) material.Dispose();
        // These resources belong to this rig; factory grain textures remain shared and alive.
        _renderParts.Clear();
    }

    private static ArrayMesh ContainmentPlate(Vector2[] outline, float depth, float inset = .065f, bool apertureSurface = false)
    {
        // Closed chamfers retain the authored taper and fracture edges. The aperture
        // version follows the void lens instead of floating on a flat plane in front of it.
        float area = 0;
        for (int i = 0; i < outline.Length; i++) area += outline[i].Cross(outline[(i + 1) % outline.Length]);
        if (area > 0) Array.Reverse(outline);
        var triangles = Geometry2D.TriangulatePolygon(outline);
        if (triangles.Length != (outline.Length - 2) * 3) throw new InvalidOperationException("Invalid carved slab outline.");
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 OnLens(Vector3 point) => new(point.X, point.Y,
            MathF.Sqrt(Math.Max(.04f, .72f * .72f - point.X * point.X - point.Y * point.Y)) + point.Z + .012f);
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (apertureSurface) { a = OnLens(a); b = OnLens(b); c = OnLens(c); }
            if ((b - a).Cross(c - a).Dot(outward) > 0) (b, c) = (c, b);
            var normal = -(b - a).Cross(c - a).Normalized();
            Vector3 up = Math.Abs(normal.Y) < .9f ? Vector3.Up : Vector3.Right;
            Vector3 u = up.Cross(normal).Normalized(), v = normal.Cross(u);
            foreach (var point in new[] { a, b, c })
            {
                surface.SetNormal(normal); surface.SetUV(new(point.Dot(u), point.Dot(v)));
                surface.AddVertex(point);
            }
        }
        Vector3 At(int i, float scale, float z) => new(outline[i].X * scale, outline[i].Y * scale, z);
        float front = depth * .5f, shoulder = front - Math.Min(depth * .3f, .065f);
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Triangle(At(triangles[i], 1, -front), At(triangles[i + 1], 1, -front), At(triangles[i + 2], 1, -front), Vector3.Forward);
            Triangle(At(triangles[i], 1 - inset, front), At(triangles[i + 1], 1 - inset, front), At(triangles[i + 2], 1 - inset, front), Vector3.Back);
        }
        for (int i = 0; i < outline.Length; i++)
        {
            int next = (i + 1) % outline.Length;
            var edge = outline[next] - outline[i];
            var normal = new Vector3(-edge.Y, edge.X, 0);
            Triangle(At(i, 1, -front), At(next, 1, -front), At(next, 1, shoulder), normal);
            Triangle(At(i, 1, -front), At(next, 1, shoulder), At(i, 1, shoulder), normal);
            normal.Z = edge.Length();
            Triangle(At(i, 1, shoulder), At(next, 1, shoulder), At(next, 1 - inset, front), normal);
            Triangle(At(i, 1, shoulder), At(next, 1 - inset, front), At(i, 1 - inset, front), normal);
        }
        surface.GenerateTangents(); surface.Index();
        return surface.Commit();
    }

    private void Build()
    {
        AddChild(_apertureLight);
        var frame = new EnvironmentBuilder(this, "FracturedBreachObservatory");
        frame.Box(new(7.2f, .28f, 2.2f), new(0, .14f, -.15f), "32303e");
        frame.Box(new(6.65f, .16f, 1.9f), new(0, .36f, -.15f), "5b586e");
        for (int side = -1; side <= 1; side += 2)
        {
            frame.Box(new(.57f, 4.9f, .87f), new(side * 3.16f, 2.9f, -.28f), "535267");
            frame.Box(new(.16f, 4.75f, .1f), new(side * 2.95f, 2.89f, .2f), "858497");
            frame.Box(new(.87f, .32f, 1.06f), new(side * 3.16f, .59f, -.28f), "797487");
            frame.Box(new(.77f, .25f, 1.01f), new(side * 3.16f, 5.49f, -.28f), "858497");
            frame.Beam(new(side * 3.16f, 5.7f, -.28f), new(side * 1.98f, 6.27f, -.28f), .3f, "797487");
            frame.Beam(new(side * 1.88f, 6.34f, -.28f), new(side * .52f, 6.49f, -.28f), .23f, "535267");
            for (int glyph = 0; glyph < 6; glyph++)
            {
                float y = 1.1f + glyph * .72f;
                frame.Box(new(.15f, .035f, .07f), new(side * 3.18f, y, .185f), "b0a4b5", new(0, 0, side * 24));
                frame.Box(new(.035f, .23f, .07f), new(side * 3.18f, y + .065f, .185f), "b0a4b5");
            }
        }
        frame.Box(new(.26f, .45f, .45f), new(-.25f, 6.43f, -.28f), "858497", new(0, 0, -16));
        frame.Box(new(.26f, .32f, .45f), new(.28f, 6.49f, -.28f), "797487", new(0, 0, 19));
        // A dark rear plate makes the central aperture legible against repeating architecture.
        frame.Cylinder(2.33f, 2.33f, .16f, new(0, 3.63f, -.3f), "252433", new(90, 0, 0));
        frame.Torus(2.34f, 2.48f, new(0, 3.63f, -.19f), "535267", new(90, 0, 0));
        frame.Flush();

        using var runeMesh = new BoxMesh { Size = new(.055f, .18f, .045f) };
        using var crossStroke = new BoxMesh { Size = new(.15f, .035f, .045f) };
        var ringStone = SurfaceMaterials.Create("817b91", SurfaceKind.Metal); ringStone.Metallic = .18f; ringStone.Roughness = .7f;
        var ringEdge = SurfaceMaterials.Create("b3a1b6", SurfaceKind.Metal);
        for (int i = 0; i < _rings.Length; i++)
        {
            _rings[i] = new Node3D { Name = "BreachOrbitalSeal" + i };
            AddChild(_rings[i]);
            float radius = 1.78f + i * .22f;
            _rings[i].AddChild(new MeshInstance3D
            {
                Name = "OrbitalStoneRing",
                Mesh = new TorusMesh { InnerRadius = radius, OuterRadius = radius + .095f, Rings = 36, RingSegments = 7 },
                MaterialOverride = ringStone,
                RotationDegrees = new(90, 0, 0)
            });
            _rings[i].AddChild(new MeshInstance3D
            {
                Name = "OrbitalInnerRim",
                Position = new(0, 0, .065f),
                Mesh = new TorusMesh { InnerRadius = radius - .06f, OuterRadius = radius + .015f, Rings = 36, RingSegments = 6 },
                MaterialOverride = ringEdge,
                RotationDegrees = new(90, 0, 0)
            });
            var fittings = new EnvironmentBuilder(_rings[i], "OrbitalSealFittings");
            using var glyphs = new SurfaceTool(); glyphs.Begin(Mesh.PrimitiveType.Triangles);
            for (int glyph = 0; glyph < 12; glyph++)
            {
                float angle = glyph * Mathf.Tau / 12;
                var position = new Vector3(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, .095f);
                var rotation = Basis.FromEuler(new(0, 0, -angle));
                glyphs.AppendFrom(runeMesh, 0, new Transform3D(rotation, position));
                glyphs.AppendFrom(crossStroke, 0, new Transform3D(rotation, position + rotation * new Vector3(0, glyph % 2 == 0 ? .035f : -.035f, .005f)));
                if (glyph % 3 != 0) continue;
                fittings.Box(new(.14f, .23f, .065f), position - Vector3.Back * .06f, "9c94ad", new(0, 0, -glyph * 30), surface: SurfaceKind.Metal);
                fittings.Cylinder(.035f, .025f, .045f, position + rotation * new Vector3(0, .125f, -.025f), "9c94ad", new(90, 0, 0), surface: SurfaceKind.Metal);
            }
            fittings.Flush();
            _rings[i].AddChild(new MeshInstance3D { Name = "OrbitingSealInscriptions", Mesh = glyphs.Commit(), MaterialOverride = _ringMaterial });
        }

        AddChild(_heart);
        _heart.AddChild(new MeshInstance3D
        {
            Name = "FacetedVoidHeart",
            Mesh = new SphereMesh { Radius = .72f, Height = 1.44f, RadialSegments = 16, Rings = 10 },
            MaterialOverride = _heartMaterial
        });
        _heart.AddChild(new MeshInstance3D
        {
            Name = "UnhealedCentralFracture",
            Mesh = ContainmentPlate([new(-.018f, .64f), new(.082f, .4f), new(.027f, .2f), new(.115f, -.035f),
                new(.043f, -.25f), new(.087f, -.5f), new(-.018f, -.64f), new(-.059f, -.36f), new(-.028f, -.18f),
                new(-.091f, .04f), new(-.035f, .3f)], .055f, .06f, apertureSurface: true),
            MaterialOverride = _ringMaterial
        });
        var lens = new EnvironmentBuilder(_heart, "FracturedLensRim");
        lens.Torus(.675f, .735f, new(0, 0, .08f), "776b88", new(90, 0, 0), surface: SurfaceKind.Metal);
        lens.Branch(new(.075f, .16f, .71f), new(.27f, .29f, .615f), .023f, .006f, "776b88", SurfaceKind.Metal);
        lens.Branch(new(-.055f, -.12f, .72f), new(-.27f, -.3f, .61f), .022f, .006f, "776b88", SurfaceKind.Metal);
        lens.Flush();
        var shutterFace = SurfaceMaterials.Create("898298", SurfaceKind.Stone);
        var shutterInset = SurfaceMaterials.Create("434255", SurfaceKind.Metal);
        Vector2[] blade = [new(-.18f, .49f), new(.16f, .49f), new(.28f, .3f), new(.21f, -.34f),
            new(0, -.5f), new(-.26f, -.27f), new(-.3f, .23f)];
        var shutterMesh = ContainmentPlate(blade, .16f);
        var shutterCore = ContainmentPlate(blade.Select(point => point * .74f).ToArray(), .055f);
        var shutterGlyph = ContainmentPlate([new(-.024f, .23f), new(.045f, .15f), new(.028f, -.06f), new(.08f, -.13f),
            new(.006f, -.235f), new(-.041f, -.1f)], .035f, .08f);
        for (int i = 0; i < _shutters.Length; i++)
        {
            _shutters[i] = new Node3D { Name = "ContainmentShutter" + i };
            AddChild(_shutters[i]);
            _shutters[i].AddChild(new MeshInstance3D { Name = "SealFace", Mesh = shutterMesh, MaterialOverride = shutterFace, RotationDegrees = new(0, 0, 12) });
            _shutters[i].AddChild(new MeshInstance3D { Name = "SealInset", Mesh = shutterCore, MaterialOverride = shutterInset, Position = new(0, 0, .1f), RotationDegrees = new(0, 0, 12) });
            _shutters[i].AddChild(new MeshInstance3D { Name = "SealGlyph", Mesh = shutterGlyph, MaterialOverride = _ringMaterial, Position = new(0, 0, .151f), RotationDegrees = new(0, 0, -12) });
            var fittings = new EnvironmentBuilder(_shutters[i], "ShutterEngravedRim");
            for (int side = -1; side <= 1; side += 2)
            {
                fittings.Branch(new(side * .13f, -.16f, .14f), new(side * .16f, .24f, .14f), .019f, .014f, "b3a1b6", SurfaceKind.Metal);
                fittings.Cylinder(.027f, .018f, .03f, new(side * .14f, .31f, .142f), "b3a1b6", new(90, 0, 0), surface: SurfaceKind.Metal);
            }
            fittings.Flush();
        }

        var channelStone = SurfaceMaterials.Create("656477", SurfaceKind.Stone);
        var channelMesh = new TorusMesh { InnerRadius = .38f, OuterRadius = .46f, Rings = 24, RingSegments = 6 };
        var channelDisk = ContainmentPlate(Enumerable.Range(0, 12).Select(i => new Vector2(Mathf.Sin(i * Mathf.Tau / 12), Mathf.Cos(i * Mathf.Tau / 12)) * .365f).ToArray(), .105f, .11f);
        var channelGlyph = new BoxMesh { Size = new(.065f, .4f, .035f) };
        for (int i = 0; i < _channels.Length; i++)
        {
            _channels[i] = new Node3D { Name = "BreachChannelSeal" + i };
            AddChild(_channels[i]);
            _channelMaterials[i] = LuminousMetal("b0c1c9", "b0c1c9", .38f);
            _channels[i].AddChild(new MeshInstance3D { Name = "ChannelStone", Mesh = channelDisk, MaterialOverride = shutterInset });
            _channels[i].AddChild(new MeshInstance3D { Name = "ChannelRing", Mesh = channelMesh, MaterialOverride = channelStone, RotationDegrees = new(90, 0, 0) });
            _channels[i].AddChild(new MeshInstance3D { Name = "ChannelLivingMark", Mesh = channelGlyph, MaterialOverride = _channelMaterials[i], Position = new(0, 0, .075f) });
            _channels[i].AddChild(new MeshInstance3D { Name = "ChannelCrossbar", Mesh = channelGlyph, MaterialOverride = _channelMaterials[i], Position = new(0, 0, .075f), RotationDegrees = new(0, 0, 90) });
            var engravings = new EnvironmentBuilder(_channels[i], "ChannelIdentityEngraving");
            for (int notch = 0; notch <= i; notch++)
                engravings.Box(new(.027f, .069f, .017f), new((notch - i * .5f) * .085f, .267f, .062f), "b3a1b6", surface: SurfaceKind.Metal);
            for (int edge = 0; edge < 6; edge++)
            {
                float angle = edge * Mathf.Tau / 6;
                engravings.Cylinder(.022f, .015f, .025f, new(Mathf.Sin(angle) * .324f, Mathf.Cos(angle) * .324f, .065f),
                    "b3a1b6", new(90, 0, 0), surface: SurfaceKind.Metal);
            }
            engravings.Flush();
        }

        // These static symbols coexist with the authoritative floor telegraphs and stay in reduced effects.
        AddChild(_echoCue);
        AddChild(_returnCue);
        AddChild(_sweepCue);
        _echoCue.Position = new(-.79f, 3.63f, 1.5f);
        _returnCue.Position = new(.79f, 3.63f, 1.5f);
        _sweepCue.Position = new(0, 3.63f, 1.57f);
        var cueMaterial = new StandardMaterial3D
        {
            AlbedoColor = new("e0cca9"),
            EmissionEnabled = true,
            Emission = new("e0cca9"),
            EmissionEnergyMultiplier = .4f,
            Roughness = .7f
        };
        var echoRing = new TorusMesh { InnerRadius = .24f, OuterRadius = .29f, Rings = 18, RingSegments = 5 };
        _echoCue.AddChild(new MeshInstance3D { Name = "FirstEchoCircle", Mesh = echoRing, MaterialOverride = cueMaterial, RotationDegrees = new(90, 0, 0) });
        _returnCue.AddChild(new MeshInstance3D { Name = "ReturningEchoCircle", Mesh = echoRing, MaterialOverride = cueMaterial, RotationDegrees = new(90, 0, 0) });
        _returnCue.AddChild(new MeshInstance3D { Name = "ReturningEchoInnerCircle", Mesh = echoRing, MaterialOverride = cueMaterial, Scale = Vector3.One * .56f, Position = new(0, 0, .025f), RotationDegrees = new(90, 0, 0) });
        _sweepCue.AddChild(new MeshInstance3D { Name = "DiagonalSweep", Mesh = new BoxMesh { Size = new(2.8f, .065f, .045f) }, MaterialOverride = cueMaterial, RotationDegrees = new(0, 0, 26) });
        var shardMesh = ContainmentPlate([new(-.03f, .09f), new(.035f, .067f), new(.052f, -.03f), new(0, -.093f), new(-.041f, -.016f)], .035f, .08f);
        var shardMaterial = new StandardMaterial3D
        {
            AlbedoColor = new("afb6c8"),
            EmissionEnabled = true,
            Emission = new("afb6c8"),
            EmissionEnergyMultiplier = .35f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        for (int i = 0; i < _shards.Length; i++)
        {
            _shards[i] = new MeshInstance3D { Name = "ContainedBreachShard" + i, Mesh = shardMesh, MaterialOverride = shardMaterial, Visible = false };
            AddChild(_shards[i]);
        }
        void RecordResources(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is MeshInstance3D mesh)
                    _renderParts.Add((mesh, mesh.Mesh, mesh.MaterialOverride ?? mesh.Mesh.SurfaceGetMaterial(0)));
                RecordResources(child);
            }
        }
        RecordResources(this);
    }
}
