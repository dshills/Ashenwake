using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignCombatTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static CampaignCombatContent Content() => CampaignCombatContent.Parse(File.ReadAllText(Path.Combine(Root, "content/combat.json")), File.ReadAllText(Path.Combine(Root, "content/campaign-combat.json")));
    [Fact]
    public void FurnaceWarningsClipToSmallerArenaAndSurviveRestore()
    {
        var baseline = CombatContent.Parse(File.ReadAllText(Path.Combine(Root, "content/combat.json")));
        baseline = baseline with { Room = baseline.Room with { HalfWidth = 8000, HalfDepth = 8000 } };
        var authored = JsonData.Read<CampaignCombatDefinition>(File.ReadAllText(Path.Combine(Root, "content/campaign-combat.json")));
        authored = authored with { Encounters = authored.Encounters.Select(e => e.Id == "campaign.furnace_spindle" ? e with { Room = baseline.Room } : e).ToArray() };
        var content = CampaignCombatContent.Parse(JsonData.Write(baseline), JsonData.Write(authored));
        var state = content.CreateEncounter("campaign.furnace_spindle").Capture();
        state.Actors[0].InvulnerableUntil = 1000;
        state.Campaign!.Actors[state.Actors[1].Id] = state.Campaign.Actors[state.Actors[1].Id] with { Modifiers = [] };
        var session = CombatSession.Restore(content.CombatJson, state);
        var axes = new HashSet<string>(); var events = new List<CombatEvent>();
        for (int tick = 0; tick < 180; tick++)
        {
            events.AddRange(session.Step());
            foreach (var hazard in session.View.CampaignHazards!.Where(h => h.ContentId == "campaign.furnace_vent"))
            {
                Assert.InRange(hazard.Position.X, -8000, 8000); Assert.InRange(hazard.End.X, -8000, 8000);
                Assert.InRange(hazard.Position.Z, -8000, 8000); Assert.InRange(hazard.End.Z, -8000, 8000);
                axes.Add(hazard.Position.X == hazard.End.X ? "vertical" : "horizontal");
            }
            var restored = CombatSession.Restore(content.CombatJson, session.Capture());
            Assert.Equal(session.StateHash, restored.StateHash);
        }
        Assert.Equal(new[] { "horizontal", "vertical" }, axes.Order());
        Assert.Contains(events, e => e.Kind == "CampaignHazardResolved" && e.ContentId == "campaign.furnace_vent");
    }

    public static IEnumerable<object[]> Encounters() => new[] { "campaign.road", "campaign.monastery", "campaign.bell_saint", "campaign.living_ruins", "campaign.plague_village", "campaign.rootheart", "campaign.cinder_pack", "campaign.extraction_floor", "campaign.furnace_spindle", "campaign.bone_causeway", "campaign.contract_hall", "campaign.covenant_warden", "campaign.repeating_rooms", "campaign.identity_memory", "campaign.breach_heart", "exploration.burning_rain", "exploration.first_oath", "exploration.antler_hunt", "exploration.widow_crypt", "exploration.briar_shrine", "exploration.sealed_foundry" }.Select(id => new object[] { id });
    [Theory, MemberData(nameof(Encounters))]
    public void ActualEncounterPlaysToVictoryAndResumesDeterministically(string id)
    {
        var content = Content();
        var hub = content.CreateEncounter("hub"); hub.ApplyProgressionBuild(new(Level: 8, Offense: 2, Defense: 2));
        var session = content.CreateEncounter(id, previous: hub.Capture()); var other = CombatSession.Restore(content.CombatJson, session.Capture());
        var phases = new HashSet<int>(); int ticks = 0;
        while (ticks++ < 9000 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0))
        {
            var commands = CampaignCombatSmoke.Commands(session.View, session.Room); var events = session.Step(commands); var replay = other.Step(commands);
            Assert.Equal(JsonData.Hash(events), JsonData.Hash(replay));
            foreach (var e in events.Where(e => e.Kind == "BossPhaseChanged")) phases.Add(e.Amount);
            if (ticks % 75 == 0) other = CombatSession.Restore(content.CombatJson, other.Capture());
        }
        Assert.True(session.View.Actors[0].Health > 0, id + " player died at " + ticks + " phase " + session.View.BossPhase + " enemies " + string.Join(",", session.View.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).Select(a => a.DefinitionId + ":" + a.Health + "@" + a.Position)));
        Assert.True(session.View.Actors.All(a => a.Faction != CombatFaction.Enemy || a.Health <= 0), id + " stalled: " + string.Join(",", session.View.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).Select(a => a.DefinitionId + ":" + a.Health + "@" + a.Position)));
        Assert.Equal(session.StateHash, other.StateHash); Assert.Empty(session.View.CampaignHazards!);
        if (id is "campaign.bell_saint" or "campaign.breach_heart") Assert.Equal(new[] { 2, 3 }, phases.Order());
    }
    [Fact]
    public void CampaignRegistryMatchesAllAuthoredCampaignAndExplorationReferences()
    {
        var content = Content(); var campaign = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(Root, "content/campaign.json")))!;
        var ids = campaign["acts"]!.AsArray().SelectMany(a => a!["encounters"]!.AsArray().Select(e => (string)e!["id"]!)).Concat(campaign["exploration"]!.AsArray().Select(e => (string)e!["encounterId"]!));
        Assert.Equal(ids.Order(), content.EncounterIds.Order());
        Assert.Equal(8, CombatContent.Parse(content.CombatJson).Campaign!.Encounters.SelectMany(e => e.Spawns).SelectMany(s => s.Modifiers).Distinct().Count());
        Assert.Throws<InvalidDataException>(() => CombatSession.ValidateEliteModifiers(["Mirrorborn", "Gravewake"]));
        Assert.Throws<InvalidDataException>(() => CombatSession.ValidateEliteModifiers(["Null", "Hunter"]));
        Assert.Throws<InvalidDataException>(() => CombatSession.ValidateEliteModifiers(["Devourer", "Martyr"]));
    }
    [Fact]
    public void StormExpiresAndAllScopedStateIsRemovedOnLeaving()
    {
        var content = Content(); var state = content.CreateEncounter("exploration.burning_rain").Capture();
        state.Actors[0].InvulnerableUntil = 1500;
        foreach (var actor in state.Actors.Where(a => a.Faction == CombatFaction.Enemy)) actor.RecoveryUntil = 1500;
        var session = CombatSession.Restore(content.CombatJson, state); var events = new List<CombatEvent>();
        for (int i = 0; i < 950; i++) events.AddRange(session.Step());
        Assert.Contains(events, e => e.Kind == "FragmentOverchargeWindow"); Assert.Contains(events, e => e.Kind == "CampaignRuleExpired");
        Assert.DoesNotContain(session.View.CampaignHazards!, h => h.ContentId.StartsWith("rule.", StringComparison.Ordinal));
        session = content.CreateEncounter("hub", previous: session.Capture());
        Assert.Null(session.Capture().Campaign); Assert.Empty(session.View.CampaignHazards!); Assert.Equal("", session.View.CampaignRule);
    }
    [Fact]
    public void WarningGeometryAllowsEvasionAndMalformedCampaignStateIsRejected()
    {
        var content = Content(); var state = content.CreateEncounter("campaign.identity_memory").Capture();
        foreach (var actor in state.Actors.Where(a => a.Faction == CombatFaction.Enemy)) actor.RecoveryUntil = 1000;
        var source = state.Actors[1]; var center = state.Actors[0].Position;
        state.Campaign!.Hazards.Add(new(state.NextObjectId++, "Circle", center, center, 1300, 20, "campaign.test_warning", source.Id, 40, DamageFamily.Void, "", state.NextActionId++));
        var stationary = CombatSession.Restore(content.CombatJson, state); var evasive = CombatSession.Restore(content.CombatJson, state);
        for (int i = 0; i < 22; i++) { stationary.Step(); evasive.Step([new(CombatCommandKind.Move, Z: -1)]); }
        Assert.True(stationary.View.Actors[0].Health < evasive.View.Actors[0].Health);
        state.Campaign.Hazards[0] = state.Campaign.Hazards[0] with { SourceId = 9999 };
        Assert.Throws<InvalidDataException>(() => CombatSession.Restore(content.CombatJson, state));
    }
    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void BellSaintIsPlayableAtEarlyCampaignLevelWithoutUnlockedUltimate(string discipline)
    {
        var content = Content();
        var hub = content.CreateEncounter("hub"); hub.ApplyProgressionBuild(new(Discipline: discipline, Level: 2, UltimateUnlocked: false, UnlockedMutations: []));
        var session = content.CreateEncounter("campaign.bell_saint", previous: hub.Capture());
        for (int i = 0; i < 5000 && session.View.Actors[0].Health > 0 && session.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0); i++) session.Step(CampaignCombatSmoke.Commands(session.View, session.Room));
        Assert.True(session.View.Actors[0].Health > 0, discipline + " died");
        Assert.DoesNotContain(session.View.Actors, a => a.Faction == CombatFaction.Enemy && a.Health > 0);
    }
    [Theory]
    [InlineData("Mirrorborn", "EliteCopyCreated")]
    [InlineData("Gravewake", "EliteResurrected")]
    [InlineData("Stormbound", "CampaignHazardResolved")]
    [InlineData("Devourer", "EliteDevoured")]
    [InlineData("Null", "FragmentSuppressed")]
    [InlineData("Riftborn", "EliteTeleported")]
    public void ActiveEliteMechanicsResolveThroughTelegraphsAndRemainBounded(string modifier, string expected)
    {
        var content = Content(); var state = content.CreateEncounter("campaign.monastery").Capture();
        var elite = state.Actors[1]; elite.Position = new(-1200, 0); elite.Health = elite.MaxHealth / 2;
        state.Campaign!.Actors[elite.Id] = new() { Modifiers = [modifier], NextEliteTick = 0 };
        state.Actors[2].Position = new(-1200, 1800);
        foreach (var actor in state.Actors.Skip(2)) actor.RecoveryUntil = 1000;
        if (modifier == "Gravewake") { state.Actors[2].Health = 0; state.Actors[2].DeathProcessed = true; }
        if (modifier != "Null") state.Actors[0].InvulnerableUntil = 1000;
        var session = CombatSession.Restore(content.CombatJson, state); var events = new List<CombatEvent>();
        for (int i = 0; i < 120; i++)
        {
            events.AddRange(session.Step());
            if (i == 20) { Assert.DoesNotContain(events, e => e.Kind == expected); session = CombatSession.Restore(content.CombatJson, session.Capture()); }
        }
        Assert.Contains(events, e => e.Kind == expected);
        if (modifier == "Mirrorborn")
        {
            Assert.InRange(events.Count(e => e.Kind == expected), 1, 2);
            for (int i = 0; i < 250; i++) events.AddRange(session.Step());
            Assert.InRange(events.Count(e => e.Kind == expected), 1, 2);
            Assert.DoesNotContain(session.View.Actors, a => session.Capture().Campaign!.Actors.GetValueOrDefault(a.Id)?.IsEcho == true && a.Health > 0);
        }
        if (modifier == "Gravewake")
        {
            Assert.Contains(state.Actors[2].Id, session.Capture().ConsumedCorpseIds);
            Assert.Single(events, e => e.Kind == "EliteResurrected");
        }
        Assert.InRange(session.View.PeakEffects, 0, CombatSession.MaxEffectsPerTick); Assert.InRange(session.View.CampaignHazards!.Count, 0, 32);
    }
    [Fact]
    public void HunterResistsHardControlAndMartyrEmpowersSurvivorsOnce()
    {
        var content = Content(); var state = content.CreateEncounter("campaign.monastery").Capture(); state.Momentum = 100;
        var elite = state.Actors[1]; elite.Position = new(-2800, 0); elite.RecoveryUntil = 1000;
        state.Campaign!.Actors[elite.Id] = new() { Modifiers = ["Hunter", "Martyr"] };
        var session = CombatSession.Restore(content.CombatJson, state); var events = new List<CombatEvent>();
        events.AddRange(session.Step([new(CombatCommandKind.Cast, SkillId: "skill.shield_breaker", TargetId: elite.Id)]));
        for (int i = 0; i < 12; i++) events.AddRange(session.Step());
        Assert.Contains(events, e => e.Kind == "StatusResisted" && e.ContentId == "Staggered");
        state = session.Capture(); state.Actors[1].Health = 1; state.Actors[1].Statuses.Add(new() { Id = "Burning", OwnerId = 1, SourceId = 1, ActionId = state.NextActionId++, NextTick = state.Tick, ExpiresTick = state.Tick + 90 });
        session = CombatSession.Restore(content.CombatJson, state); events = session.Step().ToList();
        Assert.Contains(events, e => e.Kind == "EliteEmpowered"); Assert.All(session.Capture().Campaign!.Actors.Values, a => Assert.InRange(a.Empowerment, 0, 1));
        Assert.DoesNotContain(session.Step(), e => e.Kind == "EliteEmpowered");
    }
    [Fact]
    public void FurnaceGuardPresentationUsesExactDefenseBoundaryWithoutChangingState()
    {
        var content = Content(); var state = content.CreateEncounter("campaign.furnace_spindle").Capture();
        var boss = state.Actors.Single(a => a.DefinitionId == "boss.furnace_spindle");
        // Animation state can lag the defense timer and warning budgets can leave no slag warning.
        boss.State = "Guarded"; boss.RecoveryUntil = 1000;
        state.Campaign!.Actors[boss.Id].GuardedUntil = 1;
        var session = CombatSession.Restore(content.CombatJson, state);
        string hash = session.StateHash;
        Assert.True(session.View.Actors.Single(a => a.Id == boss.Id).Guarded);
        Assert.Equal(hash, session.StateHash);
        session.Step();
        Assert.Equal(1, session.Tick);
        Assert.False(session.View.Actors.Single(a => a.Id == boss.Id).Guarded);
        state = session.Capture(); state.Campaign!.Actors[boss.Id].GuardedUntil = 20;
        state.Actors.Single(a => a.Id == boss.Id).State = "Windup";
        session = CombatSession.Restore(content.CombatJson, state);
        Assert.True(session.View.Actors.Single(a => a.Id == boss.Id).Guarded);
        Assert.False(session.View.Actors.Single(a => a.Id == 1).Guarded);
    }
    [Fact]
    public void BreachShieldProjectionMatchesDamageAcrossChannelDeathAndExcludesMirrorbornCopies()
    {
        var content = Content(); var state = content.CreateEncounter("campaign.breach_heart").Capture();
        var boss = state.Actors.Single(a => a.DefinitionId == "boss.breach_heart");
        state.Actors[0].InvulnerableUntil = 1000;
        state.Campaign!.Actors[boss.Id].NextEliteTick = 0;
        foreach (var actor in state.Actors.Where(a => a.DefinitionId == "enemy.seal_channel")) actor.RecoveryUntil = 1000;
        var session = CombatSession.Restore(content.CombatJson, state);
        var copies = new List<int>();
        for (int tick = 0; tick < 80 && copies.Count == 0; tick++)
            copies.AddRange(session.Step().Where(e => e.Kind == "EliteCopyCreated").Select(e => e.TargetId));
        Assert.NotEmpty(copies);
        state = session.Capture();
        foreach (var actor in state.Actors.Where(a => a.Faction == CombatFaction.Enemy))
        { actor.RecoveryUntil = state.Tick + 100; actor.Pending = null; }
        var channel = state.Actors.First(a => a.DefinitionId == "enemy.seal_channel");
        channel.Health = 1;
        // The same real damage pipeline hits a protected boss, an unprotected same-definition
        // Mirrorborn copy, and a channel whose death must immediately break the boss's protection.
        foreach (int id in new[] { boss.Id, copies[0], channel.Id })
            state.Actors.Single(a => a.Id == id).Statuses.Add(new()
            {
                Id = "Burning",
                OwnerId = 1,
                SourceId = 1,
                ActionId = state.NextActionId++,
                NextTick = state.Tick,
                ExpiresTick = state.Tick + 90
            });
        session = CombatSession.Restore(content.CombatJson, state);
        string hash = session.StateHash;
        Assert.Equal(3, session.View.Actors.Count(a => a.DefinitionId == "enemy.seal_channel" && a.Health > 0));
        Assert.True(session.View.Actors.Single(a => a.Id == boss.Id).Shielded);
        Assert.All(copies, id =>
        {
            var copy = session.View.Actors.Single(a => a.Id == id);
            Assert.Equal(boss.DefinitionId, copy.DefinitionId); Assert.False(copy.Shielded);
        });
        Assert.Equal(hash, session.StateHash);
        var restored = CombatSession.Restore(content.CombatJson, session.Capture());
        Assert.Equal(hash, restored.StateHash);
        Assert.True(restored.View.Actors.Single(a => a.Id == boss.Id).Shielded);
        Assert.Equal(hash, restored.StateHash);
        var first = session.Step();
        Assert.Equal(JsonData.Hash(first), JsonData.Hash(restored.Step()));
        Assert.Equal(0, first.Single(e => e.Kind == "DamageApplied" && e.TargetId == boss.Id && e.ContentId == "Burning").Amount);
        Assert.True(first.Single(e => e.Kind == "DamageApplied" && e.TargetId == copies[0] && e.ContentId == "Burning").Amount > 0);
        Assert.Equal(0, session.View.Actors.Single(a => a.Id == channel.Id).Health);
        Assert.Equal(2, session.View.Actors.Count(a => a.DefinitionId == "enemy.seal_channel" && a.Health > 0));
        Assert.False(session.View.Actors.Single(a => a.Id == boss.Id).Shielded);
        restored = CombatSession.Restore(content.CombatJson, session.Capture());
        var subsequent = new List<CombatEvent>();
        for (int tick = 0; tick < 11; tick++)
        {
            var events = session.Step(); subsequent.AddRange(events);
            Assert.Equal(JsonData.Hash(events), JsonData.Hash(restored.Step()));
        }
        Assert.Contains(subsequent, e => e.Kind == "DamageApplied" && e.TargetId == boss.Id && e.ContentId == "Burning" && e.Amount > 0);
        Assert.Equal(session.StateHash, restored.StateHash);
    }
    [Fact]
    public void FurnaceCoreWindowChangesRealDamageAndStormOverchargeExpires()
    {
        var content = Content(); var state = content.CreateEncounter("campaign.furnace_spindle").Capture();
        var boss = state.Actors[1]; boss.Position = new(-2800, 0); boss.RecoveryUntil = 1000;
        state.Campaign!.Actors[boss.Id].GuardedUntil = 60;
        int Damage(CombatSnapshot snapshot)
        {
            var session = CombatSession.Restore(content.CombatJson, snapshot); var events = new List<CombatEvent>();
            events.AddRange(session.Step([new(CombatCommandKind.Cast, SkillId: "skill.cleave", TargetId: boss.Id)]));
            for (int i = 0; i < 5; i++) events.AddRange(session.Step());
            return events.Where(e => e.Kind == "DamageApplied" && e.ContentId == "skill.cleave" && e.TargetId == boss.Id).Sum(e => e.Amount);
        }
        int guarded = Damage(state); state.Campaign.Actors[boss.Id].GuardedUntil = 0; int exposed = Damage(state);
        Assert.True(exposed > guarded * 2);
        int DotDamage(long tick)
        {
            var storm = content.CreateEncounter("exploration.burning_rain").Capture(); storm.Tick = tick;
            storm.Campaign!.NextHazardTick = tick + 40;
            foreach (var actor in storm.Actors.Where(a => a.Faction == CombatFaction.Enemy)) actor.RecoveryUntil = tick + 500;
            storm.Actors[1].Statuses.Add(new() { Id = "Burning", OwnerId = 1, SourceId = 1, FragmentId = "fragment.eye_vael", Depth = 1, ActionId = storm.NextActionId++, NextTick = tick, ExpiresTick = tick + 90 });
            return CombatSession.Restore(content.CombatJson, storm).Step().Single(e => e.Kind == "DamageApplied" && e.ContentId == "Burning").Amount;
        }
        Assert.True(DotDamage(50) > DotDamage(950));
    }
}
