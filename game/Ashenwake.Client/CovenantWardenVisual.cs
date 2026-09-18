using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>
/// A monumental covenant tablet beyond the court's north wall. Its shields follow the Warden's
/// defense projection and its seals follow live, boss-owned warnings. All geometry is cosmetic;
/// no animation predicts an attack, changes collision, or supplies combat timing.
/// </summary>
public partial class CovenantWardenVisual : Node3D
{
    private const float ReleaseDuration = 3.2f;
    private static readonly Color LivingLaw = new("bed3ce");
    private static readonly Color BrokenLaw = new("767e80");
    private static readonly Color FaultWarning = new("f2d49a");
    private readonly Node3D[] _tabletHalves = new Node3D[2];
    private readonly Node3D[] _shields = new Node3D[4];
    private readonly Node3D[] _seals = new Node3D[6];
    private readonly Node3D[] _faultPointers = new Node3D[2];
    private readonly MeshInstance3D[] _shards = new MeshInstance3D[16];
    private readonly StandardMaterial3D[] _faultMaterials = new StandardMaterial3D[2];
    private readonly Node3D _oathHalo = new() { Name = "AnnouncedOathMark" };
    private readonly StandardMaterial3D _lawMaterial = new()
    {
        AlbedoColor = LivingLaw,
        Roughness = .8f,
        EmissionEnabled = true,
        Emission = LivingLaw,
        EmissionEnergyMultiplier = .65f
    };
    private readonly StandardMaterial3D _sealMaterial = new()
    {
        AlbedoColor = new("c6b47e"),
        Roughness = .75f,
        EmissionEnabled = true,
        Emission = new("f1d59a"),
        EmissionEnergyMultiplier = .32f
    };
    private bool _initialized;
    private bool _reducedEffects;
    private float _clock;
    private float _releaseAge = ReleaseDuration;
    private float _releaseStartOpening;

    public bool Guarded { get; private set; }
    public bool Defeated { get; private set; }
    public bool IsTransitioning => Defeated && _releaseAge < ReleaseDuration;
    public float VictoryProgress => Defeated ? Mathf.SmoothStep(0, 1, _releaseAge / ReleaseDuration) : 0;
    public string FaultLane { get; private set; } = "None";
    public bool OathMarkWarning { get; private set; }
    public int ArticulatedPartCount => _tabletHalves.Length + _shields.Length + _seals.Length + _faultPointers.Length;
    public int TransientCapacity => _shards.Length;
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

    public static CovenantWardenVisual Create(float halfWidth, float halfDepth, CombatView combat, bool defeated = false)
    {
        var visual = new CovenantWardenVisual
        {
            Name = "CovenantWardenVisual",
            Position = new(0, 0, -halfDepth - 3.4f),
            Scale = new(Math.Clamp((halfWidth - 1) / 3.5f, .5f, 1), 1, 1)
        };
        visual.Build();
        visual.SetState(combat, defeated);
        return visual;
    }

    public void SetState(CombatView combat, bool defeated = false)
    {
        CombatActorView? boss = null;
        for (int i = 0; i < combat.Actors.Count; i++)
            if (combat.Actors[i].DefinitionId == "boss.covenant_warden") { boss = combat.Actors[i]; break; }
        defeated |= boss is { Health: <= 0 };
        bool guarded = !defeated && boss?.Guarded == true;
        string faultLane = "None";
        bool oathMark = false;
        if (!defeated && boss is not null && combat.CampaignHazards is { } hazards)
            for (int i = 0; i < hazards.Count; i++)
            {
                var hazard = hazards[i];
                if (hazard.SourceId != boss.Id || hazard.RemainingTicks <= 0) continue;
                if (hazard.ContentId == "campaign.oath_mark") oathMark = true;
                if (hazard.ContentId != "campaign.covenant_fault") continue;
                // Negative world Z is north. Read the actual lane, including memory-altered geometry.
                long lane = (long)hazard.Position.Z + hazard.End.Z;
                faultLane = lane < 0 ? "North" : lane > 0 ? "South" : "None";
            }
        if (_initialized && Defeated == defeated && Guarded == guarded && FaultLane == faultLane && OathMarkWarning == oathMark) return;
        if (_initialized && defeated && !Defeated && !_reducedEffects)
        {
            _releaseAge = 0;
            _releaseStartOpening = Guarded ? 0 : 1;
        }
        else if (!_initialized || !defeated || _reducedEffects) _releaseAge = ReleaseDuration;
        Guarded = guarded;
        Defeated = defeated;
        FaultLane = faultLane;
        OathMarkWarning = oathMark;
        _initialized = true;
        ApplyPose();
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        if (!_initialized) return;
        _reducedEffects = reducedEffects;
        if (reducedEffects)
        {
            // Consume the release even while paused. Re-enabling effects cannot replay a victory.
            _releaseAge = ReleaseDuration;
            ApplyPose();
            return;
        }
        if (paused) return;
        float step = (float)Math.Clamp(delta, 0, .1);
        if (!Defeated) _clock = (_clock + step) % (Mathf.Tau * 10);
        _releaseAge = Math.Min(ReleaseDuration, _releaseAge + step);
        ApplyPose();
    }

