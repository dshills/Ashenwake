using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Serialization;

/// <summary>Removes one published generation of additive legendary definitions, newest
/// first, to reconstruct its predecessor. Callers must fully validate the original
/// archive against that catalog before rebinding its content identities.</summary>
internal static class LegendaryCatalogMigration
{
    private sealed record Generation(string[] Items, string[] Powers);
    private static readonly Generation[] Generations =
    [
        new([LegendaryEquipment.LastToll, LegendaryEquipment.BroodkeepersKnot, LegendaryEquipment.TithebreakersGrasp], []),
        new([LegendaryEquipment.Grief, LegendaryEquipment.Widowthorn, LegendaryEquipment.Emberwake],
            [LegendaryEquipment.GriefPower, LegendaryEquipment.WidowthornPower, LegendaryEquipment.EmberwakePower]),
        new(["item.crown_unsworn", "item.last_witness", "item.stolen_hour"],
            ["property.unspoken_verdict", "property.witness_vow", "property.borrowed_hour"]),
        new(["item.rotwake_signet", "item.mourning_choir", "item.furnaceheart_cinch"],
            ["property.virulent_wake", "property.rallying_chorus", "property.cinder_cycle"]),
        new([LegendaryEquipment.Pyre, LegendaryEquipment.Oath, LegendaryEquipment.Widow],
            [LegendaryEquipment.PyrePower, LegendaryEquipment.OathPower, LegendaryEquipment.WidowPower])
    ];

    internal static bool TryPrevious(string combatJson, ProgressionContent policy,
        out string previousCombatJson, out ProgressionContent previousPolicy)
    {
        var node = JsonNode.Parse(combatJson)!;
        var items = node["items"]!.AsArray();
        var source = policy.Capture();
        var generation = Generations.FirstOrDefault(g => Contains(source, g) ||
            items.Any(item => g.Items.Contains(item!["id"]!.GetValue<string>(), StringComparer.Ordinal)));
        previousCombatJson = combatJson; previousPolicy = policy;
        if (generation is null) return false;
        previousPolicy = PreviousPolicy(policy, generation);
        for (int i = items.Count - 1; i >= 0; i--)
            if (generation.Items.Contains(items[i]!["id"]!.GetValue<string>(), StringComparer.Ordinal)) items.RemoveAt(i);
        // Preserve the original optional-field shape of maintained legacy combat bundles.
        previousCombatJson = node.ToJsonString();
        return true;
    }

    internal static ProgressionContent PreviousPolicy(ProgressionContent policy)
    {
        var source = policy.Capture();
        var generation = Generations.FirstOrDefault(g => Contains(source, g));
        return generation is null ? policy : PreviousPolicy(policy, generation);
    }

    private static bool Contains(ProgressionDefinition source, Generation generation)
        => source.Items.Any(i => generation.Items.Contains(i.Id, StringComparer.Ordinal)) ||
            source.Properties.Any(p => generation.Powers.Contains(p.Id, StringComparer.Ordinal));

    private static ProgressionContent PreviousPolicy(ProgressionContent policy, Generation generation)
    {
        var source = policy.Capture();
        if (!Contains(source, generation)) return policy;
        return ProgressionContent.Create(source with
        {
            Items = source.Items.Where(i => !generation.Items.Contains(i.Id, StringComparer.Ordinal)).ToArray(),
            Properties = source.Properties.Where(p => !generation.Powers.Contains(p.Id, StringComparer.Ordinal)).ToArray()
        });
    }

    internal static ProductionSnapshot Rebind(ProductionSnapshot state, string combatJson,
        AdventureContent adventure, ProgressionContent policy)
    {
        string identity = CombatContent.Parse(combatJson).Identity;
        return state with
        {
            Expedition = state.Expedition with
            {
                AdventureHash = ProductionContent.ResolveAdventure(combatJson, adventure).Hash,
                Combat = RebindCombat(state.Expedition.Combat, identity)
            },
            Progression = state.Progression with
            {
                Character = state.Progression.Character with
                { ContentHash = ProductionContent.Resolve(combatJson, policy, adventure).Hash }
            }
        };
    }

    internal static CampaignRuntimeSnapshot Rebind(CampaignRuntimeSnapshot state, string combatJson,
        AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
        => state with
        {
            Production = Rebind(state.Production, combatJson, adventure, CampaignRuntimeSession.ResolvePolicy(policy, campaign)),
            Combat = RebindCombat(state.Combat, CombatContent.Parse(combatJson).Identity),
            ClearedRooms = state.ClearedRooms is null ? null : new SortedDictionary<string, CombatSnapshot>(
                state.ClearedRooms.ToDictionary(pair => pair.Key, pair => RebindCombat(pair.Value, CombatContent.Parse(combatJson).Identity)), StringComparer.Ordinal)
        };

    internal static EndgameRuntimeSnapshot Rebind(EndgameRuntimeSnapshot state, string combatJson,
        AdventureContent adventure, ProgressionContent policy, CampaignContent campaign)
    {
        string identity = CombatContent.Parse(combatJson).Identity;
        return state with
        {
            Campaign = Rebind(state.Campaign, combatJson, adventure, EndgameProgression.Resolve(policy), campaign),
            Combat = state.Combat is null ? null : RebindCombat(state.Combat, identity),
            Manifest = state.Manifest is null ? null : state.Manifest with { ContentHash = identity },
            RegionalHunts = state.RegionalHunts is null ? null : state.RegionalHunts with
            { Combat = state.RegionalHunts.Combat is null ? null : RebindCombat(state.RegionalHunts.Combat, identity) },
            RoamingChampions = state.RoamingChampions is null ? null : state.RoamingChampions with
            { Combat = state.RoamingChampions.Combat is null ? null : RebindCombat(state.RoamingChampions.Combat, identity) },
            SecretChambers = state.SecretChambers is null ? null : state.SecretChambers with
            { Combat = state.SecretChambers.Combat is null ? null : RebindCombat(state.SecretChambers.Combat, identity) }
        };
    }

    private static CombatSnapshot RebindCombat(CombatSnapshot state, string identity)
        => state with
        {
            ContentHash = identity,
            Endgame = state.Endgame is null ? null : state.Endgame with
            { Manifest = state.Endgame.Manifest with { ContentHash = identity } }
        };
}
