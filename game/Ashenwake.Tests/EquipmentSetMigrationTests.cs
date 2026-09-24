using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class EquipmentSetMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly AdventureContent Adventure = AdventureContent.Parse(Read("adventure.json"));
    private static readonly ProgressionContent Policy = ProgressionContent.Parse(Read("progression.json"));
    private static readonly ProgressionContent OldPolicy = ProgressionContent.Parse(Read("fixtures/progression-world-encounters.json"));
    private static readonly CampaignContent Campaign = CampaignContent.Parse(Read("campaign.json"));
    private static readonly EndgameContent Endgame = EndgameContent.Parse(Read("endgame.json"));
    private static string Compose(string path) => EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read(path), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson;
    private static readonly string OldCombat = Compose("fixtures/combat-world-encounters.json"), NewCombat = Compose("combat.json");
    private static readonly Lazy<Dictionary<string, EndgameRuntimeSnapshot>> Sources = new(() =>
    {
        var result = new Dictionary<string, EndgameRuntimeSnapshot>();
        var s = EndgameRuntimeSession.Create(OldCombat, Adventure, OldPolicy, Campaign, Endgame);
        for (int i = 0; i < 20000 && result.Count < 2; i++)
        {
            if (!s.InHub && s.Campaign.EncounterCleared && s.Campaign.ActiveEncounterId is "campaign.road" or "campaign.monastery") result.TryAdd(s.Campaign.ActiveEncounterId, s.Capture());
            if (result.Count == 2) break;
            var outcome = s.ExecuteCampaign(CampaignRuntimeSmoke.Next(s.Campaign)); Assert.True(outcome.Success, outcome.Reason);
        }
        Assert.Equal(2, result.Count); return result;
    });
    private static string Save(EndgameRuntimeSession s) => JsonData.Write(new EndgameRuntimeSave(1, s.StateHash, s.Capture()));
    private static EndgameRuntimeSession Upgrade(string json) => EndgameRuntimeSaveStore.Read(NewCombat, Adventure, Policy, Campaign, Endgame, json);
    private static void EqualExceptIdentities(EndgameRuntimeSession old, EndgameRuntimeSession migrated)
    {
        var expected = JsonNode.Parse(JsonData.Write(old.Capture()))!;
        var replacements = new Dictionary<string, string> { [old.Combat.ContentHash] = migrated.Combat.ContentHash, [old.Production.Content.Hash] = migrated.Production.Content.Hash };
        void Visit(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key == "contentHash" && pair.Value is JsonValue v && v.TryGetValue<string>(out var hash) && replacements.TryGetValue(hash, out var replacement)) obj[pair.Key] = replacement;
                    else Visit(pair.Value);
                }
            else if (node is JsonArray a) foreach (var child in a) Visit(child);
        }
        Visit(expected); Assert.Equal(expected.ToJsonString(), JsonNode.Parse(JsonData.Write(migrated.Capture()))!.ToJsonString());
        Assert.DoesNotContain(migrated.Production.Capture().Progression.Character.Items, i => EquipmentSets.IsItem(i.DefinitionId));
    }
    [Theory]
    [InlineData("event.lantern", "Combat")]
    [InlineData("event.lantern", "Victory")]
    [InlineData("event.lantern", "Claimed")]
    [InlineData("event.caravan", "Foyer")]
    [InlineData("storm.grey_march", "Combat")]
    public void PublishedWorldArenaAndReceiptsMigrateWithoutInventingItemsOrChangingSourceRooms(string id, string stage)
    {
        var definition = WorldEncounterCatalog.Find(id)!;
        var s = EndgameRuntimeSession.Restore(OldCombat, Adventure, OldPolicy, Campaign, Endgame, Sources.Value[definition.SourceEncounterId]);
        for (int i = 0; i < 6000 && s.WorldEncounters.Run?.Stage != stage; i++)
        {
            var outcome = s.Execute(WorldEncounterSmoke.Next(s, id)); Assert.True(outcome.Success, outcome.Reason);
        }
        Assert.Equal(stage, s.WorldEncounters.Run?.Stage);
        if (id == "event.caravan")
        {
            for (int i = 0; i < 1000 && s.WorldEncounters.Run!.PuzzleStep != 2; i++) Assert.True(s.Execute(WorldEncounterSmoke.Next(s, id)).Success);
            Assert.Equal(2, s.WorldEncounters.Run!.PuzzleStep);
        }
        var original = Save(s); var upgraded = Upgrade(original); EqualExceptIdentities(s, upgraded);
        Assert.Equal(upgraded.StateHash, Upgrade(Save(upgraded)).StateHash);
        Assert.Equal(original, Save(s));
        Assert.True(upgraded.Step().Success);
        Assert.True(EndgameRuntimeReplayRunner.Run(NewCombat, Adventure, Policy, Campaign, Endgame, upgraded.CaptureReplay()).Success);
        if (stage == "Claimed")
        {
            Assert.False(upgraded.ClaimWorldEncounterReward().Success);
            Assert.True(s.ExitWorldEncounter().Success); EqualExceptIdentities(s, Upgrade(Save(s)));
        }
    }
    [Fact]
    public void OldSaveLoadIsReadOnlyAndCorruptPrimaryRemainsPreservedWhileBackupMigrates()
    {
        var original = EndgameRuntimeSession.Restore(OldCombat, Adventure, OldPolicy, Campaign, Endgame, Sources.Value["campaign.road"]);
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-set-migration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "character.json"); string bytes = Save(original); File.WriteAllText(path, bytes);
            var loaded = EndgameRuntimeSaveStore.Load(path, NewCombat, Adventure, Policy, Campaign, Endgame);
            Assert.False(loaded.RecoveredBackup); EqualExceptIdentities(original, loaded.Session); Assert.Equal(bytes, File.ReadAllText(path));
            Assert.False(File.Exists(path + ".bak"));
            File.WriteAllText(path + ".bak", bytes); File.WriteAllText(path, "{broken");
            var recovered = EndgameRuntimeSaveStore.Load(path, NewCombat, Adventure, Policy, Campaign, Endgame);
            Assert.True(recovered.RecoveredBackup); EqualExceptIdentities(original, recovered.Session);
            Assert.Equal(bytes, File.ReadAllText(path + ".bak")); Assert.Equal("{broken", File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
