using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class LateCampaignDepthMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign(bool old) => CampaignContent.Parse(Read(old ? "fixtures/campaign-midgame-depth.json" : "campaign.json"));
    private static readonly Lazy<string> PreviousCombat = new(() => Compose(true));
    private static readonly Lazy<string> CurrentCombat = new(() => Compose(false));
    private static string Compose(bool old) => CampaignCombatContent.Parse(Read("combat.json"), Read(old ? "fixtures/campaign-combat-midgame-depth.json" : "campaign-combat.json")).CombatJson;
    private static string Combat(bool old) => old ? PreviousCombat.Value : CurrentCombat.Value;
    private static CampaignRuntimeSession RestoreOld(CampaignRuntimeSnapshot state) => CampaignRuntimeSession.Restore(Combat(true), Adventure, Policy, Campaign(true), state);
    private static string Save(CampaignRuntimeSession session) => JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture()));
    private static CampaignRuntimeSession Upgrade(string json) => CampaignRuntimeSaveStore.Read(Combat(false), Adventure, Policy, Campaign(false), json);

    private static readonly Lazy<Dictionary<string, CampaignRuntimeSnapshot>> Checkpoints = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat(true), Adventure, Policy, Campaign(true));
        Assert.True(session.EnableExplorationMap().Success);
        var result = new Dictionary<string, CampaignRuntimeSnapshot> { ["hub"] = session.Capture() };
        for (int i = 0; i < 65000 && result.Count < 11; i++)
        {
            var outcome = session.Execute(CampaignRuntimeSmoke.Next(session));
            Assert.True(outcome.Success, outcome.Reason);
            if (session.ActiveEncounterId is "campaign.living_ruins" or "campaign.extraction_floor" or "campaign.bone_causeway" or "campaign.contract_hall" or "campaign.covenant_warden" or "campaign.repeating_rooms" or "campaign.identity_memory" or "campaign.breach_heart" or "exploration.oathkeeper_archive" or "exploration.unremembered_vault" && !session.EncounterCleared)
                result.TryAdd(session.ActiveEncounterId, session.Capture());
        }
        Assert.Equal(11, result.Count);
        return result;
    });

    [Fact]
    public void FrozenPredecessorMatchesPublishedBytesAndEveryRoomRemainsUnchanged()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures/campaign-combat-midgame-depth.json"));
        Assert.Equal("AD50D736346E9363E4F5E15AA08BC67D1710D4DF735A8BE26865AEB66E7AAEA3", Convert.ToHexString(SHA256.HashData(bytes)));
        var old = CombatContent.Parse(Combat(true));
        var current = CombatContent.Parse(Combat(false));
        Assert.Equal("campaign-combat.midgame_depth.9", old.Campaign!.Version);
        Assert.Equal("campaign-combat.late_depth.10", current.Campaign!.Version);
        Assert.Equal("campaign.midgame_depth.9", Campaign(true).Capture().Version);
        Assert.Equal("campaign.late_depth.10", Campaign(false).Capture().Version);
        Assert.Equal("6C8776FF00E23D6503845195A786E4D10725E274A377C3E9A19C9346BC9D21B0",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures/campaign-midgame-depth.json")))));
        var oldRewards = Campaign(true).Capture().Acts.SelectMany(act => act.Encounters).Select(e => new { e.Id, e.Experience, e.Materials });
        var currentRewards = Campaign(false).Capture().Acts.SelectMany(act => act.Encounters).Select(e => new { e.Id, e.Experience, e.Materials });
        Assert.Equal(JsonData.Hash(oldRewards), JsonData.Hash(currentRewards));
        Assert.Equal(old.Campaign.Encounters.Select(e => e.Id), current.Campaign.Encounters.Select(e => e.Id));
        foreach (var encounter in old.Campaign.Encounters)
            Assert.Equal(JsonData.Hash(encounter.Room), JsonData.Hash(current.Campaign.Encounters.Single(e => e.Id == encounter.Id).Room));
        foreach (var enemy in old.Enemies)
            Assert.Equal(JsonData.Hash(enemy), JsonData.Hash(current.Enemies.Single(e => e.Id == enemy.Id)));
    }

    // Only exact combat and story catalog identities may change. This compares all remaining
    // serialized fields, including receipts, ownership, actor modifiers, clocks, RNG,
    // pending casts, room caches, fog and any future optional state automatically.
    private static void SameExceptCatalogIdentities<T>(T before, T after, string previousCombat, string currentCombat, string? previousPolicy = null, string? currentPolicy = null)
    {
        string oldIdentity = CombatContent.Parse(previousCombat).Identity;
        string newIdentity = CombatContent.Parse(currentCombat).Identity;
        string oldStory = Campaign(true).Hash, newStory = Campaign(false).Hash;
        Assert.NotEqual(oldIdentity, newIdentity);
        var expected = JsonNode.Parse(JsonData.Write(before))!;
        void Rebind(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key == "contentHash" && pair.Value is JsonValue value && value.TryGetValue<string>(out var hash) &&
                        (hash == oldIdentity || hash == oldStory || previousPolicy is not null && hash == previousPolicy))
                        obj[pair.Key] = hash == oldIdentity ? newIdentity : hash == oldStory ? newStory : currentPolicy;
                    else Rebind(pair.Value);
                }
            }
            else if (node is JsonArray array)
                foreach (var item in array) Rebind(item);
        }
        Rebind(expected);
        Assert.Equal(expected.ToJsonString(), JsonNode.Parse(JsonData.Write(after))!.ToJsonString());
    }

    [Theory]
    [InlineData("hub")]
    [InlineData("campaign.bone_causeway")]
    [InlineData("campaign.contract_hall")]
    [InlineData("campaign.covenant_warden")]
    [InlineData("campaign.repeating_rooms")]
    [InlineData("campaign.identity_memory")]
    [InlineData("campaign.breach_heart")]
    [InlineData("exploration.oathkeeper_archive")]
    [InlineData("exploration.unremembered_vault")]
    public void PublishedActiveRoomsAndCachesKeepEveryFieldWithoutRespawningOrRegrantingRewards(string context)
    {
        var original = RestoreOld(Checkpoints.Value[context]);
        for (int i = 0; i < 24; i++) Assert.True(original.Step().Success);
        string json = Save(original);
        var loaded = Upgrade(json);
        SameExceptCatalogIdentities(original.Capture(), loaded.Capture(), Combat(true), Combat(false));
        Assert.Equal(original.Production.Content.Hash, loaded.Production.Content.Hash);
        if (context != "hub") Assert.NotEmpty(loaded.Capture().ClearedRooms!);
        Assert.Equal(loaded.StateHash, Upgrade(json).StateHash);
        Assert.Equal(loaded.StateHash, Upgrade(Save(loaded)).StateHash);
        Assert.Equal(json, Save(original));
        Assert.True(loaded.Step().Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("campaign.contract_hall", "campaign.oathmark")]
    [InlineData("campaign.covenant_warden", "campaign.covenant_fault")]
    [InlineData("campaign.breach_heart", "campaign.returning_echo")]
    public void PublishedAttackWarningsAndFrozenReplayKeepTheirAnnouncedTiming(string encounter, string hazard)
    {
        var original = RestoreOld(Checkpoints.Value[encounter]);
        for (int i = 0; i < 2400 && !original.Combat.View.CampaignHazards!.Any(h => h.ContentId == hazard); i++)
            Assert.True(original.Execute(CampaignRuntimeSmoke.Next(original)).Success);
        Assert.Contains(original.Combat.View.CampaignHazards!, h => h.ContentId == hazard);
        var replay = original.CaptureReplay();
        Assert.NotEmpty(replay.Frames);
        string replayBytes = JsonData.Write(replay);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(true), Adventure, Policy, Campaign(true), replay).Success);
        var loaded = Upgrade(Save(original));
        SameExceptCatalogIdentities(original.Capture(), loaded.Capture(), Combat(true), Combat(false));
        Assert.Equal(JsonData.Write(original.Combat.Capture().Campaign!.Hazards), JsonData.Write(loaded.Combat.Capture().Campaign!.Hazards));
        for (int i = 0; i < 80; i++) Assert.True(loaded.Execute(CampaignRuntimeSmoke.Next(loaded)).Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(true), Adventure, Policy, Campaign(true), replay).Success);
        Assert.Equal(replayBytes, JsonData.Write(replay));
        Assert.Throws<InvalidDataException>(() => CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), replay));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublishedMidgameSupportKeepsItsLiveChannelAndOverchargeState(bool forge)
    {
        var original = RestoreOld(Checkpoints.Value[forge ? "campaign.extraction_floor" : "campaign.living_ruins"]);
        string hazard = forge ? "campaign.forge_bellows" : "campaign.spore_mend";
        for (int i = 0; i < 1600 && !original.Combat.View.CampaignHazards!.Any(h => h.ContentId == hazard); i++)
            Assert.True(original.Execute(CampaignRuntimeSmoke.Next(original)).Success);
        Assert.Contains(original.Combat.View.CampaignHazards!, h => h.ContentId == hazard);
        var loaded = Upgrade(Save(original));
        SameExceptCatalogIdentities(original.Capture(), loaded.Capture(), Combat(true), Combat(false));
        if (forge)
        {
            for (int i = 0; i < 60 && !original.Combat.View.Actors.Any(a => a.ForgeOverchargeTicks > 0); i++) Assert.True(original.Step().Success);
            Assert.Contains(original.Combat.View.Actors, a => a.ForgeOverchargeTicks > 0);
            loaded = Upgrade(Save(original));
            SameExceptCatalogIdentities(original.Capture(), loaded.Capture(), Combat(true), Combat(false));
        }
        Assert.True(loaded.Step().Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void PublishedEndgameAndBoundMemoryKeepAllTimersOwnershipAndRunState()
    {
        var endgame = EndgameContent.Parse(Read("endgame.json"));
        var experiment = ExperimentContent.Parse(Read("experiments.json"));
        string oldCombat = EndgameCombatContent.Parse(Combat(true), Read("endgame-combat.json"), endgame).CombatJson;
        string newCombat = EndgameCombatContent.Parse(Combat(false), Read("endgame-combat.json"), endgame).CombatJson;
        var original = EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), Combat(true), oldCombat, Adventure, Policy, Campaign(true), endgame);
        var echoes = ExperimentRuntimeSession.FromEndgame(oldCombat, Adventure, Policy, Campaign(true), endgame, experiment, original.Capture());
        for (int i = 0; i < 4000 && echoes.View.Memory?.Status != "Bound"; i++)
            Assert.True(echoes.Execute(ExperimentRuntimeSmoke.Next(echoes)).Success);
        Assert.Equal("Bound", echoes.View.Memory?.Status);
        var upgradedEchoes = ExperimentSaveStore.Read(newCombat, Adventure, Policy, Campaign(false), endgame, experiment,
            JsonData.Write(new ExperimentSave(1, echoes.StateHash, echoes.Capture())));
        SameExceptCatalogIdentities(echoes.Capture(), upgradedEchoes.Capture(), oldCombat, newCombat);
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
        var upgraded = EndgameRuntimeSaveStore.Read(newCombat, Adventure, Policy, Campaign(false), endgame,
            JsonData.Write(new EndgameRuntimeSave(1, original.StateHash, original.Capture())));
        SameExceptCatalogIdentities(original.Capture(), upgraded.Capture(), oldCombat, newCombat);
        Assert.True(upgraded.Step().Success);
        Assert.True(EndgameRuntimeReplayRunner.Run(newCombat, Adventure, Policy, Campaign(false), endgame, upgraded.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("position")]
    [InlineData("receipt")]
    [InlineData("future-cache")]
    [InlineData("unknown-combat")]
    [InlineData("explicit-inactive-overcharge")]
    public void OriginalArchiveMustAuthenticateBeforeIdentitiesAreChanged(string tampering)
    {
        var node = JsonNode.Parse(Save(RestoreOld(Checkpoints.Value["campaign.extraction_floor"])))!;
        switch (tampering)
        {
            case "checksum": node["stateHash"] = new string('0', 64); break;
            case "position": node["state"]!["combat"]!["actors"]![0]!["position"]!["x"] = 999999; break;
            case "receipt": node["state"]!["production"]!["progression"]!["character"]!["operationReceipts"]!["campaign.encounter.campaign.road"] = new string('0', 64); break;
            case "future-cache": node["state"]!["clearedRooms"]!["campaign.road"]!["schemaVersion"] = 2; break;
            case "unknown-combat": node["state"]!["combat"]!["contentHash"] = new string('0', 64); break;
            case "explicit-inactive-overcharge":
                var actors = node["state"]!["combat"]!["campaign"]!["actors"]!.AsObject();
                actors.First().Value!["forgeOverchargeUntil"] = 0;
                break;
        }
        if (tampering is not ("checksum" or "explicit-inactive-overcharge")) node["stateHash"] = JsonData.Hash(JsonData.Read<CampaignRuntimeSnapshot>(node["state"]!.ToJsonString()));
        string bytes = node.ToJsonString();
        if (tampering is "future-cache" or "unknown-combat") Assert.Throws<SaveCompatibilityException>(() => Upgrade(bytes));
        else Assert.Throws<InvalidDataException>(() => Upgrade(bytes));
        Assert.Equal(bytes, node.ToJsonString());
    }

    [Fact]
    public void ExplicitImportCanAuthenticateMidgameCombatWhileMovingDirectlyToTheCurrentStory()
    {
        var endgame = EndgameContent.Parse(Read("endgame.json"));
        string oldCombat = EndgameCombatContent.Parse(Combat(true), Read("endgame-combat.json"), endgame).CombatJson;
        string newCombat = EndgameCombatContent.Parse(Combat(false), Read("endgame-combat.json"), endgame).CombatJson;
        var original = CampaignRuntimeSaveStore.Read(Combat(true), Adventure, Policy, Campaign(true), Read("fixtures/phase4-campaign-complete.json"));
        string originalBytes = Save(original);
        var published = EndgameRuntimeMigration.ImportPhaseFour(originalBytes, Combat(true), oldCombat, Adventure, Policy, Campaign(true), endgame);
        // The maintained campaign bundle must select the matching published story while
        // authenticating the source, even though the explicit import targets the newest story.
        var upgraded = EndgameRuntimeMigration.ImportPhaseFour(originalBytes, Combat(true), newCombat, Adventure, Policy, Campaign(false), endgame);
        SameExceptCatalogIdentities(published.Capture(), upgraded.Capture(), oldCombat, newCombat);
        Assert.Equal(originalBytes, Save(original));
    }

    [Fact]
    public void SaveAndProfileRemainReadOnlyUntilExplicitWriteKeepsOriginalBackups()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-late-depth-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = RestoreOld(Checkpoints.Value["campaign.extraction_floor"]);
            CampaignRuntimeSaveStore.Write(path, Combat(true), Adventure, Policy, Campaign(true), original.Capture());
            string profilePath = CampaignRuntimeSaveStore.ProfilePath(path);
            string saveBytes = File.ReadAllText(path), profileBytes = File.ReadAllText(profilePath);
            var loaded = CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign(false));
            Assert.False(loaded.RecoveredBackup);
            Assert.Equal(saveBytes, File.ReadAllText(path));
            Assert.Equal(profileBytes, File.ReadAllText(profilePath));
            SameExceptCatalogIdentities(original.Capture(), loaded.Session.Capture(), Combat(true), Combat(false));
            CampaignRuntimeSaveStore.Write(path, Combat(false), Adventure, Policy, Campaign(false), loaded.Session.Capture());
            Assert.Equal(saveBytes, File.ReadAllText(path + ".bak"));
            Assert.Equal(profileBytes, File.ReadAllText(profilePath + ".bak"));
            Assert.Equal(loaded.Session.StateHash, CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign(false)).Session.StateHash);
            File.WriteAllText(path, "{");
            var recovered = CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign(false));
            Assert.True(recovered.RecoveredBackup);
            Assert.Equal(loaded.Session.StateHash, recovered.Session.StateHash);
            Assert.Equal(saveBytes, File.ReadAllText(path + ".bak"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
