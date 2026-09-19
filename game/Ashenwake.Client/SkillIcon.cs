using Godot;

namespace Ashenwake.Client;

/// <summary>Shared skill glyphs, drawn without textures or preview worlds.</summary>
public partial class SkillIcon : Control
{
    private static readonly Color Ink = new("10232d"), Bone = new("e9dfc8"), Ember = new("f4a477"), Frost = new("a2dfed");
    private string _skill = "", _shape = "";
    private Color _accent = new("e7bc85");
    public string IconKey { get; private set; } = "";

    public SkillIcon()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new(28, 28);
    }

    public void SetSkill(string skillId, string discipline, string shape)
    {
        string id = skillId.Trim().ToLowerInvariant(), form = discipline.Trim().ToLowerInvariant();
        string shapeName = shape.Trim();
        string key = id + "|" + form + "|" + shapeName;
        if (IconKey == key) return;
        IconKey = key; _skill = id.Split('.').Last(); _shape = shapeName;
        _accent = form switch
        {
            "veilwalker" => new("c3a9e5"),
            "arcanist" => new("9dbfeb"),
            "gravecaller" => new("b6d1ad"),
            "warden" => new("96cfac"),
            _ => new("e7bc85")
        };
        QueueRedraw();
    }

    public override void _Notification(int what)
    { if (what == NotificationResized) QueueRedraw(); }

    public override void _Draw()
    {
        float side = MathF.Min(Size.X, Size.Y);
        if (side <= 0) return;
        DrawSetTransform((Size - Vector2.One * side) * .5f, 0, Vector2.One * (side / 40));
        switch (_skill)
        {
            case "cleave":
                Arc(new(19, 22), 14, 3.5f, 6.1f, _accent, 3);
                Blade(new(10, 33), new(27, 13), Bone); break;
            case "shield_breaker":
                Shield(_accent);
                Stroke(Ink, 3, new(24, 6), new(17, 17), new(24, 20), new(16, 33));
                Line(new(7, 7), new(12, 12), Bone, 2); Line(new(29, 27), new(34, 32), Bone, 2); break;
            case "seismic_wave":
                for (int i = 0; i < 3; i++) Stroke(_accent, 2.5f, new(6, 31 - i * 8), new(20, 24 - i * 8), new(34, 31 - i * 8));
                Line(new(4, 35), new(36, 35), Bone, 1.5f); break;
            case "charge":
                Arrow(new(5, 21), new(30, 21), _accent, 4);
                Stroke(Bone, 2, new(32, 7), new(29, 14), new(35, 17));
                Stroke(Bone, 2, new(35, 26), new(29, 28), new(32, 35)); break;
            case "iron_guard":
                Shield(_accent); Line(new(20, 10), new(20, 28), Bone, 2.5f);
                Line(new(12, 16), new(28, 16), Bone, 2.5f); break;
            case "cataclysm":
                Flame(new(20, 18), .9f, Ember);
                Stroke(_accent, 2, new(3, 34), new(10, 25), new(14, 34), new(20, 27), new(26, 34), new(30, 25), new(37, 34));
                Rays(new(20, 19), 14, 18, 5, Ember); break;
            case "venom_knife":
                Blade(new(9, 32), new(26, 8), Bone);
                Drop(new(29, 27), 6, new("a4d884")); break;
            case "shadow_step":
                Shape(_accent.Darkened(.3f), new(5, 29), new(9, 14), new(16, 10), new(17, 23), new(13, 32));
                Shape(_accent, new(23, 29), new(25, 13), new(32, 9), new(34, 24), new(31, 32));
                Arrow(new(10, 6), new(29, 6), Bone, 1.7f); break;
            case "dusk_fan":
                Blade(new(20, 34), new(7, 10), _accent);
                Blade(new(20, 34), new(20, 4), Bone);
                Blade(new(20, 34), new(33, 10), _accent); break;
            case "shroud":
                Shape(_accent, new(20, 3), new(29, 12), new(34, 33), new(25, 30), new(20, 35), new(15, 30), new(6, 33), new(11, 12));
                Shape(Ink, new(20, 11), new(26, 21), new(23, 28), new(17, 28), new(14, 21));
                Line(new(17, 21), new(23, 21), Bone, 1.5f); break;
            case "terror":
                Skull(new(20, 21), 1, _accent); Rays(new(20, 21), 14, 18, 8, _accent); break;
            case "shadow_execution":
                Blade(new(7, 34), new(31, 5), _accent); Blade(new(33, 34), new(9, 5), _accent);
                Skull(new(20, 21), .65f, Bone); break;
            case "fire_lance":
                Stroke(Ember, 2, new(4, 31), new(11, 28), new(7, 35), new(20, 27));
                Flame(new(25, 14), .9f, Ember);
                Line(new(12, 29), new(28, 11), Bone, 2); break;
            case "frost_nova":
                Snowflake(new(20, 20), 14); Arc(new(20, 20), 17, .1f, 6, Frost, 1); break;
            case "storm_arc":
                Lightning(new(4, 7), new(35, 31), Frost);
                Disc(new(5, 7), 3, _accent); Disc(new(34, 31), 3, _accent); break;
            case "vent":
                for (int x = 9; x <= 31; x += 11) Arrow(new(x, 30), new(x, 8), Frost, 2);
                Arc(new(20, 29), 14, .2f, Mathf.Pi - .2f, _accent, 2); break;
            case "ember_stride":
                Shape(_accent, new(19, 6), new(29, 6), new(27, 23), new(35, 27), new(34, 33), new(17, 32), new(15, 26));
                Flame(new(8, 23), .5f, Ember); Line(new(5, 35), new(26, 35), Ember, 2); break;
            case "starfall":
                Line(new(5, 4), new(23, 22), _accent, 2);
                Line(new(3, 13), new(17, 27), _accent, 1.5f);
                Line(new(14, 4), new(29, 19), Bone, 1.5f); Star(new(26, 27), 10, Bone); break;
            case "grave_bolt":
                Line(new(4, 25), new(17, 21), _accent, 2); Line(new(5, 32), new(20, 27), _accent, 2);
                Skull(new(26, 15), .9f, Bone); break;
            case "bone_lance":
                Blade(new(8, 32), new(32, 5), Bone);
                Disc(new(6, 30), 3, Bone); Disc(new(10, 34), 3, Bone);
                Line(new(14, 21), new(20, 26), _accent, 2); break;
            case "raise_ancestor":
                Shape(_accent.Darkened(.2f), new(6, 35), new(6, 23), new(12, 18), new(18, 23), new(18, 35));
                Line(new(12, 24), new(12, 32), Bone, 1.5f);
                Skull(new(26, 14), .75f, Bone);
                Stroke(_accent, 2, new(21, 23), new(24, 30), new(31, 23)); break;
            case "soul_siphon":
                Arc(new(20, 21), 13, .3f, 5.6f, _accent, 2.5f);
                Arrow(new(29, 10), new(22, 6), _accent, 2);
                Drop(new(20, 21), 7, Bone); break;
            case "grave_command":
                Skull(new(20, 23), .9f, Bone);
                Shape(_accent, new(9, 12), new(7, 4), new(15, 9), new(20, 3), new(25, 9), new(33, 4), new(31, 12)); break;
            case "procession":
                Skull(new(9, 23), .6f, _accent); Skull(new(31, 23), .6f, _accent); Skull(new(20, 12), .8f, Bone);
                Line(new(5, 34), new(35, 34), _accent, 2); break;
            case "thorn_shot":
                Arrow(new(7, 33), new(30, 6), Bone, 2.5f);
                Shape(_accent, new(15, 24), new(7, 20), new(8, 12), new(19, 19));
                Shape(_accent, new(20, 21), new(30, 20), new(34, 12), new(25, 15)); break;
            case "entangle":
                Stroke(_accent, 2.5f, new(8, 35), new(15, 24), new(11, 16), new(16, 5));
                Stroke(_accent, 2.5f, new(31, 35), new(23, 24), new(29, 15), new(24, 5));
                Line(new(12, 13), new(27, 19), Bone, 2); Line(new(13, 26), new(26, 29), Bone, 2);
                Line(new(15, 24), new(7, 24), _accent, 2); Line(new(28, 16), new(35, 11), _accent, 2); break;
            case "feral_companion": Wolf(); break;
            case "barkskin":
                Shield(_accent.Darkened(.2f));
                Stroke(Bone, 1.5f, new(14, 10), new(17, 17), new(14, 27));
                Stroke(_accent, 2, new(24, 9), new(21, 18), new(25, 28));
                Disc(new(21, 22), 2, Ink); break;
            case "adaptive_strike":
                for (int i = 0; i < 3; i++) Shape(i == 1 ? Bone : _accent, new(7 + i * 9, 33), new(14 + i * 9, 6), new(15 + i * 9, 19));
                Arc(new(20, 21), 16, .1f, 1.7f, _accent, 1.5f); break;
            case "primal_awakening":
                Wolf(.75f);
                Stroke(_accent, 2, new(14, 15), new(9, 10), new(8, 3));
                Stroke(_accent, 2, new(26, 15), new(31, 10), new(32, 3));
                Line(new(9, 10), new(4, 8), _accent, 2); Line(new(31, 10), new(36, 8), _accent, 2);
                Arc(new(20, 21), 17, .15f, Mathf.Pi - .15f, Bone, 1.5f); break;
            case "echo_storm":
                Arc(new(20, 20), 15, 0, Mathf.Tau, _accent, 1.5f); Lightning(new(10, 6), new(28, 34), Bone); break;
            default: ShapeGlyph(_shape, new(20, 20), 1); break;
        }
        string authored = AuthoredShape(_skill);
        if (authored.Length > 0 && _shape != authored)
        {
            Disc(new(32, 32), 7, Ink);
            ShapeGlyph(_shape, new(32, 32), .35f);
        }
    }

    private static string AuthoredShape(string skill) => skill switch
    {
        "cleave" or "shield_breaker" or "venom_knife" or "shadow_execution" or "soul_siphon" or "adaptive_strike" => "Melee",
        "charge" or "shadow_step" or "ember_stride" => "Dash",
        "iron_guard" or "shroud" or "vent" or "barkskin" => "Guard",
        "seismic_wave" or "dusk_fan" or "fire_lance" or "storm_arc" or "grave_bolt" or "bone_lance" or "thorn_shot" => "Projectile",
        "cataclysm" or "terror" or "frost_nova" or "starfall" or "entangle" or "primal_awakening" or "echo_storm" => "Area",
        "raise_ancestor" or "procession" or "feral_companion" => "Summon",
        "grave_command" => "Command",
        _ => ""
    };

    private void ShapeGlyph(string shape, Vector2 at, float scale)
    {
        float r = 13 * scale;
        switch (shape)
        {
            case "Area": Arc(at, r, 0, Mathf.Tau, _accent, 2 * scale); Disc(at, 3 * scale, Bone); break;
            case "Guard": Shape(_accent, at + new Vector2(-r, -r), at + new Vector2(r, -r), at + new Vector2(r * .8f, r * .3f), at + new Vector2(0, r), at + new Vector2(-r * .8f, r * .3f)); break;
            case "Summon": Skull(at, scale, _accent); break;
            case "Dash": Arrow(at - new Vector2(r, 0), at + new Vector2(r, 0), _accent, 3 * scale); break;
            case "Projectile": Arrow(at + new Vector2(-r, r), at + new Vector2(r, -r), _accent, 2 * scale); break;
            case "Command": Star(at, r, _accent); break;
            default: Blade(at + new Vector2(-r, r), at + new Vector2(r, -r), _accent, scale); break;
        }
    }

    private void Shield(Color color)
        => Shape(color, new(8, 8), new(20, 4), new(32, 8), new(29, 26), new(20, 35), new(11, 26));

    private void Blade(Vector2 start, Vector2 end, Color color, float scale = 1)
    {
        Vector2 direction = (end - start).Normalized(), side = direction.Orthogonal();
        Vector2 basePoint = start + direction * 7 * scale;
        Line(start, basePoint, _accent, 3 * scale);
        Shape(color, basePoint - side * 2.5f * scale, end - direction * 7 * scale - side * 3 * scale, end,
            end - direction * 7 * scale + side * 3 * scale, basePoint + side * 2.5f * scale);
        Line(basePoint - side * 5 * scale, basePoint + side * 5 * scale, _accent, 2 * scale);
    }

    private void Skull(Vector2 at, float scale, Color color)
    {
        Vector2 P(float x, float y) => at + new Vector2(x, y) * scale;
        Shape(color, P(-7, -7), P(0, -10), P(7, -7), P(9, 0), P(5, 5), P(4, 10), P(-4, 10), P(-5, 5), P(-9, 0));
        Disc(P(-3.5f, 0), 2 * scale, Ink); Disc(P(3.5f, 0), 2 * scale, Ink);
        Line(P(-3, 6), P(3, 6), Ink, scale);
    }

    private void Wolf(float scale = 1)
    {
        Vector2 P(float x, float y) => new Vector2(20, 22) + new Vector2(x, y) * scale;
        Shape(_accent, P(-14, -17), P(-3, -11), P(3, -11), P(14, -17), P(11, 3), P(0, 14), P(-11, 3));
        Shape(Bone, P(-6, 0), P(0, 5), P(6, 0), P(3, 9), P(-3, 9));
        Line(P(-8, -3), P(-3, -1), Ink, 2 * scale); Line(P(3, -1), P(8, -3), Ink, 2 * scale);
        Disc(P(0, 5), 2 * scale, Ink);
    }

    private void Flame(Vector2 at, float scale, Color color)
    {
        Vector2 P(float x, float y) => at + new Vector2(x, y) * scale;
        Shape(color, P(1, -14), P(7, -4), P(8, -9), P(12, 3), P(8, 11), P(0, 14), P(-9, 8), P(-10, 0), P(-4, -7), P(-4, 1));
        Shape(Bone, P(1, -2), P(5, 6), P(0, 10), P(-4, 6));
    }

    private void Drop(Vector2 at, float radius, Color color)
        => Shape(color, at + new Vector2(0, -radius * 1.4f), at + new Vector2(radius, .2f * radius), at + new Vector2(radius * .6f, radius),
            at + new Vector2(-radius * .6f, radius), at + new Vector2(-radius, radius * .2f));

    private void Snowflake(Vector2 at, float radius)
    {
        for (int i = 0; i < 6; i++)
        {
            Vector2 direction = Vector2.FromAngle(i * Mathf.Tau / 6), side = direction.Orthogonal();
            Line(at, at + direction * radius, Frost, 2);
            Stroke(Frost, 1.4f, at + direction * 10 + side * 4, at + direction * 7, at + direction * 10 - side * 4);
        }
    }

    private void Lightning(Vector2 start, Vector2 end, Color color)
    {
        Vector2 delta = end - start, side = delta.Normalized().Orthogonal() * 5;
        Stroke(color, 3, start, start + delta * .4f + side, start + delta * .5f - side, start + delta * .8f + side * .4f, end);
    }

    private void Star(Vector2 at, float radius, Color color)
    {
        var points = new Vector2[10];
        for (int i = 0; i < points.Length; i++) points[i] = at + Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Pi / 5) * radius * (i % 2 == 0 ? 1 : .4f);
        Shape(color, points);
    }

    private void Rays(Vector2 at, float inner, float outer, int count, Color color)
    {
        for (int i = 0; i < count; i++)
        { Vector2 direction = Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Tau / count); Line(at + direction * inner, at + direction * outer, color, 1.6f); }
    }

    private void Arrow(Vector2 start, Vector2 end, Color color, float width)
    {
        Vector2 direction = (end - start).Normalized(), side = direction.Orthogonal();
        Line(start, end, color, width);
        Stroke(color, width, end - direction * width * 2 + side * width * 1.6f, end, end - direction * width * 2 - side * width * 1.6f);
    }

    private void Shape(Color color, params Vector2[] points)
    {
        DrawColoredPolygon(points, color);
        for (int i = 0; i < points.Length; i++) DrawLine(points[i], points[(i + 1) % points.Length], Ink, .8f, true);
    }
    private void Line(Vector2 from, Vector2 to, Color color, float width) => DrawLine(from, to, color, width, true);
    private void Stroke(Color color, float width, params Vector2[] points) => DrawPolyline(points, color, width, true);
    private void Disc(Vector2 at, float radius, Color color) => DrawCircle(at, radius, color);
    private void Arc(Vector2 at, float radius, float start, float end, Color color, float width) => DrawArc(at, radius, start, end, 32, color, width, true);
}
