using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>
/// A cosmetic extraction engine beyond the northern arena wall. Armor follows Core's defense
/// projection and vent direction follows the live warning geometry; neither invents a boss phase.
/// All meshes and materials are created once. Animation only changes bounded rig transforms.
/// </summary>
public partial class FurnaceSpindleVisual : Node3D
{
    private const float ShutdownDuration = 3.2f;
    private const float GuardReactionDuration = .62f;
    private static readonly Color HotCore = new("ffd08b");
    private static readonly Color CoolingCore = new("796d61");
    private readonly Node3D _axle = new() { Name = "FurnaceCoreAxle", Position = new(0, 3.2f, .65f) };
    private readonly Node3D[] _shutters = new Node3D[4];
    private readonly Node3D[] _vents = new Node3D[4];
    private readonly Node3D[] _pistons = new Node3D[2];
    private readonly MeshInstance3D[] _embers = new MeshInstance3D[12];
    private readonly StandardMaterial3D[] _ventMaterials = new StandardMaterial3D[4];
    private readonly StandardMaterial3D _coreMaterial = HeatedMetal("ffd08b", "ffb568", .95f);
    private readonly OmniLight3D _coreLight = new()
    {
        Name = "CoreGlowLight",
        Position = new(0, 0, .9f),
        ShadowEnabled = false,
        LightColor = new("ffc68d"),
        LightSpecular = .5f,
        OmniAttenuation = 1.5f
    };
    private readonly List<(MeshInstance3D Node, Mesh Mesh, Material Material)> _renderParts = [];
    private bool _initialized;
    private bool _released;
    private bool _reducedEffects;
    private double _clock, _breathingPhase;
    private float _shutdownAge = ShutdownDuration;
    private float _shutdownStartAngle;
    private float _shutdownStartEnergy, _shutdownStartRange, _shutdownStartEmission;
    private Vector3 _shutdownStartAxlePosition;
    private readonly Vector3[] _shutdownShutterPositions = new Vector3[4];
    private readonly Vector3[] _shutdownShutterRotations = new Vector3[4];
    private readonly Vector3[] _shutdownPistonPositions = new Vector3[2];
    private float _guardReactionAge = GuardReactionDuration;

    public bool Guarded { get; private set; }
    public bool Defeated { get; private set; }
    public string VentOrientation { get; private set; } = "None";
    public bool VentWarning => VentOrientation != "None";
    public bool IsTransitioning => Defeated && _shutdownAge < ShutdownDuration;
    public float VictoryProgress => Defeated ? Mathf.SmoothStep(0, 1, _shutdownAge / ShutdownDuration) : 0;
    public int ArticulatedPartCount => 1 + _shutters.Length + _vents.Length + _pistons.Length;
    public int TransientCapacity => _embers.Length;
    public int ActiveTransientCount => _embers.Count(ember => ember.Visible);
    internal double MotionTime => _clock;

    public static FurnaceSpindleVisual Create(float halfWidth, float halfDepth, CombatView combat, bool defeated = false)
    {
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
        var visual = new FurnaceSpindleVisual
        {
            Name = "FurnaceSpindleVisual",
            Position = new(0, 0, -halfDepth - 3.4f),
            Scale = new(Math.Clamp((halfWidth - 1) / 3.2f, .5f, 1), 1, 1)
        };
        visual.Build();
        visual.SetState(combat, defeated);
        return visual;
    }

