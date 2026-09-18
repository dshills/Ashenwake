using Ashenwake.Core.Combat;
using Godot;

namespace Ashenwake.Client;

public partial class Sandbox
{
    public Func<CombatItem, bool>? LootCompatibility { get; set; }
    private int _minimumLootRarity;
    private bool _compatibleLootOnly, _inspectAllLoot;
    private bool _showAllLootHeld;
    private PanelContainer? _lootPanel;
    private VBoxContainer? _lootRows;
    private Label? _lootDescription;
    private string _lootSignature = "";
    private long _inspectedLoot;
    private static readonly string[] LootRarities = ["Common", "Tempered", "Rare", "Relic", "Legendary", "Godwrought"];

    private bool IsLootVisible(CombatLoot loot) => Input.IsActionPressed("aw_showloot") ||
        loot.Item.Rarity == "Godwrought" || Array.IndexOf(LootRarities, loot.Item.Rarity) >= _minimumLootRarity &&
        (!_compatibleLootOnly || LootCompatibility?.Invoke(loot.Item) != false);
    private void BuildLootSettings(VBoxContainer column)
    {
        column.AddChild(new HSeparator()); column.AddChild(TextLabel("GROUND LOOT", 14));
        var rarity = new OptionButton();
        for (int i = 0; i < LootRarities.Length; i++) rarity.AddItem(i == 0 ? "Show all rarities" : LootRarities[i] + " and above");
        rarity.Name = "LootRarityFilter";
        rarity.Select(_minimumLootRarity); rarity.ItemSelected += index => { _minimumLootRarity = (int)index; SavePreferences(); _lootSignature = ""; if (_view is not null) SynchronizeLootVisuals(); }; column.AddChild(rarity);
        var compatible = new CheckButton { Text = "Only items for the current discipline", ButtonPressed = _compatibleLootOnly };
        compatible.Toggled += value => { _compatibleLootOnly = value; SavePreferences(); _lootSignature = ""; if (_view is not null) SynchronizeLootVisuals(); }; column.AddChild(compatible);
        column.AddChild(TextLabel("Filtering does not delete drops. Godwrought items remain visible. Hold Show loot (default Alt) to reveal every drop; this binding can be changed below.", 12));
        AddButton(column, "Inspect ground loot", ToggleLootInspector);
        _lootPanel = Panel(new(841, 105), new(407, 518));
        var rows = new VBoxContainer(); _lootPanel.AddChild(rows); rows.AddChild(TextLabel("GROUND LOOT", 18));
        rows.AddChild(TextLabel("Inspect visible items and compare their base stats. Permanent affix rolls are inspected after collection in Character → Gear.", 12));
        var all = new CheckButton { Text = "Inspect all drops, including filtered items" };
        all.Toggled += value => { _inspectAllLoot = value; _lootSignature = ""; RefreshLootInspector(); }; rows.AddChild(all);
        var scroll = new ScrollContainer { CustomMinimumSize = new(369, 224) }; rows.AddChild(scroll);
        _lootRows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; scroll.AddChild(_lootRows);
        _lootDescription = TextLabel("Choose a drop to inspect its actual base statistics.", 12); rows.AddChild(_lootDescription);
        AddButton(rows, "Collect inspected drop", () =>
        {
            if (_view.Loot.Any(l => l.Id == _inspectedLoot)) ApplyWhilePaused(new(CombatCommandKind.Pickup, ItemId: _inspectedLoot));
            _lootSignature = ""; RefreshLootInspector();
        });
        AddButton(rows, "Close inspection", ToggleLootInspector);
    }
    private void ToggleLootInspector()
    {
        if (_lootPanel is null) return;
        bool open = !_lootPanel.Visible; _settingsPanel.Visible = false; _inventoryPanel.Visible = false; _lootPanel.Visible = open;
        ChangePause(open);
        if (open) FocusFirstAction(_lootPanel); else FocusResumeOrRelease();
        _lootSignature = ""; RefreshLootInspector();
    }
    private void RefreshLootInspector()
    {
        if (_lootPanel is not { Visible: true } || _lootRows is null || _lootDescription is null || _view is null) return;
        var player = _view.Actors.Single(a => a.Id == 1);
        var drops = _view.Loot.Where(l => _inspectAllLoot || IsLootVisible(l)).OrderBy(l => DistanceSquared(l.Position, player.Position)).ThenBy(l => l.Id).Take(100).ToArray();
        string signature = string.Join(',', drops.Select(l => l.Id)) + ":" + _inspectedLoot;
        if (signature == _lootSignature) return; _lootSignature = signature;
        foreach (var child in _lootRows.GetChildren()) { _lootRows.RemoveChild(child); child.QueueFree(); }
        foreach (var drop in drops)
        {
            var button = AddButton(_lootRows, $"{drop.Item.Name} · {drop.Item.Rarity} · {Math.Sqrt(DistanceSquared(drop.Position, player.Position)) * .001:F1}m", () => { _inspectedLoot = drop.Id; _lootSignature = ""; SynchronizeLootVisuals(); RefreshLootInspector(); });
            button.AddThemeFontSizeOverride("font_size", 12);
        }
        if (drops.Length == 0) _lootRows.AddChild(TextLabel(_view.Loot.Count > 0 ? "Your filter hides these drops. Enable Inspect all drops to reveal them." : "No ground loot in this arena.", 12));
        var selected = _view.Loot.FirstOrDefault(l => l.Id == _inspectedLoot);
        if (selected is null) { _lootDescription.Text = $"{_view.Loot.Count} ground drops. Nothing is destroyed by the filter."; return; }
        var item = selected.Item;
        _view.Equipment.TryGetValue(item.Slot, out long equippedId); var equipped = _view.Inventory.FirstOrDefault(i => i.Id == equippedId);
        _lootDescription.Text = $"{item.Name} · {item.Slot}\nDamage {item.Damage} ({item.Damage - (equipped?.Damage ?? 0):+0;-0;0}) · Armor {item.Armor} ({item.Armor - (equipped?.Armor ?? 0):+0;-0;0})\nCritical {item.CriticalBasisPoints / 100d:F1}% ({(item.CriticalBasisPoints - (equipped?.CriticalBasisPoints ?? 0)) / 100d:+0.0;-0.0;0.0}%)";
    }
}
