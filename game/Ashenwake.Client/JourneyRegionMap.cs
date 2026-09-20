using Godot;

namespace Ashenwake.Client;

public sealed record JourneyRegionDisplay(int Number, string Name, bool Unlocked, bool Completed, bool Current);

/// <summary>An inspectable campaign route. Selection never travels or changes campaign state.</summary>
public partial class JourneyRegionMap : Control
{
    public event Action<int>? RegionSelected;
    public int SelectedRegion { get; private set; }
    private static string PlaceholderName(int number) => number == 0 ? "Greyhaven" : "Region " + number;
    private static readonly Color[] Accents = [new("e5c897"), new("adc7cf"), new("a4c68d"), new("e9a079"), new("a4c6e5"), new("b9a5df")];
    private static readonly int[] Columns = [0, 1, 1, 0, 0, 1], Rows = [0, 0, 1, 1, 2, 2];
    private readonly List<Button> _buttons = [];
    private readonly List<Label> _names = [], _statuses = [], _numbers = [];
    private readonly Rect2[] _bounds = new Rect2[6];
    private JourneyRegionDisplay[] _regions = Enumerable.Range(1, 5).Select(i => new JourneyRegionDisplay(i, PlaceholderName(i), false, false, false)).ToArray();
    private readonly StyleBoxFlat _background = Card(new("102028"), new("53676d"), 1);
    private readonly StyleBoxFlat[] _cards = Enumerable.Range(0, 6).Select(_ => Card(new("182b33"), new("52676e"), 1)).ToArray();
    private Label _heading = null!, _legend = null!, _hint = null!;
    private bool _inHub = true;

    public JourneyRegionMap()
    {
        Name = "JourneyRegionMap"; CustomMinimumSize = new(380, 330);
        SizeFlagsHorizontal = SizeFlags.ExpandFill; SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop; FocusMode = FocusModeEnum.None;
    }

    public override void _Ready() { EnsureChildren(); LayoutRegions(); }

