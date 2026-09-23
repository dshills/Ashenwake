using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class SecretLegendaryTests
{
    private static string Content => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static CombatSession Session(CombatProgressionBuild build)
    {
        var hub = CombatSession.CreateEncounter(Content, 42, "hub"); hub.ApplyProgressionBuild(build);
        var state = CombatSession.CreateEncounter(Content, 42, "encounter.ossuary", hub.Capture()).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Actors[0].Position = new(-4500, 0); state.Momentum = 100;
        int index = 0;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy).ToArray())
            state.Actors[state.Actors.IndexOf(enemy)] = enemy with
            { Position = new(-2500 + index++ * 800, 0), Health = 10000, MaxHealth = 10000, Resistance = 0, Armor = 0, RecoveryUntil = 2500, Hidden = false };
        return CombatSession.Restore(Content, state);
    }
    private static List<CombatEvent> Advance(CombatSession session, int ticks)
    { List<CombatEvent> events = []; for (int i = 0; i < ticks; i++) events.AddRange(session.Step()); return events; }
    private static List<CombatEvent> Attack(CombatSession session, string skill = "skill.shield_breaker")
    {
        var events = session.Step([new CombatCommand(CombatCommandKind.Cast, SkillId: skill, TargetId: session.View.Actors[1].Id)]).ToList();
        events.AddRange(Advance(session, 12)); return events;
    }
    [Theory]
    [InlineData(50, 20)]
    [InlineData(345, 5)]
    [InlineData(350, 0)]
    public void GriefHealsActualInterruptedCastWithoutOverheal(int health, int healed)
    {
        var state = Session(new() { GriefsReprieve = true }).Capture(); state.Actors[0].Health = health;
        state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, 80, state.NextActionId++);
        var session = CombatSession.Restore(Content, state); var events = Attack(session);
        Assert.Null(session.Capture().Actors[1].Pending);
        Assert.Equal(healed, Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.GriefPower).Amount);
        Assert.Equal(health + healed, session.View.Actors[0].Health);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
    }
    [Theory]
    [InlineData("idle")]
    [InlineData("dead")]
    [InlineData("immune")]
    [InlineData("cooldown")]
    [InlineData("control-immune")]
    public void GriefRejectsUnqualifiedInterrupts(string reason)
    {
        var state = Session(new() { GriefsReprieve = true }).Capture(); state.Actors[0].Health = 50;
        if (reason != "idle") state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, 80, state.NextActionId++);
        if (reason == "dead") state.Actors[1].Health = 1;
        if (reason == "immune") state.Actors[1].InvulnerableUntil = 80;
        if (reason == "cooldown") state.Legendary = new() { GriefReadyTick = 90 };
        if (reason == "control-immune") state.Actors[1] = state.Actors[1] with { DefinitionId = "enemy.bell_saint", Role = "BellSaint" };
        var session = CombatSession.Restore(Content, state);
        Assert.DoesNotContain(Attack(session), e => e.ContentId == LegendaryEquipment.GriefPower);
        Assert.Equal(50, session.View.Actors[0].Health);
    }
    private static CombatSnapshot PoisonKill()
    {
        var state = Session(new(Discipline: "Veilwalker") { Widowthorn = true }).Capture();
        state.Actors[1].Health = 1;
        state.Actors[1].Statuses.Add(new() { Id = "Poisoned", SourceId = 1, OwnerId = 1, OriginSkill = "skill.venom_knife", ActionId = state.NextActionId++, NextTick = 0, ExpiresTick = 90 });
        return state;
    }
    [Fact]
    public void WidowthornRootsNearbySurvivorsWithoutDamageAndRoundTrips()
    {
        var session = CombatSession.Restore(Content, PoisonKill()); var recorder = new CombatRecorder(session); var events = recorder.Step(session, []);
        var roots = session.Capture().Actors.SelectMany(a => a.Statuses).Where(s => s.OriginSkill == "effect.widowthorn").ToArray();
        Assert.InRange(roots.Length, 1, 3);
        Assert.All(roots, s => { Assert.Equal("Rooted", s.Id); Assert.Equal(45, s.ExpiresTick); Assert.Equal(1, s.OwnerId); Assert.Equal(1, s.Depth); });
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.WidowthornPower);
        Assert.DoesNotContain(events, e => e.Kind == "DamageApplied" && e.ContentId == "effect.widowthorn");
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
        Assert.True(CombatReplayRunner.Run(Content, recorder.Capture()).Success);
        session.ApplyProgressionBuild(new(Discipline: "Veilwalker"));
        Assert.DoesNotContain(session.Capture().Actors.SelectMany(a => a.Statuses), s => s.OriginSkill == "effect.widowthorn");
    }
    [Theory]
    [InlineData("not-poison")]
    [InlineData("not-owned")]
    [InlineData("cooldown")]
    [InlineData("immune-targets")]
    [InlineData("out-of-range")]
    public void WidowthornRejectsWrongKillOrUnavailableTargets(string reason)
    {
        var state = PoisonKill();
        if (reason == "not-poison") state.Actors[1].Statuses[0].Id = "Burning";
        if (reason == "not-owned") { state.Actors[1].Statuses[0].SourceId = state.Actors[2].Id; state.Actors[1].Statuses[0].OwnerId = state.Actors[2].Id; state.Actors[1].Statuses[0].OriginSkill = ""; }
        if (reason == "cooldown") state.Legendary = new() { WidowthornReadyTick = 90 };
        foreach (var target in state.Actors.Skip(2))
        {
            if (reason == "immune-targets") target.InvulnerableUntil = 90;
            if (reason == "out-of-range") target.Position = new(7000, 0);
        }
        var session = CombatSession.Restore(Content, state);
        Assert.DoesNotContain(session.Step(), e => e.ContentId == LegendaryEquipment.WidowthornPower);
    }
    private static CombatSnapshot FireAttack(bool equipped = true, DamageFamily family = DamageFamily.Fire)
    {
        var state = Session(new() { Emberwake = equipped }).Capture(); int source = state.Actors[1].Id;
        state.Areas.Add(new(state.NextObjectId++, source, source, new(-4500, 0), 5000, "enemy.detonate", 30, family, 0, 1, state.NextActionId++, 0));
        return state;
    }
    [Fact]
    public void EmberwakeRewardsActualFireDodgeAndOnlyEmpowersDirectSkills()
    {
        var session = CombatSession.Restore(Content, FireAttack()); int health = session.View.Actors[0].Health;
        var recorder = new CombatRecorder(session); var events = recorder.Step(session, [new CombatCommand(CombatCommandKind.Dodge, Z: 1)]);
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.EmberwakePower);
        Assert.Equal(health, session.View.Actors[0].Health); Assert.Equal(119, session.View.Legendary!.EmberwakeRemainingTicks);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
        Assert.True(CombatReplayRunner.Run(Content, recorder.Capture()).Success);
        var active = session.Capture(); active.Actors[0].Position = new(-4500, 0);
        var inactive = JsonData.Copy(active); inactive.Legendary!.EmberwakeUntil = 0;
        var boosted = CombatSession.Restore(Content, active); var baseline = CombatSession.Restore(Content, inactive);
        Advance(boosted, 6); Advance(baseline, 6);
        int Damage(CombatSession s) => Attack(s, "skill.cleave").Where(e => e.Kind == "DamageApplied" && e.ContentId == "skill.cleave").Sum(e => e.Amount);
        Assert.True(Damage(boosted) > Damage(baseline));
        boosted.ApplyProgressionBuild(new()); Assert.False(boosted.View.Legendary?.EmberwakeEquipped ?? false);
    }
    [Theory]
    [InlineData("ordinary-hit")]
    [InlineData("other-immunity")]
    [InlineData("physical")]
    [InlineData("unequipped")]
    [InlineData("cooldown")]
    [InlineData("burning")]
    public void EmberwakeNeedsFireAvoidedSpecificallyByDodge(string reason)
    {
        var state = FireAttack(reason != "unequipped", reason == "physical" ? DamageFamily.PhysicalCrush : DamageFamily.Fire);
        if (reason == "other-immunity") state.Actors[0].InvulnerableUntil = 7;
        if (reason == "cooldown") state.Legendary = new() { EmberwakeReadyTick = 90 };
        if (reason == "burning")
        {
            state.Areas.Clear(); int enemy = state.Actors[1].Id;
            state.Actors[0].Statuses.Add(new() { Id = "Burning", SourceId = enemy, OwnerId = enemy, NextTick = 0, ExpiresTick = 90, ActionId = state.NextActionId++ });
        }
        var session = CombatSession.Restore(Content, state);
        var events = reason is "ordinary-hit" or "other-immunity" ? session.Step() : session.Step([new CombatCommand(CombatCommandKind.Dodge, Z: 1)]);
        Assert.DoesNotContain(events, e => e.ContentId == LegendaryEquipment.EmberwakePower);
    }
    [Fact]
    public void EmberwakeDoesNotAmplifyPoisonAndExpiresWithoutLeavingPreparedState()
    {
        var active = Session(new() { Emberwake = true }).Capture(); active.Legendary = new() { EmberwakeUntil = 120, EmberwakeReadyTick = 90 };
        active.Actors[1].Statuses.Add(new() { Id = "Poisoned", SourceId = 1, OwnerId = 1, OriginSkill = "skill.venom_knife", ActionId = active.NextActionId++, NextTick = 0, ExpiresTick = 90 });
        var inactive = JsonData.Copy(active); inactive.Legendary = null;
        var boosted = CombatSession.Restore(Content, active); var baseline = CombatSession.Restore(Content, inactive);
        int DotDamage(CombatSession session) => session.Step().Where(e => e.Kind == "DamageApplied" && e.ContentId == "Poisoned").Sum(e => e.Amount);
        int ordinary = DotDamage(baseline); Assert.True(ordinary > 0); Assert.Equal(ordinary, DotDamage(boosted));
        Advance(boosted, 120); Assert.Null(boosted.Capture().Legendary);
        Assert.Equal(boosted.StateHash, CombatSession.Restore(Content, boosted.Capture()).StateHash);
    }
    [Theory]
    [InlineData("death")]
    [InlineData("new-room")]
    public void PreparedSecretPowersClearOnDeathAndRoomChange(string reason)
    {
        var state = FireAttack(); state.ProgressionBuild = new() { GriefsReprieve = true, Widowthorn = true, Emberwake = true };
        state.Legendary = new() { GriefReadyTick = 90, WidowthornReadyTick = 90, EmberwakeUntil = 120 };
        var area = state.Areas[0]; state.Areas[0] = area with { Damage = 1000 };
        var session = CombatSession.Restore(Content, state);
        if (reason == "death") { session.Step(); Assert.Equal(0, session.View.Actors[0].Health); }
        else session = CombatSession.CreateEncounter(Content, 42, "hub", session.Capture(), restoreAtAnchor: true);
        Assert.Null(session.Capture().Legendary);
        Assert.Equal(session.StateHash, CombatSession.Restore(Content, session.Capture()).StateHash);
    }
    [Theory]
    [InlineData("ownership")]
    [InlineData("timer")]
    [InlineData("dodge")]
    public void RestoreRejectsForgedPowerState(string reason)
    {
        var state = Session(new() { GriefsReprieve = true, Widowthorn = true, Emberwake = true }).Capture();
        state.Legendary = new() { GriefReadyTick = 90, WidowthornReadyTick = 90, EmberwakeUntil = 120 };
        if (reason == "ownership") state = state with { ProgressionBuild = new() };
        if (reason == "timer") state.Legendary.GriefReadyTick = 91;
        if (reason == "dodge") state.Legendary.EmberwakeDodgeUntil = 7;
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, state));
    }
    [Fact]
    public void InactivePowerFieldsPreservePublishedShapeAndSecretItemsNeverHaveEncounterRewards()
    {
        string build = JsonData.Write(new CombatProgressionBuild()); string state = JsonData.Write(new LegendaryCombatState());
        foreach (string field in new[] { "griefsReprieve", "widowthorn", "emberwake" }) Assert.DoesNotContain(field, build);
        foreach (string field in new[] { "griefReadyTick", "widowthornReadyTick", "emberwakeUntil", "emberwakeDodgeUntil", "emberwakeReadyTick" }) Assert.DoesNotContain(field, state);
        foreach (string item in new[] { LegendaryEquipment.Grief, LegendaryEquipment.Widowthorn, LegendaryEquipment.Emberwake }) Assert.True(LegendaryEquipment.IsItem(item));
        Assert.Equal("", LegendaryEquipment.EncounterReward("secretarena.belfry"));
    }
}
