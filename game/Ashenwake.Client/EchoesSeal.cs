using Godot;

namespace Ashenwake.Client;

/// <summary>Decorative mind, borrowed-storm and earned-record emblems, paired with readable native text.</summary>
public partial class EchoesSeal : Control
{
    public string Kind { get; init; } = "mind";
    public EchoesSeal() { MouseFilter = MouseFilterEnum.Ignore; FocusMode = FocusModeEnum.None; }
    public override void _Notification(int what) { if (what == NotificationResized) QueueRedraw(); }
    public override void _Draw()
    {
        float extent = Math.Min(Size.X, Size.Y); if (extent <= 0) return;
        DrawSetTransform(Size * .5f, 0, Vector2.One * extent / 64);
        Color color = Kind == "earned" ? new("efd08b") : Kind == "mind" ? new("add7ca") : Kind == "locked" ? new("718494") : new("accdf0");
        DrawCircle(Vector2.Zero, 28, new("0a1722"));
        DrawArc(Vector2.Zero, 27, 0, Mathf.Tau, 48, color.Darkened(.45f), 1, true);
        if (Kind is "earned" or "locked")
        {
            DrawPolyline([new(-14, 23), new(-11, 8), new(0, 13), new(11, 8), new(14, 23), new(4, 19), new(0, 26), new(-4, 19), new(-14, 23)], color, 2, true);
            DrawCircle(new(0, -4), 17, color.Darkened(.8f)); DrawArc(new(0, -4), 17, 0, Mathf.Tau, 32, color, 2, true);
            DrawPolyline([new(-7, -3), new(-1, 4), new(9, -10)], color, 2.5f, true);
        }
        else if (Kind == "borrow")
        {
            DrawArc(Vector2.Zero, 18, -.4f, 4.5f, 32, color, 2, true);
            DrawPolyline([new(3, -21), new(-12, 1), new(-1, 1), new(-4, 21), new(13, -4), new(2, -4), new(3, -21)], color, 2.2f, true);
            DrawLine(new(-23, -8), new(-18, -8), color, 2, true); DrawLine(new(20, 10), new(25, 10), color, 2, true);
        }
        else
        {
            DrawPolyline([new(-2, -18), new(-10, -18), new(-16, -11), new(-16, -3), new(-20, 2), new(-14, 7), new(-13, 14), new(-5, 18), new(0, 13), new(5, 18), new(13, 14), new(14, 7), new(20, 2), new(16, -3), new(16, -11), new(10, -18), new(2, -18)], color, 2, true);
            DrawLine(new(0, -18), new(0, 13), color, 1.5f, true);
            DrawPolyline([new(-11, -11), new(-6, -7), new(-10, 0), new(-6, 8)], color, 1.5f, true);
            DrawPolyline([new(11, -11), new(6, -7), new(10, 0), new(6, 8)], color, 1.5f, true);
        }
    }
}
