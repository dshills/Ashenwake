using Ashenwake.Core.Combat;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public sealed record CombatRewardNotice(string Kind, string Key, string Title, string Detail, int Count,
    long ItemId, string DefinitionId, string Rarity, double RemainingSeconds);

/// <summary>Bounded, noninteractive acknowledgements of rewards already committed by the simulation.</summary>
public partial class CombatRewardFeed : Control
{
    public const int MaximumVisible = 3;
    public const int MaximumPending = 8;
    public const double NoticeLifetime = 5;
    private const double MaximumBurstLifetime = 8;
    private readonly List<Entry> _visible = [];
    private readonly List<Entry> _pending = [];
    private readonly List<Row> _rows = [];
    private readonly HashSet<long> _recentItems = [];
    private readonly Queue<long> _recentItemOrder = [];
    private sealed class Entry
    {
        public string Kind = "", Key = "", Title = "", Detail = "", DefinitionId = "", Rarity = "", Discipline = "", SkillId = "", Shape = "";
        public long ItemId;
        public int Count = 1;
        public EquipmentSlot Slot;
        public double Age, Remaining = NoticeLifetime;
        public CombatRewardNotice Snapshot() => new(Kind, Key, Title, Detail, Count, ItemId, DefinitionId, Rarity, Remaining);
    }
    private sealed record Row(PanelContainer Panel, GearItemIcon ItemIcon, SkillIcon SkillIcon, Label Badge, Label Title, Label Detail);

    public IReadOnlyList<CombatRewardNotice> VisibleNotices => _visible.Select(entry => entry.Snapshot()).ToArray();
    public IReadOnlyList<CombatRewardNotice> PendingNotices => _pending.Select(entry => entry.Snapshot()).ToArray();
    public int PendingCount => _pending.Count;
    public long AcceptedCount { get; private set; }
    public long PresentedCount { get; private set; }
    public long CoalescedCount { get; private set; }
    public long DroppedCount { get; private set; }

    public CombatRewardFeed()
    {
        Name = "RewardFeed";
        MouseFilter = MouseFilterEnum.Ignore;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new(240, 190);
        Size = new(300, 190);
        ClipContents = true;
    }