    public void SetState(CombatView combat, bool defeated = false)
    {
        CombatActorView? boss = null;
        foreach (var actor in combat.Actors)
            if (actor.DefinitionId == "boss.furnace_spindle") { boss = actor; break; }
        defeated |= boss is { Health: <= 0 };
        bool guarded = !defeated && boss?.Guarded == true;
        string orientation = "None";
        if (!defeated && boss is not null && combat.CampaignHazards is { } hazards)
            foreach (var hazard in hazards)
            {
                if (hazard.SourceId != boss.Id || hazard.ContentId != "campaign.furnace_vent" || hazard.RemainingTicks <= 0) continue;
                orientation = Math.Abs((long)hazard.End.X - hazard.Position.X) >= Math.Abs((long)hazard.End.Z - hazard.Position.Z) ? "Horizontal" : "Vertical";
                break;
            }
        if (_initialized && Defeated == defeated && Guarded == guarded && VentOrientation == orientation) return;
        if (_initialized && defeated && !Defeated && !_reducedEffects)
        {
            _shutdownAge = 0;
            _shutdownStartAngle = _axle.Rotation.Z;
            _shutdownStartEnergy = _coreLight.LightEnergy;
            _shutdownStartRange = _coreLight.OmniRange;
            _shutdownStartEmission = _coreMaterial.EmissionEnergyMultiplier;
            _shutdownStartAxlePosition = _axle.Position;
            for (int i = 0; i < _shutters.Length; i++)
            {
                _shutdownShutterPositions[i] = _shutters[i].Position;
                _shutdownShutterRotations[i] = _shutters[i].RotationDegrees;
            }
            for (int i = 0; i < _pistons.Length; i++) _shutdownPistonPositions[i] = _pistons[i].Position;
        }
        else if (!_initialized || !defeated || _reducedEffects) _shutdownAge = ShutdownDuration;
        // Armor assumes Core's new defense pose immediately. Only the subsequent mechanical
        // recoil is timed locally, and identical snapshots cannot restart that recoil.
        if (_initialized && !defeated && !Defeated && Guarded != guarded && !_reducedEffects) _guardReactionAge = 0;
        else if (!_initialized || defeated || _reducedEffects) _guardReactionAge = GuardReactionDuration;
        Guarded = guarded;
        Defeated = defeated;
        VentOrientation = orientation;
        _initialized = true;
        ApplyPose();
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        if (!_initialized || _released) return;
        _reducedEffects = reducedEffects;
        if (reducedEffects)
        {
            // Consume the shutdown even while paused; toggling effects back on never replays it.
            _clock = 0;
            _breathingPhase = 0;
            _shutdownAge = ShutdownDuration;
            _guardReactionAge = GuardReactionDuration;
            ApplyPose();
            return;
        }
        if (paused) return;
        float step = double.IsFinite(delta) ? (float)Math.Clamp(delta, 0, .1) : 0;
        if (!Defeated)
        {
            // Rotation and breathing have different periods; wrapping each phase separately
            // keeps long sessions finite without a visible jump in core brightness.
            _clock = (_clock + step) % 40;
            _breathingPhase = (_breathingPhase + step * 1.8) % Math.Tau;
        }
        _shutdownAge = Math.Min(ShutdownDuration, _shutdownAge + step);
        _guardReactionAge = Math.Min(GuardReactionDuration, _guardReactionAge + step);
        ApplyPose();
    }

