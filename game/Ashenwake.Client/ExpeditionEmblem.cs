using Godot;

namespace Ashenwake.Client;

/// <summary>A decorative region or deity seal; the adjacent native card text supplies its meaning.</summary>
public partial class ExpeditionEmblem : Control
{
    private string _kind = "sigil", _id = "";
    private static readonly Color[] Accents = [new("adc7cf"), new("a4c68d"), new("e9a079"), new("a4c6e5"), new("b9a5df")];

    public ExpeditionEmblem()
    {
        Name = "ExpeditionEmblem";
        CustomMinimumSize = new(48, 48);
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        ClipContents = true;
    }

    public void SetIdentity(string kind, string id)
    {
        if (_kind == kind && _id == id) return;
        _kind = kind; _id = id;
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }

    public override void _Draw()
    {
        float extent = Math.Min(Size.X, Size.Y);
        if (extent <= 0) return;
        float scale = extent / 48;
        DrawSetTransform(Size * .5f, 0, new(scale, scale));
        int identity = Identity(_id);
        Color accent = identity >= 0 ? Accents[identity] : new("cbbb93");
        Color quiet = accent.Darkened(.5f);
        bool hunt = _kind == "hunt";
        DrawCircle(Vector2.Zero, 20, new("102028"));
        if (hunt)
        {
            DrawArc(Vector2.Zero, 19, 0, Mathf.Tau, 40, quiet, 1, true);
            DrawArc(Vector2.Zero, 22, .2f, Mathf.Pi - .2f, 24, accent.Darkened(.2f), 1.5f, true);
            Stroke(accent, 1.5f, new(-7, -19), new(-5, -15), new(0, -19), new(5, -15), new(7, -19));
            DrawCircle(new(0, 22), 1, accent);
        }
        else
        {
            Stroke(quiet, 1.5f, new(0, -22), new(22, 0), new(0, 22), new(-22, 0), new(0, -22));
            DrawCircle(new(0, -21), 1.5f, accent);
            DrawCircle(new(0, 21), 1.5f, accent);
        }
        switch (identity)
        {
            case 0: DrawMemory(accent, hunt); break;
            case 1: DrawHunger(accent, hunt); break;
            case 2: DrawFlame(accent, hunt); break;
            case 3: DrawOath(accent, hunt); break;
            case 4: DrawAbsence(accent, hunt); break;
            default:
                Stroke(accent, 2, new(-8, -5), new(0, -11), new(8, -5), new(4, 0), new(8, 6), new(0, 12), new(-8, 6));
                DrawLine(new(-3, -5), new(3, 5), accent, 1.5f, true);
                break;
        }
    }

    private static int Identity(string id) => id.ToLowerInvariant() switch
    {
        "act.grey_march" or "the grey march" or "grey march" or "hunt.thousand_memories" or "serath" or "god.serath" => 0,
        "act.verdant_maw" or "the verdant maw" or "verdant maw" or "hunt.ilyra_teeth" or "ilyra" or "god.ilyra" => 1,
        "act.cinder_reach" or "the cinder reach" or "cinder reach" or "hunt.false_vael" or "vael" or "god.vael" => 2,
        "act.shattered_spine" or "the shattered spine" or "shattered spine" or "hunt.orrun_without_oath" or "orrun" or "god.orrun" => 3,
        "act.hollow_night" or "the hollow night" or "hollow night" or "hunt.nhal_reconstruction" or "nhal" or "god.nhal" => 4,
        _ => -1
    };

    private void DrawMemory(Color accent, bool hunt)
    {
        if (hunt)
        {
            Stroke(accent, 2, new(-11, 6), new(-8, 2), new(-7, -6), new(-3, -10), new(3, -10), new(7, -6), new(8, 2), new(11, 6), new(-11, 6));
            DrawArc(new(0, 7), 4, 0, Mathf.Pi, 12, accent, 1.5f, true);
            DrawCircle(new(0, -12), 1.5f, accent);
            DrawLine(new(-12, 11), new(12, 11), accent.Darkened(.45f), 1, true);
        }
        else
        {
            Stroke(accent, 2, new(-7, 9), new(-7, -7), new(-3, -10), new(2, -7), new(2, 9));
            DrawLine(new(-11, -2), new(6, -2), accent, 2, true);
            Stroke(accent.Darkened(.25f), 1.5f, new(7, 8), new(8, -2), new(11, -4));
            Stroke(accent.Darkened(.2f), 1.5f, new(-12, 11), new(-5, 9), new(3, 11), new(12, 9));
        }
    }

