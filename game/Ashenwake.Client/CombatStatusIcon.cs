using Godot;

namespace Ashenwake.Client;

/// <summary>Small status glyphs with distinct silhouettes as well as colors.</summary>
public partial class CombatStatusIcon : Control
{
    private static readonly Color Ink = new("14242c"), Bone = new("f0e4c9");
    public string StatusId { get; private set; } = "";
    private Color _accent = Bone;

    public CombatStatusIcon()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
    }

    public void SetStatus(string statusId, Color accent)
    {
        if (StatusId == statusId && _accent == accent) return;
        StatusId = statusId; _accent = accent; QueueRedraw();
    }

    public override void _Notification(int what)
    { if (what == NotificationResized) QueueRedraw(); }

    public override void _Draw()
    {
        float side = MathF.Min(Size.X, Size.Y);
        if (side <= 0) return;
        DrawSetTransform((Size - Vector2.One * side) * .5f, 0, Vector2.One * (side / 32));
        switch (StatusId)
        {
            case "Burning":
                Shape(_accent, new(17, 2), new(22, 11), new(26, 7), new(28, 19), new(23, 28), new(13, 30), new(5, 23), new(5, 14), new(11, 7), new(12, 16));
                Shape(Bone, new(17, 16), new(22, 24), new(16, 28), new(12, 24)); break;
            case "Bleeding":
                Drop(new(15, 17), 9, _accent);
                Line(new(23, 7), new(29, 2), Bone); Line(new(25, 14), new(30, 10), Bone); break;
            case "Poisoned":
                Shape(_accent, new(11, 3), new(21, 3), new(21, 7), new(19, 7), new(19, 12), new(27, 25), new(24, 29), new(8, 29), new(5, 25), new(13, 12), new(13, 7), new(11, 7));
                Line(new(10, 21), new(23, 21), Ink); Disc(new(14, 25), 1.5f, Ink); Disc(new(19, 18), 1.5f, Ink); break;
            case "Chilled":
                Snowflake(10); Line(new(7, 29), new(25, 29), Bone); break;
            case "Frozen":
                Shape(_accent.Darkened(.4f), new(7, 4), new(23, 4), new(29, 13), new(24, 29), new(8, 29), new(3, 17));
                Snowflake(11); break;
            case "Shocked":
                Shape(_accent, new(18, 2), new(7, 19), new(15, 19), new(12, 31), new(27, 12), new(19, 12), new(23, 2)); break;
            case "Staggered":
                Star(new(16, 13), 11, _accent);
                Line(new(4, 27), new(11, 24), Bone); Line(new(21, 24), new(28, 27), Bone); break;
            case "Cursed":
                Shape(_accent, new(16, 2), new(28, 13), new(20, 30), new(12, 30), new(4, 13));
                Line(new(10, 10), new(21, 22), Ink, 3); Line(new(22, 10), new(11, 22), Ink, 3); break;
            case "Terrified":
                Shape(_accent, new(7, 4), new(25, 4), new(29, 16), new(24, 24), new(21, 30), new(11, 30), new(8, 24), new(3, 16));
                Disc(new(11, 14), 3, Ink); Disc(new(21, 14), 3, Ink); Disc(new(16, 24), 3, Ink); break;
            case "Marked":
                DrawArc(new(16, 16), 9, 0, Mathf.Tau, 24, _accent, 2, true);
                Disc(new(16, 16), 3, Bone);
                Line(new(16, 1), new(16, 8), _accent); Line(new(16, 24), new(16, 31), _accent);
                Line(new(1, 16), new(8, 16), _accent); Line(new(24, 16), new(31, 16), _accent); break;
            case "Vulnerable":
                Shield(_accent);
                Stroke(Ink, 3, new(19, 3), new(13, 13), new(21, 17), new(13, 30)); break;
            case "Rooted":
                Stroke(_accent, 3, new(16, 3), new(16, 21), new(8, 29));
                Stroke(_accent, 2, new(16, 20), new(23, 24), new(26, 30));
                Stroke(_accent, 2, new(16, 23), new(16, 30));
                Line(new(16, 12), new(6, 7), _accent); Line(new(16, 16), new(26, 10), _accent); break;
            case "Barrier":
                Shield(_accent); Line(new(16, 8), new(16, 23), Ink, 2.5f); Line(new(9, 15), new(23, 15), Ink, 2.5f); break;
            case "Guarded":
                Shield(_accent); Shape(Ink, new(16, 8), new(21, 15), new(16, 23), new(11, 15)); break;
            case "Shielded":
                DrawArc(new(16, 16), 14, 0, Mathf.Tau, 28, _accent, 2, true); Shield(Bone); break;
            default:
                Shape(_accent, new(16, 2), new(30, 16), new(16, 30), new(2, 16));
                Line(new(16, 9), new(16, 19), Ink, 2.5f); Disc(new(16, 24), 1.5f, Ink); break;
        }
    }

    private void Shield(Color color) => Shape(color, new(5, 6), new(16, 2), new(27, 6), new(25, 21), new(16, 30), new(7, 21));
    private void Drop(Vector2 at, float radius, Color color) => Shape(color, at + new Vector2(0, -radius * 1.5f), at + new Vector2(radius, 0),
        at + new Vector2(radius * .7f, radius), at + new Vector2(-radius * .7f, radius), at + new Vector2(-radius, 0));
    private void Snowflake(float radius)
    {
        Vector2 center = new(16, 15);
        for (int i = 0; i < 6; i++)
        {
            Vector2 direction = Vector2.FromAngle(i * Mathf.Tau / 6), across = direction.Orthogonal();
            Line(center, center + direction * radius, _accent);
            Stroke(_accent, 1.5f, center + direction * radius + across * 3, center + direction * (radius - 3), center + direction * radius - across * 3);
        }
    }
    private void Star(Vector2 center, float radius, Color color)
    {
        Vector2[] points = new Vector2[10];
        for (int i = 0; i < points.Length; i++) points[i] = center + Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Pi / 5) * (i % 2 == 0 ? radius : radius * .45f);
        Shape(color, points);
    }
    private void Shape(Color color, params Vector2[] points) => DrawColoredPolygon(points, color);
    private void Line(Vector2 from, Vector2 to, Color color, float width = 2) => DrawLine(from, to, color, width, true);
    private void Stroke(Color color, float width, params Vector2[] points) => DrawPolyline(points, color, width, true);
    private void Disc(Vector2 at, float radius, Color color) => DrawCircle(at, radius, color);
}
