using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class LateLegendaryTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    private static string Content => Read("combat.json");
    private static CombatCommand Cast(string skill, int target = 0) => new(CombatCommandKind.Cast, SkillId: skill, TargetId: target);
    private static CombatProgressionBuild Build(string discipline = "Vanguard") => new(Discipline: discipline)
    { UnspokenVerdict = true, WitnessVow = true, BorrowedHour = true };
    private static CombatSession Session(CombatProgressionBuild? build = null)
    {
        var hub = CombatSession.CreateEncounter(Content, 42, "hub"); hub.ApplyProgressionBuild(build ?? Build());
        var state = CombatSession.CreateEncounter(Content, 42, "encounter.ossuary", hub.Capture()).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Actors[0].Position = new(-4500, 0); state.Momentum = 100;
        int index = 0;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy).ToArray())
            state.Actors[state.Actors.IndexOf(enemy)] = enemy with
            { Position = new(-2500 + index++ * 1500, 0), Health = 10000, MaxHealth = 10000, Resistance = 0, Armor = 0, RecoveryUntil = 2500, Hidden = false };
        return CombatSession.Restore(Content, state);
    }
    private static List<CombatEvent> Advance(CombatSession session, int ticks)
    { List<CombatEvent> events = []; for (int i = 0; i < ticks; i++) events.AddRange(session.Step()); return events; }
    private static List<CombatEvent> Attack(CombatSession session, string skill = "skill.cleave", int target = 0, int ticks = 13)
    {
        var events = session.Step([Cast(skill, target == 0 ? session.View.Actors[1].Id : target)]).ToList();
        events.AddRange(Advance(session, ticks)); return events;
    }

    [Theory]
    [InlineData(0, 40)]
    [InlineData(185, 15)]
    [InlineData(200, 0)]
    public void VerdictRewardsARealInterruptedWindupWithCappedBarrier(int initial, int granted)
    {
        var state = Session(new() { UnspokenVerdict = true }).Capture(); state.Actors[0].Barrier = initial;
        state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, 80, state.NextActionId++);
        var session = CombatSession.Restore(Content, state); var events = Attack(session, "skill.shield_breaker", ticks: 12);
        Assert.Null(session.Capture().Actors[1].Pending);
        Assert.Contains(events, e => e.Kind == "StatusApplied" && e.ContentId == "Staggered");
        Assert.Equal(granted, Assert.Single(events, e => e.Kind == "BarrierGranted" && e.ContentId == LegendaryEquipment.CrownPower).Amount);
        Assert.Equal(initial + granted, session.View.Barrier);
        Assert.Equal(100, session.Capture().Legendary!.VerdictReadyTick);
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("dead")]
    [InlineData("cooldown")]
    [InlineData("immune")]
    public void VerdictDoesNotRewardUnqualifiedCrowdControl(string reason)
    {
        var state = Session(new() { UnspokenVerdict = true }).Capture();
        if (reason != "idle") state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, 80, state.NextActionId++);
        if (reason == "dead") state.Actors[1].Health = 1;
        if (reason == "immune") state.Actors[1].InvulnerableUntil = 80;
        if (reason == "cooldown") state.Legendary = new() { VerdictReadyTick = 90 };
        var session = CombatSession.Restore(Content, state); var events = Attack(session, "skill.shield_breaker", ticks: 12);
        Assert.DoesNotContain(events, e => e.ContentId == LegendaryEquipment.CrownPower);
        Assert.Equal(0, session.View.Barrier);
    }

    [Fact]
    public void VerdictRespectedCrowdControlImmunityAndCooldown()
    {
        var state = Session(new() { UnspokenVerdict = true }).Capture();
        state.Actors[1] = state.Actors[1] with { DefinitionId = "enemy.bell_saint", Role = "BellSaint" };
        state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, 80, state.NextActionId++);
        var session = CombatSession.Restore(Content, state); var events = Attack(session, "skill.shield_breaker", ticks: 12);
        Assert.Contains(events, e => e.Kind == "StatusResisted" && e.ContentId == "Staggered");
        Assert.DoesNotContain(events, e => e.ContentId == LegendaryEquipment.CrownPower);
        state = Session(new() { UnspokenVerdict = true }).Capture();
        state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, 80, state.NextActionId++);
        session = CombatSession.Restore(Content, state); Attack(session, "skill.shield_breaker", ticks: 12);
        Advance(session, 33); state = session.Capture();
        state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, state.Tick + 80, state.NextActionId++);
        session = CombatSession.Restore(Content, state); events = Attack(session, "skill.shield_breaker", ticks: 12);
        Assert.Contains(events, e => e.Kind == "StatusApplied" && e.ContentId == "Staggered");
        Assert.DoesNotContain(events, e => e.ContentId == LegendaryEquipment.CrownPower);
        Advance(session, 43); state = session.Capture();
        state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, state.Tick + 80, state.NextActionId++);
        session = CombatSession.Restore(Content, state); events = Attack(session, "skill.shield_breaker", ticks: 12);
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.CrownPower);
        Assert.Equal(80, session.View.Barrier);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerdictRecognizesCancelledFutureWarningsButNotAlreadyResolvedWarnings(bool alreadyResolved)
    {
        var content = CampaignCombatContent.Parse(Content, Read("campaign-combat.json"));
        var hub = content.CreateEncounter("hub"); hub.ApplyProgressionBuild(new() { UnspokenVerdict = true });
        var state = content.CreateEncounter("campaign.road", previous: hub.Capture()).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Momentum = 100;
        var enemy = state.Actors.First(a => a.Faction == CombatFaction.Enemy);
        state.Actors[0].Position = new(-4500, 0); enemy.Position = new(-2500, 0);
        foreach (var actor in state.Actors.Where(a => a.Faction == CombatFaction.Enemy)) actor.RecoveryUntil = 2500;
        state.Campaign!.Hazards.Add(new(state.NextObjectId++, "Circle", enemy.Position, enemy.Position, 1200,
            alreadyResolved ? 0 : 80, "campaign.chain", enemy.Id, 0, DamageFamily.Storm, "", state.NextActionId++));
        var session = CombatSession.Restore(content.CombatJson, state);
        var events = Attack(session, "skill.shield_breaker", enemy.Id, 12);
        Assert.DoesNotContain(session.Capture().Campaign!.Hazards, h => h.SourceId == enemy.Id);
        Assert.Equal(alreadyResolved ? 0 : 1, events.Count(e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.CrownPower));
    }

    [Fact]
    public void WitnessRequiresFourDistinctSuccessfulActionsAndHasNoWeaponBonusOrCritical()
    {
        var session = Session(new(FlatDamage: 100, CriticalBasisPoints: 7500) { WitnessVow = true });
        for (int n = 1; n <= 3; n++)
        {
            var events = Attack(session);
            Assert.Equal(n, session.View.Legendary!.WitnessStacks);
            Assert.DoesNotContain(events, e => e.ContentId == "effect.witness_vow");
        }
        var fourth = Attack(session);
        Assert.Equal(24, Assert.Single(fourth, e => e.Kind == "DamageApplied" && e.ContentId == "effect.witness_vow").Amount);
        Assert.DoesNotContain(fourth, e => e.Kind == "CriticalHit" && e.ContentId == "effect.witness_vow");
        Assert.Single(fourth, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.WitnessPower);
        Assert.Equal(0, session.View.Legendary!.WitnessStacks);
        Assert.Equal(0, session.View.Legendary.WitnessTargetId);
    }

    [Fact]
    public void WitnessCountsAnAreaOnlyOnceAcrossTargetsAndRepeatedPulses()
    {
        var state = Session(new() { WitnessVow = true }).Capture();
        state.Actors[2].Position = new(-4500, 1700);
        var session = CombatSession.Restore(Content, state); var events = Attack(session, "skill.cataclysm", ticks: 80);
        Assert.True(events.Count(e => e.Kind == "DamageApplied" && e.ContentId == "skill.cataclysm") > 2);
        Assert.Single(events, e => e.Kind == "LegendaryCharged" && e.ContentId == LegendaryEquipment.WitnessPower);
        Assert.DoesNotContain(events, e => e.ContentId == "effect.witness_vow");
        Assert.Equal(1, session.View.Legendary!.WitnessStacks);
    }

    [Fact]
    public void WitnessSwitchDeathAndExpiryResetProgressAndImmuneHitsDoNotCharge()
    {
        var state = Session(new() { WitnessVow = true }).Capture(); state.Actors[2].Position = new(-4500, 1800);
        var session = CombatSession.Restore(Content, state);
        Attack(session); Attack(session); Assert.Equal(2, session.View.Legendary!.WitnessStacks);
        Attack(session, target: session.View.Actors[2].Id);
        Assert.Equal(1, session.View.Legendary!.WitnessStacks); Assert.Equal(session.View.Actors[2].Id, session.View.Legendary.WitnessTargetId);
        Advance(session, 120); Assert.Equal(0, session.View.Legendary!.WitnessStacks);
        state = session.Capture(); state.Actors[1].InvulnerableUntil = state.Tick + 80;
        session = CombatSession.Restore(Content, state); Attack(session); Assert.Equal(0, session.View.Legendary!.WitnessStacks);
        state = session.Capture(); state.Actors[1].InvulnerableUntil = 0; state.Actors[1].Health = 1;
        session = CombatSession.Restore(Content, state); Attack(session); Assert.Equal(0, session.View.Legendary!.WitnessStacks);
    }

    [Theory]
    [InlineData("Vanguard", "skill.cataclysm")]
    [InlineData("Veilwalker", "skill.shadow_execution")]
    [InlineData("Arcanist", "skill.starfall")]
    [InlineData("Gravecaller", "skill.procession")]
    [InlineData("Warden", "skill.primal_awakening")]
    public void EveryUnlockedUltimateReadiesThreeChargesWithoutReducingItsCooldown(string discipline, string ultimate)
    {
        var state = Session(new(Discipline: discipline) { BorrowedHour = true }).Capture();
        if (discipline == "Gravecaller")
        { state.Actors[2].Health = 0; state.Actors[2].DeathProcessed = true; state.Actors[2].Position = new(-4000, 1000); }
        var session = CombatSession.Restore(Content, state); var events = session.Step([Cast(ultimate, state.Actors[1].Id)]);
        Assert.Single(events, e => e.Kind == "LegendaryReadied" && e.ContentId == LegendaryEquipment.HourPower && e.Amount == 3);
        Assert.Equal(3, session.View.Legendary!.HourCharges); Assert.Equal(239, session.View.Legendary.HourRemainingTicks);
        Assert.Equal(CombatContent.Parse(Content).Skills.Single(s => s.Id == ultimate).Cooldown, session.Capture().Cooldowns[ultimate]);
    }

    [Fact]
    public void BorrowedHourConsumesOnAcceptanceHalvesThreeCooldownsAndDoesNotRefundResources()
    {
        var session = Session(new() { BorrowedHour = true }); session.Step([Cast("skill.cataclysm")]); Advance(session, 29);
        long tick = session.Tick; int resource = session.View.Resource;
        var events = session.Step([Cast("skill.shield_breaker", session.View.Actors[1].Id)]);
        Assert.Equal(tick + 23, session.Capture().Cooldowns["skill.shield_breaker"]);
        Assert.Equal(resource - 20, session.View.Resource); Assert.Equal(2, session.View.Legendary!.HourCharges);
        Assert.Contains(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.HourPower && e.Amount == 2);
        session.Step([Cast("skill.shield_breaker", session.View.Actors[1].Id)]); Assert.Equal(2, session.View.Legendary.HourCharges);
        // Cancel the accepted windup: the cooldown and spent charge remain authoritative.
        session.Step([new(CombatCommandKind.Dodge, X: 1, Z: 0)]); Assert.Equal(2, session.View.Legendary.HourCharges);
        Advance(session, 6);
        tick = session.Tick; session.Step([Cast("skill.cleave", session.View.Actors[1].Id)]);
        Assert.Equal(tick + 6, session.Capture().Cooldowns["skill.cleave"]); Assert.Equal(1, session.View.Legendary.HourCharges);
        Advance(session, 10); tick = session.Tick;
        session.Step([Cast("skill.cleave", session.View.Actors[1].Id)]);
        Assert.Equal(tick + 6, session.Capture().Cooldowns["skill.cleave"]); Assert.Equal(0, session.View.Legendary.HourCharges);
        Advance(session, 12); tick = session.Tick; session.Step([Cast("skill.cleave", session.View.Actors[1].Id)]);
        Assert.Equal(tick + 12, session.Capture().Cooldowns["skill.cleave"]);
    }

    [Fact]
    public void LockedUltimateCannotPrepareAndExpiredOrUnequippedChargesCannotPersist()
    {
        var session = Session(new(UltimateUnlocked: false) { BorrowedHour = true });
        Assert.Contains(session.Step([Cast("skill.cataclysm")]), e => e.Kind == "CommandRejected"); Assert.Equal(0, session.View.Legendary!.HourCharges);
        session = Session(new() { BorrowedHour = true }); session.Step([Cast("skill.cataclysm")]); Advance(session, 240);
        Assert.Equal(0, session.View.Legendary!.HourCharges);
        session = Session(); session.Step([Cast("skill.cataclysm")]); session.ApplyProgressionBuild(new()); Assert.Null(session.Capture().Legendary);
    }

    [Fact]
    public void ActivePowersAndReceiptsSurviveSaveAndDeterministicReplay()
    {
        var session = Session(); session.Step([Cast("skill.cataclysm")]); Advance(session, 20);
        Assert.NotEmpty(session.Capture().Legendary!.WitnessActions!);
        var saved = JsonData.Write(new CombatSave(1, session.StateHash, session.Capture()));
        var restored = CombatSession.Restore(Content, CombatSaveStore.Read(saved, Content));
        var recorder = new CombatRecorder(session);
        for (int i = 0; i < 180; i++)
        {
            CombatCommand[] commands = i % 18 == 0 ? [Cast("skill.cleave", session.View.Actors[1].Id)] : [];
            Assert.Equal(JsonData.Hash(restored.Step(commands)), JsonData.Hash(recorder.Step(session, commands)));
            Assert.Equal(session.StateHash, restored.StateHash);
        }
        Assert.True(CombatReplayRunner.Run(Content, JsonData.Read<CombatReplay>(JsonData.Write(recorder.Capture()))).Success);
    }

    [Theory]
    [InlineData("unequip")]
    [InlineData("death")]
    [InlineData("encounter")]
    public void LatePreparedStateClearsOnRemovalDeathAndRoomTransition(string reason)
    {
        var state = Session().Capture();
        state.Legendary = new()
        {
            VerdictReadyTick = 90,
            WitnessStacks = 2,
            WitnessTargetId = state.Actors[1].Id,
            WitnessUntil = 120,
            WitnessActions = [state.NextActionId++],
            HourCharges = 3,
            HourUntil = 240
        };
        var session = CombatSession.Restore(Content, state);
        if (reason == "unequip") session.ApplyProgressionBuild(new());
        else if (reason == "encounter") session = CombatSession.CreateEncounter(Content, 42, "hub", session.Capture());
        else
        {
            state.Actors[0].Health = 1; state.Actors[0].InvulnerableUntil = 0;
            state.Actors[1].Position = new(-3900, 0);
            state.Actors[1].Pending = new("enemy.strike", 1, state.Actors[0].Position, 0, state.NextActionId++);
            session = CombatSession.Restore(Content, state); session.Step(); Assert.Equal(0, session.View.Actors[0].Health);
        }
        Assert.Null(session.Capture().Legendary);
    }

    [Fact]
    public void AreaReceiptSurvivesSaveAndTargetSwitchUntilTheAreaFinishes()
    {
        var state = Session(new() { WitnessVow = true }).Capture();
        state.Actors[2].Position = new(-4500, 1800);
        var session = CombatSession.Restore(Content, state); session.Step([Cast("skill.cataclysm")]); Advance(session, 28);
        long areaAction = Assert.Single(session.Capture().Areas).ActionId;
        Assert.Contains(areaAction, session.Capture().Legendary!.WitnessActions!);
        session = CombatSession.Restore(Content, session.Capture());
        var events = Attack(session, target: state.Actors[2].Id, ticks: 6);
        Assert.Single(events, e => e.Kind == "LegendaryCharged" && e.ContentId == LegendaryEquipment.WitnessPower);
        Assert.Equal(state.Actors[2].Id, session.View.Legendary!.WitnessTargetId);
        events = Advance(session, 23);
        Assert.DoesNotContain(events, e => e.Kind == "LegendaryCharged" && e.ContentId == LegendaryEquipment.WitnessPower);
        Assert.Equal(1, session.View.Legendary!.WitnessStacks);
        Assert.Equal(state.Actors[2].Id, session.View.Legendary.WitnessTargetId);
        Advance(session, 25); Assert.Null(session.Capture().Legendary!.WitnessActions);
    }

    [Theory]
    [InlineData("verdict-negative")]
    [InlineData("verdict-future")]
    [InlineData("witness-count")]
    [InlineData("witness-target")]
    [InlineData("witness-expiry")]
    [InlineData("receipt-duplicate")]
    [InlineData("receipt-future")]
    [InlineData("hour-count")]
    [InlineData("hour-expiry")]
    [InlineData("unequipped")]
    public void InvalidLateLegendaryStateIsRejected(string kind)
    {
        var state = Session().Capture(); state.Legendary = new();
        switch (kind)
        {
            case "verdict-negative": state.Legendary.VerdictReadyTick = -1; break;
            case "verdict-future": state.Legendary.VerdictReadyTick = 91; break;
            case "witness-count": state.Legendary.WitnessStacks = 4; break;
            case "witness-target": state.Legendary.WitnessStacks = 1; state.Legendary.WitnessUntil = 120; state.Legendary.WitnessTargetId = 1; break;
            case "witness-expiry": state.Legendary.WitnessStacks = 1; state.Legendary.WitnessTargetId = state.Actors[1].Id; break;
            case "receipt-duplicate": state.Legendary.WitnessActions = [state.NextActionId++, state.NextActionId - 1]; break;
            case "receipt-future": state.Legendary.WitnessActions = [state.NextActionId]; break;
            case "hour-count": state.Legendary.HourCharges = 4; state.Legendary.HourUntil = 240; break;
            case "hour-expiry": state.Legendary.HourCharges = 1; state.Legendary.HourUntil = 241; break;
            default: state.ProgressionBuild = new(); state.Legendary.HourCharges = 1; state.Legendary.HourUntil = 240; break;
        }
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, state));
    }

    [Fact]
    public void DefaultsDoNotChangeLegacySerializedBuildsOrStates()
    {
        string build = JsonData.Write(new CombatProgressionBuild()); string state = JsonData.Write(new LegendaryCombatState());
        foreach (string name in new[] { "unspokenVerdict", "witnessVow", "borrowedHour" }) Assert.DoesNotContain(name, build);
        foreach (string name in new[] { "verdictReadyTick", "witnessStacks", "witnessTargetId", "witnessUntil", "witnessActions", "hourCharges", "hourUntil" }) Assert.DoesNotContain(name, state);
    }

    [Fact]
    public void ReceiptListPreservesThePreviousOrderedArrayJsonShape()
    {
        const string legacy = "{\"oathCharge\":0,\"oathUntil\":0,\"widowUntil\":0,\"witnessActions\":[17,4,11]}";
        var state = JsonData.Read<LegendaryCombatState>(legacy);
        Assert.Equal(new long[] { 17, 4, 11 }, state.WitnessActions);
        Assert.Equal(legacy, JsonData.Write(state));
        Assert.Equal(JsonData.Hash(state), JsonData.Hash(JsonData.Copy(state)));
    }

    [Fact]
    public void ReceiptCompactionRetainsActionOrderAndSerializedStateAcrossRestore()
    {
        var state = Session(new() { WitnessVow = true }).Capture();
        long expired = state.NextActionId++, first = state.NextActionId++, second = state.NextActionId++, otherExpired = state.NextActionId++;
        state.Legendary = new() { WitnessActions = [expired, second, otherExpired, first] };
        foreach (long action in new[] { first, second })
            state.Areas.Add(new(state.NextObjectId++, 1, 1, state.Actors[0].Position, 500, "skill.cataclysm", 1,
                DamageFamily.Fire, 100, 120, action, 0));
        var session = CombatSession.Restore(Content, state);
        session.ApplyProgressionBuild(new() { WitnessVow = true });
        Assert.Equal(new[] { second, first }, session.Capture().Legendary!.WitnessActions);
        var restored = CombatSession.Restore(Content, session.Capture());
        session.Step(); restored.Step();
        Assert.Equal(session.StateHash, restored.StateHash);
        Assert.Equal(new[] { second, first }, restored.Capture().Legendary!.WitnessActions);
    }

    [Theory]
    [InlineData("act.shattered_spine", 1, LegendaryEquipment.Crown)]
    [InlineData("act.hollow_night", 1, LegendaryEquipment.Witness)]
    [InlineData("act.hollow_night", 3, LegendaryEquipment.Hour)]
    public void NamedFractureRewardsDropOnceAtThePublishedRoom(string region, int index, string item)
    {
        var content = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Content, Read("campaign-combat.json")).CombatJson,
            Read("endgame-combat.json"), EndgameContent.Parse(Read("endgame.json")));
        var manifest = content.CreateFractureManifest(new(1, 42, region, 1, ["fracture.burning_haste"], "Vael", "Materials"), 1);
        var state = content.CreateEncounter(manifest, index, 0, CombatSession.CreateEncounter(content.CombatJson, 42, "hub").Capture()).Capture();
        if (manifest.Rooms[index].Boss) state.Campaign!.BossPhase = 2;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
        { enemy.Health = 1; enemy.Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 0, ExpiresTick = 90 }); }
        var session = CombatSession.Restore(content.CombatJson, state); session.Step();
        Assert.Equal("Legendary", Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item).Item.Rarity);
        Assert.Single(session.View.Loot, l => LegendaryEquipment.IsItem(l.Item.DefinitionId));
        Advance(session, 2); Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item);
    }
}
