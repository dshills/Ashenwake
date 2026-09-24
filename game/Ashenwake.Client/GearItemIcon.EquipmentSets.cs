using Godot;
using Ashenwake.Core.Progression;

namespace Ashenwake.Client;

public partial class GearItemIcon
{
    private static Silhouette? EquipmentSetSilhouette(string id, EquipmentSlot slot) => (id, slot) switch
    {
        ("lanternkeepers_crown", EquipmentSlot.Head) => Silhouette.LanternCrown,
        ("vigil_of_the_unburied", EquipmentSlot.Chest) => Silhouette.UnburiedVigil,
        ("thornmother_mantle", EquipmentSlot.Shoulders) => Silhouette.ThornmotherMantle,
        ("gravegarden_grasp", EquipmentSlot.Gloves) => Silhouette.GravegardenGrasp,
        ("cinderpilgrim_girdle", EquipmentSlot.Belt) => Silhouette.CinderpilgrimGirdle,
        ("embers_without_end", EquipmentSlot.Boots) => Silhouette.EndlessEmberBoots,
        _ => null
    };
    private static readonly Color VigilIvory = new("dcd9bf"), VigilBlue = new("91dfee"), VigilCloth = new("293f50");
    private static readonly Color BriarLeaf = new("829669"), BriarVenom = new("c6ee92"), BriarBark = new("79614d");
    private static readonly Color AshBrass = new("ad8050"), AshChar = new("342e31"), AshEmber = new("ffab5a");
    private void LanternCrown()
    {
        Arc(new(20, 23), 13, 0, Mathf.Tau, VigilIvory, 3.5f);
        Shape(VigilIvory, new(5, 24), new(3, 10), new(10, 16), new(12, 6), new(16, 24));
        Shape(VigilIvory, new(24, 24), new(28, 6), new(30, 16), new(37, 10), new(35, 24));
        SetLantern(new(20, 21), 1);
        Line(new(8, 31), new(32, 31), VigilBlue, 1.8f);
    }
    private void UnburiedVigil()
    {
        Shape(VigilCloth, new(9, 7), new(16, 4), new(24, 4), new(31, 7), new(33, 35), new(25, 37), new(20, 31), new(15, 37), new(7, 35));
        Shape(VigilIvory, new(8, 10), new(16, 7), new(24, 7), new(32, 10), new(28, 28), new(12, 28));
        for (int row = 0; row < 3; row++)
        {
            Line(new(10, 13 + row * 5), new(17, 16 + row * 4), AshBrass, 1.5f);
            Line(new(30, 13 + row * 5), new(23, 16 + row * 4), AshBrass, 1.5f);
        }
        SetLantern(new(20, 18), .9f);
    }
    private void SetLantern(Vector2 center, float scale)
    {
        Shape(VigilCloth, center + new Vector2(-6, -7) * scale, center + new Vector2(6, -7) * scale,
            center + new Vector2(6, 7) * scale, center + new Vector2(-6, 7) * scale);
        Gem(center, 4 * scale, VigilBlue);
        Line(center + new Vector2(-6, -7) * scale, center + new Vector2(-6, 7) * scale, VigilIvory, 1.5f);
        Line(center + new Vector2(6, -7) * scale, center + new Vector2(6, 7) * scale, VigilIvory, 1.5f);
        Shape(VigilIvory, center + new Vector2(-8, -7) * scale, center + new Vector2(0, -12) * scale, center + new Vector2(8, -7) * scale);
        Line(center + new Vector2(-7, 7) * scale, center + new Vector2(7, 7) * scale, VigilIvory, 2);
    }
    private void ThornmotherMantle()
    {
        foreach (int side in new[] { -1, 1 })
        {
            float x = 20 + side * 9;
            Shape(BriarBark, new(x - 7, 16), new(x - 6, 7), new(x, 12), new(x + 4, 4), new(x + 9, 18), new(x + 7, 31), new(x - 7, 31));
            for (int layer = 0; layer < 3; layer++)
            {
                float y = 16 + layer * 5;
                Shape(BriarLeaf, new(x - 7, y - 3), new(x + 7, y - 5), new(x + 5, y + 5), new(x - 4, y + 7));
                Line(new(x - 4, y), new(x + 3, y + 2), BriarVenom, 1.2f);
            }
            Gem(new(x, 20), 3.3f, BriarVenom);
        }
    }
    private void GravegardenGrasp()
    {
        foreach (int x in new[] { 5, 23 })
        {
            Shape(BriarBark, new(x, 6), new(x + 12, 6), new(x + 12, 22), new(x + 14, 29), new(x + 10, 35), new(x + 1, 33));
            for (int row = 0; row < 3; row++)
                Shape(BriarLeaf, new(x + 1, 8 + row * 5), new(x + 10, 6 + row * 5), new(x + 9, 14 + row * 5), new(x + 3, 15 + row * 5));
            Gem(new(x + 6, 16), 3, BriarVenom);
            for (int finger = 0; finger < 3; finger++)
                Line(new(x + 3 + finger * 3, 27), new(x + 3 + finger * 3, 33), Bone, 1.3f);
        }
    }
    private void CinderpilgrimGirdle()
    {
        Shape(AshChar, new(3, 12), new(37, 12), new(36, 25), new(4, 25));
        Line(new(4, 13), new(36, 13), AshBrass, 2.5f);
        Line(new(4, 24), new(36, 24), AshBrass, 2.5f);
        foreach (int x in new[] { 7, 28 })
        {
            Shape(AshChar, new(x, 25), new(x + 5, 25), new(x + 4, 37), new(x - 1, 34));
            Line(new(x + 1, 28), new(x + 1, 34), AshEmber, 1.5f);
        }
        Disc(new(20, 19), 7, AshBrass); Disc(new(20, 19), 4, AshChar); Gem(new(20, 19), 3.5f, AshEmber);
    }
    private void EndlessEmberBoots()
    {
        foreach (int x in new[] { 4, 22 })
        {
            Shape(AshChar, new(x + 2, 5), new(x + 12, 5), new(x + 11, 23), new(x + 14, 29), new(x + 14, 34), new(x, 34), new(x, 29), new(x + 2, 24));
            Line(new(x + 2, 7), new(x + 12, 7), AshBrass, 2.6f);
            Line(new(x + 1, 34), new(x + 14, 34), AshBrass, 3);
            Disc(new(x + 7, 17), 3.8f, AshBrass); Gem(new(x + 7, 17), 2.6f, AshEmber);
            for (int i = 0; i < 3; i++) Line(new(x + 2 + i * 4, 30), new(x + 3 + i * 4, 26), AshEmber, 1.4f);
        }
    }
}
