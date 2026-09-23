using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Progression;

public sealed record StashTab(string Id, string Name);
public sealed record StashLocation(long ItemId, string TabId);
public sealed record PersonalStashState
{
    public int SchemaVersion { get; init; } = 1;
    public StashTab[] Tabs { get; init; } = CharacterStash.DefaultTabs();
    public StashLocation[] Locations { get; init; } = [];
}
public sealed record StashItemReference(string Kind, string Id, string Name);
public sealed record StashItemView(PermanentItem Item, string TabId, bool Equipped, StashItemReference[] References);
public sealed record StashTabView(string Id, string Name, int Count, int Capacity);
public sealed record PersonalStashView(StashTabView[] Tabs, StashItemView[] Backpack, StashItemView[] Stored,
    bool CanUse, string Requirement, int BackpackCount, int BackpackCapacity);
public static class PersonalStashCatalog
{
    public const string InteractionId = "hub.stash";
    public static readonly Position Position = new(3500, -3500);
    public const int Range = 1800;
}
public static class CharacterStash
{
    public const int TabCount = 4, TabCapacity = 128, BackpackCapacity = 512, MaximumNameLength = 32;
    public static StashTab[] DefaultTabs() => [new("stash.1", "Weapons"), new("stash.2", "Armor"), new("stash.3", "Relics"), new("stash.4", "Keepsakes")];
    public static bool IsStored(ProgressionState state, long id) => state.Stash?.Locations.Any(l => l.ItemId == id) == true;
    public static string TabForItem(ProgressionState state, long id) => state.Stash?.Locations.FirstOrDefault(l => l.ItemId == id)?.TabId ?? "";
    public static string TabName(ProgressionState state, string tabId) => (state.Stash?.Tabs ?? DefaultTabs()).FirstOrDefault(t => t.Id == tabId)?.Name ?? "";
    public static int BackpackCount(ProgressionState state) => state.Items.Length - (state.Stash?.Locations.Length ?? 0);
    public static bool ValidTab(string? id) => id is { Length: 7 } && id.StartsWith("stash.", StringComparison.Ordinal) && id[6] is >= '1' and <= '4';
    internal static bool ValidName(string? name) => name is { Length: > 0 and <= MaximumNameLength } && name == name.Trim() &&
        !string.IsNullOrWhiteSpace(name) && !name.Any(c => char.IsControl(c) || char.IsSurrogate(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format);
}

public sealed partial class ProgressionSession
{
    public PersonalStashView Stash
    {
        get
        {
            var state = snapshot.Character;
            var tabs = (state.Stash?.Tabs ?? CharacterStash.DefaultTabs()).Select(t => new StashTabView(t.Id, t.Name,
                state.Stash?.Locations.Count(l => l.TabId == t.Id) ?? 0, CharacterStash.TabCapacity)).ToArray();
            var items = state.Items.Select(item => new StashItemView(Ashenwake.Core.Content.JsonData.Copy(item), CharacterStash.TabForItem(state, item.Id), state.Equipment.ContainsValue(item.Id),
                [.. (state.EquipmentPresets ?? []).Where(p => p.Equipment.ContainsValue(item.Id)).Select(p => new StashItemReference("Outfit", p.Id, p.Name)),
                 .. (state.BuildLoadouts ?? []).Where(p => p.Equipment.ContainsValue(item.Id)).Select(p => new StashItemReference("Build", p.Id, p.Name))])).ToArray();
            return new(tabs, items.Where(i => i.TabId == "").ToArray(), items.Where(i => i.TabId != "").ToArray(), true, "",
                CharacterStash.BackpackCount(state), CharacterStash.BackpackCapacity);
        }
    }
    public ProgressionResult StoreItem(string operationId, long itemId, string tabId)
        => Change(operationId, new { Action = "StoreItem", itemId, tabId }, (next, events) =>
        {
            var state = next.Character;
            if (!CharacterStash.ValidTab(tabId)) return "Choose one of the four stash tabs.";
            if (!state.Items.Any(i => i.Id == itemId)) return "This item is no longer owned.";
            if (CharacterStash.IsStored(state, itemId)) return "This item is already stored; move it between tabs or retrieve it.";
            if (state.Equipment.ContainsValue(itemId)) return "Unequip this item before storing it.";
            if ((state.Stash?.Locations.Count(l => l.TabId == tabId) ?? 0) >= CharacterStash.TabCapacity) return "This stash tab is full.";
            state.Stash ??= new(); state.Stash = state.Stash with { Locations = [.. state.Stash.Locations.Append(new(itemId, tabId)).OrderBy(l => l.ItemId)] };
            events.Add("ItemStored:" + itemId); return null;
        });
    public ProgressionResult RetrieveItem(string operationId, long itemId)
        => Change(operationId, new { Action = "RetrieveItem", itemId }, (next, events) =>
        {
            var state = next.Character;
            if (!CharacterStash.IsStored(state, itemId)) return "Choose an item currently stored in your stash.";
            if (CharacterStash.BackpackCount(state) >= CharacterStash.BackpackCapacity) return "Your backpack is full; store or remove an item first.";
            state.Stash = state.Stash! with { Locations = state.Stash.Locations.Where(l => l.ItemId != itemId).ToArray() };
            events.Add("ItemRetrieved:" + itemId); return null;
        });
    public ProgressionResult MoveStashedItem(string operationId, long itemId, string tabId)
        => Change(operationId, new { Action = "MoveStashedItem", itemId, tabId }, (next, events) =>
        {
            var state = next.Character;
            if (!CharacterStash.ValidTab(tabId) || !CharacterStash.IsStored(state, itemId)) return "Choose a stored item and a valid destination tab.";
            if (CharacterStash.TabForItem(state, itemId) == tabId) return "This item is already in that tab.";
            if (state.Stash!.Locations.Count(l => l.TabId == tabId) >= CharacterStash.TabCapacity) return "This stash tab is full.";
            state.Stash = state.Stash with { Locations = state.Stash.Locations.Select(l => l.ItemId == itemId ? l with { TabId = tabId } : l).ToArray() };
            events.Add("StashedItemMoved:" + itemId); return null;
        });
    public ProgressionResult RenameStashTab(string operationId, string tabId, string name)
        => Change(operationId, new { Action = "RenameStashTab", tabId, name }, (next, events) =>
        {
            // Bound raw input separately from the 32-character trimmed display name.
            if (!CharacterStash.ValidTab(tabId) || name is null || name.Length > 128 || name.Any(char.IsControl) || !CharacterStash.ValidName(name.Trim()))
                return "Use a tab name of 1–32 visible characters, without line breaks or control characters.";
            var state = next.Character; var tabs = state.Stash?.Tabs ?? CharacterStash.DefaultTabs();
            if (tabs.Any(t => t.Id != tabId && string.Equals(t.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))) return "Another stash tab already uses that name.";
            state.Stash = (state.Stash ?? new()) with { Tabs = tabs.Select(t => t.Id == tabId ? t with { Name = name.Trim() } : t).ToArray() };
            events.Add("StashTabRenamed:" + tabId); return null;
        });
    private static void ValidatePersonalStash(ProgressionState state)
    {
        if (state.Stash is not { } stash) return;
        if (stash.SchemaVersion != 1 || stash.Tabs is not { Length: CharacterStash.TabCount } || stash.Tabs.Any(t => t is null || !CharacterStash.ValidTab(t.Id) || !CharacterStash.ValidName(t.Name)) ||
            !stash.Tabs.Select(t => t.Id).SequenceEqual(CharacterStash.DefaultTabs().Select(t => t.Id)) || stash.Tabs.Select(t => t.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != CharacterStash.TabCount ||
            stash.Locations is null || stash.Locations.Length > CharacterStash.TabCount * CharacterStash.TabCapacity ||
            stash.Locations.Any(l => l is null || !CharacterStash.ValidTab(l.TabId) || !state.Items.Any(i => i.Id == l.ItemId) || state.Equipment.ContainsValue(l.ItemId)) ||
            stash.Locations.Select(l => l.ItemId).Distinct().Count() != stash.Locations.Length || !stash.Locations.Select(l => l.ItemId).SequenceEqual(stash.Locations.Select(l => l.ItemId).Order()) ||
            stash.Locations.GroupBy(l => l.TabId).Any(g => g.Count() > CharacterStash.TabCapacity))
            throw new InvalidDataException("Invalid personal stash tabs, capacity, or item ownership.");
    }
}
