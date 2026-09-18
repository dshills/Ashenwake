using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Progression;

/// <summary>Bounded deterministic affix policy. An owned instance always receives the same roll; rolling consumes no combat/resource random stream.</summary>
public static class ProgressionLoot
{
    public static SortedDictionary<string, int> RollAffixes(ProgressionContent content, string definitionId, ItemRarity rarity, ulong worldSeed, long instanceId)
    {
        if (!Enum.IsDefined(rarity) || instanceId <= 0) throw new InvalidDataException("Invalid loot rarity or permanent identity.");
        var definition = content.Data.Items.FirstOrDefault(i => i.Id == definitionId) ?? throw new InvalidDataException("Unknown loot definition.");
        if (rarity == ItemRarity.Godwrought || definition.Property != "" && rarity != ItemRarity.Legendary) throw new InvalidDataException("Special items require their authored rarity and progression policy.");
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        ulong random = unchecked(worldSeed ^ (ulong)instanceId * 0xD6E8FEB86659FD93UL);
        for (int index = 0; index < ProgressionContent.AffixLimit(rarity); index++)
        {
            var eligible = content.Data.Affixes.Where(a => !result.ContainsKey(a.Id) && definition.Slots.All(a.Slots.Contains) && (!a.Advanced || rarity >= ItemRarity.Relic) &&
                !a.Excludes.Any(result.ContainsKey) && !content.Data.Affixes.Any(existing => result.ContainsKey(existing.Id) && existing.Excludes.Contains(a.Id))).OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
            if (eligible.Length == 0) break;
            int ticket = SeededRandom.Range(ref random, eligible.Sum(a => a.Weight));
            var chosen = eligible[0];
            foreach (var affix in eligible) { if (ticket < affix.Weight) { chosen = affix; break; } ticket -= affix.Weight; }
            result[chosen.Id] = chosen.Minimum + SeededRandom.Range(ref random, chosen.Maximum - chosen.Minimum + 1);
        }
        return result;
    }
}
