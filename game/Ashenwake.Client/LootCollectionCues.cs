using Ashenwake.Core.Combat;

namespace Ashenwake.Client;

/// <summary>Only authoritative, newly collected inventory items receive pickup feedback.</summary>
internal sealed class LootCollectionCues
{
    private readonly HashSet<long> _seen = [];
    private readonly HashSet<long> _live = [];
    private long _baselineTick;
    internal int RetainedCount => _seen.Count;

    public void Reset(IReadOnlyList<CombatItem> inventory, long tick)
    {
        _baselineTick = tick; _seen.Clear();
        foreach (var item in inventory) _seen.Add(item.Id);
    }

    public void Trim(IReadOnlyList<CombatItem> inventory)
    {
        _live.Clear(); foreach (var item in inventory) _live.Add(item.Id);
        _seen.RemoveWhere(id => !_live.Contains(id));
    }

    public CombatItem? Observe(CombatEvent e, IReadOnlyList<CombatItem> inventory)
    {
        if (e.Kind != "LootPickedUp" || e.ActorId != 1 || e.Tick < _baselineTick) return null;
        var item = inventory.FirstOrDefault(i => i.Id == e.Amount && i.DefinitionId == e.ContentId);
        return item is not null && _seen.Add(item.Id) ? item : null;
    }
}
