using Ashenwake.Core.Progression;
using Ashenwake.Core.Combat;

namespace Ashenwake.Core.Production;

public sealed partial class ProductionSession
{
    public IReadOnlyList<BuildLoadoutView> BuildLoadouts => progression.BuildLoadouts;
    public ProductionResult SaveBuildLoadout(string id, string name) => Execute(new(ProductionAction.SaveBuildLoadout, Id: id, Value: name));
    public ProductionResult RenameBuildLoadout(string id, string name) => Execute(new(ProductionAction.RenameBuildLoadout, Id: id, Value: name));
    public ProductionResult DeleteBuildLoadout(string id) => Execute(new(ProductionAction.DeleteBuildLoadout, Id: id));
    public ProductionResult ApplyBuildLoadout(string id) => Execute(new(ProductionAction.ApplyBuildLoadout, Id: id));
    public static bool IsBuildLoadoutAction(ProductionAction action) => action is ProductionAction.SaveBuildLoadout or ProductionAction.RenameBuildLoadout or ProductionAction.DeleteBuildLoadout or ProductionAction.ApplyBuildLoadout;
    public string BuildLoadoutServiceBlockedReason()
    {
        if (View.RoomId != "room.greyhaven") return "Manage complete builds at Mara's workshop in Greyhaven.";
        if (!Combat.View.Actors.Any(a => a.Id == 1 && a.Health > 0)) return "Cannot manage builds while defeated.";
        if (!Near("service.mara")) return "Visit Mara to manage complete build loadouts.";
        return "";
    }
    public BuildLoadoutPreview PreviewBuildLoadout(string id)
    {
        var state = progression.CharacterState;
        var build = state.BuildLoadouts?.FirstOrDefault(b => b.Id == id);
        if (build is null) return new(false, "Choose a saved build loadout.", 0, 0, 0, [], ["Choose a saved build loadout."]);
        var world = Expedition.CaptureAdventure(); var data = Content.Data;
        var errors = progression.BuildRequirements(state, build);
        string blocked = BuildLoadoutServiceBlockedReason(); if (blocked.Length > 0) errors.Insert(0, blocked);
        foreach (var (slot, fragment) in build.Fragments)
            if (!AdventureContent.Data.Fragments.Any(f => f.Id == fragment && f.Slot == slot)) errors.Add($"Fragment {fragment} does not fit {slot}.");
        int resonance = build.Fragments.Values.Sum(id => AdventureContent.Data.Fragments.FirstOrDefault(f => f.Id == id)?.Resonance ?? 0);
        foreach (var (threshold, manifestation) in build.Manifestations)
        {
            if (!AdventureContent.Data.Manifestations.Any(m => m.Id == manifestation && m.Threshold == threshold)) errors.Add($"Unknown manifestation {manifestation}.");
            else if (world.Manifestations.GetValueOrDefault(threshold) != manifestation && resonance < threshold)
                errors.Add($"{Friendly(manifestation)} requires {threshold} resonance in the saved anatomy.");
        }
        int cost = progression.BuildRespecCost(state, build);
        if (state.Materials < cost) errors.Add($"Requires {cost} materials; only {state.Materials} are available.");
        var changes = new List<string>();
        string Item(long itemId)
        {
            if (itemId == 0) return "Empty";
            var item = state.Items.FirstOrDefault(i => i.Id == itemId);
            var definition = data.Items.FirstOrDefault(i => i.Id == item?.DefinitionId);
            return definition is null ? $"Missing item #{itemId}" : (combatContent.Items.FirstOrDefault(i => i.Id == definition.Id)?.Name ?? Friendly(definition.Id)) + $" (#{itemId})";
        }
        foreach (var slot in Enum.GetValues<EquipmentSlot>()) changes.Add($"Equipment · {slot}: {Item(state.Equipment.GetValueOrDefault(slot))} → {Item(build.Equipment.GetValueOrDefault(slot))}");
        foreach (string slot in new[] { "Mind", "Eyes", "Heart", "Spine", "Arms", "Legs" }) changes.Add($"Anatomy · {slot}: {Friendly(world.Anatomy.GetValueOrDefault(slot))} → {Friendly(build.Fragments.GetValueOrDefault(slot))}");
        foreach (var skill in data.Skills.Where(s => s.Discipline == build.Discipline)) changes.Add($"Mutation · {Friendly(skill.Id)}: {Friendly(state.SelectedMutations.GetValueOrDefault(skill.Id))} → {Friendly(build.Mutations.GetValueOrDefault(skill.Id))}");
        foreach (string passive in new[] { "Offense", "Defense", "Resource" }) changes.Add($"Passive · {passive}: {state.Passives.GetValueOrDefault(passive)} → {build.Passives.GetValueOrDefault(passive)}");
        foreach (int threshold in world.Manifestations.Keys.Union(build.Manifestations.Keys).Order()) changes.Add($"Manifestation · {threshold} resonance: {Friendly(world.Manifestations.GetValueOrDefault(threshold))} → {Friendly(build.Manifestations.GetValueOrDefault(threshold))}");
        return new(errors.Count == 0, string.Join(" ", errors), cost, cost, 0, changes.AsReadOnly(), errors.AsReadOnly());
    }
    private static string Friendly(string? id) => string.IsNullOrEmpty(id) ? "None" : id[(id.LastIndexOf('.') + 1)..].Replace('_', ' ');
    private ProductionResult ExecuteBuildLoadout(ProductionCommand command)
    {
        string blocked = BuildLoadoutServiceBlockedReason(); if (blocked.Length > 0) return new(false, blocked, [], []);
        string operation = "player." + operationSequence;
        ProgressionResult result;
        if (command.Action == ProductionAction.ApplyBuildLoadout)
        {
            var preview = PreviewBuildLoadout(command.Id);
            if (!preview.Success) return new(false, preview.Reason, [], []);
            var build = progression.CharacterState.BuildLoadouts!.Single(b => b.Id == command.Id);
            result = progression.ApplyBuildLoadout(operation, command.Id, preview.MaterialCost);
            if (!result.Success) return new(false, result.Reason, [], result.Events);
            // Both domains are covered by Execute's rollback. No travel, grants, RNG, health reset, or partial apply.
            var anatomy = Expedition.ApplyBuildAnatomy(build.Fragments, build.Manifestations);
            if (!anatomy.Success) return new(false, anatomy.Reason, [], []);
            result = result with { Events = result.Events.Concat(anatomy.Events).ToArray() };
        }
        else result = command.Action switch
        {
            ProductionAction.SaveBuildLoadout => progression.SaveBuildLoadout(operation, command.Id, command.Value, Expedition.CaptureAdventure()),
            ProductionAction.RenameBuildLoadout => progression.RenameBuildLoadout(operation, command.Id, command.Value),
            _ => progression.DeleteBuildLoadout(operation, command.Id)
        };
        if (result.Success) ProjectPermanentInventory();
        return new(result.Success, result.Reason, [], result.Events);
    }
    private void ValidateBuildLoadoutAnatomy()
    {
        foreach (var build in progression.CharacterState.BuildLoadouts ?? [])
            if (build.Fragments.Any(p => !AdventureContent.Data.Fragments.Any(f => f.Id == p.Value && f.Slot == p.Key)) ||
                build.Manifestations.Any(p => !AdventureContent.Data.Manifestations.Any(m => m.Id == p.Value && m.Threshold == p.Key)))
                throw new InvalidDataException("Saved build anatomy does not match content.");
    }
}
