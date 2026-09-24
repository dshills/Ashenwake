using System.Text.Json.Serialization;

namespace Ashenwake.Core.Progression;

public sealed record BestiaryDefeat([property: JsonRequired] string Token, [property: JsonRequired] string EntryId,
    [property: JsonRequired] bool IsElite);
/// <summary>Optional presentation knowledge, isolated from authoritative snapshots and replays.</summary>
public sealed record BestiaryMemory([property: JsonRequired] string CharacterId,
    [property: JsonRequired] string[] DiscoveredEntries, [property: JsonRequired] BestiaryDefeat[] Defeats,
    [property: JsonRequired] bool RecordLimitReached = false);
public sealed record BestiaryEntryView(string Id, string EnemyId, string Role, string Name, BestiaryKind Kind,
    string Lore, string[] Regions, string[] CombatNotes, int ArmorBasisPoints, int ResistanceBasisPoints,
    string[] RewardItemIds, string[] HuntIds, bool Discovered, bool Defeated, long Defeats, long EliteDefeats);

public static class Bestiary
{
    // Never evict old tokens: reopening an old save must not manufacture new victories.
    // Once full, discoveries continue but new defeat counts stop and the UI reports the limit.
    public const int MaximumDefeats = 16384;
    public static BestiaryMemory Empty(string characterId)
    {
        ValidateIdentity(characterId); return new(characterId, [], []);
    }
    public static BestiaryMemory Observe(BestiaryMemory memory, BestiaryCatalog catalog,
        IEnumerable<string> seen, IEnumerable<BestiaryDefeat> defeated)
    {
        Validate(memory, catalog); ArgumentNullException.ThrowIfNull(seen); ArgumentNullException.ThrowIfNull(defeated);
        var discovered = new SortedSet<string>(memory.DiscoveredEntries, StringComparer.Ordinal);
        foreach (string id in seen)
        {
            Require(id is not null && catalog.Find(id) is not null, "Unknown bestiary discovery."); discovered.Add(id);
        }
        var records = memory.Defeats.ToDictionary(d => d.Token, StringComparer.Ordinal);
        bool limit = memory.RecordLimitReached;
        foreach (var defeat in defeated)
        {
            ValidateDefeat(defeat, catalog);
            var canonical = defeat with { Token = defeat.Token.ToUpperInvariant() };
            discovered.Add(canonical.EntryId);
            if (records.TryGetValue(canonical.Token, out var existing))
            { Require(existing == canonical, "A bestiary defeat token has conflicting identities."); continue; }
            if (records.Count >= MaximumDefeats) { limit = true; continue; }
            records.Add(canonical.Token, canonical);
        }
        return new(memory.CharacterId, discovered.ToArray(), records.Values.OrderBy(d => d.Token, StringComparer.Ordinal).ToArray(), limit);
    }
    public static BestiaryEntryView[] Project(BestiaryMemory memory, BestiaryCatalog catalog)
    {
        Validate(memory, catalog);
        var discovered = memory.DiscoveredEntries.ToHashSet(StringComparer.Ordinal);
        var counts = memory.Defeats.GroupBy(d => d.EntryId).ToDictionary(g => g.Key, g => (Total: g.LongCount(), Elite: g.LongCount(d => d.IsElite)), StringComparer.Ordinal);
        return catalog.Entries.Select(e =>
        {
            bool known = discovered.Contains(e.Id); var count = counts.GetValueOrDefault(e.Id); bool defeated = count.Total > 0;
            return new BestiaryEntryView(e.Id, known ? e.EnemyId : "", known ? e.Role : "", known ? e.Name : "Undiscovered creature", e.Kind,
                known ? e.Lore : "", known ? e.Regions.ToArray() : [], defeated ? e.CombatNotes.ToArray() : [],
                defeated ? e.ArmorBasisPoints : -1, defeated ? e.ResistanceBasisPoints : -1,
                defeated ? e.RewardItemIds.ToArray() : [], defeated ? e.HuntIds.ToArray() : [], known, defeated, count.Total, count.Elite);
        }).ToArray();
    }
    public static void Validate(BestiaryMemory memory, BestiaryCatalog catalog)
    {
        Require(memory is not null, "Missing bestiary memory."); ValidateIdentity(memory.CharacterId);
        Require(memory.DiscoveredEntries is not null && memory.DiscoveredEntries.Length <= catalog.Entries.Count &&
            memory.DiscoveredEntries.All(id => id is not null && catalog.Find(id) is not null) &&
            memory.DiscoveredEntries.Distinct(StringComparer.Ordinal).Count() == memory.DiscoveredEntries.Length, "Invalid bestiary discoveries.");
        Require(memory.Defeats is not null && memory.Defeats.Length <= MaximumDefeats, "Invalid or oversized bestiary records.");
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var discovered = memory.DiscoveredEntries.ToHashSet(StringComparer.Ordinal);
        foreach (var defeat in memory.Defeats)
        {
            ValidateDefeat(defeat, catalog);
            Require(defeat.Token == defeat.Token.ToUpperInvariant() && tokens.Add(defeat.Token) && discovered.Contains(defeat.EntryId), "Invalid bestiary record identity or missing discovery.");
        }
    }
    private static void ValidateDefeat(BestiaryDefeat defeat, BestiaryCatalog catalog)
        => Require(defeat is not null && defeat.Token is { Length: 64 } && defeat.Token.All(char.IsAsciiHexDigit) &&
            defeat.EntryId is not null && catalog.Find(defeat.EntryId) is not null, "Invalid bestiary defeat.");
    private static void ValidateIdentity(string characterId) => Require(!string.IsNullOrWhiteSpace(characterId) && characterId.Length <= 80, "Invalid bestiary character identity.");
    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool valid, string message)
    { if (!valid) throw new InvalidDataException(message); }
}
