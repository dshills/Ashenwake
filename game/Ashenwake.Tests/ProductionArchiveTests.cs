using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class ProductionArchiveTests
{
    private static string Combat => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static AdventureContent Adventure => AdventureContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "adventure.json")));
    private static ProgressionContent Policy => ProgressionContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "progression.json")));
    [Fact]
    public void ActualPriorBuildHubFixtureMigratesWithoutDroppingPermanentOwnership()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "phase2-expedition-hub.json"));
        var old = JsonNode.Parse(json)!["state"]!;
        var session = PhaseTwoMigration.Read(json, Combat, Adventure, Policy); var state = session.Capture();
        Assert.Equal(1, state.Expedition.Adventure.BellVictories); Assert.True(state.Expedition.Adventure.ReturnedToMara);
        Assert.Contains("fragment.heart_serath", state.Progression.Character.OwnedFragments);
        Assert.Equal(old["combat"]!["inventory"]!.AsArray().Count, state.Progression.Character.Items.Length);
        Assert.Equal(old["adventure"]!["materials"]!.GetValue<int>(), state.Progression.Character.Materials);
        Assert.Equal(session.StateHash, ProductionSession.Restore(Combat, Adventure, Policy, state).StateHash);
        session.Step(new CombatCommand(CombatCommandKind.Move, X: 1));
        Assert.True(ProductionReplayRunner.Run(Combat, Adventure, Policy, session.CaptureReplay()).Success);
        var malformed = JsonNode.Parse(json)!; malformed["state"]!["adventure"]!["materials"] = 999;
        Assert.Throws<InvalidDataException>(() => PhaseTwoMigration.Read(malformed.ToJsonString(), Combat, Adventure, Policy));
        var withStash = JsonNode.Parse(json)!;
        var extra = withStash["state"]!["adventure"]!["godwrought"]![0]!.DeepClone(); extra["instanceId"] = "ashcleaver.stashed";
        withStash["state"]!["adventure"]!["godwrought"]!.AsArray().Add(extra);
        withStash["stateHash"] = JsonData.Hash(JsonData.Read<System.Text.Json.JsonElement>(withStash["state"]!.ToJsonString()));
        var migratedStash = PhaseTwoMigration.Read(withStash.ToJsonString(), Combat, Adventure, Policy);
        Assert.Contains(migratedStash.Capture().Progression.Character.Items, i => i.LegacyInstanceId == "ashcleaver.stashed");
        var outsideHub = JsonNode.Parse(json)!;
        outsideHub["state"]!["encounterId"] = "encounter.ossuary";
        outsideHub["stateHash"] = JsonData.Hash(JsonData.Read<System.Text.Json.JsonElement>(outsideHub["state"]!.ToJsonString()));
        Assert.Throws<SaveCompatibilityException>(() => PhaseTwoMigration.Read(outsideHub.ToJsonString(), Combat, Adventure, Policy));
    }
    [Fact]
    public void SavesRecoverCorruptionPreserveNewerDataAndReplayAfterSharedProfileMerge()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-production-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "character.json");
        try
        {
            var session = ProductionSession.Create(Combat, Adventure, Policy);
            ProductionSaveStore.Write(path, Combat, Adventure, Policy, session.Capture());
            session.Step(new CombatCommand(CombatCommandKind.Move, X: 1));
            ProductionSaveStore.Write(path, Combat, Adventure, Policy, session.Capture());
            File.WriteAllText(path, "{\"schemaVersion\":1,\"stateHash\":\"broken\",\"state\":null}");
            Assert.True(ProductionSaveStore.Load(path, Combat, Adventure, Policy).RecoveredBackup);
            ProductionSaveStore.Write(path, Combat, Adventure, Policy, session.Capture());
            var shared = session.Capture().Progression.Profile; shared.Unlocks.Add("profile.memory_cartography");
            LocalProfileStore.Merge(ProductionSaveStore.ProfilePath(path), session.Content, shared);
            var loaded = ProductionSaveStore.Load(path, Combat, Adventure, Policy).Session;
            Assert.Contains("profile.memory_cartography", loaded.Capture().Progression.Profile.Unlocks);
            loaded.Step(new CombatCommand(CombatCommandKind.Move, Z: 1));
            Assert.True(ProductionReplayRunner.Run(Combat, Adventure, Policy, loaded.CaptureReplay()).Success);
            string before = File.ReadAllText(path); var newer = JsonNode.Parse(before)!; newer["schemaVersion"] = 99; newer["futureMetadata"] = "not understood"; string newerJson = newer.ToJsonString(); File.WriteAllText(path, newerJson);
            Assert.Throws<SaveCompatibilityException>(() => ProductionSaveStore.Load(path, Combat, Adventure, Policy));
            Assert.Throws<SaveCompatibilityException>(() => ProductionSaveStore.Write(path, Combat, Adventure, Policy, session.Capture()));
            Assert.Equal(newerJson, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("expedition")]
    [InlineData("combat")]
    [InlineData("character")]
    [InlineData("content")]
    public void NestedFutureFormatsWithUnknownFieldsArePreservedBeforeStrictParsing(string kind)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-future-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var session = ProductionSession.Create(Combat, Adventure, Policy);
            ProductionSaveStore.Write(path, Combat, Adventure, Policy, session.Capture());
            ProductionSaveStore.Write(path, Combat, Adventure, Policy, session.Capture());
            var future = JsonNode.Parse(File.ReadAllText(path))!;
            var target = kind switch
            {
                "combat" => future["state"]!["expedition"]!["combat"]!,
                "character" => future["state"]!["progression"]!["character"]!,
                _ => future["state"]!["expedition"]!
            };
            if (kind == "content") target["adventureHash"] = "FUTURE_CONTENT";
            else target["schemaVersion"] = 99;
            target["futureField"] = true; string original = future.ToJsonString(); File.WriteAllText(path, original);
            Assert.Throws<SaveCompatibilityException>(() => ProductionSaveStore.Load(path, Combat, Adventure, Policy));
            Assert.Throws<SaveCompatibilityException>(() => ProductionSaveStore.Write(path, Combat, Adventure, Policy, session.Capture()));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ReservedProfileNamesAndConcurrentCharacterWritersCannotClobberFiles()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "character.json");
        try
        {
            var session = ProductionSession.Create(Combat, Adventure, Policy);
            foreach (string name in new[] { "profile.json", "PROFILE.JSON.bak", "profile.json.lock" })
                Assert.Throws<ArgumentException>(() => ProductionSaveStore.Write(Path.Combine(directory, name), Combat, Adventure, Policy, session.Capture()));
            Assert.Empty(Directory.GetFiles(directory));
            ProductionSaveStore.Write(path, Combat, Adventure, Policy, session.Capture()); string original = File.ReadAllText(path);
            using (var lease = new FileStream(path + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.Throws<IOException>(() => ProductionSaveStore.Write(path, Combat, Adventure, Policy, session.Capture()));
            Assert.Equal(original, File.ReadAllText(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}
