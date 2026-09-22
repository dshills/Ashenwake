using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Production;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignPacingMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign(bool old) => CampaignContent.Parse(Read(old ? "fixtures/campaign-hollow.json" : "campaign.json"));
    private static readonly Lazy<string> PreviousCombat = new(() => Compose(true));
    private static readonly Lazy<string> CurrentCombat = new(() => Compose(false));
    private static string Compose(bool old) => CampaignCombatContent.Parse(Read("combat.json"), Read(old ? "fixtures/campaign-combat-hollow.json" : "campaign-combat.json")).CombatJson;
    private static string Combat(bool old) => old ? PreviousCombat.Value : CurrentCombat.Value;
    private static CampaignRuntimeSession RestoreOld(CampaignRuntimeSnapshot state) => CampaignRuntimeSession.Restore(Combat(true), Adventure, Policy, Campaign(true), state);
    private static string Save(CampaignRuntimeSession session) => JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture()));
    private static CampaignRuntimeSession Upgrade(string json) => CampaignRuntimeSaveStore.Read(Combat(false), Adventure, Policy, Campaign(false), json);
    private static readonly Lazy<CampaignRuntimeSnapshot[]> Checkpoints = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat(true), Adventure, Policy, Campaign(true));
        Assert.True(session.EnableExplorationMap().Success);
        var checkpoints = new List<CampaignRuntimeSnapshot> { session.Capture() };
        for (int i = 0; checkpoints.Count < 16 && i < 64000; i++)
        {
            var result = session.Execute(CampaignRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason);
            if (session.Capture().Campaign.CompletedEncounters.Count == checkpoints.Count) checkpoints.Add(session.Capture());
        }
        Assert.Equal(16, checkpoints.Count);
        return checkpoints.ToArray();
    });

    [Fact]
    public void PublishedPacingPreservesCampaignBudgetAndMakesLevelGainsGradual()
    {
        var current = Campaign(false).Capture().Acts.SelectMany(act => act.Encounters).ToArray();
        var previous = Campaign(true).Capture().Acts.SelectMany(act => act.Encounters).ToArray();
        Assert.Equal(previous.Select(e => e.Id), current.Select(e => e.Id));
        Assert.Equal(new[] { 50, 100, 175, 175, 200, 300, 300, 325, 400, 400, 425, 500, 550, 650, 600 }, current.Select(e => e.Experience));
        Assert.Equal(5150, current.Sum(e => e.Experience));
        Assert.Equal(previous.Select(e => e.Materials), current.Select(e => e.Materials));
        var progression = ProgressionSession.Create(Policy);
        int previousTotal = 0, previousLevel = 1;
        var milestones = new Dictionary<string, int>
        {
            ["campaign.bell_saint"] = 3,
            ["campaign.rootheart"] = 5,
            ["campaign.furnace_spindle"] = 6,
            ["campaign.covenant_warden"] = 8,
            ["campaign.repeating_rooms"] = 9,
            ["campaign.identity_memory"] = 10
        };
        for (int i = 0; i < current.Length; i++)
        {
            var encounter = current[i]; previousTotal += previous[i].Experience;
            Assert.True(progression.EarnExperience(encounter.Id, encounter.Experience).Success);
            Assert.InRange(progression.Level - previousLevel, 0, 1);
            Assert.True(progression.View.Experience >= previousTotal);
            if (milestones.TryGetValue(encounter.Id, out int expectedLevel)) Assert.Equal(expectedLevel, progression.Level);
            if (progression.Level < 10) Assert.Empty(progression.View.UltimateSkills);
            else Assert.Contains("skill.cataclysm", progression.View.UltimateSkills);
            if (encounter.Id == "campaign.identity_memory") Assert.Equal(previousTotal, progression.View.Experience);
            previousLevel = progression.Level;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void EveryPublishedCampaignPrefixRebindsReceiptsAndTopUpsExperienceExactlyOnce(int completed)
    {
        var original = RestoreOld(Checkpoints.Value[completed]); var before = original.Capture();
        string json = Save(original);
        var migrated = Upgrade(json); var after = migrated.Capture();
        var earned = Campaign(false).Capture().Acts.SelectMany(act => act.Encounters).Where(e => before.Campaign.CompletedEncounters.Contains(e.Id)).ToArray();
        long difference = earned.Sum(e => e.Experience) - before.Campaign.EarnedExperience;
        Assert.InRange(difference, 0, 550);
        Assert.Equal(before.Production.Progression.Character.Experience + difference, after.Production.Progression.Character.Experience);
        Assert.Equal(earned.Sum(e => e.Experience), after.Campaign.EarnedExperience);
        Assert.Equal(before.Campaign.EarnedMaterials, after.Campaign.EarnedMaterials);
        Assert.Equal(before.Production.Progression.Character.Materials, after.Production.Progression.Character.Materials);
        Assert.Equal(JsonData.Hash(before.Production.Progression.Character.Items), JsonData.Hash(after.Production.Progression.Character.Items));
        Assert.Equal(JsonData.Hash(before.Production.Progression.Profile), JsonData.Hash(after.Production.Progression.Profile));
        Assert.Equal(JsonData.Hash(before.Campaign.Choices), JsonData.Hash(after.Campaign.Choices));
        Assert.Equal(JsonData.Hash(before.ExplorationMap), JsonData.Hash(after.ExplorationMap));
        foreach (var encounter in earned)
            Assert.Equal(JsonData.Hash(new { Action = "Experience", amount = encounter.Experience, materials = encounter.Materials }),
                after.Production.Progression.Character.OperationReceipts["campaign.encounter." + encounter.Id]);
        Assert.Equal(before.Production.Progression.Character.OperationReceipts.Keys, after.Production.Progression.Character.OperationReceipts.Keys);
        Assert.Equal(migrated.Production.ProgressionView.Level, after.Production.Expedition.Combat.ProgressionBuild.Level);
        Assert.Equal(migrated.Production.ProgressionView.Level, after.Combat.ProgressionBuild.Level);
        AssertRuntimePreserved(before.Combat, after.Combat);
        AssertRuntimePreserved(before.Production.Expedition.Combat, after.Production.Expedition.Combat);
        foreach (var pair in before.ClearedRooms ?? [])
        {
            var cached = after.ClearedRooms![pair.Key];
            Assert.Equal(migrated.Production.ProgressionView.Level, cached.ProgressionBuild.Level);
            AssertRuntimePreserved(pair.Value, cached);
        }
        Assert.Equal(migrated.StateHash, Upgrade(json).StateHash);
        Assert.Equal(migrated.StateHash, Upgrade(Save(migrated)).StateHash);
        Assert.Equal(json, Save(original));
    }

    private static void AssertRuntimePreserved(CombatSnapshot before, CombatSnapshot after)
    {
        Assert.Equal(before.Tick, after.Tick); Assert.Equal(before.Rng, after.Rng);
        Assert.Equal(before.NextActionId, after.NextActionId); Assert.Equal(before.NextObjectId, after.NextObjectId);
        Assert.Equal(JsonData.Hash(before.Actors), JsonData.Hash(after.Actors));
        Assert.Equal(JsonData.Hash(before.Projectiles), JsonData.Hash(after.Projectiles));
        Assert.Equal(JsonData.Hash(before.Areas), JsonData.Hash(after.Areas));
        Assert.Equal(JsonData.Hash(before.Loot), JsonData.Hash(after.Loot));
        Assert.Equal(JsonData.Hash(before.Inventory), JsonData.Hash(after.Inventory));
        Assert.Equal(JsonData.Hash(before.Equipment), JsonData.Hash(after.Equipment));
        Assert.Equal(JsonData.Hash(before.BufferedCommand), JsonData.Hash(after.BufferedCommand));
        Assert.Equal(JsonData.Hash(before.Cooldowns), JsonData.Hash(after.Cooldowns));
        Assert.Equal(JsonData.Hash(before.Campaign), JsonData.Hash(after.Campaign));
    }

    [Fact]
    public void LiveCastAndOldReplaySurviveThePartialCampaignUpgrade()
    {
        var original = RestoreOld(Checkpoints.Value[3]);
        for (int i = 0; i < 4000 && (original.ActiveEncounterId != "campaign.living_ruins" || original.Combat.Capture().Actors.Single(a => a.Id == 1).Pending is null); i++)
            Assert.True(original.Execute(CampaignRuntimeSmoke.Next(original)).Success);
        Assert.Equal("campaign.living_ruins", original.ActiveEncounterId);
        Assert.NotNull(original.Combat.Capture().Actors.Single(a => a.Id == 1).Pending);
        var before = original.Capture(); var replay = original.CaptureReplay();
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(true), Adventure, Policy, Campaign(true), replay).Success);
        var migrated = Upgrade(Save(original));
        Assert.Equal(before.Production.Progression.Character.Experience + 50, migrated.Production.ProgressionView.Experience);
        AssertRuntimePreserved(before.Combat, migrated.Capture().Combat);
        Assert.True(migrated.Step().Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), migrated.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData(12345)]
    [InlineData(122500)]
    public void ImportedExperienceIsNeverReducedAndTopUpRespectsTheExistingLevelCap(long experience)
    {
        var before = JsonData.Copy(Checkpoints.Value[3]);
        before.Production.Progression.Character.Experience = experience;
        var rules = ProductionContent.Resolve(Combat(true), CampaignRuntimeSession.ResolvePolicy(Policy, Campaign(true)), Adventure).Capture();
        long maximumExperience = (long)rules.ExperiencePerLevel * rules.LevelCap * (rules.LevelCap - 1) / 2;
        int level = 1;
        while (level < rules.LevelCap && experience >= (long)rules.ExperiencePerLevel * level * (level + 1) / 2) level++;
        var build = before.Combat.ProgressionBuild with { Level = level, UltimateUnlocked = level >= 10 };
        before = before with
        {
            Combat = before.Combat with { ProgressionBuild = build },
            Production = before.Production with
            {
                Expedition = before.Production.Expedition with
                { Combat = before.Production.Expedition.Combat with { ProgressionBuild = build } }
            }
        };
        // This archive represents a valid character imported with earlier earned XP;
        // ordinary old-catalog Restore authenticates both permanent and combat projections.
        var original = RestoreOld(before);
        var migrated = Upgrade(Save(original));
        Assert.Equal(Math.Min(maximumExperience, experience + 50), migrated.Production.ProgressionView.Experience);
        Assert.True(migrated.Production.ProgressionView.Experience >= experience);
        Assert.Equal(JsonData.Hash(before.Production.Progression.Character.Items), JsonData.Hash(migrated.Capture().Production.Progression.Character.Items));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AValidHistoricalProductionProjectionKeepsItsInventoryOrderAndSubset(bool removeUnequipped)
    {
        var source = JsonData.Copy(Checkpoints.Value[13]);
        var projection = source.Production.Expedition.Combat;
        Assert.True(projection.Inventory.Count > 2);
        long? omittedId = null;
        if (removeUnequipped)
        {
            var omitted = projection.Inventory.First(item => !projection.Equipment.Values.Contains(item.Id) && item.DefinitionId != "item.ashcleaver");
            omittedId = omitted.Id;
            projection.Inventory.Remove(omitted);
        }
        projection.Inventory.Reverse();
        // The active campaign arena must match its permanent owner's projection.
        source.Combat.Inventory.Clear(); source.Combat.Inventory.AddRange(projection.Inventory);
        var original = RestoreOld(source); // Ordinary old Restore authenticates this valid projection.
        var upgraded = Upgrade(Save(original)); var after = upgraded.Capture();
        Assert.Equal(source.Production.Progression.Character.Experience + 550, after.Production.Progression.Character.Experience);
        AssertRuntimePreserved(projection, after.Production.Expedition.Combat);
        AssertRuntimePreserved(source.Combat, after.Combat);
        Assert.Equal(JsonData.Hash(source.Production.Progression.Character.Items), JsonData.Hash(after.Production.Progression.Character.Items));
        if (omittedId is not null)
        {
            Assert.DoesNotContain(after.Production.Expedition.Combat.Inventory, item => item.Id == omittedId);
            Assert.Contains(after.Production.Progression.Character.Items, item => item.Id == omittedId);
        }
        Assert.Equal(upgraded.StateHash, Upgrade(Save(upgraded)).StateHash);
    }

    [Fact]
    public void SaveAndProfileAreReadOnlyUntilAnExplicitWriteKeepsTheOriginalBackup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-pacing-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = RestoreOld(Checkpoints.Value[3]);
            CampaignRuntimeSaveStore.Write(path, Combat(true), Adventure, Policy, Campaign(true), original.Capture());
            string profilePath = CampaignRuntimeSaveStore.ProfilePath(path), oldSave = File.ReadAllText(path), oldProfile = File.ReadAllText(profilePath);
            var migrated = CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign(false));
            Assert.False(migrated.RecoveredBackup); Assert.Equal(oldSave, File.ReadAllText(path)); Assert.Equal(oldProfile, File.ReadAllText(profilePath));
            CampaignRuntimeSaveStore.Write(path, Combat(false), Adventure, Policy, Campaign(false), migrated.Session.Capture());
            Assert.Equal(oldSave, File.ReadAllText(path + ".bak")); Assert.Equal(oldProfile, File.ReadAllText(profilePath + ".bak"));
            Assert.Equal(migrated.Session.StateHash, CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign(false)).Session.StateHash);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("receipt")]
    [InlineData("total")]
    [InlineData("build")]
    [InlineData("unknown-catalog")]
    public void ARecomputedChecksumCannotBypassOriginalCatalogValidation(string tampering)
    {
        var node = JsonNode.Parse(Save(RestoreOld(Checkpoints.Value[3])))!;
        switch (tampering)
        {
            case "checksum": node["stateHash"] = new string('0', 64); break;
            case "receipt":
                node["state"]!["production"]!["progression"]!["character"]!["operationReceipts"]!["campaign.encounter.campaign.bell_saint"] =
                JsonData.Hash(new { Action = "Experience", amount = 175, materials = 15 }); break;
            case "total": node["state"]!["campaign"]!["earnedExperience"] = 325; break;
            case "build": node["state"]!["combat"]!["progressionBuild"]!["level"] = 99; break;
            case "unknown-catalog": node["state"]!["campaign"]!["contentHash"] = new string('0', 64); break;
        }
        if (tampering != "checksum") node["stateHash"] = JsonData.Hash(JsonData.Read<CampaignRuntimeSnapshot>(node["state"]!.ToJsonString()));
        if (tampering == "unknown-catalog") Assert.Throws<SaveCompatibilityException>(() => Upgrade(node.ToJsonString()));
        else Assert.Throws<InvalidDataException>(() => Upgrade(node.ToJsonString()));
    }

    [Fact]
    public void PhaseFourImportAndActiveEndgameAndEchoesArchivesKeepTheirProgressionAndCombat()
    {
        var endgame = EndgameContent.Parse(Read("endgame.json")); var experiment = ExperimentContent.Parse(Read("experiments.json"));
        string oldCombat = EndgameCombatContent.Parse(Combat(true), Read("endgame-combat.json"), endgame).CombatJson;
        string newCombat = EndgameCombatContent.Parse(Combat(false), Read("endgame-combat.json"), endgame).CombatJson;
        var original = EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), Combat(true), oldCombat, Adventure, Policy, Campaign(true), endgame);
        var imported = EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), Combat(true), newCombat, Adventure, Policy, Campaign(false), endgame);
        Assert.Equal(original.Production.ProgressionView.Experience, imported.Production.ProgressionView.Experience);
        Assert.Equal(JsonData.Hash(original.Production.Capture().Progression.Character.Items), JsonData.Hash(imported.Production.Capture().Progression.Character.Items));
        var echoes = ExperimentRuntimeSession.FromEndgame(oldCombat, Adventure, Policy, Campaign(true), endgame, experiment, original.Capture());
        var upgradedEchoes = ExperimentSaveStore.Read(newCombat, Adventure, Policy, Campaign(false), endgame, experiment,
            JsonData.Write(new ExperimentSave(1, echoes.StateHash, echoes.Capture())));
        Assert.Equal(Campaign(false).Hash, upgradedEchoes.Capture().Endgame.Campaign.Campaign.ContentHash);
        Assert.True(upgradedEchoes.Step().Success);
        Assert.True(ExperimentReplayRunner.Run(newCombat, Adventure, Policy, Campaign(false), endgame, experiment, upgradedEchoes.CaptureReplay()).Success);
        for (int i = 0; i < 4000 && original.View.Run is null; i++)
        {
            var request = original.View.AvailableSigils.Length == 0
                ? new EndgameRuntimeCommand(EndgameRuntimeAction.ClaimRecoverySigil)
                : new EndgameRuntimeCommand(EndgameRuntimeAction.StartFracture, SigilId: original.View.AvailableSigils[0].Id);
            Assert.True(original.Execute(EndgameRuntimeSmoke.AtGate(original, request)).Success);
        }
        Assert.NotNull(original.View.Run);
        Assert.True(original.Step().Success);
        var before = original.Capture();
        var migrated = EndgameRuntimeSaveStore.Read(newCombat, Adventure, Policy, Campaign(false), endgame,
            JsonData.Write(new EndgameRuntimeSave(1, original.StateHash, before)));
        Assert.Equal(JsonData.Hash(before.Endgame), JsonData.Hash(migrated.Capture().Endgame));
        AssertRuntimePreserved(before.Combat!, migrated.Capture().Combat!);
        Assert.Equal(original.Production.ProgressionView.Experience, migrated.Production.ProgressionView.Experience);
        Assert.True(migrated.Step().Success);
        Assert.True(EndgameRuntimeReplayRunner.Run(newCombat, Adventure, Policy, Campaign(false), endgame, migrated.CaptureReplay()).Success);
    }
}
