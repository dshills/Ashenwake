using Godot;

namespace Ashenwake.Client;

/// <summary>A reusable, cosmetic body map. Selecting a slot never changes the character's anatomy.</summary>
public partial class AnatomyDiagram : Control
{
    private static readonly string[] Slots = ["Mind", "Eyes", "Heart", "Spine", "Arms", "Legs"];
    private static readonly Color Ash = new("567581"), Mint = new("78d5bc"), Gold = new("f4cd88");
    private static readonly Color Body = new("263c48"), BodyOutline = new("607985"), Quiet = new("8da4ae");
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _installed = new(StringComparer.Ordinal);
    private Label _heading = null!, _legend = null!;
    private string _selectedSlot = "Heart";
    private string? _previewSlot;
    private bool _previewOccupied;

    public event Action<string>? SlotSelected;
    public string SelectedSlot => _selectedSlot;

    public override void _Ready()
    {
        Name = "AnatomyDiagram";
        CustomMinimumSize = new(270, 300);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
        _heading = Caption("AnatomyDiagramHeading", "SELECT A BODY SLOT", 11, Quiet);
        _legend = Caption("AnatomyDiagramLegend", "● Installed    ○ Empty", 11, Quiet);
        foreach (string slot in Slots)
        {
            var button = new Button
            {
                Name = "AnatomySlot" + slot,
                ToggleMode = true,
                FocusMode = FocusModeEnum.All,
                MouseFilter = MouseFilterEnum.Stop,
                MouseDefaultCursorShape = CursorShape.PointingHand,
                ClipText = true
            };
            button.AddThemeFontSizeOverride("font_size", 12);
            button.AddThemeColorOverride("font_color", new Color("cfdee3"));
            button.AddThemeColorOverride("font_hover_color", new Color("ffffff"));
            button.AddThemeColorOverride("font_pressed_color", Gold);
            button.AddThemeStyleboxOverride("normal", ButtonStyle(new("12242e"), new("3a5360")));
            button.AddThemeStyleboxOverride("hover", ButtonStyle(new("1b3440"), Mint));
            button.AddThemeStyleboxOverride("pressed", ButtonStyle(new("344039"), Gold));
            button.AddThemeStyleboxOverride("hover_pressed", ButtonStyle(new("414c3f"), Gold));
            button.AddThemeStyleboxOverride("focus", ButtonStyle(Colors.Transparent, new("ecf8ff"), 2));
            button.Pressed += () => SelectSlot(slot);
            _buttons.Add(slot, button);
            AddChild(button);
        }
        Resized += LayoutDiagram;
        LayoutDiagram();
        RefreshButtons();
    }

    public void SetAnatomy(IReadOnlyDictionary<string, string> installed, string selectedSlot,
        string? previewSlot = null, bool previewOccupied = false)
    {
        string selection = Slots.Contains(selectedSlot, StringComparer.Ordinal) ? selectedSlot : "Heart";
        string? preview = previewSlot is not null && Slots.Contains(previewSlot, StringComparer.Ordinal) ? previewSlot : null;
        bool changed = _selectedSlot != selection || _previewSlot != preview || _previewOccupied != previewOccupied;
        foreach (string slot in Slots)
        {
            string fragment = installed.TryGetValue(slot, out string? value) ? value : "";
            string previous = _installed.GetValueOrDefault(slot, "");
            changed |= previous != fragment;
            if (fragment.Length == 0) _installed.Remove(slot); else _installed[slot] = fragment;
        }
        _selectedSlot = selection;
        _previewSlot = preview;
        _previewOccupied = previewOccupied;
        if (!changed || !IsNodeReady()) return;
        RefreshButtons();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Size.X <= 0 || Size.Y <= 0) return;
        // The etched guide and the body share one coordinate system; button text retains its actual pixel size.
        DrawRect(new(Vector2.Zero, Size), new("0e1c25"));
        Stroke([new(16, 31), new(284, 31)], new("2b414c"), 1);
        Stroke([new(16, 353), new(284, 353)], new("2b414c"), 1);
        Stroke([new(150, 38), new(150, 341)], new("233a45"), 1);
        for (int y = 58; y < 340; y += 28)
        {
            Stroke([new(105, y), new(109, y)], new("354d58"), 1);
            Stroke([new(191, y), new(195, y)], new("354d58"), 1);
        }
        DrawArc(Point(150, 183), 76 * DiagramScale, 0, Mathf.Tau, 64, new("1c333f"), 1, true);
        DrawArc(Point(150, 183), 88 * DiagramScale, -.75f, .75f, 24, new("2b414c"), 1, true);
        DrawArc(Point(150, 183), 88 * DiagramScale, Mathf.Pi - .75f, Mathf.Pi + .75f, 24, new("2b414c"), 1, true);

