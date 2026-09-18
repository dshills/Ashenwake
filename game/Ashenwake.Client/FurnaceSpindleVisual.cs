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
    private static readonly Color HotCore = new("ffd08b");
    private static readonly Color CoolingCore = new("796d61");
    private readonly Node3D _axle = new() { Name = "FurnaceCoreAxle", Position = new(0, 3.2f, .65f) };
    private readonly Node3D[] _shutters = new Node3D[4];
    private readonly Node3D[] _vents = new Node3D[4];
    private readonly Node3D[] _pistons = new Node3D[2];
    private readonly MeshInstance3D[] _embers = new MeshInstance3D[12];
    private readonly StandardMaterial3D[] _ventMaterials = new StandardMaterial3D[4];
    private readonly StandardMaterial3D _coreMaterial = new()
    {
        AlbedoColor = HotCore,
        Roughness = .6f,
        EmissionEnabled = true,
        Emission = new("ffb568"),
        EmissionEnergyMultiplier = .95f
    };
    private bool _initialized;
    private bool _reducedEffects;
    private float _clock;
    private float _shutdownAge = ShutdownDuration;
    private float _shutdownStartAngle;

    public bool Guarded { get; private set; }
    public bool Defeated { get; private set; }
    public string VentOrientation { get; private set; } = "None";
    public bool VentWarning => VentOrientation != "None";
    public bool IsTransitioning => Defeated && _shutdownAge < ShutdownDuration;
    public float VictoryProgress => Defeated ? Mathf.SmoothStep(0, 1, _shutdownAge / ShutdownDuration) : 0;
    public int ArticulatedPartCount => 1 + _shutters.Length + _vents.Length + _pistons.Length;
    public int TransientCapacity => _embers.Length;
    public int ActiveTransientCount => _embers.Count(ember => ember.Visible);

    public static FurnaceSpindleVisual Create(float halfWidth, float halfDepth, CombatView combat, bool defeated = false)
    {
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
        }
        else if (!_initialized || !defeated || _reducedEffects) _shutdownAge = ShutdownDuration;
        Guarded = guarded;
        Defeated = defeated;
        VentOrientation = orientation;
        _initialized = true;
        ApplyPose();
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        if (!_initialized) return;
        _reducedEffects = reducedEffects;
        if (reducedEffects)
        {
            // Consume the shutdown even while paused; toggling effects back on never replays it.
            _shutdownAge = ShutdownDuration;
            ApplyPose();
            return;
        }
        if (paused) return;
        float step = (float)Math.Clamp(delta, 0, .1);
        if (!Defeated) _clock += step;
        _shutdownAge = Math.Min(ShutdownDuration, _shutdownAge + step);
        ApplyPose();
    }

    private void ApplyPose()
    {
        float victory = VictoryProgress;
        float breath = !Defeated && !_reducedEffects ? Mathf.Sin(_clock * 1.8f) : 0;
        _axle.Position = new(0, 3.2f - victory * .42f, .65f);
        float rotation = Defeated ? Mathf.RadToDeg(Mathf.LerpAngle(_shutdownStartAngle, Mathf.DegToRad(-24), victory)) : _reducedEffects ? 0 : _clock * 9 % 360;
        _axle.RotationDegrees = new(0, 0, rotation);
        _coreMaterial.AlbedoColor = HotCore.Lerp(CoolingCore, victory);
        _coreMaterial.EmissionEnergyMultiplier = Mathf.Lerp((Guarded ? .55f : 1.1f) + breath * .07f, .04f, victory);
        for (int i = 0; i < _shutters.Length; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            float up = i < 2 ? -1 : 1;
            float opening = Guarded ? 0 : 1;
            _shutters[i].Position = new(side * (.43f + opening * .57f), 3.2f + up * (.43f + opening * .25f) - victory * .2f, 1.37f);
            _shutters[i].RotationDegrees = new(0, side * (-opening * 24 - victory * 8), -side * up * victory * 8);
        }
        for (int i = 0; i < _vents.Length; i++)
        {
            bool active = !Defeated && (i < 2 ? VentOrientation == "Horizontal" : VentOrientation == "Vertical");
            _vents[i].RotationDegrees = new(active ? -38 : Defeated ? -16 * victory : 0, 0, i < 2 ? 90 : 0);
            _ventMaterials[i].EmissionEnergyMultiplier = active ? 1.25f + breath * .08f : Mathf.Lerp(.13f, .015f, victory);
        }
        for (int i = 0; i < _pistons.Length; i++)
        {
            float side = i == 0 ? -1 : 1;
            _pistons[i].Position = new(side * 2.65f, 1.25f + (Guarded ? .28f : 0) - victory * .2f, -.05f);
        }
        bool cooling = Defeated && _shutdownAge < ShutdownDuration && !_reducedEffects;
        for (int i = 0; i < _embers.Length; i++)
        {
            var ember = _embers[i];
            float age = _shutdownAge - i * .045f;
            ember.Visible = cooling && age is >= 0 and < 1.9f;
            if (!ember.Visible) continue;
            float life = age / 1.9f;
            float angle = i * 2.399f;
            ember.Position = new(Mathf.Sin(angle) * (.4f + life * .8f), 3.1f + Mathf.Cos(angle) * .48f + life * 1.4f, 1.6f + Mathf.Sin(angle) * .12f);
            ember.Scale = Vector3.One * (1 - life) * .8f;
            ember.RotationDegrees = new(0, 0, i * 31 + life * 60);
        }
    }

    private void Build()
    {
        var frame = new EnvironmentBuilder(this, "SpindleCastIronFrame");
        frame.Box(new(6.3f, .38f, 2.35f), new(0, .19f, -.1f), "302c2b");
        frame.Box(new(5.85f, .12f, 2.16f), new(0, .44f, -.1f), "65564a");
        frame.Cylinder(1.46f, 1.46f, .48f, new(0, 3.2f, .13f), "2f3031", new(90, 0, 0));
        frame.Torus(1.35f, 1.54f, new(0, 3.2f, .48f), "9c7852", new(90, 0, 0));
        frame.Torus(1.58f, 1.72f, new(0, 3.2f, .11f), "464343", new(90, 0, 0));
        for (int side = -1; side <= 1; side += 2)
        {
            frame.Box(new(.6f, 4.55f, .72f), new(side * 1.85f, 2.75f, -.48f), "464343");
            frame.Box(new(.82f, .2f, .92f), new(side * 1.85f, .7f, -.48f), "9c7852");
            frame.Box(new(.82f, .25f, .92f), new(side * 1.85f, 4.7f, -.48f), "65564a");
            frame.Beam(new(side * 1.85f, 4.95f, -.48f), new(side * .57f, 5.65f, -.48f), .37f, "464343");
            frame.Cylinder(.24f, .24f, 3.8f, new(side * 2.65f, 2.4f, -.28f), "65564a");
            frame.Cylinder(.34f, .34f, .16f, new(side * 2.65f, 4.27f, -.28f), "a68053");
            frame.Beam(new(side * 2.65f, 4.35f, -.28f), new(side * 1.84f, 4.35f, -.28f), .27f, "65564a");
            frame.Box(new(.4f, 1.05f, .55f), new(side * 2.65f, 1.05f, -.32f), "302c2b");
            for (int rivet = 0; rivet < 5; rivet++)
                frame.Cylinder(.067f, .067f, .065f, new(side * 1.85f, 1.15f + rivet * .74f, -.075f), "ad9169", new(90, 0, 0));
        }
        frame.Box(new(1.45f, .42f, .85f), new(0, 5.62f, -.48f), "65564a");
        frame.Cylinder(.29f, .23f, 1.03f, new(0, 6.27f, -.48f), "464343");
        frame.Cylinder(.38f, .38f, .16f, new(0, 6.78f, -.48f), "a68053");
        frame.Flush();

        AddChild(_axle);
        _axle.AddChild(new MeshInstance3D
        {
            Name = "ExposedDivineCore",
            Mesh = new SphereMesh { Radius = .91f, Height = 1.82f, RadialSegments = 12, Rings = 6 },
            MaterialOverride = _coreMaterial,
            Scale = new(1, 1, .48f)
        });
        var core = new EnvironmentBuilder(_axle, "CoreSpindleBands");
        core.Torus(.9f, 1.03f, new(0, 0, .03f), "a68053", new(90, 0, 0));
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.Tau / 6;
            core.Box(new(.11f, .35f, .13f), new(Mathf.Sin(angle) * .88f, Mathf.Cos(angle) * .88f, .14f), "665647", new(0, 0, -i * 60));
        }
        core.Cylinder(.19f, .14f, .15f, new(0, 0, .5f), "d7ba86", new(90, 0, 0));
        core.Flush();

        for (int i = 0; i < _shutters.Length; i++)
        {
            _shutters[i] = new Node3D { Name = "CoreArmorShutter" + i };
            AddChild(_shutters[i]);
            var shutter = new EnvironmentBuilder(_shutters[i], "ArmoredPlate");
            shutter.Box(new(.83f, .83f, .23f), Vector3.Zero, "4f4740");
            shutter.Box(new(.63f, .63f, .035f), new(0, 0, .137f), "74604a");
            shutter.Box(new(.06f, .64f, .07f), new(0, 0, .17f), "a68053");
            shutter.Box(new(.64f, .06f, .07f), new(0, 0, .17f), "a68053");
            shutter.Flush();
        }
        var ventMesh = new BoxMesh { Size = new(.67f, .12f, .045f) };
        for (int i = 0; i < _vents.Length; i++)
        {
            Vector3 position = i switch { 0 => new(-2.28f, 3.2f, .9f), 1 => new(2.28f, 3.2f, .9f), 2 => new(0, 1.16f, .9f), _ => new(0, 5.13f, .9f) };
            var housing = new EnvironmentBuilder(this, "VentHousing" + i);
            housing.Box(new(.92f, .92f, .34f), position, "302c2b");
            housing.Box(new(1.04f, .12f, .43f), position + Vector3.Up * .49f, "9c7852");
            housing.Box(new(1.04f, .12f, .43f), position + Vector3.Down * .49f, "65564a");
            housing.Flush();
            _vents[i] = new Node3D { Name = "VentLouvers" + i, Position = position + Vector3.Back * .21f };
            AddChild(_vents[i]);
            _ventMaterials[i] = new StandardMaterial3D { AlbedoColor = new("b9754b"), Roughness = .75f, EmissionEnabled = true, Emission = new("ee8d47"), EmissionEnergyMultiplier = .13f };
            for (int bar = -2; bar <= 2; bar++)
                _vents[i].AddChild(new MeshInstance3D { Name = "SlottedVent" + (bar + 2), Mesh = ventMesh, MaterialOverride = _ventMaterials[i], Position = new(0, bar * .145f, 0) });
        }
        for (int i = 0; i < _pistons.Length; i++)
        {
            _pistons[i] = new Node3D { Name = "PressurePiston" + i };
            AddChild(_pistons[i]);
            var piston = new EnvironmentBuilder(_pistons[i], "PressureCylinder");
            piston.Cylinder(.12f, .12f, 1.2f, new(0, .6f, 0), "b3a289");
            piston.Cylinder(.3f, .3f, .43f, new(0, .3f, 0), "74604a");
            piston.Torus(.26f, .33f, new(0, .52f, 0), "a68053");
            piston.Flush();
        }
        var emberMesh = new BoxMesh { Size = new(.08f, .1f, .045f) };
        var emberMaterial = new StandardMaterial3D { AlbedoColor = HotCore, EmissionEnabled = true, Emission = new("ed9b59"), EmissionEnergyMultiplier = .75f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        for (int i = 0; i < _embers.Length; i++)
        {
            _embers[i] = new MeshInstance3D { Name = "CoolingSpindleEmber" + i, Mesh = emberMesh, MaterialOverride = emberMaterial, Visible = false };
            AddChild(_embers[i]);
        }
    }
}
