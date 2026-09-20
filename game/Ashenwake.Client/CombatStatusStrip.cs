using System.Globalization;
using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

/// <summary>Bounded projection of player conditions; every timer comes from the current combat view.</summary>
public partial class CombatStatusStrip : Control
{
    private const int MaximumChips = 6;
    private const float ChipWidth = 94, Gap = 6;
    private readonly List<Entry> _entries = [];
    private readonly List<string> _activeKeys = [], _displayedKeys = [];
    private readonly List<Chip> _chips = [];
    // Core permits at most 32 actor statuses. Compare their displayed values without
    // allocating strings or enumerators on the unchanged, every-frame refresh path.
    private readonly StatusDisplay[] _renderedStatuses = new StatusDisplay[32];
    private int _renderedStatusCount, _renderedBarrier;
    private bool _hasRenderedState, _renderedAlive, _renderedGuarded, _renderedShielded;
    private Panel? _overflow;
    private Label? _overflowLabel;
    public int ActiveCount => _entries.Count;
    public int DisplayedCount => _displayedKeys.Count;
    public int OverflowCount { get; private set; }
    public IReadOnlyList<string> ActiveKeys => _activeKeys;
    public IReadOnlyList<string> DisplayedKeys => _displayedKeys;
    public string DetailsText { get; private set; } = "";

