using System.Security.Cryptography;
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

public sealed class SecretLegendaryMigrationTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy(bool old) => ProgressionContent.Parse(Read(old ? "fixtures/progression-regional-hunts.json" : "progression.json"));
    private static CampaignContent Campaign => CampaignContent.Parse(Read("campaign.json"));
    private static EndgameContent Endgame => EndgameContent.Parse(Read("endgame.json"));
    private static ExperimentContent Experiment => ExperimentContent.Parse(Read("experiments.json"));
    private static string Base(bool old) => Read(old ? "fixtures/combat-regional-hunts.json" : "combat.json");
    private static readonly Lazy<string> PreviousCombat = new(() => Compose(true));
    private static readonly Lazy<string> CurrentCombat = new(() => Compose(false));
    private static string Compose(bool old) => CampaignCombatContent.Parse(Base(old), Read("campaign-combat.json")).CombatJson;
    private static string Combat(bool old) => old ? PreviousCombat.Value : CurrentCombat.Value;
    private static string EndgameCombat(bool old) => EndgameCombatContent.Parse(Combat(old), Read("endgame-combat.json"), Endgame).CombatJson;
    private static CampaignRuntimeSession RestoreOld(CampaignRuntimeSnapshot state) => CampaignRuntimeSession.Restore(Combat(true), Adventure, Policy(true), Campaign, state);
    private static string Save(CampaignRuntimeSession session) => JsonData.Write(new CampaignRuntimeSave(1, session.StateHash, session.Capture()));
    private static CampaignRuntimeSession Upgrade(string json) => CampaignRuntimeSaveStore.Read(Combat(false), Adventure, Policy(false), Campaign, json);
    private static EndgameRuntimeSession PreviousEndgame() => EndgameRuntimeMigration.ImportPhaseFour(
        Read("fixtures/phase4-campaign-complete.json"), Combat(true), EndgameCombat(true), Adventure, Policy(true), Campaign, Endgame);

    private static readonly Lazy<Dictionary<string, CampaignRuntimeSnapshot>> Checkpoints = new(() =>
    {
        var session = CampaignRuntimeSession.Create(Combat(true), Adventure, Policy(true), Campaign);
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
    public void FrozenRegionalHuntCatalogsHavePublishedBytesAndNewCatalogsOnlyAddThreeItemsAndPowers()
    {
        Assert.Equal("36F96BC37C48AAC7F966A89E40033CC74DA52DF32C5D57B430AC7EB088A051A1",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures/combat-regional-hunts.json")))));
        Assert.Equal("16158F0DEAE5D082C1F0E0D5062DA888D5D2288F04C3BA6AFD66BFFF6CF67072",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures/progression-regional-hunts.json")))));
        var before = CombatContent.Parse(Base(true)); var current = CombatContent.Parse(Base(false));
        string[] newItems = ["item.emberwake_mantle", "item.griefs_reprieve", "item.widowthorn"];
        string[] newPowers = ["property.emberwake", "property.griefs_reprieve", "property.widowthorn"];
        Assert.Equal(newItems, current.Items.Select(i => i.Id).Except(before.Items.Select(i => i.Id)).Order());
        Assert.Equal(JsonData.Hash(before), JsonData.Hash(current with { Items = current.Items.Where(i => !newItems.Contains(i.Id)).ToArray() }));
        var oldPolicy = Policy(true).Capture(); var policy = Policy(false).Capture();
        Assert.Equal(newPowers, policy.Properties.Select(p => p.Id).Except(oldPolicy.Properties.Select(p => p.Id)).Order());
        Assert.Equal(JsonData.Hash(oldPolicy), JsonData.Hash(policy with
        {
            Items = policy.Items.Where(i => !newItems.Contains(i.Id)).ToArray(),
            Properties = policy.Properties.Where(p => !newPowers.Contains(p.Id)).ToArray()
        }));
    }

    // Compare all serialized logical fields, including any future additions; only the
    // authenticated combat and permanent-policy identities are allowed to change.
    private static void SameExceptCatalogs<T>(T before, T after, string oldCombat, string newCombat, string oldPolicy, string newPolicy)
    {
        Assert.NotEqual(oldCombat, newCombat); Assert.NotEqual(oldPolicy, newPolicy);
        var expected = JsonNode.Parse(JsonData.Write(before))!;
        void Rebind(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                foreach (var pair in obj.ToArray())
                {
                    if (pair.Key == "contentHash" && pair.Value is JsonValue value && value.TryGetValue<string>(out var hash) &&
                        (hash == oldCombat || hash == oldPolicy)) obj[pair.Key] = hash == oldCombat ? newCombat : newPolicy;
                    else Rebind(pair.Value);
                }
            }
            else if (node is JsonArray array) foreach (var item in array) Rebind(item);
        }
        Rebind(expected);
        Assert.Equal(expected.ToJsonString(), JsonNode.Parse(JsonData.Write(after))!.ToJsonString());
    }

    private static void SameCampaign(CampaignRuntimeSession before, CampaignRuntimeSession after) =>
        SameExceptCatalogs(before.Capture(), after.Capture(), before.Combat.ContentHash, after.Combat.ContentHash,
            before.Production.Content.Hash, after.Production.Content.Hash);

    [Theory]
    [InlineData("hub")]
    [InlineData("campaign.road")]
    [InlineData("campaign.monastery")]
    [InlineData("exploration.widow_crypt")]
    public void PublishedRoomsRetainOwnershipReceiptsCachesPendingCastsAndReplay(string context)
    {
        var original = RestoreOld(Checkpoints.Value[context]);
        for (int i = 0; i < 24; i++) Assert.True(original.Step().Success);
        if (context == "campaign.monastery")
        {
            for (int i = 0; i < 180 && !original.Combat.Capture().Actors.Any(a => a.Pending is not null); i++)
                Assert.True(original.Execute(CampaignRuntimeSmoke.Next(original)).Success);
            Assert.Contains(original.Combat.Capture().Actors, a => a.Pending is not null);
        }
        var replay = original.CaptureReplay(); string replayBytes = JsonData.Write(replay);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(true), Adventure, Policy(true), Campaign, replay).Success);
        string json = Save(original); var loaded = Upgrade(json);
        SameCampaign(original, loaded);
        if (context is "campaign.monastery" or "exploration.widow_crypt") Assert.NotEmpty(loaded.Capture().ClearedRooms!);
        Assert.Equal(loaded.StateHash, Upgrade(json).StateHash);
        Assert.Equal(loaded.StateHash, Upgrade(Save(loaded)).StateHash);
        Assert.True(loaded.Step().Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy(false), Campaign, loaded.CaptureReplay()).Success);
        Assert.True(CampaignRuntimeReplayRunner.Run(Combat(true), Adventure, Policy(true), Campaign, replay).Success);
        Assert.Throws<InvalidDataException>(() => CampaignRuntimeReplayRunner.Run(Combat(false), Adventure, Policy(false), Campaign, replay));
        Assert.Equal(replayBytes, JsonData.Write(replay)); Assert.Equal(json, Save(original));
    }

    [Fact]
    public void ExistingLegendaryOwnershipActiveChargesAndBurningGroundSurviveProductionUpgrade()
    {
        // Build a valid owned-item fixture with the existing three powers; every pickup,
        // equip and dodge below uses the ordinary production authority.
        var state = ProductionSession.Create(Base(true), Adventure, Policy(true)).Capture();
        var combat = state.Expedition.Combat; combat.Actors[0].Position = new(2000, -2000);
        foreach (string definitionId in new[] { LegendaryEquipment.Pyre, LegendaryEquipment.Oath, LegendaryEquipment.Widow })
        {
            var definition = CombatContent.Parse(Base(true)).Items.Single(i => i.Id == definitionId); long id = combat.NextObjectId++;
            combat.Loot.Add(new(id, combat.Actors[0].Position, new(id, definition.Id, definition.Name, definition.Slot, "Legendary", definition.Damage, definition.Armor, definition.CriticalBasisPoints)));
        }
        var session = ProductionSession.Restore(Base(true), Adventure, Policy(true), state);
        foreach (long id in session.Combat.View.Loot.Select(l => l.Id).ToArray())
            Assert.Contains(session.Step(new CombatCommand(CombatCommandKind.Pickup, ItemId: id)), e => e.Kind == "LootPickedUp");
        foreach (var item in session.Capture().Progression.Character.Items.Where(i => i.DefinitionId is LegendaryEquipment.Pyre or LegendaryEquipment.Oath or LegendaryEquipment.Widow).ToArray())
        {
            var slot = Enum.Parse<EquipmentSlot>(CombatContent.Parse(Base(true)).Items.Single(i => i.Id == item.DefinitionId).Slot);
            Assert.True(session.Equip(item.Id, slot).Success);
        }
        Assert.Contains(session.Step(new CombatCommand(CombatCommandKind.Dodge, Z: 1)), e => e.Kind == "LegendaryReadied");
        state = session.Capture();
        state.Expedition.Combat.Legendary!.OathCharge = 31;
        state.Expedition.Combat.Legendary.OathUntil = state.Expedition.Combat.Tick + 200;
        session = ProductionSession.Restore(Base(true), Adventure, Policy(true), state);
        Assert.Equal(3, session.Combat.View.Areas.Count); Assert.True(session.Combat.Capture().Legendary!.WidowUntil > session.Combat.Tick);
        string bytes = JsonData.Write(new ProductionSave(1, session.StateHash, session.Capture()));
        var loaded = ProductionSaveStore.Read(Base(false), Adventure, Policy(false), bytes);
        SameExceptCatalogs(session.Capture(), loaded.Capture(), session.Combat.ContentHash, loaded.Combat.ContentHash, session.Content.Hash, loaded.Content.Hash);
        Assert.Equal(31, loaded.Combat.Capture().Legendary!.OathCharge);
        for (int i = 0; i < 65; i++) loaded.Step();
        Assert.Empty(loaded.Combat.View.Areas);
        Assert.True(ProductionReplayRunner.Run(Base(false), Adventure, Policy(false), loaded.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData("Fracture")]
    [InlineData("GodHunt")]
    [InlineData("BoundMemory")]
    public void PublishedRunsRetainManifestRngProgressBorrowedMemoryAndReplay(string kind)
    {
        var original = kind == "GodHunt" ? EndgameRuntimeSaveStore.Read(EndgameCombat(true), Adventure, Policy(true), Campaign, Endgame,
            Read("fixtures/phase5-endgame-complete.json")) : PreviousEndgame();
        if (kind == "BoundMemory")
        {
            var echoes = ExperimentRuntimeSession.FromEndgame(EndgameCombat(true), Adventure, Policy(true), Campaign, Endgame, Experiment, original.Capture());
            for (int i = 0; i < 4000 && echoes.View.Memory?.Status != "Bound"; i++)
                Assert.True(echoes.Execute(ExperimentRuntimeSmoke.Next(echoes)).Success);
            Assert.Equal("Bound", echoes.View.Memory?.Status);
            var loaded = ExperimentSaveStore.Read(EndgameCombat(false), Adventure, Policy(false), Campaign, Endgame, Experiment,
                JsonData.Write(new ExperimentSave(1, echoes.StateHash, echoes.Capture())));
            SameExceptCatalogs(echoes.Capture(), loaded.Capture(), echoes.Combat.ContentHash, loaded.Combat.ContentHash,
                echoes.Production.Content.Hash, loaded.Production.Content.Hash);
            Assert.True(loaded.Step().Success);
            Assert.True(ExperimentReplayRunner.Run(EndgameCombat(false), Adventure, Policy(false), Campaign, Endgame, Experiment, loaded.CaptureReplay()).Success);
            return;
        }
        for (int i = 0; i < 500 && original.InHub; i++)
            Assert.True(original.Execute(kind == "GodHunt" ? EndgameRuntimeSmoke.AtGate(original,
                new(EndgameRuntimeAction.StartGodHunt, Id: "hunt.false_vael")) : EndgameRuntimeSmoke.Next(original)).Success);
        Assert.False(original.InHub); Assert.Equal(kind, original.RunView!.Kind);
        for (int i = 0; i < 20; i++) Assert.True(original.Step().Success);
        var upgraded = EndgameRuntimeSaveStore.Read(EndgameCombat(false), Adventure, Policy(false), Campaign, Endgame,
            JsonData.Write(new EndgameRuntimeSave(1, original.StateHash, original.Capture())));
        SameExceptCatalogs(original.Capture(), upgraded.Capture(), original.Combat.ContentHash, upgraded.Combat.ContentHash,
            original.Production.Content.Hash, upgraded.Production.Content.Hash);
        Assert.True(upgraded.Step().Success);
        Assert.True(EndgameRuntimeReplayRunner.Run(EndgameCombat(false), Adventure, Policy(false), Campaign, Endgame, upgraded.CaptureReplay()).Success);
    }

    [Fact]
    public void ProductionAuthenticatesOriginalFieldShapeBeforeInactiveDefaultsAreOmitted()
    {
        var original = ProductionSession.Create(Base(true), Adventure, Policy(true));
        var node = JsonNode.Parse(JsonData.Write(new ProductionSave(1, original.StateHash, original.Capture())))!;
        // Explicit false deserializes to the same inactive build as the absent new field.
        // It must still fail the unchanged original checksum before catalog rebinding.
        node["state"]!["expedition"]!["combat"]!["progressionBuild"]!["griefsReprieve"] = false;
        Assert.Throws<InvalidDataException>(() => ProductionSaveStore.Read(Base(false), Adventure, Policy(false), node.ToJsonString()));
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("receipt")]
    [InlineData("future-cache")]
    [InlineData("unknown-combat")]
    [InlineData("foreign-item")]
    public void OriginalArchiveMustAuthenticateBeforeAnyIdentityChanges(string kind)
    {
        var node = JsonNode.Parse(Save(RestoreOld(Checkpoints.Value["campaign.monastery"])))!;
        switch (kind)
        {
            case "checksum": node["stateHash"] = new string('0', 64); break;
            case "receipt": node["state"]!["production"]!["progression"]!["character"]!["operationReceipts"]!["campaign.encounter.campaign.road"] = new string('0', 64); break;
            case "future-cache": node["state"]!["clearedRooms"]!["campaign.road"]!["schemaVersion"] = 2; break;
            case "unknown-combat": node["state"]!["combat"]!["contentHash"] = new string('0', 64); break;
            case "foreign-item": node["state"]!["production"]!["progression"]!["character"]!["items"]![0]!["definitionId"] = "item.griefs_reprieve"; break;
        }
        if (kind != "checksum") node["stateHash"] = JsonData.Hash(JsonData.Read<CampaignRuntimeSnapshot>(node["state"]!.ToJsonString()));
        string bytes = node.ToJsonString();
        if (kind is "future-cache" or "unknown-combat") Assert.Throws<SaveCompatibilityException>(() => Upgrade(bytes));
        else Assert.Throws<InvalidDataException>(() => Upgrade(bytes));
        Assert.Equal(bytes, node.ToJsonString());
    }

    [Fact]
    public void SaveAndProfileReadWithoutWritesThenRetainExactOriginalBackups()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-secret-legendary-migration-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "character.json");
        try
        {
            var original = RestoreOld(Checkpoints.Value["campaign.monastery"]);
            CampaignRuntimeSaveStore.Write(path, Combat(true), Adventure, Policy(true), Campaign, original.Capture());
            string profilePath = CampaignRuntimeSaveStore.ProfilePath(path), save = File.ReadAllText(path), profile = File.ReadAllText(profilePath);
            var loaded = CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy(false), Campaign);
            Assert.False(loaded.RecoveredBackup); Assert.Equal(save, File.ReadAllText(path)); Assert.Equal(profile, File.ReadAllText(profilePath));
            SameCampaign(original, loaded.Session);
            CampaignRuntimeSaveStore.Write(path, Combat(false), Adventure, Policy(false), Campaign, loaded.Session.Capture());
            Assert.Equal(save, File.ReadAllText(path + ".bak")); Assert.Equal(profile, File.ReadAllText(profilePath + ".bak"));
            Assert.Equal(loaded.Session.StateHash, CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy(false), Campaign).Session.StateHash);
            File.WriteAllText(path, "{");
            var recovered = CampaignRuntimeSaveStore.Load(path, Combat(false), Adventure, Policy(false), Campaign);
            Assert.True(recovered.RecoveredBackup); Assert.Equal(loaded.Session.StateHash, recovered.Session.StateHash);
            Assert.Equal(save, File.ReadAllText(path + ".bak"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData("Tracking")]
    [InlineData("Combat")]
    public void PublishedRegionalHuntSeparateArenaMigratesWithAllLedgerFields(string stage)
    {
        var original = PreviousEndgame(); const string id = "hunt.regional.pallbearer";
        for (int i = 0; i < 5000 && original.RegionalHunts.Run?.Stage != stage; i++)
        {
            var result = original.Execute(RegionalHuntSmoke.Next(original, id));
            Assert.True(result.Success, result.Reason);
        }
        Assert.Equal(stage, original.RegionalHunts.Run?.Stage);
        string bytes = JsonData.Write(new EndgameRuntimeSave(1, original.StateHash, original.Capture()));
        var loaded = EndgameRuntimeSaveStore.Read(EndgameCombat(false), Adventure, Policy(false), Campaign, Endgame, bytes);
        SameExceptCatalogs(original.Capture(), loaded.Capture(), original.Combat.ContentHash, loaded.Combat.ContentHash,
            original.Production.Content.Hash, loaded.Production.Content.Hash);
        Assert.NotNull(loaded.Capture().RegionalHunts!.Combat);
        Assert.Equal(loaded.Combat.ContentHash, loaded.Capture().RegionalHunts!.Combat!.ContentHash);
        Assert.True(loaded.Step().Success);
        Assert.True(EndgameRuntimeReplayRunner.Run(EndgameCombat(false), Adventure, Policy(false), Campaign, Endgame, loaded.CaptureReplay()).Success);
        Assert.Equal(bytes, JsonData.Write(new EndgameRuntimeSave(1, original.StateHash, original.Capture())));
    }
}
