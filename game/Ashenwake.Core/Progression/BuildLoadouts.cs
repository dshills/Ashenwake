using System.Collections.ObjectModel;
using Ashenwake.Core.Adventure;

namespace Ashenwake.Core.Progression;

/// <summary>Owned references and reversible selections, never grants or a character snapshot.</summary>
public sealed record BuildLoadout(string Id, string Name, string Discipline,
    SortedDictionary<EquipmentSlot, long> Equipment, SortedDictionary<string, string> Fragments,
    SortedDictionary<string, string> Mutations, SortedDictionary<string, int> Passives,
    SortedDictionary<int, string> Manifestations);
public sealed record BuildLoadoutView(string Id, string Name, string Discipline,
    IReadOnlyDictionary<EquipmentSlot, long> Equipment, IReadOnlyDictionary<string, string> Fragments,
    IReadOnlyDictionary<string, string> Mutations, IReadOnlyDictionary<string, int> Passives,
    IReadOnlyDictionary<int, string> Manifestations);
public sealed record BuildLoadoutPreview(bool Success, string Reason, int MaterialCost, int RespecCost,
    int FragmentRemovalCost, IReadOnlyList<string> Changes, IReadOnlyList<string> Requirements);

public sealed partial class ProgressionSession
{
    public const int MaximumBuildLoadouts = 8;
    public IReadOnlyList<BuildLoadoutView> BuildLoadouts => Array.AsReadOnly((snapshot.Character.BuildLoadouts ?? [])
        .Select(p => new BuildLoadoutView(p.Id, p.Name, p.Discipline, ReadOnlyEquipment(p.Equipment),
            Frozen(p.Fragments), Frozen(p.Mutations), Frozen(p.Passives), Frozen(p.Manifestations))).ToArray());
    private static IReadOnlyDictionary<TKey, TValue> Frozen<TKey, TValue>(SortedDictionary<TKey, TValue> source) where TKey : notnull
        => new ReadOnlyDictionary<TKey, TValue>(new SortedDictionary<TKey, TValue>(source));
    private static bool ValidBuildId(string? id) => id is { Length: 9 } && id.StartsWith("loadout.", StringComparison.Ordinal) && id[8] is >= '1' and <= '8';
    private static string? BuildNameError(ProgressionState state, string id, string? name)
    {
        if (name is null || name.Length > 128 || name.Any(char.IsControl) || !ValidPresetName(name.Trim())) return "Use a name of 1–32 visible characters, without line breaks or control characters.";
        return (state.BuildLoadouts ?? []).Any(p => p.Id != id && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ? "Another build loadout already uses that name." : null;
    }
    internal ProgressionResult SaveBuildLoadout(string operationId, string id, string name, AdventureState anatomy)
        => Change(operationId, new { Action = "SaveBuildLoadout", id, name, anatomy.Anatomy, anatomy.Manifestations }, (next, events) =>
        {
            if (!ValidBuildId(id)) return "Choose build loadout 1 through 8.";
            string? error = BuildNameError(next.Character, id, name); if (error is not null) return error;
            var state = next.Character;
            var build = new BuildLoadout(id, name.Trim(), state.Discipline, new(state.Equipment), new(anatomy.Anatomy),
                new(state.SelectedMutations), new(state.Passives), new(anatomy.Manifestations));
            state.BuildLoadouts = [.. (state.BuildLoadouts ?? []).Where(p => p.Id != id).Append(build).OrderBy(p => p.Id, StringComparer.Ordinal)];
            events.Add("BuildLoadoutSaved:" + id); return null;
        });
    public ProgressionResult RenameBuildLoadout(string operationId, string id, string name)
        => Change(operationId, new { Action = "RenameBuildLoadout", id, name }, (next, events) =>
        {
            if (next.Character.BuildLoadouts?.Any(p => p.Id == id) != true) return "Choose a saved build loadout.";
            string? error = BuildNameError(next.Character, id, name); if (error is not null) return error;
            next.Character.BuildLoadouts = next.Character.BuildLoadouts.Select(p => p.Id == id ? p with { Name = name.Trim() } : p).ToArray();
            events.Add("BuildLoadoutRenamed:" + id); return null;
        });
    public ProgressionResult DeleteBuildLoadout(string operationId, string id)
        => Change(operationId, new { Action = "DeleteBuildLoadout", id }, (next, events) =>
        {
            if (next.Character.BuildLoadouts?.Any(p => p.Id == id) != true) return "Choose a saved build loadout.";
            var remaining = next.Character.BuildLoadouts.Where(p => p.Id != id).ToArray();
            next.Character.BuildLoadouts = remaining.Length == 0 ? null : remaining;
            events.Add("BuildLoadoutDeleted:" + id); return null;
        });
    internal ProgressionResult ApplyBuildLoadout(string operationId, string id, int cost)
        => Change(operationId, new { Action = "ApplyBuildLoadout", id, cost }, (next, events) =>
        {
            var build = next.Character.BuildLoadouts?.FirstOrDefault(p => p.Id == id);
            if (build is null) return "Choose a saved build loadout.";
            var errors = BuildRequirements(next.Character, build);
            if (errors.Count > 0) return string.Join(" ", errors);
            if (cost != BuildRespecCost(next.Character, build) || next.Character.Materials < cost) return "Insufficient materials for this build change.";
            next.Character.Equipment = new(build.Equipment); next.Character.SelectedMutations = new(build.Mutations);
            next.Character.Passives = new(build.Passives); next.Character.Materials -= cost;
            events.Add("BuildLoadoutApplied:" + id); return null;
        });
    internal int BuildRespecCost(ProgressionState state, BuildLoadout build) => state.Passives.Any(p => build.Passives.GetValueOrDefault(p.Key) < p.Value) ? Data.RespecCost : 0;
    internal List<string> BuildRequirements(ProgressionState state, BuildLoadout build)
    {
        var errors = new List<string>();
        if (build.Discipline != state.Discipline) errors.Add($"This loadout requires {build.Discipline}; retrain separately before applying it.");
        errors.AddRange(PreviewEquipmentArrangement(state, build.Equipment).Issues.Select(i => i.Reason));
        if (build.Passives.Values.Sum() > LevelOf(state) - 1) errors.Add("Not enough passive points for the saved allocation.");
        foreach (var (skill, mutation) in build.Mutations)
            if (state.Mastery.GetValueOrDefault(skill) < 100 || !Data.Skills.Any(s => s.Id == skill && s.Discipline == state.Discipline && s.Mutations.Contains(mutation)))
                errors.Add($"Master {skill} in the active discipline before selecting {mutation}.");
        foreach (var fragment in build.Fragments.Values)
            if (!state.OwnedFragments.Contains(fragment)) errors.Add($"Fragment {fragment} is no longer owned.");
        return errors;
    }
    private static void ValidateBuildLoadouts(ProgressionContent content, ProgressionState state)
    {
        var builds = state.BuildLoadouts; if (builds is null) return;
        if (builds.Length is < 1 or > MaximumBuildLoadouts || builds.Any(p => p is null || !ValidBuildId(p.Id) || !ValidPresetName(p.Name) ||
            !content.Data.Disciplines.Any(d => d.Id == p.Discipline) || p.Equipment is null || p.Fragments is null || p.Mutations is null || p.Passives is null || p.Manifestations is null) ||
            builds.Select(p => p.Id).Distinct().Count() != builds.Length || builds.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != builds.Length ||
            !builds.Select(p => p.Id).SequenceEqual(builds.Select(p => p.Id).Order(StringComparer.Ordinal))) throw new InvalidDataException("Malformed build loadout identities or collections.");
        foreach (var build in builds)
        {
            ValidateEquipmentPresets(content, state with { EquipmentPresets = [new("preset.1", build.Name, build.Equipment)] });
            if (build.Fragments.Count > 6 || build.Fragments.Any(p => p.Key is not ("Mind" or "Eyes" or "Heart" or "Spine" or "Arms" or "Legs") || !content.Data.FragmentIds.Contains(p.Value)) ||
                build.Fragments.Values.Distinct().Count() != build.Fragments.Count || build.Passives.Count > 3 || build.Passives.Any(p => p.Key is not ("Offense" or "Defense" or "Resource") || p.Value is < 1 or > 99) ||
                build.Passives.Values.Sum() >= content.Data.LevelCap || build.Mutations.Count > content.Data.Skills.Length ||
                build.Mutations.Any(p => !content.Data.Skills.Any(s => s.Id == p.Key && s.Discipline == build.Discipline && s.Mutations.Contains(p.Value))) ||
                build.Manifestations.Count > 32 || build.Manifestations.Any(p => p.Key is < 1 or > 10000 || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 100))
                throw new InvalidDataException("Invalid saved build choices.");
        }
    }
}
