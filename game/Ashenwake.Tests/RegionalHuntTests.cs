using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class RegionalHuntTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly AdventureContent Adventure = AdventureContent.Parse(Read("adventure.json"));
    private static readonly ProgressionContent Policy = ProgressionContent.Parse(Read("progression.json"));
    private static readonly CampaignContent Campaign = CampaignContent.Parse(Read("campaign.json"));
    private static readonly EndgameContent Endgame = EndgameContent.Parse(Read("endgame.json"));
    private static readonly string Combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson;
    private static readonly string Previous = CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson;
    private static EndgameRuntimeSession Import() => EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), Previous, Combat, Adventure, Policy, Campaign, Endgame);
    private static EndgameRuntimeSession Restore(EndgameRuntimeSnapshot state) => EndgameRuntimeSession.Restore(Combat, Adventure, Policy, Campaign, Endgame, state);
    private static void Until(EndgameRuntimeSession session, string id, Func<bool> done, int maximum = 5000)
    {
        for (int i = 0; i < maximum && !done(); i++)
        {
            var command = RegionalHuntSmoke.Next(session, id); var result = session.Execute(command);
            Assert.True(result.Success, result.Reason + " / " + command);
            Assert.NotEqual("Failed", session.RegionalHunts.Run?.Stage);
        }
        Assert.True(done(), "Hunt stalled: " + session.RegionalHunts.Run);
    }
    private static void Start(EndgameRuntimeSession session, string id)
        => Until(session, id, () => session.RegionalHunts.Run?.Stage == "Tracking");
    private static string First => RegionalHuntCatalog.Contracts[0].Id;

    [Fact]
    public void UnusedFeaturePreservesOldArchiveBytesAndFreshCharacterLocksAllContracts()
    {
        var s = EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame);
        Assert.Null(s.Capture().RegionalHunts); Assert.DoesNotContain("regionalHunts", System.Text.Json.JsonSerializer.Serialize(s.Capture(), JsonData.Options));
        string hash = s.StateHash;
        Assert.All(s.RegionalHunts.Contracts, c => { Assert.False(c.Unlocked); Assert.Equal("", c.RewardItemId); });
        Assert.False(s.StartRegionalHunt(First).Success); Assert.Equal(hash, s.StateHash);
        Assert.Equal(hash, Restore(s.Capture()).StateHash);
    }
    [Theory]
    [InlineData("hunt.regional.pallbearer")]
    [InlineData("hunt.regional.briarwidow")]
    [InlineData("hunt.regional.kilnmaw")]
    public void ActualVictoryClaimsOneLegendaryAndMaterialsWithExactReplay(string id)
    {
        var s = Import(); var contract = RegionalHuntCatalog.Find(id)!;
        int items = s.Production.Capture().Progression.Character.Items.Count(i => i.DefinitionId == contract.RewardItemId);
        int materials = s.Production.ProgressionView.Materials;
        Until(s, id, () => s.RegionalHunts.Run?.Stage == "Victory");
        Assert.Equal(materials, s.Production.ProgressionView.Materials); Assert.Empty(s.Combat.View.Loot);
        Assert.False(s.ClaimRegionalHuntReward().Success);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Until(s, id, () => s.RegionalHunts.Run?.Stage == "Claimed");
        Assert.Equal(materials + contract.Materials, s.Production.ProgressionView.Materials);
        Assert.Equal(items + 1, s.Production.Capture().Progression.Character.Items.Count(i => i.DefinitionId == contract.RewardItemId));
        string hash = s.StateHash; Assert.False(s.ClaimRegionalHuntReward().Success); Assert.Equal(hash, s.StateHash);
        Assert.Equal(hash, Restore(s.Capture()).StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(Combat, Adventure, Policy, Campaign, Endgame, s.CaptureReplay());
        Assert.True(replay.Success, replay.ToString());
    }
    [Fact]
    public void CluesRequireOrderAndPhysicalProximityAndPersistHalfway()
    {
        var s = Import(); Start(s, First); string hash = s.StateHash;
        Assert.False(s.TrackRegionalHuntClue(First + ".clue.2").Success);
        Assert.False(s.TrackRegionalHuntClue(First + ".clue.1").Success); Assert.Equal(hash, s.StateHash);
        Until(s, First, () => s.RegionalHunts.Run?.TrackedClues == 1);
        Assert.Equal("Tracking", s.RegionalHunts.Run!.Stage);
        Assert.Single(s.Interactions); Assert.Equal(First + ".clue.2", s.Interactions[0].ActionId);
        var restored = Restore(s.Capture()); Assert.Equal(s.StateHash, restored.StateHash);
        Until(restored, First, () => restored.RegionalHunts.Run?.Stage == "Combat");
        Assert.Equal(3, restored.RegionalHunts.Run!.TrackedClues); Assert.Empty(restored.Interactions);
    }
    [Fact]
    public void AbandonDuringCombatForfeitsAllRewardsAndNewRunHasDistinctIdentity()
    {
        var s = Import(); string inventory = JsonData.Hash(s.Production.Capture().Progression.Character.Items);
        Until(s, First, () => s.RegionalHunts.Run?.Stage == "Combat");
        long id = s.RegionalHunts.Run!.Id; ulong seed = s.Capture().RegionalHunts!.Run!.Seed;
        Assert.True(s.AbandonRegionalHunt().Success); Assert.True(s.InHub);
        Assert.Equal(inventory, JsonData.Hash(s.Production.Capture().Progression.Character.Items));
        Assert.Empty(s.Capture().RegionalHunts!.Rewards); Assert.False(s.ClaimRegionalHuntReward().Success);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Start(s, First); Assert.Equal(id + 1, s.RegionalHunts.Run!.Id); Assert.NotEqual(seed, s.Capture().RegionalHunts!.Run!.Seed);
    }
    [Fact]
    public void ActiveContractBlocksNestedTravelPermanentBuildAndTraining()
    {
        var s = Import(); Start(s, First); string hash = s.StateHash;
        Assert.False(s.ExecuteCampaign(new(CampaignRuntimeAction.EnterAct, Act: 1)).Success);
        Assert.False(s.ExecuteProduction(new(ProductionAction.Passive, Id: "Defense")).Success);
        Assert.False(s.StartGodHunt("hunt.false_vael").Success); Assert.False(s.ReturnToHub().Success);
        Assert.Throws<InvalidOperationException>(() => s.CreateTrainingSession()); Assert.Equal(hash, s.StateHash);
        Assert.False(s.ReturnRegionalHunt().Success); Assert.Null(s.LocalMap);
    }
    [Fact]
    public void DeathPaysNothingRejectsRetryAndAllowsExplicitReturn()
    {
        var s = Import(); Until(s, First, () => s.RegionalHunts.Run?.Stage == "Combat");
        string inventory = JsonData.Hash(s.Production.Capture().Progression.Character.Items);
        int materials = s.Production.ProgressionView.Materials;
        for (int i = 0; i < 6000 && s.RegionalHunts.Run!.Stage != "Failed"; i++) Assert.True(s.Step().Success);
        Assert.Equal("Failed", s.RegionalHunts.Run!.Stage); Assert.False(s.RetryEncounter().Success);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Assert.True(s.ReturnRegionalHunt().Success); Assert.True(s.InHub); Assert.False(s.ClaimRegionalHuntReward().Success);
        Assert.Equal(inventory, JsonData.Hash(s.Production.Capture().Progression.Character.Items)); Assert.Equal(materials, s.Production.ProgressionView.Materials);
        Assert.Equal("Abandoned", s.RegionalHunts.Run!.Stage); Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
    }
    [Fact]
    public void ReturnBeforeClaimPreservesRealVictoryAndClaimCannotBeRepeatedAfterRestore()
    {
        var s = Import(); Until(s, First, () => s.RegionalHunts.Run?.Stage == "Victory");
        Assert.True(s.ReturnRegionalHunt().Success); Assert.True(s.InHub); Assert.True(s.HasUnresolvedRegionalHunt);
        var restored = Restore(s.Capture());
        foreach (var kind in new[] { CombatCommandKind.Equip, CombatCommandKind.EquipFragment, CombatCommandKind.UnequipFragment, CombatCommandKind.SetMutation })
        {
            string blockedHash = restored.StateHash;
            Assert.False(restored.Step(new CombatCommand(kind)).Success); Assert.Equal(blockedHash, restored.StateHash);
        }
        Assert.True(restored.Step(new CombatCommand(CombatCommandKind.Cast, SkillId: "skill.iron_guard")).Success);
        Assert.Equal(restored.StateHash, Restore(restored.Capture()).StateHash);
        Assert.False(restored.ExecuteCampaign(new(CampaignRuntimeAction.EnterAct, Act: 1)).Success);
        Until(restored, First, () => restored.RegionalHunts.Run?.Stage == "Claimed");
        Assert.False(Restore(restored.Capture()).ClaimRegionalHuntReward().Success);
    }
    [Fact]
    public void ForgedVictoryAndMissingRewardLedgerAreRejected()
    {
        var s = Import(); Until(s, First, () => s.RegionalHunts.Run?.Stage == "Combat"); var before = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(before with { RegionalHunts = before.RegionalHunts! with { Run = before.RegionalHunts.Run! with { Stage = "Victory" } } }));
        Until(s, First, () => s.RegionalHunts.Run?.Stage == "Claimed"); var won = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(won with { RegionalHunts = null }));
        Assert.Throws<InvalidDataException>(() => Restore(won with { RegionalHunts = won.RegionalHunts! with { Rewards = [] } }));
    }
    [Fact]
    public void RepeatableContractMakesTwoSeparateClaimsAndBoundedHistoryRejectsMalformedData()
    {
        var s = Import(); Until(s, First, () => s.RegionalHunts.Run?.Stage == "Claimed");
        Start(s, First); Until(s, First, () => s.RegionalHunts.Run?.Stage == "Claimed");
        Assert.Equal(2, s.RegionalHunts.Contracts.Single(c => c.Id == First).Completed);
        Assert.Equal(2, s.Capture().RegionalHunts!.Rewards.Length);
        var snapshot = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { RegionalHunts = snapshot.RegionalHunts! with { NextRunId = 1002 } }));
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { RegionalHunts = snapshot.RegionalHunts! with { Rewards = [snapshot.RegionalHunts.Rewards[0], snapshot.RegionalHunts.Rewards[0]] } }));
    }
    [Fact]
    public void TrackingArchiveRoundTripsThroughCheckedSaveStore()
    {
        var s = Import(); Start(s, First);
        string dir = Path.Combine(Path.GetTempPath(), "ashenwake-hunt-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "character.json"); EndgameRuntimeSaveStore.Write(path, Combat, Adventure, Policy, Campaign, Endgame, s.Capture());
            Assert.Equal(s.StateHash, EndgameRuntimeSaveStore.Load(path, Combat, Adventure, Policy, Campaign, Endgame).Session.StateHash);
        }
        finally { Directory.Delete(dir, true); }
    }
}
