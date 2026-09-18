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
    private readonly StandardMaterial3D _ringMaterial = new()
    {
        AlbedoColor = FirstPhase,
        Roughness = .68f,
        EmissionEnabled = true,
        Emission = FirstPhase,
        EmissionEnergyMultiplier = .28f
    };
    private readonly StandardMaterial3D _heartMaterial = new()
    {
        AlbedoColor = new("49405c"),
        Metallic = .25f,
        Roughness = .35f,
        EmissionEnabled = true,
        Emission = new("76639c"),
        EmissionEnergyMultiplier = .28f
    };
    private bool _initialized;
    private bool _reducedEffects;
    private int _channelMask;
    private float _clock;
    private float _containmentAge = ContainmentDuration;
    private float _initialOpening;

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
        if (!_initialized) return;
        _reducedEffects = reducedEffects;
        if (reducedEffects)
        {
            // Settle immediately, including while paused, so re-enabling motion cannot replay it.
            _containmentAge = ContainmentDuration;
            ApplyPose();
            return;
        }
        if (paused) return;
        float step = (float)Math.Clamp(delta, 0, .1);
        if (!Defeated) _clock = (_clock + step) % (Mathf.Tau * 10);
        _containmentAge = Math.Min(ContainmentDuration, _containmentAge + step);
        ApplyPose();
    }

    private void ApplyPose()
    {
        float victory = VictoryProgress;
        int phase = Math.Clamp(Phase, 1, 3);
        float breath = !Defeated && !_reducedEffects ? Mathf.Sin(_clock * 1.35f) : 0;
        float opening = Defeated ? Mathf.Lerp(_initialOpening, 0, victory) : Shielded ? 0 : 1;
        Color phaseColor = phase == 3 ? ThirdPhase : phase == 2 ? SecondPhase : FirstPhase;
        _ringMaterial.AlbedoColor = phaseColor.Lerp(Contained, victory);
        _ringMaterial.Emission = _ringMaterial.AlbedoColor;
        _ringMaterial.EmissionEnergyMultiplier = Mathf.Lerp(.24f + phase * .07f + breath * .025f, .1f, victory);
        _heartMaterial.EmissionEnergyMultiplier = Mathf.Lerp(.25f + phase * .1f + breath * .045f, .1f, victory);
        _heart.Position = new(0, 3.63f, .5f);
        _heart.RotationDegrees = new(0, 0, Mathf.Lerp((phase - 1) * 18 + breath * 2, 0, victory));
        _heart.Scale = new(Mathf.Lerp(.67f + phase * .12f + breath * .025f, .22f, victory),
            Mathf.Lerp(1.32f + phase * .025f + breath * .035f, .59f, victory), .5f);
        for (int i = 0; i < _rings.Length; i++)
        {
            float side = i == 1 ? -1 : 1;
            _rings[i].Position = new(0, 3.63f, .36f + i * .11f);
            _rings[i].RotationDegrees = new(
                Mathf.Lerp(side * (phase - 1) * (i + 1) * 9, 0, victory),
                Mathf.Lerp(side * (phase - 1) * 11, 0, victory),
                Mathf.Lerp(i * 30 + side * ((phase - 1) * 17 + breath * (i + 1)), i * 30, victory));
            float size = Mathf.Lerp(1 + (phase - 1) * .025f, .87f, victory);
            _rings[i].Scale = Vector3.One * size;
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
            _channelMaterials[i].EmissionEnergyMultiplier = alive ? Mathf.Lerp(.38f + breath * .025f, .12f, victory) : 0;
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

    private void Build()
    {
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
        var ringStone = new StandardMaterial3D { AlbedoColor = new("817b91"), Metallic = .18f, Roughness = .7f };
        for (int i = 0; i < _rings.Length; i++)
        {
            _rings[i] = new Node3D { Name = "BreachOrbitalSeal" + i };
            AddChild(_rings[i]);
            float radius = 1.78f + i * .22f;
            _rings[i].AddChild(new MeshInstance3D
            {
                Name = "OrbitalStoneRing",
                Mesh = new TorusMesh { InnerRadius = radius, OuterRadius = radius + .07f, Rings = 32, RingSegments = 5 },
                MaterialOverride = ringStone,
                RotationDegrees = new(90, 0, 0)
            });
            using var glyphs = new SurfaceTool();
            glyphs.Begin(Mesh.PrimitiveType.Triangles);
            for (int glyph = 0; glyph < 12; glyph++)
            {
                float angle = glyph * Mathf.Tau / 12;
                var position = new Vector3(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, .05f);
                glyphs.AppendFrom(runeMesh, 0, new Transform3D(Basis.FromEuler(new(0, 0, -angle)), position));
            }
            _rings[i].AddChild(new MeshInstance3D { Name = "OrbitingSealInscriptions", Mesh = glyphs.Commit(), MaterialOverride = _ringMaterial });
        }

        AddChild(_heart);
        _heart.AddChild(new MeshInstance3D
        {
            Name = "FacetedVoidHeart",
            Mesh = new SphereMesh { Radius = .72f, Height = 1.44f, RadialSegments = 8, Rings = 4 },
            MaterialOverride = _heartMaterial
        });
        _heart.AddChild(new MeshInstance3D
        {
            Name = "UnhealedCentralFracture",
            Mesh = new BoxMesh { Size = new(.07f, 1.04f, .06f) },
            MaterialOverride = _ringMaterial,
            Position = new(0, 0, .67f),
            RotationDegrees = new(0, 0, 13)
        });
        var shutterFace = new StandardMaterial3D { AlbedoColor = new("898298"), Roughness = .8f };
        var shutterInset = new StandardMaterial3D { AlbedoColor = new("434255"), Roughness = .88f };
        var shutterMesh = new BoxMesh { Size = new(.51f, .95f, .12f) };
        var shutterCore = new BoxMesh { Size = new(.32f, .73f, .055f) };
        var shutterGlyph = new BoxMesh { Size = new(.075f, .45f, .045f) };
        for (int i = 0; i < _shutters.Length; i++)
        {
            _shutters[i] = new Node3D { Name = "ContainmentShutter" + i };
            AddChild(_shutters[i]);
            _shutters[i].AddChild(new MeshInstance3D { Name = "SealFace", Mesh = shutterMesh, MaterialOverride = shutterFace, RotationDegrees = new(0, 0, 12) });
            _shutters[i].AddChild(new MeshInstance3D { Name = "SealInset", Mesh = shutterCore, MaterialOverride = shutterInset, Position = new(0, 0, .09f), RotationDegrees = new(0, 0, 12) });
            _shutters[i].AddChild(new MeshInstance3D { Name = "SealGlyph", Mesh = shutterGlyph, MaterialOverride = _ringMaterial, Position = new(0, 0, .14f), RotationDegrees = new(0, 0, -12) });
        }

        var channelStone = new StandardMaterial3D { AlbedoColor = new("656477"), Roughness = .88f };
        var channelMesh = new TorusMesh { InnerRadius = .38f, OuterRadius = .46f, Rings = 18, RingSegments = 5 };
        var channelDisk = new CylinderMesh { BottomRadius = .36f, TopRadius = .36f, Height = .085f, RadialSegments = 12 };
        var channelGlyph = new BoxMesh { Size = new(.085f, .42f, .05f) };
        for (int i = 0; i < _channels.Length; i++)
        {
            _channels[i] = new Node3D { Name = "BreachChannelSeal" + i };
            AddChild(_channels[i]);
            _channelMaterials[i] = new StandardMaterial3D
            {
                AlbedoColor = ActiveChannel,
                EmissionEnabled = true,
                Emission = ActiveChannel,
                EmissionEnergyMultiplier = .38f,
                Roughness = .65f
            };
            _channels[i].AddChild(new MeshInstance3D { Name = "ChannelStone", Mesh = channelDisk, MaterialOverride = shutterInset, RotationDegrees = new(90, 0, 0) });
            _channels[i].AddChild(new MeshInstance3D { Name = "ChannelRing", Mesh = channelMesh, MaterialOverride = channelStone, RotationDegrees = new(90, 0, 0) });
            _channels[i].AddChild(new MeshInstance3D { Name = "ChannelLivingMark", Mesh = channelGlyph, MaterialOverride = _channelMaterials[i], Position = new(0, 0, .09f) });
            _channels[i].AddChild(new MeshInstance3D { Name = "ChannelCrossbar", Mesh = channelGlyph, MaterialOverride = _channelMaterials[i], Position = new(0, 0, .09f), RotationDegrees = new(0, 0, 90) });
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
        var shardMesh = new BoxMesh { Size = new(.08f, .17f, .045f) };
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
    }
}
