using Godot;

namespace Ashenwake.Client;

/// <summary>
/// A cosmetic, persistent bell rig outside the sanctuary's north wall. All parts are created once;
/// phase changes only move transforms and tint private materials. Core remains the source of phase state.
/// </summary>
public partial class BellSanctuaryVisual : Node3D
{
    private const float SnapDuration = 1.4f;
    private const float DefeatDuration = 3.2f;
    private readonly Node3D _pivot = new() { Name = "BellPivot", Position = new(0, 7.3f, .35f) };
    private readonly Node3D _intactChains = new() { Name = "IntactChains" };
    private readonly Node3D _brokenChains = new() { Name = "BrokenChains" };
    private readonly MeshInstance3D[] _links = new MeshInstance3D[18];
    private readonly MeshInstance3D[] _embers = new MeshInstance3D[10];
    private readonly MeshInstance3D[] _rings = new MeshInstance3D[2];
    private readonly StandardMaterial3D[] _ringMaterials = new StandardMaterial3D[2];
    private readonly StandardMaterial3D _ritualMaterial = new()
    {
        AlbedoColor = new("87cec0"),
        EmissionEnabled = true,
        Emission = new("87cec0"),
        EmissionEnergyMultiplier = 1.3f,
        Roughness = .75f
    };
    private readonly List<MeshInstance3D> _ritualLights = [];
    private bool _initialized;
    private bool _reducedEffects;
    private float _clock;
    private float _snapAge = SnapDuration;
    private float _defeatAge = DefeatDuration;
    public int Phase { get; private set; }
    public bool Defeated { get; private set; }
    public bool IsTransitioning => _snapAge < SnapDuration || _defeatAge < DefeatDuration;
    public int ActiveDebrisCount => _links.Count(link => link.Visible);
    public int DebrisCapacity => _links.Length;
    public int TransientCapacity => _links.Length + _embers.Length + _rings.Length;
    public Transform3D BellPose => _pivot.Transform;

    public static BellSanctuaryVisual Create(float halfWidth, float halfDepth, int phase = 1, bool defeated = false)
    {
        var visual = new BellSanctuaryVisual { Name = "BellSanctuaryVisual", Position = new(0, 0, -halfDepth - 3.3f) };
        visual.Build(halfWidth);
        visual.SetPhase(phase, defeated);
        return visual;
    }

    public void SetPhase(int phase, bool defeated = false)
    {
        phase = Math.Clamp(phase, 1, 3);
        if (_initialized && phase == Phase && defeated == Defeated) return;
        // Loading a save midway through a fight must not replay the chain break or victory effect.
        if (!_initialized)
        {
            Phase = phase; Defeated = defeated; _initialized = true;
            ApplyPose(_reducedEffects);
            return;
        }
        if (phase >= 3 && Phase < 3) _snapAge = 0;
        if (defeated && !Defeated) _defeatAge = 0;
        if (phase < 3) _snapAge = SnapDuration;
        if (!defeated) _defeatAge = DefeatDuration;
        Phase = phase; Defeated = defeated;
        if (_reducedEffects) { _snapAge = SnapDuration; _defeatAge = DefeatDuration; }
        ApplyPose(_reducedEffects);
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        if (!_initialized) return;
        _reducedEffects = reducedEffects;
        if (reducedEffects)
        {
            _snapAge = SnapDuration; _defeatAge = DefeatDuration;
            ApplyPose(true);
        }
        if (paused) return;
        float step = (float)Math.Clamp(delta, 0, .1);
        _clock += step;
        _snapAge = Math.Min(SnapDuration, _snapAge + step);
        _defeatAge = Math.Min(DefeatDuration, _defeatAge + step);
        if (reducedEffects)
        {
            // Preference changes cannot defer a burst for later; consume any outstanding transition.
            _snapAge = SnapDuration; _defeatAge = DefeatDuration;
        }
        ApplyPose(reducedEffects);
    }

