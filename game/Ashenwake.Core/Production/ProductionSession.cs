using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;

namespace Ashenwake.Core.Production;

public enum ProductionAction { Expedition, Equip, Unequip, Craft, Passive, Respec, Retrain, Mutation, Discard, SaveEquipmentPreset, RenameEquipmentPreset, DeleteEquipmentPreset, ApplyEquipmentPreset, SetItemFavorite, SetItemLocked, Salvage, SaveBuildLoadout, RenameBuildLoadout, DeleteBuildLoadout, ApplyBuildLoadout, StoreItem, RetrieveItem, MoveStashedItem, RenameStashTab }
public sealed record ProductionCommand(ProductionAction Action, ExpeditionCommand? Expedition = null, long ItemId = 0,
    EquipmentSlot Slot = EquipmentSlot.MainHand, string Id = "", string Value = "", CraftingRequest? Crafting = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool ConfirmPermanent = false);
public sealed record ProductionResult(bool Success, string Reason, CombatEvent[] CombatEvents, string[] WorldEvents);
public sealed record ProductionSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public string RulesVersion { get; init; } = "production.1";
    public long OperationSequence { get; init; }
    public ExpeditionSnapshot Expedition { get; init; } = null!;
    public ProgressionSnapshot Progression { get; init; } = null!;
}
public sealed record ProductionFrame(ProductionCommand Command, string StateHash, string EventHash);
public sealed record ProductionReplay(int SchemaVersion, ProductionSnapshot Initial, ProductionFrame[] Frames);

/// <summary>Permanent progression is authoritative; Expedition holds validated combat/world projections of those owned items and materials.</summary>
public sealed partial class ProductionSession
{
    private readonly string combatJson;
    private readonly CombatContent combatContent;
    public AdventureContent AdventureContent { get; }
    public ProgressionContent Content { get; }
    public ExpeditionSession Expedition { get; private set; }
    private ProgressionSession progression;
    private long operationSequence;
    private readonly List<ProductionFrame> frames = [];
    private ProductionSnapshot initial = null!;
    public CombatSession Combat => Expedition.Combat;
    public AdventureView View => Expedition.View;
    public ProgressionView ProgressionView => progression.View;
    public long CurrentLevelExperience => progression.ExperienceForLevel(progression.Level);
    public IReadOnlyList<string> WorldEvents { get; private set; } = [];
    public string StateHash => JsonData.Hash(Capture());
    public IReadOnlyList<ExpeditionInteraction> Interactions => View.RoomId == "room.greyhaven"
        ? [new("npc.mara", "Mara · anatomy and research", new(-4500, -1800), 2400),
           new("service.mara", "Mara · Divine Anatomy", new(-4500, -1800), 2400),
           new("npc.torren", "Torren · tempering", new(2000, -2000), 2600),
           new("service.torren", "Torren · equipment", new(2000, -2000), 2600),
           new(PersonalStashCatalog.InteractionId, "Personal stash · stored equipment", PersonalStashCatalog.Position, PersonalStashCatalog.Range),
           new("npc.cael", "Sister Cael · purification", new(-4500, 1800), 2600),
           new("npc.oris", "Oris · rebinding", new(0, 4200), 2600),
           new("npc.kesh", "Kesh · extraction", new(4500, 2600), 2600),
           new("hub.workshops", "Restore the engraving workshop · 15 materials", new(1500, 3500), 2600),
           new("dungeon.replay", "Begin another expedition", new(6500, 0), 2600)] : Expedition.Interactions;

    private ProductionSession(string combatJson, AdventureContent adventure, ProgressionContent content, ExpeditionSession expedition, ProgressionSession progression)
    { this.combatJson = combatJson; combatContent = CombatContent.Parse(combatJson); AdventureContent = adventure; Content = content; Expedition = expedition; this.progression = progression; ProjectStoredGodwrought(); }

