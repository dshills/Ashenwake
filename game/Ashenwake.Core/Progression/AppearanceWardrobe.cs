using System.Text.Json.Serialization;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Progression;

public sealed record WardrobeUnlock([property: JsonRequired] string ItemId, [property: JsonRequired] ItemRarity Rarity);
public sealed record WardrobeLook([property: JsonRequired] string Name,
    [property: JsonRequired] SortedDictionary<EquipmentSlot, string> Overrides, [property: JsonRequired] bool HideHelmet);
public sealed record AppearanceWardrobeMemory([property: JsonRequired] string CharacterId, [property: JsonRequired] long Revision,
    [property: JsonRequired] WardrobeUnlock[] Unlocks, [property: JsonRequired] SortedDictionary<EquipmentSlot, string> Overrides,
    [property: JsonRequired] bool HideHelmet, [property: JsonRequired] WardrobeLook[] Looks);

/// <summary>Character-local cosmetic knowledge and choices. Pure operations never touch gameplay state or replay.</summary>
public static class AppearanceWardrobe
{
    public const int MaximumLooks = 8, MaximumNameLength = 32, MaximumUnlocks = 1024;
    public static IReadOnlyList<EquipmentSlot> ArmorSlots { get; } = Array.AsReadOnly(new[]
    { EquipmentSlot.Head, EquipmentSlot.Shoulders, EquipmentSlot.Chest, EquipmentSlot.Gloves, EquipmentSlot.Belt, EquipmentSlot.Legs, EquipmentSlot.Boots });
    public static AppearanceWardrobeMemory Empty(string characterId)
    {
        var result = new AppearanceWardrobeMemory(characterId, 0, [], [], false, []); ValidateShape(result); return result;
    }
    public static AppearanceWardrobeMemory Observe(AppearanceWardrobeMemory memory, ProgressionState character, ProgressionDefinition definition)
    {
        Validate(memory, definition);
        if (character.CharacterId != memory.CharacterId) throw new InvalidDataException("Wardrobe belongs to another character.");
        var unlocked = memory.Unlocks.ToDictionary(u => u.ItemId, u => u.Rarity, StringComparer.Ordinal);
        foreach (var item in character.Items)
        {
            if (!Enum.IsDefined(item.Rarity) || !IsArmor(definition.Items.FirstOrDefault(d => d.Id == item.DefinitionId))) continue;
            if (!unlocked.TryGetValue(item.DefinitionId, out var rarity) || item.Rarity > rarity) unlocked[item.DefinitionId] = item.Rarity;
        }
        var result = Copy(memory) with { Unlocks = unlocked.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new WardrobeUnlock(p.Key, p.Value)).ToArray() };
        Validate(result, definition); return result;
    }
    public static AppearanceWardrobeMemory Select(AppearanceWardrobeMemory memory, EquipmentSlot slot, string itemId, ProgressionDefinition definition)
    {
        Validate(memory, definition);
        if (!ArmorSlots.Contains(slot) || itemId is null) throw new ArgumentException("Choose an armor slot and an unlocked appearance.");
        var result = Copy(memory);
        if (itemId.Length == 0) result.Overrides.Remove(slot); else result.Overrides[slot] = itemId;
        Validate(result, definition); return result;
    }
    public static AppearanceWardrobeMemory SetHelmetHidden(AppearanceWardrobeMemory memory, bool hidden)
    { ValidateShape(memory); return Copy(memory) with { HideHelmet = hidden }; }
    public static AppearanceWardrobeMemory Reset(AppearanceWardrobeMemory memory)
    { ValidateShape(memory); return Copy(memory) with { Overrides = [], HideHelmet = false }; }
    public static AppearanceWardrobeMemory SaveLook(AppearanceWardrobeMemory memory, string name, ProgressionDefinition definition)
    {
        Validate(memory, definition); name = NormalizeName(name);
        var result = Copy(memory); var looks = result.Looks.ToList();
        int index = looks.FindIndex(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        var look = new WardrobeLook(name, new(result.Overrides), result.HideHelmet);
        if (index >= 0) looks[index] = look;
        else if (looks.Count < MaximumLooks) looks.Add(look);
        else throw new InvalidOperationException("The wardrobe already has eight saved looks.");
        result = result with { Looks = looks.ToArray() }; Validate(result, definition); return result;
    }
    public static AppearanceWardrobeMemory ApplyLook(AppearanceWardrobeMemory memory, string name, ProgressionDefinition definition)
    {
        Validate(memory, definition); name = NormalizeName(name);
        var look = memory.Looks.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("This saved look does not exist.", nameof(name));
        return Copy(memory) with { Overrides = new(look.Overrides), HideHelmet = look.HideHelmet };
    }
    public static AppearanceWardrobeMemory DeleteLook(AppearanceWardrobeMemory memory, string name)
    {
        ValidateShape(memory); name = NormalizeName(name);
        if (!memory.Looks.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("This saved look does not exist.", nameof(name));
        return Copy(memory) with { Looks = memory.Looks.Where(l => !string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)).Select(l => new WardrobeLook(l.Name, new(l.Overrides), l.HideHelmet)).ToArray() };
    }
    public static void Validate(AppearanceWardrobeMemory memory, ProgressionDefinition definition)
    {
        ValidateShape(memory);
        foreach (var unlock in memory.Unlocks)
            if (!IsArmor(definition.Items.FirstOrDefault(i => i.Id == unlock.ItemId))) throw new InvalidDataException("Wardrobe appearance is unavailable in this armor catalog.");
        void Selection(SortedDictionary<EquipmentSlot, string> overrides)
        {
            foreach (var pair in overrides)
                if (!memory.Unlocks.Any(u => u.ItemId == pair.Value) || !definition.Items.Any(i => i.Id == pair.Value && i.Slots.Contains(pair.Key)))
                    throw new InvalidDataException("Wardrobe appearance is locked or incompatible with its armor slot.");
        }
        Selection(memory.Overrides); foreach (var look in memory.Looks) Selection(look.Overrides);
    }
    internal static bool IsArmor(ProductionItemDefinition? item) => item is not null && item.Hands == 0 && item.Slots.Length > 0 && item.Slots.All(ArmorSlots.Contains);
    internal static AppearanceWardrobeMemory Copy(AppearanceWardrobeMemory memory) => JsonData.Copy(memory);
    internal static string ChoicesHash(AppearanceWardrobeMemory memory) => JsonData.Hash(new { memory.Overrides, memory.HideHelmet, memory.Looks });
    private static string NormalizeName(string name)
    {
        if (name is null) throw new ArgumentException("Give the look a name.", nameof(name));
        name = name.Trim();
        if (name.Length is < 1 or > MaximumNameLength || name.Any(char.IsControl)) throw new ArgumentException("Look names need 1–32 characters without control characters.", nameof(name));
        return name;
    }
    private static void ValidateShape(AppearanceWardrobeMemory memory)
    {
        if (memory is null || string.IsNullOrWhiteSpace(memory.CharacterId) || memory.CharacterId.Length > 80 || memory.Revision < 0 || memory.Revision == long.MaxValue ||
            memory.Unlocks is null || memory.Unlocks.Length > MaximumUnlocks || memory.Unlocks.Any(u => u is null || string.IsNullOrWhiteSpace(u.ItemId) || u.ItemId.Length > 160 || !Enum.IsDefined(u.Rarity)) ||
            memory.Unlocks.Select(u => u.ItemId).Distinct(StringComparer.Ordinal).Count() != memory.Unlocks.Length || memory.Looks is null || memory.Looks.Length > MaximumLooks ||
            memory.Looks.Any(l => l is null || string.IsNullOrWhiteSpace(l.Name) || l.Name.Length > MaximumNameLength || l.Name != l.Name.Trim() || l.Name.Any(char.IsControl)) ||
            memory.Looks.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != memory.Looks.Length)
            throw new InvalidDataException("Invalid or oversized wardrobe memory.");
        void Selection(SortedDictionary<EquipmentSlot, string> overrides)
        {
            if (overrides is null || overrides.Count > ArmorSlots.Count || overrides.Any(p => !ArmorSlots.Contains(p.Key) || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 160))
                throw new InvalidDataException("Invalid wardrobe armor selection.");
        }
        Selection(memory.Overrides); foreach (var look in memory.Looks) Selection(look.Overrides);
    }
}