    private void ApplyPose(bool reduced)
    {
        float breakProgress = Phase >= 3 ? Mathf.SmoothStep(0, 1, _snapAge / SnapDuration) : 0;
        float defeatProgress = Defeated ? Mathf.SmoothStep(0, 1, _defeatAge / DefeatDuration) : 0;
        float swing = Defeated ? 0 : Phase switch
        {
            2 => Mathf.Sin(_clock * 2.15f) * (reduced ? .25f : 4.5f),
            >= 3 => 0,
            _ => Mathf.Sin(_clock * 1.1f) * (reduced ? .15f : .9f)
        };
        float snapRecoil = Phase >= 3 && !reduced ? Mathf.Sin(_snapAge * 17) * (1 - breakProgress) * 8 : 0;
        float toll = Defeated && !reduced ? Mathf.Sin(_defeatAge * 10) * (1 - defeatProgress) * 10 : 0;
        _pivot.Position = new(.45f * breakProgress, 7.3f - .45f * breakProgress - .25f * defeatProgress, .35f);
        _pivot.RotationDegrees = new(0, 0, swing - 19 * breakProgress - 7 * defeatProgress + snapRecoil + toll);
        _intactChains.Visible = Phase < 3;
        _intactChains.RotationDegrees = new(0, 0, swing * .25f);
        _brokenChains.Visible = Phase >= 3;
        _brokenChains.RotationDegrees = new(0, 0, snapRecoil * .12f);
        foreach (var light in _ritualLights) light.Visible = Phase == 2 && !Defeated;
        _ritualMaterial.EmissionEnergyMultiplier = reduced ? .8f : 1.25f + Mathf.Sin(_clock * 3) * .25f;
        UpdateLinks(reduced);
        UpdateDefeat(reduced);
    }

    private void UpdateLinks(bool reduced)
    {
        bool falling = Phase >= 3 && _snapAge < SnapDuration && !reduced;
        for (int i = 0; i < _links.Length; i++)
        {
            var link = _links[i]; link.Visible = falling;
            if (!falling) continue;
            float t = _snapAge;
            float side = i % 2 == 0 ? -1 : 1;
            float startX = side * (1.1f + i % 5 * .28f);
            float startY = 4.7f + i % 6 * .32f;
            float height = Math.Max(.17f, startY + (i % 3 - 1) * t - 5.8f * t * t);
            link.Position = new(startX + side * t * .3f, height, .55f + i % 4 * .09f);
            link.Rotation = new(t * (3 + i % 3), i * .3f + t * 2, t * side * 4);
        }
    }

    private void UpdateDefeat(bool reduced)
    {
        bool playing = Defeated && _defeatAge < DefeatDuration && !reduced;
        for (int i = 0; i < _rings.Length; i++)
        {
            float age = _defeatAge - i * .55f;
            bool visible = playing && age is >= 0 and < 1.2f;
            _rings[i].Visible = visible;
            if (!visible) continue;
            float life = age / 1.2f;
            _rings[i].Scale = Vector3.One * (1 + life * .42f);
            _ringMaterials[i].AlbedoColor = new Color("dce3ba") { A = (1 - life) * .5f };
        }
        for (int i = 0; i < _embers.Length; i++)
        {
            var ember = _embers[i]; ember.Visible = playing;
            if (!playing) continue;
            float life = (_defeatAge * .6f + i * .1f) % 1;
            ember.Position = new(Mathf.Sin(i * 2.4f) * (1.25f - life * .5f), 2.2f + life * 3, .8f + Mathf.Cos(i * 1.7f) * .3f);
            ember.Scale = Vector3.One * Mathf.Sin(life * Mathf.Pi) * Math.Min(1, (DefeatDuration - _defeatAge) * 2);
        }
    }

