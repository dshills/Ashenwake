using System.Globalization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Read-only, cached comparison of two item instances in one equipment slot.</summary>
public partial class GearComparison : PanelContainer
{
    public string ComparisonKey { get; private set; } = "";
    public string ComparisonText { get; private set; } = "";
    private static readonly Color TextColor = new("d3dfe5"), MutedColor = new("9bb0ba"),
        BetterColor = new("9eddb4"), WorseColor = new("eeb196");
    private bool _styled;
    private sealed record ComparisonRow(string Candidate, string Equipped, Color CandidateColor, Color EquippedColor);

    public override void _Ready() => EnsureStyle();

    public void SetItems(ProgressionSnapshot state, ProgressionDefinition content, PermanentItem candidate, EquipmentSlot slot, string restriction)
    {
        EnsureStyle();
        var equipped = state.Character.Equipment.TryGetValue(slot, out long equippedId)
            ? state.Character.Items.FirstOrDefault(item => item.Id == equippedId) : null;
        var definition = content.Items.Single(item => item.Id == candidate.DefinitionId);
        var equippedDefinition = equipped is null ? null : content.Items.Single(item => item.Id == equipped.DefinitionId);
        string key = candidate.Id + "/" + (equipped?.Id.ToString(CultureInfo.InvariantCulture) ?? "empty") + "/" + slot;
        string heading = SlotName(slot) + " · base values + rolled affixes";
        var rows = new List<ComparisonRow>
        {
            new("HOVERED ITEM", "EQUIPPED · " + SlotName(slot), MutedColor, MutedColor),
            new(ItemName(candidate), equipped is null ? "Empty slot" : ItemName(equipped), RarityColor(candidate.Rarity), equipped is null ? MutedColor : RarityColor(equipped.Rarity)),
            new(candidate.Rarity + " · #" + candidate.Id, equipped is null ? "No item equipped" : equipped.Rarity + " · #" + equipped.Id, MutedColor, MutedColor)
        };
        rows.Add(new(EquipmentDetails.Lore(candidate.DefinitionId), equipped is null ? "" : EquipmentDetails.Lore(equipped.DefinitionId), MutedColor, MutedColor));
        if (EquipmentDetails.Source(candidate.DefinitionId).Length > 0 || equipped is not null && EquipmentDetails.Source(equipped.DefinitionId).Length > 0)
            rows.Add(new(EquipmentDetails.Source(candidate.DefinitionId), equipped is null ? "" : EquipmentDetails.Source(equipped.DefinitionId), MutedColor, MutedColor));
        AddStat(rows, "Damage", candidate.BaseDamage + Roll(candidate, "affix.damage"), (equipped?.BaseDamage ?? 0) + Roll(equipped, "affix.damage"));
        AddStat(rows, "Armor", candidate.BaseArmor + Roll(candidate, "affix.armor"), (equipped?.BaseArmor ?? 0) + Roll(equipped, "affix.armor"));
        AddStat(rows, "Critical", candidate.BaseCriticalBasisPoints + Roll(candidate, "affix.critical"), (equipped?.BaseCriticalBasisPoints ?? 0) + Roll(equipped, "affix.critical"), percent: true);
        string[] rolled = candidate.Affixes.Keys.Concat(equipped?.Affixes.Keys ?? Enumerable.Empty<string>())
            .Where(id => id is not ("affix.damage" or "affix.armor" or "affix.critical"))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (string id in rolled) AddStat(rows, Readable(id), Roll(candidate, id), Roll(equipped, id));
        if (rolled.Length == 0) rows.Add(new("Other rolls: none", "Other rolls: none", MutedColor, MutedColor));
        rows.Add(new("Property: " + EquipmentDetails.Power(definition.Property), "Property: " + EquipmentDetails.Power(equippedDefinition?.Property), TextColor, TextColor));
        rows.Add(new("Engraving: " + EquipmentDetails.Power(candidate.Engraving), "Engraving: " + EquipmentDetails.Power(equipped?.Engraving), TextColor, TextColor));
        if (HasAwakening(candidate) || HasAwakening(equipped))
        {
            rows.Add(new(AwakeningText(candidate), AwakeningText(equipped), TextColor, TextColor));
            rows.Add(new("Evolution: " + EquipmentDetails.Evolution(candidate.Evolution), "Evolution: " + EquipmentDetails.Evolution(equipped?.Evolution), TextColor, TextColor));
        }
        foreach (var set in EquipmentSets.Catalog.Where(set => set.PieceIds.Contains(candidate.DefinitionId) || set.PieceIds.Contains(equipped?.DefinitionId ?? "")))
        {
            int current = EquipmentSets.CountEquipped(set.Id, state.Character);
            int after = EquipmentSets.PreviewCountEquipped(set.Id, state.Character, candidate, slot);
            rows.Add(new($"{set.Name} · {current}/2 → {after}/2", current == 2 ? "Set bonus active" : "Set bonus inactive",
                after > current ? BetterColor : after < current ? WorseColor : TextColor, MutedColor));
            rows.Add(new("(2) " + set.Bonus, after == 2 ? "After equipping: ACTIVE" : "After equipping: inactive", TextColor, after == 2 ? BetterColor : MutedColor));
        }
        var notes = new List<string> { "This slot only; other gear, skills and properties are excluded from numeric deltas." };
        if (definition.Hands == 2 || equippedDefinition?.Hands == 2)
            notes.Add("Two-handed weapon: requires an empty off hand; off-hand changes are excluded.");
        if (definition.Disciplines.Length > 0)
            notes.Add("Disciplines: " + string.Join(", ", definition.Disciplines) + ". Current: " + state.Character.Discipline + ".");
        if (restriction.Length > 0) notes.Add(restriction);
        string note = string.Join(" ", notes);
        float width = !string.IsNullOrEmpty(candidate.Evolution) || !string.IsNullOrEmpty(equipped?.Evolution) ||
            EquipmentSets.IsItem(candidate.DefinitionId) || EquipmentSets.IsItem(equipped?.DefinitionId ?? "") ||
            Ashenwake.Core.Combat.LegendaryEquipment.IsItem(candidate.DefinitionId) || Ashenwake.Core.Combat.LegendaryEquipment.IsItem(equipped?.DefinitionId ?? "") ? 640 : 520;
        string comparison = heading + "\n" + string.Join("\n", rows.Select(row => row.Candidate + " | " + row.Equipped)) + "\n" + note;
        if (ComparisonKey == key && ComparisonText == comparison) return;
        ComparisonKey = key; ComparisonText = comparison;
        CustomMinimumSize = new(width, 0);
        foreach (Node child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        var layout = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        layout.AddThemeConstantOverride("separation", 6); AddChild(layout);
        layout.AddChild(Caption(heading, new Color("83d6c5")));
        var columns = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("h_separation", 14); columns.AddThemeConstantOverride("v_separation", 3);
        layout.AddChild(columns);
        foreach (var row in rows)
        {
            columns.AddChild(Caption(row.Candidate, row.CandidateColor, (width - 42) / 2));
            columns.AddChild(Caption(row.Equipped, row.EquippedColor, (width - 42) / 2));
        }
        layout.AddChild(Caption(note, restriction.Length > 0 ? WorseColor : MutedColor));
        // Discard the previous tooltip's dimensions so a shorter comparison can shrink immediately.
        Size = new(width, 0);
    }

    private void EnsureStyle()
    {
        if (_styled) return;
        _styled = true; Name = "GearComparison";
        MouseFilter = MouseFilterEnum.Ignore; FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new(520, 0); Size = new(520, 0);
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new("10212bf9"),
            BorderColor = new("83cdb5"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5,
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10
        });
    }

