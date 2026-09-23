using Godot;

namespace Ashenwake.Client;

public partial class GearItemIcon
{
    private void UnswornCrown()
    {
        Color gold = new("b99754"), oath = new("ffe2a0");
        Shape(gold, new(5, 29), new(2, 12), new(11, 19), new(13, 6), new(19, 20), new(17, 29));
        Shape(gold, new(23, 29), new(21, 20), new(27, 6), new(29, 19), new(38, 12), new(35, 29));
        Line(new(6, 27), new(16, 27), oath, 2);
        Line(new(24, 27), new(34, 27), oath, 2);
        Gem(new(20, 23), 3, oath);
    }

    private void WitnessAmulet()
    {
        Color silver = new("a6a0bb"), eye = new("beafff");
        Arc(new(20, 10), 14, 0, Mathf.Pi, silver, 1.6f);
        Shape(silver, new(3, 26), new(20, 16), new(37, 26), new(20, 35));
        Shape(Ink, new(7, 26), new(20, 20), new(33, 26), new(20, 31));
        Disc(new(20, 26), 5.5f, eye);
        Line(new(20, 22), new(20, 30), Ink, 2);
    }

    private void HourLegs()
    {
        Color slate = new("354350"), silver = new("a2babd"), hour = new("97eee6");
        foreach (int x in new[] { 6, 23 })
        {
            Shape(silver, new(x, 5), new(x + 12, 5), new(x + 11, 35), new(x + 2, 35));
            Shape(slate, new(x + 2, 8), new(x + 10, 8), new(x + 9, 32), new(x + 3, 32));
            Line(new(x + 3, 12), new(x + 9, 28), hour, 1.5f);
            Line(new(x + 9, 12), new(x + 3, 28), hour, 1.5f);
            Line(new(x + 3, 12), new(x + 9, 12), silver, 1.4f);
            Line(new(x + 3, 28), new(x + 9, 28), silver, 1.4f);
        }
    }
}
