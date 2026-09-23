using Godot;

namespace Ashenwake.Client;

/// <summary>Bounded cosmetic particles. No physics, gameplay clocks, or simulation random numbers.</summary>
public partial class CombatEffects : Node3D
{
    public const int Maximum = 40;
    private static readonly BoxMesh Shard = new() { Size = Vector3.One };
    private static readonly SphereMesh Puff = new() { Radius = .5f, Height = 1, RadialSegments = 8, Rings = 4 };
    private sealed class Burst
    {
        public required Node3D Root;
        public required MeshInstance3D[] Pieces;
        public required StandardMaterial3D Material;
        public string Cue = "";
        public Color Color;
        public float Age, Duration;
        public long Serial;
        public bool Active;
        public CharacterVisual? Weapon;
        public bool TrailStarted;
        public readonly Vector3[] Trail = new Vector3[6];
    }
    private readonly List<Burst> _pool = [];
    private long _serial;
    public int Count => _pool.Count(b => b.Active);
    public int PoolCount => _pool.Count;

    public void Emit(string cue, Vector3 origin, Vector3 direction, Color color, bool reducedEffects = false)
    {
        if (!reducedEffects) EmitBurst(cue, origin, direction, color);
    }

    private Burst EmitBurst(string cue, Vector3 origin, Vector3 direction, Color color)
    {
        var burst = _pool.FirstOrDefault(b => !b.Active);
        if (burst is null && _pool.Count < Maximum)
        {
            var root = new Node3D(); AddChild(root);
            var material = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
            var pieces = new MeshInstance3D[6];
            for (int i = 0; i < pieces.Length; i++)
            {
                pieces[i] = new MeshInstance3D { Mesh = Shard, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                root.AddChild(pieces[i]);
            }
            burst = new Burst { Root = root, Pieces = pieces, Material = material }; _pool.Add(burst);
        }
        burst ??= _pool.MinBy(b => b.Serial)!;
        burst.Active = true; burst.Serial = ++_serial; burst.Age = 0; burst.Cue = cue; burst.Color = color;
        burst.Weapon = null;
        burst.Duration = cue switch { "loot_legendary" => 1.1f, "loot_godwrought" => 1.6f, "legendary_pyre" => .55f, "legendary_oath" => .42f, "legendary_widow" or "legendary_ready" or "legendary_rotwake" or "legendary_chorus" or "legendary_cinder" or "legendary_verdict" or "legendary_witness" or "legendary_hour" => .5f, "victory" => 1.3f, "phase" => .85f, "death" => .65f, "spell" => .4f, "dodge" or "dust" => .32f, _ => .24f };
        burst.Root.Position = origin;
        burst.Root.Rotation = new(0, direction.LengthSquared() > .001f ? Mathf.Atan2(direction.X, direction.Z) : 0, 0);
        burst.Root.Visible = true;
        foreach (var piece in burst.Pieces) piece.Mesh = cue is "dodge" or "dust" or "death" ? Puff : Shard;
        Pose(burst);
        return burst;
    }

    /// <summary>Cosmetic contact glints follow the rendered joint; damage still resolves in Core.</summary>
    public void EmitWeapon(CharacterVisual weapon, Color color, bool reducedEffects = false)
    {
        if (reducedEffects || !IsInsideTree() || !weapon.IsInsideTree() || weapon.IsDying || weapon.ActiveCue != "attack" ||
            !weapon.TryGetWeaponEffectAnchor(out var point)) return;
        var burst = EmitBurst(weapon.AttackEffectCue, Vector3.Zero, Vector3.Forward, color);
        burst.Weapon = weapon;
        burst.TrailStarted = false;
        burst.Root.Rotation = Vector3.Zero;
        Array.Fill(burst.Trail, ToLocal(weapon.ToGlobal(point)));
        Pose(burst);
    }

    public void Advance(double delta, bool paused, bool reducedEffects)
    {
        if (reducedEffects) { Clear(); return; }
        if (paused) return;
        float dt = (float)Math.Clamp(delta, 0, .1);
        foreach (var burst in _pool)
        {
            if (!burst.Active) continue;
            burst.Age += dt;
            if (burst.Age >= burst.Duration) { Retire(burst); continue; }
            if (burst.Weapon is { } weapon)
            {
                if (!GodotObject.IsInstanceValid(weapon) || !weapon.IsInsideTree() || weapon.IsQueuedForDeletion() ||
                    weapon.IsDying || weapon.ActiveCue != "attack" || !weapon.TryGetWeaponEffectAnchor(out var point))
                { Retire(burst); continue; }
                var contact = ToLocal(weapon.ToGlobal(point));
                if (!burst.TrailStarted) { Array.Fill(burst.Trail, contact); burst.TrailStarted = true; }
                else
                {
                    for (int i = burst.Trail.Length - 1; i > 0; i--) burst.Trail[i] = burst.Trail[i - 1];
                    burst.Trail[0] = contact;
                }
            }
            Pose(burst);
        }
    }

    public void Clear()
    { foreach (var burst in _pool) Retire(burst); }

    private static void Retire(Burst burst)
    { burst.Active = false; burst.Root.Visible = false; burst.Weapon = null; }

    private static void Pose(Burst burst)
    {
        float t = burst.Age / burst.Duration;
        burst.Material.AlbedoColor = new Color(burst.Color, (1 - t) * .8f);
        for (int i = 0; i < 6; i++)
        {
            var piece = burst.Pieces[i];
            float a = i * Mathf.Tau / 6;
            piece.Rotation = Vector3.Zero;
            if (burst.Weapon is not null)
            {
                float size = (1 - i / 7f) * (burst.Cue == "heavy_slash" ? .115f : .065f);
                piece.Position = burst.Trail[i];
                piece.Scale = Vector3.One * size;
                if (burst.Cue == "spell")
                {
                    a += t * 3;
                    piece.Position = burst.Trail[0] + new Vector3(MathF.Sin(a), MathF.Cos(a), 0) * (.09f + t * .12f);
                    piece.Rotation = new(a, a, a);
                    piece.Scale = new(size * .6f, size * 1.8f, size * .6f);
                }
                continue;
            }
            switch (burst.Cue)
            {
                case "legendary_pyre":
                    piece.Position = new(MathF.Sin(a) * (.26f + t * .35f), .10f + t * .55f, MathF.Cos(a) * (.26f + t * .35f));
                    piece.Rotation = new(0, a, .15f); piece.Scale = new(.045f, .30f * (1 - t) + .06f, .045f); break;
                case "legendary_oath":
                    piece.Position = new(MathF.Sin(a) * (0.45f + t * 1.95f), .13f + .22f * (1 - t), MathF.Cos(a) * (0.45f + t * 1.95f));
                    piece.Rotation = new(0, a, 0); piece.Scale = new(.38f, .08f * (1 - t) + .025f, .08f); break;
                case "legendary_widow":
                    piece.Position = new(MathF.Sin(a) * .20f * (1 - t), 1 + MathF.Cos(a) * .16f, .25f + t * 1.5f - i * .075f);
                    piece.Rotation = new(0, 0, a); piece.Scale = new(.028f, .028f, .32f * (1 - t) + .10f); break;
                case "legendary_rotwake":
                    piece.Position = new(MathF.Sin(a) * (.18f + t * 1.15f), .12f + MathF.Sin(t * Mathf.Pi) * .35f, MathF.Cos(a) * (.18f + t * 1.15f));
                    piece.Rotation = new(0, a, .5f); piece.Scale = new(.075f * (1 - t) + .02f, .18f, .035f); break;
                case "legendary_chorus":
                    a = (i / 2) * Mathf.Tau / 3;
                    piece.Position = new(MathF.Sin(a) * (.85f * (1 - t) + .10f), .55f + (i % 2) * .35f + t * .3f, MathF.Cos(a) * (.85f * (1 - t) + .10f));
                    piece.Rotation = new(0, a, 0); piece.Scale = new(.035f, .3f * (1 - t) + .06f, .035f); break;
                case "legendary_cinder":
                    a += t * 1.8f;
                    piece.Position = new(MathF.Sin(a) * (.4f - t * .25f), .95f + t * .25f, MathF.Cos(a) * (.4f - t * .25f));
                    piece.Rotation = new(0, a, .2f); piece.Scale = new(.035f, .15f * (1 - t) + .05f, .035f); break;
                case "legendary_verdict":
                    piece.Position = new(MathF.Sin(a) * (.52f + t * .15f), .45f + t * .85f, MathF.Cos(a) * (.52f + t * .15f));
                    piece.Rotation = new(0, a, 0); piece.Scale = new(.12f, .42f * (1 - t) + .04f, .035f); break;
                case "legendary_witness":
                    piece.Position = new(MathF.Sin(a) * (.85f * (1 - t)), .9f + MathF.Cos(a) * .38f * (1 - t), 0);
                    piece.Rotation = new(0, 0, -a); piece.Scale = new(.04f, .20f, .04f); break;
                case "legendary_hour":
                    a -= t * 3;
                    piece.Position = new(MathF.Sin(a) * .48f, .35f + i * .20f, MathF.Cos(a) * .48f);
                    piece.Rotation = new(0, a, .7f); piece.Scale = new(.06f, .15f * (1 - t) + .03f, .035f); break;
                case "legendary_ready":
                    a += t * 1.7f;
                    piece.Position = new(MathF.Sin(a) * .40f, .90f + MathF.Cos(a) * .24f, MathF.Cos(a) * .28f);
                    piece.Rotation = new(0, a, .4f); piece.Scale = new(.025f, .14f, .025f); break;
                case "loot_legendary":
                    a += t * .65f;
                    piece.Position = new(MathF.Sin(a) * (.20f + t * .50f), .12f + t * (1.0f + i * .08f), MathF.Cos(a) * (.20f + t * .50f));
                    piece.Rotation = new(0, a, .25f); piece.Scale = new(.035f, .22f * (1 - t) + .04f, .035f); break;
                case "loot_godwrought":
                    a -= t * .4f;
                    piece.Position = new(MathF.Sin(a) * (.35f + t * .25f), .18f + t * .65f, MathF.Cos(a) * (.35f + t * .25f));
                    piece.Rotation = new(0, -a, 0); piece.Scale = new(.055f, (.65f + i % 2 * .4f) * (1 - t) + .08f, .055f); break;
                case "slash":
                    a = -.95f + i * .32f + t * .6f;
                    piece.Position = new(MathF.Sin(a) * 1.05f, .95f + i * .025f, MathF.Cos(a) * 1.05f);
                    piece.Rotation = new(0, a, -.12f);
                    piece.Scale = new(.34f, .045f, .045f); break;
                case "thrust":
                    piece.Position = new((i % 2 - .5f) * .08f, 1 + i * .025f, .55f + i * .14f + t * .5f);
                    piece.Scale = new(.025f, .025f, .48f); break;
                case "spell":
                    a += t * 2;
                    piece.Position = new(MathF.Sin(a) * (.38f + t * .3f), .8f + i * .09f + t * .4f, .5f + MathF.Cos(a) * (.38f + t * .3f));
                    piece.Rotation = new(a, a, a); piece.Scale = new(.08f, .18f, .08f); break;
                case "dust":
                case "dodge":
                    piece.Position = new(MathF.Sin(a) * (.2f + t * .6f), .1f + t * .13f, MathF.Cos(a) * (.2f + t * .3f) - t * .5f);
                    piece.Scale = Vector3.One * (.12f + t * .22f); break;
                case "death":
                    piece.Position = new(MathF.Sin(a) * (.25f + t * .5f), .15f + MathF.Sin(t * Mathf.Pi) * .28f, MathF.Cos(a) * (.25f + t * .5f));
                    piece.Scale = Vector3.One * (.12f + t * .14f); break;
                default:
                    float spread = burst.Cue is "victory" or "phase" ? 1.5f : .65f;
                    piece.Position = new(MathF.Sin(a) * t * spread, .85f + MathF.Sin(a * 2 + 1) * t * .5f + (burst.Cue == "victory" ? t : 0), MathF.Cos(a) * t * spread);
                    piece.Rotation = new(a, a + t, a * .5f);
                    piece.Scale = new(.035f, burst.Cue == "block" ? .25f : .15f, .035f); break;
            }
        }
    }
}
