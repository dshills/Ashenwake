using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class PetTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly AdventureContent Adventure = AdventureContent.Parse(Read("adventure.json"));
    private static readonly ProgressionContent Policy = ProgressionContent.Parse(Read("progression.json"));
    private static readonly CampaignContent Campaign = CampaignContent.Parse(Read("campaign.json"));
    private static readonly EndgameContent Endgame = EndgameContent.Parse(Read("endgame.json"));
    private static readonly string Combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson;
    private static readonly Lazy<Dictionary<string, EndgameRuntimeSnapshot>> Sources = new(EarnSources);
    private static EndgameRuntimeSession Restore(EndgameRuntimeSnapshot snapshot) => EndgameRuntimeSession.Restore(Combat, Adventure, Policy, Campaign, Endgame, snapshot);
    private static EndgameRuntimeSession Create() => EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame);
    private static Dictionary<string, EndgameRuntimeSnapshot> EarnSources()
    {
        var result = new Dictionary<string, EndgameRuntimeSnapshot>(); var s = Create();
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && result.Count < 3; i++)
        {
            foreach (var d in PetCatalog.Definitions)
                if (!s.InHub && s.Campaign.ActiveEncounterId == d.SourceEncounterId && s.EncounterCleared && !result.ContainsKey(d.Id)) result[d.Id] = s.Capture();
            if (result.Count == 3) break;
            var command = CampaignRuntimeSmoke.Next(s.Campaign);
            var step = s.ExecuteCampaign(command); Assert.True(step.Success, step.Reason + " / " + command);
        }
        Assert.Equal(3, result.Count); return result;
    }
    private static EndgameRuntimeSession Source(string id = "pet.ashfox")
    { var s = Restore(Sources.Value[id]); Good(s, EndgameRuntimeAction.EnablePets); return s; }
    private static void Good(EndgameRuntimeSession s, EndgameRuntimeAction action, string id = "", string value = "")
    { var r = s.Execute(new(action, Id: id, Value: value)); Assert.True(r.Success, r.Reason); }
    private static void Reject(EndgameRuntimeSession s, EndgameRuntimeAction action, string id = "", string value = "")
    { string hash = s.StateHash; Assert.False(s.Execute(new(action, Id: id, Value: value)).Success); Assert.Equal(hash, s.StateHash); }
    private static void Approach(EndgameRuntimeSession s, Position target)
    {
        for (int i = 0; i < 600; i++)
        {
            var p = s.Combat.View.Actors.Single(a => a.Id == 1).Position;
            if (Position.DistanceSquared(p, target) <= 1600L * 1600 && new SpatialWorld(s.Room).HasLineOfSight(p, target)) return;
            var direction = CombatProductionSmoke.MovementDirection(p, target, s.Room);
            Assert.True(s.Step(new CombatCommand(CombatCommandKind.Move, X: direction.X, Z: direction.Z)).Success);
        }
        Assert.Fail("Pet interaction route stalled.");
    }
    [Fact]
    public void DisabledLegacyStateAndReplayRemainUnchanged()
    {
        var s = Create(); Assert.Null(s.Capture().Pets); Assert.DoesNotContain("\"pets\"", JsonData.Write(s.Capture())); Assert.Empty(s.PetInteractions);
        Reject(s, EndgameRuntimeAction.RescuePet, "pet.ashfox"); Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Assert.True(s.Step().Success); Assert.Null(s.Capture().Pets);
        Assert.True(EndgameRuntimeReplayRunner.Run(Combat, Adventure, Policy, Campaign, Endgame, s.CaptureReplay()).Success);
        Good(s, EndgameRuntimeAction.EnablePets); Assert.False(s.Pets.AutoGather); Assert.All(s.Pets.Entries, p => Assert.False(p.Rescued));
        Reject(s, EndgameRuntimeAction.RescuePet, "pet.ashfox"); Reject(s, EndgameRuntimeAction.SelectPet, "pet.ashfox");
        Reject(s, EndgameRuntimeAction.SetPetAutoGather, value: "yes");
    }
    [Theory]
    [InlineData("pet.ashfox")]
    [InlineData("pet.gloammoth")]
    [InlineData("pet.cinderbeetle")]
    public void AllThreeRescuesArePermanentCosmeticAndConfigurable(string id)
    {
        var s = Source(id); var d = PetCatalog.Find(id)!;
        Assert.Contains(s.PetInteractions, i => i.ActionId == id + ".rescue");
        Approach(s, PetCatalog.RescuePosition(s.Room));
        string combat = JsonData.Hash(s.Combat.Capture()); string progression = JsonData.Hash(s.Production.Capture().Progression);
        Good(s, EndgameRuntimeAction.RescuePet, id); Assert.Equal(combat, JsonData.Hash(s.Combat.Capture())); Assert.Equal(progression, JsonData.Hash(s.Production.Capture().Progression));
        Assert.Equal(id, s.Pets.SelectedPetId); Assert.DoesNotContain(s.PetInteractions, i => i.ActionId == id + ".rescue");
        Reject(s, EndgameRuntimeAction.RescuePet, id); Reject(s, EndgameRuntimeAction.SetPetAppearance, id, "forged");
        foreach (string name in new[] { "", "  ", "bad\nname", "[b]Markup[/b]", new string('x', 25), "hidden\u200bname" }) Reject(s, EndgameRuntimeAction.RenamePet, id, name);
        Good(s, EndgameRuntimeAction.RenamePet, id, "  Moonbeam  "); Good(s, EndgameRuntimeAction.SetPetAppearance, id, d.Appearances[1].Id);
        Good(s, EndgameRuntimeAction.DismissPet); Assert.Empty(s.Pets.SelectedPetId); Good(s, EndgameRuntimeAction.SelectPet, id);
        var p = Assert.Single(s.Pets.Entries, p => p.Rescued); Assert.Equal("Moonbeam", p.Name); Assert.Equal(d.Appearances[1].Id, p.AppearanceId);
        var view = s.Pets; view.Entries[0].Appearances[0] = new("forged", "forged"); Assert.NotEqual("forged", s.Pets.Entries[0].Appearances[0].Id);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
        Assert.True(EndgameRuntimeReplayRunner.Run(Combat, Adventure, Policy, Campaign, Endgame, s.CaptureReplay()).Success);
    }
    [Fact]
    public void ManualAndAutomaticGatherShareOneReceiptAcrossRestoreAndReplay()
    {
        var manual = Source(); string id = PetCatalog.MaterialId("campaign.road");
        Approach(manual, PetCatalog.CachePosition(manual.Room)); int before = manual.Production.ProgressionView.Materials;
        Good(manual, EndgameRuntimeAction.CollectPetMaterials, id); Assert.Equal(before + 5, manual.Production.ProgressionView.Materials);
        Reject(manual, EndgameRuntimeAction.CollectPetMaterials, id); Assert.Empty(manual.Pets.SelectedPetId);
        var auto = Source(); Approach(auto, PetCatalog.RescuePosition(auto.Room)); Good(auto, EndgameRuntimeAction.RescuePet, "pet.ashfox");
        Approach(auto, PetCatalog.CachePosition(auto.Room)); before = auto.Production.ProgressionView.Materials;
        Assert.True(auto.Step().Success); Assert.Equal(before, auto.Production.ProgressionView.Materials);
        Good(auto, EndgameRuntimeAction.SetPetAutoGather, value: "true"); Good(auto, EndgameRuntimeAction.DismissPet);
        Assert.True(auto.Step().Success); Assert.Equal(before, auto.Production.ProgressionView.Materials);
        Good(auto, EndgameRuntimeAction.SelectPet, "pet.ashfox"); Assert.Equal(before, auto.Production.ProgressionView.Materials);
        var checkpoint = auto.Capture(); var tick = new EndgameRuntimeCommand(EndgameRuntimeAction.Tick);
        Assert.True(auto.Execute(tick).Success); Assert.Equal(before + 5, auto.Production.ProgressionView.Materials);
        Assert.Equal(JsonData.Hash(manual.Capture().Campaign.Production.Progression.Character.OperationReceipts.Where(x => x.Key.StartsWith("pet.materials."))),
            JsonData.Hash(auto.Capture().Campaign.Production.Progression.Character.OperationReceipts.Where(x => x.Key.StartsWith("pet.materials."))));
        string hash = auto.StateHash; var replayed = Restore(checkpoint); Assert.True(replayed.Execute(tick).Success); Assert.Equal(hash, replayed.StateHash);
        auto = Restore(auto.Capture()); Assert.True(auto.Step().Success); Reject(auto, EndgameRuntimeAction.CollectPetMaterials, id); Assert.Equal(before + 5, auto.Production.ProgressionView.Materials);
        Assert.Single(auto.Capture().Pets!.CollectedMaterials); Assert.True(EndgameRuntimeReplayRunner.Run(Combat, Adventure, Policy, Campaign, Endgame, auto.CaptureReplay()).Success);
    }
    [Fact]
    public void ForgedOwnersCacheLedgersAndReceiptPayloadsAreRejected()
    {
        var fresh = Create().Capture();
        Assert.Throws<InvalidDataException>(() => Restore(fresh with { Pets = new() { Rescued = [new("pet.ashfox", "Ember", "ash")] } }));
        var s = Source(); var state = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(state with { Pets = new() { SelectedPetId = "pet.ashfox" } }));
        Assert.Throws<InvalidDataException>(() => Restore(state with { Pets = new() { Rescued = [new("pet.fake", "Ember", "ash")] } }));
        string id = PetCatalog.MaterialId("campaign.road");
        Assert.Throws<InvalidDataException>(() => Restore(state with { Pets = new() { CollectedMaterials = [id] } }));
        Approach(s, PetCatalog.CachePosition(s.Room)); Good(s, EndgameRuntimeAction.CollectPetMaterials, id); state = s.Capture();
        Assert.Throws<InvalidDataException>(() => Restore(state with { Pets = null }));
        Assert.Throws<InvalidDataException>(() => Restore(state with { Pets = state.Pets! with { CollectedMaterials = [] } }));
        Assert.Throws<InvalidDataException>(() => Restore(state with { Pets = state.Pets! with { CollectedMaterials = [id, id] } }));
        var forged = JsonData.Copy(state); forged.Campaign.Production.Progression.Character.OperationReceipts[id] = new string('0', 64);
        Assert.Throws<InvalidDataException>(() => Restore(forged));
        forged = JsonData.Copy(state); forged.Campaign.Production.Progression.Character.OperationReceipts["pet.materials.forged"] = new string('0', 64);
        Assert.Throws<InvalidDataException>(() => Restore(forged));
    }
    [Fact]
    public void RangeUnclearedRoomAndMaterialCapacityBlockBothCollectionRoutes()
    {
        var s = Create(); Good(s, EndgameRuntimeAction.EnablePets);
        Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.EnterAct, Act: 1)).Success);
        Assert.False(s.EncounterCleared); Assert.Empty(s.PetInteractions);
        Reject(s, EndgameRuntimeAction.RescuePet, "pet.ashfox"); Reject(s, EndgameRuntimeAction.CollectPetMaterials, PetCatalog.MaterialId("campaign.road"));
        s = Source(); var far = PetCatalog.RescuePosition(s.Room); Approach(s, far);
        string id = PetCatalog.MaterialId("campaign.road");
        // The cache lies on the opposite side of the entry, outside interaction range.
        var player = s.Combat.View.Actors.Single(a => a.Id == 1).Position;
        Assert.True(Position.DistanceSquared(player, PetCatalog.CachePosition(s.Room)) > 1800L * 1800);
        Reject(s, EndgameRuntimeAction.CollectPetMaterials, id);
        Good(s, EndgameRuntimeAction.RescuePet, "pet.ashfox");
        Approach(s, PetCatalog.CachePosition(s.Room));
        var full = s.Capture(); full.Campaign.Production.Progression.Character.Materials = 1000000; full.Campaign.Production.Expedition.Adventure.Materials = 1000000;
        s = Restore(full); Reject(s, EndgameRuntimeAction.CollectPetMaterials, id);
        Good(s, EndgameRuntimeAction.SetPetAutoGather, value: "true"); Assert.True(s.Step().Success);
        Assert.Equal(1000000, s.Production.ProgressionView.Materials); Assert.Empty(s.Capture().Pets!.CollectedMaterials);
        var directory = Path.Combine(Path.GetTempPath(), "ashenwake-pets-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "character.json"); string hash = s.StateHash;
            EndgameRuntimeSaveStore.Write(path, Combat, Adventure, Policy, Campaign, Endgame, s.Capture());
            var loaded = EndgameRuntimeSaveStore.Load(path, Combat, Adventure, Policy, Campaign, Endgame).Session;
            Assert.Equal(hash, loaded.StateHash); Assert.Equal("pet.ashfox", loaded.Pets.SelectedPetId); Assert.True(loaded.Pets.AutoGather);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void OptionalArenaAllowsCosmeticManagementButCannotGatherCampaignCaches()
    {
        var s = Source(); Approach(s, PetCatalog.RescuePosition(s.Room)); Good(s, EndgameRuntimeAction.RescuePet, "pet.ashfox");
        for (int i = 0; i < 600 && !s.InWorldEncounter; i++)
        {
            var result = s.Execute(WorldEncounterSmoke.Next(s, "event.lantern")); Assert.True(result.Success, result.Reason);
        }
        Assert.True(s.InWorldEncounter); Assert.Empty(s.PetInteractions);
        Good(s, EndgameRuntimeAction.RenamePet, "pet.ashfox", "Lantern"); Good(s, EndgameRuntimeAction.SetPetAppearance, "pet.ashfox", "ivory");
        Good(s, EndgameRuntimeAction.DismissPet); Good(s, EndgameRuntimeAction.SelectPet, "pet.ashfox"); Good(s, EndgameRuntimeAction.SetPetAutoGather, value: "true");
        int materials = s.Production.ProgressionView.Materials;
        Reject(s, EndgameRuntimeAction.CollectPetMaterials, PetCatalog.MaterialId("campaign.road"));
        Assert.True(s.Step().Success); Assert.Equal(materials, s.Production.ProgressionView.Materials); Assert.Empty(s.Capture().Pets!.CollectedMaterials);
        Assert.Equal(s.StateHash, Restore(s.Capture()).StateHash);
    }

    [Fact]
    public void CanonicalPetReachRequiresUnobstructedGeometryEvenWithinRadius()
    {
        var room = new RoomDefinition(5000, 5000, new(-500, 0), new(3000, 0), [new(-100, -700, 100, 700)]);
        var from = new Position(-500, 0); var cache = new Position(500, 0);
        Assert.True(new SpatialWorld(room).CanOccupy(from, 300)); Assert.True(new SpatialWorld(room).CanOccupy(cache, 300));
        Assert.False(PetCatalog.WithinReach(room, from, cache));
        Assert.True(PetCatalog.WithinReach(room, new(-500, 900), new(500, 900)));
        Assert.True(PetCatalog.WithinReach(room, new(-2000, 2000), new(-200, 2000)));
        Assert.False(PetCatalog.WithinReach(room, new(-2000, 2000), new(-199, 2000)));
    }
    [Fact]
    public void NonNullPetLedgerRequiresAllSerializedFields()
    {
        var s = Source(); string json = JsonData.Write(s.Capture());
        foreach (string field in new[] { "schemaVersion", "rescued", "selectedPetId", "autoGather", "collectedMaterials" })
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!; node["pets"]!.AsObject().Remove(field);
            Assert.Throws<System.Text.Json.JsonException>(() => JsonData.Read<EndgameRuntimeSnapshot>(node.ToJsonString()));
        }
    }

    [Fact]
    public void ArchivedPrePetReplayRemainsByteIdentical()
    {
        // Recorded before pets by the graphics-smoothing front-menu native diagnostic.
        string json = Read("fixtures/pets-legacy-endgame.awendgame");
        var replay = JsonData.Read<EndgameRuntimeReplay>(json);
        Assert.Null(replay.Initial.Pets); Assert.Equal(json.TrimEnd(), JsonData.Write(replay).TrimEnd());

    }

    [Fact]
    public void ArchivedPrePetSaveRetainsItsAuthoritativeStateHash()
    {
        string json = Read("fixtures/pets-legacy-save.json");
        using var document = System.Text.Json.JsonDocument.Parse(json);
        string hash = document.RootElement.GetProperty("stateHash").GetString()!;
        var state = JsonData.Read<EndgameRuntimeSnapshot>(document.RootElement.GetProperty("state").GetRawText());
        Assert.Null(state.Pets); Assert.Equal(hash, JsonData.Hash(state)); Assert.Equal(hash, Restore(state).StateHash);
    }
    [Fact]
    public void OneCharacterKeepsAllThreeCompanionsAcrossRegionChangesAndRestores()
    {
        var s = Source("pet.cinderbeetle");
        foreach (var pet in new[] { "pet.cinderbeetle", "pet.ashfox", "pet.gloammoth" })
        {
            var d = PetCatalog.Find(pet)!;
            if (s.Campaign.ActiveEncounterId != d.SourceEncounterId)
                Assert.True(s.ExecuteCampaign(new(CampaignRuntimeAction.EnterAct, Act: d.Act)).Success);
            Assert.Equal(d.SourceEncounterId, s.Campaign.ActiveEncounterId);
            Approach(s, PetCatalog.RescuePosition(s.Room)); Good(s, EndgameRuntimeAction.RescuePet, pet);
            s = Restore(s.Capture()); Assert.Contains(s.Pets.Entries, p => p.Id == pet && p.Rescued);
        }
        Assert.Equal(3, s.Pets.Entries.Count(p => p.Rescued)); Assert.Equal("pet.cinderbeetle", s.Pets.SelectedPetId);
        Good(s, EndgameRuntimeAction.SelectPet, "pet.gloammoth"); Assert.Equal("pet.gloammoth", Restore(s.Capture()).Pets.SelectedPetId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FuturePetSchemaWithUnknownFieldsPreservesPrimaryAndBackup(bool insideExperiment)
    {
        var s = Create(); Good(s, EndgameRuntimeAction.EnablePets);
        var experiments = ExperimentContent.Parse(Read("experiments.json"));
        var echoes = ExperimentRuntimeSession.FromEndgame(Combat, Adventure, Policy, Campaign, Endgame, experiments, s.Capture());
        var directory = Path.Combine(Path.GetTempPath(), "ashenwake-future-pets-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "character.json");
            void Write()
            {
                if (insideExperiment) ExperimentSaveStore.Write(path, Combat, Adventure, Policy, Campaign, Endgame, experiments, echoes.Capture());
                else EndgameRuntimeSaveStore.Write(path, Combat, Adventure, Policy, Campaign, Endgame, s.Capture());
            }
            void Load()
            {
                if (insideExperiment) ExperimentSaveStore.Load(path, Combat, Adventure, Policy, Campaign, Endgame, experiments);
                else EndgameRuntimeSaveStore.Load(path, Combat, Adventure, Policy, Campaign, Endgame);
            }
            Write(); Write();
            byte[] backup = File.ReadAllBytes(path + ".bak");
            var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            var state = insideExperiment ? node["state"]!["endgame"]! : node["state"]!;
            state["pets"]!["schemaVersion"] = 2;
            state["pets"]!["futureCompanionBond"] = "preserve this unknown field";
            File.WriteAllText(path, node.ToJsonString()); byte[] primary = File.ReadAllBytes(path);
            Assert.Throws<SaveCompatibilityException>(Load);
            Assert.Equal(primary, File.ReadAllBytes(path)); Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
            Assert.Throws<SaveCompatibilityException>(Write);
            Assert.Equal(primary, File.ReadAllBytes(path)); Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        }
        finally { Directory.Delete(directory, true); }
    }

}
