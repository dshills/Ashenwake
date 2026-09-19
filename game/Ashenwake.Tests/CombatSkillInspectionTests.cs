using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CombatSkillInspectionTests
{
    private static string Content() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));

    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void AllFiveDisciplinesExposeTheirSixAuthoredSkillsWithoutMutatingCombat(string discipline)
    {
        string json = Content(); var content = CombatContent.Parse(json); var session = CombatSession.CreateEncounter(json, 42, "hub");
        session.ApplyProgressionBuild(new(Discipline: discipline, Level: 1, UltimateUnlocked: false, UnlockedMutations: []));
        string original = session.StateHash;
        var skills = content.Skills.Where(skill => skill.Discipline == discipline).ToArray(); Assert.Equal(6, skills.Length);
        foreach (var skill in skills)
        {
            var inspection = session.InspectSkill(skill.Id);
            Assert.Equal(skill.Id, inspection.Id); Assert.Equal(skill.Name, inspection.Name); Assert.Equal(discipline, inspection.Discipline);
            Assert.Equal(skill.Shape, inspection.Shape); Assert.Equal(skill.Family, inspection.Family);
            Assert.Equal(skill.Damage, inspection.BaseDamage); Assert.Equal(100, inspection.DamagePercent);
            Assert.Equal(skill.Cost, inspection.Cost); Assert.Equal(skill.Generate, inspection.Generate);
            Assert.Equal(skill.Cooldown, inspection.CooldownTicks); Assert.Equal(skill.Range, inspection.Range); Assert.Equal(skill.Radius, inspection.Radius);
            Assert.Equal(skill.Status, inspection.Status); Assert.Equal(skill.Behavior, inspection.Behavior); Assert.Equal(skill.ResourceMode, inspection.ResourceMode);
            Assert.Empty(inspection.MutationId); Assert.Empty(inspection.MutationName); Assert.Empty(inspection.MutationDescription);
        }
        Assert.Equal(original, session.StateHash);
    }

    [Fact]
    public void EveryAuthoredMutationMatchesRuntimeShapeCostAndDefinitionMetadata()
    {
        string json = Content(); var content = CombatContent.Parse(json);
        foreach (var mutation in content.Mutations)
        {
            var skill = content.Skills.Single(skill => skill.Id == mutation.SkillId);
            var session = CombatSession.CreateEncounter(json, 42, "hub"); session.ApplyProgressionBuild(new(Discipline: skill.Discipline));
            string original = session.StateHash; var inspection = session.InspectSkill(skill.Id, mutation.Id);
            Assert.Equal(original, session.StateHash);
            Assert.Equal(mutation.Id, inspection.MutationId); Assert.Equal(mutation.Name, inspection.MutationName);
            Assert.Equal(mutation.Description, inspection.MutationDescription); Assert.Equal(mutation.DamagePercent, inspection.DamagePercent);
            Assert.Equal(skill.Damage, inspection.BaseDamage);
            session.Step([new(CombatCommandKind.SetMutation, SkillId: skill.Id, ContentId: mutation.Id)]);
            var live = session.View.Skills.Single(candidate => candidate.Id == skill.Id);
            Assert.Equal(live.Shape, inspection.Shape); Assert.Equal(live.Cost, inspection.Cost); Assert.Equal(live.CooldownTicks, inspection.CooldownTicks);
            int radius = inspection.Shape == "Projectile" && mutation.Radius == 0 ? skill.Radius : mutation.Radius;
            Assert.Equal(radius, inspection.Radius);
            string mutated = session.StateHash;
            Assert.Equal(skill.Shape, session.InspectSkill(skill.Id, "").Shape);
            Assert.Equal(100, session.InspectSkill(skill.Id, "").DamagePercent);
            Assert.Equal(mutated, session.StateHash);
        }
    }

    [Theory]
    [InlineData("skill.missing", "")]
    [InlineData("skill.cleave", "mutation.missing")]
    [InlineData("skill.cleave", "mutation.avalanche")]
    [InlineData("skill.fire_lance", "mutation.furnace")]
    public void InvalidOrMismatchedSkillMutationPairsRejectWithoutChangingState(string skill, string mutation)
    {
        var session = CombatSession.Create(Content()); string original = session.StateHash;
        Assert.Throws<InvalidDataException>(() => session.InspectSkill(skill, mutation)); Assert.Equal(original, session.StateHash);
    }

    [Fact]
    public void InspectionIsStaticEvenForLockedMutationsAndAnotherDiscipline()
    {
        var session = CombatSession.CreateEncounter(Content(), 42, "hub");
        session.ApplyProgressionBuild(new(Discipline: "Vanguard", Offense: 3, FlatDamage: 19, ResourceBonus: 40, UltimateUnlocked: false, UnlockedMutations: []));
        string original = session.StateHash;
        var locked = session.InspectSkill("skill.shield_breaker", "mutation.avalanche");
        Assert.Equal(50, locked.BaseDamage); Assert.Equal(80, locked.DamagePercent); Assert.Equal(30, locked.Cost); Assert.Equal(3400, locked.Radius);
        var other = session.InspectSkill("skill.fire_lance", "mutation.fire_furnace");
        Assert.Equal("Arcanist", other.Discipline); Assert.Equal("Heat", other.ResourceMode); Assert.Equal(13, other.Cost); Assert.Equal(2400, other.Radius);
        Assert.Equal(14, session.InspectSkill("skill.cleave").Generate);
        Assert.Equal(original, session.StateHash); Assert.Empty(session.Capture().Mutations);
    }

    [Theory]
    [InlineData("Projectile", 0, 1600)]
    [InlineData("Projectile", 2600, 2600)]
    [InlineData("Area", 0, 0)]
    [InlineData("Area", 3200, 3200)]
    public void ProjectedRadiusMatchesActualProjectileOrAreaCreation(string shape, int mutationRadius, int expectedRadius)
    {
        var authored = CombatContent.Parse(Content());
        var content = authored with { Mutations = authored.Mutations.Select(mutation => mutation.Id == "mutation.iron_rain" ? mutation with { Shape = shape, Radius = mutationRadius } : mutation).ToArray() };
        string json = JsonData.Write(content); var session = CombatSession.Create(json);
        var state = session.Capture(); state.Momentum = 100;
        // As in the combat fixtures, hold other actors still so this check isolates the cast geometry.
        foreach (var actor in state.Actors.Where(actor => actor.Faction == CombatFaction.Enemy)) actor.RecoveryUntil = 1000;
        state.Actors[1].Position = new(-2400, 0); session = CombatSession.Restore(json, state);
        session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.iron_rain")]);
        var inspection = session.InspectSkill("skill.seismic_wave", "mutation.iron_rain"); Assert.Equal(expectedRadius, inspection.Radius);
        Assert.Contains(session.Step([new(CombatCommandKind.Cast, SkillId: "skill.seismic_wave", TargetId: state.Actors[1].Id)]), e => e.Kind == "AbilityStarted");
        bool created = false;
        for (int i = 0; i < 15 && !created; i++)
        {
            session.Step(); var live = session.Capture();
            if (shape == "Projectile" && live.Projectiles.FirstOrDefault(projectile => projectile.SkillId == "skill.seismic_wave") is { } projectile)
            { Assert.Equal(inspection.Radius, projectile.ImpactRadius); created = true; }
            if (shape == "Area" && live.Areas.FirstOrDefault(area => area.SkillId == "skill.seismic_wave") is { } area)
            { Assert.Equal(inspection.Radius, area.Radius); created = true; }
        }
        Assert.True(created, "The ordinary cast must create geometry before its radius can be compared.");
    }
}
