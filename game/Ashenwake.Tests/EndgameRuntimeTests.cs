using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class EndgameRuntimeTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign => CampaignContent.Parse(Read("campaign.json"));
    private static EndgameContent Endgame => EndgameContent.Parse(Read("endgame.json"));
    private static readonly Lazy<string> Previous = new(() => CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson);
    private static readonly Lazy<string> Composed = new(() => EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson);
    private static string CombatJson => Composed.Value;
    private static EndgameRuntimeSession Import() => EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), Previous.Value, CombatJson, Adventure, Policy, Campaign, Endgame);
    private static EndgameRuntimeSession Restore(EndgameRuntimeSnapshot snapshot) => EndgameRuntimeSession.Restore(CombatJson, Adventure, Policy, Campaign, Endgame, snapshot);
    private static void AssertDiskRoundTrip(EndgameRuntimeSession session)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-endgame-live-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "character.json");
            EndgameRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, session.Capture());
            Assert.Equal(session.StateHash, EndgameRuntimeSaveStore.Load(path, CombatJson, Adventure, Policy, Campaign, Endgame).Session.StateHash);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void AtGate(EndgameRuntimeSession session, EndgameRuntimeCommand command)
    {
        for (int i = 0; i < 500; i++)
        {
            var next = EndgameRuntimeSmoke.AtGate(session, command); var result = session.Execute(next); Assert.True(result.Success, result.Reason);
            if (next.Action != EndgameRuntimeAction.Tick) return;
        }
        Assert.Fail("Gate navigation failed.");
    }
    private static void RunUntil(EndgameRuntimeSession session, Func<EndgameRuntimeSession, bool> done, int tier = 1, bool hunts = false, int maximum = 30000)
    {
        for (int i = 0; i < maximum && !done(session); i++)
        {
            var command = EndgameRuntimeSmoke.Next(session, tier, hunts); var result = session.Execute(command);
            Assert.True(result.Success, result.Reason + " / " + command);
        }
        Assert.True(done(session), "Public input route stalled at " + session.RunView);
    }

    [Fact]
    public void MaintainedPhaseFourBytesMatchManifestAndImportPreservesEarnedCharacterAndStory()
    {
        using var manifest = JsonDocument.Parse(Read("fixtures/phase4-migration-manifest.json"));
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateObject())
            Assert.Equal(entry.Value.GetString()!.ToUpperInvariant(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", entry.Name)))));
        var previous = CampaignRuntimeSaveStore.Read(Previous.Value, Adventure, ProgressionContent.Parse(Read("fixtures/progression-phase4.json")), Campaign, Read("fixtures/phase4-campaign-complete.json"));
        Assert.Equal(manifest.RootElement.GetProperty("stateHash").GetString(), previous.StateHash);
        var session = Import(); Assert.True(session.InHub); Assert.True(session.View.Unlocked); Assert.Empty(session.View.AvailableSigils); Assert.Empty(session.View.Catalysts);
        Assert.Equal(JsonData.Hash(previous.Capture().Campaign), JsonData.Hash(session.Campaign.Capture().Campaign));
        Assert.Equal(previous.Production.ProgressionView.Experience, session.Production.ProgressionView.Experience);
        Assert.Equal(JsonData.Hash(previous.Production.Capture().Progression.Character.Items), JsonData.Hash(session.Production.Capture().Progression.Character.Items));
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }

    [Fact]
    public void FreshCharacterCannotUseProfileMetadataToSkipCampaignGate()
    {
        var profile = Import().Production.Capture().Progression.Profile;
        var session = EndgameRuntimeSession.Create(CombatJson, Adventure, Policy, Campaign, Endgame, profile: profile);
        Assert.False(session.View.Unlocked); Assert.False(session.ClaimRecoverySigil().Success); Assert.False(session.StartGodHunt("hunt.false_vael").Success);
        Assert.Equal(1, session.Production.ProgressionView.Level); Assert.Empty(session.View.Catalysts);
    }

    [Fact]
    public void ActualFractureVictoryCommitsCanonicalRewardsNextSigilAndReplayExactlyOnce()
    {
        var session = Import(); long priorExperience = session.Production.ProgressionView.Experience; int materials = session.Production.ProgressionView.Materials;
        RunUntil(session, s => EndgameRuntimeSmoke.Complete(s), maximum: 3500);
        var reward = Assert.Single(session.Capture().Endgame.Rewards).Value;
        Assert.Equal(1, session.View.HighestClearedTier); Assert.Equal(materials + reward.Materials, session.Production.ProgressionView.Materials);
        Assert.Equal(priorExperience + 4 * 125 + 100, session.Production.ProgressionView.Experience);
        Assert.Single(session.View.AvailableSigils); Assert.Equal(2, session.View.AvailableSigils[0].Tier);
        string hash = session.StateHash; Assert.False(session.AdvanceEncounter().Success); Assert.Equal(hash, session.StateHash);
        Assert.Equal(hash, Restore(session.Capture()).StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, Endgame, session.CaptureReplay()); Assert.True(replay.Success, replay.ToString());
    }

    [Fact]
    public void ActualDeathRetryAbandonAndDeterministicRecoveryRetainOwnedLootWithoutScopedRules()
    {
        var session = Import(); AtGate(session, new(EndgameRuntimeAction.ClaimRecoverySigil));
        long first = session.View.AvailableSigils.Single().Id; AtGate(session, new(EndgameRuntimeAction.StartFracture, first));
        AssertDiskRoundTrip(session);
        for (int i = 0; i < 5000 && !session.AwaitingRetry; i++) Assert.True(session.Step().Success);
        Assert.True(session.AwaitingRetry); Assert.Equal(1, session.RunView!.Deaths); Assert.Equal(2, session.RunView.AttemptsRemaining);
        AssertDiskRoundTrip(session);
        session = Restore(session.Capture()); Assert.True(session.RetryEncounter().Success); Assert.Equal(1, session.Combat.Capture().Endgame!.Attempt);
        Assert.True(session.Abandon().Success); Assert.True(session.InHub); Assert.Null(session.Combat.Capture().Endgame); Assert.Empty(session.Capture().Endgame.Rewards);
        var origin = session.Capture(); AtGate(session, new(EndgameRuntimeAction.ClaimRecoverySigil));
        var replayed = Restore(origin); AtGate(replayed, new(EndgameRuntimeAction.ClaimRecoverySigil));
        Assert.Equal(JsonData.Hash(session.View.AvailableSigils), JsonData.Hash(replayed.View.AvailableSigils));
        Assert.True(session.View.AvailableSigils.Single().Id > first); Assert.Equal(1, session.View.AvailableSigils.Single().Tier);
        Assert.False(session.ClaimRecoverySigil().Success);
    }

    [Fact]
    public void AttunementUsesPermanentWalletAndRejectsChangedOrInvalidRequestsWithoutPartialDebit()
    {
        var session = Import(); RunUntil(session, s => EndgameRuntimeSmoke.Complete(s));
        var sigil = session.View.AvailableSigils.Single(); var definition = Endgame.Capture();
        string replacement = definition.Modifiers.First(m => m.MinimumTier <= sigil.Tier && !sigil.Modifiers.Contains(m.Id) && !m.Excludes.Any(sigil.Modifiers.Contains) && sigil.Modifiers.All(id => !definition.Modifiers.Single(d => d.Id == id).Excludes.Contains(m.Id))).Id;
        int before = session.Production.ProgressionView.Materials;
        AtGate(session, new(EndgameRuntimeAction.AttuneSigil, sigil.Id, sigil.Modifiers[0], replacement));
        Assert.Equal(before - 5, session.Production.ProgressionView.Materials);
        string hash = session.StateHash; Assert.False(session.AttuneSigil(sigil.Id, sigil.Modifiers[0], "unknown").Success); Assert.Equal(hash, session.StateHash);
    }

    [Fact]
    public void SaveRestoresProfileAndReplayButPreservesFutureUnknownHeaders()
    {
        var session = Import(); string dir = Path.Combine(Path.GetTempPath(), "ashenwake-endgame-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "character.json");
        try
        {
            EndgameRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, session.Capture());
            AtGate(session, new(EndgameRuntimeAction.ClaimRecoverySigil));
            EndgameRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, session.Capture());
            var loaded = EndgameRuntimeSaveStore.Load(path, CombatJson, Adventure, Policy, Campaign, Endgame); Assert.Equal(session.StateHash, loaded.Session.StateHash);
            var node = JsonNode.Parse(File.ReadAllText(path))!; node["state"]!["schemaVersion"] = 2; node["state"]!["futureOnly"] = "preserve";
            string future = node.ToJsonString(); File.WriteAllText(path, future);
            Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Load(path, CombatJson, Adventure, Policy, Campaign, Endgame));
            Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, session.Capture()));
            Assert.Equal(future, File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FailedCharacterPublicationCannotAdvanceSharedProfile()
    {
        var session = Import(); var incoming = session.Capture();
        incoming.Campaign.Production.Progression.Profile.Unlocks.Add("profile.secret_hunt");
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-character-commit-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json"); Directory.CreateDirectory(path);
        try
        {
            string profilePath = EndgameRuntimeSaveStore.ProfilePath(path);
            var baseline = session.Production.Capture().Progression.Profile; baseline.Unlocks.Remove("profile.secret_hunt");
            LocalProfileStore.Merge(profilePath, session.Production.Content, baseline);
            string before = File.ReadAllText(profilePath);
            Assert.ThrowsAny<IOException>(() => EndgameRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, incoming));
            Assert.Equal(before, File.ReadAllText(profilePath));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ProfilePublicationFailureLeavesValidCharacterAndRetryMergesConcurrentMetadata()
    {
        var session = Import(); var incoming = session.Capture();
        incoming.Campaign.Production.Progression.Profile.Unlocks.Add("profile.secret_hunt");
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-profile-commit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "character.json"), profilePath = EndgameRuntimeSaveStore.ProfilePath(path);
        try
        {
            var baseline = session.Production.Capture().Progression.Profile;
            baseline.Unlocks.Remove("profile.secret_hunt"); baseline.Unlocks.Remove("profile.memory_cartography");
            LocalProfileStore.Merge(profilePath, session.Production.Content, baseline);
            using (var lease = new FileStream(profilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Assert.ThrowsAny<IOException>(() => EndgameRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, incoming));
            var durable = EndgameRuntimeSaveStore.Read(CombatJson, Adventure, Policy, Campaign, Endgame, File.ReadAllText(path));
            Assert.Contains("profile.secret_hunt", durable.Production.Capture().Progression.Profile.Unlocks);
            Assert.DoesNotContain("profile.secret_hunt", LocalProfileStore.Load(profilePath, session.Production.Content).Profile.Unlocks);
            baseline.Unlocks.Add("profile.memory_cartography"); LocalProfileStore.Merge(profilePath, session.Production.Content, baseline);
            EndgameRuntimeSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, incoming);
            var merged = LocalProfileStore.Load(profilePath, session.Production.Content).Profile;
            Assert.Contains("profile.secret_hunt", merged.Unlocks); Assert.Contains("profile.memory_cartography", merged.Unlocks);
            Assert.Equal(durable.Production.ProgressionView.Experience, EndgameRuntimeSaveStore.Load(path, CombatJson, Adventure, Policy, Campaign, Endgame).Session.Production.ProgressionView.Experience);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void CatalystGraftingAndTemperingAreAtomicPermanentTransactions()
    {
        var source = Import(); var snapshot = source.Production.Capture().Progression;
        var state = snapshot.Character; long id = state.NextItemId++;
        state.Items = [.. state.Items, new PermanentItem { Id = id, DefinitionId = "item.ashcleaver", Rarity = ItemRarity.Godwrought, BurningKills = 1000 }];
        state.Endgame!.Catalysts["material.serath_memory"] = 1; state.Endgame.Catalysts["material.vael_rib"] = 1;
        var session = ProgressionSession.Restore(source.Production.Content, snapshot);
        string before = session.StateHash;
        var wrong = new CraftingRequest("graft", CraftingService.DivineGrafting, id, Lineage: "Serath", ConfirmPermanent: true, CatalystId: "material.orrun_oath");
        Assert.False(session.Craft(wrong).Success); Assert.Equal(before, session.StateHash);
        var request = wrong with { CatalystId = "material.serath_memory" }; int materials = session.View.Materials;
        Assert.True(session.Craft(request).Success); Assert.Equal(materials - 20, session.View.Materials);
        Assert.Equal("Serath", session.Capture().Character.Items.Single(i => i.Id == id).Evolution);
        Assert.DoesNotContain("material.serath_memory", session.Capture().Character.Endgame!.Catalysts.Keys);
        string committed = session.StateHash; Assert.True(session.Craft(request).Success); Assert.Equal(committed, session.StateHash);
        Assert.False(session.Craft(request with { Lineage = "Orrun", CatalystId = "material.orrun_oath" }).Success); Assert.Equal(committed, session.StateHash);
        var temper = new CraftingRequest("catalytic-temper", CraftingService.Tempering, id, AffixId: "affix.damage", CatalystId: "material.vael_rib");
        Assert.True(session.Craft(temper).Success); Assert.Equal(materials - 20, session.View.Materials);
        Assert.Equal(2, session.Capture().Character.Items.Single(i => i.Id == id).Affixes["affix.damage"]);
        Assert.Empty(session.Capture().Character.Endgame!.Catalysts);
    }

    [Fact]
    public void InsufficientCommonMaterialOrCappedTemperingCannotConsumeCatalysts()
    {
        var source = Import(); var snapshot = source.Production.Capture().Progression; var state = snapshot.Character;
        long id = state.NextItemId++; state.Materials = 0;
        state.Items = [.. state.Items, new PermanentItem { Id = id, DefinitionId = "item.ashcleaver", Rarity = ItemRarity.Godwrought, BurningKills = 1000, Affixes = new() { ["affix.damage"] = 10 } }];
        state.Endgame!.Catalysts["material.serath_memory"] = 1;
        var session = ProgressionSession.Restore(source.Production.Content, snapshot); string hash = session.StateHash;
        Assert.False(session.Craft(new("no-money", CraftingService.DivineGrafting, id, Lineage: "Serath", ConfirmPermanent: true, CatalystId: "material.serath_memory")).Success);
        Assert.Equal(hash, session.StateHash);
        Assert.False(session.Craft(new("capped", CraftingService.Tempering, id, AffixId: "affix.damage", CatalystId: "material.serath_memory")).Success);
        Assert.Equal(hash, session.StateHash);
    }

    [Fact]
    public void AllEightTiersAndFiveActualHuntsFinishWithCanonicalCatalystsAndBoundaryRestores()
    {
        var session = Import(); string boundary = "";
        for (int i = 0; i < EndgameRuntimeSmoke.MaximumCommands && !EndgameRuntimeSmoke.Complete(session, 8, true); i++)
        {
            var result = session.Execute(EndgameRuntimeSmoke.Next(session, 8, true)); Assert.True(result.Success, result.Reason);
            string next = session.RunView is { } run ? $"{run.Id}:{run.EncounterIndex}:{run.Deaths}:{run.Status}:{session.InHub}" : "hub";
            if (next != boundary) { Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash); boundary = next; }
        }
        Assert.True(EndgameRuntimeSmoke.Complete(session, 8, true), session.RunView?.ToString());
        Assert.Equal(5, session.Capture().Endgame.Rewards.Values.Count(r => r.Kind == "GodHunt"));
        foreach (var hunt in Endgame.Capture().Hunts) Assert.Equal(1, session.View.Catalysts[hunt.EvolutionMaterial]);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
        Assert.True(EndgameRuntimeReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, Endgame, session.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("endgame.room.999.0")]
    [InlineData("endgame.reward.999")]
    [InlineData("endgame.reward.999.mastery")]
    public void PhantomPermanentEndgameReceiptsAreRejected(string key)
    {
        var state = Import().Capture(); state.Campaign.Production.Progression.Character.OperationReceipts[key] = JsonData.Hash(new { Forged = true });
        Assert.Throws<InvalidDataException>(() => Restore(state));
    }

    [Fact]
    public void PhantomOwnedOrSpentCatalystsAreRejectedWithoutMatchingEarnedReward()
    {
        var state = Import().Capture(); state.Campaign.Production.Progression.Character.Endgame!.Catalysts["material.divine_catalyst"] = 1;
        Assert.Throws<InvalidDataException>(() => Restore(state));
        state = Import().Capture(); state.Campaign.Production.Progression.Character.Endgame!.SpentCatalysts["material.divine_catalyst"] = 1;
        Assert.Throws<InvalidDataException>(() => Restore(state));
    }

    [Fact]
    public void FullInventoryAndCappedWalletCanCommitARealFractureWithoutCollectingDrops()
    {
        var snapshot = Import().Capture(); var state = snapshot.Campaign.Production.Progression.Character;
        var body = snapshot.Campaign.Production.Expedition.Combat; var hub = snapshot.Campaign.Combat;
        var definition = CombatContent.Parse(CombatJson).Items.Single(i => i.Id == "item.starter_head");
        while (body.Inventory.Count < 512)
        {
            long id = state.NextItemId++;
            var item = new CombatItem(id, definition.Id, definition.Name, definition.Slot, "Common", definition.Damage, definition.Armor, definition.CriticalBasisPoints);
            body.Inventory.Add(item); hub.Inventory.Add(item);
            state.Items = [.. state.Items, new PermanentItem { Id = id, DefinitionId = definition.Id, Rarity = ItemRarity.Common, BaseDamage = item.Damage, BaseArmor = item.Armor, BaseCriticalBasisPoints = item.CriticalBasisPoints }];
        }
        body.NextObjectId = Math.Max(body.NextObjectId, state.NextItemId); hub.NextObjectId = Math.Max(hub.NextObjectId, state.NextItemId);
        state.Materials = 1000000; snapshot.Campaign.Production.Expedition.Adventure.Materials = 1000000;
        var session = Restore(snapshot); bool disclosed = false;
        for (int i = 0; i < 10000 && !EndgameRuntimeSmoke.Complete(session); i++)
        {
            var result = session.Execute(EndgameRuntimeSmoke.Next(session)); Assert.True(result.Success, result.Reason);
            disclosed |= result.WorldEvents.Any(e => e.StartsWith("GroundLootLeftBehind:", StringComparison.Ordinal));
        }
        Assert.True(EndgameRuntimeSmoke.Complete(session)); Assert.True(disclosed); Assert.Equal(512, session.Combat.View.Inventory.Count);
        Assert.Equal(1000000, session.Production.ProgressionView.Materials); Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }

    [Theory]
    [InlineData("state.manifest")]
    [InlineData("state.combat.endgame")]
    [InlineData("state.combat.endgame.manifest")]
    [InlineData("state.campaign.production.progression.character.endgame")]
    public void NestedFutureEndgameHeadersAreCompatibilityErrorsBeforeUnknownFields(string path)
    {
        var session = Import(); AtGate(session, new(EndgameRuntimeAction.ClaimRecoverySigil));
        AtGate(session, new(EndgameRuntimeAction.StartFracture, session.View.AvailableSigils.Single().Id));
        var node = JsonNode.Parse(JsonData.Write(new EndgameRuntimeSave(1, session.StateHash, session.Capture())))!;
        var target = path.Split('.').Aggregate(node, (current, name) => current[name]!);
        target["schemaVersion"] = 2; target["futureProperty"] = "preserve";
        Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Read(CombatJson, Adventure, Policy, Campaign, Endgame, node.ToJsonString()));
    }

    [Fact]
    public void ExplicitPhaseFourImportRejectsInFlightCampaignInsteadOfRestartingIt()
    {
        var journey = CampaignRuntimeSaveStore.Read(Previous.Value, Adventure, Policy, Campaign, Read("fixtures/phase4-campaign-complete.json"));
        Assert.True(journey.EnterAct(1).Success); Assert.False(journey.InHub);
        string source = JsonData.Write(new CampaignRuntimeSave(1, journey.StateHash, journey.Capture()));
        Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeMigration.ImportPhaseFour(source, Previous.Value, CombatJson, Adventure, Policy, Campaign, Endgame));
    }
}
