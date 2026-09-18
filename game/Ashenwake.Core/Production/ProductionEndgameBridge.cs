using System.Collections.ObjectModel;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    internal bool HasEndgameInventory => progression.CharacterState.Endgame is not null;
    internal bool CanReserveEndgameRun
        => progression.CharacterState.OperationReceipts.Count + Content.Data.Skills.Where(s => s.Discipline == progression.CharacterState.Discipline)
            .Sum(s => 1000 - progression.CharacterState.Mastery.GetValueOrDefault(s.Id)) + 40 < 99980;
    public IReadOnlyDictionary<string, int> EndgameCatalysts => new ReadOnlyDictionary<string, int>(new SortedDictionary<string, int>(progression.CharacterState.Endgame?.Catalysts ?? []));
    internal void InitializeEndgame()
    {
        if (HasEndgameInventory) return;
        var next = progression.Capture(); next.Character.Endgame = new(); progression.AdoptAuthoritativeState(next); ProjectPermanentInventory();
    }
    internal ProgressionResult SpendEndgameMaterials(string operation, int amount, object purpose)
    {
        var result = progression.SpendEndgameMaterials(operation, amount, purpose); if (result.Success) ProjectPermanentInventory(); return result;
    }
    internal string[] ReconcileEndgameCombat(CombatSession active, IReadOnlyList<CombatActorView> before, CombatEvent[] events, long runId, int attempt, int room, long tick)
    {
        var messages = new List<string>(Expedition.ReconcileExternalGodwrought(before, events));
        if (messages.Count == 0 && !events.Any(e => e.Kind is "LootPickedUp" or "LootDropped" or "AbilityStarted")) return [];
        var external = active.Capture(); var source = Expedition.Capture() with { Combat = external };
        var next = progression.Capture(); var state = next.Character; var known = state.Items.Select(i => i.Id).ToHashSet();
        var imported = external.Inventory.Where(i => !known.Contains(i.Id)).Select(i => ImportItem(i, source, rollAffixes: true)).ToArray();
        if (imported.Length > 0) state.Items = [.. state.Items, .. imported];
        state.NextItemId = Math.Max(state.NextItemId, external.NextObjectId);
        foreach (var mapping in source.GodwroughtItems)
            state.Items.Single(i => i.Id == mapping.Value).BurningKills = source.Adventure.Godwrought.Single(g => g.InstanceId == mapping.Key).BurningKills;
        progression.AdoptAuthoritativeState(next);
        foreach (var started in events.Where(e => e.Kind == "AbilityStarted" && e.ActorId == 1))
        {
            if (!Content.Data.Skills.Any(s => s.Id == started.ContentId) || progression.CharacterState.Mastery.GetValueOrDefault(started.ContentId) >= 1000) continue;
            var mastery = progression.GainMastery($"endgame.mastery.{runId}.{attempt}.{room}.{tick}.{started.ActionId}", started.ContentId, 1);
            Require(mastery); messages.AddRange(mastery.Events);
        }
        ProjectPermanentInventory(); return messages.ToArray();
    }
    internal string[] GrantEndgameRoom(long runId, int room, int tier)
    {
        var result = progression.EarnExperience($"endgame.room.{runId}.{room}", RoomExperience(tier), 0);
        Require(result); ProjectPermanentInventory(); return result.Events;
    }
    internal bool HasEndgameRoom(long runId, int room, int tier)
        => HasCampaignReward($"endgame.room.{runId}.{room}", RoomExperience(tier), 0);
    private static int RoomExperience(int tier) => 100 + tier * 25;
    private static string RewardKey(EndgameReward reward) => "endgame.reward." + reward.RunId;
    internal bool HasEndgameReward(EndgameReward reward)
        => progression.CharacterState.OperationReceipts.TryGetValue(RewardKey(reward), out string? hash) && hash == JsonData.Hash(new { Action = "EndgameReward", Reward = reward });
    internal void ValidateEndgameLedger(EndgameState ledger)
    {
        var state = progression.CharacterState; var receipts = state.OperationReceipts;
        var expectedRooms = ledger.CompletedRooms.Values.Select(r => $"endgame.room.{r.RunId}.{r.EncounterIndex}").ToHashSet();
        if (!expectedRooms.SetEquals(receipts.Keys.Where(k => k.StartsWith("endgame.room.", StringComparison.Ordinal))) ||
            ledger.CompletedRooms.Values.Any(r => !HasEndgameRoom(r.RunId, r.EncounterIndex, r.Tier)))
            throw new InvalidDataException("Permanent room receipts and the endgame ledger differ.");
        bool Matches(string key, object payload) => receipts.TryGetValue(key, out string? hash) && hash == JsonData.Hash(payload);
        var expectedRewards = new HashSet<string>();
        foreach (var reward in ledger.Rewards.Values)
        {
            string key = RewardKey(reward); expectedRewards.UnionWith([key, key + ".experience", key + ".mastery"]);
            if (!HasEndgameReward(reward) || !Matches(key + ".experience", new { Action = "Experience", amount = reward.Tier * 100, materials = reward.Materials }) ||
                !Content.Data.Disciplines.Where(d => state.UnlockedDisciplines.Contains(d.Id)).Any(d => Matches(key + ".mastery", new { Action = "Mastery", skillId = d.StartingSkill, amount = reward.Mastery })))
                throw new InvalidDataException("Permanent final reward payload differs from the endgame ledger.");
            if (reward.EvolutionMaterial != "")
            {
                expectedRewards.Add(key + ".catalyst");
                if (!Matches(key + ".catalyst", new { Action = "EndgameCatalyst", id = reward.EvolutionMaterial, amount = reward.EvolutionCount }))
                    throw new InvalidDataException("Permanent catalyst reward payload differs from its source.");
            }
            if (reward.Kind == "GodHunt" && reward.ContentId == "hunt.nhal_reconstruction")
            {
                expectedRewards.Add(key + ".profile");
                if (!Matches(key + ".profile", new { Action = "ProfileUnlock", unlockId = "profile.secret_hunt" }))
                    throw new InvalidDataException("Endgame profile reward payload differs from its source.");
            }
        }
        if (!expectedRewards.SetEquals(receipts.Keys.Where(k => k.StartsWith("endgame.reward.", StringComparison.Ordinal))))
            throw new InvalidDataException("Unmatched permanent endgame reward receipt.");
        foreach (string catalyst in EndgameProgression.CatalystIds)
        {
            int earned = ledger.Rewards.Values.Where(r => r.EvolutionMaterial == catalyst).Sum(r => r.EvolutionCount);
            if (state.Endgame is null || state.Endgame.Catalysts.GetValueOrDefault(catalyst) + state.Endgame.SpentCatalysts.GetValueOrDefault(catalyst) != earned)
                throw new InvalidDataException("Owned and spent catalysts differ from authoritative endgame rewards.");
        }
    }
    internal string[] GrantEndgameReward(EndgameReward reward)
    {
        string operation = RewardKey(reward);
        if (progression.CharacterState.OperationReceipts.ContainsKey(operation))
        {
            if (!HasEndgameReward(reward)) throw new InvalidDataException("Endgame reward receipt payload mismatch.");
            return [];
        }
        var messages = new List<string>();
        var experience = progression.EarnExperience(operation + ".experience", reward.Tier * 100, reward.Materials); Require(experience); messages.AddRange(experience.Events);
        string skill = Content.Data.Disciplines.Single(d => d.Id == progression.CharacterState.Discipline).StartingSkill;
        var mastery = progression.GainMastery(operation + ".mastery", skill, reward.Mastery); Require(mastery); messages.AddRange(mastery.Events);
        if (reward.EvolutionMaterial != "")
        {
            var catalyst = progression.GrantEndgameCatalyst(operation + ".catalyst", reward.EvolutionMaterial, reward.EvolutionCount);
            Require(catalyst); messages.AddRange(catalyst.Events);
        }
        if (reward.Kind == "GodHunt" && reward.ContentId == "hunt.nhal_reconstruction")
        {
            var unlock = progression.UnlockProfile(operation + ".profile", "profile.secret_hunt"); Require(unlock); messages.AddRange(unlock.Events);
        }
        var next = progression.Capture(); next.Character.OperationReceipts[operation] = JsonData.Hash(new { Action = "EndgameReward", Reward = reward });
        progression.AdoptAuthoritativeState(next); ProjectPermanentInventory(); return messages.ToArray();
    }
}
