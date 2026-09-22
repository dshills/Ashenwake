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

public sealed class OpeningDepthMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign(bool old) => CampaignContent.Parse(Read(old ? "fixtures/campaign-pacing.json" : "campaign.json"));
    private static readonly Lazy<string> PreviousCombat = new(() => Compose(true));
    private static readonly Lazy<string> CurrentCombat = new(() => Compose(false));
    private static string Compose(bool old) => CampaignCombatContent.Parse(Read("combat.json"), Read(old ? "fixtures/campaign-combat-pacing.json" : "campaign-combat.json")).CombatJson;
    private static string Combat(bool old) => old ? PreviousCombat.Value : CurrentCombat.Value;
    private static CampaignRuntimeSession RestoreOld(CampaignRuntimeSnapshot state) => CampaignRuntimeSession.Restore(Combat(true), Adventure, Policy, Campaign(true), state);
    private static string Save(CampaignRuntimeSession session) => JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture()));
    private static CampaignRuntimeSession Upgrade(string json) => CampaignRuntimeSaveStore.Read(Combat(false), Adventure, Policy, Campaign(false), json);

    private static readonly Lazy<Dictionary<string, CampaignRuntimeSnapshot>> Checkpoints = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat(true), Adventure, Policy, Campaign(true));
        Assert.True(session.EnableExplorationMap().Success);
        var result = new Dictionary<string, CampaignRuntimeSnapshot> { ["hub"] = session.Capture() };
        for (int i = 0; i < 12000 && result.Count < 4; i++)
        {
            var outcome = session.Execute(CampaignRuntimeSmoke.Next(session));
            Assert.True(outcome.Success, outcome.Reason);
            if (session.ActiveEncounterId is "campaign.road" or "campaign.monastery" or "exploration.widow_crypt" && !session.EncounterCleared)
                result.TryAdd(session.ActiveEncounterId, session.Capture());
        }
        Assert.Equal(4, result.Count);
        return result;
    });

    [Fact]
    public void FrozenPredecessorMatchesPublishedBytesAndEveryRoomRemainsUnchanged()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures/campaign-combat-pacing.json"));
        Assert.Equal("86A267B3109BF2CAA968535E183889429ED50E92E8F7AD2B95772BDDDE9BF709", Convert.ToHexString(SHA256.HashData(bytes)));
        var old = CombatContent.Parse(Combat(true));
        var current = CombatContent.Parse(Combat(false));
        Assert.Equal("campaign-combat.pacing.7", old.Campaign!.Version);
        Assert.Equal("campaign-combat.opening_depth.8", current.Campaign!.Version);
        Assert.Equal("campaign.pacing.7", Campaign(true).Capture().Version);
        Assert.Equal("campaign.opening_depth.8", Campaign(false).Capture().Version);
        Assert.Equal("98BF4001F7DB8EEDDD2EEEAE5DC439DD94CB65C9AB36F7BB0A14D822E981DF9A",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures/campaign-pacing.json")))));
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
    private static void SameExceptCatalogIdentities<T>(T before, T after, string previousCombat, string currentCombat)
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
                        (hash == oldIdentity || hash == oldStory))
                        obj[pair.Key] = hash == oldIdentity ? newIdentity : newStory;
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
    [InlineData("campaign.road")]
    [InlineData("campaign.monastery")]
    [InlineData("exploration.widow_crypt")]
    public void PublishedActiveRoomsAndCachesKeepEveryFieldWithoutRespawningOrRegrantingRewards(string context)
    {
        var original = RestoreOld(Checkpoints.Value[context]);
        for (int i = 0; i < 24; i++) Assert.True(original.Step().Success);
        string json = Save(original);
        var loaded = Upgrade(json);
        SameExceptCatalogIdentities(original.Capture(), loaded.Capture(), Combat(true), Combat(false));
        Assert.Equal(original.Production.Content.Hash, loaded.Production.Content.Hash);
        if (context is "campaign.monastery" or "exploration.widow_crypt") Assert.NotEmpty(loaded.Capture().ClearedRooms!);
        Assert.Equal(loaded.StateHash, Upgrade(json).StateHash);
        Assert.Equal(loaded.StateHash, Upgrade(Save(loaded)).StateHash);
        Assert.Equal(json, Save(original));
        Assert.True(loaded.Step().Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void ActiveCastAndFrozenReplayRemainBoundToTheirOriginalCatalog()
    {
        var original = RestoreOld(Checkpoints.Value["campaign.monastery"]);
        for (int i = 0; i < 120 && !original.Combat.Capture().Actors.Any(a => a.Pending is not null); i++)
            Assert.True(original.Execute(CampaignRuntimeSmoke.Next(original)).Success);
        Assert.Contains(original.Combat.Capture().Actors, actor => actor.Pending is not null);
        var replay = original.CaptureReplay();
        Assert.NotEmpty(replay.Frames);
        string replayBytes = JsonData.Write(replay);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(true), Adventure, Policy, Campaign(true), replay).Success);
        var loaded = Upgrade(Save(original));
        SameExceptCatalogIdentities(original.Capture(), loaded.Capture(), Combat(true), Combat(false));
        for (int i = 0; i < 48; i++) Assert.True(loaded.Execute(CampaignRuntimeSmoke.Next(loaded)).Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), loaded.CaptureReplay()).Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(true), Adventure, Policy, Campaign(true), replay).Success);
        Assert.Equal(replayBytes, JsonData.Write(replay));
        Assert.Throws<InvalidDataException>(() => CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign(false), replay));
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
    public void OriginalArchiveMustAuthenticateBeforeIdentitiesAreChanged(string tampering)
    {
        var node = JsonNode.Parse(Save(RestoreOld(Checkpoints.Value["campaign.monastery"])))!;
        switch (tampering)
        {
            case "checksum": node["stateHash"] = new string('0', 64); break;
            case "position": node["state"]!["combat"]!["actors"]![0]!["position"]!["x"] = 999999; break;
            case "receipt": node["state"]!["production"]!["progression"]!["character"]!["operationReceipts"]!["campaign.encounter.campaign.road"] = new string('0', 64); break;
            case "future-cache": node["state"]!["clearedRooms"]!["campaign.road"]!["schemaVersion"] = 2; break;
            case "unknown-combat": node["state"]!["combat"]!["contentHash"] = new string('0', 64); break;
        }
        if (tampering != "checksum") node["stateHash"] = JsonData.Hash(JsonData.Read<CampaignRuntimeSnapshot>(node["state"]!.ToJsonString()));
        string bytes = node.ToJsonString();
        if (tampering is "future-cache" or "unknown-combat") Assert.Throws<SaveCompatibilityException>(() => Upgrade(bytes));
        else Assert.Throws<InvalidDataException>(() => Upgrade(bytes));
        Assert.Equal(bytes, node.ToJsonString());
    }

    [Fact]
    public void SaveAndProfileRemainReadOnlyUntilExplicitWriteKeepsOriginalBackups()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-opening-depth-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = RestoreOld(Checkpoints.Value["campaign.monastery"]);
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
