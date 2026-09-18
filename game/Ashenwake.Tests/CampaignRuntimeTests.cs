using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignRuntimeTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly Lazy<string> Composed = new(() => CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson);
    private static string CombatJson => Composed.Value;
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign => CampaignContent.Parse(Read("campaign.json"));
    private static CampaignRuntimeSession Fresh(string discipline = "Vanguard") => CampaignRuntimeSession.Create(CombatJson, Adventure, Policy, Campaign, discipline: discipline);
    private static CampaignRuntimeSession Restore(CampaignRuntimeSnapshot value) => CampaignRuntimeSession.Restore(CombatJson, Adventure, Policy, Campaign, value);
    private static void RunUntil(CampaignRuntimeSession session, Func<CampaignRuntimeSession, bool> complete, int maximum = CampaignRuntimeSmoke.MaximumCommands)
    {
        for (int i = 0; i < maximum && !complete(session); i++)
        {
            var command = CampaignRuntimeSmoke.Next(session); var result = session.Execute(command);
            Assert.True(result.Success, result.Reason + " / " + command);
        }
        Assert.True(complete(session), "Bounded public-input driver did not reach its objective: " + session.ActiveEncounterId);
    }

    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void EveryFreshDisciplineCompletesActualCampaignExplorationChoicesAndEnding(string discipline)
    {
        var session = Fresh(discipline); bool finalUltimateAvailable = false; int previousAct = 1;
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && !CampaignRuntimeSmoke.Complete(session); i++)
        {
            var result = session.Execute(CampaignRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason);
            if (session.ActiveEncounterId == "campaign.breach_heart") finalUltimateAvailable |= session.Combat.ProgressionBuild.UltimateUnlocked;
            if (session.View.Act != previousAct) { Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash); previousAct = session.View.Act; }
        }
        Assert.True(CampaignRuntimeSmoke.Complete(session)); var state = session.Capture();
        Assert.Equal(15, state.Campaign.CompletedEncounters.Count); Assert.Equal(3, state.Campaign.CompletedExploration.Count); Assert.Equal(5, state.Campaign.Choices.Count);
        Assert.Equal(5150, state.Campaign.EarnedExperience); Assert.Equal(5150, session.Production.ProgressionView.Experience); Assert.True(finalUltimateAvailable);
        Assert.Equal(0, state.Campaign.Deaths); Assert.Contains("profile.fractures", state.Production.Progression.Profile.Unlocks); Assert.Contains("profile.god_hunts", state.Production.Progression.Profile.Unlocks);
        Assert.Contains("profile.secret_hunt", state.Production.Progression.Profile.Unlocks); Assert.True(state.Production.Progression.Character.Mastery.Count > 0);
        Assert.Null(session.Combat.Capture().Campaign); Assert.Empty(session.Combat.View.CampaignHazards!);
        Assert.Equal(session.StateHash, Restore(state).StateHash);
        Assert.True(CampaignRuntimeReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, session.CaptureReplay()).Success);
    }

    [Fact]
    public void ChoiceGatesBossAndRevisitingCompletedRegionsCannotDuplicateRewards()
    {
        var session = Fresh(); Assert.False(session.EnterAct(5).Success);
        Assert.DoesNotContain(session.Interactions, i => i.ActionId == "npc.cael"); Assert.Equal("Explore the region and recover its testimony.", session.View.Revelation);
        RunUntil(session, s => s.Capture().Campaign.CompletedEncounters.Contains("campaign.monastery") && s.Combat.View.Loot.Count == 0);
        Assert.False(session.AdvanceEncounter().Success); Assert.NotEqual("Explore the region and recover its testimony.", session.View.Revelation);
        Assert.True(session.Choose("choice.fragment", "community").Success); Assert.False(session.Choose("choice.fragment", "reliquary").Success);
        RunUntil(session, s => s.Capture().Campaign.CompletedActs.Contains(1) && s.Combat.View.Loot.Count == 0);
        long xp = session.Production.ProgressionView.Experience; int materials = session.Production.ProgressionView.Materials;
        Assert.True(session.ReturnToHub().Success); Assert.Contains(session.Interactions, i => i.ActionId == "npc.cael");
        Assert.True(session.EnterAct(1).Success); Assert.True(session.EncounterCleared); Assert.DoesNotContain(session.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy);
        session.Step(); Assert.Equal(xp, session.Production.ProgressionView.Experience); Assert.Equal(materials, session.Production.ProgressionView.Materials);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }

    [Fact]
    public void DeathAndExplorationExitRestoreTheAnchorWithoutKeepingScopedCombatRules()
    {
        var session = Fresh(); RunUntil(session, s => s.ActiveEncounterId == "campaign.road");
        var dead = session.Capture(); var actor = dead.Combat.Actors.Single(a => a.Id == 1); actor.Health = 0; actor.DeathProcessed = true; actor.Pending = null;
        session = Restore(dead); session.Step(); Assert.Equal(1, session.Capture().Campaign.Deaths); Assert.Equal("campaign.road", session.ActiveEncounterId);
        Assert.Empty(session.Capture().Campaign.CompletedEncounters); Assert.Equal(0, session.Production.ProgressionView.Experience);
        Assert.Equal(session.Combat.View.Actors[0].MaxHealth, session.Combat.View.Actors[0].Health);
        RunUntil(session, s => s.Capture().Campaign.Exploration?.Id == "event.wake_hunt");
        Assert.Equal("clear", session.ActiveEncounterId); Assert.False(session.TrackClue("clue.heartwood_nest").Success);
        var before = session.Capture().Campaign.CompletedEncounters.ToArray();
        Assert.True(session.LeaveExploration().Success); Assert.Null(session.Capture().Campaign.Exploration); Assert.Null(session.Combat.Capture().Campaign);
        Assert.Equal(before, session.Capture().Campaign.CompletedEncounters); Assert.DoesNotContain("event.wake_hunt", session.Capture().Campaign.CompletedExploration);
        Assert.True(session.BeginExploration("event.wake_hunt").Success); RunUntil(session, s => s.ActiveEncounterId == "exploration.antler_hunt");
        Assert.NotNull(session.Combat.Capture().Campaign);
        var huntDeath = session.Capture(); var hunter = huntDeath.Combat.Actors.Single(a => a.Id == 1); hunter.Health = 0; hunter.DeathProcessed = true; hunter.Pending = null;
        var recovered = Restore(huntDeath); recovered.Step(); Assert.Null(recovered.Capture().Campaign.Exploration);
        Assert.NotEqual("Hunt", recovered.Combat.View.CampaignRule); Assert.DoesNotContain("event.wake_hunt", recovered.Capture().Campaign.CompletedExploration);
        Assert.True(session.ReturnToHub().Success);
        Assert.Null(session.Combat.Capture().Campaign); Assert.Empty(session.Combat.View.CampaignHazards!); Assert.Equal("", session.Capture().ExplorationReturnEncounter);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }

    [Fact]
    public void TimedStormExpiresInActualCombatWithoutGrantingItsUniqueReward()
    {
        var session = Fresh(); RunUntil(session, s => s.ActiveEncounterId == "exploration.burning_rain");
        var checkpoint = session.Capture();
        // Keep the fixture alive while real enemy AI and all 900 timed simulation ticks execute.
        checkpoint.Combat.Actors.Single(a => a.Id == 1).InvulnerableUntil = checkpoint.Combat.Tick + 1000;
        session = Restore(checkpoint); int materials = session.Production.ProgressionView.Materials; long experience = session.Production.ProgressionView.Experience;
        for (int i = 0; i < 900; i++) Assert.True(session.Execute(new(CampaignRuntimeAction.Tick), recordReplay: false).Success);
        Assert.Null(session.Capture().Campaign.Exploration); Assert.Null(session.Combat.Capture().Campaign); Assert.Empty(session.Combat.View.CampaignHazards!);
        Assert.DoesNotContain("event.resonance_storm", session.Capture().Campaign.CompletedExploration);
        Assert.Equal(materials, session.Production.ProgressionView.Materials); Assert.Equal(experience, session.Production.ProgressionView.Experience);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }

    [Fact]
    public void CappedRewardsStillCommitAndRestoreRejectsMissingOrPoisonedReceipts()
    {
        var initial = Fresh().Capture(); initial.Production.Progression.Character.Materials = 1000000; initial.Production.Expedition.Adventure.Materials = 1000000;
        var session = Restore(initial); RunUntil(session, s => s.Capture().Campaign.CompletedEncounters.Contains("campaign.road"));
        Assert.Equal(1000000, session.Production.ProgressionView.Materials); Assert.Equal(50, session.Production.ProgressionView.Experience);
        var missing = session.Capture(); missing.Production.Progression.Character.OperationReceipts.Remove("campaign.encounter.campaign.road");
        Assert.Throws<InvalidDataException>(() => Restore(missing));
        var poisoned = Fresh().Capture(); poisoned.Production.Progression.Character.OperationReceipts["campaign.encounter.campaign.road"] = new string('A', 64);
        Assert.Throws<InvalidDataException>(() => Restore(poisoned));
    }

    [Fact]
    public void FullProjectedInventoryCanLeaveDropsAndFinishWonExploration()
    {
        var snapshot = Fresh().Capture(); var state = snapshot.Production.Progression.Character;
        var body = snapshot.Production.Expedition.Combat; var arena = snapshot.Combat;
        var definition = CombatContent.Parse(CombatJson).Items.Single(i => i.Id == "item.starter_head");
        while (body.Inventory.Count < 512)
        {
            long id = state.NextItemId++;
            var item = new CombatItem(id, definition.Id, definition.Name, definition.Slot, "Common", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
            body.Inventory.Add(item); arena.Inventory.Add(item);
            state.Items = [.. state.Items, new PermanentItem { Id = id, DefinitionId = definition.Id, Rarity = ItemRarity.Common, BaseDamage = item.Damage, BaseArmor = item.Armor, BaseCriticalBasisPoints = item.CriticalBasisPoints }];
        }
        body.NextObjectId = Math.Max(body.NextObjectId, state.NextItemId); arena.NextObjectId = Math.Max(arena.NextObjectId, state.NextItemId);
        var session = Restore(snapshot);
        RunUntil(session, s => s.Capture().Campaign.CompletedEncounters.Contains("campaign.road"));
        Assert.True(session.Combat.View.Loot.Count > 0); Assert.Equal(512, session.Combat.View.Inventory.Count);
        var next = session.AdvanceEncounter(); Assert.True(next.Success, next.Reason); Assert.Contains(next.WorldEvents, e => e.StartsWith("GroundLootLeftBehind:", StringComparison.Ordinal));
        RunUntil(session, s => s.ActiveEncounterId == "exploration.antler_hunt" && !s.Combat.View.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0));
        Assert.True(session.Combat.View.Loot.Count > 0); int materials = session.Production.ProgressionView.Materials;
        var finish = session.LeaveExploration(); Assert.True(finish.Success, finish.Reason);
        Assert.Contains("event.wake_hunt", session.Capture().Campaign.CompletedExploration); Assert.Equal(materials + 20, session.Production.ProgressionView.Materials);
        Assert.Equal(512, session.Production.Capture().Progression.Character.Items.Length); Assert.Null(session.Combat.Capture().Campaign);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void BurningDeathsAdvanceEquippedGodwroughtOnlyForAnUnspentBody(bool previouslyResurrected, int expectedKills)
    {
        var session = Fresh(); Assert.True(session.EnterAct(1).Success); var snapshot = session.Capture();
        long ash = snapshot.Production.Progression.Character.Items.Single(i => i.DefinitionId == "item.ashcleaver").Id;
        snapshot.Production.Progression.Character.Equipment[EquipmentSlot.MainHand] = ash;
        foreach (var combat in new[] { snapshot.Production.Expedition.Combat, snapshot.Combat })
        {
            combat.Equipment["MainHand"] = ash; combat.Build = combat.Build with { AshcleaverEquipped = true };
        }
        var victim = snapshot.Combat.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Role == "Melee"); victim.Health = 1;
        long action = snapshot.Combat.NextActionId++;
        victim.Statuses.Add(new CombatStatus { Id = "Burning", SourceId = 1, OwnerId = 1, FragmentId = "fragment.eye_vael", ActionId = action, NextTick = snapshot.Combat.Tick, ExpiresTick = snapshot.Combat.Tick + 90, Depth = 1 });
        // Gravewake's actual revival sets this consumed-body receipt; test its subsequent lethal burn at the production boundary.
        if (previouslyResurrected) snapshot.Combat.ConsumedCorpseIds.Add(victim.Id);
        session = Restore(snapshot); var result = session.Step();
        Assert.Contains(result.CombatEvents, e => e.Kind == "EntityKilled" && e.TargetId == victim.Id);
        Assert.Equal(expectedKills, session.Production.Capture().Progression.Character.Items.Single(i => i.Id == ash).BurningKills);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }

    [Theory]
    [InlineData("state")]
    [InlineData("state.campaign")]
    [InlineData("state.combat")]
    [InlineData("state.production")]
    [InlineData("state.production.expedition")]
    [InlineData("state.production.expedition.combat")]
    [InlineData("state.production.progression.character")]
    public void FutureNestedArchiveSchemasWithUnknownFieldsArePreserved(string location)
    {
        string folder = Path.Combine(Path.GetTempPath(), "ashenwake-campaign-future-" + Guid.NewGuid().ToString("N")), path = Path.Combine(folder, "campaign.save.json");
        try
        {
            var session = Fresh(); CampaignRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, session.Capture());
            var root = JsonNode.Parse(File.ReadAllText(path))!; JsonNode node = root;
            foreach (string part in location.Split('.')) node = node[part]!;
            node["schemaVersion"] = 999; node["futureUnknownField"] = "preserve me";
            string newer = root.ToJsonString(); File.WriteAllText(path, newer);
            Assert.Throws<SaveCompatibilityException>(() => CampaignRuntimeSaveStore.Load(path, CombatJson, Adventure, Policy, Campaign));
            Assert.Throws<SaveCompatibilityException>(() => CampaignRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, session.Capture()));
            Assert.Equal(newer, File.ReadAllText(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void AtomicCampaignArchiveRecoversBackupAndRejectsReservedProfilePath()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ashenwake-campaign-backup-" + Guid.NewGuid().ToString("N")), path = Path.Combine(folder, "campaign.save.json");
        try
        {
            var session = Fresh(); string initial = session.StateHash;
            CampaignRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, session.Capture()); session.Step();
            CampaignRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, session.Capture()); File.WriteAllText(path, "{truncated");
            var restored = CampaignRuntimeSaveStore.Load(path, CombatJson, Adventure, Policy, Campaign); Assert.True(restored.RecoveredBackup); Assert.Equal(initial, restored.Session.StateHash);
            Assert.Throws<ArgumentException>(() => CampaignRuntimeSaveStore.Write(Path.Combine(folder, "profile.json"), CombatJson, Adventure, Policy, Campaign, session.Capture()));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void ActualPhaseThreeHubFixtureImportsRetainedPowerIntoFreshCampaignWithoutChangingOriginal()
    {
        string original = Read("fixtures/phase3-production-hub.json");
        string baseline = Read("fixtures/combat-phase3.json");
        var adventure = AdventureContent.Parse(Read("fixtures/adventure-phase3.json")); var policy = ProgressionContent.Parse(Read("fixtures/progression-phase3.json"));
        var old = JsonData.Read<ProductionSave>(original).State;
        var imported = CampaignRuntimeMigration.ImportPhaseThree(original, baseline, CombatJson, adventure, policy, Campaign);
        Assert.True(imported.InHub); Assert.Empty(imported.Capture().Campaign.CompletedEncounters); Assert.Empty(imported.Capture().Campaign.Choices);
        Assert.Equal(old.Progression.Character.Experience, imported.Production.ProgressionView.Experience); Assert.Equal(old.Progression.Character.Materials, imported.Production.ProgressionView.Materials);
        Assert.Equal(JsonData.Hash(old.Progression.Character.Items), JsonData.Hash(imported.Production.Capture().Progression.Character.Items));
        Assert.Equal(JsonData.Hash(old.Progression.Character.Mastery), JsonData.Hash(imported.Production.Capture().Progression.Character.Mastery));
        Assert.Equal(original, Read("fixtures/phase3-production-hub.json"));
        Assert.Equal(imported.StateHash, CampaignRuntimeSession.Restore(CombatJson, adventure, policy, Campaign, imported.Capture()).StateHash);
        var corrupted = JsonNode.Parse(original)!; corrupted["state"]!["progression"]!["character"]!["materials"] = 123;
        Assert.Throws<InvalidDataException>(() => CampaignRuntimeMigration.ImportPhaseThree(corrupted.ToJsonString(), baseline, CombatJson, adventure, policy, Campaign));
    }

    [Fact]
    public void MaintainedPhaseThreeMigrationFixtureBytesMatchTheirManifest()
    {
        using var manifest = System.Text.Json.JsonDocument.Parse(Read("fixtures/phase3-migration-manifest.json"));
        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        var files = manifest.RootElement.GetProperty("files").EnumerateObject().ToArray(); Assert.Equal(4, files.Length);
        foreach (var fixture in files)
        {
            Assert.Equal(Path.GetFileName(fixture.Name), fixture.Name);
            byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", fixture.Name));
            Assert.Equal(fixture.Value.GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
        }
    }
}
