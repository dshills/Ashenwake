using Ashenwake.Core.Combat;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public static class ProductionContent
{
    /// <summary>Merge authored progression policy with the immutable combat registry so every persistent roll has a valid runtime definition.</summary>
    public static ProgressionContent Resolve(string combatJson, ProgressionContent policy, AdventureContent? adventure = null)
    {
        var combat = CombatContent.Parse(combatJson); var definition = policy.Capture();
        var items = combat.Items.Select(item =>
        {
            var authored = definition.Items.FirstOrDefault(i => i.Id == item.Id);
            var slots = item.CompatibleSlots is { Length: > 0 } ? item.CompatibleSlots : [item.Slot];
            if (slots.Any(slot => !Enum.TryParse<EquipmentSlot>(slot, out _))) throw new InvalidDataException("Combat item uses an unknown permanent equipment slot.");
            return authored is null
                ? new ProductionItemDefinition(item.Id, slots.Select(Enum.Parse<EquipmentSlot>).ToArray(), item.Hands, item.Disciplines ?? [], "", item.Damage, item.Armor, item.CriticalBasisPoints)
                : authored with { Slots = slots.Select(Enum.Parse<EquipmentSlot>).ToArray(), Hands = item.Hands, Disciplines = item.Disciplines ?? [], BaseDamage = item.Damage, BaseArmor = item.Armor, BaseCriticalBasisPoints = item.CriticalBasisPoints };
        }).ToArray();
        var skills = combat.Skills.Where(s => definition.Disciplines.Any(d => d.Id == s.Discipline))
            .Select(skill => new SkillMasteryDefinition(skill.Id, skill.Discipline, combat.Mutations.Where(m => m.SkillId == skill.Id).Select(m => m.Id).ToArray())).ToArray();
        return ProgressionContent.Create(definition with { Items = items, Skills = skills, FragmentIds = combat.Fragments.Select(f => f.Id).ToArray(), DiscoveryIds = definition.DiscoveryIds.Concat(adventure?.Capture().Rooms.Select(r => r.Discovery) ?? []).Distinct().Order(StringComparer.Ordinal).ToArray() });
    }
    public static AdventureContent ResolveAdventure(string combatJson, AdventureContent policy)
    {
        var combat = CombatContent.Parse(combatJson); var definition = policy.Capture();
        var fragments = combat.Fragments.Select(fragment => definition.Fragments.FirstOrDefault(f => f.Id == fragment.Id) ??
            new AnatomyFragment(fragment.Id, fragment.Slot.ToString(), fragment.Lineage switch
            {
                "Vael" => ["Flame"],
                "Serath" => ["Death", "Memory"],
                "Ilyra" => ["Beast", "Hunger"],
                "Orrun" => ["Oath"],
                "Nhal" => ["Shadow", "Void"],
                _ => [fragment.Lineage]
            }, fragment.Resonance)).ToArray();
        return AdventureContent.Create(definition with { Fragments = fragments });
    }
}
