using Ashenwake.Core.Content;

namespace Ashenwake.Core.Combat;

/// <summary>Authored skill metadata with one explicitly selected mutation; excludes equipment, passives, targets and combat state.</summary>
public sealed record CombatSkillInspection(string Id, string Name, string Discipline, string Shape, DamageFamily Family,
    int BaseDamage, int DamagePercent, int Cost, int Generate, int CooldownTicks, int Range, int Radius,
    string Status, string Behavior, string ResourceMode, string MutationId, string MutationName, string MutationDescription);

public sealed partial class CombatSession
{
    /// <summary>Inspects base or mutated form without selecting it or changing unlocks. Empty mutation means the base form.</summary>
    public CombatSkillInspection InspectSkill(string skillId, string mutationId = "")
    {
        var skill = _content.Skills.FirstOrDefault(candidate => candidate.Id == skillId)
            ?? throw new InvalidDataException("Unknown skill for inspection.");
        CombatMutation? mutation = null;
        if (mutationId != "")
        {
            mutation = _content.Mutations.FirstOrDefault(candidate => candidate.Id == mutationId && candidate.SkillId == skill.Id)
                ?? throw new InvalidDataException("Unknown mutation or mutation does not belong to this skill.");
        }
        string shape = mutation?.Shape ?? skill.Shape;
        // Projectile impact uses its authored base radius when a mutation does not supply a positive one.
        // Direct areas and dashes instead honor an explicit zero, exactly as Resolve does.
        int radius = shape == "Projectile" ? mutation?.Radius > 0 ? mutation.Radius : skill.Radius : mutation?.Radius ?? skill.Radius;
        return new(skill.Id, skill.Name, skill.Discipline, shape, skill.Family, skill.Damage, mutation?.DamagePercent ?? 100,
            skill.Cost + (mutation?.ExtraCost ?? 0), skill.Generate, skill.Cooldown, skill.Range, radius,
            skill.Status, skill.Behavior, skill.ResourceMode, mutation?.Id ?? "", mutation?.Name ?? "", mutation?.Description ?? "");
    }
}
