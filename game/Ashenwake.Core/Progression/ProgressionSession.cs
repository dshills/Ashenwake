using System.Diagnostics.CodeAnalysis;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Content;

namespace Ashenwake.Core.Progression;

public sealed record PermanentItem
{
    public long Id { get; init; }
    public string DefinitionId { get; init; } = "";
    public ItemRarity Rarity { get; set; }
    public SortedDictionary<string, int> Affixes { get; set; } = [];
    public string Engraving { get; set; } = "";
    public int BurningKills { get; set; }
    public string Evolution { get; set; } = "";
    [JsonIgnore] public bool Awakened => DefinitionId == "item.ashcleaver" && GodwroughtProgress.HasAwakened(BurningKills);
    public int BaseDamage { get; init; }
    public int BaseArmor { get; init; }
    public int BaseCriticalBasisPoints { get; init; }
    public string LegacyInstanceId { get; init; } = "";
}
public sealed record LocalProfileState
{
    public string ProfileId { get; init; } = "local";
    public SortedSet<string> Unlocks { get; set; } = [];
    public SortedSet<string> Discoveries { get; set; } = [];
}
public sealed record ProgressionState
{
    public int SchemaVersion { get; init; } = 1;
    public string ContentHash { get; init; } = "";
    public string CharacterId { get; init; } = "wanderer";
    public string Discipline { get; set; } = "Vanguard";
    public long Experience { get; set; }
    public int Materials { get; set; } = 50;
    public int HubStage { get; set; }
    public long NextItemId { get; set; } = 1;
    public PermanentItem[] Items { get; set; } = [];
    public SortedDictionary<EquipmentSlot, long> Equipment { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EquipmentPreset[]? EquipmentPresets { get; set; }
    public SortedDictionary<string, int> Mastery { get; set; } = [];
    public SortedDictionary<string, string> SelectedMutations { get; set; } = [];
    public SortedDictionary<string, int> Passives { get; set; } = [];
    public SortedSet<string> UnlockedDisciplines { get; set; } = ["Vanguard"];
    public SortedSet<string> CompletedObjectives { get; set; } = [];
    public SortedSet<CraftingService> Services { get; set; } = [];
    public SortedSet<string> PropertyLibrary { get; set; } = ["rune.guard"];
    public SortedSet<string> OwnedFragments { get; set; } = ["fragment.eye_vael", "fragment.nerve_ilyra", "fragment.orrun_bone"];
    public SortedSet<string> PurifiedFragments { get; set; } = [];
    public SortedDictionary<string, string> OperationReceipts { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EndgamePermanentState? Endgame { get; set; }
}
public sealed record ProgressionSnapshot(ProgressionState Character, LocalProfileState Profile);
public sealed record ProgressionResult(bool Success, string Reason, string[] Events);
public sealed record CraftingRequest(string OperationId, CraftingService Service, long ItemId = 0,
    string AffixId = "", string ReplacementId = "", string PropertyId = "", string Lineage = "", string FragmentId = "", bool ConfirmPermanent = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CatalystId = null);
public sealed record ProgressionView(string Discipline, string Resource, int Level, long Experience, long NextLevelExperience,
    int AvailablePassivePoints, int Materials, int HubStage, string[] MasteredSkills, string[] UltimateSkills,
    IReadOnlyDictionary<EquipmentSlot, long> Equipment, IReadOnlyDictionary<string, int> Stats, string[] Journal,
    CraftingService[] Services, string[] ProfileUnlocks);

/// <summary>Permanent character progression, distinct from per-encounter resources and local profile unlocks.</summary>
public sealed partial class ProgressionSession
{
    private readonly ProgressionContent content;
    private ProgressionSnapshot snapshot;
    private IReadOnlyDictionary<string, int>? derivedStats;
    private ProgressionDefinition Data => content.Data;
    private ProgressionSession(ProgressionContent content, ProgressionSnapshot snapshot)
    {
        Validate(content, snapshot); this.content = content; this.snapshot = JsonData.Copy(snapshot);
    }
    public static ProgressionSession Create(ProgressionContent content, string discipline = "Vanguard", string characterId = "wanderer", LocalProfileState? profile = null)
        => new(content, new(new() { ContentHash = content.Hash, CharacterId = characterId, Discipline = discipline, UnlockedDisciplines = [discipline] }, profile ?? new()));
    public static ProgressionSession Restore(ProgressionContent content, ProgressionSnapshot snapshot) => new(content, snapshot);
    public ProgressionSnapshot Capture() => JsonData.Copy(snapshot);
    internal ProgressionState CharacterState => snapshot.Character;
    internal LocalProfileState ProfileState => snapshot.Profile;
    internal void AdoptAuthoritativeState(ProgressionSnapshot value) { Validate(content, value); snapshot = JsonData.Copy(value); derivedStats = null; }
    public string StateHash => JsonData.Hash(snapshot);
    public int Level => LevelOf(snapshot.Character);
    private int LevelOf(ProgressionState state)
    {
        int level = 1;
        while (level < Data.LevelCap && state.Experience >= ExperienceForLevel(level + 1)) level++;
        return level;
    }
    public long ExperienceForLevel(int level)
    {
        if (level < 1 || level > Data.LevelCap) throw new ArgumentOutOfRangeException(nameof(level));
        return (long)Data.ExperiencePerLevel * level * (level - 1) / 2;
    }
    public ProgressionView View
    {
        get
        {
            var state = snapshot.Character; int level = LevelOf(state);
            return new(state.Discipline, Data.Disciplines.Single(d => d.Id == state.Discipline).Resource, level, state.Experience,
                level == Data.LevelCap ? ExperienceForLevel(level) : ExperienceForLevel(level + 1), level - 1 - state.Passives.Values.Sum(), state.Materials,
                state.HubStage, state.Mastery.Where(m => m.Value >= 100).Select(m => m.Key).ToArray(),
                level < 10 ? [] : Data.Disciplines.Where(d => state.UnlockedDisciplines.Contains(d.Id)).Select(d => d.UltimateSkill).ToArray(),
                new SortedDictionary<EquipmentSlot, long>(state.Equipment), DerivedStats(),
                Data.Objectives.Where(o => state.CompletedObjectives.Contains(o.Id)).Select(o => Data.Strings[o.JournalKey]).ToArray(), state.Services.ToArray(), snapshot.Profile.Unlocks.ToArray());
        }
    }

    private IReadOnlyDictionary<string, int> DerivedStats()
    {
        if (derivedStats is not null) return derivedStats;
        var state = snapshot.Character;
        var items = state.Items.ToDictionary(item => item.Id);
        var stats = new SortedDictionary<string, int>();
        foreach (long itemId in state.Equipment.Values)
            foreach (var pair in items[itemId].Affixes) stats[pair.Key] = stats.GetValueOrDefault(pair.Key) + pair.Value;
        foreach (var passive in state.Passives) stats["passive." + passive.Key] = passive.Value;
        // Views may outlive this state. Never expose a mutable cached dictionary to a caller.
        return derivedStats = new ReadOnlyDictionary<string, int>(stats);
    }

    private ProgressionResult Change(string operationId, object payload, Func<ProgressionSnapshot, List<string>, string?> mutate)
    {
        if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 120) return new(false, "A bounded operation ID is required.", []);
        string fingerprint = JsonData.Hash(payload);
        if (snapshot.Character.OperationReceipts.TryGetValue(operationId, out string? previous))
            return previous == fingerprint ? new(true, "Operation was already committed.", []) : new(false, "Operation ID was previously used with a different payload.", []);
        if (snapshot.Character.OperationReceipts.Count >= 100000) return new(false, "Operation receipt capacity reached; archive under a versioned migration.", []);
        var candidate = Capture(); var events = new List<string>();
        string? error = mutate(candidate, events);
        if (error is not null) return new(false, error, []);
        candidate.Character.OperationReceipts[operationId] = fingerprint;
        try { Validate(content, candidate); } catch (InvalidDataException ex) { return new(false, ex.Message, []); }
        snapshot = candidate; derivedStats = null;
        return new(true, "", events.ToArray());
    }
    public ProgressionResult EarnExperience(string operationId, int amount, int materials = 0) => Change(operationId, new { Action = "Experience", amount, materials }, (next, events) =>
    {
        if (amount is < 0 or > 100000 || materials is < 0 or > 10000) return "Invalid experience/material reward.";
        int before = LevelOf(next.Character);
        next.Character.Experience = Math.Min(ExperienceForLevel(Data.LevelCap), next.Character.Experience + amount);
        next.Character.Materials = Math.Min(1000000, next.Character.Materials + materials); events.Add("ExperienceEarned:" + amount);
        if (LevelOf(next.Character) > before) events.Add("LevelReached:" + LevelOf(next.Character));
        return null;
    });
    public ProgressionResult GainMastery(string operationId, string skillId, int amount) => Change(operationId, new { Action = "Mastery", skillId, amount }, (next, events) =>
    {
        var skill = Data.Skills.FirstOrDefault(s => s.Id == skillId);
        if (skill is null || amount is < 1 or > 1000) return "Unknown mastery skill or invalid amount.";
        var discipline = Data.Disciplines.Single(d => d.Id == skill.Discipline);
        if (!next.Character.UnlockedDisciplines.Contains(discipline.Id) || (discipline.UltimateSkill == skillId && LevelOf(next.Character) < 10)) return "This skill is not unlocked.";
        next.Character.Mastery[skillId] = Math.Min(1000, next.Character.Mastery.GetValueOrDefault(skillId) + amount);
        events.Add("MasteryGained:" + skillId); return null;
    });
    public ProgressionResult SelectMutation(string operationId, string skillId, string mutationId) => Change(operationId, new { Action = "Mutation", skillId, mutationId }, (next, events) =>
    {
        var skill = Data.Skills.FirstOrDefault(s => s.Id == skillId && s.Discipline == next.Character.Discipline);
        if (skill is null) return "Skill is not part of the active discipline.";
        if (mutationId == "") { next.Character.SelectedMutations.Remove(skillId); events.Add("MutationRemoved:" + skillId); return null; }
        if (next.Character.Mastery.GetValueOrDefault(skillId) < 100 || !skill.Mutations.Contains(mutationId)) return "Master this skill before selecting its mutation.";
        next.Character.SelectedMutations[skillId] = mutationId; events.Add("MutationSelected:" + mutationId); return null;
    });
    public ProgressionResult AllocatePassive(string operationId, string passive) => Change(operationId, new { Action = "Passive", passive }, (next, events) =>
    {
        if (passive is not ("Offense" or "Defense" or "Resource") || LevelOf(next.Character) - 1 <= next.Character.Passives.Values.Sum()) return "No available point or unknown passive.";
        next.Character.Passives[passive] = next.Character.Passives.GetValueOrDefault(passive) + 1; events.Add("PassiveAllocated:" + passive); return null;
    });
    public ProgressionResult Respec(string operationId) => Change(operationId, new { Action = "Respec" }, (next, events) =>
    {
        if (next.Character.Materials < Data.RespecCost || next.Character.Passives.Count == 0) return "No allocated passives or insufficient materials.";
        next.Character.Materials -= Data.RespecCost; next.Character.Passives.Clear(); events.Add("PassivesReset"); return null;
    });
    public ProgressionResult Retrain(string operationId, string discipline) => Change(operationId, new { Action = "Retrain", discipline }, (next, events) =>
    {
        if (!Data.Disciplines.Any(d => d.Id == discipline) || LevelOf(next.Character) < 5 || next.Character.Materials < Data.RespecCost) return "Retraining requires level five, a known discipline, and its material cost.";
        if (next.Character.Discipline == discipline) return "Already using this discipline.";
        next.Character.Materials -= Data.RespecCost; next.Character.Discipline = discipline; next.Character.UnlockedDisciplines.Add(discipline);
        foreach (var skill in next.Character.SelectedMutations.Keys.Where(id => Data.Skills.Single(s => s.Id == id).Discipline != discipline).ToArray()) next.Character.SelectedMutations.Remove(skill);
        foreach (var pair in next.Character.Equipment.ToArray())
        {
            var definition = Data.Items.Single(i => i.Id == next.Character.Items.Single(item => item.Id == pair.Value).DefinitionId);
            if (definition.Disciplines.Length > 0 && !definition.Disciplines.Contains(discipline)) next.Character.Equipment.Remove(pair.Key);
        }
        events.Add("DisciplineChanged:" + discipline); return null;
    });
    public ProgressionResult GrantItem(string operationId, string definitionId, ItemRarity rarity, IReadOnlyDictionary<string, int>? affixes = null)
    {
        var values = new SortedDictionary<string, int>(); if (affixes is not null) foreach (var p in affixes) values[p.Key] = p.Value;
        return Change(operationId, new { Action = "GrantItem", definitionId, rarity, Affixes = values }, (next, events) =>
        {
            if (next.Character.Items.Length >= 10512 || next.Character.NextItemId == long.MaxValue) return "Inventory is full.";
            var definition = Data.Items.FirstOrDefault(i => i.Id == definitionId);
            if (definition is null) return "Unknown item definition.";
            var item = new PermanentItem
            {
                Id = next.Character.NextItemId,
                DefinitionId = definitionId,
                Rarity = rarity,
                Affixes = values,
                BaseDamage = definition.BaseDamage,
                BaseArmor = definition.BaseArmor,
                BaseCriticalBasisPoints = definition.BaseCriticalBasisPoints
            };
            try { ValidateItem(content, item); } catch (InvalidDataException ex) { return ex.Message; }
            next.Character.NextItemId++; next.Character.Items = [.. next.Character.Items, item]; events.Add("ItemGranted:" + item.Id); return null;
        });
    }
    public ProgressionResult Equip(string operationId, long itemId, EquipmentSlot slot) => Change(operationId, new { Action = "Equip", itemId, slot }, (next, events) =>
    {
        var item = next.Character.Items.FirstOrDefault(i => i.Id == itemId);
        var definition = item is null ? null : Data.Items.Single(i => i.Id == item.DefinitionId);
        if (definition is null || !definition.Slots.Contains(slot) || (definition.Disciplines.Length > 0 && !definition.Disciplines.Contains(next.Character.Discipline))) return "Item is incompatible with the slot or discipline.";
        if (definition.Hands == 2 && next.Character.Equipment.ContainsKey(EquipmentSlot.OffHand)) return "Unequip the off hand before using a two-handed weapon.";
        if (slot == EquipmentSlot.OffHand && next.Character.Equipment.TryGetValue(EquipmentSlot.MainHand, out long main) && Data.Items.Single(i => i.Id == next.Character.Items.Single(x => x.Id == main).DefinitionId).Hands == 2) return "The main-hand weapon requires both hands.";
        foreach (var equipped in next.Character.Equipment.Where(p => p.Value == itemId).ToArray()) next.Character.Equipment.Remove(equipped.Key);
        next.Character.Equipment[slot] = itemId; events.Add("ItemEquipped:" + itemId + ":" + slot); return null;
    });
    public ProgressionResult Unequip(string operationId, EquipmentSlot slot) => Change(operationId, new { Action = "Unequip", slot }, (next, events) =>
    {
        if (!next.Character.Equipment.Remove(slot)) return "Slot is empty.";
        events.Add("ItemUnequipped:" + slot); return null;
    });
    public ProgressionResult CompleteObjective(string operationId, string objectiveId) => Change(operationId, new { Action = "Objective", objectiveId }, (next, events) =>
    {
        var objective = Data.Objectives.FirstOrDefault(o => o.Id == objectiveId);
        if (objective is null || !objective.Requires.All(next.Character.CompletedObjectives.Contains)) return "Objective prerequisites are incomplete.";
        if (!next.Character.CompletedObjectives.Add(objectiveId)) return "Objective already completed.";
        next.Character.Services.Add(Enum.Parse<CraftingService>(objective.Service));
        next.Character.HubStage = Math.Min(3, next.Character.CompletedObjectives.Count / 2);
        events.Add("ObjectiveCompleted:" + objectiveId); events.Add("ServiceUnlocked:" + objective.Service); return null;
    });
    public ProgressionResult InvestHub(string operationId) => Change(operationId, new { Action = "InvestHub" }, (next, events) =>
    {
        var objective = Data.Objectives.Single(o => o.Id == "objective.haven");
        if (!objective.Requires.All(next.Character.CompletedObjectives.Contains) || next.Character.CompletedObjectives.Contains(objective.Id) || next.Character.Materials < 15) return "Restore the specialist workshops and contribute 15 materials first.";
        next.Character.Materials -= 15; next.Character.CompletedObjectives.Add(objective.Id); next.Character.Services.Add(CraftingService.Engraving);
        next.Character.HubStage = Math.Min(3, next.Character.CompletedObjectives.Count / 2); events.Add("HubInvested"); return null;
    });
    public ProgressionResult UnlockProfile(string operationId, string unlockId) => Change(operationId, new { Action = "ProfileUnlock", unlockId }, (next, events) =>
    {
        if (!Data.ProfileUnlocks.Contains(unlockId)) return "Unknown profile unlock.";
        if (next.Profile.Unlocks.Add(unlockId)) events.Add("ProfileUnlocked:" + unlockId); return null;
    });
    public ProgressionResult GrantFragment(string operationId, string fragmentId) => Change(operationId, new { Action = "GrantFragment", fragmentId }, (next, events) =>
    {
        if (!Data.FragmentIds.Contains(fragmentId)) return "Unknown permanent fragment.";
        if (next.Character.OwnedFragments.Add(fragmentId)) events.Add("FragmentGranted:" + fragmentId); return null;
    });
    public ProgressionResult RecordGodwroughtKill(string operationId, long itemId) => Change(operationId, new { Action = "BurningKill", itemId }, (next, events) =>
    {
        var item = next.Character.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null || item.DefinitionId != "item.ashcleaver" || item.Rarity != ItemRarity.Godwrought || !next.Character.Equipment.Values.Contains(itemId)) return "Ashcleaver must be equipped to earn awakening progress.";
        item.BurningKills = Math.Min(1000, item.BurningKills + 1); events.Add("GodwroughtKill:" + itemId); return null;
    });
    public ProgressionResult Craft(CraftingRequest request) => Change(request.OperationId, new { Action = "Craft", Request = request }, (next, events) =>
    {
        var state = next.Character;
        if (!Enum.IsDefined(request.Service) || !state.Services.Contains(request.Service)) return "Crafting service is not unlocked.";
        int cost = Data.CraftingCosts[request.Service];
        var item = state.Items.FirstOrDefault(i => i.Id == request.ItemId);
        var definition = item is null ? null : Data.Items.Single(d => d.Id == item.DefinitionId);
        if (request.Service != CraftingService.Purification && item is null) return "Item does not exist.";
        string? catalystError = SpendEndgameCraftCatalyst(state, item, request, ref cost);
        if (catalystError is not null) return catalystError;
        if (state.Materials < cost) return "Insufficient crafting materials.";
        switch (request.Service)
        {
            case CraftingService.Tempering:
                if (item!.Rarity == ItemRarity.Godwrought && request.AffixId == "affix.damage")
                {
                    int temper = item.Affixes.GetValueOrDefault("affix.damage");
                    if (temper >= 10) return "Godwrought tempering is already at its five-step limit.";
                    item.Affixes["affix.damage"] = Math.Min(10, temper + 2); break;
                }
                if (!item.Affixes.TryGetValue(request.AffixId, out int value)) return "Choose an existing conventional affix.";
                var affix = Data.Affixes.Single(a => a.Id == request.AffixId);
                if (affix.Advanced || value >= affix.Maximum) return "Affix cannot be tempered further.";
                item.Affixes[affix.Id] = Math.Min(affix.Maximum, value + Math.Max(1, (affix.Maximum - affix.Minimum) / 10)); break;
            case CraftingService.Rebinding:
                if (!item!.Affixes.ContainsKey(request.AffixId) || item.Affixes.ContainsKey(request.ReplacementId)) return "Select an existing affix and a different replacement.";
                var replacement = Data.Affixes.FirstOrDefault(a => a.Id == request.ReplacementId);
                if (replacement is null) return "Unknown replacement affix.";
                item.Affixes.Remove(request.AffixId); item.Affixes[replacement.Id] = replacement.Minimum; break;
            case CraftingService.Engraving:
                if (item!.Engraving != "" || !state.PropertyLibrary.Contains(request.PropertyId) || request.PropertyId.StartsWith("evolution.", StringComparison.Ordinal)) return "An empty engraving slot and learned property are required.";
                item.Engraving = request.PropertyId; break;
            case CraftingService.Extraction:
                if (!request.ConfirmPermanent || item!.Rarity != ItemRarity.Legendary || definition!.Property == "") return "Confirm destruction of a Legendary with an extractable property.";
                state.PropertyLibrary.Add(definition.Property); state.Items = state.Items.Where(i => i.Id != item.Id).ToArray();
                foreach (var slot in state.Equipment.Where(p => p.Value == item.Id).Select(p => p.Key).ToArray()) state.Equipment.Remove(slot);
                break;
            case CraftingService.DivineGrafting:
                if (!request.ConfirmPermanent || item!.Rarity != ItemRarity.Godwrought || item.DefinitionId != "item.ashcleaver" || !item.Awakened || item.Evolution != "" || request.Lineage is not ("Serath" or "Orrun")) return "Confirm one permanent evolution of an awakened Godwrought item.";
                if (!state.OwnedFragments.Contains(request.Lineage == "Serath" ? "fragment.heart_serath" : "fragment.orrun_bone")) return "Learn the selected lineage's fragment before grafting.";
                item.Evolution = request.Lineage; break;
            case CraftingService.Purification:
                if (!state.OwnedFragments.Contains(request.FragmentId) || !state.PurifiedFragments.Add(request.FragmentId)) return "Select an unpurified owned fragment.";
                break;
        }
        if (item is not null && request.Service != CraftingService.Extraction)
            try { ValidateItem(content, item); } catch (InvalidDataException ex) { return ex.Message; }
        state.Materials -= cost; events.Add("Crafted:" + request.Service); return null;
    });