    private static void AddStat(List<ComparisonRow> rows, string name, int candidate, int equipped, bool percent = false)
    {
        int delta = candidate - equipped;
        string value = percent ? Percent(candidate) : candidate.ToString(CultureInfo.InvariantCulture);
        string current = percent ? Percent(equipped) : equipped.ToString(CultureInfo.InvariantCulture);
        string change = percent ? (delta / 100m).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + " pp"
            : delta.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        rows.Add(new(name + "  " + value + "  (" + change + ")", name + "  " + current,
            delta > 0 ? BetterColor : delta < 0 ? WorseColor : TextColor, TextColor));
    }

    private static Label Caption(string text, Color color, float minimumWidth = 0)
    {
        var label = new Label
        {
            Text = text,
            CustomMinimumSize = new(minimumWidth, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Top,
            MouseFilter = MouseFilterEnum.Ignore,
            FocusMode = FocusModeEnum.None
        };
        label.AddThemeFontSizeOverride("font_size", 12); label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static int Roll(PermanentItem? item, string id) => item?.Affixes.GetValueOrDefault(id) ?? 0;
    private static string Percent(int basisPoints) => (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    private static bool HasAwakening(PermanentItem? item) => item?.DefinitionId == "item.ashcleaver" && item.Rarity == ItemRarity.Godwrought;
    private static string AwakeningText(PermanentItem? item) => HasAwakening(item)
        ? $"{(item!.Awakened ? "Awakened" : "Dormant")} · {item.BurningKills}/{GodwroughtProgress.AwakeningKills} burning kills" : "Awakening: none";
    private static string ItemName(PermanentItem item) => EquipmentNames.For(item.DefinitionId);
    private static string Readable(string id) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Split('.').Last().Replace('_', ' '));
    private static string SlotName(EquipmentSlot slot) => slot switch { EquipmentSlot.MainHand => "Main hand", EquipmentSlot.OffHand => "Off hand", EquipmentSlot.Ring1 => "Ring 1", EquipmentSlot.Ring2 => "Ring 2", _ => slot.ToString() };
    private static Color RarityColor(ItemRarity rarity) => LootVisual.RarityColor(rarity.ToString());
}