    public CombatStatusStrip()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new(ChipWidth, 44);
    }

    public override void _Ready() => EnsureChildren();

    public void SetView(CombatActorView player)
    {
        EnsureChildren();
        if (MatchesRenderedState(player)) return;
        _hasRenderedState = true; _renderedAlive = player.Health > 0;
        _renderedBarrier = player.Barrier; _renderedGuarded = player.Guarded; _renderedShielded = player.Shielded;
        _renderedStatusCount = 0; _entries.Clear(); _activeKeys.Clear();
        if (_renderedAlive)
        {
            if (player.Barrier > 0) _entries.Add(new("Barrier", "Barrier", "Barrier", player.Barrier.ToString(CultureInfo.InvariantCulture),
                $"Barrier: {player.Barrier}\nAbsorbs incoming damage before health.", new("9bdbd1")));
            if (player.Guarded) _entries.Add(new("Guarded", "Guarded", "Guarded", "Active", "Guarded\nDefense is increased while this condition is active.", new("d8c38b")));
            if (player.Shielded) _entries.Add(new("Shielded", "Shielded", "Shielded", "Active", "Shielded\nProtected from damage while this condition is active.", new("b9d4e8")));
            for (int index = 0; index < player.Statuses.Count; index++)
            {
                CombatStatusView status = player.Statuses[index];
                if (status.RemainingTicks <= 0) continue;
                StatusDisplay display = Display(status);
                if (_renderedStatusCount < _renderedStatuses.Length) _renderedStatuses[_renderedStatusCount] = display;
                _renderedStatusCount++;
                string duration = (display.Tenths / 10d).ToString("0.0", CultureInfo.InvariantCulture) + "s";
                string stacks = status.Stacks > 1 ? " ×" + status.Stacks.ToString(CultureInfo.InvariantCulture) : "";
                string tooltip = $"{status.Id}{stacks}\n{Description(status.Id)}\n{duration} remaining.";
                _entries.Add(new(status.Id + ":" + status.SourceId, status.Id, status.Id, duration + stacks, tooltip, Accent(status.Id)));
            }
        }
        foreach (Entry entry in _entries) _activeKeys.Add(entry.Key);
        DetailsText = string.Join("\n\n", _entries.Select(entry => entry.Tooltip));
        UpdateLayout();
    }

    private bool MatchesRenderedState(CombatActorView player)
    {
        if (!_hasRenderedState || _renderedAlive != (player.Health > 0)) return false;
        if (!_renderedAlive) return true;
        if (_renderedBarrier != player.Barrier || _renderedGuarded != player.Guarded || _renderedShielded != player.Shielded ||
            _renderedStatusCount > _renderedStatuses.Length) return false;
        int active = 0;
        for (int index = 0; index < player.Statuses.Count; index++)
        {
            CombatStatusView status = player.Statuses[index];
            if (status.RemainingTicks <= 0) continue;
            if (active >= _renderedStatusCount || _renderedStatuses[active] != Display(status)) return false;
            active++;
        }
        return active == _renderedStatusCount;
    }

    private static StatusDisplay Display(CombatStatusView status) => new(status.Id, status.SourceId, status.Stacks,
        status.RemainingTicks / 3 + (status.RemainingTicks % 3 == 0 ? 0 : 1));

    public override void _Notification(int what)
    { if (what == NotificationResized && _overflow is not null) UpdateLayout(); }

    private void UpdateLayout()
    {
        if (_overflow is null || _overflowLabel is null) return;
        float width = MathF.Min(600, Size.X);
        int capacity = Math.Clamp((int)((width + Gap) / (ChipWidth + Gap)), 1, MaximumChips);
        int visible = Math.Min(_entries.Count, capacity);
        OverflowCount = _entries.Count > capacity ? _entries.Count - capacity + 1 : 0;
        if (OverflowCount > 0) visible--;
        _displayedKeys.Clear();
        for (int i = 0; i < _chips.Count; i++)
        {
            Chip chip = _chips[i]; chip.Panel.Visible = i < visible;
            if (i >= visible) continue;
            Entry entry = _entries[i]; _displayedKeys.Add(entry.Key);
            chip.Panel.Position = new(i * (ChipWidth + Gap), 0);
            chip.Panel.Size = new(ChipWidth, 44);
            chip.Panel.TooltipText = entry.Tooltip;
            chip.Icon.SetStatus(entry.Id, entry.Accent);
            chip.Name.Text = entry.Name; chip.Value.Text = entry.Value;
            if (chip.Accent != entry.Accent)
            {
                chip.Value.AddThemeColorOverride("font_color", entry.Accent);
                chip.Style.BorderColor = entry.Accent.Darkened(.5f);
                chip.Accent = entry.Accent;
            }
        }
        _overflow.Visible = OverflowCount > 0;
        _overflow.Position = new(visible * (ChipWidth + Gap), 0);
        _overflow.Size = new(ChipWidth, 44);
        _overflowLabel.Text = "+" + OverflowCount.ToString(CultureInfo.InvariantCulture) + " more";
        _overflow.TooltipText = string.Join("\n\n", _entries.Skip(visible).Select(entry => entry.Tooltip));
        Visible = _entries.Count > 0;
    }

    private void EnsureChildren()
    {
        if (_overflow is not null) return;
        for (int i = 0; i < MaximumChips; i++)
        {
            var style = Style();
            var panel = new Panel { Name = "StatusChip" + (i + 1), Size = new(ChipWidth, 44), MouseFilter = MouseFilterEnum.Pass, Visible = false };
            panel.AddThemeStyleboxOverride("panel", style); AddChild(panel);
            var icon = new CombatStatusIcon { Name = "StatusIcon", Position = new(5, 10), Size = new(23, 23) }; panel.AddChild(icon);
            Label name = Caption(panel, "StatusName", new(31, 6), new(60, 13), 10);
            Label value = Caption(panel, "StatusValue", new(31, 23), new(60, 14), 11);
            _chips.Add(new(panel, icon, name, value, style));
        }
        _overflow = new Panel { Name = "StatusOverflow", Size = new(ChipWidth, 44), MouseFilter = MouseFilterEnum.Pass, Visible = false };
        _overflow.AddThemeStyleboxOverride("panel", Style()); AddChild(_overflow);
        _overflowLabel = Caption(_overflow, "StatusOverflowLabel", new(4, 13), new(86, 16), 12);
        _overflowLabel.HorizontalAlignment = HorizontalAlignment.Center;
    }

    private static Label Caption(Control parent, string name, Vector2 position, Vector2 size, int fontSize)
    {
        var label = new Label { Name = name, Position = position, MouseFilter = MouseFilterEnum.Ignore, ClipText = true };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", new Color("e6e1d6"));
        parent.AddChild(label); label.Size = size; return label;
    }

    private static StyleBoxFlat Style() => new()
    {
        BgColor = new("14242cec"),
        BorderColor = new("647877"),
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 4,
        CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4,
        CornerRadiusBottomRight = 4
    };

    private static Color Accent(string id) => id switch
    {
        "Burning" => new("f4aa73"),
        "Bleeding" => new("ef9199"),
        "Poisoned" => new("b0d887"),
        "Chilled" or "Frozen" => new("a9ddec"),
        "Shocked" => new("e4d981"),
        "Staggered" => new("e5cbb0"),
        "Cursed" or "Terrified" => new("c3a5dd"),
        "Marked" => new("e9b98b"),
        "Vulnerable" => new("e7a591"),
        "Rooted" => new("afc99b"),
        _ => new("d2d9da")
    };

    private static string Description(string id) => id switch
    {
        "Burning" => "Taking fire damage over time.",
        "Bleeding" => "Taking physical damage over time.",
        "Poisoned" => "Taking venom damage over time; stacks increase the damage.",
        "Chilled" => "Movement is slowed; three stacks become Frozen.",
        "Frozen" => "Movement and actions are interrupted.",
        "Shocked" => "Afflicted by storm energy.",
        "Staggered" => "Movement and actions are interrupted.",
        "Cursed" => "Outgoing damage is reduced.",
        "Terrified" => "Actions are interrupted by fear.",
        "Marked" => "Certain attacks consume this mark for extra damage.",
        "Vulnerable" => "Incoming damage is increased.",
        "Rooted" => "Movement is prevented.",
        _ => "An active combat condition."
    };

    private sealed record Entry(string Key, string Id, string Name, string Value, string Tooltip, Color Accent);
    private readonly record struct StatusDisplay(string Id, int SourceId, int Stacks, long Tenths);
    private sealed record Chip(Panel Panel, CombatStatusIcon Icon, Label Name, Label Value, StyleBoxFlat Style)
    {
        public Color Accent { get; set; }
    }
}
