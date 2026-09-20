using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Small equipment silhouettes drawn locally; inspecting an item creates no world or preview resources.</summary>
public partial class GearItemIcon : Control
{
    private enum Silhouette { Sword, Dagger, Axe, Ashcleaver, Hammer, Pike, Staff, BoneStaff, Shield, Focus, Skull, Helmet, Hood, Plate, Robe, Tunic, Shoulders, Gloves, Belt, Legs, Boots, Ring, Amulet }
    private static readonly Color Steel = new("c2d1d3"), SteelShade = new("536e7d"), Ink = new("15252f");
    private static readonly Color Leather = new("886048"), Bone = new("e4d5b1"), Ember = new("ff9166");
    private static readonly Color EmptyFill = new("30465099"), EmptyEdge = new("6c8793aa");
    private string _definitionId = "", _discipline = "";
    private Silhouette _silhouette = Silhouette.Sword;
    private bool _empty = true;
    private ArmorPalette _armor = ArmorPalette.For("Vanguard");
    private Color _accent = new("bdc6c8"), _cloth = new("354d57");

    public string IconKey { get; private set; } = "";

    public GearItemIcon()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new(32, 32);
    }

    public void Configure(string definitionId, EquipmentSlot slot, string discipline, ItemRarity? rarity)
    {
        string id = definitionId.Trim().ToLowerInvariant(), form = discipline.Trim().ToLowerInvariant();
        string itemName = id.Split('.').Last();
        Silhouette silhouette = Classify(rarity.HasValue ? itemName : "", slot, form);
        string key = $"{id}|{slot}|{form}|{rarity?.ToString() ?? "Empty"}|{silhouette}";
        if (IconKey == key) return;
        IconKey = key;
        _definitionId = itemName;
        _discipline = form;
        _armor = ArmorPalette.For(form);
        _silhouette = silhouette;
        _empty = !rarity.HasValue;
        _accent = rarity switch
        {
            ItemRarity.Tempered => new("7dcbae"),
            ItemRarity.Rare => new("6cafff"),
            ItemRarity.Relic => new("b98bf3"),
            ItemRarity.Legendary => new("ffd172"),
            ItemRarity.Godwrought => new("ff8559"),
            _ => new("bdc6c8")
        };
        _cloth = form switch
        {
            "veilwalker" => new("55506b"),
            "arcanist" => new("435f8c"),
            "gravecaller" => new("56636a"),
            "warden" => new("6c684b"),
            _ => new("45626f")
        };
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) QueueRedraw();
    }

    public override void _Draw()
    {
        float side = MathF.Min(42, MathF.Min(Size.X, Size.Y));
        if (side <= 0) return;
        // All shapes share a forty-unit drawing space and retain a two-unit gutter at compact sizes.
        DrawSetTransform((Size - Vector2.One * side) * .5f, 0, Vector2.One * (side / 40));
        switch (_silhouette)
        {
            case Silhouette.Sword: Sword(false); break;
            case Silhouette.Dagger: Sword(true); break;
            case Silhouette.Axe: Axe(false); break;
            case Silhouette.Ashcleaver: Axe(true); break;
            case Silhouette.Hammer: Hammer(); break;
            case Silhouette.Pike: Pike(); break;
            case Silhouette.Staff: Staff(false); break;
            case Silhouette.BoneStaff: Staff(true); break;
            case Silhouette.Shield: Shield(); break;
            case Silhouette.Focus: Focus(); break;
            case Silhouette.Skull: Skull(new(20, 20), 1.3f); break;
            case Silhouette.Helmet: Helmet(false); break;
            case Silhouette.Hood: Helmet(true); break;
            case Silhouette.Plate: Chest(false, false); break;
            case Silhouette.Robe: Chest(true, false); break;
            case Silhouette.Tunic: Chest(false, true); break;
            case Silhouette.Shoulders: Shoulders(); break;
            case Silhouette.Gloves: Gloves(); break;
            case Silhouette.Belt: Belt(); break;
            case Silhouette.Legs: Legs(); break;
            case Silhouette.Boots: Boots(); break;
            case Silhouette.Ring: Ring(); break;
            case Silhouette.Amulet: Amulet(); break;
        }
    }

    private static Silhouette Classify(string id, EquipmentSlot slot, string discipline)
    {
        if (slot == EquipmentSlot.MainHand)
        {
            return id switch
            {
                "ashcleaver" => Silhouette.Ashcleaver,
                "ash_axe" => Silhouette.Axe,
                "oath_hammer" => Silhouette.Hammer,
                "pilgrim_pike" => Silhouette.Pike,
                "greatstaff" => Silhouette.Staff,
                "cinder_edge" => Silhouette.Sword,
                "" or "starter_mainhand" => discipline switch
                {
                    "veilwalker" => Silhouette.Dagger,
                    "arcanist" => Silhouette.Staff,
                    "gravecaller" => Silhouette.BoneStaff,
                    "warden" => Silhouette.Pike,
                    _ => Silhouette.Sword
                },
                _ => Silhouette.Sword
            };
        }
        if (slot == EquipmentSlot.OffHand)
        {
            if (id.Contains("focus", StringComparison.Ordinal) || id.Contains("tome", StringComparison.Ordinal)) return Silhouette.Focus;
            return discipline switch
            {
                "veilwalker" => Silhouette.Dagger,
                "gravecaller" => Silhouette.Skull,
                "arcanist" or "warden" => Silhouette.Focus,
                _ => Silhouette.Shield
            };
        }
        return slot switch
        {
            EquipmentSlot.Head => discipline is "veilwalker" or "arcanist" or "gravecaller" or "warden" ? Silhouette.Hood : Silhouette.Helmet,
            EquipmentSlot.Chest => id switch
            {
                "ash_weave" or "serath_shroud" => Silhouette.Robe,
                "march_plate" or "oath_plate" => Silhouette.Plate,
                _ => discipline switch { "arcanist" or "gravecaller" => Silhouette.Robe, "veilwalker" or "warden" => Silhouette.Tunic, _ => Silhouette.Plate }
            },
            EquipmentSlot.Shoulders => Silhouette.Shoulders,
            EquipmentSlot.Gloves => Silhouette.Gloves,
            EquipmentSlot.Belt => Silhouette.Belt,
            EquipmentSlot.Legs => Silhouette.Legs,
            EquipmentSlot.Boots => Silhouette.Boots,
            EquipmentSlot.Ring1 or EquipmentSlot.Ring2 => Silhouette.Ring,
            EquipmentSlot.Amulet => Silhouette.Amulet,
            _ => Silhouette.Sword
        };
    }

    private void Sword(bool dagger)
    {
        Line(new(9, 34), new(17, 24), Leather, 4);
        Shape(Steel, new(15, 23), new(dagger ? 25 : 28, dagger ? 10 : 5), new(33, dagger ? 7 : 3), new(31, 14), new(20, 28));
        Line(new(18, 25), new(30, dagger ? 10 : 6), SteelShade, 1.4f);
        Line(new(11, 22), new(23, 30), _accent, 3);
        Disc(new(9, 34), 2.3f, Steel);
        if (_definitionId == "cinder_edge") Line(new(22, 24), new(31, 10), Ember, 1.4f);
    }

    private void Axe(bool awakened)
    {
        Line(new(11, 35), new(23, 7), awakened ? Bone : Leather, 4);
        Shape(SteelShade, new(18, 11), new(26, 7), new(35, 9), new(36, 20), new(31, 25), new(23, 20), new(22, 15), new(14, 15));
        Shape(Steel, new(30, 9), new(35, 9), new(36, 20), new(31, 25), new(29, 20), new(32, 17));
        Line(new(17, 20), new(22, 22), _accent, 2);
        if (!awakened) return;
        Shape(Bone, new(21, 9), new(23, 3), new(27, 9));
        Line(new(33, 11), new(34, 19), Ember, 1.8f);
        Disc(new(25, 14), 2.4f, Ember);
    }

    private void Hammer()
    {
        Line(new(12, 35), new(23, 13), Leather, 4);
        Shape(SteelShade, new(18, 5), new(35, 13), new(30, 23), new(13, 15));
        Shape(Steel, new(18, 5), new(22, 7), new(17, 17), new(13, 15));
        Shape(Steel, new(31, 11), new(35, 13), new(30, 23), new(26, 21));
        Line(new(24, 10), new(21, 17), _accent, 2.2f);
    }

    private void Pike()
    {
        Line(new(9, 36), new(26, 10), Leather, 3);
        Shape(Steel, new(31, 3), new(30, 16), new(26, 14), new(21, 13));
        Line(new(26, 14), new(30, 6), SteelShade, 1.2f);
        Line(new(20, 17), new(26, 21), _accent, 2.4f);
        Shape(_cloth, new(21, 20), new(25, 22), new(22, 29), new(20, 26));
    }

    private void Staff(bool bone)
    {
        Line(new(12, 35), new(24, 11), bone ? Bone : Leather, 3.6f);
        if (bone) Skull(new(26, 10), .67f);
        else
        {
            Arc(new(26, 10), 6.4f, 0, Mathf.Tau, Bone, 2.4f);
            Gem(new(26, 10), 3.5f, _accent);
        }
        Line(new(18, 20), new(23, 22), _accent, 2.4f);
        Line(new(11, 33), new(15, 35), Steel, 2);
    }

    private void Shield()
    {
        Shape(Steel, new(7, 7), new(20, 4), new(33, 7), new(31, 25), new(20, 36), new(9, 25));
        Shape(_cloth, new(11, 10), new(20, 8), new(29, 10), new(27, 24), new(20, 31), new(13, 24));
        Line(new(20, 10), new(20, 29), _accent, 2);
        Line(new(13, 17), new(27, 17), _accent, 2);
        Disc(new(20, 17), 3.2f, Steel);
    }

    private void Focus()
    {
        if (_discipline == "warden")
        {
            Arc(new(20, 17), 10, -.25f, Mathf.Pi + .25f, Leather, 3);
            Line(new(10, 17), new(16, 31), Bone, 2.3f);
            Line(new(30, 17), new(24, 31), Bone, 2.3f);
            Gem(new(20, 19), 6, _accent);
            return;
        }
        Shape(Bone, new(9, 8), new(29, 6), new(32, 29), new(12, 33));
        Shape(_cloth, new(7, 7), new(28, 5), new(30, 28), new(10, 31));
        Line(new(11, 9), new(13, 28), Steel, 1.4f);
        Gem(new(20, 18), 5.8f, _accent);
        Line(new(29, 23), new(33, 23), Leather, 3);
    }

    private void Skull(Vector2 at, float scale)
    {
        Vector2 P(float x, float y) => at + new Vector2(x, y) * scale;
        Shape(Bone, P(-7, -7), P(0, -10), P(7, -7), P(9, 0), P(5, 5), P(4, 10), P(-4, 10), P(-5, 5), P(-9, 0));
        Disc(P(-3.7f, 0), 2.2f * scale, Ink);
        Disc(P(3.7f, 0), 2.2f * scale, Ink);
        Line(P(0, 3), P(0, 5), _accent, 2 * scale);
        Line(P(-3, 7), P(3, 7), SteelShade, scale);
    }

    private void Helmet(bool hood)
    {
        Shape(hood ? _cloth : Steel, new(8, 16), new(12, 7), new(20, 3), new(28, 7), new(32, 16), new(31, 32), new(24, 36), new(20, 31), new(16, 36), new(9, 32));
        if (hood)
        {
            Shape(Ink, new(20, 10), new(27, 19), new(25, 29), new(20, 31), new(15, 29), new(13, 19));
            Line(new(20, 5), new(20, 10), _accent, 2);
        }
        else
        {
            Shape(Ink, new(11, 18), new(29, 18), new(27, 24), new(13, 24));
            Line(new(20, 5), new(20, 31), SteelShade, 3);
            Line(new(20, 7), new(20, 15), _accent, 1.6f);
            Line(new(12, 28), new(16, 31), SteelShade, 1.3f);
            Line(new(28, 28), new(24, 31), SteelShade, 1.3f);
        }
    }

    private void Chest(bool robe, bool tunic)
    {
        Color fabric = robe || tunic ? _cloth : SteelShade;
        Shape(fabric, new(13, 5), new(17, 8), new(23, 8), new(27, 5), new(35, 12), new(32, 21), new(27, 18), new(robe ? 31 : 28, 35), new(robe ? 9 : 12, 35), new(13, 18), new(8, 21), new(5, 12));
        if (!robe && !tunic)
        {
            Shape(Steel, new(13, 11), new(20, 14), new(27, 11), new(26, 23), new(20, 26), new(14, 23));
            Line(new(20, 15), new(20, 24), _accent, 2);
            Line(new(13, 29), new(27, 29), Steel, 3);
        }
        else
        {
            Line(new(15, 8), new(20, 18), Bone, 2);
            Line(new(25, 8), new(20, 18), Bone, 2);
            Line(new(20, 18), new(20, 33), SteelShade, 1.5f);
            Line(new(13, 24), new(27, 24), Leather, 3);
            Gem(new(20, 24), 2.6f, _accent);
            if (robe)
            {
                Line(new(15, 27), new(13, 33), Bone, 1);
                Line(new(25, 27), new(27, 33), Bone, 1);
            }
        }
    }

    private void Shoulders()
    {
        foreach (int x in new[] { 4, 22 })
        {
            Shape(new(_armor.Cloth), new(x, 15), new(x + 14, 15), new(x + 13, 32), new(x + 9, 29), new(x + 5, 33), new(x + 1, 30));
            Shape(new(_armor.Shell), new(x, 15), new(x + 4, 8), new(x + 11, 8), new(x + 14, 15), new(x + 12, 21), new(x + 2, 21));
            if (_discipline is "gravecaller" or "warden")
                for (int i = 0; i < 3; i++)
                    Shape(_discipline == "gravecaller" ? Bone : new(_armor.Shell), new(x + 2 + i * 4, 11), new(x + 3 + i * 4, 3 + i), new(x + 6 + i * 4, 12));
            Arc(new(x + 7, 16), 3, 0, Mathf.Tau, new(_armor.Trim), 1.3f);
            Line(new(x + 7, 13), new(x + 7, 19), new(_armor.Trim), 1);
            for (int i = 0; i < 3; i++)
                Line(new(x + 3 + i * 4, 24), new(x + 3 + i * 4, 28), new(_armor.Trim), 1);
        }
    }

    private void Gloves()
    {
        foreach (int x in new[] { 4, 22 })
        {
            Shape(new(ArmorPalette.Leather), new(x + 2, 8), new(x + 10, 8), new(x + 11, 19), new(x + 14, 17), new(x + 15, 20), new(x + 11, 26), new(x + 10, 33), new(x + 1, 33), new(x, 18));
            Shape(new(_armor.Shell), new(x + 1, 6), new(x + 11, 6), new(x + 11, 18), new(x + 1, 18));
            for (int i = 0; i < 3; i++)
            {
                Line(new(x + 2, 9 + i * 3), new(x + 10, 8 + i * 3), new(_armor.Trim), 1.3f);
                Line(new(x + 3 + i * 3, 22), new(x + 3 + i * 3, 25), Bone, 2);
            }
            Line(new(x + 5, 28), new(x + 5, 31), SteelShade, 1);
        }
    }

    private void Belt()
    {
        Shape(new(ArmorPalette.Leather), new(3, 12), new(12, 10), new(28, 10), new(37, 12), new(36, 24), new(28, 22), new(12, 22), new(4, 24));
        foreach (int x in new[] { 10, 25 })
        {
            Shape(new(_armor.Cloth), new(x, 20), new(x + 5, 20), new(x + 7, 36), new(x + 1, 34));
            for (int i = 0; i < 3; i++) Line(new(x + 2, 25 + i * 3), new(x + 5, 25 + i * 3), new(_armor.Trim), 1);
        }
        Shape(new(_armor.Trim), new(14, 10), new(26, 10), new(26, 24), new(14, 24));
        Shape(new(_armor.Cloth), new(17, 13), new(23, 13), new(23, 21), new(17, 21));
        Line(new(20, 14), new(20, 21), Bone, 1.4f);
        Line(new(18, 17), new(23, 17), Bone, 1.4f);
        Disc(new(7, 18), 1, Bone);
        Disc(new(33, 18), 1, Bone);
    }

    private void Legs()
    {
        Shape(new(_armor.Cloth), new(10, 5), new(30, 5), new(30, 18), new(28, 36), new(21, 36), new(20, 20), new(19, 36), new(12, 36), new(10, 18));
        foreach (int x in new[] { 11, 22 })
        {
            Shape(new(_armor.Shell), new(x, 11), new(x + 7, 11), new(x + 7, 20), new(x + 1, 20));
            Shape(new(_armor.Shell), new(x, 23), new(x + 7, 23), new(x + 7, 29), new(x + 3.5f, 32), new(x, 29));
            Line(new(x + 1, 26), new(x + 3.5f, 28), new(_armor.Trim), 1.5f);
            Line(new(x + 6, 26), new(x + 3.5f, 28), new(_armor.Trim), 1.5f);
        }
        Line(new(9, 18), new(9, 31), new(_armor.Trim), 1.4f);
        Line(new(31, 18), new(31, 31), new(_armor.Trim), 1.4f);
    }

    private void Boots()
    {
        foreach (int x in new[] { 4, 22 })
        {
            Shape(new(ArmorPalette.Leather), new(x + 3, 6), new(x + 12, 6), new(x + 11, 26), new(x + 15, 28), new(x + 15, 33), new(x, 33), new(x, 28), new(x + 3, 24));
            Line(new(x + 3, 8), new(x + 12, 8), new(_armor.Shell), 4);
            Shape(new(_armor.Shell), new(x + 2, 27), new(x + 10, 27), new(x + 14, 29), new(x + 14, 32), new(x + 1, 32));
            Line(new(x + 1, 33), new(x + 14, 33), new(ArmorPalette.Ember), 1.7f);
            for (int i = 0; i < 3; i++)
                Line(new(x + 4, 13 + i * 4), new(x + 10, 15 + i * 4), new(_armor.Trim), 1.2f);
        }
    }

    private void Ring()
    {
        Arc(new(20, 23), 10, 0, Mathf.Tau, SteelShade, 5);
        Arc(new(20, 23), 10, -.35f, Mathf.Pi + .55f, Steel, 2.3f);
        Gem(new(20, 12), 6.2f, SteelShade);
        Gem(new(20, 11), 4, _accent);
        if (_definitionId == "echo_ring") Arc(new(20, 23), 6, .3f, Mathf.Pi - .3f, _accent, 1);
    }

    private void Amulet()
    {
        Arc(new(20, 15), 10, -.6f, Mathf.Pi + .6f, Steel, 1.8f);
        Line(new(12, 9), new(15, 5), Steel, 1.8f);
        Line(new(28, 9), new(25, 5), Steel, 1.8f);
        if (_definitionId == "ember_lens")
        {
            Disc(new(20, 28), 7, SteelShade);
            Disc(new(20, 28), 4.6f, Ember);
            Line(new(18, 26), new(21, 26), Bone, 1);
        }
        else if (_definitionId is "stone_seal" or "war_token")
        {
            Shape(_definitionId == "stone_seal" ? SteelShade : Bone, new(14, 23), new(26, 23), new(26, 33), new(20, 36), new(14, 33));
            Line(new(17, 27), new(23, 27), _accent, 2);
            Line(new(20, 27), new(20, 32), Ink, 1.3f);
        }
        else
        {
            Gem(new(20, 28), 8, SteelShade);
            Gem(new(20, 28), 5.2f, _accent);
        }
    }

    private void Shape(Color fill, params Vector2[] points)
    {
        DrawColoredPolygon(points, _empty ? EmptyFill : fill);
        for (int i = 0; i < points.Length; i++)
            DrawLine(points[i], points[(i + 1) % points.Length], _empty ? EmptyEdge : Ink, 1, true);
    }

    private void Line(Vector2 from, Vector2 to, Color color, float width)
        => DrawLine(from, to, _empty ? EmptyEdge : color, width, true);

    private void Arc(Vector2 center, float radius, float start, float end, Color color, float width)
        => DrawArc(center, radius, start, end, 24, _empty ? EmptyEdge : color, width, true);

    private void Disc(Vector2 center, float radius, Color color)
    {
        DrawCircle(center, radius, _empty ? EmptyFill : color);
        Arc(center, radius, 0, Mathf.Tau, Ink, 1);
    }

    private void Gem(Vector2 center, float radius, Color color)
        => Shape(color, center + new Vector2(0, -radius), center + new Vector2(radius * .75f, 0), center + new Vector2(0, radius), center + new Vector2(-radius * .75f, 0));
}
