using System.Globalization;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

/// <summary>Inventory polish checks use owned rewards, native controls and the existing legal equipment branches.</summary>
public partial class AppearanceSmoke
{
    private string _equippedHeadIcon = "";
    private ulong _headIconInstance;

    private async Task InventoryPresentationChecks()
    {
        var icons = Enum.GetValues<EquipmentSlot>().Select(slot => Descendants(Find<Control>("GearEquipment" + slot)).OfType<GearItemIcon>().Single()).ToArray();
        Check("inventory_all_twelve_equipment_cards_have_named_icons", icons.Length == 12 && icons.All(i => i.Name == "GearItemIcon" && i.IsVisibleInTree() && i.IconKey.Length > 0));
        var head = Descendants(Find<Control>("GearEquipmentHead")).OfType<GearItemIcon>().Single();
        _equippedHeadIcon = head.IconKey; _headIconInstance = head.GetInstanceId();
        var backpack = BackpackItems();
        Check("inventory_owned_backpack_cards_have_item_icons", backpack.All(item => Descendants(Find<Control>("GearInventoryItem" + item.Id)).OfType<GearItemIcon>().Count(icon => icon.IconKey.Length > 0) == 1));
        var main = _session.Capture().Progression.Character.Items.Single(item => item.Id == EquipmentId(EquipmentSlot.MainHand));
        var initial = backpack.Single(item => item.Id == _initialMainHand);
        Check("inventory_named_ashcleaver_uses_its_distinct_icon", main.DefinitionId == "item.ashcleaver" &&
            Descendants(Find<Control>("GearEquipmentMainHand")).OfType<GearItemIcon>().Single().IconKey.EndsWith("|Ashcleaver", StringComparison.Ordinal));
        Check("inventory_named_ash_axe_uses_axe_icon", initial.DefinitionId == "item.ash_axe" &&
            Descendants(Find<Control>("GearInventoryItem" + initial.Id)).OfType<GearItemIcon>().Single().IconKey.EndsWith("|Axe", StringComparison.Ordinal));
        await CheckHoverComparison(_initialMainHand, "vanguard");
        await Capture("inventory-icons.png");
    }

    private void InventoryEmptySlotCheck()
    {
        var icon = Descendants(Find<Control>("GearEquipmentHead")).OfType<GearItemIcon>().Single();
        Check("inventory_empty_slot_reuses_icon_with_distinct_silhouette", EquipmentId(EquipmentSlot.Head) == 0 && icon.GetInstanceId() == _headIconInstance && icon.IconKey.Contains("|Empty|", StringComparison.Ordinal) && icon.IconKey != _equippedHeadIcon);
    }