    public override void _Ready()
    {
        for (int index = 0; index < MaximumVisible; index++)
        {
            var panel = new PanelContainer { Name = "Reward" + index, Position = new(0, index * 63), Size = new(Size.X, 58), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new("101e29ed"),
                BorderColor = new("526b75"),
                BorderWidthLeft = 2,
                BorderWidthTop = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 7,
                ContentMarginBottom = 7
            });
            AddChild(panel);
            var content = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; content.AddThemeConstantOverride("separation", 8); panel.AddChild(content);
            var iconSpace = new Control { CustomMinimumSize = new(34, 34), MouseFilter = MouseFilterEnum.Ignore }; content.AddChild(iconSpace);
            var itemIcon = new GearItemIcon { Name = "RewardItemIcon" + index, Size = new(34, 34), Position = new(0, 4) }; iconSpace.AddChild(itemIcon);
            var skillIcon = new SkillIcon { Name = "RewardSkillIcon" + index, Size = new(34, 34), Position = new(0, 4) }; iconSpace.AddChild(skillIcon);
            var badge = Caption("↑", 25); badge.Name = "RewardBadge" + index; badge.HorizontalAlignment = HorizontalAlignment.Center; badge.Size = new(34, 40); iconSpace.AddChild(badge);
            var words = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore }; words.AddThemeConstantOverride("separation", 2); content.AddChild(words);
            var title = Caption("", 13); title.Name = "RewardTitle" + index; title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; words.AddChild(title);
            var detail = Caption("", 11); detail.Name = "RewardDetail" + index; detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; words.AddChild(detail);
            _rows.Add(new(panel, itemIcon, skillIcon, badge, title, detail));
        }
        Resized += FitRows;
        FitRows(); RefreshRows();
    }

    public void PresentLoot(CombatItem item, string discipline)
    {
        // An event may be observed again during a presentation refresh. Remember a bounded recent window.
        if (item.Id <= 0 || !_recentItems.Add(item.Id)) return;
        _recentItemOrder.Enqueue(item.Id);
        while (_recentItemOrder.Count > 128) _recentItems.Remove(_recentItemOrder.Dequeue());
        if (!Enum.TryParse<EquipmentSlot>(item.Slot, out var slot)) slot = EquipmentSlot.MainHand;
        Enqueue(new()
        {
            Kind = "Loot",
            Key = "loot:" + item.DefinitionId + "/" + item.Rarity,
            Title = item.Name,
            Detail = item.Rarity + " · Collected",
            ItemId = item.Id,
            DefinitionId = item.DefinitionId,
            Rarity = item.Rarity,
            Slot = slot,
            Discipline = discipline
        });
    }

    public void PresentProgress(string kind, string key, string title, string detail,
        string skillId = "", string discipline = "", string shape = "") => Enqueue(new()
        { Kind = kind, Key = key, Title = title, Detail = detail, SkillId = skillId, Discipline = discipline, Shape = shape });

    public void Advance(double delta, bool paused)
    {
        if (paused || !double.IsFinite(delta) || delta <= 0) return;
        bool changed = false;
        for (int index = _visible.Count - 1; index >= 0; index--)
        {
            var entry = _visible[index]; entry.Age += delta; entry.Remaining -= delta;
            if (entry.Remaining <= 0) { _visible.RemoveAt(index); changed = true; }
        }
        if (changed) { PromotePending(); RefreshRows(); }
        for (int index = 0; index < _visible.Count && index < _rows.Count; index++)
            _rows[index].Panel.Modulate = new Color(1, 1, 1, (float)Math.Clamp(_visible[index].Remaining / .6, 0, 1));
    }

    public void Reset()
    {
        _visible.Clear(); _pending.Clear(); _recentItems.Clear(); _recentItemOrder.Clear();
        AcceptedCount = PresentedCount = CoalescedCount = DroppedCount = 0;
        RefreshRows();
    }

    private void Enqueue(Entry entry)
    {
        AcceptedCount++;
        // A fresh pickup near the burst's hard lifetime starts a new row/queued burst,
        // instead of disappearing before the player can read the updated count.
        var matching = _visible.Concat(_pending).FirstOrDefault(existing => existing.Key == entry.Key && existing.Age <= MaximumBurstLifetime - 1.5);
        if (matching is not null)
        {
            matching.Count++; matching.Title = entry.Title; matching.ItemId = entry.ItemId;
            matching.Detail = entry.Kind == "Loot" ? entry.Rarity + " · Collected ×" + matching.Count : entry.Detail;
            matching.Remaining = Math.Min(MaximumBurstLifetime - matching.Age, Math.Max(matching.Remaining, 1.5));
            CoalescedCount++; RefreshRows(); return;
        }
        if (_visible.Count < MaximumVisible)
        { _visible.Add(entry); PresentedCount++; }
        else
        {
            if (_pending.Count == MaximumPending)
            {
                // Retain build milestones ahead of surplus loot when a large pickup burst fills the queue.
                int discard = _pending.FindIndex(notice => notice.Kind == "Loot");
                if (discard < 0 && entry.Kind == "Loot") { DroppedCount++; return; }
                _pending.RemoveAt(discard < 0 ? 0 : discard); DroppedCount++;
            }
            if (entry.Kind == "Loot") _pending.Add(entry);
            else
            {
                // Milestones keep arrival order while taking priority over queued loot.
                int firstLoot = _pending.FindIndex(notice => notice.Kind == "Loot");
                _pending.Insert(firstLoot < 0 ? _pending.Count : firstLoot, entry);
            }
        }
        RefreshRows();
    }

    private void PromotePending()
    {
        while (_visible.Count < MaximumVisible && _pending.Count > 0)
        {
            _visible.Add(_pending[0]); _pending.RemoveAt(0); PresentedCount++;
        }
    }

    private void RefreshRows()
    {
        for (int index = 0; index < _rows.Count; index++)
        {
            var row = _rows[index]; row.Panel.Visible = index < _visible.Count;
            if (index >= _visible.Count) continue;
            var entry = _visible[index];
            Color color = entry.Kind == "Loot" ? LootVisual.RarityColor(entry.Rarity) : entry.Kind == "LevelUp" ? new("f2cf85") : new("a2d7c0");
            row.Title.Text = entry.Title; row.Detail.Text = entry.Detail;
            row.Title.AddThemeColorOverride("font_color", color); row.Detail.AddThemeColorOverride("font_color", new Color("c4d2d8"));
            row.Panel.Modulate = new Color(1, 1, 1, (float)Math.Clamp(entry.Remaining / .6, 0, 1));
            row.ItemIcon.Visible = entry.Kind == "Loot"; row.SkillIcon.Visible = entry.Kind != "Loot" && entry.SkillId.Length > 0;
            row.Badge.Visible = !row.ItemIcon.Visible && !row.SkillIcon.Visible; row.Badge.AddThemeColorOverride("font_color", color);
            if (row.ItemIcon.Visible)
                row.ItemIcon.Configure(entry.DefinitionId, entry.Slot, entry.Discipline, Enum.TryParse<ItemRarity>(entry.Rarity, out var rarity) ? rarity : ItemRarity.Common);
            else if (row.SkillIcon.Visible) row.SkillIcon.SetSkill(entry.SkillId, entry.Discipline, entry.Shape);
        }
    }

    private void FitRows()
    {
        foreach (var row in _rows) row.Panel.Size = new(Size.X, 58);
    }
    private static Label Caption(string text, int fontSize)
    {
        var label = new Label { Text = text, ClipText = true, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", fontSize); return label;
    }
}