    private void DrawHunger(Color accent, bool hunt)
    {
        Stroke(accent, 1.7f, new(-11, -6), new(-8, 4), new(0, 12), new(8, 4), new(11, -6));
        if (hunt)
        {
            Stroke(accent, 1.7f, new(-11, -6), new(-7, -10), new(0, -13), new(7, -10), new(11, -6));
            foreach (int x in new[] { -7, 0, 7 })
            {
                Stroke(accent, 1.5f, new(x - 2, -6), new(x, -1), new(x + 2, -6));
                Stroke(accent, 1.5f, new(x - 2, 4), new(x, 0), new(x + 2, 4));
            }
        }
        else
        {
            DrawLine(new(0, 10), new(0, -12), accent, 1.8f, true);
            DrawPolygon([new(-1, -2), new(-10, -5), new(-8, -11), new(-2, -8)], [accent.Darkened(.22f)]);
            DrawPolygon([new(1, 3), new(10, -1), new(9, -7), new(3, -4)], [accent]);
        }
    }

    private void DrawFlame(Color accent, bool hunt)
    {
        Vector2[] flame = [new(0, -13), new(4, -3), new(8, -7), new(11, 3), new(7, 10), new(0, 13), new(-8, 8), new(-11, 2), new(-6, -5), new(-5, 3), new(-1, -1)];
        DrawPolygon(flame, [accent.Darkened(.43f)]);
        Stroke(accent, 1.5f, [.. flame, flame[0]]);
        Stroke(new("f4d0a0"), 1.8f, new(0, -2), new(3, 4), new(-1, 8), new(1, 11));
        if (hunt)
        {
            DrawLine(new(-13, -9), new(-9, -6), accent, 1.5f, true);
            DrawLine(new(13, -9), new(9, -6), accent, 1.5f, true);
            DrawCircle(new(0, 4), 2, new("f4d0a0"));
        }
    }

    private void DrawOath(Color accent, bool hunt)
    {
        if (hunt)
        {
            Stroke(accent, 2, new(-10, -11), new(-10, 6), new(-2, 12), new(1, 7), new(-3, 2), new(1, -3), new(-2, -8), new(0, -11), new(-10, -11));
            Stroke(accent, 2, new(4, -11), new(10, -11), new(10, 6), new(4, 11));
            DrawLine(new(-7, -5), new(-4, -5), accent.Darkened(.2f), 1.5f, true);
            DrawLine(new(5, 0), new(8, 0), accent.Darkened(.2f), 1.5f, true);
        }
        else
        {
            DrawPolygon([new(-14, 11), new(-5, -13), new(4, 11)], [accent.Darkened(.4f)]);
            DrawPolygon([new(-2, 11), new(7, -8), new(14, 11)], [accent.Darkened(.2f)]);
            Stroke(accent, 1.8f, new(-10, 0), new(-5, -13), new(0, 0));
            Stroke(accent, 1.5f, new(2, 2), new(7, -8), new(11, 2));
            Stroke(new("14242c"), 2, new(-2, 12), new(1, 6), new(-1, 2));
        }
    }

    private void DrawAbsence(Color accent, bool hunt)
    {
        if (hunt)
        {
            Stroke(accent.Darkened(.4f), 1.5f, new(-7, 11), new(-10, -8), new(3, -12), new(9, 8));
            Stroke(accent, 2, new(-4, 12), new(-4, -7), new(7, -10), new(7, 9));
            DrawCircle(new(1, -1), 3.5f, new("0c171e"));
            DrawArc(new(1, -1), 4.5f, .3f, Mathf.Pi * 1.3f, 16, accent, 1, true);
        }
        else
        {
            DrawArc(Vector2.Zero, 12, -.4f, Mathf.Pi + .4f, 24, accent, 1.8f, true);
            Stroke(accent, 1.8f, new(-8, 10), new(-5, -6), new(5, -6), new(8, 10));
            DrawCircle(new(0, -1), 3, accent.Darkened(.2f));
        }
        DrawCircle(new(-12, -10), 1, new("dfd8ed"));
        DrawCircle(new(12, -6), 1, new("dfd8ed"));
    }

    private void Stroke(Color color, float width, params Vector2[] points) => DrawPolyline(points, color, width, true);
}