    private async Task ArchivedInventoryChecks()
    {
        string hash = _session.StateHash, world = _sandbox.CurrentAppearance.Key, preview = Find<CharacterPreview>("CharacterPreview").AppearanceKey;
        int history = _session.CaptureReplay().Frames.Length, equips = _equips, unequips = _unequips;
        var owned = OwnedItemIds(); var items = BackpackItems(); var definitions = _session.Content.Capture().Items.ToDictionary(i => i.Id);
        Check("inventory_sort_branch_contains_legitimate_earned_duplicates", _session.ProgressionView.Discipline == "Arcanist" && owned.Count >= 60 && items.GroupBy(i => (i.DefinitionId, i.Rarity)).Any(group => group.Count() > 1));
        var cards = InventoryCards().ToDictionary(card => InventoryCardId(card), card => card.GetInstanceId());
        try
        {
            foreach (int kind in new[] { 1, 2, 3 })
            {
                await ChooseInventoryOption("GearTypeFilter", kind);
                var expected = items.Where(item => InventoryType(definitions[item.DefinitionId]) == kind).Select(item => item.Id).ToHashSet();
                Check("inventory_type_filter_" + kind + "_matches_owned_items", expected.SetEquals(VisibleInventoryIds()) && expected.Count > 0);
            }
            await ChooseInventoryOption("GearTypeFilter", 0);
            var rarity = items.GroupBy(item => item.Rarity).OrderByDescending(group => group.Count()).First().Key;
            await ChooseInventoryOption("GearRarityFilter", (int)rarity + 1);
            Check("inventory_rarity_filter_matches_exact_selected_tier", items.Where(item => item.Rarity == rarity).Select(item => item.Id).ToHashSet().SetEquals(VisibleInventoryIds()));
            await ChooseInventoryOption("GearRarityFilter", 0);
            await TypeInventorySearch("gReAtStAfF");
            Check("inventory_search_matches_friendly_names_case_insensitively", items.Where(item => FriendlyItemName(item).Contains("greatstaff", StringComparison.OrdinalIgnoreCase)).Select(item => item.Id).ToHashSet().SetEquals(VisibleInventoryIds()) && VisibleInventoryIds().Count > 0);
            await ChooseInventoryOption("GearTypeFilter", 2);
            Check("inventory_combined_filters_show_honest_empty_state", VisibleInventoryIds().Count == 0 && Find<Label>("GearEmptyState").IsVisibleInTree() && OwnedItemIds().SetEquals(owned));
            await Capture("inventory-empty-filter.png");
            await ClickGearControl(Find<Button>("GearResetFilters"));
            Check("inventory_reset_restores_all_owned_backpack_items", items.Select(item => item.Id).ToHashSet().SetEquals(VisibleInventoryIds()) && Find<LineEdit>("GearSearch").Text.Length == 0 &&
                Find<OptionButton>("GearTypeFilter").GetSelectedId() == 0 && Find<OptionButton>("GearRarityFilter").GetSelectedId() == 0 && !Find<Label>("GearEmptyState").Visible);
            Check("inventory_clear_releases_only_search_pause", !Find<LineEdit>("GearSearch").HasFocus() && !DragPauseOwners.Contains("inventory-search") && DragPauseOwners.Contains("session") && _sandbox.IsPaused);
            Check("inventory_filters_reuse_cached_card_instances", cards.Count == InventoryCards().Count() && InventoryCards().All(card => cards.GetValueOrDefault(InventoryCardId(card)) == card.GetInstanceId()));

            foreach (int sort in new[] { 0, 1, 2 })
            {
                await ChooseInventoryOption("GearSort", sort);
                var actual = VisibleInventoryIdsInOrder();
                var expected = sort switch
                {
                    1 => items.OrderByDescending(item => item.Rarity).ThenBy(FriendlyItemName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).Select(item => item.Id),
                    2 => items.OrderBy(FriendlyItemName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).Select(item => item.Id),
                    _ => items.OrderBy(item => InventoryType(definitions[item.DefinitionId])).ThenBy(FriendlyItemName, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).Select(item => item.Id)
                };
                Check("inventory_sort_" + sort + "_is_deterministic_with_instance_tiebreaks", actual.SequenceEqual(expected));
                await ChooseInventoryOption("GearSort", (sort + 1) % 3); await ChooseInventoryOption("GearSort", sort);
                Check("inventory_sort_" + sort + "_repeats_without_new_cards", actual.SequenceEqual(VisibleInventoryIdsInOrder()) && InventoryCards().All(card => cards.GetValueOrDefault(InventoryCardId(card)) == card.GetInstanceId()));
            }
            await Capture("inventory-sorted-earned-items.png");
            var candidate = items.First(item =>
            {
                var definition = definitions[item.DefinitionId]; var slot = definition.Slots[0];
                var current = _session.Capture().Progression.Character.Items.FirstOrDefault(i => i.Id == EquipmentId(slot));
                return ItemDamageForComparison(item) != ItemDamageForComparison(current) || ItemArmorForComparison(item) != ItemArmorForComparison(current);
            });
            await CheckHoverComparison(candidate.Id, "earned_arcanist");
            Check("inventory_filter_sort_and_comparison_never_mutate_ownership_or_character", _session.StateHash == hash && _session.CaptureReplay().Frames.Length == history &&
                _sandbox.CurrentAppearance.Key == world && Find<CharacterPreview>("CharacterPreview").AppearanceKey == preview && _equips == equips && _unequips == unequips && OwnedItemIds().SetEquals(owned));
        }
        finally
        {
            await ClickGearControl(Find<Button>("GearResetFilters")); await ChooseInventoryOption("GearSort", 0);
            Find<LineEdit>("GearSearch").ReleaseFocus();
            GetViewport().PushInput(new InputEventMouseMotion { Position = new(8, 8) }, true); await Frames();
        }
        CheckInventoryProjection("inventory_polish_reset");
    }