        Shape([new(139, 85), new(161, 85), new(162, 108), new(138, 108)], Body, BodyOutline);
        Shape([new(134, 43), new(143, 38), new(157, 38), new(166, 43), new(169, 67), new(163, 84), new(150, 93), new(137, 84), new(131, 67)], Body, BodyOutline);
        Shape([new(129, 102), new(139, 100), new(150, 105), new(161, 100), new(171, 102), new(183, 113), new(177, 151), new(170, 178), new(173, 207), new(162, 228), new(150, 233), new(138, 228), new(127, 207), new(130, 178), new(123, 151), new(117, 113)], Body, BodyOutline);
        Shape([new(121, 106), new(127, 121), new(117, 154), new(112, 182), new(105, 206), new(96, 208), new(91, 199), new(98, 170), new(104, 137), new(109, 117)], RegionFill("Arms"), RegionOutline("Arms"));
        Shape([new(179, 106), new(173, 121), new(183, 154), new(188, 182), new(195, 206), new(204, 208), new(209, 199), new(202, 170), new(196, 137), new(191, 117)], RegionFill("Arms"), RegionOutline("Arms"));
        Shape([new(130, 208), new(149, 227), new(145, 267), new(139, 297), new(141, 330), new(136, 338), new(118, 338), new(117, 332), new(124, 323), new(122, 287), new(124, 262)], RegionFill("Legs"), RegionOutline("Legs"));
        Shape([new(170, 208), new(151, 227), new(155, 267), new(161, 297), new(159, 330), new(164, 338), new(182, 338), new(183, 332), new(176, 323), new(178, 287), new(176, 262)], RegionFill("Legs"), RegionOutline("Legs"));

        // A restrained bone pattern keeps the image readable as a person without competing with the six organs.
        foreach (int direction in new[] { -1, 1 })
        {
            for (int rib = 0; rib < 4; rib++)
                Stroke([new(150 + direction * 5, 120 + rib * 12), new(150 + direction * 20, 123 + rib * 11), new(150 + direction * (26 - rib * 2), 117 + rib * 12)], new("496570"), 1.2f);
            Stroke([new(150 + direction * 19, 177), new(150 + direction * 13, 202), new(150 + direction * 3, 213)], new("496570"), 1.2f);
            Stroke([new(150 + direction * 31, 123), new(150 + direction * 40, 162), new(150 + direction * 49, 194)], new("57727b"), 1.2f);
            Stroke([new(150 + direction * 13, 239), new(150 + direction * 20, 277), new(150 + direction * 17, 320)], new("57727b"), 1.2f);
        }

        Shape([new(137, 54), new(140, 47), new(148, 44), new(150, 48), new(153, 44), new(161, 47), new(164, 54), new(160, 61), new(153, 64), new(150, 61), new(147, 64), new(140, 61)], RegionFill("Mind", true), RegionOutline("Mind"));
        Stroke([new(150, 49), new(150, 58)], RegionOutline("Mind"), 1);
        Stroke([new(137, 73), new(146, 75), new(150, 72), new(154, 75), new(163, 73)], RegionOutline("Eyes"), 2);
        DrawCircle(Point(141, 73), 2.2f * DiagramScale, RegionOutline("Eyes"));
        DrawCircle(Point(159, 73), 2.2f * DiagramScale, RegionOutline("Eyes"));
        for (int y = 114; y <= 200; y += 11)
        {
            Shape([new(147, y), new(153, y), new(154, y + 5), new(150, y + 8), new(146, y + 5)], RegionFill("Spine", true), RegionOutline("Spine"));
        }
        Shape([new(151, 138), new(156, 135), new(159, 139), new(164, 136), new(169, 141), new(168, 149), new(157, 161), new(149, 153), new(146, 145)], RegionFill("Heart", true), RegionOutline("Heart"));

