using System.Globalization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Progression;
using Godot;

namespace Ashenwake.Client;

public partial class CraftingWorkbench
{
    private static readonly Color PreviewNeutral = new("d3dfe5"), PreviewMuted = new("9bb0ba"),
        PreviewGain = new("9eddb4"), PreviewLoss = new("eeb196"), PreviewAccent = new("83d6c5");

    private void ShowPreview(CraftingPreview preview, CraftingRequest request)
    {
        Clear(_comparison);
        _comparison.AddThemeConstantOverride("separation", 6);
        var text = new List<string>();
        void Line(string value, Color? color = null, int size = 12)
        {
            text.Add(value);
            var label = Caption(value, size); label.Modulate = color ?? PreviewNeutral; _comparison.AddChild(label);
        }
        void Heading(string value) => Line(value, PreviewAccent, 13);
        var before = preview.Before.Character; var after = preview.After.Character;
        var current = before.Items.FirstOrDefault(item => item.Id == request.ItemId);
        var projected = after.Items.FirstOrDefault(item => item.Id == request.ItemId);
        Heading(preview.Success ? "CURRENT → AFTER CRAFT" : "CURRENT ITEM · RECIPE BLOCKED");
        if (request.Service == CraftingService.Purification)
        {
            Line("Fragment: " + NameOrNone(request.FragmentId));
            string currentState = FragmentState(before, request.FragmentId);
            Line(preview.Success ? "Purification: " + currentState + " → " + FragmentState(after, request.FragmentId) : "Purification: " + currentState,
                preview.Success ? PreviewGain : PreviewNeutral);
            if (preview.Success)
                Line("Ownership: " + (before.OwnedFragments.Contains(request.FragmentId) ? "owned" : "not owned") + " → " +
                    (after.OwnedFragments.Contains(request.FragmentId) ? "owned" : "not owned"), PreviewMuted);
        }
        else if (current is not null)
        {
            Line($"{EquipmentNames.For(current.DefinitionId)} · {current.Rarity} · #{current.Id}", LootVisual.RarityColor(current.Rarity.ToString()), 13);
            Line(EquipmentDetails.Lore(current.DefinitionId), PreviewMuted);
            Line("Item totals include base values and rolled affixes.", PreviewMuted);
            foreach (var stat in ItemStatRows(current, preview.Success ? projected : null))
                Line(stat.Text, stat.Delta is null ? PreviewNeutral : ChangeColor(stat.Delta.Value));
            string property = _definition.Items.Single(item => item.Id == current.DefinitionId).Property;
            if (property.Length > 0)
                Line("Property: " + EquipmentDetails.Power(property), PreviewMuted);
            if (current.Engraving.Length > 0 || projected?.Engraving.Length > 0 || request.Service == CraftingService.Engraving)
                Line(preview.Success && projected is not null ? "Engraving: " + EquipmentDetails.Power(current.Engraving) + " → " + EquipmentDetails.Power(projected.Engraving)
                    : "Engraving: " + EquipmentDetails.Power(current.Engraving), preview.Success && projected is not null && projected.Engraving != current.Engraving ? PreviewGain : PreviewNeutral);
            if (current.Rarity == ItemRarity.Godwrought && current.DefinitionId == "item.ashcleaver")
            {
                Line(EquipmentDetails.Awakening(current), PreviewMuted);
                Line($"Awakening: {current.BurningKills}/{GodwroughtProgress.AwakeningKills} burning kills · {(current.Awakened ? "awakened" : "dormant")}", PreviewMuted);
                Line(preview.Success && projected is not null ? "Evolution: " + EquipmentDetails.Evolution(current.Evolution) + " → " + EquipmentDetails.Evolution(projected.Evolution)
                    : "Evolution: " + EquipmentDetails.Evolution(current.Evolution), preview.Success && projected is not null && projected.Evolution != current.Evolution ? PreviewGain : PreviewNeutral);
            }
            if (preview.Success && projected is null)
            {
                Line("After craft: this item is destroyed permanently.", PreviewLoss);
                string[] removedSlots = before.Equipment.Where(pair => pair.Value == current.Id && !after.Equipment.ContainsValue(current.Id)).Select(pair => SlotLabel(pair.Key)).ToArray();
                if (removedSlots.Length > 0) Line("Equipment removed: " + string.Join(", ", removedSlots) + ".", PreviewLoss);
                if (property.Length > 0)
                    Line("Property library: " + EquipmentDetails.PowerName(property) + " · " + (before.PropertyLibrary.Contains(property) ? "already learned" : "not learned") + " → " +
                        (after.PropertyLibrary.Contains(property) ? "learned" : "not learned"), PreviewGain);
            }
            if (preview.Success && projected is not null && projected.Evolution != current.Evolution)
                Line("Permanent choice: this item cannot take the other evolution branch.", PreviewLoss);
        }
        else Line("No item selected.", PreviewMuted);

        Heading(preview.Success ? "COST & REMAINING BALANCE" : "CURRENT BALANCE & BASE PRICE");
        if (preview.Success)
        {
            int delta = after.Materials - before.Materials;
            Line($"Materials: {Number(before.Materials)} → {Number(after.Materials)} ({Signed(delta)} · {(delta < 0 ? "spent" : delta > 0 ? "gained" : "no materials spent")})",
                ChangeColor(delta));
        }
        else
        {
            Line("Materials owned: " + Number(before.Materials) + ". No materials spent.");
            if (_definition.CraftingCosts.TryGetValue(request.Service, out int baseCost))
                Line("Base workshop price: " + Number(baseCost) + " materials, before any catalyst payment.", PreviewMuted);
        }
        var catalysts = CatalystChanges(preview).Select(change => change.Id).ToHashSet(StringComparer.Ordinal);
        if (request.CatalystId is { Length: > 0 } selected) catalysts.Add(selected);
        foreach (string id in catalysts.Order(StringComparer.Ordinal))
        {
            int owned = before.Endgame?.Catalysts.GetValueOrDefault(id) ?? 0;
            int remaining = after.Endgame?.Catalysts.GetValueOrDefault(id) ?? 0;
            string blocked = request.Service == CraftingService.DivineGrafting && before.Endgame is not null
                ? $"Required catalyst: {Readable(id)} · 1 needed, {owned} owned. Nothing spent."
                : $"Selected catalyst: {Readable(id)} · {owned} owned. Nothing spent.";
            Line(preview.Success ? $"{Readable(id)}: {owned} → {remaining} ({Signed(remaining - owned)} · {(remaining < owned ? "spent" : remaining > owned ? "gained" : "unchanged")})"
                : blocked, preview.Success ? ChangeColor(remaining - owned) : PreviewMuted);
        }
        if (!preview.Success) Line("No result: " + preview.Reason, PreviewLoss);
        else if (preview.RequiresConfirmation) Line("Nothing changes until you confirm this permanent craft.", PreviewLoss);
        else Line("Preview only. Your current item and materials are unchanged.", PreviewMuted);
        PreviewText = string.Join("\n", text);
    }

