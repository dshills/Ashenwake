using Godot;

namespace Ashenwake.Client;

public partial class GearItemIcon
{
    private void RotwakeRing()
    {
        Color bronze = new("7d8854"), venom = new("b4d879");
        Arc(new(20, 24), 10, 0, Mathf.Tau, bronze, 4.5f);
        Arc(new(20, 24), 7, .4f, Mathf.Pi - .4f, venom, 1.2f);
        foreach (int side in new[] { -1, 1 })
        {
            Shape(bronze, new(20 + side * 8, 17), new(20 + side * 17, 10), new(20 + side * 12, 22));
            Shape(bronze, new(20 + side * 9, 25), new(20 + side * 16, 21), new(20 + side * 11, 31));
        }
        Gem(new(20, 12), 8, bronze);
        Gem(new(20, 11), 5.5f, venom);
        Line(new(18, 9), new(21, 9), Bone, 1.3f);
    }

    private void MourningShoulders()
    {
        foreach (int side in new[] { 3, 23 })
        {
            Shape(new("292f41"), new(side, 17), new(side + 14, 17), new(side + 13, 36), new(side + 7, 32), new(side + 1, 36));
            Shape(new("42495a"), new(side, 15), new(side + 4, 10), new(side + 11, 10), new(side + 14, 15), new(side + 13, 23), new(side + 1, 23));
            for (int pipe = 0; pipe < 3; pipe++)
            {
                float x = side + 3 + pipe * 4;
                Line(new(x, 20), new(x, 10 - pipe * 3), Bone, 3);
                Line(new(x - 1, 9 - pipe * 3), new(x + 1, 9 - pipe * 3), Ink, 1.2f);
                Line(new(x, 17), new(x, 20), new("99d9cf"), 1.3f);
                Line(new(x, 26), new(x, 30), Bone, 1.2f);
            }
        }
    }

    private void FurnaceBelt()
    {
        Color iron = new("383335"), copper = new("bb7850"), ember = new("ffc06c");
        Shape(iron, new(3, 13), new(12, 10), new(28, 10), new(37, 13), new(36, 28), new(28, 25), new(12, 25), new(4, 28));
        Shape(copper, new(11, 8), new(29, 8), new(30, 29), new(10, 29));
        Shape(iron, new(14, 11), new(26, 11), new(27, 26), new(13, 26));
        Disc(new(20, 19), 5.3f, ember);
        for (int x = 16; x <= 24; x += 4) Line(new(x, 11), new(x, 27), iron, 2);
        foreach (int side in new[] { 6, 32 })
            for (int row = 0; row < 3; row++) Line(new(side - 1, 17 + row * 3), new(side + 1, 17 + row * 3), copper, 1.2f);
        Line(new(11, 31), new(14, 35), copper, 2);
        Line(new(29, 31), new(26, 35), copper, 2);
    }
}
