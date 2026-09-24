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
    private const float ShieldReactionDuration = .72f;
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
    private readonly StandardMaterial3D _lawMaterial = LuminousSurface("bed3ce", "bed3ce", SurfaceKind.Stone, .65f);
    private readonly StandardMaterial3D _sealMaterial = LuminousSurface("c6b47e", "f1d59a", SurfaceKind.Metal, .32f);
    private readonly OmniLight3D _lawLight = new()
    {
        Name = "LawGlowLight",
        Position = new(0, 3.5f, 1.5f),
        LightColor = new("c5d8d2"),
        ShadowEnabled = false,
        LightSpecular = .35f,
        OmniAttenuation = 1.5f
    };
    private readonly List<(MeshInstance3D Node, Mesh Mesh, Material Material)> _renderParts = [];
    private bool _initialized;
    private bool _released;
    private bool _reducedEffects;
    private double _clock;
    private float _releaseAge = ReleaseDuration;
    private float _shieldReactionAge = ShieldReactionDuration;
    private readonly Vector3[] _releaseTabletPositions = new Vector3[2], _releaseTabletRotations = new Vector3[2];
    private readonly Vector3[] _releaseShieldPositions = new Vector3[4], _releaseShieldRotations = new Vector3[4];
    private readonly Vector3[] _releaseSealPositions = new Vector3[6], _releaseSealRotations = new Vector3[6];
    private float _releaseStartLawEmission, _releaseStartSealEmission, _releaseStartLightEnergy, _releaseStartLightRange;

    public bool Guarded { get; private set; }
    public bool Defeated { get; private set; }
    public bool IsTransitioning => Defeated && _releaseAge < ReleaseDuration;
    public float VictoryProgress => Defeated ? Mathf.SmoothStep(0, 1, _releaseAge / ReleaseDuration) : 0;
    public string FaultLane { get; private set; } = "None";
    public bool OathMarkWarning { get; private set; }
    public int ArticulatedPartCount => _tabletHalves.Length + _shields.Length + _seals.Length + _faultPointers.Length;
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

    public static CovenantWardenVisual Create(float halfWidth, float halfDepth, CombatView combat, bool defeated = false)
    {
        if (!float.IsFinite(halfWidth) || halfWidth <= 0) throw new ArgumentOutOfRangeException(nameof(halfWidth));
        if (!float.IsFinite(halfDepth) || halfDepth <= 0) throw new ArgumentOutOfRangeException(nameof(halfDepth));
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
            CaptureRelease(_tabletHalves, _releaseTabletPositions, _releaseTabletRotations);
            CaptureRelease(_shields, _releaseShieldPositions, _releaseShieldRotations);
            CaptureRelease(_seals, _releaseSealPositions, _releaseSealRotations);
            _releaseStartLawEmission = _lawMaterial.EmissionEnergyMultiplier;
            _releaseStartSealEmission = _sealMaterial.EmissionEnergyMultiplier;
            _releaseStartLightEnergy = _lawLight.LightEnergy;
            _releaseStartLightRange = _lawLight.OmniRange;
        }
        else if (!_initialized || !defeated || _reducedEffects) _releaseAge = ReleaseDuration;
        if (_initialized && !Defeated && !defeated && Guarded != guarded && !_reducedEffects) _shieldReactionAge = 0;
        else if (!_initialized || defeated || _reducedEffects) _shieldReactionAge = ShieldReactionDuration;
        Guarded = guarded;
        Defeated = defeated;
        FaultLane = faultLane;
        OathMarkWarning = oathMark;
        _initialized = true;
        ApplyPose();
    }

    public void Animate(double delta, bool paused, bool reducedEffects)
    {
        if (!_initialized || _released) return;
        _reducedEffects = reducedEffects;
        if (reducedEffects)
        {
            // Consume the release even while paused. Re-enabling effects cannot replay a victory.
            _clock = 0;
            _releaseAge = ReleaseDuration;
            _shieldReactionAge = ShieldReactionDuration;
            ApplyPose();
            return;
        }
        if (paused) return;
        float step = double.IsFinite(delta) ? (float)Math.Clamp(delta, 0, .1) : 0;
        if (!Defeated) _clock = (_clock + step) % (Math.Tau / 1.5);
        _releaseAge = Math.Min(ReleaseDuration, _releaseAge + step);
        _shieldReactionAge = Math.Min(ShieldReactionDuration, _shieldReactionAge + step);
        ApplyPose();
    }

    private static void CaptureRelease(Node3D[] nodes, Vector3[] positions, Vector3[] rotations)
    {
        for (int i = 0; i < nodes.Length; i++) { positions[i] = nodes[i].Position; rotations[i] = nodes[i].RotationDegrees; }
    }

    private float ReleaseStage(float start, float duration) => Mathf.SmoothStep(0, 1, Math.Clamp((_releaseAge - start) / duration, 0, 1));

    private float ShieldReaction(int index)
    {
        if (Defeated || _reducedEffects) return 0;
        float progress = Math.Clamp((_shieldReactionAge - index * .07f) / .5f, 0, 1);
        return Mathf.Sin(progress * Mathf.Pi) * (1 - progress);
    }

    private void ApplyPose()
    {
        float victory = VictoryProgress;
        float breath = !Defeated && !_reducedEffects ? (float)Math.Sin(_clock * 1.5) : 0;
        float opening = Guarded ? 0 : 1;
        _lawMaterial.AlbedoColor = LivingLaw.Lerp(BrokenLaw, victory);
        _lawMaterial.Emission = LivingLaw.Lerp(BrokenLaw, victory);
        _lawMaterial.EmissionEnergyMultiplier = Defeated ? Mathf.Lerp(_releaseStartLawEmission, .015f, victory) : (Guarded ? .28f : .85f) + breath * .04f;
        _sealMaterial.EmissionEnergyMultiplier = Defeated ? Mathf.Lerp(_releaseStartSealEmission, .01f, victory) : OathMarkWarning ? 1.1f + breath * .04f : Guarded ? .5f : .18f;
        _lawLight.LightEnergy = Defeated ? Mathf.Lerp(_releaseStartLightEnergy, .04f, victory) : Guarded ? .8f : 1.2f;
        _lawLight.OmniRange = Defeated ? Mathf.Lerp(_releaseStartLightRange, 2.8f, victory) : Guarded ? 4.2f : 5;
        _lawLight.LightColor = new Color("c5d8d2").Lerp(new Color("83908e"), victory);
        _lawLight.Position = new(0, 3.5f - victory * .38f, 1.5f);
        _oathHalo.Visible = OathMarkWarning;
        for (int i = 0; i < _tabletHalves.Length; i++)
        {
            float side = i == 0 ? -1 : 1;
            float release = ReleaseStage(.95f + i * .24f, 1.35f);
            _tabletHalves[i].Position = Defeated ? _releaseTabletPositions[i].Lerp(new(side * .97f, 3.12f, .08f), release) : new(side * .73f, 3.5f, .08f);
            _tabletHalves[i].RotationDegrees = Defeated ? _releaseTabletRotations[i].Lerp(new(4, side * 7, -side * 7), release) : Vector3.Zero;
        }
        for (int i = 0; i < _shields.Length; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            float tier = i < 2 ? -1 : 1;
            float reaction = ShieldReaction(i), release = ReleaseStage(.45f + i * .1f, .85f);
            // Defense opens/closes immediately; staggered recoil only adds depth and a small roll.
            _shields[i].Position = Defeated ? _releaseShieldPositions[i].Lerp(new(side * 1.86f, 3.1f + tier * .84f, .9f), release) :
                new(side * (.59f + opening * 1.27f), 3.5f + tier * .84f, .9f + reaction * (Guarded ? -.1f : .18f));
            _shields[i].RotationDegrees = Defeated ? _releaseShieldRotations[i].Lerp(new(0, side * 28, -side * 23), release) :
                new(0, side * opening * 28, -side * (opening * 10 + reaction * 8));
        }
        for (int i = 0; i < _seals.Length; i++)
        {
            float angle = i * Mathf.Tau / _seals.Length + Mathf.Pi / 6;
            float x = Mathf.Sin(angle);
            float y = Mathf.Cos(angle);
            float release = ReleaseStage(.1f + i * .1f, .65f);
            _seals[i].Position = Defeated ? _releaseSealPositions[i].Lerp(new(x * 2.14f, 2.45f + y * 1.94f, 1.25f), release) : new(x * 1.52f, 3.5f + y * 1.94f, 1.25f);
            _seals[i].RotationDegrees = Defeated ? _releaseSealRotations[i].Lerp(new(32, 0, -i * 60 + x * 43), release) : new(0, 0, -i * 60);
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
            float age = _releaseAge - .12f - i * .055f;
            shard.Visible = releasing && age is >= 0 and < 1.8f;
            if (!shard.Visible) { shard.Transform = Transform3D.Identity; continue; }
            float life = age / 1.8f;
            float angle = i * 2.399f;
            shard.Position = new(Mathf.Sin(angle) * (1.15f + life * .65f), 3.4f + Mathf.Cos(angle) * 1.55f + life * .5f, 1.5f + Mathf.Sin(angle) * .08f);
            shard.RotationDegrees = new(life * 36, 0, i * 47 + life * 55);
            shard.Scale = Vector3.One * (1 - life);
        }
    }

    private static StandardMaterial3D LuminousSurface(string color, string glow, SurfaceKind kind, float energy)
    {
        var material = SurfaceMaterials.Create(color, kind);
        material.Roughness = .78f; material.EmissionEnabled = true;
        material.Emission = new(glow); material.EmissionEnergyMultiplier = energy;
        return material;
    }

    public override void _Notification(int what)
    {
        if (what != NotificationPredelete || _released) return;
        _released = true;
        var meshes = _renderParts.Select(part => part.Mesh).DistinctBy(mesh => mesh.GetInstanceId()).ToArray();
        var materials = _renderParts.Select(part => part.Material).Append(_lawMaterial).Append(_sealMaterial)
            .DistinctBy(material => material.GetInstanceId()).ToArray();
        foreach (var part in _renderParts)
            if (GodotObject.IsInstanceValid(part.Node)) { part.Node.Mesh = null; part.Node.MaterialOverride = null; }
        foreach (var mesh in meshes) mesh.Dispose();
        foreach (var material in materials) material.Dispose();
        // This rig owns its mesh/material resources. Shared factory grain textures remain alive.
        _renderParts.Clear();
    }

    private static ArrayMesh CarvedSlab(Vector2[] outline, float depth, float inset = .065f)
    {
        // A closed, chamfered extrusion preserves the authored chips rather than covering
        // them with rectangular trim. Normalize winding so mirrored tablet halves agree.
        float area = 0;
        for (int i = 0; i < outline.Length; i++) area += outline[i].Cross(outline[(i + 1) % outline.Length]);
        if (area > 0) Array.Reverse(outline);
        var triangles = Geometry2D.TriangulatePolygon(outline);
        if (triangles.Length != (outline.Length - 2) * 3) throw new InvalidOperationException("Invalid carved slab outline.");
        using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
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
        AddChild(_lawLight);
        var frame = new EnvironmentBuilder(this, "BoneCourtCovenantFrame");
        frame.Box(new(6.8f, .3f, 2.1f), new(0, .15f, -.05f), "46545e", surface: SurfaceKind.Stone);
        frame.Box(new(6.15f, .16f, 1.85f), new(0, .38f, -.05f), "8f9b9c", surface: SurfaceKind.Stone);
        frame.Box(new(3.5f, .23f, 1.25f), new(0, .58f, .05f), "d2cbb7", surface: SurfaceKind.Bone);
        for (int side = -1; side <= 1; side += 2)
        {
            frame.Branch(new(side * 2.8f, .5f, -.25f), new(side * 2.8f, 5.15f, -.25f), .34f, .24f, "bcbda9", SurfaceKind.Bone);
            frame.Cylinder(.42f, .38f, .25f, new(side * 2.8f, .66f, -.25f), "d7d2bd", surface: SurfaceKind.Bone);
            frame.Cylinder(.3f, .42f, .26f, new(side * 2.8f, 5.02f, -.25f), "d7d2bd", surface: SurfaceKind.Bone);
            frame.Cylinder(.42f, .25f, .24f, new(side * 2.8f, 5.27f, -.25f), "d7d2bd", surface: SurfaceKind.Bone);
            Vector3[] arch = [new(side * 2.8f, 5.3f, -.25f), new(side * 2.48f, 5.77f, -.25f),
                new(side * 1.83f, 6.11f, -.25f), new(side * .56f, 6.47f, -.25f)];
            for (int i = 0; i < arch.Length - 1; i++)
                frame.Branch(arch[i], arch[i + 1], .22f - i * .035f, .18f - i * .035f, "bcbda9", SurfaceKind.Bone);
            for (int band = 0; band < 4; band++)
            {
                frame.Torus(.29f, .36f, new(side * 2.8f, 1.25f + band * .99f, -.25f), "978665", surface: SurfaceKind.Metal);
                frame.Branch(new(side * 2.78f, 1.48f + band * .99f, .035f), new(side * 2.78f, 1.73f + band * .99f, .015f),
                    .026f, .011f, "66777c", SurfaceKind.Stone);
            }
            frame.Branch(new(side * 2.48f, 1.04f, -.27f), new(side * 1.65f, 1.6f, -.27f), .13f, .08f, "bcbda9", SurfaceKind.Bone);
            frame.Branch(new(side * 2.8f, 4.85f, -.03f), new(side * 1.92f, 5.68f, -.03f), .075f, .025f, "d7d2bd", SurfaceKind.Bone);
        }
        frame.Box(new(1.4f, .22f, .6f), new(0, 6.48f, -.25f), "978665", surface: SurfaceKind.Metal);
        frame.Cylinder(.25f, .15f, .47f, new(0, 6.61f, -.02f), "d7d2bd", surface: SurfaceKind.Bone);
        frame.Box(new(.66f, .08f, .1f), new(0, 6.59f, .21f), "978665", surface: SurfaceKind.Metal);
        frame.Flush();

        using var runeMesh = new BoxMesh { Size = new(.058f, .22f, .035f) };
        var tabletMaterial = SurfaceMaterials.Create("71838b", SurfaceKind.Stone);
        var insetMaterial = SurfaceMaterials.Create("3e505d", SurfaceKind.Stone);
        for (int i = 0; i < _tabletHalves.Length; i++)
        {
            float side = i == 0 ? -1 : 1;
            Vector2[] outline = [new(-.56f, 1.82f), new(.4f, 1.82f), new(.61f, 1.62f), new(.65f, 1.22f),
                new(.53f, .91f), new(.7f, .55f), new(.61f, .21f), new(.72f, -.13f), new(.55f, -.46f),
                new(.66f, -.9f), new(.52f, -1.21f), new(.6f, -1.76f), new(-.43f, -1.82f), new(-.65f, -1.51f),
                new(-.69f, .98f), new(-.59f, 1.08f), new(-.67f, 1.56f)];
            for (int point = 0; point < outline.Length; point++) outline[point] = new(-side * outline[point].X, outline[point].Y);
            _tabletHalves[i] = new Node3D { Name = "CrackedCovenantTablet" + i };
            AddChild(_tabletHalves[i]);
            _tabletHalves[i].AddChild(new MeshInstance3D { Name = "ChippedLawTablet", Mesh = CarvedSlab(outline, .49f), MaterialOverride = tabletMaterial });
            _tabletHalves[i].AddChild(new MeshInstance3D
            {
                Name = "RecessedLawFace",
                Position = new(0, 0, .255f),
                Mesh = CarvedSlab(outline.Select(point => point * .875f).ToArray(), .055f),
                MaterialOverride = insetMaterial
            });
            var tablet = new EnvironmentBuilder(_tabletHalves[i], "InscribedLawStone");
            tablet.Branch(new(side * .52f, -1.45f, .302f), new(side * .52f, 1.47f, .302f), .035f, .03f, "acb8b3", SurfaceKind.Stone);
            using var inscriptions = new SurfaceTool(); inscriptions.Begin(Mesh.PrimitiveType.Triangles);
            for (int line = 0; line < 7; line++)
            {
                float y = -1.4f + line * .44f;
                tablet.Box(new(.72f, .022f, .02f), new(side * .03f, y - .155f, .293f), "8f9b9c", surface: SurfaceKind.Stone);
                for (int glyph = 0; glyph < 3; glyph++)
                {
                    var glyphAt = new Vector3(-.3f + glyph * .29f, y, .313f);
                    float glyphAngle = Mathf.DegToRad((line + glyph + i) % 3 == 0 ? 36 : -13);
                    inscriptions.AppendFrom(runeMesh, 0, new Transform3D(Basis.FromEuler(new(0, 0, glyphAngle)), glyphAt));
                    tablet.Box(new(.14f, .032f, .023f), glyphAt + new Vector3(side * .047f, .013f, .012f), "acb8b3", surface: SurfaceKind.Stone);
                }
            }
            // Short cuts on the exposed fracture faces catch light without smoothing the crack.
            for (int chip = 0; chip < 5; chip++)
                tablet.Branch(new(-side * .54f, -1.08f + chip * .51f, .25f), new(-side * .62f, -.99f + chip * .51f, .17f),
                    .028f, .009f, "acb8b3", SurfaceKind.Stone);
            tablet.Flush();
            _tabletHalves[i].AddChild(new MeshInstance3D { Name = "CovenantInscriptions", Mesh = inscriptions.Commit(), MaterialOverride = _lawMaterial });
        }
        Vector2[] shieldOutline = [new(-.32f, .77f), new(.32f, .77f), new(.5f, .55f), new(.43f, -.3f),
            new(0, -.8f), new(-.43f, -.3f), new(-.5f, .55f)];
        var shieldMesh = CarvedSlab(shieldOutline, .22f);
        var shieldInset = CarvedSlab(shieldOutline.Select(point => point * .79f).ToArray(), .065f);
        var shieldMaterial = SurfaceMaterials.Create("bec0ae", SurfaceKind.Bone);
        var shieldFaceMaterial = SurfaceMaterials.Create("718079", SurfaceKind.Metal);
        for (int i = 0; i < _shields.Length; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            _shields[i] = new Node3D { Name = "WardenOathShield" + i };
            AddChild(_shields[i]);
            _shields[i].AddChild(new MeshInstance3D { Name = "LayeredBoneShield", Mesh = shieldMesh, MaterialOverride = shieldMaterial });
            _shields[i].AddChild(new MeshInstance3D { Name = "InsetOathShield", Mesh = shieldInset, MaterialOverride = shieldFaceMaterial, Position = new(0, 0, .13f) });
            var shield = new EnvironmentBuilder(_shields[i], "ArticulatedBoneAegis");
            shield.Branch(new(0, -.57f, .184f), new(0, .58f, .184f), .045f, .057f, "dcd5be", SurfaceKind.Bone);
            shield.Branch(new(-.34f, .32f, .19f), new(.34f, .32f, .19f), .04f, .04f, "c6b47e", SurfaceKind.Metal);
            shield.Branch(new(-side * .29f, -.4f, .18f), new(side * .29f, .08f, .18f), .029f, .021f, "c6b47e", SurfaceKind.Metal);
            shield.Cylinder(.135f, .11f, .08f, new(0, .32f, .224f), "c6b47e", new(90, 0, 0), surface: SurfaceKind.Metal);
            for (int rivet = -1; rivet <= 1; rivet += 2)
                shield.Cylinder(.037f, .025f, .035f, new(rivet * .25f, .52f, .184f), "c6b47e", new(90, 0, 0), surface: SurfaceKind.Metal);
            shield.Flush();
        }
        var sealMesh = new TorusMesh { InnerRadius = .185f, OuterRadius = .225f, Rings = 20, RingSegments = 6 };
        var medallionMesh = CarvedSlab(Enumerable.Range(0, 12).Select(i => new Vector2(Mathf.Sin(i * Mathf.Tau / 12), Mathf.Cos(i * Mathf.Tau / 12)) * .26f).ToArray(), .095f, .12f);
        var medallionMaterial = SurfaceMaterials.Create("364b56", SurfaceKind.Metal);
        for (int i = 0; i < _seals.Length; i++)
        {
            _seals[i] = new Node3D { Name = "BindingOathSeal" + i };
            AddChild(_seals[i]);
            _seals[i].AddChild(new MeshInstance3D { Name = "EngravedSealMedallion", Mesh = medallionMesh, MaterialOverride = medallionMaterial });
            var seal = new EnvironmentBuilder(_seals[i], "CarvedOathMedallion");
            seal.Box(new(.045f, .29f, .018f), new(0, 0, .058f), "c6b47e", surface: SurfaceKind.Metal);
            seal.Box(new(.27f, .045f, .018f), new(0, .04f, .058f), "c6b47e", surface: SurfaceKind.Metal);
            seal.Branch(new(-.13f, -.055f, .06f), new(0, -.16f, .06f), .017f, .012f, "c6b47e", SurfaceKind.Metal);
            seal.Branch(new(0, -.16f, .06f), new(.13f, -.055f, .06f), .012f, .017f, "c6b47e", SurfaceKind.Metal);
            for (int notch = 0; notch < 8; notch++)
            {
                float angle = notch * Mathf.Tau / 8;
                seal.Box(new(.019f, .04f, .015f), new(Mathf.Sin(angle) * .158f, Mathf.Cos(angle) * .158f, .059f),
                    "c6b47e", new(0, 0, -notch * 45), surface: SurfaceKind.Metal);
            }
            seal.Flush();
            _seals[i].AddChild(new MeshInstance3D { Name = "OathSealRing", Mesh = sealMesh, MaterialOverride = _sealMaterial, Position = new(0, 0, .074f), RotationDegrees = new(90, 0, 0) });
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
        var shardMesh = CarvedSlab([new(-.025f, .09f), new(.035f, .06f), new(.061f, -.03f), new(0, -.1f), new(-.023f, -.02f), new(-.051f, .02f)], .035f, .08f);
        var shardMaterial = new StandardMaterial3D { AlbedoColor = new("dedac0"), EmissionEnabled = true, Emission = new("bdd3d1"), EmissionEnergyMultiplier = .55f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        for (int i = 0; i < _shards.Length; i++)
        {
            _shards[i] = new MeshInstance3D { Name = "ReleasedLawGlyph" + i, Mesh = shardMesh, MaterialOverride = shardMaterial, Visible = false };
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