    public static void ValidateItem(ProgressionContent content, PermanentItem item)
    {
        static void Check([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Check(item is not null && item.Id > 0 && Enum.IsDefined(item.Rarity) && item.Affixes is not null, "Invalid permanent item identity/rarity/affixes.");
        Check(item.BaseDamage is >= 0 and <= 400 && item.BaseArmor is >= 0 and <= 7500 && item.BaseCriticalBasisPoints is >= 0 and <= 7500 && item.LegacyInstanceId is not null && item.LegacyInstanceId.Length <= 100, "Invalid retained item base roll/legacy identity.");
        var d = content.Data; var definition = d.Items.FirstOrDefault(i => i.Id == item.DefinitionId);
        Check(definition is not null, "Unknown permanent item definition.");
        Check(item.Affixes.Count <= ProgressionContent.AffixLimit(item.Rarity), "Too many affixes for this rarity.");
        foreach (var pair in item.Affixes)
        {
            var affix = d.Affixes.FirstOrDefault(a => a.Id == pair.Key);
            Check(affix is not null && definition.Slots.All(affix.Slots.Contains) && pair.Value >= affix.Minimum && pair.Value <= affix.Maximum && !affix.Excludes.Any(item.Affixes.ContainsKey), "Unknown, incompatible, or out-of-range affix.");
            Check(!affix.Advanced || item.Rarity >= ItemRarity.Relic, "Advanced affixes require Relic rarity or greater.");
        }
        Check(item.Engraving == "" || d.Properties.Any(p => p.Id == item.Engraving && definition.Slots.All(p.Slots.Contains) && !p.Id.StartsWith("evolution.", StringComparison.Ordinal)), "Ineligible engraving.");
        Check(item.BurningKills is >= 0 and <= 1000 && item.Evolution is "" or "Serath" or "Orrun" && (item.Evolution == "" || item.BurningKills == 1000), "Invalid Godwrought evolution.");
        Check((item.BurningKills == 0 && item.Evolution == "") || (item.Rarity == ItemRarity.Godwrought && item.DefinitionId == "item.ashcleaver"), "Ordinary items cannot hold Godwrought progression.");
        Check(item.LegacyInstanceId == "" || item.Rarity == ItemRarity.Godwrought, "Only Godwrought items retain a legacy instance identity.");
        Check(item.Rarity != ItemRarity.Godwrought || item.DefinitionId == "item.ashcleaver", "Only declared Godwrought items use that rarity.");
        Check(item.DefinitionId != "item.ashcleaver" || item.Rarity == ItemRarity.Godwrought, "Ashcleaver must be Godwrought.");
        Check(definition.Property == "" || item.Rarity is ItemRarity.Legendary or ItemRarity.Godwrought, "Behavior-changing item definitions require Legendary/Godwrought rarity.");
    }
    public static void Validate(ProgressionContent content, ProgressionSnapshot value)
    {
        static void Check([DoesNotReturnIf(false)] bool condition, string error) { if (!condition) throw new InvalidDataException(error); }
        Check(value?.Character is not null && value.Profile is not null, "Character and profile state are required.");
        var s = value.Character; var d = content.Data;
        Check(s.SchemaVersion == 1 && s.ContentHash == content.Hash, "Progression schema/content mismatch; preserve the save for its matching build.");
        Check(!string.IsNullOrWhiteSpace(s.CharacterId) && s.CharacterId.Length <= 80 && d.Disciplines.Any(x => x.Id == s.Discipline), "Invalid character identity/discipline.");
        Check(s.Experience >= 0 && s.Experience <= (long)d.ExperiencePerLevel * d.LevelCap * (d.LevelCap - 1) / 2 && s.Materials is >= 0 and <= 1000000 && s.HubStage is >= 0 and <= 3 && s.NextItemId > 0, "Progression values exceed bounds.");
        Check(s.Items is not null && s.Items.Length <= 10512 && s.Items.All(i => i is not null) && s.Equipment is not null && s.Mastery is not null && s.SelectedMutations is not null && s.Passives is not null && s.UnlockedDisciplines is not null && s.CompletedObjectives is not null && s.Services is not null && s.PropertyLibrary is not null && s.OwnedFragments is not null && s.PurifiedFragments is not null && s.OperationReceipts is not null, "Null/oversized progression collection.");
        foreach (var item in s.Items) ValidateItem(content, item);
        Check(s.Items.Select(i => i.Id).Distinct().Count() == s.Items.Length && s.Items.All(i => i.Id < s.NextItemId), "Duplicate or invalid item sequence.");
        ValidateEquipmentPresets(content, s);
        Check(s.UnlockedDisciplines.Contains(s.Discipline) && s.UnlockedDisciplines.All(id => d.Disciplines.Any(x => x.Id == id)), "Unknown unlocked discipline.");
        Check(s.Equipment.Values.Distinct().Count() == s.Equipment.Count && s.Equipment.All(pair => Enum.IsDefined(pair.Key) && s.Items.Any(i => i.Id == pair.Value)), "Invalid equipment references.");
        foreach (var pair in s.Equipment)
        {
            var definition = d.Items.Single(i => i.Id == s.Items.Single(x => x.Id == pair.Value).DefinitionId);
            Check(definition.Slots.Contains(pair.Key) && (definition.Disciplines.Length == 0 || definition.Disciplines.Contains(s.Discipline)), "Saved equipment violates slot/discipline restrictions.");
            Check(definition.Hands != 2 || !s.Equipment.ContainsKey(EquipmentSlot.OffHand), "Two-handed/off-hand conflict.");
        }
        var equippedAffixes = s.Equipment.Values.SelectMany(id => s.Items.Single(i => i.Id == id).Affixes.Keys).ToHashSet();
        Check(!d.Affixes.Any(a => equippedAffixes.Contains(a.Id) && a.Excludes.Any(equippedAffixes.Contains)), "Equipped affixes exclude one another across this loadout.");
        Check(s.Items.Count(i => i.DefinitionId == "item.ashcleaver") <= 10000, "Godwrought stash capacity exceeded.");
        Check(s.Items.Where(i => i.LegacyInstanceId != "").Select(i => i.LegacyInstanceId).Distinct().Count() == s.Items.Count(i => i.LegacyInstanceId != ""), "Duplicate legacy instance identity.");
        int level = 1; while (level < d.LevelCap && s.Experience >= (long)d.ExperiencePerLevel * (level + 1) * level / 2) level++;
        Check(s.Passives.All(p => p.Key is "Offense" or "Defense" or "Resource" && p.Value is >= 1 and <= 99) && s.Passives.Values.Sum() <= level - 1, "Invalid passive allocation.");
        Check(s.Mastery.All(p => p.Value is >= 1 and <= 1000 && d.Skills.Any(skill => skill.Id == p.Key && s.UnlockedDisciplines.Contains(skill.Discipline)) && !d.Disciplines.Any(x => x.UltimateSkill == p.Key && level < 10)), "Invalid skill mastery.");
        Check(s.SelectedMutations.All(p => s.Mastery.GetValueOrDefault(p.Key) >= 100 && d.Skills.Any(skill => skill.Id == p.Key && skill.Discipline == s.Discipline && skill.Mutations.Contains(p.Value))), "Invalid selected mutation or missing mastery.");
        Check(s.CompletedObjectives.All(id => d.Objectives.Any(o => o.Id == id && o.Requires.All(s.CompletedObjectives.Contains))), "Unknown objective or missing prerequisite.");
        var expectedServices = d.Objectives.Where(o => s.CompletedObjectives.Contains(o.Id)).Select(o => Enum.Parse<CraftingService>(o.Service)).ToHashSet();
        Check(s.Services.SetEquals(expectedServices) && s.HubStage == Math.Min(3, s.CompletedObjectives.Count / 2), "Service/hub state does not match completed objectives.");
        Check(s.PropertyLibrary.All(id => d.Properties.Any(p => p.Id == id)) && s.OwnedFragments.All(d.FragmentIds.Contains) && s.PurifiedFragments.All(s.OwnedFragments.Contains), "Unknown extracted property or unowned purified fragment.");
        Check(s.OperationReceipts.Count <= 100000 && s.OperationReceipts.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Key.Length <= 120 && p.Value is { Length: 64 } && p.Value.All(Uri.IsHexDigit)), "Invalid operation receipt.");
        EndgameProgression.Validate(s.Endgame);
        ValidateProfile(content, value.Profile);
    }
    public static void ValidateProfile(ProgressionContent content, LocalProfileState profile)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.ProfileId) || profile.ProfileId.Length > 80 || profile.Unlocks is null || profile.Discoveries is null ||
            !profile.Unlocks.All(content.Data.ProfileUnlocks.Contains) || !profile.Discoveries.All(content.Data.DiscoveryIds.Contains))
            throw new InvalidDataException("Unknown/corrupt local profile state.");
    }
}