    private void ApplyPose()
    {
        float victory = VictoryProgress;
        float breath = !Defeated && !_reducedEffects ? (float)Math.Sin(_breathingPhase) : 0;
        float reactionTime = _guardReactionAge / GuardReactionDuration;
        float reaction = !Defeated && !_reducedEffects ? Mathf.Sin(reactionTime * Mathf.Pi) * (1 - reactionTime) : 0;
        float brake = ShutdownStage(0, .85f);
        float drop = ShutdownStage(.55f, 1.5f);
        float cooling = ShutdownStage(.2f, ShutdownDuration);
        _axle.Position = Defeated ? _shutdownStartAxlePosition.Lerp(new(0, 2.78f, .65f), drop) :
            new(0, 3.2f + breath * (Guarded ? .006f : .025f), .65f + (Guarded ? -.055f : .12f) * reaction);
        float rotation = Defeated ? Mathf.RadToDeg(Mathf.LerpAngle(_shutdownStartAngle, Mathf.DegToRad(-24), brake)) : _reducedEffects ? 0 : (float)(_clock * 9);
        _axle.RotationDegrees = new(0, 0, rotation);
        _coreMaterial.AlbedoColor = HotCore.Lerp(CoolingCore, victory);
        _coreMaterial.Emission = new Color("ffb568").Lerp(new Color("8a7f73"), victory);
        _coreMaterial.EmissionEnergyMultiplier = Defeated ? Mathf.Lerp(_shutdownStartEmission, .04f, cooling) : (Guarded ? .55f : 1.1f) + breath * (Guarded ? .035f : .11f);
        _coreLight.LightColor = new Color("ffc68d").Lerp(new Color("9a8d7d"), victory);
        _coreLight.LightEnergy = Defeated ? Mathf.Lerp(_shutdownStartEnergy, .08f, cooling) : Guarded ? .9f : 1.65f;
        _coreLight.OmniRange = Defeated ? Mathf.Lerp(_shutdownStartRange, 2.8f, cooling) : Guarded ? 4.2f : 5.2f;
        for (int i = 0; i < _shutters.Length; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            float up = i < 2 ? -1 : 1;
            float opening = Guarded ? 0 : 1;
            float release = ShutdownStage(.12f + i * .12f, 1.1f + i * .12f);
            _shutters[i].Position = Defeated ? _shutdownShutterPositions[i].Lerp(new(side, 3.0f + up * .68f, 1.37f), release) :
                new(side * (.43f + opening * .57f), 3.2f + up * (.43f + opening * .25f), 1.37f + reaction * .075f);
            _shutters[i].RotationDegrees = Defeated ? _shutdownShutterRotations[i].Lerp(new(0, -side * 32, -side * up * 8), release) :
                new(0, side * (-opening * 24 - reaction * 7), -side * up * reaction * 3);
        }
        for (int i = 0; i < _vents.Length; i++)
        {
            bool active = !Defeated && (i < 2 ? VentOrientation == "Horizontal" : VentOrientation == "Vertical");
            // Warning louvers clear immediately on defeat, then droop below the inactive pose.
            _vents[i].RotationDegrees = new(active ? -38 : Defeated ? -16 * ShutdownStage(.05f + i * .08f, .7f + i * .08f) : 0, 0, i < 2 ? 90 : 0);
            _ventMaterials[i].EmissionEnergyMultiplier = active ? 1.25f + breath * .08f : Mathf.Lerp(.13f, .015f, victory);
        }
        for (int i = 0; i < _pistons.Length; i++)
        {
            float side = i == 0 ? -1 : 1;
            float depressurize = ShutdownStage(1.05f + i * .16f, 2.2f + i * .16f);
            _pistons[i].Position = Defeated ? _shutdownPistonPositions[i].Lerp(new(side * 2.65f, 1.05f, -.05f), depressurize) :
                new(side * 2.65f, 1.25f + (Guarded ? .28f : 0) + reaction * (i == 0 ? .08f : -.05f), -.05f);
        }
        bool shedding = Defeated && _shutdownAge < ShutdownDuration && !_reducedEffects;
        for (int i = 0; i < _embers.Length; i++)
        {
            var ember = _embers[i];
            float age = _shutdownAge - i * .045f;
            ember.Visible = shedding && age is >= 0 and < 1.9f;
            if (!ember.Visible)
            {
                ember.Transform = Transform3D.Identity;
                continue;
            }
            float life = age / 1.9f;
            float angle = i * 2.399f;
            ember.Position = new(Mathf.Sin(angle) * (.4f + life * .8f), 3.1f + Mathf.Cos(angle) * .48f + life * 1.4f, 1.6f + Mathf.Sin(angle) * .12f);
            ember.Scale = Vector3.One * (1 - life) * .8f;
            ember.RotationDegrees = new(0, 0, i * 31 + life * 60);
        }
    }

    private float ShutdownStage(float start, float end) => Defeated ? Mathf.SmoothStep(0, 1, Math.Clamp((_shutdownAge - start) / (end - start), 0, 1)) : 0;