        foreach (string slot in Slots) DrawConnector(slot);
        if (_previewSlot is not null)
        {
            Vector2 anchor = Point(AnchorFor(_previewSlot));
            DrawArc(anchor, 11 * DiagramScale, 0, Mathf.Tau, 36, Gold, 1.5f, true);
            if (_previewOccupied) DrawCircle(anchor, 3 * DiagramScale, Gold);
        }
    }

    private void SelectSlot(string slot)
    {
        _selectedSlot = slot;
        RefreshButtons();
        QueueRedraw();
        SlotSelected?.Invoke(slot);
    }

    private void RefreshButtons()
    {
        foreach (var (slot, button) in _buttons)
        {
            bool occupied = _installed.TryGetValue(slot, out string? fragment);
            button.Text = (occupied ? "● " : "○ ") + slot;
            button.SetPressedNoSignal(slot == _selectedSlot);
            button.TooltipText = $"{slot} · {(occupied ? fragment : "Empty slot")}\nSelect to inspect compatible fragments.";
            button.AddThemeColorOverride("font_color", occupied ? Mint : new Color("cfdee3"));
        }
    }

    private void LayoutDiagram()
    {
        if (_heading is null) return;
        _heading.Position = new(0, 6); _heading.Size = new(Size.X, 20);
        _legend.Position = new(0, Size.Y - 27); _legend.Size = new(Size.X, 22);
        foreach (var (slot, button) in _buttons)
        {
            var layout = SlotLayout(slot);
            float width = Math.Clamp(Size.X * .277f, 72, 108);
            button.Position = new(layout.Left ? 7 : Size.X - width - 7, Size.Y * layout.Y / 390);
            button.Size = new(width, Math.Clamp(Size.Y * .092f, 30, 38));
        }
        QueueRedraw();
    }

    private void DrawConnector(string slot)
    {
        if (!_buttons.TryGetValue(slot, out Button? button)) return;
        bool left = SlotLayout(slot).Left;
        Vector2 start = button.Position + new Vector2(left ? button.Size.X : 0, button.Size.Y / 2);
        Vector2 target = Point(AnchorFor(slot));
        float elbow = Point(left ? 101 : 199, 0).X;
        Color color = slot == _selectedSlot ? Gold : _installed.ContainsKey(slot) ? Mint : Ash;
        Vector2[] path = [start, new(elbow, start.Y), new(elbow, target.Y), target];
        DrawPolyline(path, new(color, slot == _selectedSlot ? .95f : .65f), slot == _selectedSlot ? 1.6f : 1, true);
        DrawCircle(target, 5 * DiagramScale, new("0e1c25"));
        DrawArc(target, 4 * DiagramScale, 0, Mathf.Tau, 20, color, 1.2f, true);
        if (_installed.ContainsKey(slot)) DrawCircle(target, 2 * DiagramScale, color);
    }

    private static (bool Left, float Y) SlotLayout(string slot) => slot switch
    {
        "Mind" => (true, 38),
        "Eyes" => (false, 77),
        "Arms" => (true, 115),
        "Heart" => (false, 151),
        "Spine" => (true, 198),
        _ => (false, 273)
    };

    private static Vector2 AnchorFor(string slot) => slot switch
    {
        "Mind" => new(150, 54),
        "Eyes" => new(159, 73),
        "Heart" => new(158, 147),
        "Spine" => new(150, 194),
        "Arms" => new(112, 148),
        _ => new(170, 286)
    };

    private Color RegionFill(string slot, bool organ = false)
    {
        if (slot == _selectedSlot) return new("686049");
        if (_installed.ContainsKey(slot)) return organ ? new("367565") : new("284f4d");
        return organ ? new("3a535f") : Body;
    }

    private Color RegionOutline(string slot) => slot == _selectedSlot ? Gold : _installed.ContainsKey(slot) ? Mint : BodyOutline;
    private float DiagramScale => Math.Min(Size.X / 300, Size.Y / 390);
    private Vector2 Point(float x, float y) => new(x * Size.X / 300, y * Size.Y / 390);
    private Vector2 Point(Vector2 point) => Point(point.X, point.Y);

    private void Stroke(Vector2[] points, Color color, float width)
        => DrawPolyline(points.Select(Point).ToArray(), color, width, true);

    private void Shape(Vector2[] points, Color fill, Color outline)
    {
        Vector2[] scaled = points.Select(Point).ToArray();
        DrawColoredPolygon(scaled, fill);
        DrawPolyline([.. scaled, scaled[0]], outline, 1.15f, true);
    }

    private Label Caption(string name, string text, int size, Color color)
    {
        var label = new Label
        {
            Name = name,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        AddChild(label);
        return label;
    }

    private static StyleBoxFlat ButtonStyle(Color background, Color border, int width = 1) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = width,
        BorderWidthRight = width,
        BorderWidthTop = width,
        BorderWidthBottom = width,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 5,
        ContentMarginRight = 5,
        ContentMarginTop = 4,
        ContentMarginBottom = 4
    };
}