    public void SetRegions(IReadOnlyList<JourneyRegionDisplay> regions, bool inHub, int selectedRegion)
    {
        EnsureChildren();
        var next = Enumerable.Range(1, 5).Select(i => regions.LastOrDefault(r => r.Number == i) ?? new(i, PlaceholderName(i), false, false, false)).ToArray();
        selectedRegion = Math.Clamp(selectedRegion, 0, 5);
        bool changed = !_regions.SequenceEqual(next) || _inHub != inHub || SelectedRegion != selectedRegion;
        _regions = next; _inHub = inHub; SelectedRegion = selectedRegion;
        if (changed) { RefreshButtons(); QueueRedraw(); }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized) { LayoutRegions(); QueueRedraw(); }
    }

    private void EnsureChildren()
    {
        if (_buttons.Count > 0) return;
        _heading = Caption("JourneyMapHeading", "EDRATH", 13, new("e9dfc8")); AddChild(_heading);
        _legend = Caption("JourneyMapLegend", "FIVE REGIONS · ONE JOURNEY", 9, new("9ab3b7")); _legend.HorizontalAlignment = HorizontalAlignment.Right; AddChild(_legend);
        _hint = Caption("JourneyMapHint", "Select a region to inspect your journey.", 10, new("a6bdbe")); AddChild(_hint);
        for (int i = 0; i < 6; i++)
        {
            int number = i;
            var button = new Button { Name = "JourneyRegion" + i, ToggleMode = true, ClipText = true, FocusMode = FocusModeEnum.All, MouseFilter = MouseFilterEnum.Stop };
            // Keep the complete native label for assistive technology and text-based diagnostics.
            // The three visible labels keep each state legible at the narrow map size.
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color", "font_outline_color" })
                button.AddThemeColorOverride(state, Colors.Transparent);
            button.AddThemeFontSizeOverride("font_size", 10);
            button.AddThemeStyleboxOverride("normal", Card(Colors.Transparent, Colors.Transparent, 0));
            button.AddThemeStyleboxOverride("hover", Card(Colors.Transparent, Accents[i], 1));
            button.AddThemeStyleboxOverride("pressed", Card(Colors.Transparent, new("f0dfb5"), 2));
            button.AddThemeStyleboxOverride("hover_pressed", Card(Colors.Transparent, new("fff0ca"), 2));
            button.AddThemeStyleboxOverride("focus", Card(Colors.Transparent, new("f0dfb5"), 2));
            button.Pressed += () =>
            {
                SelectedRegion = number; RefreshButtons(); QueueRedraw(); RegionSelected?.Invoke(number);
            };
            AddChild(button); _buttons.Add(button);
            var act = Caption("JourneyRegionNumber" + i, "", 9, Accents[i]); button.AddChild(act); _numbers.Add(act);
            var name = Caption("JourneyRegionName" + i, "", 11, new("e6eded")); name.AutowrapMode = TextServer.AutowrapMode.WordSmart; name.MaxLinesVisible = 2; button.AddChild(name); _names.Add(name);
            var status = Caption("JourneyRegionStatus" + i, "", 10, new("b2c4c8")); button.AddChild(status); _statuses.Add(status);
        }
        RefreshButtons(); LayoutRegions();
    }

    private void RefreshButtons()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            var region = Region(i); bool current = i == 0 ? _inHub : !_inHub && region.Current;
            bool available = i == 0 || region.Unlocked;
            string state = current ? region.Completed ? "Here · Cleared" : "You are here" : region.Completed ? "Cleared" : available ? i == 0 ? "Safe haven" : "Unlocked" : "Locked";
            _numbers[i].Text = i == 0 ? "HOME" : "ACT " + i;
            _names[i].Text = region.Name;
            _names[i].AddThemeColorOverride("font_color", available ? new Color("e6eded") : new("a5b2bc"));
            _statuses[i].Text = state;
            _statuses[i].AddThemeColorOverride("font_color", current ? new Color("f0d8a6") : region.Completed ? new("a4d3b5") : available ? new("b2c4c8") : new("a4aebb"));
            _buttons[i].SetPressedNoSignal(SelectedRegion == i);
            _buttons[i].Text = region.Name + " · " + (i == 0 ? "Greyhaven hub" : "Act " + i) + " · " + state;
            _buttons[i].TooltipText = _buttons[i].Text + (region.Completed ? " · Completed region" : "") + ".\nSelect to inspect destinations and progress." + (!available ? " This region is not yet unlocked." : "");
            _cards[i].BgColor = available ? new Color("172c34").Lerp(Accents[i], .07f) : new("14232c");
            _cards[i].BorderColor = current ? new("ddc799") : available ? Accents[i].Darkened(.45f) : new("43545f");
        }
    }

    private JourneyRegionDisplay Region(int i) => i == 0 ? new(0, PlaceholderName(0), true, false, _inHub) : _regions[i - 1];

    private void LayoutRegions()
    {
        if (_buttons.Count != 6) return;
        float width = Math.Max(380, Size.X), height = Math.Max(330, Size.Y);
        float cardWidth = Math.Min(250, (width - 76) * .5f), cardHeight = Math.Clamp((height - 112) / 3, 72, 86);
        float top = 43, rowStep = (height - 30 - top - cardHeight) * .5f;
        _heading.Position = new(18, 10); _heading.Size = new(90, 20);
        _legend.Position = new(110, 11); _legend.Size = new(width - 128, 18);
        _hint.Position = new(18, height - 22); _hint.Size = new(width - 36, 17);
        for (int i = 0; i < 6; i++)
        {
            _bounds[i] = new(new(Columns[i] == 0 ? 24 : width - 24 - cardWidth, top + Rows[i] * rowStep), new(cardWidth, cardHeight));
            _buttons[i].Position = _bounds[i].Position; _buttons[i].Size = _bounds[i].Size;
            float textWidth = cardWidth - 53;
            _numbers[i].Position = new(46, 6); _numbers[i].Size = new(textWidth, 13);
            _names[i].Position = new(46, 20); _names[i].Size = new(textWidth, 29);
            _statuses[i].Position = new(46, cardHeight - 19); _statuses[i].Size = new(textWidth, 14);
        }
    }

    public override void _Draw()
    {
        if (Size.X <= 0 || Size.Y <= 0) return;
        DrawStyleBox(_background, new(Vector2.Zero, Size));
        DrawContours();
        if (_buttons.Count != 6) return;
        for (int i = 0; i < 5; i++) DrawRoute(_bounds[i].GetCenter(), _bounds[i + 1].GetCenter(), Region(i + 1).Unlocked);
        for (int i = 0; i < 6; i++)
        {
            DrawStyleBox(_cards[i], _bounds[i]);
            Color accent = i == 0 || Region(i).Unlocked ? Accents[i] : Accents[i].Darkened(.43f);
            Vector2 at = _bounds[i].Position + new Vector2(24, _bounds[i].Size.Y * .52f);
            DrawLandmark(i, at, accent);
            if (i == 0 ? _inHub : !_inHub && Region(i).Current)
            {
                Vector2 pin = _bounds[i].Position + new Vector2(_bounds[i].Size.X - 11, 11);
                DrawCircle(pin, 4, new("f0d8a6")); DrawCircle(pin, 1.5f, new("14232b"));
            }
        }
    }

    private void DrawContours()
    {
        Color contour = new("29414a"), fine = new("21353e");
        for (int i = 0; i < 4; i++)
        {
            float inset = 9 + i * 7;
            Stroke(i % 2 == 0 ? contour : fine, 1,
                new(inset, Size.Y * .16f), new(Size.X * .19f, 30 + i * 5), new(Size.X * .48f, 34 + i * 4),
                new(Size.X - inset, Size.Y * .24f), new(Size.X - 8 - i * 6, Size.Y * .55f),
                new(Size.X - 17 - i * 4, Size.Y - 35 - i * 5), new(Size.X * .59f, Size.Y - 25 - i * 3),
                new(Size.X * .22f, Size.Y - 29 - i * 4), new(inset, Size.Y * .72f), new(6 + i * 5, Size.Y * .39f));
        }
        Stroke(new("37535c"), 1, new(Size.X * .47f, 33), new(Size.X * .5f, Size.Y * .3f), new(Size.X * .46f, Size.Y * .49f), new(Size.X * .51f, Size.Y * .72f), new(Size.X * .48f, Size.Y - 29));
    }

    private void DrawRoute(Vector2 from, Vector2 to, bool unlocked)
    {
        Color route = unlocked ? new("88b0ae") : new("526372");
        DrawLine(from, to, new("0d1a21"), 7, true);
        if (unlocked) DrawLine(from, to, route, 2, true);
        else
        {
            Vector2 delta = to - from; float length = delta.Length(); Vector2 direction = delta.Normalized();
            for (float step = 0; step < length; step += 9) DrawLine(from + direction * step, from + direction * Math.Min(step + 4, length), route, 1.5f, true);
        }
        Vector2 middle = (from + to) * .5f, forward = (to - from).Normalized(), side = new(-forward.Y, forward.X);
        Stroke(route, 1.5f, middle - forward * 3 + side * 3, middle + forward * 2, middle - forward * 3 - side * 3);
    }

    private void DrawLandmark(int region, Vector2 at, Color accent)
    {
        DrawCircle(at, 18, new Color("101f28"));
        DrawArc(at, 18, 0, Mathf.Tau, 32, accent.Darkened(.5f), 1, true);
        switch (region)
        {
            case 0: // Greyhaven's sheltering walls and light.
                DrawRect(new(at + new Vector2(-12, -5), new(24, 15)), accent.Darkened(.4f));
                DrawRect(new(at + new Vector2(-11, -11), new(6, 21)), accent);
                DrawRect(new(at + new Vector2(5, -11), new(6, 21)), accent);
                Stroke(accent, 2, at + new Vector2(-5, -4), at + new Vector2(0, -8), at + new Vector2(5, -4));
                DrawRect(new(at + new Vector2(-2, 2), new(4, 8)), new("14242c"));
                DrawCircle(at + new Vector2(0, -1), 2, new("f2dfaa"));
                break;
            case 1: // Broken grave markers on the Grey March.
                Stroke(accent.Darkened(.2f), 1.5f, at + new Vector2(-13, 11), at + new Vector2(-5, 8), at + new Vector2(3, 10), at + new Vector2(13, 7));
                Stroke(accent, 2.5f, at + new Vector2(-8, 6), at + new Vector2(-7, -7), at + new Vector2(-4, -10), at + new Vector2(0, -7), at + new Vector2(0, 7));
                DrawLine(at + new Vector2(-10, -3), at + new Vector2(3, -3), accent, 2, true);
                DrawLine(at + new Vector2(8, 7), at + new Vector2(7, -3), accent.Darkened(.1f), 2, true);
                DrawLine(at + new Vector2(4, 0), at + new Vector2(11, 0), accent.Darkened(.1f), 2, true);
                break;
            case 2: // Living trees and twisting roots of the Verdant Maw.
                DrawPolygon([at + new Vector2(-14, 1), at + new Vector2(-7, -14), at + new Vector2(1, 1)], [accent.Darkened(.25f)]);
                DrawPolygon([at + new Vector2(-3, 4), at + new Vector2(6, -13), at + new Vector2(15, 4)], [accent]);
                Stroke(accent, 2, at + new Vector2(-7, 0), at + new Vector2(-7, 8), at + new Vector2(-12, 12));
                Stroke(accent, 2, at + new Vector2(6, 3), at + new Vector2(6, 9), at + new Vector2(0, 12));
                break;
            case 3: // A volcanic ridge and ember fissure in the Cinder Reach.
                DrawPolygon([at + new Vector2(-15, 11), at + new Vector2(-6, -10), at + new Vector2(-1, -6), at + new Vector2(5, -11), at + new Vector2(15, 11)], [accent.Darkened(.35f)]);
                Stroke(accent, 2, at + new Vector2(-8, -4), at + new Vector2(-2, -2), at + new Vector2(0, 3), at + new Vector2(-3, 7), at + new Vector2(2, 12));
                DrawCircle(at + new Vector2(0, -11), 2, accent); DrawCircle(at + new Vector2(6, -15), 1, accent);
                break;
            case 4: // The sharp peaks and broken crossing of the Shattered Spine.
                DrawPolygon([at + new Vector2(-16, 11), at + new Vector2(-6, -14), at + new Vector2(3, 11)], [accent.Darkened(.35f)]);
                DrawPolygon([at + new Vector2(-3, 11), at + new Vector2(7, -10), at + new Vector2(16, 11)], [accent.Darkened(.1f)]);
                Stroke(accent, 2, at + new Vector2(-10, -4), at + new Vector2(-6, -14), at + new Vector2(-2, -4));
                Stroke(new("e6eef0"), 1.5f, at + new Vector2(3, -2), at + new Vector2(7, -10), at + new Vector2(11, -2));
                Stroke(new("14242c"), 2, at + new Vector2(-4, 12), at + new Vector2(0, 6), at + new Vector2(-1, 0));
                break;
            case 5: // A hollow celestial gate against the last region's night.
                DrawArc(at, 12, -.25f, Mathf.Pi + .25f, 24, accent, 2, true);
                DrawArc(at, 9, Mathf.Pi, Mathf.Tau, 20, accent.Darkened(.15f), 1.5f, true);
                DrawLine(at + new Vector2(-8, 10), at + new Vector2(-5, -5), accent, 2, true);
                DrawLine(at + new Vector2(8, 10), at + new Vector2(5, -5), accent, 2, true);
                DrawCircle(at + new Vector2(0, -6), 3, accent);
                DrawCircle(at + new Vector2(-11, -11), 1, new("dfd8ed")); DrawCircle(at + new Vector2(12, -8), 1, new("dfd8ed"));
                break;
        }
    }

    private void Stroke(Color color, float width, params Vector2[] points) => DrawPolyline(points, color, width, true);
    private static Label Caption(string name, string text, int fontSize, Color color)
    {
        var label = new Label { Name = name, Text = text, ClipText = true, MouseFilter = MouseFilterEnum.Ignore, FocusMode = FocusModeEnum.None };
        label.AddThemeFontSizeOverride("font_size", fontSize); label.AddThemeColorOverride("font_color", color); return label;
    }
    private static StyleBoxFlat Card(Color background, Color border, int width) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthLeft = width,
        BorderWidthTop = width,
        BorderWidthRight = width,
        BorderWidthBottom = width,
        CornerRadiusTopLeft = 5,
        CornerRadiusTopRight = 5,
        CornerRadiusBottomLeft = 5,
        CornerRadiusBottomRight = 5,
        ContentMarginLeft = 0,
        ContentMarginRight = 0,
        ContentMarginTop = 0,
        ContentMarginBottom = 0
    };
}
