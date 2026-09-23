using System.Collections.ObjectModel;

namespace Ashenwake.Core.Progression;

/// <summary>A full equipment arrangement. An absent slot is deliberately empty; items are references, never copies.</summary>
public sealed record EquipmentPreset(string Id, string Name, SortedDictionary<EquipmentSlot, long> Equipment);
public sealed record EquipmentPresetView(string Id, string Name, IReadOnlyDictionary<EquipmentSlot, long> Equipment);
public sealed record EquipmentPresetIssue(EquipmentSlot Slot, long ItemId, string Reason);
public sealed record EquipmentPresetPreview(bool Success, string Reason, IReadOnlyDictionary<EquipmentSlot, long> Equipment,
    IReadOnlyList<EquipmentPresetIssue> Issues);

public sealed partial class ProgressionSession
{
    public const int MaximumEquipmentPresets = 8;
    public const int MaximumEquipmentPresetNameLength = 32;
    public IReadOnlyList<EquipmentPresetView> EquipmentPresets => Array.AsReadOnly((snapshot.Character.EquipmentPresets ?? [])
        .Select(p => new EquipmentPresetView(p.Id, p.Name, ReadOnlyEquipment(p.Equipment))).ToArray());

    public ProgressionResult SaveEquipmentPreset(string operationId, string id, string name)
        => Change(operationId, new { Action = "SaveEquipmentPreset", id, name }, (next, events) =>
        {
            if (!ValidPresetId(id)) return "Choose equipment preset 1 through 8.";
            string? error = PresetNameError(next.Character, id, name);
            if (error is not null) return error;
            var preset = new EquipmentPreset(id, name.Trim(), new(next.Character.Equipment));
            next.Character.EquipmentPresets = [.. (next.Character.EquipmentPresets ?? []).Where(p => p.Id != id).Append(preset).OrderBy(p => p.Id, StringComparer.Ordinal)];
            events.Add("EquipmentPresetSaved:" + id); return null;
        });

    public ProgressionResult RenameEquipmentPreset(string operationId, string id, string name)
        => Change(operationId, new { Action = "RenameEquipmentPreset", id, name }, (next, events) =>
        {
            if (next.Character.EquipmentPresets?.Any(p => p.Id == id) != true) return "Choose a saved equipment preset.";
            string? error = PresetNameError(next.Character, id, name);
            if (error is not null) return error;
            next.Character.EquipmentPresets = next.Character.EquipmentPresets.Select(p => p.Id == id ? p with { Name = name.Trim() } : p).ToArray();
            events.Add("EquipmentPresetRenamed:" + id); return null;
        });

    public ProgressionResult DeleteEquipmentPreset(string operationId, string id)
        => Change(operationId, new { Action = "DeleteEquipmentPreset", id }, (next, events) =>
        {
            if (next.Character.EquipmentPresets?.Any(p => p.Id == id) != true) return "Choose a saved equipment preset.";
            var remaining = next.Character.EquipmentPresets.Where(p => p.Id != id).ToArray();
            next.Character.EquipmentPresets = remaining.Length == 0 ? null : remaining;
            events.Add("EquipmentPresetDeleted:" + id); return null;
        });

    public ProgressionResult ApplyEquipmentPreset(string operationId, string id)
        => Change(operationId, new { Action = "ApplyEquipmentPreset", id }, (next, events) =>
        {
            var preview = PreviewEquipmentPreset(next.Character, id);
            if (!preview.Success) return preview.Reason;
            // Replace as one transaction: rings and both hands must not depend on equip/unequip ordering.
            next.Character.Equipment = new(preview.Equipment.ToDictionary(p => p.Key, p => p.Value));
            events.Add("EquipmentPresetApplied:" + id); return null;
        });

    public EquipmentPresetPreview PreviewEquipmentPreset(string id) => PreviewEquipmentPreset(snapshot.Character, id);

    private EquipmentPresetPreview PreviewEquipmentPreset(ProgressionState state, string id)
    {
        var preset = state.EquipmentPresets?.FirstOrDefault(p => p.Id == id);
        if (preset is null) return new(false, "Choose a saved equipment preset.", ReadOnlyEquipment([]), []);
        return PreviewEquipmentArrangement(state, preset.Equipment);
    }

