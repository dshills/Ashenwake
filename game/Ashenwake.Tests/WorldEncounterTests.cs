using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class WorldEncounterTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly AdventureContent Adventure = AdventureContent.Parse(Read("adventure.json"));
    private static readonly ProgressionContent Policy = ProgressionContent.Parse(Read("progression.json"));
    private static readonly CampaignContent Campaign = CampaignContent.Parse(Read("campaign.json"));
    private static readonly EndgameContent Endgame = EndgameContent.Parse(Read("endgame.json"));
    private static readonly string Combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson;
    private static readonly Lazy<Dictionary<string, EndgameRuntimeSnapshot>> Sources = new(EarnSources);
    private static EndgameRuntimeSession Restore(EndgameRuntimeSnapshot snapshot) => EndgameRuntimeSession.Restore(Combat, Adventure, Policy, Campaign, Endgame, snapshot);
    private static Dictionary<string, EndgameRuntimeSnapshot> EarnSources()
    {
        var result = new Dictionary<string, EndgameRuntimeSnapshot>();
        var session = EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame);
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && result.Count < 8; i++)
        {
            foreach (var definition in WorldEncounterCatalog.Definitions)
                if (!session.InHub && session.Campaign.ActiveEncounterId == definition.SourceEncounterId && session.Campaign.EncounterCleared && !result.ContainsKey(definition.Id))
                    result.Add(definition.Id, session.Capture());
            if (result.Count == 8) break;
            var command = CampaignRuntimeSmoke.Next(session.Campaign);
            var step = session.ExecuteCampaign(command); Assert.True(step.Success, step.Reason + " / " + command);
        }
        Assert.Equal(8, result.Count); return result;
    }
    private static EndgameRuntimeSession Source(string id = "event.lantern") => Restore(Sources.Value[id]);
    private static void Until(EndgameRuntimeSession session, string id, Func<bool> done, int maximum = 6000)
    {
        for (int i = 0; i < maximum && !done(); i++)
        {
            var command = WorldEncounterSmoke.Next(session, id); var result = session.Execute(command);
            Assert.True(result.Success, result.Reason + " / " + command);
            Assert.NotEqual("Failed", session.WorldEncounters.Run?.Stage);
        }
        Assert.True(done(), "World encounter route stalled at " + session.WorldEncounters.Run);
    }
    private static void Enter(EndgameRuntimeSession session, string id = "event.lantern")
        => Until(session, id, () => session.WorldEncounters.Run?.Stage == "Foyer");
    [Fact]
    public void LegacySnapshotUnchangedAndEventsDoNotSpoilFutureRegions()
    {
        var s = EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame);
        Assert.Null(s.Capture().WorldEncounters); Assert.DoesNotContain("worldEncounters", JsonData.Write(s.Capture()));
        Assert.Empty(s.WorldEncounters.Entries); string hash = s.StateHash;
        Assert.False(s.EnterWorldEncounter("event.lantern").Success); Assert.False(s.ChooseWorldEncounter("rescue").Success);
        Assert.Equal(hash, s.StateHash); Assert.Equal(hash, Restore(s.Capture()).StateHash);
    }
    [Theory]
    [InlineData("event.lantern")]
    [InlineData("event.caravan")]
    [InlineData("event.shrine")]
    [InlineData("storm.grey_march")]
    [InlineData("storm.verdant")]
    [InlineData("storm.cinder")]
    [InlineData("storm.spine")]
    [InlineData("storm.hollow")]
    public void EarnedOutcomeClaimsOnceAndRetainsCampaignAndReplay(string id)
    {
        var s = Source(id); var d = WorldEncounterCatalog.Find(id)!;
        int items = s.Production.Capture().Progression.Character.Items.Count(i => i.DefinitionId == d.RewardItemId);
        int materials = s.Production.ProgressionView.Materials;
        Enter(s, id); var campaign = s.Campaign.Capture();
        Assert.False(s.ClaimWorldEncounterReward().Success); Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        var earlier = s.Capture(); string earlierHash = JsonData.Hash(earlier);
        Until(s, id, () => s.WorldEncounters.Run?.Stage == "Victory");
        Assert.Equal(earlierHash, JsonData.Hash(earlier));
        Assert.Empty(s.Combat.View.Loot); Assert.Equal(items, s.Production.Capture().Progression.Character.Items.Count(i => i.DefinitionId == d.RewardItemId));
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Until(s, id, () => s.WorldEncounters.Run?.Stage == "Claimed");
        Assert.Equal(items + 1, s.Production.Capture().Progression.Character.Items.Count(i => i.DefinitionId == d.RewardItemId));
        Assert.Equal(materials + WorldEncounterCatalog.Materials(id, s.WorldEncounters.Run!.Outcome), s.Production.ProgressionView.Materials);
        string hash = s.StateHash; Assert.False(s.ClaimWorldEncounterReward().Success); Assert.Equal(hash, s.StateHash);
        Assert.Equal(hash, Restore(s.Capture()).StateHash); Assert.True(s.ExitWorldEncounter().Success);
        Assert.Equal(JsonData.Hash(campaign.Campaign), JsonData.Hash(s.Campaign.Capture().Campaign));
        Assert.Equal(JsonData.Hash(campaign.Combat.Loot), JsonData.Hash(s.Combat.Capture().Loot));
        Assert.Equal(campaign.Combat.Actors.Single(a => a.Id == 1).Position, s.Combat.View.Actors.Single(a => a.Id == 1).Position);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(Combat, Adventure, Policy, Campaign, Endgame, s.CaptureReplay()); Assert.True(replay.Success, replay.ToString());
        Assert.True(s.EnterWorldEncounter(id).Success); Assert.Equal("Claimed", s.WorldEncounters.Run!.Stage);
        Assert.False(s.ChooseWorldEncounter("stabilize").Success); Assert.False(s.ClaimWorldEncounterReward().Success);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
    }
    [Fact]
    public void CaravanPuzzleOrderAndPartialProgressPersistWhileCombatRouteOffersHigherReward()
    {
        var s = Source("event.caravan"); Enter(s, "event.caravan");
        for (int i = 0; i < 300 && !s.WorldEncounters.Run!.CanChoose; i++) Assert.True(s.Execute(WorldEncounterSmoke.Approach(s, WorldEncounterCatalog.ChoicePosition, new(EndgameRuntimeAction.Tick))).Success);
        string hash = s.StateHash; Assert.False(s.ChooseWorldEncounter("return").Success); Assert.False(s.ChooseWorldEncounter("ring").Success); Assert.Equal(hash, s.StateHash);
        Assert.True(s.ChooseWorldEncounter("extinguish").Success); Assert.Equal(1, s.WorldEncounters.Run!.PuzzleStep);
        s = Restore(s.Capture()); Assert.Equal(1, s.WorldEncounters.Run!.PuzzleStep);
        Assert.True(s.ExitWorldEncounter().Success); Enter(s, "event.caravan"); Assert.Equal(1, s.WorldEncounters.Run!.PuzzleStep);
        int materials = s.Production.ProgressionView.Materials;
        for (int i = 0; i < 6000 && s.WorldEncounters.Run?.Stage != "Claimed"; i++)
        {
            var result = s.Execute(WorldEncounterSmoke.Next(s, "event.caravan", caravanCombat: true)); Assert.True(result.Success, result.Reason);
            Assert.NotEqual("Failed", s.WorldEncounters.Run?.Stage);
        }
        Assert.Equal("Claimed", s.WorldEncounters.Run?.Stage); Assert.Equal("Combat", s.WorldEncounters.Run!.Outcome);
        Assert.Equal(materials + 30, s.Production.ProgressionView.Materials); Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
    }
    [Fact]
    public void BuildAndOuterCommandsAreFrozenAndAbandonReturnsUntouchedRoom()
    {
        var s = Source(); Enter(s); string campaign = JsonData.Hash(s.Campaign.Capture()); string hash = s.StateHash;
        Assert.False(s.ExecuteCampaign(new(CampaignRuntimeAction.ReturnToHub)).Success);
        Assert.False(s.ExecuteProduction(new(ProductionAction.Passive, Id: "passive.offense")).Success);
        Assert.False(s.EnterSecretChamber("secret.belfry").Success); Assert.False(s.EnterRoamingChampion("champion.pilgrim").Success);
        Assert.False(s.StartRegionalHunt("hunt.grey_march").Success); Assert.Null(s.LocalMap);
        foreach (var kind in new[] { CombatCommandKind.Equip, CombatCommandKind.EquipFragment, CombatCommandKind.UnequipFragment, CombatCommandKind.SetMutation })
            Assert.False(s.Step(new CombatCommand(kind)).Success);
        Assert.Throws<InvalidOperationException>(() => s.CreateTrainingSession()); Assert.Equal(hash, s.StateHash);
        Until(s, "event.lantern", () => s.WorldEncounters.Run?.Stage == "Combat");
        for (int i = 0; i < 10; i++) Assert.True(s.Step().Success);
        Assert.True(s.ExitWorldEncounter().Success); Assert.Equal(campaign, JsonData.Hash(s.Campaign.Capture()));
        Assert.Empty(s.Capture().WorldEncounters!.Claimed); Assert.Empty(s.Capture().WorldEncounters!.Completed);
    }
    [Fact]
    public void DeathAndUnclaimedVictoryAllowSafeExitAndResume()
    {
        var s = Source(); Enter(s); string campaign = JsonData.Hash(s.Campaign.Capture());
        Until(s, "event.lantern", () => s.WorldEncounters.Run?.Stage == "Combat");
        for (int i = 0; i < 6000 && s.WorldEncounters.Run!.Stage != "Failed"; i++) Assert.True(s.Step().Success);
        Assert.Equal("Failed", s.WorldEncounters.Run!.Stage); Assert.False(s.ClaimWorldEncounterReward().Success);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash); Assert.True(s.ExitWorldEncounter().Success); Assert.Equal(campaign, JsonData.Hash(s.Campaign.Capture()));
        Enter(s); Until(s, "event.lantern", () => s.WorldEncounters.Run?.Stage == "Victory");
        Assert.True(s.ExitWorldEncounter().Success); s = Restore(s.Capture()); Assert.True(s.EnterWorldEncounter("event.lantern").Success);
        Assert.Equal("Victory", s.WorldEncounters.Run!.Stage); Until(s, "event.lantern", () => s.WorldEncounters.Run?.Stage == "Claimed");
    }
    [Fact]
    public void ForgedOutcomeSeedMissingLedgerAndConcurrentModesAreRejected()
    {
        var s = Source(); Enter(s); var snapshot = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { WorldEncounters = snapshot.WorldEncounters! with { Active = snapshot.WorldEncounters.Active! with { Stage = "Victory" } } }));
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { WorldEncounters = snapshot.WorldEncounters! with { Discovered = [null!] } }));
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { WorldEncounters = snapshot.WorldEncounters! with { Completed = new() { ["event.lantern"] = "Puzzle" } } }));
        ulong seed = snapshot.WorldEncounters!.Active!.Seed ^ 1UL;
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { WorldEncounters = snapshot.WorldEncounters with { Active = snapshot.WorldEncounters.Active with { Seed = seed }, Combat = snapshot.WorldEncounters.Combat! with { Seed = seed } } }));
        Until(s, "event.lantern", () => s.WorldEncounters.Run?.Stage == "Claimed"); var claimed = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(claimed with { WorldEncounters = null }));
        Assert.Throws<InvalidDataException>(() => Restore(claimed with { WorldEncounters = claimed.WorldEncounters! with { Claimed = [] } }));
    }

    [Fact]
    public void BackpackCapacityPreventsCommitmentWithoutLosingProgress()
    {
        var s = Source(); Enter(s);
        for (int i = 0; i < 300 && !s.WorldEncounters.Run!.CanChoose; i++) Assert.True(s.Execute(WorldEncounterSmoke.Approach(s, WorldEncounterCatalog.ChoicePosition, new(EndgameRuntimeAction.Tick))).Success);
        var state = s.Capture(); var owner = state.Campaign.Production.Progression.Character;
        var template = owner.Items.First(i => i.DefinitionId == "item.starter_head");
        long next = new[] { owner.NextItemId, state.Campaign.Combat.NextObjectId, state.Campaign.Production.Expedition.Combat.NextObjectId, state.WorldEncounters!.Combat!.NextObjectId }.Max();
        var added = Enumerable.Range(0, CharacterStash.BackpackCapacity - CharacterStash.BackpackCount(owner)).Select(index => template with { Id = next + index }).ToArray();
        owner.Items = [.. owner.Items, .. added]; owner.NextItemId = next + added.Length;
        CombatSnapshot Fill(CombatSnapshot combat)
        {
            var helmet = combat.Inventory.First(i => i.Id == template.Id);
            return combat with { Inventory = [.. combat.Inventory, .. added.Select(i => helmet with { Id = i.Id })], NextObjectId = Math.Max(combat.NextObjectId, owner.NextItemId) };
        }
        var expedition = state.Campaign.Production.Expedition;
        var campaign = state.Campaign with { Combat = Fill(state.Campaign.Combat), Production = state.Campaign.Production with { Expedition = expedition with { Combat = Fill(expedition.Combat) } } };
        s = Restore(state with { Campaign = campaign, WorldEncounters = state.WorldEncounters! with { Combat = Fill(state.WorldEncounters.Combat!) } });
        string hash = s.StateHash; Assert.False(s.WorldEncounters.Run!.CanChoose); Assert.False(s.ChooseWorldEncounter("rescue").Success);
        Assert.Equal(hash, s.StateHash); Assert.Empty(s.Capture().WorldEncounters!.Completed); Assert.Empty(s.Capture().WorldEncounters!.Claimed);
        Assert.True(s.ExitWorldEncounter().Success);
    }
    [Fact]
    public void CaravanSecondStepAndActiveSaveFileRoundTrip()
    {
        var s = Source("event.caravan"); Enter(s, "event.caravan");
        Until(s, "event.caravan", () => s.WorldEncounters.Run!.PuzzleStep == 2);
        string hash = s.StateHash; Assert.False(s.ChooseWorldEncounter("relight").Success); Assert.False(s.ChooseWorldEncounter("extinguish").Success); Assert.Equal(hash, s.StateHash);
        string dir = Path.Combine(Path.GetTempPath(), "ashenwake-world-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "character.json"); EndgameRuntimeSaveStore.Write(path, Combat, Adventure, Policy, Campaign, Endgame, s.Capture());
            s = EndgameRuntimeSaveStore.Load(path, Combat, Adventure, Policy, Campaign, Endgame).Session;
            Assert.Equal(hash, s.StateHash); Assert.Equal(2, s.WorldEncounters.Run!.PuzzleStep);
            Until(s, "event.caravan", () => s.WorldEncounters.Run?.Stage == "Claimed"); Assert.Equal("Puzzle", s.WorldEncounters.Run!.Outcome);
        }
        finally { Directory.Delete(dir, true); }
    }
}
