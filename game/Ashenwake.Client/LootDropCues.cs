using Ashenwake.Core.Combat;

namespace Ashenwake.Client;

/// <summary>Presentation receipts for live ground drops only. Loading seeds a quiet baseline.</summary>
internal sealed class LootDropCues
{
    private readonly HashSet<long> _seen = [];
    private readonly HashSet<long> _live = [];
    internal int RetainedCount => _seen.Count;
    public void Reset(IReadOnlyList<CombatLoot> loot)
    { _seen.Clear(); foreach (var drop in loot) _seen.Add(drop.Id); }

    public void Trim(IReadOnlyList<CombatLoot> loot)
    {
        _live.Clear(); foreach (var drop in loot) _live.Add(drop.Id);
        _seen.RemoveWhere(id => !_live.Contains(id));
    }

    public CombatLoot? Observe(CombatEvent e, IReadOnlyList<CombatLoot> loot)
    {
        if (e.Kind != "LootDropped") return null;
        var drop = loot.FirstOrDefault(l => l.Id == e.Amount && l.Item.DefinitionId == e.ContentId);
        if (drop is null || !_seen.Add(drop.Id)) return null;
        return drop.Item.Rarity is "Rare" or "Relic" or "Legendary" or "Godwrought" ? drop : null;
    }

    public static string Cue(string rarity) => rarity switch
    {
        "Rare" => "drop_rare",
        "Relic" => "drop_relic",
        "Legendary" => "loot_legendary",
        "Godwrought" => "loot_godwrought",
        _ => ""
    };
}