    public static ProductionSession Create(string combatJson, AdventureContent adventure, ProgressionContent content, ulong seed = 42,
        string discipline = "Vanguard", LocalProfileState? profile = null)
    {
        var resolved = ProductionContent.Resolve(combatJson, content, adventure); var world = ProductionContent.ResolveAdventure(combatJson, adventure);
        var expedition = ExpeditionSession.Create(combatJson, world, seed);
        var permanent = ProgressionSession.Create(resolved, discipline, "wanderer", profile);
        var session = new ProductionSession(combatJson, world, resolved, expedition, permanent);
        session.ImportInitial(expedition.Capture()); session.ProjectPermanentInventory(); session.initial = session.Capture(); return session;
    }
    public static ProductionSession Restore(string combatJson, AdventureContent adventure, ProgressionContent content, ProductionSnapshot snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.RulesVersion != "production.1" || snapshot.OperationSequence is < 0 or > 1000000000 || snapshot.Expedition is null || snapshot.Progression is null)
            throw new InvalidDataException("Unsupported or malformed production state.");
        var resolved = ProductionContent.Resolve(combatJson, content, adventure); var world = ProductionContent.ResolveAdventure(combatJson, adventure);
        var permanent = ProgressionSession.Restore(resolved, snapshot.Progression);
        var expedition = ExpeditionSession.Restore(combatJson, world, snapshot.Expedition);
        var session = new ProductionSession(combatJson, world, resolved, expedition, permanent) { operationSequence = snapshot.OperationSequence };
        session.ValidateProjection(); session.ValidateBuildLoadoutAnatomy(); session.initial = session.Capture(); return session;
    }
    internal static ProductionSession ImportPhaseTwo(string combatJson, AdventureContent adventure, ProgressionContent content, ExpeditionSnapshot legacy)
    {
        var resolved = ProductionContent.Resolve(combatJson, content, adventure); var world = ProductionContent.ResolveAdventure(combatJson, adventure);
        var rebasedCombat = legacy.Combat with { ContentHash = CombatSession.Create(combatJson).ContentHash };
        var rebased = legacy with { AdventureHash = world.Hash, Combat = rebasedCombat };
        var expedition = ExpeditionSession.Restore(combatJson, world, rebased);
        var permanent = ProgressionSession.Create(resolved, characterId: legacy.Adventure.CharacterId);
        var session = new ProductionSession(combatJson, world, resolved, expedition, permanent);
        session.ImportInitial(legacy, preserveMutations: true); session.ProjectPermanentInventory(); session.initial = session.Capture(); return session;
    }
    public ProductionSnapshot Capture() => new() { OperationSequence = operationSequence, Expedition = Expedition.Capture(), Progression = progression.Capture() };
    public ProductionReplay CaptureReplay() => JsonData.Copy(new ProductionReplay(1, initial, frames.ToArray()));
    public IReadOnlyList<CombatEvent> Step(params CombatCommand[] commands) => Execute(new(ProductionAction.Expedition, new(ExpeditionAction.Tick, Commands: commands))).CombatEvents;
    public ProductionResult Travel(string room) => Execute(new(ProductionAction.Expedition, new(ExpeditionAction.Travel, room)));
    public ProductionResult Interact(string id) => Execute(new(ProductionAction.Expedition, new(ExpeditionAction.Interact, id)));
    public ProductionResult InstallFragment(string slot, string? id) => Execute(new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, slot, id ?? "")));
    public ProductionResult SelectManifestation(string id) => Execute(new(ProductionAction.Expedition, new(ExpeditionAction.Manifestation, id)));
    public ProductionResult Equip(long itemId, EquipmentSlot slot) => Execute(new(ProductionAction.Equip, ItemId: itemId, Slot: slot));
    public ProductionResult Unequip(EquipmentSlot slot) => Execute(new(ProductionAction.Unequip, Slot: slot));
    public ProductionResult Discard(long itemId, bool confirmPermanent = false) => Execute(new(ProductionAction.Discard, ItemId: itemId, ConfirmPermanent: confirmPermanent));
    public ProductionResult Craft(CraftingRequest request) => Execute(new(ProductionAction.Craft, Crafting: request));
    public ProductionResult AllocatePassive(string id) => Execute(new(ProductionAction.Passive, Id: id));
    public ProductionResult Respec() => Execute(new(ProductionAction.Respec));
    public ProductionResult Retrain(string discipline) => Execute(new(ProductionAction.Retrain, Id: discipline));
    public ProductionResult SetMutation(string mutationId)
    {
        var mutation = combatContent.Mutations.FirstOrDefault(m => m.Id == mutationId);
        return SetMutation(mutation?.SkillId ?? "", mutationId);
    }
    public ProductionResult SetMutation(string skillId, string mutationId) => Execute(new(ProductionAction.Mutation, Id: skillId, Value: mutationId));
    internal void MergeProfile(LocalProfileState profile)
    {
        var next = progression.Capture();
        if (next.Profile.ProfileId != profile.ProfileId) throw new InvalidDataException("Local profile identities conflict.");
        next.Profile.Unlocks.UnionWith(profile.Unlocks); next.Profile.Discoveries.UnionWith(profile.Discoveries); progression.AdoptAuthoritativeState(next);
    }

    public ProductionResult Execute(ProductionCommand command, bool recordReplay = true)
    {
        if (command is null || !Enum.IsDefined(command.Action)) throw new InvalidDataException("Invalid production command.");
        if (operationSequence >= 1000000000 || progression.CharacterState.OperationReceipts.Count >= 99990)
            return new(false, "Permanent operation archive is full; preserve this save for an archive migration.", [], []);
        if (recordReplay && frames.Count >= 1800) { initial = Capture(); frames.Clear(); }
        bool tick = command.Action == ProductionAction.Expedition && command.Expedition?.Action == ExpeditionAction.Tick;
        // Non-tick transactions may span multiple services; a snapshot permits complete rollback. Ordinary ticks do not serialize/restore a duplicate simulation.
        var rollback = tick ? null : Capture(); ProductionResult result;
        try
        {
            result = command.Action == ProductionAction.Expedition ? ExecuteExpedition(command.Expedition ?? throw new InvalidDataException("Missing expedition action.")) : ExecutePermanent(command);
            if (result.Success) operationSequence++;
            else if (rollback is not null) RestoreFields(rollback);
        }
        catch
        {
            if (rollback is not null) RestoreFields(rollback);
            throw;
        }
        WorldEvents = result.WorldEvents;
        if (recordReplay) frames.Add(new(JsonData.Copy(command), StateHash, JsonData.Hash(result)));
        return result;
    }
    private void RestoreFields(ProductionSnapshot value)
    {
        Expedition = ExpeditionSession.Restore(combatJson, AdventureContent, value.Expedition);
        progression = ProgressionSession.Restore(Content, value.Progression); operationSequence = value.OperationSequence; ProjectStoredGodwrought();
    }
    private ProductionResult ExecuteExpedition(ExpeditionCommand command)
    {
        if (command.Action is ExpeditionAction.Temper or ExpeditionAction.Graft or ExpeditionAction.EquipGodwrought)
            return new(false, "Use the permanent equipment and crafting services.", [], []);
        if (command.Action == ExpeditionAction.Interact && command.Id is "npc.torren" or "npc.cael" or "npc.oris" or "npc.kesh" or "hub.workshops") return Specialist(command.Id);
        if (command.Action == ExpeditionAction.Tick)
        {
            var commands = command.Commands ?? [];
            if (commands.Any(c => c is null) || commands.Length > 64) throw new InvalidDataException("Invalid production combat command batch.");
            if (commands.Any(c => c.Kind is CombatCommandKind.Equip or CombatCommandKind.EquipFragment or CombatCommandKind.UnequipFragment or CombatCommandKind.SetMutation))
                return new(false, "Loadout changes require validated production commands.", [], []);
        }
        var before = Expedition.CaptureAdventure(); var result = Expedition.Execute(command, recordReplay: false);
        if (!result.Success) return new(false, result.Reason, result.CombatEvents, result.WorldEvents);
        var events = new List<string>(result.WorldEvents); bool projectionChanged = ReconcileAuthoritativeOutcomes(before, result, events);
        if (command.Action == ExpeditionAction.Interact && command.Id == "npc.mara" && !progression.CharacterState.CompletedObjectives.Contains("objective.mara"))
        {
            var accepted = progression.CompleteObjective("quest.mara", "objective.mara"); Require(accepted); events.AddRange(accepted.Events); projectionChanged = true;
        }
        if (projectionChanged) ProjectPermanentInventory();
        return new(true, "", result.CombatEvents, events.ToArray());
    }
    private ProductionResult ExecutePermanent(ProductionCommand command)
    {
        if (IsItemOrganizationAction(command.Action)) return ExecuteItemOrganization(command);
        if (IsBuildLoadoutAction(command.Action)) return ExecuteBuildLoadout(command);
        if (IsStashAction(command.Action)) return ExecuteStash(command);
        if (View.RoomId != "room.greyhaven") return new(false, "Change permanent builds at Greyhaven's workshops.", [], []);
        SynchronizeItemSequence();
        string operation = "player." + operationSequence; ProgressionResult result;
        switch (command.Action)
        {
            case ProductionAction.SaveEquipmentPreset:
            case ProductionAction.RenameEquipmentPreset:
            case ProductionAction.DeleteEquipmentPreset:
            case ProductionAction.ApplyEquipmentPreset:
                string presetBlocked = EquipmentPresetServiceBlockedReason();
                if (presetBlocked.Length > 0) return new(false, presetBlocked, [], []);
                result = command.Action switch
                {
                    ProductionAction.SaveEquipmentPreset => progression.SaveEquipmentPreset(operation, command.Id, command.Value),
                    ProductionAction.RenameEquipmentPreset => progression.RenameEquipmentPreset(operation, command.Id, command.Value),
                    ProductionAction.DeleteEquipmentPreset => progression.DeleteEquipmentPreset(operation, command.Id),
                    _ => progression.ApplyEquipmentPreset(operation, command.Id)
                };
                break;
            case ProductionAction.Equip:
                if (!Near("service.torren")) return new(false, "Visit Torren to equip items.", [], []);
                result = progression.Equip(operation, command.ItemId, command.Slot); break;
            case ProductionAction.Unequip:
                if (!Near("service.torren")) return new(false, "Visit Torren to unequip items.", [], []);
                result = progression.Unequip(operation, command.Slot); break;
            case ProductionAction.Salvage:
                string salvageBlocked = SalvageServiceBlockedReason();
                if (salvageBlocked.Length > 0) return new(false, salvageBlocked, [], []);
                result = progression.Salvage(operation, command.ItemId, command.ConfirmPermanent); break;
            case ProductionAction.Discard:
                if (!Near("service.torren")) return new(false, "Visit Torren to discard items.", [], []);
                if (!Combat.View.Actors.Any(actor => actor.Id == 1 && actor.Health > 0)) return new(false, "Cannot discard equipment while defeated.", [], []);
                result = progression.Discard(operation, command.ItemId, command.ConfirmPermanent); break;
            case ProductionAction.Craft:
                if (command.Crafting is null) return new(false, "Choose a crafting recipe.", [], []);
                string service = command.Crafting.Service switch { CraftingService.Tempering => "service.torren", CraftingService.Rebinding => "npc.oris", CraftingService.Engraving => "hub.workshops", CraftingService.Extraction => "npc.kesh", CraftingService.Purification => "npc.cael", _ => "service.mara" };
                if (!Near(service)) return new(false, "Visit the specialist for this crafting service.", [], []);
                result = progression.Craft(command.Crafting with { OperationId = command.Crafting.OperationId == "" ? operation : command.Crafting.OperationId }); break;
            case ProductionAction.Passive:
                if (!Near("service.mara")) return new(false, "Visit Mara to invest a passive point.", [], []);
                result = progression.AllocatePassive(operation, command.Id); break;
            case ProductionAction.Respec:
                if (!Near("service.mara")) return new(false, "Visit Mara to respec.", [], []);
                result = progression.Respec(operation); break;
            case ProductionAction.Retrain:
                if (!Near("service.mara")) return new(false, "Visit Mara to retrain.", [], []);
                result = progression.Retrain(operation, command.Id); break;
            case ProductionAction.Mutation:
                if (!Near("service.mara")) return new(false, "Visit Mara to select a mutation.", [], []);
                result = progression.SelectMutation(operation, command.Id, command.Value); break;
            default: return new(false, "Unknown permanent action.", [], []);
        }
        if (result.Success) ProjectPermanentInventory();
        return new(result.Success, result.Reason, [], result.Events);
    }
    private ProductionResult Specialist(string id)
    {
        if (!Near(id)) return new(false, "Move within reach of the specialist.", [], []);
        SynchronizeItemSequence();
        var world = Expedition.CaptureAdventure();
        string objective = id switch { "npc.torren" => "objective.torren", "npc.cael" => "objective.cael", "npc.oris" => "objective.oris", "npc.kesh" => "objective.kesh", _ => "objective.haven" };
        if (progression.CharacterState.CompletedObjectives.Contains(objective)) return new(true, "", [], ["ServiceOpened:" + id]);
        bool rescued = id switch
        {
            "npc.torren" => world.CompletedEncounters.Contains("encounter.ossuary") || world.BellVictories > 0,
            "npc.cael" => world.CompletedEncounters.Contains("encounter.cloister") || world.BellVictories > 0,
            "npc.oris" => world.BellVictories > 0,
            "npc.kesh" => world.ReturnedToMara,
            _ => true
        };
        if (!rescued) return new(false, "Complete the specialist's rescue or research objective in the dungeon first.", [], []);
        var result = id == "hub.workshops" ? progression.InvestHub("quest.haven.invest") : progression.CompleteObjective("quest." + objective, objective);
        if (!result.Success) return new(false, result.Reason, [], result.Events);
        var events = new List<string>(result.Events);
        if (id != "hub.workshops")
        {
            var reward = progression.EarnExperience("reward." + objective, 50, 10); Require(reward); events.AddRange(reward.Events);
            if (id == "npc.kesh" && Content.Data.Items.Any(i => i.Id == "item.echo_ring"))
            {
                var grant = progression.GrantItem("reward.kesh.legendary", "item.echo_ring", ItemRarity.Legendary); Require(grant); events.AddRange(grant.Events);
            }
        }
        ProjectPermanentInventory(); return new(true, "", [], events.ToArray());
    }
    private bool Near(string id)
    {
        var interaction = Interactions.FirstOrDefault(i => i.ActionId == id);
        // Preserve the established permanent-service command reach for save/replay compatibility.
        // Mara's visible dialogue/service interaction has the narrower expedition boundary.
        const int permanentServiceRange = 2600;
        return interaction is not null && Position.DistanceSquared(Combat.View.Actors.Single(a => a.Id == 1).Position, interaction.Position) <= (long)permanentServiceRange * permanentServiceRange;
    }
    private static void Require(ProgressionResult result) { if (!result.Success) throw new InvalidDataException("Authoritative progression transaction rejected: " + result.Reason); }

    private void ImportInitial(ExpeditionSnapshot source, bool preserveMutations = false)
    {
        var next = progression.Capture(); var state = next.Character;
        state.Materials = source.Adventure.Materials; state.OwnedFragments = new(source.Adventure.OwnedFragments);
        state.Items = source.Combat.Inventory.Select(item => ImportItem(item, source)).ToArray();
        state.NextItemId = Math.Max(source.Combat.NextObjectId, state.Items.Select(i => i.Id + 1).DefaultIfEmpty(1).Max());
        ImportUnmappedGodwrought(state, source);
        state.Equipment = new(source.Combat.Equipment.ToDictionary(p => Enum.Parse<EquipmentSlot>(p.Key), p => p.Value));
        if (preserveMutations)
            foreach (var pair in source.Combat.Mutations) { state.Mastery[pair.Key] = 100; state.SelectedMutations[pair.Key] = pair.Value; }
        next.Profile.Discoveries.UnionWith(source.Adventure.Discoveries);
        progression.AdoptAuthoritativeState(next);
    }
    private PermanentItem ImportItem(CombatItem item, ExpeditionSnapshot source, bool rollAffixes = false)
    {
        string? legacy = source.GodwroughtItems.FirstOrDefault(p => p.Value == item.Id).Key;
        var godwrought = legacy is null ? null : source.Adventure.Godwrought.Single(g => g.InstanceId == legacy);
        var definition = Content.Data.Items.Single(i => i.Id == item.DefinitionId);
        var rarity = godwrought is not null ? ItemRarity.Godwrought : Enum.Parse<ItemRarity>(item.Rarity);
        if (definition.Property != "" && rarity < ItemRarity.Legendary) rarity = ItemRarity.Legendary;
        var affixes = rollAffixes && godwrought is null ? ProgressionLoot.RollAffixes(Content, item.DefinitionId, rarity, source.Adventure.Seed, item.Id) : new SortedDictionary<string, int>();
        if (godwrought?.TemperLevel > 0) affixes["affix.damage"] = godwrought.TemperLevel * 2;
        else if (!rollAffixes && rarity is ItemRarity.Tempered or ItemRarity.Rare or ItemRarity.Relic)
            affixes[definition.Slots.All(s => s is EquipmentSlot.MainHand or EquipmentSlot.OffHand) ? "affix.damage" : "affix.resource"] = 1;
        return new()
        {
            Id = item.Id,
            DefinitionId = item.DefinitionId,
            Rarity = rarity,
            BaseDamage = item.Damage,
            BaseArmor = item.Armor,
            BaseCriticalBasisPoints = item.CriticalBasisPoints,
            Affixes = affixes,
            LegacyInstanceId = legacy ?? "",
            BurningKills = godwrought?.BurningKills ?? 0,
            Evolution = godwrought?.Evolution ?? ""
        };
    }
    private bool ReconcileAuthoritativeOutcomes(AdventureState before, ExpeditionResult result, List<string> events)
    {
        if (result.WorldEvents.Length == 0 && !result.CombatEvents.Any(e => e.Kind is "LootPickedUp" or "AbilityStarted")) return false;
        var after = Expedition.Capture(); bool changed = false;
        var next = progression.Capture(); var state = next.Character;
        var known = state.Items.Select(i => i.Id).ToHashSet();
        foreach (var item in after.Combat.Inventory.Where(i => !known.Contains(i.Id))) { state.Items = [.. state.Items, ImportItem(item, after, rollAffixes: true)]; changed = true; }
        state.NextItemId = Math.Max(state.NextItemId, after.Combat.NextObjectId);
        if (ImportUnmappedGodwrought(state, after)) changed = true;
        foreach (var mapping in after.GodwroughtItems)
        {
            var item = state.Items.FirstOrDefault(i => i.Id == mapping.Value); if (item is null) continue;
            var godwrought = after.Adventure.Godwrought.Single(g => g.InstanceId == mapping.Key);
            if (item.BurningKills != godwrought.BurningKills) { item.BurningKills = godwrought.BurningKills; changed = true; }
        }
        if (!state.OwnedFragments.SetEquals(after.Adventure.OwnedFragments)) { state.OwnedFragments.UnionWith(after.Adventure.OwnedFragments); changed = true; }
        next.Profile.Discoveries.UnionWith(after.Adventure.Discoveries);
        if (changed || state.NextItemId != progression.CharacterState.NextItemId || next.Profile.Discoveries.Count != progression.ProfileState.Discoveries.Count) progression.AdoptAuthoritativeState(next);
        if (AdventureContent.Data.Rooms.All(r => progression.ProfileState.Discoveries.Contains(r.Discovery)) && !progression.ProfileState.Unlocks.Contains("profile.memory_cartography"))
        {
            var unlock = progression.UnlockProfile("discovery.cartography", "profile.memory_cartography"); Require(unlock); events.AddRange(unlock.Events);
        }
        foreach (var combatEvent in result.CombatEvents.Where(e => e.Kind == "AbilityStarted" && e.ActorId == 1))
        {
            if (!Content.Data.Skills.Any(s => s.Id == combatEvent.ContentId) || progression.CharacterState.Mastery.GetValueOrDefault(combatEvent.ContentId) >= 1000) continue;
            var mastery = progression.GainMastery($"mastery.{after.Tick}.{combatEvent.ActionId}", combatEvent.ContentId, 1);
            Require(mastery); events.AddRange(mastery.Events); changed = true;
        }
        foreach (var encounter in after.Adventure.CompletedEncounters.Except(before.CompletedEncounters))
        {
            int xp = encounter == "bell_saint.3" ? 300 : encounter.StartsWith("bell_saint.", StringComparison.Ordinal) ? 100 : 150;
            int materials = encounter == "bell_saint.3" ? 15 : 5;
            var reward = progression.EarnExperience($"encounter.{after.Adventure.Expedition}.{encounter}", xp, materials); Require(reward); events.AddRange(reward.Events); changed = true;
        }
        return changed;
    }
    // The legacy Adventure Godwrought ledger represents Ashcleaver instances specifically;
    // other equipment, regardless of rarity, is projected solely from canonical Items.
    private void ProjectStoredGodwrought() => Expedition.SetStoredGodwrought(progression.CharacterState.Items
        .Where(i => i.DefinitionId == "item.ashcleaver" && CharacterStash.IsStored(progression.CharacterState, i.Id))
        .Select(i => i.LegacyInstanceId == "" ? "ashcleaver.production." + i.Id : i.LegacyInstanceId));
    private void ProjectPermanentInventory()
    {
        var canonical = progression.CharacterState; var source = Expedition.Capture(); var world = source.Adventure;
        var derived = CombatSession.Restore(combatJson, source.Combat); derived.ApplyProgressionBuild(DeriveBuild());
        var combat = derived.Capture();
        world.Materials = canonical.Materials; world.OwnedFragments = new(canonical.OwnedFragments);
        var projected = canonical.Items.Where(i => !CharacterStash.IsStored(canonical, i.Id)).OrderBy(i => canonical.Equipment.Values.Contains(i.Id) ? 0 : 1).ThenBy(i => i.Id).Take(512).ToArray();
        combat.Inventory.Clear();
        foreach (var item in projected)
        {
            var definition = combatContent.Items.Single(i => i.Id == item.DefinitionId);
            combat.Inventory.Add(new(item.Id, item.DefinitionId, definition.Name, definition.Slot, item.Rarity.ToString(),
                item.BaseDamage, item.BaseArmor, item.BaseCriticalBasisPoints));
        }
        combat.Equipment.Clear(); foreach (var pair in canonical.Equipment) combat.Equipment[pair.Key.ToString()] = pair.Value;
        combat.Mutations.Clear(); foreach (var pair in canonical.SelectedMutations) combat.Mutations[pair.Key] = pair.Value;
        combat.NextObjectId = Math.Max(combat.NextObjectId, canonical.NextItemId);
        var mapping = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var ownedGodwrought = canonical.Items.Where(item => item.DefinitionId == "item.ashcleaver")
            .Select(item => item.LegacyInstanceId == "" ? "ashcleaver.production." + item.Id : item.LegacyInstanceId).ToHashSet(StringComparer.Ordinal);
        world.Godwrought = world.Godwrought.Where(item => ownedGodwrought.Contains(item.InstanceId)).ToArray();
        foreach (var item in canonical.Items.Where(i => i.DefinitionId == "item.ashcleaver"))
        {
            string id = item.LegacyInstanceId == "" ? "ashcleaver.production." + item.Id : item.LegacyInstanceId;
            var existing = world.Godwrought.FirstOrDefault(g => g.InstanceId == id) ?? new GodwroughtProgress { InstanceId = id };
            existing.BurningKills = item.BurningKills; existing.Evolution = item.Evolution; existing.TemperLevel = 0;
            if (!world.Godwrought.Any(g => g.InstanceId == id)) world.Godwrought = [.. world.Godwrought, existing];
            if (projected.Any(i => i.Id == item.Id)) mapping[id] = item.Id;
        }
        ProjectStoredGodwrought();
        Expedition.ApplyPermanentProjection(world, combat, mapping);
    }
    private CombatProgressionBuild DeriveBuild()
    {
        var view = progression.View; var state = progression.CharacterState;
        var properties = state.Equipment.Values.Select(id => state.Items.Single(i => i.Id == id)).SelectMany(item =>
            new[] { Content.Data.Items.Single(d => d.Id == item.DefinitionId).Property, item.Engraving }).ToHashSet();
        string[] unlocked = Content.Data.Skills.Where(s => state.UnlockedDisciplines.Contains(s.Discipline) && state.Mastery.GetValueOrDefault(s.Id) >= 100).SelectMany(s => s.Mutations).Distinct().Order(StringComparer.Ordinal).ToArray();
        return new(view.Discipline, view.Level, state.Passives.GetValueOrDefault("Offense"), state.Passives.GetValueOrDefault("Defense"),
            Math.Min(1000, view.Stats.GetValueOrDefault("affix.resource") + state.Passives.GetValueOrDefault("Resource")), Math.Min(2000, view.Stats.GetValueOrDefault("affix.damage")),
            Math.Min(10000, view.Stats.GetValueOrDefault("affix.armor")), Math.Min(7500, view.Stats.GetValueOrDefault("affix.critical")),
            Math.Min(2, view.Stats.GetValueOrDefault("affix.fork")), Math.Min(3, view.Stats.GetValueOrDefault("affix.chain")),
            properties.Contains("rune.guard"), properties.Contains("property.summon_burst"), view.UltimateSkills.Contains(Content.Data.Disciplines.Single(d => d.Id == view.Discipline).UltimateSkill), unlocked, state.PurifiedFragments.ToArray())
        {
            Resistances = state.Endgame is null ? null : EndgameProgression.Resistances(view.Stats),
            PyreTrail = properties.Contains(LegendaryEquipment.PyrePower),
            OathReprisal = properties.Contains(LegendaryEquipment.OathPower),
            WidowEcho = properties.Contains(LegendaryEquipment.WidowPower),
            VirulentWake = properties.Contains(LegendaryEquipment.RotwakePower),
            RallyingChorus = properties.Contains(LegendaryEquipment.MourningPower),
            CinderCycle = properties.Contains(LegendaryEquipment.FurnacePower),
            UnspokenVerdict = properties.Contains(LegendaryEquipment.CrownPower),
            WitnessVow = properties.Contains(LegendaryEquipment.WitnessPower),
            BorrowedHour = properties.Contains(LegendaryEquipment.HourPower),
            GriefsReprieve = properties.Contains(LegendaryEquipment.GriefPower),
            Widowthorn = properties.Contains(LegendaryEquipment.WidowthornPower),
            Emberwake = properties.Contains(LegendaryEquipment.EmberwakePower),
            LastVigilSet = EquipmentSets.Active(EquipmentSets.LastVigil, state),
            BriarboundSet = EquipmentSets.Active(EquipmentSets.Briarbound, state),
            AshrunnerSet = EquipmentSets.Active(EquipmentSets.Ashrunner, state)
        };
    }
    private void SynchronizeItemSequence()
    {
        long sequence = Combat.Capture().NextObjectId;
        if (sequence > progression.CharacterState.NextItemId)
        {
            var state = progression.Capture(); state.Character.NextItemId = sequence; progression.AdoptAuthoritativeState(state);
        }
    }
    private bool ImportUnmappedGodwrought(Ashenwake.Core.Progression.ProgressionState state, ExpeditionSnapshot source)
    {
        bool changed = false;
        foreach (var reward in source.Adventure.Godwrought.Where(g => !state.Items.Any(i => i.LegacyInstanceId == g.InstanceId || (i.LegacyInstanceId == "" && "ashcleaver.production." + i.Id == g.InstanceId))))
        {
            var definition = combatContent.Items.Single(i => i.Id == "item.ashcleaver");
            var item = new CombatItem(state.NextItemId++, definition.Id, definition.Name, definition.Slot, "Godwrought", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
            state.Items = [.. state.Items, new PermanentItem { Id = item.Id, DefinitionId = item.DefinitionId, Rarity = ItemRarity.Godwrought,
                BaseDamage = item.Damage, BaseArmor = item.Armor, BaseCriticalBasisPoints = item.CriticalBasisPoints, LegacyInstanceId = reward.InstanceId,
                BurningKills = reward.BurningKills, Evolution = reward.Evolution,
                Affixes = reward.TemperLevel == 0 ? [] : new() { ["affix.damage"] = reward.TemperLevel * 2 } }];
            changed = true;
        }
        return changed;
    }
    private void ValidateProjection()
    {
        var snapshot = Expedition.Capture(); var permanent = progression.CharacterState;
        if (snapshot.Adventure.Materials != permanent.Materials || !snapshot.Adventure.OwnedFragments.SetEquals(permanent.OwnedFragments) ||
            !snapshot.Combat.Equipment.SequenceEqual(permanent.Equipment.ToDictionary(p => p.Key.ToString(), p => p.Value).OrderBy(p => p.Key, StringComparer.Ordinal)) ||
            !snapshot.Combat.Mutations.SequenceEqual(permanent.SelectedMutations) || JsonData.Hash(Combat.ProgressionBuild) != JsonData.Hash(DeriveBuild()))
            throw new InvalidDataException("Permanent progression and runtime build projection disagree.");
        foreach (var item in snapshot.Combat.Inventory)
        {
            var canonical = permanent.Items.FirstOrDefault(i => i.Id == item.Id);
            if (canonical is null || CharacterStash.IsStored(permanent, item.Id) || canonical.DefinitionId != item.DefinitionId || canonical.BaseDamage != item.Damage || canonical.BaseArmor != item.Armor || canonical.BaseCriticalBasisPoints != item.CriticalBasisPoints || canonical.Rarity.ToString() != item.Rarity)
                throw new InvalidDataException("Runtime inventory differs from its permanent item owner.");
        }
        if (snapshot.Adventure.Godwrought.Length != permanent.Items.Count(i => i.DefinitionId == "item.ashcleaver") ||
            snapshot.Adventure.Godwrought.Any(g => !permanent.Items.Any(i => i.DefinitionId == "item.ashcleaver" && (i.LegacyInstanceId == "" ? "ashcleaver.production." + i.Id : i.LegacyInstanceId) == g.InstanceId && i.BurningKills == g.BurningKills && i.Evolution == g.Evolution && g.TemperLevel == 0)))
            throw new InvalidDataException("Godwrought stash differs from its permanent item owner.");
        foreach (var mapping in snapshot.GodwroughtItems)
        {
            var item = permanent.Items.Single(i => i.Id == mapping.Value); var world = snapshot.Adventure.Godwrought.Single(i => i.InstanceId == mapping.Key);
            if (item.DefinitionId != "item.ashcleaver" || item.BurningKills != world.BurningKills || item.Evolution != world.Evolution || world.TemperLevel != 0 || (item.LegacyInstanceId != "" && item.LegacyInstanceId != mapping.Key))
                throw new InvalidDataException("Godwrought instance history differs from its canonical item.");
        }
    }
}