    internal EquipmentPresetPreview PreviewEquipmentArrangement(ProgressionState state, SortedDictionary<EquipmentSlot, long> equipment)
    {
        var issues = new List<EquipmentPresetIssue>();
        var items = state.Items.ToDictionary(i => i.Id);
        foreach (var (slot, itemId) in equipment)
        {
            if (!items.TryGetValue(itemId, out var item))
            {
                issues.Add(new(slot, itemId, $"{slot}: item #{itemId} is no longer owned; replace or re-save this preset."));
                continue;
            }
            var definition = Data.Items.Single(i => i.Id == item.DefinitionId);
            if (!definition.Slots.Contains(slot)) issues.Add(new(slot, itemId, $"{slot}: item #{itemId} cannot occupy this slot."));
            if (definition.Disciplines.Length > 0 && !definition.Disciplines.Contains(state.Discipline))
                issues.Add(new(slot, itemId, $"{slot}: item #{itemId} requires {string.Join(" or ", definition.Disciplines)}; current discipline is {state.Discipline}."));
            if (definition.Hands == 2 && equipment.ContainsKey(EquipmentSlot.OffHand))
                issues.Add(new(slot, itemId, "MainHand: this two-handed weapon requires an empty off hand."));
        }
        var affixes = equipment.Values.Where(items.ContainsKey).SelectMany(id => items[id].Affixes.Keys).ToHashSet();
        foreach (var (slot, itemId) in equipment.Where(p => items.ContainsKey(p.Value)))
            if (Data.Affixes.Any(a => items[itemId].Affixes.ContainsKey(a.Id) && a.Excludes.Any(affixes.Contains)))
                issues.Add(new(slot, itemId, $"{slot}: item #{itemId} has an affix that conflicts with another item in this preset."));
        return new(issues.Count == 0, string.Join(" ", issues.Select(i => i.Reason)), ReadOnlyEquipment(equipment), issues.AsReadOnly());
    }

    private static IReadOnlyDictionary<EquipmentSlot, long> ReadOnlyEquipment(SortedDictionary<EquipmentSlot, long> equipment)
        => new ReadOnlyDictionary<EquipmentSlot, long>(new SortedDictionary<EquipmentSlot, long>(equipment));
    private static bool ValidPresetId(string? id) => id is { Length: 8 } && id.StartsWith("preset.", StringComparison.Ordinal) && id[7] is >= '1' and <= '8';
    private static bool ValidPresetName(string? name) => name is { Length: > 0 and <= MaximumEquipmentPresetNameLength } && name == name.Trim() &&
        !string.IsNullOrWhiteSpace(name) && !name.Any(c => char.IsControl(c) || char.IsSurrogate(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format);
    private static string? PresetNameError(ProgressionState state, string id, string? name)
    {
        if (name is null || name.Length > 128 || name.Any(char.IsControl) || !ValidPresetName(name.Trim())) return "Use a name of 1–32 visible characters, without line breaks or control characters.";
        if ((state.EquipmentPresets ?? []).Any(p => p.Id != id && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return "Another equipment preset already uses that name.";
        return null;
    }

    private static void ValidateEquipmentPresets(ProgressionContent content, ProgressionState state)
    {
        var presets = state.EquipmentPresets;
        if (presets is null) return;
        if (presets.Length is < 1 or > MaximumEquipmentPresets || presets.Any(p => p is null || !ValidPresetId(p.Id) || !ValidPresetName(p.Name) || p.Equipment is null) ||
            presets.Select(p => p.Id).Distinct().Count() != presets.Length || presets.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != presets.Length ||
            !presets.Select(p => p.Id).SequenceEqual(presets.Select(p => p.Id).Order(StringComparer.Ordinal)))
            throw new InvalidDataException("Malformed equipment preset identities, names, or ordering.");
        var items = state.Items.ToDictionary(i => i.Id);
        foreach (var preset in presets)
        {
            if (preset.Equipment.Count > Enum.GetValues<EquipmentSlot>().Length || preset.Equipment.Values.Distinct().Count() != preset.Equipment.Count ||
                preset.Equipment.Any(p => !Enum.IsDefined(p.Key) || p.Value <= 0 || p.Value >= state.NextItemId))
                throw new InvalidDataException("Invalid equipment preset references.");
            foreach (var (slot, itemId) in preset.Equipment.Where(p => items.ContainsKey(p.Value)))
            {
                var definition = content.Data.Items.Single(i => i.Id == items[itemId].DefinitionId);
                if (!definition.Slots.Contains(slot) || definition.Hands == 2 && preset.Equipment.ContainsKey(EquipmentSlot.OffHand))
                    throw new InvalidDataException("Equipment preset violates slot or hand restrictions.");
            }
            // Ownership, current discipline, and affix conflicts are checked on application: discarding,
            // extraction, retraining, and crafting must leave an old preset available for repair.
        }
    }
}
