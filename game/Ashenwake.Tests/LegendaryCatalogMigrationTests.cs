using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class LegendaryCatalogMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    // These maintained fixtures also describe the exact catalog immediately before legendaries.
    private static ProgressionContent PreviousPolicy => ProgressionContent.Parse(Read("fixtures/progression-phase4.json"));
    private static CampaignContent Campaign => CampaignContent.Parse(Read("campaign.json"));
    private static EndgameContent Endgame => EndgameContent.Parse(Read("endgame.json"));
    private static ExperimentContent Experiment => ExperimentContent.Parse(Read("experiments.json"));
    private static string Base(bool previous) => Read(previous ? "fixtures/combat-phase4.json" : "combat.json");
    private static string Journey(bool previous) => CampaignCombatContent.Parse(Base(previous), Read("campaign-combat.json")).CombatJson;
    private static string Combat(bool previous) => EndgameCombatContent.Parse(Journey(previous), Read("endgame-combat.json"), Endgame).CombatJson;
    private static EndgameRuntimeSession PreviousEndgame() => EndgameRuntimeMigration.ImportPhaseFour(
        Read("fixtures/phase4-campaign-complete.json"), Journey(true), Combat(true), Adventure, PreviousPolicy, Campaign, Endgame);
    private static string Save(EndgameRuntimeSession session) => JsonData.Write(new EndgameRuntimeSave(1, session.StateHash, session.Capture()));
    private static EndgameRuntimeSession Upgrade(string json) => EndgameRuntimeSaveStore.Read(Combat(false), Adventure, Policy, Campaign, Endgame, json);

    private static void SameExceptCatalogs<T>(T before, T after, string oldCombat, string newCombat, string oldProgression, string newProgression)
    {
        Assert.NotEqual(oldCombat, newCombat); Assert.NotEqual(oldProgression, newProgression);
        var node = JsonNode.Parse(JsonData.Write(before))!;
        void Rebind(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key == "contentHash" && pair.Value is JsonValue value && value.TryGetValue<string>(out var identity))
                    {
                        if (identity == oldCombat) obj[pair.Key] = newCombat;
                        else if (identity == oldProgression) obj[pair.Key] = newProgression;
                    }
                    else Rebind(pair.Value);
                }
            }
            else if (current is JsonArray array) foreach (var child in array) Rebind(child);
        }
        Rebind(node);
        Assert.Equal(node.ToJsonString(), JsonNode.Parse(JsonData.Write(after))!.ToJsonString());
    }

    [Fact]
    public void ProductionAndActiveCampaignRetainEveryLogicalFieldExceptCatalogIdentities()
    {
        var production = ProductionSession.Create(Base(true), Adventure, PreviousPolicy);
        for (int i = 0; i < 400 && production.View.RoomId == "room.greyhaven"; i++)
            Assert.True(production.Execute(ProductionSmoke.Next(production)).Success);
        Assert.NotEqual("room.greyhaven", production.View.RoomId);
        for (int i = 0; i < 15; i++) production.Step(new CombatCommand(CombatCommandKind.Move, X: 1));
        var loadedProduction = ProductionSaveStore.Read(Base(false), Adventure, Policy,
            JsonData.Write(new ProductionSave(1, production.StateHash, production.Capture())));
        SameExceptCatalogs(production.Capture(), loadedProduction.Capture(), production.Combat.ContentHash,
            loadedProduction.Combat.ContentHash, production.Content.Hash, loadedProduction.Content.Hash);

        var campaign = CampaignRuntimeSession.Create(Journey(true), Adventure, PreviousPolicy, Campaign);
        Assert.True(campaign.EnterAct(1).Success);
        for (int i = 0; i < 25; i++) Assert.True(campaign.Step(new CombatCommand(CombatCommandKind.Move, X: 1)).Success);
        Assert.False(campaign.InHub);
        var loadedCampaign = CampaignRuntimeSaveStore.Read(Journey(false), Adventure, Policy, Campaign,
            JsonData.Write(new CampaignRuntimeSave(1, campaign.StateHash, campaign.Capture())));
        SameExceptCatalogs(campaign.Capture(), loadedCampaign.Capture(), campaign.Combat.ContentHash,
            loadedCampaign.Combat.ContentHash, campaign.Production.Content.Hash, loadedCampaign.Production.Content.Hash);
        Assert.Equal(loadedCampaign.StateHash, CampaignRuntimeSession.Restore(Journey(false), Adventure, Policy, Campaign, loadedCampaign.Capture()).StateHash);
    }

    [Fact]
    public void MaintainedPhaseThreeImportsWithExpandedPolicyAndPreservesOwnedItems()
    {
        string original = Read("fixtures/phase3-production-hub.json");
        var before = JsonData.Read<ProductionSave>(original).State;
        var session = CampaignRuntimeMigration.ImportPhaseThree(original, Read("fixtures/combat-phase3.json"), Journey(false), Adventure, Policy, Campaign);
        Assert.True(session.InHub);
        Assert.Equal(JsonData.Hash(before.Progression.Character.Items), JsonData.Hash(session.Production.Capture().Progression.Character.Items));
        Assert.Equal(before.Progression.Character.Materials, session.Production.ProgressionView.Materials);
        Assert.Equal(before.Progression.Character.Experience, session.Production.ProgressionView.Experience);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveFractureAndGodHuntRetainManifestCombatProgressAndReplayOrigin(bool hunt)
    {
        var original = hunt ? EndgameRuntimeSaveStore.Read(Combat(true), Adventure, PreviousPolicy, Campaign, Endgame,
            Read("fixtures/phase5-endgame-complete.json")) : PreviousEndgame();
        for (int i = 0; i < 500 && original.InHub; i++)
            Assert.True(original.Execute(hunt ? EndgameRuntimeSmoke.AtGate(original,
                new(EndgameRuntimeAction.StartGodHunt, Id: "hunt.false_vael")) : EndgameRuntimeSmoke.Next(original)).Success);
        Assert.False(original.InHub);
        Assert.Equal(hunt ? "GodHunt" : "Fracture", original.RunView!.Kind);
        for (int i = 0; i < 20; i++) Assert.True(original.Step().Success);
        var loaded = Upgrade(Save(original));
        SameExceptCatalogs(original.Capture(), loaded.Capture(), original.Combat.ContentHash,
            loaded.Combat.ContentHash, original.Production.Content.Hash, loaded.Production.Content.Hash);
        Assert.True(loaded.Step().Success);
        Assert.True(EndgameRuntimeReplayRunner.Run(Combat(false), Adventure, Policy, Campaign, Endgame, loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void BoundEchoesRunRetainsBorrowedMemoryAndPermanentCharacter()
    {
        var original = ExperimentRuntimeSession.FromEndgame(Combat(true), Adventure, PreviousPolicy, Campaign, Endgame, Experiment, PreviousEndgame().Capture());
        for (int i = 0; i < 4000 && original.View.Memory?.Status != "Bound"; i++)
            Assert.True(original.Execute(ExperimentRuntimeSmoke.Next(original)).Success);
        Assert.Equal("Bound", original.View.Memory?.Status);
        var loaded = ExperimentSaveStore.Read(Combat(false), Adventure, Policy, Campaign, Endgame, Experiment,
            JsonData.Write(new ExperimentSave(1, original.StateHash, original.Capture())));
        SameExceptCatalogs(original.Capture(), loaded.Capture(), original.Combat.ContentHash,
            loaded.Combat.ContentHash, original.Production.Content.Hash, loaded.Production.Content.Hash);
        Assert.True(loaded.Step().Success);
        Assert.True(ExperimentReplayRunner.Run(Combat(false), Adventure, Policy, Campaign, Endgame, Experiment, loaded.CaptureReplay()).Success);
    }

    [Fact]
    public void FirstWriteBacksUpExactPreviousCharacterAndProfileBytesAndRepeatedLoadsAreStable()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-legendary-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = PreviousEndgame();
            EndgameRuntimeSaveStore.Write(path, Combat(true), Adventure, PreviousPolicy, Campaign, Endgame, original.Capture());
            string oldSave = File.ReadAllText(path), profilePath = EndgameRuntimeSaveStore.ProfilePath(path), oldProfile = File.ReadAllText(profilePath);
            var loaded = EndgameRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign, Endgame);
            Assert.False(loaded.RecoveredBackup); Assert.Equal(oldSave, File.ReadAllText(path)); Assert.Equal(oldProfile, File.ReadAllText(profilePath));
            Assert.Equal(JsonData.Hash(original.Production.Capture().Progression.Profile), JsonData.Hash(loaded.Session.Production.Capture().Progression.Profile));
            EndgameRuntimeSaveStore.Write(path, Combat(false), Adventure, Policy, Campaign, Endgame, loaded.Session.Capture());
            Assert.Equal(oldSave, File.ReadAllText(path + ".bak")); Assert.Equal(oldProfile, File.ReadAllText(profilePath + ".bak"));
            Assert.Equal(loaded.Session.StateHash, EndgameRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign, Endgame).Session.StateHash);
            // Recovery also validates and upgrades a previous-catalog backup without modifying it.
            File.WriteAllText(path, "{");
            var recovered = EndgameRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign, Endgame);
            Assert.True(recovered.RecoveredBackup); Assert.Equal(loaded.Session.StateHash, recovered.Session.StateHash);
            Assert.Equal(oldSave, File.ReadAllText(path + ".bak"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("invalid-state")]
    [InlineData("future-version")]
    [InlineData("unknown-catalog")]
    public void OldArchiveIsAuthenticatedAndValidatedBeforeAnyCatalogRebinding(string kind)
    {
        var original = PreviousEndgame();
        var node = JsonNode.Parse(Save(original))!;
        switch (kind)
        {
            case "checksum": node["stateHash"] = new string('0', 64); break;
            case "invalid-state":
                node["state"]!["campaign"]!["production"]!["progression"]!["character"]!["materials"] = -1;
                node["stateHash"] = JsonData.Hash(JsonData.Read<EndgameRuntimeSnapshot>(node["state"]!.ToJsonString())); break;
            case "future-version": node["state"]!["campaign"]!["combat"]!["schemaVersion"] = 2; break;
            case "unknown-catalog": node["state"]!["campaign"]!["combat"]!["contentHash"] = new string('0', 64); break;
        }
        string source = node.ToJsonString();
        if (kind is "future-version" or "unknown-catalog") Assert.Throws<SaveCompatibilityException>(() => Upgrade(source));
        else Assert.Throws<InvalidDataException>(() => Upgrade(source));
        Assert.Equal(source, node.ToJsonString());
    }

    [Fact]
    public void ProfileUpgradeRejectsUnknownCatalogFutureSchemaAndInvalidOriginalChecksum()
    {
        var previous = PreviousEndgame(); var current = Upgrade(Save(previous));
        var profile = previous.Production.Capture().Progression.Profile;
        string json = JsonData.Write(new LocalProfileEnvelope(1, previous.Production.Content.Hash, JsonData.Hash(profile), profile));
        Assert.Equal(JsonData.Hash(profile), JsonData.Hash(LocalProfileStore.Read(current.Production.Content, json)));
        var node = JsonNode.Parse(json)!; node["contentHash"] = new string('0', 64);
        Assert.Throws<SaveCompatibilityException>(() => LocalProfileStore.Read(current.Production.Content, node.ToJsonString()));
        node = JsonNode.Parse(json)!; node["schemaVersion"] = 2;
        Assert.Throws<SaveCompatibilityException>(() => LocalProfileStore.Read(current.Production.Content, node.ToJsonString()));
        node = JsonNode.Parse(json)!; node["stateHash"] = new string('0', 64);
        Assert.Throws<InvalidDataException>(() => LocalProfileStore.Read(current.Production.Content, node.ToJsonString()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedPreviousArchiveNeverFallsBackToBackupOrOverwritesEitherFile(bool future)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-legendary-preserve-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = PreviousEndgame(); var upgraded = Upgrade(Save(original));
            EndgameRuntimeSaveStore.Write(path, Combat(true), Adventure, PreviousPolicy, Campaign, Endgame, original.Capture());
            EndgameRuntimeSaveStore.Write(path, Combat(true), Adventure, PreviousPolicy, Campaign, Endgame, original.Capture());
            var node = JsonNode.Parse(File.ReadAllText(path))!;
            if (future) node["state"]!["campaign"]!["combat"]!["schemaVersion"] = 2;
            else node["state"]!["campaign"]!["combat"]!["contentHash"] = new string('0', 64);
            string unsupported = node.ToJsonString(); File.WriteAllText(path, unsupported);
            string backup = File.ReadAllText(path + ".bak"), profilePath = EndgameRuntimeSaveStore.ProfilePath(path), profile = File.ReadAllText(profilePath);
            Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy, Campaign, Endgame));
            Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Write(path, Combat(false), Adventure, Policy, Campaign, Endgame, upgraded.Capture()));
            Assert.Equal(unsupported, File.ReadAllText(path)); Assert.Equal(backup, File.ReadAllText(path + ".bak")); Assert.Equal(profile, File.ReadAllText(profilePath));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