    private static StandardMaterial3D HeatedMetal(string color, string glow, float energy)
    {
        var material = SurfaceMaterials.Create(color, SurfaceKind.Metal);
        material.Roughness = .6f; material.Metallic = .38f;
        material.EmissionEnabled = true; material.Emission = new(glow);
        material.EmissionEnergyMultiplier = energy;
        return material;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _released) return;
        _released = true;
        var meshes = _renderParts.Select(part => part.Mesh).DistinctBy(mesh => mesh.GetInstanceId()).ToArray();
        var materials = _renderParts.Select(part => part.Material).Append(_coreMaterial).DistinctBy(material => material.GetInstanceId()).ToArray();
        foreach (var part in _renderParts)
            if (GodotObject.IsInstanceValid(part.Node)) { part.Node.Mesh = null; part.Node.MaterialOverride = null; }
        foreach (var mesh in meshes) mesh.Dispose();
        foreach (var material in materials) material.Dispose();
        // SurfaceMaterials owns the reusable grain textures; only this rig's resources die.
        _renderParts.Clear();
    }

    private static ArrayMesh ArmorPlate(Vector2 size, float depth, float bevel)
    {
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] Ring(float inset, float z)
        {
            float x = size.X * .5f - inset, y = size.Y * .5f - inset;
            float cut = Math.Min(Math.Min(x, y) * .35f, bevel);
            return [new(-x + cut, y, z), new(x - cut, y, z), new(x, y - cut, z), new(x, -y + cut, z),
                new(x - cut, -y, z), new(-x + cut, -y, z), new(-x, -y + cut, z), new(-x, y - cut, z)];
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if ((b - a).Cross(c - a).Dot(outward) > 0) (b, c) = (c, b);
            var normal = -(b - a).Cross(c - a).Normalized();
            Vector3 up = Math.Abs(normal.Y) < .9f ? Vector3.Up : Vector3.Right;
            Vector3 u = up.Cross(normal).Normalized(), v = normal.Cross(u);
            foreach (var point in new[] { a, b, c })
            {
                surface.SetNormal(normal); surface.SetUV(new(point.Dot(u) / size.X + .5f, point.Dot(v) / size.Y + .5f));
                surface.AddVertex(point);
            }
        }
        var back = Ring(0, -depth * .5f);
        var edge = Ring(0, depth * .5f - Math.Min(bevel, depth * .35f));
        var face = Ring(Math.Min(bevel * .45f, Math.Min(size.X, size.Y) * .12f), depth * .5f);
        for (int i = 0; i < 8; i++)
        {
            int next = (i + 1) % 8;
            Triangle(new(0, 0, -depth * .5f), back[i], back[next], Vector3.Forward);
            Triangle(new(0, 0, depth * .5f), face[i], face[next], Vector3.Back);
            var normal = new Vector3(back[i].X + back[next].X, back[i].Y + back[next].Y, 0);
            Triangle(back[i], back[next], edge[next], normal);
            Triangle(back[i], edge[next], edge[i], normal);
            normal.Z = bevel;
            Triangle(edge[i], edge[next], face[next], normal);
            Triangle(edge[i], face[next], face[i], normal);
        }
        surface.GenerateTangents(); surface.Index();
        return surface.Commit();
    }

    private void Build()
    {
        var frame = new EnvironmentBuilder(this, "SpindleCastIronFrame");
        frame.Box(new(6.3f, .38f, 2.35f), new(0, .19f, -.1f), "302c2b", surface: SurfaceKind.Stone);
        frame.Box(new(5.85f, .12f, 2.16f), new(0, .44f, -.1f), "65564a", surface: SurfaceKind.Metal);
        frame.Cylinder(1.46f, 1.46f, .48f, new(0, 3.2f, .13f), "2f3031", new(90, 0, 0), surface: SurfaceKind.Metal);
        frame.Torus(1.35f, 1.54f, new(0, 3.2f, .48f), "9c7852", new(90, 0, 0), surface: SurfaceKind.Metal);
        frame.Torus(1.58f, 1.72f, new(0, 3.2f, .11f), "464343", new(90, 0, 0), surface: SurfaceKind.Metal);
        frame.Torus(1.38f, 1.45f, new(0, 3.2f, .66f), "ad9169", new(90, 0, 0), surface: SurfaceKind.Metal);
        for (int side = -1; side <= 1; side += 2)
        {
            frame.Box(new(.6f, 4.55f, .72f), new(side * 1.85f, 2.75f, -.48f), "464343", surface: SurfaceKind.Metal);
            frame.Box(new(.16f, 4.1f, .1f), new(side * 2.09f, 2.75f, -.065f), "65564a", surface: SurfaceKind.Metal);
            frame.Box(new(.82f, .2f, .92f), new(side * 1.85f, .7f, -.48f), "9c7852", surface: SurfaceKind.Metal);
            frame.Box(new(.82f, .25f, .92f), new(side * 1.85f, 4.7f, -.48f), "65564a", surface: SurfaceKind.Metal);
            frame.Beam(new(side * 1.85f, 4.95f, -.48f), new(side * .57f, 5.65f, -.48f), .37f, "464343", SurfaceKind.Metal);
            frame.Beam(new(side * 1.85f, .82f, .08f), new(side * .8f, 1.88f, .08f), .2f, "65564a", SurfaceKind.Metal);
            frame.Cylinder(.24f, .24f, 3.8f, new(side * 2.65f, 2.4f, -.28f), "65564a", surface: SurfaceKind.Metal);
            // Segmented cylindrical elbows keep the supply runs round through their bends.
            Vector3[] bend = [new(side * 2.65f, 4.25f, -.28f), new(side * 2.61f, 4.48f, -.28f),
                new(side * 2.45f, 4.64f, -.28f), new(side * 2.22f, 4.69f, -.28f), new(side * 1.85f, 4.69f, -.28f)];
            for (int i = 0; i < bend.Length - 1; i++) frame.Branch(bend[i], bend[i + 1], .2f, .2f, "65564a", SurfaceKind.Metal);
            for (int collar = 0; collar < 3; collar++)
            {
                float y = 1.5f + collar * 1.3f;
                frame.Cylinder(.29f, .29f, .13f, new(side * 2.65f, y, -.28f), "a68053", surface: SurfaceKind.Metal);
                frame.Torus(.235f, .275f, new(side * 2.65f, y + .08f, -.28f), "ad9169", surface: SurfaceKind.Metal);
            }
            frame.Box(new(.4f, 1.05f, .55f), new(side * 2.65f, 1.05f, -.32f), "302c2b", surface: SurfaceKind.Metal);
            for (int rivet = 0; rivet < 5; rivet++)
                frame.Cylinder(.067f, .047f, .075f, new(side * 1.85f, 1.15f + rivet * .74f, -.075f), "ad9169", new(90, 0, 0), surface: SurfaceKind.Metal);
        }
        for (int bolt = 0; bolt < 12; bolt++)
        {
            float angle = bolt * Mathf.Tau / 12;
            frame.Cylinder(.055f, .045f, .075f, new(Mathf.Sin(angle) * 1.46f, 3.2f + Mathf.Cos(angle) * 1.46f, .66f),
                "ad9169", new(90, 0, 0), surface: SurfaceKind.Metal);
        }
        frame.Box(new(1.45f, .42f, .85f), new(0, 5.62f, -.48f), "65564a", surface: SurfaceKind.Metal);
        frame.Cylinder(.29f, .23f, 1.03f, new(0, 6.27f, -.48f), "464343", surface: SurfaceKind.Metal);
        frame.Cylinder(.38f, .38f, .16f, new(0, 6.78f, -.48f), "a68053", surface: SurfaceKind.Metal);
        frame.Flush();

        AddChild(_axle); _axle.AddChild(_coreLight);
        _axle.AddChild(new MeshInstance3D
        {
            Name = "ExposedDivineCore",
            Mesh = new SphereMesh { Radius = .91f, Height = 1.82f, RadialSegments = 24, Rings = 12 },
            MaterialOverride = _coreMaterial,
            Scale = new(1, 1, .48f)
        });
        _axle.AddChild(new MeshInstance3D
        {
            Name = "CorePressureLens",
            Position = new(0, 0, .48f),
            Mesh = new SphereMesh { Radius = .23f, Height = .46f, RadialSegments = 16, Rings = 8 },
            MaterialOverride = _coreMaterial,
            Scale = new(1, 1, .6f)
        });
        var core = new EnvironmentBuilder(_axle, "CoreSpindleBands");
        core.Torus(.9f, 1.03f, new(0, 0, .03f), "a68053", new(90, 0, 0), surface: SurfaceKind.Metal);
        core.Torus(.69f, .74f, new(0, 0, .3f), "665647", new(90, 0, 0), surface: SurfaceKind.Metal);
        core.Torus(.22f, .28f, new(0, 0, .54f), "d7ba86", new(90, 0, 0), surface: SurfaceKind.Metal);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.Tau / 8;
            Vector3 radial = new(Mathf.Sin(angle), Mathf.Cos(angle), 0);
            core.Branch(radial * .91f + Vector3.Back * .13f, radial * .28f + Vector3.Back * .5f, .043f, .028f, "665647", SurfaceKind.Metal);
            core.Box(new(.12f, .25f, .19f), radial * .91f + Vector3.Back * .15f, "a68053", new(0, 0, -i * 45), surface: SurfaceKind.Metal);
            core.Cylinder(.043f, .033f, .055f, radial * .79f + Vector3.Back * .35f, "d7ba86", new(90, 0, 0), surface: SurfaceKind.Metal);
        }
        core.Flush();

        var armorMesh = ArmorPlate(new(.86f, .86f), .24f, .12f);
        var insetMesh = ArmorPlate(new(.66f, .66f), .065f, .09f);
        var armorMaterial = SurfaceMaterials.Create("4f4740", SurfaceKind.Metal);
        var insetMaterial = SurfaceMaterials.Create("74604a", SurfaceKind.Metal);
        for (int i = 0; i < _shutters.Length; i++)
        {
            _shutters[i] = new Node3D { Name = "CoreArmorShutter" + i };
            AddChild(_shutters[i]);
            _shutters[i].AddChild(new MeshInstance3D { Name = "ChamferedArmorShell", Mesh = armorMesh, MaterialOverride = armorMaterial });
            _shutters[i].AddChild(new MeshInstance3D { Name = "InsetArmorPanel", Position = new(0, 0, .136f), Mesh = insetMesh, MaterialOverride = insetMaterial });
            var shutter = new EnvironmentBuilder(_shutters[i], "ArmorFasteners");
            shutter.Box(new(.065f, .54f, .042f), new(0, 0, .187f), "a68053", surface: SurfaceKind.Metal);
            for (int side = -1; side <= 1; side += 2)
            {
                shutter.Branch(new(side * .23f, -.22f, .185f), new(side * .23f, .22f, .185f), .024f, .024f, "a68053", SurfaceKind.Metal);
                for (int row = -1; row <= 1; row += 2)
                    shutter.Cylinder(.035f, .024f, .042f, new(side * .24f, row * .24f, .183f), "a68053", new(90, 0, 0), surface: SurfaceKind.Metal);
                shutter.Box(new(.105f, .025f, .012f), new(side * .12f, 0, .174f), "302c2b", surface: SurfaceKind.Metal);
            }
            shutter.Flush();
        }
        var ventMesh = ArmorPlate(new(.67f, .12f), .055f, .025f);
        var housingMesh = ArmorPlate(new(.94f, .94f), .34f, .12f);
        var housingMaterial = SurfaceMaterials.Create("302c2b", SurfaceKind.Metal);
        for (int i = 0; i < _vents.Length; i++)
        {
            Vector3 position = i switch { 0 => new(-2.28f, 3.2f, .9f), 1 => new(2.28f, 3.2f, .9f), 2 => new(0, 1.16f, .9f), _ => new(0, 5.13f, .9f) };
            AddChild(new MeshInstance3D { Name = "ChamferedVentHousing" + i, Mesh = housingMesh, MaterialOverride = housingMaterial, Position = position });
            var housing = new EnvironmentBuilder(this, "VentHousing" + i);
            housing.Box(new(1.04f, .12f, .43f), position + Vector3.Up * .49f, "9c7852", surface: SurfaceKind.Metal);
            housing.Box(new(1.04f, .12f, .43f), position + Vector3.Down * .49f, "65564a", surface: SurfaceKind.Metal);
            for (int side = -1; side <= 1; side += 2)
                housing.Cylinder(.045f, .035f, .055f, position + new Vector3(side * .43f, .49f, .25f), "9c7852", new(90, 0, 0), surface: SurfaceKind.Metal);
            housing.Flush();
            _vents[i] = new Node3D { Name = "VentLouvers" + i, Position = position + Vector3.Back * .21f };
            AddChild(_vents[i]);
            _ventMaterials[i] = HeatedMetal("b9754b", "ee8d47", .13f);
            for (int bar = -2; bar <= 2; bar++)
                _vents[i].AddChild(new MeshInstance3D { Name = "SlottedVent" + (bar + 2), Mesh = ventMesh, MaterialOverride = _ventMaterials[i], Position = new(0, bar * .145f, 0) });
        }
        for (int i = 0; i < _pistons.Length; i++)
        {
            _pistons[i] = new Node3D { Name = "PressurePiston" + i };
            AddChild(_pistons[i]);
            var piston = new EnvironmentBuilder(_pistons[i], "PressureCylinder");
            piston.Cylinder(.12f, .12f, 1.2f, new(0, .6f, 0), "b3a289", surface: SurfaceKind.Metal);
            piston.Cylinder(.3f, .3f, .43f, new(0, .3f, 0), "74604a", surface: SurfaceKind.Metal);
            piston.Torus(.26f, .33f, new(0, .52f, 0), "a68053", surface: SurfaceKind.Metal);
            piston.Cylinder(.33f, .27f, .09f, new(0, .08f, 0), "a68053", surface: SurfaceKind.Metal);
            piston.Cylinder(.16f, .16f, .055f, new(0, 1.19f, 0), "b3a289", surface: SurfaceKind.Metal);
            piston.Flush();
        }
        var emberMesh = new BoxMesh { Size = new(.08f, .1f, .045f) };
        var emberMaterial = new StandardMaterial3D { AlbedoColor = HotCore, EmissionEnabled = true, Emission = new("ed9b59"), EmissionEnergyMultiplier = .75f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        for (int i = 0; i < _embers.Length; i++)
        {
            _embers[i] = new MeshInstance3D { Name = "CoolingSpindleEmber" + i, Mesh = emberMesh, MaterialOverride = emberMaterial, Visible = false };
            AddChild(_embers[i]);
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