    private async Task CheckHoverComparison(long id, string suffix)
    {
        var state = _session.Capture().Progression.Character;
        var candidate = state.Items.Single(item => item.Id == id);
        var slot = _session.Content.Capture().Items.Single(item => item.Id == candidate.DefinitionId).Slots[0];
        var equipped = state.Items.FirstOrDefault(item => item.Id == state.Equipment.GetValueOrDefault(slot));
        var card = Find<Control>("GearInventoryItem" + id); await RevealDragControl(card);
        string hash = _session.StateHash, world = _sandbox.CurrentAppearance.Key, preview = Find<CharacterPreview>("CharacterPreview").AppearanceKey;
        int operations = _session.CaptureReplay().Frames.Length, equips = _equips, unequips = _unequips;
        GetViewport().PushInput(new InputEventMouseMotion { Position = new(8, 8) }, true); await Frames();
        GetViewport().PushInput(new InputEventMouseMotion { Position = card.GetGlobalRect().GetCenter() }, true); await Frames();
        var comparison = Find<GearComparison>("GearComparison");
        string key = id.ToString(CultureInfo.InvariantCulture) + "/" + (equipped?.Id.ToString(CultureInfo.InvariantCulture) ?? "empty") + "/" + slot;
        Check("inventory_hover_compares_real_candidate_and_equipped_item_" + suffix, comparison.IsVisibleInTree() && comparison.ComparisonKey == key);
        int damage = ItemDamageForComparison(candidate), oldDamage = ItemDamageForComparison(equipped);
        int armor = ItemArmorForComparison(candidate), oldArmor = ItemArmorForComparison(equipped);
        Check("inventory_comparison_displays_content_backed_damage_and_armor_deltas_" + suffix,
            comparison.ComparisonText.Contains($"Damage  {damage}  ({(damage - oldDamage).ToString("+0;-0;0", CultureInfo.InvariantCulture)}) | Damage  {oldDamage}", StringComparison.Ordinal) &&
            comparison.ComparisonText.Contains($"Armor  {armor}  ({(armor - oldArmor).ToString("+0;-0;0", CultureInfo.InvariantCulture)}) | Armor  {oldArmor}", StringComparison.Ordinal));
        Check("inventory_comparison_fits_viewport_" + suffix, GetViewport().GetVisibleRect().Encloses(comparison.GetGlobalRect()));
        await Capture("inventory-" + suffix + "-comparison.png");
        if (suffix == "earned_arcanist") await CheckComparisonSizes(card, comparison);
        GetViewport().PushInput(new InputEventMouseMotion { Position = new(8, 8) }, true); await Frames();
        card.GrabFocus(); await Frames();
        Check("inventory_keyboard_focus_shows_same_comparison_" + suffix, card.HasFocus() && comparison.IsVisibleInTree() && comparison.ComparisonKey == key);
        Check("inventory_hover_and_focus_leave_gameplay_history_world_and_preview_unchanged_" + suffix, _session.StateHash == hash && _session.CaptureReplay().Frames.Length == operations &&
            _sandbox.CurrentAppearance.Key == world && Find<CharacterPreview>("CharacterPreview").AppearanceKey == preview && _equips == equips && _unequips == unequips);
        card.ReleaseFocus(); await Frames();
    }

    private async Task CheckComparisonSizes(Control card, GearComparison comparison)
    {
        var window = GetWindow(); var originalSize = window.Size; var originalScale = window.ContentScaleSize;
        try
        {
            foreach (var size in new[] { new Vector2I(1280, 720), new Vector2I(780, 800) })
            {
                window.Size = window.ContentScaleSize = size; await Frames(5);
                await RevealDragControl(card);
                GetViewport().PushInput(new InputEventMouseMotion { Position = new(8, 8) }, true); await Frames();
                GetViewport().PushInput(new InputEventMouseMotion { Position = card.GetGlobalRect().GetCenter() }, true); await Frames();
                Check("inventory_comparison_clamps_at_" + size.X + "x" + size.Y,
                    comparison.IsVisibleInTree() && GetViewport().GetVisibleRect().Encloses(comparison.GetGlobalRect()));
                await Capture("inventory-comparison-" + size.X + "x" + size.Y + ".png");
            }
        }
        finally { window.Size = originalSize; window.ContentScaleSize = originalScale; await Frames(5); }
    }