    private void ApplyPose()
    {
        float victory = VictoryProgress;
        float breath = !Defeated && !_reducedEffects ? Mathf.Sin(_clock * 1.5f) : 0;
        float opening = Defeated ? Mathf.Lerp(_releaseStartOpening, 1, victory) : Guarded ? 0 : 1;
        _lawMaterial.AlbedoColor = LivingLaw.Lerp(BrokenLaw, victory);
        _lawMaterial.EmissionEnergyMultiplier = Mathf.Lerp((Guarded ? .28f : .85f) + breath * .04f, .015f, victory);
        _sealMaterial.EmissionEnergyMultiplier = Mathf.Lerp(OathMarkWarning ? 1.1f + breath * .04f : Guarded ? .5f : .18f, .01f, victory);
        _oathHalo.Visible = OathMarkWarning;
        for (int i = 0; i < _tabletHalves.Length; i++)
        {
            float side = i == 0 ? -1 : 1;
            _tabletHalves[i].Position = new(side * (.73f + victory * .24f), 3.5f - victory * .38f, .08f);
            _tabletHalves[i].RotationDegrees = new(0, side * victory * 7, -side * victory * 7);
        }
        for (int i = 0; i < _shields.Length; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            float tier = i < 2 ? -1 : 1;
            _shields[i].Position = new(side * (.59f + opening * 1.27f), 3.5f + tier * .84f - victory * .4f, .9f);
            _shields[i].RotationDegrees = new(0, side * opening * 28, -side * (opening * 10 + victory * 13));
        }
        for (int i = 0; i < _seals.Length; i++)
        {
            float angle = i * Mathf.Tau / _seals.Length + Mathf.Pi / 6;
            float x = Mathf.Sin(angle);
            float y = Mathf.Cos(angle);
            _seals[i].Position = new(x * (1.52f + victory * .62f), 3.5f + y * 1.94f - victory * 1.05f, 1.25f);
            _seals[i].RotationDegrees = new(victory * 32, 0, -i * 60 + x * victory * 43);
        }
        for (int i = 0; i < _faultPointers.Length; i++)
        {
            bool active = i == 0 ? FaultLane == "North" : FaultLane == "South";
            float side = i == 0 ? 1 : -1;
            _faultPointers[i].Position = new(0, 3.5f + side * (2.2f + (active ? .12f : 0)), 1.12f);
            _faultMaterials[i].EmissionEnergyMultiplier = active ? 1.25f + breath * .035f : Mathf.Lerp(.035f, 0, victory);
            _faultMaterials[i].AlbedoColor = active ? FaultWarning : BrokenLaw;
        }
        bool releasing = Defeated && _releaseAge < ReleaseDuration && !_reducedEffects;
        for (int i = 0; i < _shards.Length; i++)
        {
            var shard = _shards[i];
            float age = _releaseAge - i * .05f;
            shard.Visible = releasing && age is >= 0 and < 1.8f;
            if (!shard.Visible) continue;
            float life = age / 1.8f;
            float angle = i * 2.399f;
            shard.Position = new(Mathf.Sin(angle) * (1.15f + life * .65f), 3.4f + Mathf.Cos(angle) * 1.55f + life * .5f, 1.5f + Mathf.Sin(angle) * .08f);
            shard.RotationDegrees = new(life * 36, 0, i * 47 + life * 55);
            shard.Scale = Vector3.One * (1 - life);
        }
    }

