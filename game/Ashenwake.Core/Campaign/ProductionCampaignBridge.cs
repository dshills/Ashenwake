using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    internal ProgressionResult GrantCryptTestament()
    {
        SynchronizeItemSequence();
        var result = progression.GrantItem("campaign.crypt.testament", "item.serath_shroud", ItemRarity.Rare);
        if (result.Success) ProjectPermanentInventory();
        return result;
    }
    internal bool HasCryptTestamentReceipt => progression.CharacterState.OperationReceipts.TryGetValue("campaign.crypt.testament", out string? hash) &&
        hash == JsonData.Hash(new { Action = "GrantItem", definitionId = "item.serath_shroud", rarity = ItemRarity.Rare, Affixes = new SortedDictionary<string, int>() });
    internal ProgressionResult GrantBriarTestament()
    {
        SynchronizeItemSequence();
        var result = progression.GrantItem("campaign.briar.testament", "item.stone_seal", ItemRarity.Rare);
        if (result.Success) ProjectPermanentInventory();
        return result;
    }
    internal bool HasBriarTestamentReceipt => progression.CharacterState.OperationReceipts.TryGetValue("campaign.briar.testament", out string? hash) &&
        hash == JsonData.Hash(new { Action = "GrantItem", definitionId = "item.stone_seal", rarity = ItemRarity.Rare, Affixes = new SortedDictionary<string, int>() });
    internal ProgressionResult GrantFoundryTestament()
    {
        SynchronizeItemSequence();
        var result = progression.GrantItem("campaign.foundry.testament", "item.cinder_edge", ItemRarity.Rare);
        if (result.Success) ProjectPermanentInventory();
        return result;
    }
    internal bool HasFoundryTestamentReceipt => progression.CharacterState.OperationReceipts.TryGetValue("campaign.foundry.testament", out string? hash) &&
        hash == JsonData.Hash(new { Action = "GrantItem", definitionId = "item.cinder_edge", rarity = ItemRarity.Rare, Affixes = new SortedDictionary<string, int>() });
    internal ProgressionResult GrantArchiveTestament()
    {
        SynchronizeItemSequence();
        var result = progression.GrantItem("campaign.archive.testament", "item.oath_plate", ItemRarity.Rare);
        if (result.Success) ProjectPermanentInventory();
        return result;
    }
    internal bool HasArchiveTestamentReceipt => progression.CharacterState.OperationReceipts.TryGetValue("campaign.archive.testament", out string? hash) &&
        hash == JsonData.Hash(new { Action = "GrantItem", definitionId = "item.oath_plate", rarity = ItemRarity.Rare, Affixes = new SortedDictionary<string, int>() });
    internal bool ContainsCampaignReceipt(string receipt) => progression.CharacterState.OperationReceipts.ContainsKey(receipt);
    internal bool HasCampaignReward(string receipt, int amount, int materials)
        => progression.CharacterState.OperationReceipts.TryGetValue(receipt, out string? hash) && hash == JsonData.Hash(new { Action = "Experience", amount, materials });
    internal void ReserveCampaignItemSequence(long sequence)
    {
        if (sequence <= progression.CharacterState.NextItemId) return;
        var next = progression.Capture(); next.Character.NextItemId = sequence; progression.AdoptAuthoritativeState(next); ProjectPermanentInventory();
    }
    internal bool ExternalReceiptCapacityAvailable => progression.CharacterState.OperationReceipts.Count < 99980;

    internal string[] ReconcileCampaignCombat(CombatSession arena, IReadOnlyList<CombatActorView> before, CombatEvent[] events, long campaignTick)
    {
        var messages = new List<string>(Expedition.ReconcileExternalGodwrought(before, events));
        bool relevant = messages.Count > 0 || events.Any(e => e.Kind is "LootPickedUp" or "LootDropped" or "AbilityStarted");
        if (relevant)
        {
            var external = arena.Capture(); var source = Expedition.Capture() with { Combat = external };
            var next = progression.Capture(); var state = next.Character; var known = state.Items.Select(i => i.Id).ToHashSet();
            var addedItems = external.Inventory.Where(i => !known.Contains(i.Id)).Select(i => ImportItem(i, source, rollAffixes: true)).ToArray();
            if (addedItems.Length > 0) state.Items = [.. state.Items, .. addedItems];
            state.NextItemId = Math.Max(state.NextItemId, external.NextObjectId);
            foreach (var mapping in source.GodwroughtItems)
            {
                var item = state.Items.Single(i => i.Id == mapping.Value);
                item.BurningKills = source.Adventure.Godwrought.Single(g => g.InstanceId == mapping.Key).BurningKills;
            }
            progression.AdoptAuthoritativeState(next);
            foreach (var started in events.Where(e => e.Kind == "AbilityStarted" && e.ActorId == 1))
            {
                if (!Content.Data.Skills.Any(s => s.Id == started.ContentId) || progression.CharacterState.Mastery.GetValueOrDefault(started.ContentId) >= 1000) continue;
                var mastery = progression.GainMastery($"campaign.mastery.{campaignTick}.{started.ActionId}", started.ContentId, 1);
                Require(mastery); messages.AddRange(mastery.Events);
            }
            ProjectPermanentInventory();
        }
        return messages.ToArray();
    }

    internal string[] GrantCampaignOutcome(string receipt, int experience, int materials, string[] discoveries, string[] residents, string[] unlocks, string fragment = "")
    {
        var events = new List<string>();
        var reward = progression.EarnExperience(receipt, experience, materials); Require(reward); events.AddRange(reward.Events);
        var next = progression.Capture(); next.Profile.Discoveries.UnionWith(discoveries); progression.AdoptAuthoritativeState(next);
        foreach (string unlock in unlocks)
        { var result = progression.UnlockProfile(receipt + "." + unlock, unlock); Require(result); events.AddRange(result.Events); }
        if (fragment != "") { var result = progression.GrantFragment(receipt + ".fragment", fragment); Require(result); events.AddRange(result.Events); }
        foreach (var (resident, objective) in new[] { ("Mara Vey", "objective.mara"), ("Torren Bale", "objective.torren"), ("Sister Cael", "objective.cael"), ("Oris Fen", "objective.oris"), ("Kesh", "objective.kesh") })
        {
            if (!residents.Contains(resident) || progression.CharacterState.CompletedObjectives.Contains(objective)) continue;
            var result = progression.CompleteObjective("campaign.rescue." + objective, objective); Require(result); events.AddRange(result.Events);
            if (resident == "Kesh")
            {
                SynchronizeItemSequence(); result = progression.GrantItem("campaign.kesh.legendary", "item.echo_ring", ItemRarity.Legendary);
                Require(result); events.AddRange(result.Events);
            }
        }
        ProjectPermanentInventory(); return events.ToArray();
    }

    internal CombatSession ProjectCampaignCombat(CombatSnapshot source)
    {
        var arena = CombatSession.Restore(combatJson, source);
        arena.ApplyProgressionBuild(DeriveBuild());
        var body = Expedition.Capture(); arena.ApplyAnatomy(body.Adventure.Anatomy);
        arena.ApplyAdventureBuild(Combat.Build);
        var state = arena.Capture(); var projected = Combat.Capture();
        state.Inventory.Clear(); state.Inventory.AddRange(projected.Inventory);
        state.Equipment.Clear(); foreach (var pair in projected.Equipment) state.Equipment[pair.Key] = pair.Value;
        state.Mutations.Clear(); foreach (var pair in projected.Mutations) state.Mutations[pair.Key] = pair.Value;
        state.NextObjectId = Math.Max(state.NextObjectId, projected.NextObjectId);
        return CombatSession.Restore(combatJson, state);
    }

    internal void ValidateCampaignCombat(CombatSnapshot arena)
    {
        var body = Combat.Capture();
        if (!arena.Inventory.SequenceEqual(body.Inventory) || !arena.Equipment.SequenceEqual(body.Equipment) || !arena.Fragments.SequenceEqual(body.Fragments) ||
            !arena.Mutations.SequenceEqual(body.Mutations) || arena.Build != body.Build || JsonData.Hash(arena.ProgressionBuild) != JsonData.Hash(body.ProgressionBuild) || arena.NextObjectId < progression.CharacterState.NextItemId)
            throw new InvalidDataException("Campaign combat projection differs from permanent ownership.");
    }
    internal void ClearCampaignEffects() => Expedition.ClearExternalGodwroughtEffects();
    internal void ReturnCampaignToHub(CombatSnapshot source)
    {
        ClearCampaignEffects(); var projected = ProjectCampaignCombat(source);
        var hub = CombatSession.CreateEncounter(combatJson, source.Seed, "hub", projected.Capture(), restoreAtAnchor: true);
        Expedition.ApplyPermanentProjection(Expedition.CaptureAdventure(), hub.Capture());
    }
}