    private async Task InventoryRejectedDropFeedback(string reason, string targetName)
    {
        string expected = reason switch
        {
            "armor_to_weapon" => "slot",
            "wrong_discipline" => "discipline",
            "two_handed_with_offhand" or "offhand_with_two_handed" => "both hands",
            "away_from_torren" => "Torren",
            _ => ""
        };
        if (expected.Length == 0) return;
        var feedback = Find<Label>("GearDropFeedback");
        Check("inventory_native_rejection_explains_" + reason, feedback.IsVisibleInTree() && feedback.Text.Contains(expected, StringComparison.OrdinalIgnoreCase));
        var rect = feedback.GetGlobalRect(); var pointer = DropPoint(Find<Control>(targetName));
        float distance = pointer.DistanceTo(rect.GetCenter());
        Check("inventory_rejection_stays_near_pointer_" + reason, GetViewport().GetVisibleRect().Encloses(rect) && distance < Math.Max(260, rect.Size.X * .5f + 80));
        if (reason == "armor_to_weapon")
        {
            await Capture("inventory-wrong-slot-feedback.png");
            await Frames(140);
            Check("inventory_rejected_drop_message_expires", !feedback.IsVisibleInTree());
        }
    }

    private async Task ChooseInventoryOption(string name, int id)
    {
        var option = Find<OptionButton>(name); int index = option.GetItemIndex(id);
        if (index < 0) throw new InvalidDataException("Inventory option is missing: " + name + "/" + id);
        // Godot PopupMenu consumes window-level input that synthetic headless viewport
        // events cannot deliver. Exercise its public selection contract here; real pointer
        // hover, text input and all drag gestures remain viewport-driven below.
        await RevealDragControl(option);
        option.Select(index); option.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        await Frames();
        if (option.GetSelectedId() != id) throw new InvalidDataException("Inventory option did not select " + name + "/" + id);
    }
    private async Task TypeInventorySearch(string text)
    {
        var search = Find<LineEdit>("GearSearch"); await ClickGearControl(search);
        Check("inventory_search_focus_owns_independent_pause", search.HasFocus() && DragPauseOwners.Contains("inventory-search") && DragPauseOwners.Contains("session") && _sandbox.IsPaused);
        search.SelectAll(); await InventoryKey(Key.Backspace);
        foreach (char character in text)
            foreach (bool pressed in new[] { true, false })
                GetViewport().PushInput(new InputEventKey { Unicode = character, Pressed = pressed }, true);
        await Frames();
        if (search.Text != text) throw new InvalidDataException("Native inventory search text did not arrive.");
        _sandbox.SetSession(_session.Combat); Refresh(); await Frames();
        Check("inventory_search_pause_reacquired_after_session_restore", search.HasFocus() && search.Text == text && DragPauseOwners.Contains("inventory-search") && DragPauseOwners.Contains("session") && _sandbox.IsPaused);
        search.ReleaseFocus(); await Frames();
        Check("inventory_search_focus_release_preserves_session_pause", !search.HasFocus() && !DragPauseOwners.Contains("inventory-search") && DragPauseOwners.Contains("session") && _sandbox.IsPaused);
    }
    private async Task InventoryKey(Key key, Viewport? viewport = null)
    {
        foreach (bool pressed in new[] { true, false })
            (viewport ?? GetViewport()).PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
        await Frames(1);
    }
    private PermanentItem[] BackpackItems()
    {
        var state = _session.Capture().Progression.Character;
        return state.Items.Where(item => !state.Equipment.Values.Contains(item.Id)).ToArray();
    }
    private IEnumerable<Control> InventoryCards() => Descendants(Find<Control>("GearLoadout")).OfType<Control>()
        .Where(card => card.Name.ToString().StartsWith("GearInventoryItem", StringComparison.Ordinal));
    private static long InventoryCardId(Control card) => long.Parse(card.Name.ToString()["GearInventoryItem".Length..], CultureInfo.InvariantCulture);
    private HashSet<long> VisibleInventoryIds() => VisibleInventoryIdsInOrder().ToHashSet();
    private long[] VisibleInventoryIdsInOrder() => InventoryCards().Where(card => card.Visible).Select(InventoryCardId).ToArray();
    private static int InventoryType(ProductionItemDefinition definition) => definition.Slots.Any(slot => slot is EquipmentSlot.MainHand or EquipmentSlot.OffHand) ? 1 :
        definition.Slots.Any(slot => slot is EquipmentSlot.Amulet or EquipmentSlot.Ring1 or EquipmentSlot.Ring2) ? 3 : 2;
    private static string FriendlyItemName(PermanentItem item) => EquipmentNames.For(item.DefinitionId);
    private static int ItemDamageForComparison(PermanentItem? item) => (item?.BaseDamage ?? 0) + (item?.Affixes.GetValueOrDefault("affix.damage") ?? 0);
    private static int ItemArmorForComparison(PermanentItem? item) => (item?.BaseArmor ?? 0) + (item?.Affixes.GetValueOrDefault("affix.armor") ?? 0);
}