    internal static string ConfirmationText(CraftingPreview preview, CraftingRequest request, ProgressionDefinition content)
    {
        var before = preview.Before.Character; var after = preview.After.Character;
        var item = before.Items.Single(i => i.Id == request.ItemId);
        string warning = request.Service == CraftingService.Extraction
            ? "This permanently destroys the selected item, including equipped gear, and learns its property."
            : "This permanently evolves Ashcleaver. The other evolution branch will be unavailable for this item.";
        string power = request.Service == CraftingService.Extraction
            ? EquipmentDetails.Power(content.Items.Single(i => i.Id == item.DefinitionId).Property)
            : EquipmentDetails.Evolution(after.Items.Single(i => i.Id == item.Id).Evolution);
        // Keep the irreversible consequence, selected identity and exact projected costs visible.
        // Lore and the full stat comparison remain in the scrollable workbench behind this modal.
        return warning + $"\n\n{EquipmentNames.For(item.DefinitionId)} · {item.Rarity} · #{item.Id}\n\n" +
            ResultSummary(preview) + "\n\n" + power +
            $"\n\nMaterials: {Number(before.Materials)} → {Number(after.Materials)} ({Number(before.Materials - after.Materials)} spent).";
    }

    private static string ResultSummary(CraftingPreview preview)
    {
        if (!preview.Success) return "Craft not applied: " + preview.Reason;
        var before = preview.Before.Character; var after = preview.After.Character;
        var changes = new List<string>();
        var previous = before.Items.ToDictionary(item => item.Id);
        var current = after.Items.ToDictionary(item => item.Id);
        foreach (var item in before.Items.Where(item => !current.ContainsKey(item.Id)))
            changes.Add($"Destroyed {EquipmentNames.For(item.DefinitionId)} #{item.Id}.");
        foreach (var slot in before.Equipment.Where(pair => !after.Equipment.TryGetValue(pair.Key, out long id) || id != pair.Value))
            changes.Add("Equipment removed: " + SlotLabel(slot.Key) + ".");
        foreach (string property in after.PropertyLibrary.Except(before.PropertyLibrary, StringComparer.Ordinal))
            changes.Add("Learned " + EquipmentDetails.PowerName(property) + ".");
        foreach (var item in after.Items)
        {
            if (!previous.TryGetValue(item.Id, out var old))
            {
                changes.Add($"Added {EquipmentNames.For(item.DefinitionId)} #{item.Id}.");
                continue;
            }
            foreach (var stat in ItemStatRows(old, item).Where(row => row.Delta is not (null or 0))) changes.Add(stat.Text + ".");
            if (old.Engraving != item.Engraving) changes.Add("Engraving: " + EquipmentDetails.PowerName(old.Engraving) + " → " + EquipmentDetails.PowerName(item.Engraving) + ".");
            if (old.Evolution != item.Evolution) changes.Add("Permanent evolution: " + EvolutionName(old.Evolution) + " → " + EvolutionName(item.Evolution) + ".");
            if (old.Rarity != item.Rarity) changes.Add("Rarity: " + old.Rarity + " → " + item.Rarity + ".");
        }
        foreach (string fragment in after.PurifiedFragments.Except(before.PurifiedFragments, StringComparer.Ordinal))
            changes.Add("Purified " + Readable(fragment) + (after.OwnedFragments.Contains(fragment) ? "; fragment remains owned." : "."));
        foreach (var catalyst in CatalystChanges(preview))
            changes.Add($"{Readable(catalyst.Id)}: {catalyst.Before} → {catalyst.After} ({Signed(catalyst.After - catalyst.Before)} · {(catalyst.After < catalyst.Before ? "spent" : "gained")}).");
        return changes.Count > 0 ? string.Join("\n", changes) : "The authoritative craft completed; item properties are unchanged.";
    }

