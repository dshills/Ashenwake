using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class ProductionCombatTests
{
    private static string Content() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static CombatSession Session(string discipline = "Vanguard", string encounter = "encounter.ossuary")
    {
        var hub = CombatSession.CreateEncounter(Content(), 42, "hub"); hub.ApplyProgressionBuild(new(Discipline: discipline));
        var state = CombatSession.CreateEncounter(Content(), 42, encounter, hub.Capture()).Capture();
        int index = 0;
        foreach (var actor in state.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            actor.Position = new(-2400 + index++ * 1600, 0); actor.RecoveryUntil = 1000; actor.Hidden = false;
        }
        return CombatSession.Restore(Content(), state);
    }
    private static CombatCommand Cast(string id, int target) => new(CombatCommandKind.Cast, SkillId: id, TargetId: target);
    private static List<CombatEvent> Advance(CombatSession session, int ticks)
    { List<CombatEvent> events = []; for (int i = 0; i < ticks; i++) events.AddRange(session.Step()); return events; }
    [Theory]
    [InlineData("Vanguard", "Momentum")]
    [InlineData("Veilwalker", "Exposure")]
    [InlineData("Arcanist", "Instability")]
    [InlineData("Gravecaller", "Remains")]
    [InlineData("Warden", "Adaptation")]
    public void EveryDisciplineHasSixSkillsAndDeterministicPlayableBossBaseline(string discipline, string resource)
    {
        var hub = CombatSession.CreateEncounter(Content(), 57, "hub"); hub.ApplyProgressionBuild(new(Discipline: discipline));
        var session = CombatSession.CreateEncounter(Content(), 57, "bell_saint.1", hub.Capture());
        Assert.Equal(6, session.View.Skills.Count); Assert.Equal(resource, session.View.ResourceName);
        var other = CombatSession.Restore(Content(), session.Capture());
        for (int i = 0; i < 2500 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); i++)
        {
            var commands = CombatProductionSmoke.Commands(session.View);
            Assert.Equal(JsonData.Write(session.Step(commands)), JsonData.Write(other.Step(commands)));
            if (i % 40 == 0) other = CombatSession.Restore(Content(), other.Capture());
        }
        Assert.Equal(session.StateHash, other.StateHash);
        Assert.True(session.View.Actors[0].Health > 0);
        Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
    }
    [Fact]
    public void ArcanistBuildsHeatVentsAndLocksOverCapacityWithoutSpending()
    {
        var session = Session("Arcanist"); int target = session.View.Actors[1].Id;
        session.Step([Cast("skill.fire_lance", target)]); Assert.Equal(8, session.View.Resource);
        var state = session.Capture(); state.Momentum = 99; state.Actors[0].Pending = null; state.Actors[0].RecoveryUntil = state.Tick; state.Cooldowns.Clear();
        session = CombatSession.Restore(Content(), state);
        Assert.Contains(session.Step([Cast("skill.fire_lance", target)]), e => e.Kind == "CommandRejected"); Assert.Equal(99, session.View.Resource);
        session.Step([Cast("skill.vent", target)]); Advance(session, 3); Assert.InRange(session.View.Resource, 50, 59);
        Assert.True(session.View.Barrier > 0);
    }
    [Fact]
    public void ExposureRewardsBehindTargetAndWardenAdaptsIncomingDamage()
    {
        var session = Session("Veilwalker"); var state = session.Capture(); state.Actors[1].FacingX = 1;
        session = CombatSession.Restore(Content(), state); session.Step([Cast("skill.venom_knife", state.Actors[1].Id)]); Advance(session, 5);
        Assert.Equal(20, session.View.Resource);
        session = Session("Warden"); state = session.Capture(); var enemy = state.Actors[1];
        enemy.Pending = new("enemy.strike", 1, state.Actors[0].Position, state.Tick, state.NextActionId++); enemy.Position = new(-3300, 0);
        session = CombatSession.Restore(Content(), state); session.Step();
        Assert.Equal(8, session.View.Resource); Assert.Equal(1, session.Capture().ThreatStacks);
    }
    [Fact]
    public void CorpseConsumptionArbitratesSummonsHealingAndBossResurrection()
    {
        var session = Session("Gravecaller", "bell_saint.2"); var state = session.Capture(); state.Momentum = 100;
        var corpse = state.Actors.First(a => a.Health == 0); corpse.Position = new(-3000, 0);
        session = CombatSession.Restore(Content(), state);
        session.Step([Cast("skill.raise_ancestor", 0)]); Advance(session, 12);
        Assert.Contains(session.View.Actors, a => a.Faction == CombatFaction.Ally);
        Assert.Contains(corpse.Id, session.Capture().ConsumedCorpseIds);
        Assert.Contains(session.Step([new(CombatCommandKind.ConsumeCorpse, TargetId: corpse.Id)]), e => e.Kind == "CommandRejected");
        Advance(session, 200); Assert.DoesNotContain(corpse.Id, session.Capture().ResurrectedActorIds);
    }
    [Fact]
    public void FireLanceForksExplodesAndCauterizesThroughAttributedBurning()
    {
        var session = Session("Arcanist"); int target = session.View.Actors[1].Id;
        session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.forking_flame")]);
        session.Step([Cast("skill.fire_lance", target)]); var events = Advance(session, 30);
        Assert.Equal(2, events.Count(e => e.Kind == "ProjectileForked"));
        session = Session("Arcanist"); var state = session.Capture(); state.Actors[0].Health = 100;
        session = CombatSession.Restore(Content(), state);
        session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.cauterize")]); session.Step([Cast("skill.fire_lance", session.View.Actors[1].Id)]);
        events = Advance(session, 35); Assert.Contains(events, e => e.Kind == "Healed" && e.ContentId == "mutation.cauterize");
        session = Session("Arcanist"); session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.fire_furnace")]); session.Step([Cast("skill.fire_lance", session.View.Actors[1].Id)]);
        events = Advance(session, 20); Assert.True(events.Where(e => e.Kind == "DamageApplied" && e.ContentId == "skill.fire_lance").Select(e => e.TargetId).Distinct().Count() >= 2);
    }
    [Fact]
    public void ChilledTransformsIntoFrozenAndBossesResistHardControl()
    {
        var session = Session("Arcanist"); var state = session.Capture(); state.Actors[0].Position = new(-3300, 0);
        session = CombatSession.Restore(Content(), state); session.Step([Cast("skill.frost_nova", 0)]);
        var events = Advance(session, 52); Assert.Contains(events, e => e.Kind == "StatusApplied" && e.ContentId == "Frozen");
        var before = session.View.Actors[1].Position; session.Step(); Assert.Equal(before, session.View.Actors[1].Position);
        session = Session("Vanguard", "bell_saint.1"); state = session.Capture(); state.Momentum = 100;
        session = CombatSession.Restore(Content(), state); session.Step([Cast("skill.shield_breaker", session.View.Actors[1].Id)]);
        Assert.Contains(Advance(session, 12), e => e.Kind == "StatusResisted" && e.ContentId == "Staggered");
    }
    [Fact]
    public void AllStatusesRestoreAndInvalidStatusCannotEnterSimulation()
    {
        var session = Session(); var state = session.Capture();
        foreach (string id in new[] { "Burning", "Bleeding", "Poisoned", "Chilled", "Frozen", "Shocked", "Staggered", "Cursed", "Terrified", "Marked", "Vulnerable", "Rooted" })
            state.Actors[1].Statuses.Add(new() { Id = id, SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 10, ExpiresTick = 90 });
        session = CombatSession.Restore(Content(), state); Assert.Equal(12, session.View.Actors[1].Statuses.Count);
        state.Actors[1].Statuses[0].Id = "ArbitraryScript"; Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), state));
    }
    [Fact]
    public void AdvancedFragmentsStoreReleaseHeatAndCaptureOnlyCuratedEcho()
    {
        var session = Session(); var state = session.Capture(); state.Momentum = 100; state.Fragments["Heart"] = "fragment.heart_vael"; state.FragmentHeat = 100;
        session = CombatSession.Restore(Content(), state); session.Step([Cast("skill.shield_breaker", session.View.Actors[1].Id)]); Assert.Equal(0, session.Capture().FragmentHeat);
        Assert.Contains(Advance(session, 12), e => e.ContentId == "effect.overheated" && e.Kind == "DamageApplied");
        session = Session(); state = session.Capture(); state.Fragments["Arms"] = "fragment.orrun_knuckle";
        session = CombatSession.Restore(Content(), state); Advance(session, 30); session.Step([new(CombatCommandKind.Move, X: 1)]);
        Assert.Contains(session.Step(), e => e.ContentId == "effect.seismic_release");
        session = Session("Vanguard", "bell_saint.1"); state = session.Capture(); state.Actors[1].Health = 1; state.Fragments["Mind"] = "fragment.last_memory";
        session = CombatSession.Restore(Content(), state); session.Step([Cast("skill.cleave", session.View.Actors[1].Id)]); Advance(session, 6);
        Assert.Equal("skill.echo_storm", session.View.CapturedSkillId);
        session = CombatSession.CreateEncounter(Content(), 42, "bell_saint.1", session.Capture());
        Assert.Contains(session.Step([new(CombatCommandKind.CastEcho, TargetId: session.View.Actors[1].Id)]), e => e.Kind == "CapturedAbilityUsed");
        Assert.Equal("", session.View.CapturedSkillId);
    }
    [Fact]
    public void AnatomyProjectionRetainsOwnedChargesAndCleansRemovedFragmentsAtomically()
    {
        var state = Session().Capture();
        state.Fragments["Heart"] = "fragment.heart_vael"; state.Fragments["Mind"] = "fragment.last_memory"; state.Fragments["Arms"] = "fragment.orrun_knuckle";
        state.FragmentHeat = 100; state.CapturedSkillId = "skill.echo_storm"; state.CapturedUntil = state.Tick + 450; state.SeismicCharge = 90;
        state.OverheatedActionId = state.NextActionId++;
        var session = CombatSession.CreateEncounter(Content(), 42, "hub", state);
        session.ApplyAnatomy(state.Fragments);
        Assert.Equal(100, session.View.FragmentHeat); Assert.Equal("skill.echo_storm", session.View.CapturedSkillId); Assert.Equal(90, session.View.SeismicCharge);
        string before = session.StateHash;
        Assert.Throws<InvalidDataException>(() => session.ApplyAnatomy(new Dictionary<string, string> { ["Legs"] = "fragment.heart_vael" }));
        Assert.Equal(before, session.StateHash);
        session.ApplyAnatomy(new Dictionary<string, string> { ["Eyes"] = "fragment.eye_vael" });
        Assert.Equal(0, session.View.FragmentHeat); Assert.Equal("", session.View.CapturedSkillId); Assert.Equal(0, session.View.SeismicCharge);
        Assert.Equal(0, session.Capture().OverheatedActionId);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content(), session.Capture()).StateHash);
        state = session.Capture(); state.FragmentHeat = 1;
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), state));
        state = session.Capture(); state.CapturedSkillId = "skill.echo_storm";
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), state));
        state = session.Capture(); state.SeismicCharge = 1;
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), state));
    }
    [Fact]
    public void ProductionAnatomyChangesUseCoreChargeCleanup()
    {
        var adventure = AdventureContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "adventure.json")));
        var progression = ProgressionContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "progression.json")));
        var session = ProductionSession.Create(Content(), adventure, progression);
        Assert.True(session.InstallFragment("Heart", "fragment.heart_vael").Success);
        Assert.True(session.InstallFragment("Mind", "fragment.last_memory").Success);
        var state = session.Capture(); state.Expedition.Combat.FragmentHeat = 100;
        state.Expedition.Combat.CapturedSkillId = "skill.echo_storm"; state.Expedition.Combat.CapturedUntil = state.Expedition.Combat.Tick + 450;
        session = ProductionSession.Restore(Content(), adventure, progression, state);
        Assert.True(session.InstallFragment("Heart", null).Success);
        Assert.Equal(0, session.Combat.View.FragmentHeat); Assert.Equal("skill.echo_storm", session.Combat.View.CapturedSkillId);
        Assert.True(session.InstallFragment("Mind", null).Success); Assert.Equal("", session.Combat.View.CapturedSkillId);
        Assert.Equal(session.StateHash, ProductionSession.Restore(Content(), adventure, progression, session.Capture()).StateHash);
    }
    [Fact]
    public void NaturalLootCoversAllConventionalRaritiesWithUnchangedTwoDrawStream()
    {
        var definitions = CombatContent.Parse(Content()).Items.Where(i => i.Id != "item.ashcleaver").ToArray();
        var rarities = new HashSet<string>();
        for (ulong seed = 1; seed <= 64; seed++)
        {
            var state = Session().Capture(); state.Rng = state.Rng with { Loot = SeededRandom.Streams(seed).Loot };
            var rng = state.Rng.Loot; var definition = definitions[SeededRandom.Range(ref rng, definitions.Length)]; int roll = SeededRandom.Range(ref rng, 6);
            state.Actors[1].Health = 1;
            state.Actors[1].Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 0, ExpiresTick = 90 });
            var session = CombatSession.Restore(Content(), state); session.Step();
            var item = Assert.Single(session.View.Loot).Item; rarities.Add(item.Rarity);
            Assert.Equal(rng, session.Capture().Rng.Loot); Assert.Equal(definition.Id, item.DefinitionId);
            Assert.Equal(definition.Damage + (definition.Damage > 0 ? roll : 0), item.Damage);
            Assert.Equal(definition.Id == "item.echo_ring" ? "Legendary" : roll switch { 0 => "Common", 1 or 2 => "Tempered", 3 or 4 => "Rare", _ => "Relic" }, item.Rarity);
        }
        Assert.Equal(new[] { "Common", "Legendary", "Rare", "Relic", "Tempered" }, rarities.Order().ToArray());
    }
    [Fact]
    public void BothManifestationTiersOperateTogetherAndPurificationReducesComplications()
    {
        var session = Session(); var state = session.Capture(); state.Actors[0].Health = 100;
        session = CombatSession.Restore(Content(), state); session.ApplyAdventureBuild(new(Manifestation: "manifestation.burning_blood", SecondaryManifestation: "manifestation.whispering_shadow"));
        session.ApplyProgressionBuild(new(PurifiedFragments: ["fragment.eye_vael", "fragment.heart_serath"]));
        Assert.NotEmpty(session.View.Illusions!);
        Assert.Contains(session.Step([new(CombatCommandKind.Potion)]), e => e.Kind == "Healed" && e.Amount == 105);
        Advance(session, 40); Assert.Empty(session.View.Illusions!);
        session = Session("Gravecaller", "bell_saint.2"); state = session.Capture(); state.Actors[0].Health = 100; state.Actors.First(a => a.Health == 0).Position = new(-3200, 0);
        session = CombatSession.Restore(Content(), state); session.ApplyAdventureBuild(new(SecondaryManifestation: "manifestation.voracious_renewal"));
        session.Step([new(CombatCommandKind.ConsumeCorpse, TargetId: session.View.Actors.First(a => a.Health == 0).Id)]);
        Assert.Equal(370, session.View.Actors[0].MaxHealth); Assert.True(session.View.Actors[0].Health > 100);
        Advance(session, 181); Assert.Equal(350, session.View.Actors[0].MaxHealth);
    }
    [Fact]
    public void RingCompatibilityAndTwoHandedRestrictionsRejectInvalidSnapshots()
    {
        var session = CombatSession.Create(Content()); var state = session.Capture();
        var ring = state.Inventory.Single(i => i.DefinitionId == "item.echo_ring"); state.Equipment["Ring2"] = ring.Id;
        session = CombatSession.Restore(Content(), state); Assert.Equal(ring.Id, session.View.Equipment["Ring2"]);
        state.Equipment["Ring1"] = ring.Id; Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), state));
        state = session.Capture(); state.Equipment["MainHand"] = state.Inventory.Single(i => i.DefinitionId == "item.greatstaff").Id;
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content(), state));
        Assert.Equal(12, Enum.GetValues<Ashenwake.Core.Progression.EquipmentSlot>().Length);
    }
    [Fact]
    public void MultiPulsePrimaryGrantsResourceOnlyOnceIncludingAfterRestore()
    {
        var state = Session().Capture(); state.Momentum = 10;
        var session = CombatSession.Restore(Content(), state);
        session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.reaping_arc")]);
        session.Step([Cast("skill.cleave", 0)]); Advance(session, 10);
        Assert.Equal(19, session.View.Resource);
        session = CombatSession.Restore(Content(), session.Capture()); Advance(session, 50);
        Assert.Equal(19, session.View.Resource);
    }
    [Fact]
    public void ChargedShieldBreakerCanReleaseEarlyAndReceivesChargeDamage()
    {
        var state = Session().Capture(); state.Momentum = 100;
        var session = CombatSession.Restore(Content(), state); int target = session.View.Actors[1].Id;
        session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.orruns_patience")]);
        session.Step([Cast("skill.shield_breaker", target)]); Advance(session, 20);
        Assert.NotNull(session.Capture().Actors[0].Pending);
        var events = session.Step([new(CombatCommandKind.ReleaseCharge)]);
        Assert.Contains(events, e => e.Kind == "ChargeReleased" && e.Amount >= 20);
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ContentId == "skill.shield_breaker" && e.Amount >= 60);
    }
    [Fact]
    public void GodwroughtEvolutionsRaiseRevenantsOrReleaseMoltenSeismicDamage()
    {
        foreach (string evolution in new[] { "Serath", "Orrun" })
        {
            var state = Session().Capture(); var definition = CombatContent.Parse(Content()).Items.Single(i => i.Id == "item.ashcleaver");
            long id = state.NextObjectId++; state.Inventory.Add(new(id, definition.Id, definition.Name, definition.Slot, "Godwrought", definition.Damage, definition.Armor, definition.CriticalBasisPoints)); state.Equipment["MainHand"] = id;
            state.Actors[1].Health = evolution == "Serath" ? 1 : state.Actors[1].Health;
            state.Actors[1].Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 50, ExpiresTick = 100 });
            var session = CombatSession.Restore(Content(), state); session.ApplyAdventureBuild(new(AshcleaverStacks: 5, AshcleaverAwakened: true, AshcleaverEvolution: evolution, AshcleaverEquipped: true));
            session.Step([Cast("skill.cleave", session.View.Actors[1].Id)]); var events = Advance(session, 8);
            if (evolution == "Serath") Assert.Contains(session.View.Actors, a => a.DefinitionId == "summon.flaming_revenant");
            else Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ContentId == "effect.molten_seismic");
        }
    }
    [Fact]
    public void LivingFlameCanRaiseAConsumedCorpseAndKilledEmberlingsWarnBeforeExploding()
    {
        bool raised = false;
        for (ulong seed = 1; seed <= 8 && !raised; seed++)
        {
            var state = Session("Arcanist").Capture(); state.Actors[1].Health = 1; state.Rng = SeededRandom.Streams(seed);
            var session = CombatSession.Restore(Content(), state); session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.living_flame")]);
            session.Step([Cast("skill.fire_lance", state.Actors[1].Id)]); Advance(session, 12);
            raised = session.View.Actors.Any(a => a.DefinitionId == "summon.fire_spirit");
            if (raised) Assert.Contains(state.Actors[1].Id, session.Capture().ConsumedCorpseIds);
        }
        Assert.True(raised);
        var killedState = Session().Capture(); var rusher = killedState.Actors.Single(a => a.Role == "Rusher");
        rusher.Position = new(-2600, 0); rusher.Health = 1;
        var killedSession = CombatSession.Restore(Content(), killedState); killedSession.Step([Cast("skill.cleave", rusher.Id)]);
        var events = Advance(killedSession, 6);
        Assert.Contains(events, e => e.Kind == "DeathExplosionArmed");
        Assert.DoesNotContain(events, e => e.Kind == "DamageApplied" && e.ContentId == "enemy.detonate");
        Assert.Contains(Advance(killedSession, 16), e => e.Kind == "DamageApplied" && e.ContentId == "enemy.detonate");
    }
    [Fact]
    public void LegacyPhaseTwoContentIdentityAndStateRemainReadableForMigration()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string oldJson = File.ReadAllText(Path.Combine(root, "fixtures/combat-phase2.json"));
        string fixture = File.ReadAllText(Path.Combine(root, "fixtures/phase2-expedition-hub.json"));
        var node = System.Text.Json.Nodes.JsonNode.Parse(fixture)!;
        var state = JsonData.Read<CombatSnapshot>(node["state"]!["combat"]!.ToJsonString());
        var session = CombatSession.Restore(oldJson, state);
        Assert.Equal("F378B5144C97B807236EB19246BDA871890AE8F3BA288E9BE77EE2D7ED2C5263", session.ContentHash);
        Assert.Equal(6, session.View.Skills.Count);
        Assert.Equal("Vanguard", session.View.Discipline);
    }
    [Fact]
    public void MutationLocksAndProgressionRefreshDoNotRefundResources()
    {
        var session = Session(); session.ApplyProgressionBuild(new(UltimateUnlocked: false, UnlockedMutations: []));
        Assert.Contains(session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.avalanche")]), e => e.Kind == "CommandRejected");
        Assert.False(session.View.Skills.Single(s => s.Id == "skill.cataclysm").Available);
        var state = session.Capture(); state.Momentum = 42; session = CombatSession.Restore(Content(), state);
        session.ApplyProgressionBuild(new(Offense: 2)); Assert.Equal(42, session.View.Resource);
        Assert.Throws<InvalidOperationException>(() => session.ApplyProgressionBuild(new(Discipline: "Arcanist")));
        Assert.Throws<InvalidDataException>(() => session.ApplyProgressionBuild(new(ForkCount: 1, ChainCount: 1)));
    }
}
