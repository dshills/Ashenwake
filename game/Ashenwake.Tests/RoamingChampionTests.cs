using System.Text.Json.Nodes;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class RoamingChampionTests
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
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && result.Count < 3; i++)
        {
            foreach (var entry in session.RoamingChampions.Entries.Where(e => e.Here))
                if (!result.ContainsKey(entry.Id)) result.Add(entry.Id, session.Capture());
            if (result.Count == 3) break;
            var command = CampaignRuntimeSmoke.Next(session.Campaign);
            var step = session.ExecuteCampaign(command); Assert.True(step.Success, step.Reason + " / " + command);
        }
        Assert.Equal(3, result.Count); return result;
    }
    private static EndgameRuntimeSession Source(string id = "champion.pilgrim") => Restore(Sources.Value[id]);
    private static void Until(EndgameRuntimeSession session, string id, Func<bool> done, int maximum = 8000)
    {
        for (int i = 0; i < maximum && !done(); i++)
        {
            var command = RoamingChampionSmoke.Next(session, id); var result = session.Execute(command);
            Assert.True(result.Success, result.Reason + " / " + command);
            Assert.NotEqual("Failed", session.RoamingChampions.Run?.Stage);
        }
        Assert.True(done(), "Champion route stalled at " + session.RoamingChampions.Run);
    }
    private static void Enter(EndgameRuntimeSession session, string id = "champion.pilgrim")
        => Until(session, id, () => session.RoamingChampions.Run?.Stage == "Foyer");

    [Fact]
    public void UnusedFeatureIsOmittedAndInvalidCommandsCannotAlterFreshCharacter()
    {
        var s = EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame);
        Assert.Null(s.Capture().RoamingChampions); Assert.DoesNotContain("roamingChampions", JsonData.Write(s.Capture()));
        Assert.Empty(s.RoamingChampions.Entries); string hash = s.StateHash;
        Assert.False(s.EnterRoamingChampion("champion.pilgrim").Success); Assert.False(s.ChallengeRoamingChampion().Success);
        Assert.False(s.ClaimRoamingChampionReward().Success); Assert.False(s.ExitRoamingChampion().Success);
        Assert.Equal(hash, s.StateHash); Assert.Equal(hash, Restore(s.Capture()).StateHash);
    }
    [Fact]
    public void SourceIsDeterministicVariesBySeedAndEverySightingHasPhysicalClearance()
    {
        var catalog = CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json"));
        foreach (var d in RoamingChampionCatalog.Definitions)
        {
            Assert.NotEqual(RoamingChampionCatalog.SourceEncounter(d, 42), RoamingChampionCatalog.SourceEncounter(d, 43));
            Assert.Contains(RoamingChampionCatalog.SourceEncounter(d, ulong.MaxValue), d.SourceEncounterIds);
            foreach (string source in d.SourceEncounterIds)
            {
                var room = CombatSession.CreateEncounter(catalog.CombatJson, 42, source).Room;
                var point = RoamingChampionCatalog.SightingPosition;
                Assert.InRange(point.X, -room.HalfWidth + 500, room.HalfWidth - 500);
                Assert.InRange(point.Z, -room.HalfDepth + 500, room.HalfDepth - 500);
                Assert.DoesNotContain(room.Obstacles, o => point.X >= o.MinX - 500 && point.X <= o.MaxX + 500 && point.Z >= o.MinZ - 500 && point.Z <= o.MaxZ + 500);
            }
        }
    }
    [Fact]
    public void DiscoveryRequiresSecuredSourceAndProximityAndReadingViewsDoesNotMutate()
    {
        var s = Source(); string hash = s.StateHash;
        var entry = s.RoamingChampions.Entries.Single(); Assert.True(entry.Here); Assert.False(entry.InReach);
        Assert.False(entry.Discovered); Assert.Single(s.Interactions, i => i.ActionId.EndsWith(".sighting", StringComparison.Ordinal));
        Assert.False(s.EnterRoamingChampion(entry.Id).Success); Assert.Equal(hash, s.StateHash);
        for (int i = 0; i < 600 && !s.RoamingChampions.Entries.Single().Discovered; i++)
            Assert.True(s.Execute(RoamingChampionSmoke.Approach(s, RoamingChampionCatalog.SightingPosition, new(EndgameRuntimeAction.Tick))).Success);
        Assert.True(s.RoamingChampions.Entries.Single().Discovered); Assert.False(s.InRoamingChampion);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.ReturnToHub)).Success);
        var known = s.RoamingChampions.Entries.Single(); Assert.True(known.Discovered); Assert.False(known.Here);
        Assert.False(s.EnterRoamingChampion(known.Id).Success);
    }
    [Theory]
    [InlineData("champion.pilgrim")]
    [InlineData("champion.rootwidow")]
    [InlineData("champion.tithekeeper")]
    public void EarnedVictoryClaimsSignatureOncePreservesCampaignAndReplays(string id)
    {
        var s = Source(id); var d = RoamingChampionCatalog.Find(id)!;
        Until(s, id, () => Position.DistanceSquared(s.Combat.View.Actors.Single(a => a.Id == 1).Position, RoamingChampionCatalog.SightingPosition) <= 1700L * 1700);
        var original = s.Campaign.Capture(); int items = s.Production.Capture().Progression.Character.Items.Length;
        Enter(s, id); Assert.Equal("championarena.foyer", s.Combat.EncounterId); Assert.DoesNotContain(s.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy);
        Assert.False(s.ClaimRoamingChampionReward().Success); Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Until(s, id, () => s.RoamingChampions.Run?.Stage == "Combat"); Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Until(s, id, () => s.RoamingChampions.Run?.Stage == "Victory");
        Assert.Empty(s.Combat.View.Loot); Assert.Equal(items, s.Production.Capture().Progression.Character.Items.Length);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Until(s, id, () => s.RoamingChampions.Run?.Stage == "Claimed");
        var character = s.Production.Capture().Progression.Character;
        Assert.Single(character.Items, i => i.DefinitionId == d.RewardItemId); Assert.Equal(items + 1, character.Items.Length);
        Assert.Equal(character.Items.Length, character.Items.Select(i => i.Id).Distinct().Count());
        string hash = s.StateHash; Assert.False(s.ClaimRoamingChampionReward().Success); Assert.Equal(hash, s.StateHash);
        Assert.Equal(hash, Restore(s.Capture()).StateHash); Assert.True(s.ExitRoamingChampion().Success);
        Assert.Equal(JsonData.Hash(original.Campaign), JsonData.Hash(s.Campaign.Capture().Campaign));
        Assert.Equal(JsonData.Hash(original.Combat.Loot), JsonData.Hash(s.Campaign.Combat.Capture().Loot));
        Assert.Equal(original.ActiveEncounterId, s.Campaign.ActiveEncounterId);
        Assert.Equal(original.Combat.Actors.Single(a => a.Id == 1).Position, s.Combat.View.Actors.Single(a => a.Id == 1).Position);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(Combat, Adventure, Policy, Campaign, Endgame, s.CaptureReplay()); Assert.True(replay.Success, replay.ToString());
        Assert.True(s.EnterRoamingChampion(id).Success); Assert.Equal("Claimed", s.RoamingChampions.Run!.Stage);
        Assert.False(s.ChallengeRoamingChampion().Success); Assert.False(s.ClaimRoamingChampionReward().Success);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
    }
    [Theory]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void EveryOtherDisciplineCanEarnAllThreeChampionVictories(string discipline)
    {
        var s = EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame, discipline: discipline);
        var defeated = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && defeated.Count < RoamingChampionCatalog.Definitions.Count; i++)
        {
            var source = s.RoamingChampions.Entries.FirstOrDefault(e => e.Here && !defeated.Contains(e.Id));
            if (source is not null)
            {
                Until(s, source.Id, () => s.RoamingChampions.Run?.Stage == "Victory");
                Assert.Empty(s.Combat.View.Loot);
                Assert.True(s.Combat.View.Actors.Single(a => a.Id == 1).Health > 0, discipline + " died against " + source.Id);
                Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
                Until(s, source.Id, () => s.RoamingChampions.Run?.Stage == "Claimed");
                Assert.Single(s.Production.Capture().Progression.Character.Items, item => item.DefinitionId == source.RewardItemId);
                Assert.True(s.ExitRoamingChampion().Success); defeated.Add(source.Id);
                continue;
            }
            var command = CampaignRuntimeSmoke.Next(s.Campaign); var outcome = s.ExecuteCampaign(command);
            Assert.True(outcome.Success, discipline + " / " + s.Campaign.ActiveEncounterId + " / " + outcome.Reason + " / " + command);
        }
        Assert.Equal(RoamingChampionCatalog.Definitions.Count, defeated.Count);
        Assert.Equal(discipline, s.Production.Capture().Progression.Character.Discipline);
        Assert.Equal(3, s.Capture().RoamingChampions!.Claimed.Length);
    }
    [Fact]
    public void RetreatRestoresExactSourceIncludingUncollectedLootAndAllowsRetry()
    {
        var s = Source(); Until(s, "champion.pilgrim", () => Position.DistanceSquared(s.Combat.View.Actors.Single(a => a.Id == 1).Position, RoamingChampionCatalog.SightingPosition) <= 1700L * 1700);
        var original = s.Campaign.Capture(); Assert.NotEmpty(original.Combat.Loot);
        Enter(s); Assert.True(s.ExitRoamingChampion().Success); Assert.Equal(JsonData.Hash(original), JsonData.Hash(s.Campaign.Capture()));
        Enter(s); Until(s, "champion.pilgrim", () => s.RoamingChampions.Run?.Stage == "Combat");
        for (int i = 0; i < 20; i++) Assert.True(s.Step().Success);
        Assert.True(s.ExitRoamingChampion().Success); Assert.Equal(JsonData.Hash(original), JsonData.Hash(s.Campaign.Capture()));
        Assert.Empty(s.Capture().RoamingChampions!.Claimed); Enter(s); Assert.Equal("Foyer", s.RoamingChampions.Run!.Stage);
    }
    [Fact]
    public void DeathCanExitWithoutRewardAndRetryAtSameSighting()
    {
        var s = Source(); Enter(s); Until(s, "champion.pilgrim", () => s.RoamingChampions.Run?.Stage == "Combat");
        string original = JsonData.Hash(s.Campaign.Capture());
        for (int i = 0; i < 8000 && s.RoamingChampions.Run!.Stage != "Failed"; i++) Assert.True(s.Step().Success);
        Assert.Equal("Failed", s.RoamingChampions.Run!.Stage); Assert.False(s.ClaimRoamingChampionReward().Success);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash); Assert.True(s.ExitRoamingChampion().Success);
        Assert.Equal(original, JsonData.Hash(s.Campaign.Capture())); Assert.Empty(s.Capture().RoamingChampions!.Claimed);
        Enter(s); Assert.Equal("Foyer", s.RoamingChampions.Run!.Stage);
    }
    [Fact]
    public void WonUnclaimedTreasureSurvivesExitAndSaveWithoutRefight()
    {
        var s = Source(); Enter(s); Until(s, "champion.pilgrim", () => s.RoamingChampions.Run?.Stage == "Victory");
        Assert.True(s.ExitRoamingChampion().Success); var restored = Restore(s.Capture());
        Assert.True(restored.RoamingChampions.Entries.Single().Defeated); Assert.True(restored.EnterRoamingChampion("champion.pilgrim").Success);
        Assert.Equal("Victory", restored.RoamingChampions.Run!.Stage); Assert.DoesNotContain(restored.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy);
        Assert.Equal(restored.StateHash, Restore(restored.Capture()).StateHash);
        Until(restored, "champion.pilgrim", () => restored.RoamingChampions.Run?.Stage == "Claimed");
    }
    [Fact]
    public void ChallengeBlocksOtherJourneysPermanentChangesStashAndTraining()
    {
        var s = Source(); Enter(s); string hash = s.StateHash;
        Assert.False(s.StartRegionalHunt("hunt.regional.pallbearer").Success); Assert.False(s.StartGodHunt("hunt.false_vael").Success);
        Assert.False(s.EnterSecretChamber("secret.belfry").Success); Assert.False(s.ExecuteCampaign(new(CampaignRuntimeAction.ReturnToHub)).Success);
        Assert.False(s.ExecuteProduction(new(ProductionAction.Passive, Id: "Defense")).Success);
        Assert.False(s.ExecuteProduction(new(ProductionAction.StoreItem, ItemId: 1, Id: "stash.1")).Success); Assert.False(s.Stash.CanUse);
        foreach (var kind in new[] { CombatCommandKind.Equip, CombatCommandKind.EquipFragment, CombatCommandKind.UnequipFragment, CombatCommandKind.SetMutation })
            Assert.False(s.Step(new CombatCommand(kind)).Success);
        Assert.Throws<InvalidOperationException>(() => s.CreateTrainingSession()); Assert.Equal(hash, s.StateHash); Assert.Null(s.LocalMap);
    }
    [Fact]
    public void ForgedDiscoverySeedVictoryAndMissingRewardLedgerAreRejected()
    {
        var s = Source(); Enter(s); Until(s, "champion.pilgrim", () => s.RoamingChampions.Run?.Stage == "Combat"); var snapshot = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { RoamingChampions = snapshot.RoamingChampions! with { Active = snapshot.RoamingChampions.Active! with { Stage = "Victory" } } }));
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { RoamingChampions = snapshot.RoamingChampions! with { Defeated = [null!] } }));
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { RoamingChampions = snapshot.RoamingChampions! with { Discovered = ["champion.rootwidow"] } }));
        ulong forgedSeed = snapshot.RoamingChampions!.Active!.Seed ^ 1UL;
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with
        {
            RoamingChampions = snapshot.RoamingChampions! with
            { Active = snapshot.RoamingChampions.Active! with { Seed = forgedSeed }, Combat = snapshot.RoamingChampions.Combat! with { Seed = forgedSeed } }
        }));
        Until(s, "champion.pilgrim", () => s.RoamingChampions.Run?.Stage == "Claimed"); Assert.True(s.ExitRoamingChampion().Success); var claimed = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(claimed with { RoamingChampions = null }));
        Assert.Throws<InvalidDataException>(() => Restore(claimed with { RoamingChampions = claimed.RoamingChampions! with { Claimed = [] } }));
    }
    private static void Visit(EndgameRuntimeSession s, string interaction)
    {
        var target = s.Interactions.Single(i => i.ActionId == interaction);
        for (int i = 0; i < 500; i++)
        {
            var player = s.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, target.Position) <= (long)(target.Range - 100) * (target.Range - 100)) return;
            var direction = CombatProductionSmoke.MovementDirection(player.Position, target.Position, s.Room);
            Assert.True(s.Step(new CombatCommand(CombatCommandKind.Move, X: direction.X, Z: direction.Z)).Success);
        }
        Assert.Fail("Could not approach " + interaction);
    }
    [Fact]
    public void StashedOwnershipStaysExcludedDuringFightAndSignatureCanBeStoredWithoutLosingReceipt()
    {
        var s = Source("champion.rootwidow"); Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.ReturnToHub)).Success);
        long stored = s.Production.ProgressionView.Equipment[EquipmentSlot.Head]; Visit(s, "service.torren");
        Assert.True(s.ExecuteProduction(new(ProductionAction.Unequip, Slot: EquipmentSlot.Head)).Success);
        Visit(s, PersonalStashCatalog.InteractionId); Assert.True(s.ExecuteProduction(new(ProductionAction.StoreItem, ItemId: stored, Id: "stash.2")).Success);
        string storedFields = JsonData.Hash(s.Production.Capture().Progression.Character.Items.Single(i => i.Id == stored));
        Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.EnterAct, Act: 2)).Success);
        if (s.Campaign.ActiveEncounterId != "campaign.living_ruins")
        {
            for (int i = 0; i < 2000 && !s.Campaign.EncounterCleared; i++)
                Assert.True(s.ExecuteCampaign(CampaignRuntimeSmoke.Next(s.Campaign)).Success);
            Visit(s, "verdant.back.ruins");
            Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.InteractVerdant, Id: "verdant.back.ruins")).Success);
        }
        Assert.True(s.RoamingChampions.Entries.Single(e => e.Id == "champion.rootwidow").Here);
        Enter(s, "champion.rootwidow"); Assert.DoesNotContain(s.Combat.View.Inventory, i => i.Id == stored);
        Until(s, "champion.rootwidow", () => s.RoamingChampions.Run?.Stage == "Claimed");
        Assert.DoesNotContain(s.Combat.View.Inventory, i => i.Id == stored); Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Assert.True(s.ExitRoamingChampion().Success); Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.ReturnToHub)).Success);
        Visit(s, PersonalStashCatalog.InteractionId);
        var reward = s.Production.Capture().Progression.Character.Items.Single(i => i.DefinitionId == "item.broodkeepers_knot");
        Assert.True(s.ExecuteProduction(new(ProductionAction.SetItemFavorite, ItemId: reward.Id, Value: "true")).Success);
        Assert.True(s.ExecuteProduction(new(ProductionAction.SetItemLocked, ItemId: reward.Id, Value: "true")).Success);
        string rewardFields = JsonData.Hash(s.Production.Capture().Progression.Character.Items.Single(i => i.Id == reward.Id));
        Assert.True(s.ExecuteProduction(new(ProductionAction.StoreItem, ItemId: reward.Id, Id: "stash.3")).Success);
        s = Restore(s.Capture()); Assert.True(s.RoamingChampions.Entries.Single().Claimed);
        Assert.Equal(storedFields, JsonData.Hash(s.Production.Capture().Progression.Character.Items.Single(i => i.Id == stored)));
        Assert.Equal(rewardFields, JsonData.Hash(s.Production.Capture().Progression.Character.Items.Single(i => i.Id == reward.Id)));
        Assert.True(s.ExecuteProduction(new(ProductionAction.RetrieveItem, ItemId: reward.Id)).Success);
        Assert.Equal(rewardFields, JsonData.Hash(s.Production.Capture().Progression.Character.Items.Single(i => i.Id == reward.Id)));
    }
    [Fact]
    public void FullBackpackBlocksChallengeAtomicallyBeforeAnyRewardIsEarned()
    {
        var s = Source(); Enter(s); Visit(s, "champion.pilgrim.challenge"); var state = s.Capture();
        // A validated capacity boundary fixture fills ownership and its combat projections with ordinary helmets.
        var owner = state.Campaign.Production.Progression.Character;
        var template = owner.Items.First(i => i.DefinitionId == "item.starter_head");
        long next = new[] { owner.NextItemId, state.Campaign.Combat.NextObjectId, state.Campaign.Production.Expedition.Combat.NextObjectId, state.RoamingChampions!.Combat!.NextObjectId }.Max();
        var added = Enumerable.Range(0, CharacterStash.BackpackCapacity - CharacterStash.BackpackCount(owner))
            .Select(index => template with { Id = next + index }).ToArray();
        owner.Items = [.. owner.Items, .. added]; owner.NextItemId = next + added.Length;
        CombatSnapshot Fill(CombatSnapshot combat)
        {
            var helmet = combat.Inventory.First(i => i.Id == template.Id);
            return combat with { Inventory = [.. combat.Inventory, .. added.Select(i => helmet with { Id = i.Id })], NextObjectId = Math.Max(combat.NextObjectId, owner.NextItemId) };
        }
        var expedition = state.Campaign.Production.Expedition;
        var campaign = state.Campaign with { Combat = Fill(state.Campaign.Combat), Production = state.Campaign.Production with { Expedition = expedition with { Combat = Fill(expedition.Combat) } } };
        s = Restore(state with { Campaign = campaign, RoamingChampions = state.RoamingChampions! with { Combat = Fill(state.RoamingChampions.Combat!) } });
        string hash = s.StateHash; Assert.False(s.RoamingChampions.Run!.CanChallenge); Assert.False(s.ChallengeRoamingChampion().Success);
        Assert.Equal(hash, s.StateHash); Assert.Empty(s.Capture().RoamingChampions!.Defeated); Assert.Empty(s.Capture().RoamingChampions!.Claimed);
    }
    [Fact]
    public void ActiveChallengeSaveStoreRoundTrips()
    {
        var s = Source(); Enter(s); string dir = Path.Combine(Path.GetTempPath(), "ashenwake-champion-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "character.json"); EndgameRuntimeSaveStore.Write(path, Combat, Adventure, Policy, Campaign, Endgame, s.Capture());
            Assert.Equal(s.StateHash, EndgameRuntimeSaveStore.Load(path, Combat, Adventure, Policy, Campaign, Endgame).Session.StateHash);
            var json = JsonNode.Parse(File.ReadAllText(path))!;
            json["state"]!["roamingChampions"]!["schemaVersion"] = 999;
            File.WriteAllText(path, json.ToJsonString()); string futureSave = File.ReadAllText(path);
            Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Load(path, Combat, Adventure, Policy, Campaign, Endgame));
            Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Write(path, Combat, Adventure, Policy, Campaign, Endgame, s.Capture()));
            Assert.Equal(futureSave, File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }
}