    private void Build(float halfWidth)
    {
        AddChild(_pivot); AddChild(_intactChains); AddChild(_brokenChains);
        var bell = new EnvironmentBuilder(_pivot, "BronzeBell");
        Vector3 center = new(0, -2.25f, 0);
        bell.Cylinder(1.48f, .64f, 1.83f, center, "ad8b57");
        bell.Cylinder(.65f, .36f, .32f, center + Vector3.Up * 1.06f, "dec895");
        bell.Cylinder(1.64f, 1.48f, .23f, center + Vector3.Down * 1.02f, "ad8b57");
        bell.Cylinder(1.43f, 1.43f, .035f, center + Vector3.Down * 1.155f, "33474d");
        bell.Torus(1.42f, 1.65f, center + Vector3.Down * 1.155f, "dec895");
        bell.Torus(.6f, .71f, center + Vector3.Up * .8f, "dec895");
        bell.Torus(.99f, 1.07f, center + Vector3.Down * .08f, "dec895");
        bell.Cylinder(.11f, .12f, .92f, center + Vector3.Down * 1.36f, "535650");
        bell.Cylinder(.21f, .16f, .32f, center + Vector3.Down * 1.82f, "ad8b57");
        for (int i = -2; i <= 2; i++)
        {
            float angle = i * .45f;
            bell.Box(new(.11f, .4f, .055f), center + new Vector3(Mathf.Sin(angle) * 1.05f, -.24f, Mathf.Cos(angle) * 1.05f), "dec895", new(0, Mathf.RadToDeg(angle), 0));
        }
        bell.Flush();
        var chains = new EnvironmentBuilder(_intactChains, "Suspension");
        Chain(chains, new(-.42f, 7.3f, .35f), new(-.42f, 6.15f, .35f));
        Chain(chains, new(.42f, 7.3f, .35f), new(.42f, 6.15f, .35f));
        Chain(chains, new(-2.75f, 7.3f, .2f), new(-1.05f, 4.62f, .8f));
        Chain(chains, new(2.75f, 7.3f, .2f), new(1.05f, 4.62f, .8f));
        chains.Flush();
        var broken = new EnvironmentBuilder(_brokenChains, "SnappedSuspension");
        Chain(broken, new(-.42f, 7.2f, .35f), new(-.7f, 6.48f, .35f));
        Chain(broken, new(.65f, 7.2f, .35f), new(.16f, 6.22f, .35f));
        Chain(broken, new(-2.6f, 5.25f, .45f), new(-2.8f, 2.75f, .45f));
        broken.Flush();
        var linkMesh = new TorusMesh { InnerRadius = .075f, OuterRadius = .126f, Rings = 10, RingSegments = 5 };
        var iron = new StandardMaterial3D { AlbedoColor = new("535650"), Metallic = .55f, Roughness = .7f };
        for (int i = 0; i < _links.Length; i++)
        {
            _links[i] = new MeshInstance3D { Name = "FallingLink" + i, Mesh = linkMesh, MaterialOverride = iron, Visible = false };
            AddChild(_links[i]);
        }
        var ritualMesh = new TorusMesh { InnerRadius = .36f, OuterRadius = .43f, Rings = 16, RingSegments = 6 };
        foreach (float side in new[] { -1f, 1f })
        {
            var light = new MeshInstance3D { Name = side < 0 ? "RitualLeft" : "RitualRight", Mesh = ritualMesh, MaterialOverride = _ritualMaterial, Position = new(side * (halfWidth - 1.2f), .435f, 1.95f), Visible = false };
            _ritualLights.Add(light); AddChild(light);
        }
        var ringMesh = new TorusMesh { InnerRadius = 1.68f, OuterRadius = 1.72f, Rings = 32, RingSegments = 4 };
        for (int i = 0; i < _rings.Length; i++)
        {
            _ringMaterials[i] = new StandardMaterial3D
            {
                AlbedoColor = new Color("dce3ba") { A = .5f },
                EmissionEnabled = true,
                Emission = new("dce3ba"),
                EmissionEnergyMultiplier = .7f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            _rings[i] = new MeshInstance3D { Name = "LastToll" + i, Mesh = ringMesh, MaterialOverride = _ringMaterials[i], Position = new(.45f, 4.6f, 1), RotationDegrees = new(90, 0, 0), Visible = false };
            AddChild(_rings[i]);
        }
        var emberMesh = new BoxMesh { Size = new(.065f, .13f, .05f) };
        var emberMaterial = new StandardMaterial3D { AlbedoColor = new("edc681"), EmissionEnabled = true, Emission = new("edc681"), EmissionEnergyMultiplier = 1.5f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        for (int i = 0; i < _embers.Length; i++)
        {
            _embers[i] = new MeshInstance3D { Name = "SettlingEmber" + i, Mesh = emberMesh, MaterialOverride = emberMaterial, Visible = false };
            AddChild(_embers[i]);
        }
    }

    private static void Chain(EnvironmentBuilder art, Vector3 from, Vector3 to)
    {
        int count = Math.Max(2, Mathf.CeilToInt(from.DistanceTo(to) / .24f));
        for (int i = 0; i <= count; i++)
            art.Torus(.075f, .126f, from.Lerp(to, i / (float)count), "535650", i % 2 == 0 ? new(90, 0, 0) : new(0, 0, 90));
    }
}
