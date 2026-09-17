using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class SliceCombatTests
{
    [Fact]
    public void DisplacedEmberlingDetonatesAtItsCommittedTelegraphPosition()
    {
        var state = CombatSession.CreateEncounter(Content(), 42, "encounter.ossuary").Capture();
        var rusher = state.Actors.Single(a => a.Role == "Rusher");
        var warning = state.Actors[0].Position;
        rusher.Position = new(6000, 0);
        rusher.Pending = new("enemy.detonate", 1, warning, state.Tick, state.NextActionId++);
        rusher.RecoveryUntil = state.Tick + 30;
        var session = CombatSession.Restore(Content(), state);
        Assert.Contains(session.Step(), e => e.Kind == "DamageApplied" && e.TargetId == 1 && e.ContentId == "enemy.detonate" && e.Amount > 0);
    }
    private static string Content() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static CombatCommand Cast(string id, int target) => new(CombatCommandKind.Cast, SkillId: id, TargetId: target);
    private static CombatSession AtMelee(string encounter = "bell_saint.1")
    {
        var state = CombatSession.CreateEncounter(Content(), 42, encounter).Capture();
        var enemy = state.Actors.First(a => a.Id != 1 && a.Health > 0);
        state.Actors[0].Position = new(enemy.Position.X - 1800, enemy.Position.Z);
        return CombatSession.Restore(Content(), state);
    }
    [Theory]
    [InlineData("hub")]
    [InlineData("encounter.ossuary")]
    [InlineData("encounter.cloister")]
    [InlineData("bell_saint.1")]
    [InlineData("bell_saint.2")]
    [InlineData("bell_saint.3")]
    [InlineData("clear")]
    public void EncounterStatesRoundTripAndRemainDeterministic(string id)
    {
        var a = CombatSession.CreateEncounter(Content(), 919, id); var b = CombatSession.Restore(Content(), a.Capture());
        for (int tick = 0; tick < 300; tick++)
        {
            Assert.Equal(JsonData.Write(a.Step()), JsonData.Write(b.Step()));
            if (tick % 30 == 0) b = CombatSession.Restore(Content(), b.Capture());
        }
        Assert.Equal(a.StateHash, b.StateHash);
    }
    [Fact]
    public void TravelPreservesItemsRngHealthAndChargesUntilAnchorRest()
    {
        var state = AtMelee().Capture(); state.Actors[0].Health = 137; state.PotionCharges = 1; state.Momentum = 48;
        var combat = CombatSession.CreateEncounter(Content(), 99, "encounter.cloister", state);
        Assert.Equal(137, combat.View.Actors[0].Health); Assert.Equal(1, combat.View.PotionCharges); Assert.Equal(48, combat.View.Momentum);
        Assert.Equal(JsonData.Write(state.Inventory), JsonData.Write(combat.Capture().Inventory));
        Assert.Equal(state.Rng, combat.Capture().Rng);
        var reset = CombatSession.CreateEncounter(Content(), 99, "encounter.cloister", combat.Capture(), restoreAtAnchor: true);
        Assert.Equal(350, reset.View.Actors[0].Health); Assert.Equal(3, reset.View.PotionCharges);
        Assert.DoesNotContain(combat.View.Fragments, f => f.Id == "fragment.heart_serath" && f.Equipped);
        Assert.DoesNotContain(CombatSession.CreateEncounter(Content(), 99, "hub", combat.Capture()).View.Actors, a => a.Faction == CombatFaction.Enemy);
    }
    [Fact]
    public void AnchorsAreDamageableAndBossIsShieldedUntilBothDie()
    {
        var session = AtMelee("bell_saint.2"); var boss = session.View.Actors.First(a => a.Role.StartsWith("BellSaint", StringComparison.Ordinal));
        session.Step([Cast("skill.cleave", boss.Id)]); for (int i = 0; i < 6; i++) session.Step();
        Assert.Equal(boss.MaxHealth, session.View.Actors.Single(a => a.Id == boss.Id).Health);
        var state = session.Capture();
        foreach (var anchor in state.Actors.Where(a => a.Role == "Anchor")) { anchor.Health = 0; anchor.DeathProcessed = true; anchor.State = "Dead"; }
        state.Cooldowns.Clear(); state.Actors[0].RecoveryUntil = state.Tick; state.Actors[0].Pending = null;
        session = CombatSession.Restore(Content(), state);
        session.Step([Cast("skill.cleave", boss.Id)]); for (int i = 0; i < 6; i++) session.Step();
        Assert.True(session.View.Actors.Single(a => a.Id == boss.Id).Health < boss.MaxHealth);
    }
    [Fact]
    public void SaintRaisesEachOwnedCorpseAtMostOnceAndBellsActIndependently()
    {
        var session = CombatSession.CreateEncounter(Content(), 42, "bell_saint.2");
        var state = session.Capture(); state.Actors[0] = state.Actors[0] with { Health = 10000, MaxHealth = 10000 };
        session = CombatSession.Restore(Content(), state);
        List<CombatEvent> events = [];
        for (int i = 0; i < 350; i++) events.AddRange(session.Step());
        Assert.Equal(2, events.Count(e => e.Kind == "CorpseResurrected"));
        Assert.Equal(2, session.Capture().ResurrectedActorIds.Distinct().Count());
        session = CombatSession.CreateEncounter(Content(), 42, "bell_saint.3");
        events.Clear(); for (int i = 0; i < 150; i++) events.AddRange(session.Step());
        Assert.Equal(2, events.Where(e => e.Kind == "AbilityStarted" && e.ContentId == "boss.bell_ring").Select(e => e.ActorId).Distinct().Count());
        Assert.Contains(events, e => e.ContentId == "boss.beast_rush");
    }
    [Fact]
    public void PriestHealsAndShieldsWhileEmberlingTelegraphsFiniteExplosion()
    {
        var session = CombatSession.CreateEncounter(Content(), 42, "encounter.cloister"); var state = session.Capture();
        state.Actors.First(a => a.Role == "Armored").Health = 10;
        state.Actors.First(a => a.Role == "Rusher").Position = new(-3400, 0);
        session = CombatSession.Restore(Content(), state);
        List<CombatEvent> events = [];
        for (int i = 0; i < 100; i++) events.AddRange(session.Step());
        Assert.Contains(events, e => e.Kind == "Healed" && e.ContentId == "enemy.mend");
        Assert.Contains(events, e => e.Kind == "BarrierGranted" && e.ContentId == "enemy.mend");
        Assert.Contains(events, e => e.Kind == "AbilityStarted" && e.ContentId == "enemy.detonate");
        Assert.Single(events, e => e.Kind == "EntityKilled" && e.ContentId == "enemy.detonate");
    }
    [Fact]
    public void BurningBloodDoesNotTriggerFromOutgoingHitsAndReducesPotionHealing()
    {
        var session = AtMelee(); var state = session.Capture(); state.Actors[0].Health = 100;
        session = CombatSession.Restore(Content(), state); session.ApplyAdventureBuild(new(Manifestation: "manifestation.burning_blood"));
        var heal = session.Step([new(CombatCommandKind.Potion)]).Single(e => e.Kind == "Healed");
        Assert.Equal(90, heal.Amount);
        int target = session.View.Actors.First(a => a.Faction == CombatFaction.Enemy).Id;
        session.Step([Cast("skill.cleave", target)]);
        List<CombatEvent> events = []; for (int i = 0; i < 5; i++) events.AddRange(session.Step());
        Assert.DoesNotContain(events, e => e.ContentId == "effect.burning_blood");
        Assert.Equal(session.Build, CombatSession.Restore(Content(), session.Capture()).Build);
    }
    [Fact]
    public void BurningBloodRespondsToIncomingPhysicalDamageOnceWithoutReflectionRecursion()
    {
        var state = AtMelee().Capture(); var boss = state.Actors.First(a => a.Faction == CombatFaction.Enemy);
        boss.Pending = new("enemy.strike", 1, state.Actors[0].Position, state.Tick, state.NextActionId++);
        boss.RecoveryUntil = state.Tick + 50;
        var session = CombatSession.Restore(Content(), state);
        session.ApplyAdventureBuild(new(Manifestation: "manifestation.burning_blood"));
        var events = session.Step();
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.TargetId == 1 && e.ContentId == "enemy.strike" && e.Amount > 0);
        Assert.Single(events, e => e.Kind == "DamageApplied" && e.ActorId == 1 && e.ContentId == "effect.burning_blood" && e.Amount == 8);
        Assert.Single(events, e => e.Kind == "ManifestationTriggered" && e.ContentId == "manifestation.burning_blood");
        Assert.DoesNotContain(events, e => e.Kind == "EffectBudgetExceeded");
        Assert.Equal(0, session.View.PendingEffects);
    }
    [Fact]
    public void StoneMemoryResistsRepeatedDamageAndSlowsDodgeRecovery()
    {
        var state = AtMelee().Capture(); state.Actors[0].Health = 300;
        state.Actors[0].Statuses.Add(new() { Id = "Burning", SourceId = state.Actors[1].Id, OwnerId = state.Actors[1].Id, NextTick = 0, ExpiresTick = 90, ActionId = state.NextActionId++ });
        var session = CombatSession.Restore(Content(), state); session.ApplyAdventureBuild(new(Manifestation: "manifestation.stone_memory"));
        List<int> amounts = [];
        for (int i = 0; i < 32; i++) amounts.AddRange(session.Step().Where(e => e.Kind == "DamageApplied" && e.TargetId == 1 && e.ContentId == "Burning").Select(e => e.Amount));
        Assert.Equal(new[] { 6, 5, 4, 4 }, amounts);
        session.Step([new(CombatCommandKind.Dodge, X: -1)]);
        Assert.Equal(39, session.View.DodgeCooldownTicks);
        session.ApplyAdventureBuild(new()); Assert.Equal(0, session.Capture().MemoryStacks);
    }
    [Fact]
    public void AshcleaverStacksSpeedAttacksAwakeningCreatesWaveAndEvolutionChangesHits()
    {
        var session = AtMelee(); var state = session.Capture(); state.Actors[0].Health = 100;
        var axe = CombatContent.Parse(Content()).Items.Single(i => i.Id == "item.ashcleaver");
        var axeId = state.NextObjectId++; state.Inventory.Add(new(axeId, axe.Id, axe.Name, axe.Slot, "Godwrought", axe.Damage, axe.Armor, axe.CriticalBasisPoints)); state.Equipment["MainHand"] = axeId;
        session = CombatSession.Restore(Content(), state);
        session.ApplyAdventureBuild(new(AshcleaverStacks: 5, AshcleaverAwakened: true, AshcleaverEvolution: "Serath", TemperLevel: 3, AshcleaverEquipped: true));
        int target = session.View.Actors.First(a => a.Faction == CombatFaction.Enemy).Id;
        session.Step([Cast("skill.cleave", target)]);
        Assert.Equal(9, session.View.Skills.Single(s => s.Id == "skill.cleave").RemainingTicks);
        List<CombatEvent> events = []; for (int i = 0; i < 9; i++) events.AddRange(session.Step());
        Assert.Contains(events, e => e.Kind == "Healed" && e.ContentId == "evolution.serath" && e.Amount > 0);
        Assert.Contains(events, e => e.Kind == "FlameWaveCreated");
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ContentId == "effect.ashcleaver_wave");
        Assert.Throws<InvalidDataException>(() => session.ApplyAdventureBuild(new(AshcleaverEvolution: "Orrun")));
        session.ApplyAdventureBuild(new(AshcleaverStacks: 5, AshcleaverAwakened: true, AshcleaverEquipped: false));
        Assert.False(session.Build.AshcleaverEquipped);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LowAndHighResonanceCanDefeatTheSameSaintPhase(bool highResonance)
    {
        var state = AtMelee().Capture(); state.Fragments.Clear();
        if (highResonance) foreach (var fragment in CombatContent.Parse(Content()).Fragments) state.Fragments[fragment.Slot.ToString()] = fragment.Id;
        var session = CombatSession.Restore(Content(), state);
        if (highResonance) session.ApplyAdventureBuild(new(Manifestation: "manifestation.burning_blood"));
        for (int tick = 0; tick < 1500 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
        {
            var view = session.View; var player = view.Actors[0]; var target = view.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
            var commands = new List<CombatCommand> { new(CombatCommandKind.Move, X: Math.Sign(target.Position.X - player.Position.X), Z: Math.Sign(target.Position.Z - player.Position.Z)) };
            if (player.Health < 180) commands.Add(new(CombatCommandKind.Potion));
            if (tick % 12 == 0) commands.Add(Cast("skill.cleave", target.Id));
            if (tick % 47 == 0) commands.Add(Cast("skill.shield_breaker", target.Id));
            session.Step(commands);
        }
        Assert.True(session.View.Actors[0].Health > 0);
        Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
    }
    [Fact]
    public void CuratedLoadoutsHaveTwentyDistinctEquipmentChoicesAndMultipleBuildDirections()
    {
        var content = CombatContent.Parse(Content());
        Assert.Equal(20, content.Loadouts.Length);
        Assert.Equal(20, content.Loadouts.Select(l => string.Join("/", l.ItemIds)).Distinct().Count());
        Assert.Contains(content.Loadouts, l => l.FragmentIds.Length == 0);
        Assert.Contains(content.Loadouts, l => l.FragmentIds.Length == 4);
        Assert.Contains(content.Loadouts, l => l.MutationIds.Contains("mutation.no_ground_given"));
        Assert.Contains(content.Loadouts, l => l.MutationIds.Contains("mutation.furnace"));
        foreach (var loadout in content.Loadouts)
        {
            var state = CombatSession.Create(Content()).Capture(); state.Equipment.Clear(); state.Fragments.Clear(); state.Mutations.Clear();
            foreach (var id in loadout.ItemIds)
            {
                var definition = content.Items.Single(i => i.Id == id);
                var item = state.Inventory.FirstOrDefault(i => i.DefinitionId == id);
                if (item is null) { item = new(state.NextObjectId++, id, definition.Name, definition.Slot, "Godwrought", definition.Damage, definition.Armor, definition.CriticalBasisPoints); state.Inventory.Add(item); }
                state.Equipment[item.Slot] = item.Id;
            }
            foreach (var id in loadout.FragmentIds) state.Fragments[content.Fragments.Single(f => f.Id == id).Slot.ToString()] = id;
            foreach (var id in loadout.MutationIds) state.Mutations[content.Mutations.Single(m => m.Id == id).SkillId] = id;
            var session = CombatSession.Restore(Content(), state);
            Assert.Equal(loadout.FragmentIds.Length, session.View.Fragments.Count(f => f.Equipped));
        }
        Assert.DoesNotContain(CombatSession.Create(Content()).View.Inventory, i => i.DefinitionId == "item.ashcleaver");
    }
}
