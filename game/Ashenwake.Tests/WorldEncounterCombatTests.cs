using Ashenwake.Core.Combat;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class WorldEncounterCombatTests
{
    private static readonly Lazy<string> Content = new(() => CampaignCombatContent.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json")),
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "campaign-combat.json"))).CombatJson);
    private static CombatSession Arena(string id) => CombatSession.CreateEncounter(Content.Value, 17, "worldarena." + id);
    private static CombatEvent[] Advance(CombatSession session, int ticks)
    { List<CombatEvent> events = []; for (int i = 0; i < ticks; i++) events.AddRange(session.Step()); return events.ToArray(); }

    [Theory]
    [InlineData("lantern", 3)]
    [InlineData("caravan_challenge", 3)]
    [InlineData("shrine", 2)]
    [InlineData("storm_grey_march", 3)]
    [InlineData("storm_verdant", 3)]
    [InlineData("storm_cinder", 4)]
    [InlineData("storm_spine", 3)]
    [InlineData("storm_hollow", 3)]
    public void OptionalBattlesHaveAuthoredPacksAndRestoreDeterministically(string id, int enemies)
    {
        var session = Arena(id);
        Assert.Equal(enemies, session.View.Actors.Count(a => a.Faction == CombatFaction.Enemy));
        Assert.Equal(10500, session.Room.HalfWidth);
        var replay = new CombatRecorder(session);
        for (int i = 0; i < 65; i++) replay.Step(session, []);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content.Value, session.Capture()).StateHash);
        Assert.True(CombatReplayRunner.Run(Content.Value, replay.Capture()).Success);
        Assert.Equal(id.StartsWith("storm_", StringComparison.Ordinal), session.View.ResonanceStormActive);
        Assert.Equal(id == "shrine", session.View.ShrineDamagePenalty);
    }

    [Theory]
    [InlineData("storm_grey_march", "rule.soniclanes", "Stormbound")]
    [InlineData("storm_verdant", "rule.poison_bloom", "Devourer")]
    [InlineData("storm_cinder", "rule.conveyor", "Stormbound")]
    [InlineData("storm_spine", "rule.fault.1", "Dirgebound")]
    [InlineData("storm_hollow", "rule.causalechoes", "Riftborn")]
    public void StormsCombineVisibleRegionalHazardsWithAlteredEnemyBehavior(string id, string hazard, string modifier)
    {
        var session = Arena(id);
        Assert.Contains(session.View.Actors, a => a.EliteModifiers?.Contains(modifier) == true);
        Advance(session, 46);
        var warning = Assert.Single(session.View.CampaignHazards!, h => h.ContentId == hazard);
        Assert.InRange(warning.RemainingTicks, 29, 42);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content.Value, session.Capture()).StateHash);
        var events = Advance(session, 45);
        Assert.Contains(events, e => e.Kind == "CampaignHazardResolved" && e.ContentId == hazard);
    }

    [Fact]
    public void ShrineAddsOneQuarterIncomingDamageBeforeBarrierAndRestoresTheNormalRuleOutside()
    {
        var state = Arena("shrine").Capture();
        state.Fragments.Clear(); state.Equipment.Clear();
        state.Actors[0] = state.Actors[0] with { Resistance = 0, Barrier = 10 };
        state.Campaign!.Hazards.Add(new(state.NextObjectId++, "Circle", state.Actors[0].Position, state.Actors[0].Position,
            1000, state.Tick, "rule.soniclanes", state.Actors[1].Id, 40, DamageFamily.Storm, "", state.NextActionId++));
        var shrine = CombatSession.Restore(Content.Value, state);
        var ordinary = CombatSession.Restore(Content.Value, state with { EncounterId = "worldarena.lantern" });
        Assert.Equal(40, shrine.Step().Single(e => e.Kind == "DamageApplied" && e.ContentId == "rule.soniclanes").Amount);
        Assert.Equal(30, ordinary.Step().Single(e => e.Kind == "DamageApplied" && e.ContentId == "rule.soniclanes").Amount);
        Assert.Equal(0, shrine.View.Barrier);
        Assert.False(ordinary.View.ShrineDamagePenalty);
        Assert.False(CombatSession.CreateEncounter(Content.Value, 17, "worldarena.foyer", shrine.Capture()).View.ShrineDamagePenalty);
    }

    [Theory]
    [InlineData("fragment.eye_vael", 15)]
    [InlineData("", 12)]
    public void StormChargeOnlyAmplifiesOwnedFragmentEffects(string fragment, int expected)
    {
        var state = Arena("storm_grey_march").Capture();
        state.Fragments.Clear(); state.Equipment.Clear();
        state.Actors[1] = state.Actors[1] with { Resistance = 0 };
        state.Actors[1].Statuses.Add(new()
        {
            Id = "Burning",
            SourceId = 1,
            OwnerId = 1,
            FragmentId = fragment,
            Stacks = 2,
            ActionId = state.NextActionId++,
            NextTick = state.Tick,
            ExpiresTick = state.Tick + 60
        });
        var storm = CombatSession.Restore(Content.Value, state);
        Assert.Equal(expected, storm.Step().Single(e => e.Kind == "DamageApplied" && e.ContentId == "Burning").Amount);
        var normal = CombatSession.Restore(Content.Value, state with { EncounterId = "worldarena.lantern" });
        Assert.Equal(12, normal.Step().Single(e => e.Kind == "DamageApplied" && e.ContentId == "Burning").Amount);
    }

    [Theory]
    [InlineData("lantern")]
    [InlineData("caravan_challenge")]
    [InlineData("shrine")]
    [InlineData("storm_grey_march")]
    [InlineData("storm_verdant")]
    [InlineData("storm_cinder")]
    [InlineData("storm_spine")]
    [InlineData("storm_hollow")]
    public void KillingTheLastCaptorsHasNoRandomLootAndEndsTemporaryRules(string id)
    {
        var state = Arena(id).Capture();
        state.Fragments.Clear(); state.Equipment.Clear();
        foreach (var actor in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
        {
            actor.Health = 1;
            actor.Statuses.Add(new()
            {
                Id = "Burning",
                SourceId = 1,
                OwnerId = 1,
                ActionId = state.NextActionId++,
                NextTick = state.Tick,
                ExpiresTick = state.Tick + 60
            });
        }
        var session = CombatSession.Restore(Content.Value, state);
        var events = session.Step();
        Assert.All(session.View.Actors.Where(a => a.Faction == CombatFaction.Enemy), a => Assert.Equal(0, a.Health));
        Assert.Empty(session.View.Loot);
        Assert.DoesNotContain(events, e => e.Kind == "LootDropped");
        Assert.Equal(state.Rng.Loot, session.Capture().Rng.Loot);
        Assert.False(session.View.ResonanceStormActive);
        Assert.False(session.View.ShrineDamagePenalty);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content.Value, session.Capture()).StateHash);
    }

    [Theory]
    [InlineData("foyer")]
    [InlineData("caravan")]
    public void PeacefulTableauxNeverGenerateEnemiesHazardsOrCombatPenalties(string id)
    {
        var session = Arena(id);
        Assert.DoesNotContain(Advance(session, 240), e => e.Kind is "DamageApplied" or "CampaignHazardWarned");
        Assert.Single(session.View.Actors);
        Assert.Empty(session.View.CampaignHazards!);
        Assert.False(session.View.ResonanceStormActive);
        Assert.False(session.View.ShrineDamagePenalty);
    }
}
