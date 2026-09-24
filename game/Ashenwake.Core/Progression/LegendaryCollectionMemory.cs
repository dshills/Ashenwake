using System.Text.Json.Serialization;

namespace Ashenwake.Core.Progression;

/// <summary>Optional, character-local presentation knowledge. Never part of a gameplay snapshot or replay.</summary>
public sealed record LegendaryCollectionMemory(
    [property: JsonRequired] string CharacterId,
    [property: JsonRequired] string[] DiscoveredItems,
    [property: JsonRequired] string TrackedItem = "");

public static class LegendaryCollection
{
    public static LegendaryCollectionMemory Empty(string characterId)
    {
        var memory = new LegendaryCollectionMemory(characterId, []); Validate(memory); return memory;
    }

    /// <summary>Only current ownership and an extracted innate power prove a discovery. Engravings and quest completion do not.</summary>
    public static LegendaryCollectionMemory Observe(LegendaryCollectionMemory memory, ProgressionState character)
    {
        Validate(memory);
        if (character.CharacterId != memory.CharacterId) throw new InvalidDataException("Legendary collection belongs to another character.");
        var discovered = new SortedSet<string>(memory.DiscoveredItems, StringComparer.Ordinal);
        foreach (var item in character.Items)
            if (IsItem(item.DefinitionId)) discovered.Add(item.DefinitionId);
        foreach (string power in character.PropertyLibrary)
            if (ItemForPower(power) is { Length: > 0 } item) discovered.Add(item);
        return memory with { DiscoveredItems = discovered.ToArray() };
    }

    public static LegendaryCollectionMemory Track(LegendaryCollectionMemory memory, string itemId)
    {
        Validate(memory);
        if (itemId is null || itemId.Length > 0 && !IsItem(itemId))
            throw new ArgumentException("Choose a legendary item, or clear tracking.", nameof(itemId));
        return memory with { DiscoveredItems = memory.DiscoveredItems.ToArray(), TrackedItem = itemId };
    }

    public static string ItemForPower(string power) => string.IsNullOrEmpty(power) ? "" : LegendaryCollectionCatalog.Entries.FirstOrDefault(entry => entry.PowerId == power)?.ItemId ?? "";
    private static bool IsItem(string id) => LegendaryCollectionCatalog.Entries.Any(entry => entry.ItemId == id);

    internal static void Validate(LegendaryCollectionMemory memory)
    {
        if (memory is null || string.IsNullOrWhiteSpace(memory.CharacterId) || memory.CharacterId.Length > 80 ||
            memory.DiscoveredItems is null || memory.DiscoveredItems.Length > LegendaryCollectionCatalog.Entries.Count ||
            memory.DiscoveredItems.Any(id => id is null || !IsItem(id)) ||
            memory.DiscoveredItems.Distinct(StringComparer.Ordinal).Count() != memory.DiscoveredItems.Length ||
            memory.TrackedItem is null || memory.TrackedItem.Length > 0 && !IsItem(memory.TrackedItem))
            throw new InvalidDataException("Invalid or oversized legendary collection memory.");
    }
}
