using System.Text.Json.Serialization;

namespace Ashenwake.Core.Progression;

/// <summary>Optional character-local guidance, outside authoritative saves and replays.</summary>
public sealed record OpeningGuidanceMemory(
    [property: JsonRequired] string CharacterId,
    [property: JsonRequired] bool Enabled,
    [property: JsonRequired] string[] Dismissed,
    [property: JsonRequired] string[] Completed);

public sealed record OpeningGuidanceCard(string Id, string Title, string Text, string Action, string Target, string Category, bool Completed = false);
public sealed record OpeningGuidanceView(OpeningGuidanceCard? Hint, OpeningGuidanceCard[] Steps, OpeningGuidanceCard[] Services, OpeningGuidanceCard[] Basics);

public static partial class OpeningGuidance
{
    private static readonly HashSet<string> KnownIds = new(StringComparer.Ordinal)
    {
        "hint.move", "hint.interact", "hint.dodge", "hint.interrupt", "hint.loot",
        "build.pyre", "build.heart", "build.practice", "service.training", "service.stash", "service.hunts",
        "unlock.Tempering", "unlock.Rebinding", "unlock.Engraving", "unlock.Extraction", "unlock.DivineGrafting", "unlock.Purification"
    };
    public static OpeningGuidanceMemory Empty(string characterId)
    {
        var memory = new OpeningGuidanceMemory(characterId, true, [], []); Validate(memory); return memory;
    }
    public static void Validate(OpeningGuidanceMemory memory)
    {
        bool Valid(string[]? values) => values is not null && values.Length <= KnownIds.Count &&
            values.All(id => id is not null && KnownIds.Contains(id)) && values.Distinct(StringComparer.Ordinal).Count() == values.Length;
        if (memory is null || string.IsNullOrWhiteSpace(memory.CharacterId) || memory.CharacterId.Length > 80 || !Valid(memory.Dismissed) || !Valid(memory.Completed))
            throw new InvalidDataException("Invalid or oversized opening guidance memory.");
    }
    public static OpeningGuidanceMemory Dismiss(OpeningGuidanceMemory memory, string id) => Update(memory, id, true);
    public static OpeningGuidanceMemory Complete(OpeningGuidanceMemory memory, string id) => Update(memory, id, false);
    public static OpeningGuidanceMemory SetEnabled(OpeningGuidanceMemory memory, bool enabled)
    {
        Validate(memory); if (memory.Enabled == enabled) return memory;
        return memory with { Enabled = enabled, Dismissed = memory.Dismissed.ToArray(), Completed = memory.Completed.ToArray() };
    }
    private static OpeningGuidanceMemory Update(OpeningGuidanceMemory memory, string id, bool dismissed)
    {
        Validate(memory);
        if (id is null || !KnownIds.Contains(id)) throw new ArgumentException("Unknown opening guidance card.", nameof(id));
        if ((dismissed ? memory.Dismissed : memory.Completed).Contains(id, StringComparer.Ordinal)) return memory;
        var values = new SortedSet<string>(dismissed ? memory.Dismissed : memory.Completed, StringComparer.Ordinal) { id };
        return memory with { Dismissed = dismissed ? values.ToArray() : memory.Dismissed.ToArray(), Completed = dismissed ? memory.Completed.ToArray() : values.ToArray() };
    }
}