    private void Build()
    {
        var frame = new EnvironmentBuilder(this, "BoneCourtCovenantFrame");
        frame.Box(new(6.8f, .3f, 2.1f), new(0, .15f, -.05f), "46545e");
        frame.Box(new(6.15f, .16f, 1.85f), new(0, .38f, -.05f), "8f9b9c");
        frame.Box(new(3.5f, .23f, 1.25f), new(0, .58f, .05f), "d2cbb7");
        for (int side = -1; side <= 1; side += 2)
        {
            frame.Cylinder(.36f, .26f, 4.6f, new(side * 2.8f, 2.77f, -.25f), "bcbda9");
            frame.Cylinder(.42f, .38f, .25f, new(side * 2.8f, .66f, -.25f), "d7d2bd");
            frame.Cylinder(.36f, .42f, .31f, new(side * 2.8f, 5.12f, -.25f), "e1dac1");
            frame.Beam(new(side * 2.8f, 5.3f, -.25f), new(side * 1.85f, 6.05f, -.25f), .33f, "bcbda9");
            frame.Beam(new(side * 1.85f, 6.05f, -.25f), new(side * .56f, 6.47f, -.25f), .27f, "d7d2bd");
            for (int band = 0; band < 4; band++)
            {
                frame.Torus(.29f, .36f, new(side * 2.8f, 1.25f + band * .99f, -.25f), "978665");
                frame.Box(new(.085f, .39f, .08f), new(side * 2.8f, 1.58f + band * .99f, .028f), "66777c");
            }
            frame.Beam(new(side * 2.48f, 1.04f, -.27f), new(side * 1.65f, 1.6f, -.27f), .19f, "aeb5a9");
        }
        frame.Box(new(1.4f, .22f, .6f), new(0, 6.48f, -.25f), "978665");
        frame.Box(new(.33f, .47f, .37f), new(0, 6.57f, -.02f), "ddd5b9");
        frame.Box(new(.66f, .08f, .1f), new(0, 6.59f, .21f), "c6b47e");
        frame.Flush();

        using var runeMesh = new BoxMesh { Size = new(.065f, .23f, .035f) };
        for (int i = 0; i < _tabletHalves.Length; i++)
        {
            float side = i == 0 ? -1 : 1;
            _tabletHalves[i] = new Node3D { Name = "CrackedCovenantTablet" + i };
            AddChild(_tabletHalves[i]);
            var tablet = new EnvironmentBuilder(_tabletHalves[i], "InscribedLawStone");
            tablet.Box(new(1.34f, 3.55f, .49f), Vector3.Zero, "71838b");
            tablet.Box(new(1.18f, 3.31f, .065f), new(side * .05f, 0, .27f), "3e505d");
            tablet.Box(new(.085f, 3.26f, .075f), new(side * .57f, 0, .32f), "acb8b3");
            // All inscriptions on a half move together and share the live law material.
            // Merge their geometry once while retaining that material's guarded/defeat response.
            using var inscriptions = new SurfaceTool();
            inscriptions.Begin(Mesh.PrimitiveType.Triangles);
            for (int line = 0; line < 7; line++)
            {
                float y = -1.4f + line * .44f;
                float fracture = line % 2 == 0 ? .06f : -.015f;
                tablet.Box(new(.105f, .31f, .16f), new(-side * (.69f + fracture), y, .14f), "bec8bd");
                tablet.Box(new(.73f, .028f, .04f), new(side * .05f, y - .155f, .32f), "8f9b9c");
                for (int glyph = 0; glyph < 3; glyph++)
                {
                    var glyphAt = new Vector3(-.3f + glyph * .29f, y, .32f);
                    float glyphAngle = Mathf.DegToRad((line + glyph + i) % 3 == 0 ? 36 : -13);
                    inscriptions.AppendFrom(runeMesh, 0, new Transform3D(Basis.FromEuler(new(0, 0, glyphAngle)), glyphAt));
                    tablet.Box(new(.16f, .045f, .025f), glyphAt + new Vector3(side * .055f, .015f, .018f), "b4c4bc");
                }
            }
            tablet.Box(new(1.53f, .18f, .6f), new(0, 1.88f, 0), "bec8bd");
            tablet.Box(new(1.45f, .16f, .59f), new(0, -1.88f, 0), "b0b7ad");
            tablet.Flush();
            _tabletHalves[i].AddChild(new MeshInstance3D { Name = "CovenantInscriptions", Mesh = inscriptions.Commit(), MaterialOverride = _lawMaterial });
        }
        for (int i = 0; i < _shields.Length; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            _shields[i] = new Node3D { Name = "WardenOathShield" + i };
            AddChild(_shields[i]);
            var shield = new EnvironmentBuilder(_shields[i], "ArticulatedBoneAegis");
            shield.Box(new(.99f, 1.52f, .19f), Vector3.Zero, "bec0ae");
            shield.Box(new(.76f, 1.29f, .045f), new(0, 0, .117f), "718079");
            shield.Box(new(.12f, 1.35f, .07f), new(0, 0, .15f), "dcd5be");
            shield.Box(new(.8f, .1f, .07f), new(0, .34f, .15f), "c6b47e");
            shield.Beam(new(-side * .34f, -.53f, .15f), new(side * .34f, .1f, .15f), .07f, "a69770");
            shield.Cylinder(.14f, .14f, .09f, new(0, .34f, .2f), "ddd5b9", new(90, 0, 0));
            shield.Flush();
        }
        var sealMesh = new TorusMesh { InnerRadius = .185f, OuterRadius = .225f, Rings = 12, RingSegments = 5 };
        for (int i = 0; i < _seals.Length; i++)
        {
            _seals[i] = new Node3D { Name = "BindingOathSeal" + i };
            AddChild(_seals[i]);
            var seal = new EnvironmentBuilder(_seals[i], "CarvedOathMedallion");
            seal.Cylinder(.25f, .25f, .085f, Vector3.Zero, "364b56", new(90, 0, 0));
            seal.Box(new(.065f, .31f, .045f), new(0, 0, .07f), "dbd3b9");
            seal.Box(new(.29f, .065f, .045f), new(0, .045f, .07f), "c6b47e");
            seal.Beam(new(-.14f, -.06f, .08f), new(0, -.17f, .08f), .035f, "c6b47e");
            seal.Beam(new(0, -.17f, .08f), new(.14f, -.06f, .08f), .035f, "c6b47e");
            seal.Flush();
            _seals[i].AddChild(new MeshInstance3D { Name = "OathSealRing", Mesh = sealMesh, MaterialOverride = _sealMaterial, Position = new(0, 0, .075f), RotationDegrees = new(90, 0, 0) });
        }
        AddChild(_oathHalo);
        var oath = new EnvironmentBuilder(_oathHalo, "AnnouncedCircularOath");
        oath.Torus(.45f, .51f, new(0, 3.5f, 1.34f), "f1d59a", new(90, 0, 0), true);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.Pi / 2;
            oath.Box(new(.09f, .18f, .05f), new(Mathf.Sin(angle) * .55f, 3.5f + Mathf.Cos(angle) * .55f, 1.34f), "f1d59a", new(0, 0, -i * 90), true);
        }
        oath.Flush();
        var pointerMesh = new BoxMesh { Size = new(.54f, .08f, .065f) };
        for (int i = 0; i < _faultPointers.Length; i++)
        {
            float direction = i == 0 ? 1 : -1;
            _faultPointers[i] = new Node3D { Name = i == 0 ? "NorthFaultPointer" : "SouthFaultPointer" };
            AddChild(_faultPointers[i]);
            _faultMaterials[i] = new StandardMaterial3D { AlbedoColor = BrokenLaw, Roughness = .7f, EmissionEnabled = true, Emission = new("f2d49a"), EmissionEnergyMultiplier = .035f };
            for (int side = -1; side <= 1; side += 2)
                _faultPointers[i].AddChild(new MeshInstance3D { Name = "FaultChevron" + side, Mesh = pointerMesh, MaterialOverride = _faultMaterials[i], Position = new(side * .22f, 0, 0), RotationDegrees = new(0, 0, -side * direction * 28) });
        }
        var shardMesh = new BoxMesh { Size = new(.085f, .17f, .045f) };
        var shardMaterial = new StandardMaterial3D { AlbedoColor = new("dedac0"), EmissionEnabled = true, Emission = new("bdd3d1"), EmissionEnergyMultiplier = .55f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        for (int i = 0; i < _shards.Length; i++)
        {
            _shards[i] = new MeshInstance3D { Name = "ReleasedLawGlyph" + i, Mesh = shardMesh, MaterialOverride = shardMaterial, Visible = false };
            AddChild(_shards[i]);
        }
    }
}
