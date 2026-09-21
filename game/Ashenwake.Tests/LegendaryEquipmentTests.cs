using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class LegendaryEquipmentTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    private static string Content => Read("combat.json");
    private static CombatCommand Dodge => new(CombatCommandKind.Dodge, Z: 1);
    private static CombatCommand Cast(string skill, int target) => new(CombatCommandKind.Cast, SkillId: skill, TargetId: target);

    [Fact]
    public void CatalogWithOnlyExclusiveRewardsIsRejectedBeforeEnemyDeath()
    {
        var content = CombatContent.Parse(Content);
        var exclusive = content with { Items = content.Items.Where(i => LegendaryEquipment.IsItem(i.Id)).ToArray(), Loadouts = [] };
        Assert.Throws<InvalidDataException>(() => CombatContent.Parse(JsonData.Write(exclusive)));
    }

    private static CombatSession Session(CombatProgressionBuild build)
    {
        var hub = CombatSession.CreateEncounter(Content, 42, "hub"); hub.ApplyProgressionBuild(build);
        var state = CombatSession.CreateEncounter(Content, 42, "encounter.ossuary", hub.Capture()).Capture();
        state.Fragments.Clear(); state.Equipment.Clear(); state.Actors[0].Position = new(-4500, 0);
        int index = 0;
        foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
        {
            enemy.Position = new(-2600 + 1600 * index++, 0); enemy.RecoveryUntil = 1500; enemy.Hidden = false;
        }
        return CombatSession.Restore(Content, state);
    }
    private static List<CombatEvent> Advance(CombatSession session, int count)
    {
        List<CombatEvent> events = [];
        for (int i = 0; i < count; i++) events.AddRange(session.Step());
        return events;
    }
    private static CombatSession EnemyStrike(CombatSession session, int barrier, int health = 350)
    {
        var state = session.Capture(); state.Actors[0].Barrier = barrier; state.Actors[0].Health = health; state.Actors[0].InvulnerableUntil = state.Tick;
        var enemy = state.Actors[1]; enemy.Position = new(state.Actors[0].Position.X + 600, state.Actors[0].Position.Z);
        enemy.Pending = new("enemy.strike", 1, state.Actors[0].Position, state.Tick, state.NextActionId++);
        return CombatSession.Restore(Content, state);
    }

    [Fact]
    public void PyreRequiresAnAcceptedMovingDodgeAndPulsesExpire()
    {
        var session = Session(new() { PyreTrail = true });
        Assert.Contains(session.Step([new(CombatCommandKind.Dodge)]), e => e.Kind == "CommandRejected");
        Assert.Empty(session.View.Areas);
        var events = session.Step([Dodge]);
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.PyrePower);
        Assert.Equal(3, session.View.Areas.Count);
        Assert.All(session.Capture().Areas, area =>
        {
            Assert.Equal("effect.pyre_trail", area.SkillId); Assert.Equal(DamageFamily.Fire, area.Family);
            Assert.Equal(1, area.Depth); Assert.InRange(area.ExpiresTick - session.Tick, 1, 60);
        });
        long[] patches = session.View.Areas.Select(a => a.Id).ToArray();
        Assert.Contains(session.Step([Dodge]), e => e.Kind == "CommandRejected");
        Assert.Equal(patches, session.View.Areas.Select(a => a.Id));
        var state = session.Capture(); state.Actors[1].Position = state.Areas[0].Position;
        session = CombatSession.Restore(Content, state);
        events = Advance(session, 61);
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ContentId == "effect.pyre_trail" && e.Amount > 0);
        Assert.Contains(events, e => e.Kind == "StatusApplied" && e.ContentId == "Burning");
        Assert.Empty(session.View.Areas);
    }

    [Fact]
    public void WallBlockedDodgeDoesNotLayAnUntraveledFireTrail()
    {
        var state = Session(new() { PyreTrail = true }).Capture();
        state.Actors[0].Position = new(CombatContent.Parse(Content).Room.HalfWidth - CombatSession.ActorRadius, 0);
        var session = CombatSession.Restore(Content, state); session.Step([new(CombatCommandKind.Dodge, X: 1)]);
        Assert.Empty(session.View.Areas);
    }

    [Fact]
    public void OathChargesOnlyActualEnemyBarrierAbsorptionAndCapsAtSixty()
    {
        var session = EnemyStrike(Session(new() { OathReprisal = true }), barrier: 200);
        var events = session.Step(); int absorbed = Assert.Single(events, e => e.Kind == "BarrierAbsorbed").Amount;
        Assert.Equal(absorbed, session.Capture().Legendary!.OathCharge);
        Assert.Equal(240, session.Capture().Legendary!.OathUntil);
        for (int i = 0; i < 8; i++) { session = EnemyStrike(session, barrier: 200); session.Step(); }
        Assert.Equal(60, session.Capture().Legendary!.OathCharge);
        session = EnemyStrike(Session(new() { OathReprisal = true }), barrier: 0); session.Step();
        Assert.Null(session.Capture().Legendary);
        session = EnemyStrike(Session(new() { OathReprisal = true }), barrier: 200);
        var state = session.Capture(); state.Actors[0].InvulnerableUntil = 10;
        session = CombatSession.Restore(Content, state); session.Step(); Assert.Null(session.Capture().Legendary);
        session = EnemyStrike(Session(new()), barrier: 200); session.Step(); Assert.Null(session.Capture().Legendary);
    }

    [Fact]
    public void OathReleasesOnceOnSuccessfulMeleeAndDoesNotCritOrGenerateExtraResource()
    {
        var session = EnemyStrike(Session(new() { OathReprisal = true }), 200); session.Step();
        int charge = session.Capture().Legendary!.OathCharge;
        var state = session.Capture(); state.Actors[2].Position = new(-3300, 700);
        session = CombatSession.Restore(Content, state);
        List<CombatEvent> events = [.. session.Step([Cast("skill.cleave", state.Actors[1].Id)]), .. Advance(session, 8)];
        Assert.Single(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.OathPower && e.Amount == charge);
        Assert.Equal(2, events.Count(e => e.Kind == "DamageApplied" && e.ContentId == "effect.oath_reprisal"));
        Assert.DoesNotContain(events, e => e.Kind == "CriticalHit" && e.ContentId == "effect.oath_reprisal");
        Assert.Equal(14, session.View.Resource); Assert.Null(session.Capture().Legendary);
        events = Advance(session, 10); events.AddRange(session.Step([Cast("skill.cleave", state.Actors[1].Id)])); events.AddRange(Advance(session, 8));
        Assert.DoesNotContain(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.OathPower);
    }

    [Fact]
    public void MissedMeleeAndProjectilesDoNotConsumeOathCharge()
    {
        var session = EnemyStrike(Session(new() { OathReprisal = true }), 200); session.Step();
        var state = session.Capture(); int charge = state.Legendary!.OathCharge;
        state.Actors[1].Position = new(5000, 0);
        session = CombatSession.Restore(Content, state); session.Step([Cast("skill.cleave", state.Actors[1].Id)]); Advance(session, 8);
        Assert.Equal(charge, session.Capture().Legendary!.OathCharge);
        session = EnemyStrike(Session(new(Discipline: "Arcanist") { OathReprisal = true }), 200); session.Step();
        charge = session.Capture().Legendary!.OathCharge;
        session.Step([Cast("skill.fire_lance", session.View.Actors[1].Id)]); Advance(session, 20);
        Assert.Equal(charge, session.Capture().Legendary!.OathCharge);
    }

    [Fact]
    public void BarrierRetaliationCannotReleaseItsOwnOathCharge()
    {
        var session = Session(new() { OathReprisal = true });
        session.Step([new(CombatCommandKind.SetMutation, ContentId: "mutation.no_ground_given")]);
        session = EnemyStrike(session, 200); var events = session.Step();
        Assert.Contains(events, e => e.Kind == "DamageApplied" && e.ContentId == "effect.retaliation" && e.Amount > 0);
        Assert.True(session.Capture().Legendary!.OathCharge > 0);
        Assert.DoesNotContain(events, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.OathPower);
    }

    [Fact]
    public void WidowConsumesOneDodgeTokenWithADelayedWeakNonRecursiveProjectile()
    {
        var session = Session(new(Discipline: "Arcanist", FlatDamage: 400, ForkCount: 2, CriticalBasisPoints: 7500) { WidowEcho = true });
        var setup = session.Capture(); setup.Actors[1] = setup.Actors[1] with { Health = 5000, MaxHealth = 5000 };
        session = CombatSession.Restore(Content, setup);
        session.Step([Dodge]); Advance(session, 5);
        session.Step([Cast("skill.fire_lance", session.View.Actors[1].Id)]); Advance(session, 5);
        var state = session.Capture(); var echo = Assert.Single(state.Projectiles, p => p.SkillId == "effect.widow_echo");
        Assert.True(echo.LaunchTick > session.Tick); Assert.DoesNotContain(session.View.Projectiles, p => p.ContentId == echo.SkillId);
        Assert.Equal(12, echo.Damage); Assert.Equal(1, echo.Depth); Assert.Equal(0, echo.Fork + echo.Chain + echo.Pierce);
        Assert.Equal(DamageFamily.Void, echo.Family); Assert.True(state.Legendary is null || state.Legendary.WidowUntil == 0);
        var events = Advance(session, 18);
        Assert.Equal(12, Assert.Single(events, e => e.Kind == "DamageApplied" && e.ContentId == echo.SkillId).Amount);
        Assert.DoesNotContain(events, e => e.ContentId == echo.SkillId && e.Kind is "CriticalHit" or "ProjectileForked" or "ProjectileChained");
        Assert.DoesNotContain(events, e => e.Kind == "LegendaryTriggered");
        session.Step([Cast("skill.fire_lance", session.View.Actors[1].Id)]); events = Advance(session, 20);
        Assert.DoesNotContain(events, e => e.ContentId == LegendaryEquipment.WidowPower);
    }

    [Fact]
    public void InvalidDodgeAndMeleeDoNotPrepareOrConsumeWidow()
    {
        var session = Session(new() { WidowEcho = true }); session.Step([new(CombatCommandKind.Dodge)]);
        Assert.Null(session.Capture().Legendary);
        session.Step([Dodge]); long until = session.Capture().Legendary!.WidowUntil;
        session.Step([Dodge]); Assert.Equal(until, session.Capture().Legendary!.WidowUntil);
        Advance(session, 5); session.Step([Cast("skill.cleave", session.View.Actors[1].Id)]); Advance(session, 8);
        Assert.Equal(until, session.Capture().Legendary!.WidowUntil);
        Assert.DoesNotContain(session.Capture().Projectiles, p => p.SkillId == "effect.widow_echo");
    }

    [Fact]
    public void LegendaryEffectsRespectSharedAreaAndProjectileBudgets()
    {
        var session = Session(new() { PyreTrail = true }); session.Step([Dodge]);
        var state = session.Capture(); var patch = state.Areas[0];
        while (state.Areas.Count < CombatSession.MaxAreas - 1) state.Areas.Add(patch with { Id = state.NextObjectId++ });
        state.DodgeReadyTick = state.Tick;
        session = CombatSession.Restore(Content, state); var events = session.Step([Dodge]);
        Assert.Equal(CombatSession.MaxAreas, session.Capture().Areas.Count);
        Assert.Contains(events, e => e.Kind == "EffectBudgetExceeded");

        session = Session(new(Discipline: "Arcanist") { WidowEcho = true }); session.Step([Dodge]); Advance(session, 5);
        session.Step([Cast("skill.fire_lance", session.View.Actors[1].Id)]); Advance(session, 4);
        state = session.Capture(); var pending = state.Actors[0].Pending!;
        while (state.Projectiles.Count < CombatSession.MaxProjectiles - 1)
            state.Projectiles.Add(new(state.NextObjectId++, 1, 1, state.Actors[0].Position, new(-4500, 7000), 0,
                "skill.fire_lance", 1, DamageFamily.Fire, state.Tick + 90, pending.ActionId, 0));
        session = CombatSession.Restore(Content, state); events = session.Step();
        Assert.Contains(events, e => e.Kind == "EffectBudgetExceeded");
        Assert.True(session.Capture().Projectiles.Count <= CombatSession.MaxProjectiles);
        Assert.DoesNotContain(session.Capture().Projectiles, p => p.SkillId == "effect.widow_echo");
        Assert.True(session.Capture().Legendary is null || session.Capture().Legendary!.WidowUntil == 0);
    }

    [Theory]
    [InlineData("oath", 241)]
    [InlineData("widow", 151)]
    public void UnusedPreparationExpires(string power, int ticks)
    {
        var session = power == "oath" ? EnemyStrike(Session(new() { OathReprisal = true }), 200) : Session(new() { WidowEcho = true });
        if (power == "oath") session.Step(); else session.Step([Dodge]);
        Assert.NotNull(session.Capture().Legendary); Advance(session, ticks);
        Assert.Null(session.Capture().Legendary);
    }

    [Theory]
    [InlineData("oath")]
    [InlineData("widow")]
    [InlineData("pyre")]
    public void SavesAndReplaysResumeActiveLegendaryEffects(string power)
    {
        var session = power switch
        {
            "oath" => EnemyStrike(Session(new() { OathReprisal = true }), 200),
            "widow" => Session(new(Discipline: "Arcanist") { WidowEcho = true }),
            _ => Session(new() { PyreTrail = true })
        };
        if (power == "oath") session.Step();
        else
        {
            session.Step([Dodge]);
            if (power == "widow") { Advance(session, 5); session.Step([Cast("skill.fire_lance", session.View.Actors[1].Id)]); Advance(session, 5); }
        }
        var state = session.Capture(); string save = JsonData.Write(new CombatSave(1, session.StateHash, state));
        var restored = CombatSession.Restore(Content, CombatSaveStore.Read(save, Content));
        Assert.Equal(session.StateHash, restored.StateHash);
        var recorder = new CombatRecorder(session);
        for (int i = 0; i < 80; i++)
        {
            CombatCommand[] commands = power == "oath" && i == 0 ? [Cast("skill.cleave", session.View.Actors[1].Id)] : [];
            Assert.Equal(JsonData.Hash(restored.Step(commands)), JsonData.Hash(recorder.Step(session, commands)));
            Assert.Equal(session.StateHash, restored.StateHash);
        }
        Assert.True(CombatReplayRunner.Run(Content, JsonData.Read<CombatReplay>(JsonData.Write(recorder.Capture()))).Success);
    }

    [Theory]
    [InlineData("unequip")]
    [InlineData("death")]
    [InlineData("encounter")]
    public void RemovingPowersDyingAndChangingRoomsClearPreparedAndActiveEffects(string cause)
    {
        var session = Session(new(Discipline: "Arcanist") { PyreTrail = true, OathReprisal = true, WidowEcho = true });
        session.Step([Dodge]); Advance(session, 5);
        session.Step([Cast("skill.fire_lance", session.View.Actors[1].Id)]); Advance(session, 5);
        session = EnemyStrike(session, 200); session.Step();
        Assert.NotEmpty(session.Capture().Areas); Assert.NotNull(session.Capture().Legendary);
        Assert.Contains(session.Capture().Projectiles, p => p.SkillId == "effect.widow_echo");
        if (cause == "unequip") session.ApplyProgressionBuild(new(Discipline: "Arcanist"));
        else if (cause == "encounter") session = CombatSession.CreateEncounter(Content, 42, "hub", session.Capture());
        else { session = EnemyStrike(session, barrier: 0, health: 1); session.Step(); Assert.Equal(0, session.View.Actors[0].Health); }
        Assert.Null(session.Capture().Legendary);
        Assert.DoesNotContain(session.Capture().Areas, a => LegendaryEquipment.IsEffect(a.SkillId));
        Assert.DoesNotContain(session.Capture().Projectiles, p => LegendaryEquipment.IsEffect(p.SkillId));
    }

    [Theory]
    [InlineData("charge")]
    [InlineData("oath-expiry")]
    [InlineData("widow-expiry")]
    [InlineData("unequipped")]
    [InlineData("delayed-ordinary")]
    [InlineData("charge-without-expiry")]
    [InlineData("recursive-echo")]
    [InlineData("echo-as-ground")]
    public void MalformedLegendarySnapshotsAreRejected(string kind)
    {
        var session = Session(new(Discipline: "Arcanist") { PyreTrail = true, OathReprisal = true, WidowEcho = true });
        session.Step([Dodge]); Advance(session, 5); session.Step([Cast("skill.fire_lance", session.View.Actors[1].Id)]); Advance(session, 5);
        var state = session.Capture(); state.Legendary ??= new();
        switch (kind)
        {
            case "charge": state.Legendary.OathCharge = 61; break;
            case "oath-expiry": state.Legendary.OathUntil = state.Tick + 241; break;
            case "widow-expiry": state.Legendary.WidowUntil = state.Tick + 151; break;
            case "unequipped": state.ProgressionBuild = state.ProgressionBuild with { WidowEcho = false }; break;
            case "charge-without-expiry": state.Legendary.OathCharge = 20; state.Legendary.OathUntil = 0; break;
            case "recursive-echo":
                int echoIndex = state.Projectiles.FindIndex(p => p.SkillId == "effect.widow_echo");
                state.Projectiles[echoIndex] = state.Projectiles[echoIndex] with { Fork = 2 }; break;
            case "echo-as-ground": state.Areas[0] = state.Areas[0] with { SkillId = "effect.widow_echo" }; break;
            default:
                int index = state.Projectiles.FindIndex(p => p.SkillId == "effect.widow_echo");
                state.Projectiles[index] = state.Projectiles[index] with { SkillId = "skill.fire_lance" }; break;
        }
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(Content, state));
    }

    [Theory]
    [InlineData("campaign.road", LegendaryEquipment.Pyre)]
    [InlineData("campaign.rootheart", LegendaryEquipment.Widow)]
    [InlineData("campaign.covenant_warden", LegendaryEquipment.Oath)]
    public void ActualCampaignVictoryAwardsExactlyOneNamedLegendary(string encounter, string item)
    {
        var content = CampaignCombatContent.Parse(Content, Read("campaign-combat.json"));
        var hub = content.CreateEncounter("hub"); hub.ApplyProgressionBuild(new(Level: 8, Offense: 2, Defense: 2));
        var session = content.CreateEncounter(encounter, previous: hub.Capture());
        List<CombatEvent> events = [];
        for (int tick = 0; tick < 9000 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); tick++)
        {
            events.AddRange(session.Step(CampaignCombatSmoke.Commands(session.View, session.Room)));
            if (session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) Assert.DoesNotContain(session.View.Loot, l => l.Item.DefinitionId == item);
        }
        Assert.True(session.View.Actors[0].Health > 0); Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        Assert.Single(events, e => e.Kind == "LootDropped" && e.ContentId == item);
        Assert.Equal("Legendary", Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item).Item.Rarity);
        Advance(session, 10); Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item);
        Assert.Equal(session.StateHash, CombatSession.Restore(content.CombatJson, session.Capture()).StateHash);
        var state = session.Capture(); var revived = state.Actors.First(a => a.Faction == CombatFaction.Enemy);
        revived.Health = 1; revived.DeathProcessed = false; revived.Statuses.Clear(); state.ResurrectedActorIds.Add(revived.Id);
        revived.Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = state.Tick, ExpiresTick = state.Tick + 90 });
        session = CombatSession.Restore(content.CombatJson, state); var repeatedDeath = session.Step();
        Assert.DoesNotContain(repeatedDeath, e => e.Kind == "LootDropped" && e.ContentId == item);
        Assert.Single(session.View.Loot, l => l.Item.DefinitionId == item);
    }

    [Theory]
    [InlineData(LegendaryEquipment.Pyre, LegendaryEquipment.PyrePower, "item.starter_boots")]
    [InlineData(LegendaryEquipment.Oath, LegendaryEquipment.OathPower, "item.starter_chest")]
    [InlineData(LegendaryEquipment.Widow, LegendaryEquipment.WidowPower, "item.starter_gloves")]
    public void ExtractionLearnsThePowerAndEngravingEnforcesItsSlot(string legendary, string power, string receiver)
    {
        var content = ProductionContent.Resolve(Content, ProgressionContent.Parse(Read("progression.json")));
        var session = ProgressionSession.Create(content);
        foreach (string id in new[] { "mara", "torren", "cael", "oris", "kesh", "haven" }) Assert.True(session.CompleteObjective("quest." + id, "objective." + id).Success);
        Assert.True(session.EarnExperience("materials", 0, 500).Success);
        Assert.True(session.GrantItem("legendary", legendary, ItemRarity.Legendary).Success);
        Assert.True(session.GrantItem("receiver", receiver, ItemRarity.Rare).Success);
        Assert.True(session.GrantItem("wrong-slot", "item.starter_head", ItemRarity.Rare).Success);
        var extract = new CraftingRequest("extract", CraftingService.Extraction, 1);
        Assert.False(session.Craft(extract).Success); Assert.True(session.Craft(extract with { ConfirmPermanent = true }).Success);
        Assert.Contains(power, session.Capture().Character.PropertyLibrary); Assert.DoesNotContain(session.Capture().Character.Items, i => i.Id == 1);
        string before = session.StateHash;
        Assert.False(session.Craft(new("wrong", CraftingService.Engraving, 3, PropertyId: power)).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.Craft(new("engrave", CraftingService.Engraving, 2, PropertyId: power)).Success);
        Assert.Equal(power, session.Capture().Character.Items.Single(i => i.Id == 2).Engraving);
        Assert.Equal(session.StateHash, ProgressionSession.Restore(content, session.Capture()).StateHash);
    }

    [Theory]
    [InlineData(LegendaryEquipment.Pyre, LegendaryEquipment.PyrePower, "item.starter_legs")]
    [InlineData(LegendaryEquipment.Oath, LegendaryEquipment.OathPower, "item.starter_offhand")]
    [InlineData(LegendaryEquipment.Widow, LegendaryEquipment.WidowPower, "item.starter_amulet")]
    public void EquippedInnateAndEngravedCopiesProjectOnePowerAndUnequipIndependently(string legendary, string power, string engravedDefinition)
    {
        var adventure = AdventureContent.Parse(Read("adventure.json")); var policy = ProgressionContent.Parse(Read("progression.json"));
        var state = ProductionSession.Create(Content, adventure, policy).Capture(); var combat = state.Expedition.Combat;
        combat.Actors[0].Position = new(2000, -2000);
        foreach (string definitionId in new[] { legendary, engravedDefinition })
        {
            var definition = CombatContent.Parse(Content).Items.Single(i => i.Id == definitionId); long id = combat.NextObjectId++;
            combat.Loot.Add(new(id, combat.Actors[0].Position, new(id, definition.Id, definition.Name, definition.Slot, "Legendary", definition.Damage, definition.Armor, definition.CriticalBasisPoints)));
        }
        var session = ProductionSession.Restore(Content, adventure, policy, state);
        foreach (long id in session.Combat.View.Loot.Select(l => l.Id).ToArray())
            Assert.Contains(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: id)), e => e.Kind == "LootPickedUp");
        state = session.Capture(); var character = state.Progression.Character;
        long innateId = character.Items.Single(i => i.DefinitionId == legendary).Id;
        var engraved = character.Items.Last(i => i.DefinitionId == engravedDefinition); engraved.Engraving = power; character.PropertyLibrary.Add(power);
        session = ProductionSession.Restore(Content, adventure, policy, state);
        var innateSlot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(Content).Items.Single(i => i.Id == legendary).Slot);
        var engravedSlot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(Content).Items.Single(i => i.Id == engravedDefinition).Slot);
        Assert.False(Active(session.Combat.ProgressionBuild, power));
        Assert.True(session.Equip(innateId, innateSlot).Success); Assert.True(Active(session.Combat.ProgressionBuild, power));
        var once = session.Combat.ProgressionBuild;
        Assert.True(session.Equip(engraved.Id, engravedSlot).Success);
        var twice = session.Combat.ProgressionBuild;
        Assert.Equal((once.PyreTrail, once.OathReprisal, once.WidowEcho), (twice.PyreTrail, twice.OathReprisal, twice.WidowEcho));
        Assert.True(session.Unequip(innateSlot).Success); Assert.True(Active(session.Combat.ProgressionBuild, power));
        Assert.True(session.Unequip(engravedSlot).Success); Assert.False(Active(session.Combat.ProgressionBuild, power));
        Assert.Equal(session.StateHash, ProductionSession.Restore(Content, adventure, policy, session.Capture()).StateHash);
    }

    private static bool Active(CombatProgressionBuild build, string power) => power switch
    {
        LegendaryEquipment.PyrePower => build.PyreTrail,
        LegendaryEquipment.OathPower => build.OathReprisal,
        _ => build.WidowEcho
    };

    [Theory]
    [InlineData("act.grey_march", LegendaryEquipment.Pyre)]
    [InlineData("act.verdant_maw", LegendaryEquipment.Widow)]
    [InlineData("act.shattered_spine", LegendaryEquipment.Oath)]
    public void RepeatableFracturesAwardTheirRegionLegendaryOnlyInTheFinalRoom(string region, string expected)
    {
        var content = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Content, Read("campaign-combat.json")).CombatJson,
            Read("endgame-combat.json"), EndgameContent.Parse(Read("endgame.json")));
        var manifest = content.CreateFractureManifest(new(1, 42, region, 1, ["fracture.burning_haste"], "Vael", "Materials"), 1);
        var hub = CombatSession.CreateEncounter(content.CombatJson, 42, "hub");
        foreach (int index in new[] { 0, manifest.Rooms.Length - 1 })
        {
            // Isolate completion reward policy from boss difficulty, which the earned campaign route covers above.
            var state = content.CreateEncounter(manifest, index, 0, hub.Capture()).Capture();
            if (manifest.Rooms[index].Boss) state.Campaign!.BossPhase = 2;
            foreach (var enemy in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
            {
                enemy.Health = 1;
                enemy.Statuses.Add(new() { Id = "Burning", SourceId = 1, OwnerId = 1, ActionId = state.NextActionId++, NextTick = 0, ExpiresTick = 90 });
            }
            var session = CombatSession.Restore(content.CombatJson, state); session.Step();
            Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
            if (index == 0) Assert.DoesNotContain(session.View.Loot, l => LegendaryEquipment.IsItem(l.Item.DefinitionId));
            else Assert.Equal("Legendary", Assert.Single(session.View.Loot, l => l.Item.DefinitionId == expected).Item.Rarity);
        }
    }

    [Fact]
    public void FreshCampaignEarnsPicksUpEquipsAndUsesPyreWithSaveAndReplay()
    {
        string content = CampaignCombatContent.Parse(Content, Read("campaign-combat.json")).CombatJson;
        var adventure = AdventureContent.Parse(Read("adventure.json")); var policy = ProgressionContent.Parse(Read("progression.json"));
        var campaign = CampaignContent.Parse(Read("campaign.json"));
        var session = CampaignRuntimeSession.Create(content, adventure, policy, campaign);
        bool Equipped() => session.Combat.ProgressionBuild.PyreTrail;
        for (int i = 0; i < 3000 && !(session.ActiveEncounterId == "campaign.road" && session.EncounterCleared && session.Combat.View.Loot.Count == 0); i++)
        {
            var result = session.Execute(CampaignRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason);
        }
        Assert.Equal("campaign.road", session.ActiveEncounterId); Assert.True(session.EncounterCleared); Assert.Empty(session.Combat.View.Loot);
        var earned = Assert.Single(session.Capture().Production.Progression.Character.Items, i => i.DefinitionId == LegendaryEquipment.Pyre);
        Assert.Equal(ItemRarity.Legendary, earned.Rarity); Assert.False(Equipped());
        Assert.True(session.ReturnToHub().Success); Assert.Contains(session.Interactions, i => i.ActionId == "service.torren");
        var equip = new CampaignRuntimeCommand(CampaignRuntimeAction.Production, Production: new(ProductionAction.Equip, ItemId: earned.Id, Slot: EquipmentSlot.Boots));
        for (int i = 0; i < 150 && !Equipped(); i++)
        {
            var result = session.Execute(CampaignRuntimeSmoke.AtInteraction(session, "service.torren", equip)); Assert.True(result.Success, result.Reason);
        }
        Assert.True(Equipped()); Assert.Equal(earned.Id, session.Production.ProgressionView.Equipment[EquipmentSlot.Boots]);
        Assert.True(session.Step(new CombatCommand(CombatCommandKind.Stop)).Success);
        var dodged = session.Step(new CombatCommand(CombatCommandKind.Dodge, Z: 1)); Assert.True(dodged.Success, dodged.Reason);
        Assert.Single(dodged.CombatEvents, e => e.Kind == "LegendaryTriggered" && e.ContentId == LegendaryEquipment.PyrePower);
        Assert.Equal(3, session.Combat.View.Areas.Count(a => a.ContentId == "effect.pyre_trail"));
        var restored = CampaignRuntimeSession.Restore(content, adventure, policy, campaign, session.Capture());
        Assert.Equal(session.StateHash, restored.StateHash);
        Assert.True(CampaignRuntimeReplayRunner.Run(content, adventure, policy, campaign, session.CaptureReplay()).Success);
        for (int i = 0; i < 65; i++) Assert.Equal(JsonData.Hash(session.Step()), JsonData.Hash(restored.Step()));
        Assert.Equal(session.StateHash, restored.StateHash); Assert.Empty(session.Combat.View.Areas);
    }
}
