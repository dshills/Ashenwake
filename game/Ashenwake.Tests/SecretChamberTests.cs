using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class SecretChamberTests
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
            foreach (var definition in SecretChamberCatalog.Definitions)
                if (!session.InHub && session.Campaign.ActiveEncounterId == definition.SourceEncounterId && session.Campaign.EncounterCleared && !result.ContainsKey(definition.Id))
                    result.Add(definition.Id, session.Capture());
            if (result.Count == 3) break;
            var command = CampaignRuntimeSmoke.Next(session.Campaign);
            var step = session.ExecuteCampaign(command); Assert.True(step.Success, step.Reason + " / " + command);
        }
        Assert.Equal(3, result.Count); return result;
    }
    private static EndgameRuntimeSession Source(string id = "secret.belfry") => Restore(Sources.Value[id]);
    private static void Until(EndgameRuntimeSession session, string id, Func<bool> done, int maximum = 6000)
    {
        for (int i = 0; i < maximum && !done(); i++)
        {
            var command = SecretChamberSmoke.Next(session, id); var result = session.Execute(command);
            Assert.True(result.Success, result.Reason + " / " + command);
            Assert.NotEqual("Failed", session.SecretChambers.Run?.Stage);
        }
        Assert.True(done(), "Secret chamber route stalled at " + session.SecretChambers.Run);
    }
    private static void Reveal(EndgameRuntimeSession session, string id = "secret.belfry")
        => Until(session, id, () => session.SecretChambers.Entries.Single(e => e.Id == id).Revealed);
    private static void Enter(EndgameRuntimeSession session, string id = "secret.belfry")
        => Until(session, id, () => session.SecretChambers.Run?.Stage == "Foyer");

    [Fact]
    public void UnusedFeatureIsOmittedAndFreshCharacterHasNoSpoilerEntries()
    {
        var s = EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame);
        Assert.Null(s.Capture().SecretChambers); Assert.DoesNotContain("secretChambers", JsonData.Write(s.Capture()));
        Assert.Empty(s.SecretChambers.Entries); string hash = s.StateHash;
        Assert.False(s.EnterSecretChamber("secret.belfry").Success); Assert.False(s.ResolveSecretClue("secret.belfry.puzzle.1", "Keep silence").Success);
        Assert.Equal(hash, s.StateHash); Assert.Equal(hash, Restore(s.Capture()).StateHash);
    }
    [Fact]
    public void PuzzleRequiresCorrectOrderProximityAndHintedResponseAndPersistsProgress()
    {
        var s = Source(); var clue = SecretChamberCatalog.Find("secret.belfry")!.Clues[0];
        Assert.False(s.SecretChambers.Entries[0].Revealed); Assert.Equal("An unmarked passage", s.SecretChambers.Entries[0].Name);
        Assert.False(s.ResolveSecretClue("secret.belfry.puzzle.2", "Remove the clapper").Success);
        Assert.False(s.ResolveSecretClue(clue.Id, clue.Solution).Success);
        for (int i = 0; i < 300 && !s.SecretChambers.Entries[0].Clue!.InReach; i++)
            Assert.True(s.Execute(SecretChamberSmoke.Approach(s, clue.Position, new(EndgameRuntimeAction.ResolveSecretClue, Id: clue.Id, Value: clue.Solution))).Success);
        string hash = s.StateHash; var wrong = s.ResolveSecretClue(clue.Id, clue.Choices[1]);
        Assert.False(wrong.Success); Assert.Contains(clue.Hint, wrong.Reason); Assert.Equal(hash, s.StateHash);
        Assert.True(s.ResolveSecretClue(clue.Id, clue.Solution).Success);
        var restored = Restore(s.Capture()); Assert.Equal(1, restored.SecretChambers.Entries[0].PuzzleStep);
        Reveal(restored); Assert.Equal("Unrung Belfry", restored.SecretChambers.Entries[0].Name);
        Assert.Equal(restored.StateHash, Restore(restored.Capture()).StateHash);
    }
    [Theory]
    [InlineData("secret.belfry")]
    [InlineData("secret.nest")]
    [InlineData("secret.furnace")]
    public void ActualGuardianVictoryClaimsUniqueItemOncePreservesCampaignAndReplays(string id)
    {
        var s = Source(id); var definition = SecretChamberCatalog.Find(id)!;
        int items = s.Production.Capture().Progression.Character.Items.Count(i => i.DefinitionId == definition.RewardItemId);
        Reveal(s, id); var original = s.Campaign.Capture();
        Enter(s, id); Assert.Equal("secretarena.foyer", s.Combat.EncounterId); Assert.DoesNotContain(s.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy);
        Assert.False(s.ClaimSecretTreasure().Success); Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Until(s, id, () => s.SecretChambers.Run?.Stage == "Victory");
        Assert.Empty(s.Combat.View.Loot); Assert.Equal(items, s.Production.Capture().Progression.Character.Items.Count(i => i.DefinitionId == definition.RewardItemId));
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Until(s, id, () => s.SecretChambers.Run?.Stage == "Claimed");
        Assert.Equal(items + 1, s.Production.Capture().Progression.Character.Items.Count(i => i.DefinitionId == definition.RewardItemId));
        string hash = s.StateHash; Assert.False(s.ClaimSecretTreasure().Success); Assert.Equal(hash, s.StateHash);
        Assert.Equal(hash, Restore(s.Capture()).StateHash); Assert.True(s.ExitSecretChamber().Success);
        Assert.Equal(JsonData.Hash(original.Campaign), JsonData.Hash(s.Campaign.Capture().Campaign));
        Assert.Equal(JsonData.Hash(original.Combat.Loot), JsonData.Hash(s.Campaign.Combat.Capture().Loot));
        Assert.Equal(original.ActiveEncounterId, s.Campaign.ActiveEncounterId);
        Assert.Equal(original.Combat.Actors.Single(a => a.Id == 1).Position, s.Combat.View.Actors.Single(a => a.Id == 1).Position);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(Combat, Adventure, Policy, Campaign, Endgame, s.CaptureReplay()); Assert.True(replay.Success, replay.ToString());
        Assert.True(s.EnterSecretChamber(id).Success); Assert.Equal("Claimed", s.SecretChambers.Run!.Stage);
        Assert.False(s.ChallengeSecretGuardian().Success); Assert.False(s.ClaimSecretTreasure().Success);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
    }
    [Fact]
    public void FoyerAndAbandonedCombatReturnExactRoomIncludingUncollectedLoot()
    {
        var s = Source(); Reveal(s); var original = s.Campaign.Capture(); Assert.NotEmpty(original.Combat.Loot);
        Enter(s); Assert.True(s.ExitSecretChamber().Success); Assert.Equal(JsonData.Hash(original), JsonData.Hash(s.Campaign.Capture()));
        Enter(s); Until(s, "secret.belfry", () => s.SecretChambers.Run?.Stage == "Combat");
        for (int i = 0; i < 20; i++) Assert.True(s.Step().Success);
        Assert.True(s.ExitSecretChamber().Success); Assert.Equal(JsonData.Hash(original), JsonData.Hash(s.Campaign.Capture()));
        Assert.Empty(s.Capture().SecretChambers!.Claimed); Assert.True(s.SecretChambers.Entries[0].Revealed);
    }
    [Fact]
    public void DeathCanExitAnywhereWithoutRewardAndSafelyRetry()
    {
        var s = Source(); Reveal(s); string original = JsonData.Hash(s.Campaign.Capture());
        Enter(s); Until(s, "secret.belfry", () => s.SecretChambers.Run?.Stage == "Combat");
        for (int i = 0; i < 6000 && s.SecretChambers.Run!.Stage != "Failed"; i++) Assert.True(s.Step().Success);
        Assert.Equal("Failed", s.SecretChambers.Run!.Stage); Assert.False(s.ClaimSecretTreasure().Success);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash); Assert.True(s.ExitSecretChamber().Success);
        Assert.Equal(original, JsonData.Hash(s.Campaign.Capture())); Assert.Empty(s.Capture().SecretChambers!.Claimed);
        Enter(s); Assert.Equal("Foyer", s.SecretChambers.Run!.Stage);
    }
    [Fact]
    public void UnclaimedVictoryPersistsAcrossExitSaveAndReentryWithoutRefighting()
    {
        var s = Source(); Enter(s); Until(s, "secret.belfry", () => s.SecretChambers.Run?.Stage == "Victory");
        Assert.True(s.ExitSecretChamber().Success); var restored = Restore(s.Capture());
        Assert.True(restored.SecretChambers.Entries.Single(e => e.Id == "secret.belfry").Defeated);
        Assert.True(restored.EnterSecretChamber("secret.belfry").Success);
        Assert.Equal("Victory", restored.SecretChambers.Run!.Stage);
        Assert.DoesNotContain(restored.Combat.View.Actors, a => a.Faction == CombatFaction.Enemy);
        Assert.Equal(restored.StateHash, Restore(restored.Capture()).StateHash);
        Until(restored, "secret.belfry", () => restored.SecretChambers.Run?.Stage == "Claimed");
        Assert.Single(restored.Capture().SecretChambers!.Claimed);
    }

    [Fact]
    public void ChamberBlocksOtherJourneysBuildChangesAndTraining()
    {
        var s = Source(); Enter(s); string hash = s.StateHash;
        Assert.False(s.StartRegionalHunt("hunt.regional.pallbearer").Success); Assert.False(s.StartGodHunt("hunt.false_vael").Success);
        Assert.False(s.ExecuteCampaign(new(CampaignRuntimeAction.ReturnToHub)).Success);
        Assert.False(s.ExecuteProduction(new(ProductionAction.Passive, Id: "Defense")).Success);
        foreach (var kind in new[] { CombatCommandKind.Equip, CombatCommandKind.EquipFragment, CombatCommandKind.UnequipFragment, CombatCommandKind.SetMutation })
            Assert.False(s.Step(new CombatCommand(kind)).Success);
        Assert.Throws<InvalidOperationException>(() => s.CreateTrainingSession()); Assert.Equal(hash, s.StateHash); Assert.Null(s.LocalMap);
    }
    [Fact]
    public void ForgedVictoryDiscoveryAndMissingRewardLedgerAreRejected()
    {
        var s = Source(); Enter(s); Until(s, "secret.belfry", () => s.SecretChambers.Run?.Stage == "Combat"); var snapshot = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { SecretChambers = snapshot.SecretChambers! with { Active = snapshot.SecretChambers.Active! with { Stage = "Victory" } } }));
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { SecretChambers = snapshot.SecretChambers! with { Defeated = [null!] } }));
        ulong forgedSeed = snapshot.SecretChambers!.Active!.Seed ^ 1UL;
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with
        {
            SecretChambers = snapshot.SecretChambers! with
            { Active = snapshot.SecretChambers!.Active! with { Seed = forgedSeed }, Combat = snapshot.SecretChambers!.Combat! with { Seed = forgedSeed } }
        }));
        var invalid = JsonData.Copy(snapshot.SecretChambers!); invalid.PuzzleProgress["secret.unknown"] = 3;
        Assert.Throws<InvalidDataException>(() => Restore(snapshot with { SecretChambers = invalid }));
        Until(s, "secret.belfry", () => s.SecretChambers.Run?.Stage == "Claimed");
        Assert.True(s.ExitSecretChamber().Success); var claimed = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(claimed with { SecretChambers = null }));
        Assert.Throws<InvalidDataException>(() => Restore(claimed with { SecretChambers = claimed.SecretChambers! with { Claimed = [] } }));
    }
    [Fact]
    public void ChamberSaveStoreRoundTripsAndRevealedDoorRemainsAfterLeavingRegion()
    {
        var s = Source(); Enter(s); string dir = Path.Combine(Path.GetTempPath(), "ashenwake-secret-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "character.json"); EndgameRuntimeSaveStore.Write(path, Combat, Adventure, Policy, Campaign, Endgame, s.Capture());
            Assert.Equal(s.StateHash, EndgameRuntimeSaveStore.Load(path, Combat, Adventure, Policy, Campaign, Endgame).Session.StateHash);
        }
        finally { Directory.Delete(dir, true); }
        Assert.True(s.ExitSecretChamber().Success); Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.ReturnToHub)).Success);
        Assert.True(s.SecretChambers.Entries.Single().Revealed); Assert.False(s.SecretChambers.Entries.Single().Here);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
    }
}