    private static IEnumerable<(string Text, int? Delta)> ItemStatRows(PermanentItem before, PermanentItem? after)
    {
        yield return StatRow("Damage", before.BaseDamage + ItemRoll(before, "affix.damage"), after is null ? null : after.BaseDamage + ItemRoll(after, "affix.damage"));
        yield return StatRow("Armor", before.BaseArmor + ItemRoll(before, "affix.armor"), after is null ? null : after.BaseArmor + ItemRoll(after, "affix.armor"));
        yield return StatRow("Critical", before.BaseCriticalBasisPoints + ItemRoll(before, "affix.critical"), after is null ? null : after.BaseCriticalBasisPoints + ItemRoll(after, "affix.critical"), true);
        foreach (string id in before.Affixes.Keys.Concat(after?.Affixes.Keys.AsEnumerable() ?? []).Distinct(StringComparer.Ordinal)
            .Where(id => id is not ("affix.damage" or "affix.armor" or "affix.critical")).Order(StringComparer.Ordinal))
            yield return StatRow(Readable(id), ItemRoll(before, id), after is null ? null : ItemRoll(after, id));
    }

    private static (string Text, int? Delta) StatRow(string name, int before, int? after, bool critical = false)
    {
        string Format(int value) => critical ? (value / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%" : Number(value);
        if (after is null) return (name + ": " + Format(before), null);
        int delta = after.Value - before;
        string difference = critical ? (delta / 100m).ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + " pp" : Signed(delta);
        return ($"{name}: {Format(before)} → {Format(after.Value)} ({difference} · {(delta > 0 ? "gained" : delta < 0 ? "lost" : "unchanged")})", delta);
    }

    private static IEnumerable<(string Id, int Before, int After)> CatalystChanges(CraftingPreview preview)
    {
        var before = preview.Before.Character.Endgame?.Catalysts;
        var after = preview.After.Character.Endgame?.Catalysts;
        foreach (string id in (before?.Keys.AsEnumerable() ?? []).Concat(after?.Keys.AsEnumerable() ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            int current = before?.GetValueOrDefault(id) ?? 0, projected = after?.GetValueOrDefault(id) ?? 0;
            if (current != projected) yield return (id, current, projected);
        }
    }

    private static int ItemRoll(PermanentItem item, string id) => item.Affixes.GetValueOrDefault(id);
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Signed(int value) => value.ToString("+0;-0;0", CultureInfo.InvariantCulture);
    private static string NameOrNone(string id) => id.Length == 0 ? "none" : Readable(id);
    private static string EvolutionName(string id) => id.Length == 0 ? "none" : EquipmentDetails.PowerName("evolution." + id.ToLowerInvariant());
    private static string FragmentState(ProgressionState state, string id) => !state.OwnedFragments.Contains(id) ? "not owned" : state.PurifiedFragments.Contains(id) ? "purified" : "unpurified";
    private static Color ChangeColor(int delta) => delta > 0 ? PreviewGain : delta < 0 ? PreviewLoss : PreviewMuted;
    private static string SlotLabel(EquipmentSlot slot) => slot switch { EquipmentSlot.MainHand => "main hand", EquipmentSlot.OffHand => "off hand", EquipmentSlot.Ring1 => "ring 1", EquipmentSlot.Ring2 => "ring 2", _ => slot.ToString().ToLowerInvariant() };
}
