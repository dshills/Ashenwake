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
    private static bool AddedItem(string id) => id is LegendaryEquipment.Pyre or LegendaryEquipment.Oath or LegendaryEquipment.Widow;
    private static bool AddedPower(string id) => id is LegendaryEquipment.PyrePower or LegendaryEquipment.OathPower or LegendaryEquipment.WidowPower;
    private static bool MidgameItem(string id) => id is "item.rotwake_signet" or "item.mourning_choir" or "item.furnaceheart_cinch";
    private static bool MidgamePower(string id) => id is "property.virulent_wake" or "property.rallying_chorus" or "property.cinder_cycle";

    internal static bool TryPrevious(string combatJson, ProgressionContent policy,
        out string previousCombatJson, out ProgressionContent previousPolicy)
    {
        var node = JsonNode.Parse(combatJson)!;
        var items = node["items"]!.AsArray();
        bool midgame = HasMidgame(policy) || items.Any(item => MidgameItem(item!["id"]!.GetValue<string>()));
        previousPolicy = PreviousPolicy(policy, midgame);
        bool changed = previousPolicy.Hash != policy.Hash;
        for (int i = items.Count - 1; i >= 0; i--)
            if (midgame ? MidgameItem(items[i]!["id"]!.GetValue<string>()) : AddedItem(items[i]!["id"]!.GetValue<string>()))
            { items.RemoveAt(i); changed = true; }
        // Preserve the original optional-field shape of maintained legacy combat bundles.
        previousCombatJson = changed ? node.ToJsonString() : combatJson;
        return changed;
    }

    internal static ProgressionContent PreviousPolicy(ProgressionContent policy)
        => PreviousPolicy(policy, HasMidgame(policy));

    private static bool HasMidgame(ProgressionContent policy)
    {
        var source = policy.Capture();
        return source.Items.Any(i => MidgameItem(i.Id)) || source.Properties.Any(p => MidgamePower(p.Id));
    }

    private static ProgressionContent PreviousPolicy(ProgressionContent policy, bool midgame)
    {
        var source = policy.Capture();
        bool RemoveItem(string id) => midgame ? MidgameItem(id) : AddedItem(id);
        bool RemovePower(string id) => midgame ? MidgamePower(id) : AddedPower(id);
        if (!source.Items.Any(i => RemoveItem(i.Id)) &&
            !source.Properties.Any(p => RemovePower(p.Id))) return policy;
        return ProgressionContent.Create(source with
        {
            Items = source.Items.Where(i => !RemoveItem(i.Id)).ToArray(),
            Properties = source.Properties.Where(p => !RemovePower(p.Id)).ToArray()
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
            Manifest = state.Manifest is null ? null : state.Manifest with { ContentHash = identity }
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
