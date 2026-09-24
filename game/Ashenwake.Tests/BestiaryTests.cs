using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class BestiaryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ashenwake-bestiary-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(directory, "wanderer.save.json");
    private string Sidecar => BestiaryStore.PathFor(SavePath);
    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));
    private static readonly EndgameContent Endgame = EndgameContent.Parse(Read("endgame.json"));
    private static readonly CombatContent Combat = CombatContent.Parse(EndgameCombatContent.Parse(
        CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson);
    private static readonly BestiaryCatalog Catalog = BestiaryCatalog.Build(Combat, CampaignContent.Parse(Read("campaign.json")).Capture(), Endgame.Capture());
    private static BestiaryMemory Empty => Bestiary.Empty("wanderer");
    private static BestiaryDefeat Kill(int token, string id = "enemy.ash_ghoul", bool elite = false) => new(token.ToString("X64"), id, elite);
    private static BestiaryMemory Record(params BestiaryDefeat[] defeats) => Bestiary.Observe(Empty, Catalog, [], defeats);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public void CatalogIncludesCreaturesAndChampionAliasesButNotMechanisms()
    {
        Assert.Equal(Combat.Enemies.Count(e => e.Role is not ("Anchor" or "Bell") && e.Id is not ("enemy.bell_saint" or "enemy.bell_beast")) + 3, Catalog.Entries.Count);
        Assert.Equal(3, Catalog.Entries.Count(e => e.Kind == BestiaryKind.Champion));
        Assert.Null(Catalog.Resolve("enemy.ritual_anchor")); Assert.Null(Catalog.Resolve("enemy.broken_bell"));
        foreach (var champion in RoamingChampionCatalog.Definitions)
        {
            Assert.Equal(champion.Id, Catalog.Resolve(champion.PrimaryEnemyId, champion.EncounterId)!.Id);
            Assert.Equal(champion.PrimaryEnemyId, Catalog.Resolve(champion.PrimaryEnemyId, "campaign.road")!.Id);
            Assert.Equal(champion.RewardItemId, Assert.Single(Catalog.Find(champion.Id)!.RewardItemIds));
        }
        Assert.All(Catalog.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Lore)); Assert.NotEmpty(e.Regions); Assert.NotEmpty(e.CombatNotes);
            Assert.Equal(Combat.Enemies.Single(c => c.Id == e.EnemyId).Armor, e.ArmorBasisPoints); Assert.Equal(0, e.ResistanceBasisPoints);
            Assert.All(e.RewardItemIds, id => Assert.Contains(LegendaryCollectionCatalog.Entries, c => c.ItemId == id));
        });
    }
    [Fact]
    public void SoloRosterExcludesLegacySliceFormsAndEveryFamilyHasAnActualSpawnRoute()
    {
        Assert.Null(Catalog.Find("enemy.bell_saint")); Assert.Null(Catalog.Find("enemy.bell_beast"));
        Assert.NotNull(Catalog.Find("boss.bell_saint"));
        var routes = Combat.Campaign!.Encounters.SelectMany(e => e.Spawns.Select(s => s.EnemyId))
            .Concat(Combat.Endgame!.Packs.SelectMany(p => p.EnemyIds))
            .Concat(Combat.Endgame.HuntPhases.Select(p => p.BossId))
            .Concat(Endgame.Capture().BossFamilies.Select(f => "boss.fracture_" + f.ToLowerInvariant()))
            // GenerateFracture adds these two creatures alongside each final-room boss.
            .Concat(new[] { "enemy.ash_ghoul", "enemy.cinder_acolyte" }).ToHashSet(StringComparer.Ordinal);
        Assert.All(Catalog.Entries, e => Assert.Contains(e.EnemyId, routes));
    }
    [Theory]
    [InlineData("act.grey_march", LegendaryEquipment.Pyre)]
    [InlineData("act.verdant_maw", LegendaryEquipment.Widow)]
    [InlineData("act.cinder_reach", LegendaryEquipment.Furnace)]
    [InlineData("act.shattered_spine", LegendaryEquipment.Oath)]
    [InlineData("act.hollow_night", LegendaryEquipment.Hour)]
    public void EveryFractureBossCanGuardEveryRegionSpecificFinalReward(string region, string reward)
    {
        var composer = EndgameCombatContent.FromComposed(JsonData.Write(Combat));
        foreach (string family in Endgame.Capture().BossFamilies)
        {
            var manifest = composer.CreateFractureManifest(new(1, 42, region, 1, ["fracture.burning_haste"], family, "Materials"), 1);
            Assert.Equal(region, manifest.Region);
            string bossId = Assert.Single(manifest.Rooms[^1].Spawns, s => s.EnemyId.StartsWith("boss.", StringComparison.Ordinal)).EnemyId;
            Assert.Equal("boss.fracture_" + family.ToLowerInvariant(), bossId);
            var entry = Catalog.Find(bossId)!;
            Assert.Contains(reward, entry.RewardItemIds);
            Assert.Equal(5, entry.RewardItemIds.Length);
            Assert.DoesNotContain(LegendaryEquipment.Crown, entry.RewardItemIds); // Spine room 2, never the final boss.
            Assert.DoesNotContain(EquipmentSets.VigilChest, entry.RewardItemIds); // Set pieces also belong to earlier rooms.
            Assert.Contains(entry.CombatNotes, note => note.Contains("treasure follows the region", StringComparison.Ordinal));
        }
    }
    [Fact]
    public void DiscoveryAndDefeatGateAllSpoilersAndReturnDetachedViews()
    {
        foreach (var view in Bestiary.Project(Empty, Catalog))
        {
            Assert.False(view.Discovered); Assert.Equal("Undiscovered creature", view.Name); Assert.Empty(view.EnemyId); Assert.Empty(view.Role);
            Assert.Empty(view.Lore); Assert.Empty(view.Regions); Assert.Empty(view.CombatNotes); Assert.Empty(view.RewardItemIds); Assert.Empty(view.HuntIds);
            Assert.Equal(-1, view.ArmorBasisPoints); Assert.Equal(-1, view.ResistanceBasisPoints); Assert.Equal(0, view.Defeats);
        }
        const string id = "boss.hunt_orrun";
        var seen = Bestiary.Observe(Empty, Catalog, [id], []);
        var entry = Bestiary.Project(seen, Catalog).Single(e => e.Id == id);
        Assert.True(entry.Discovered); Assert.False(entry.Defeated); Assert.NotEmpty(entry.Lore); Assert.NotEmpty(entry.EnemyId);
        Assert.Empty(entry.CombatNotes); Assert.Empty(entry.RewardItemIds); Assert.Empty(entry.HuntIds); Assert.Equal(-1, entry.ArmorBasisPoints);
        var killed = Bestiary.Observe(seen, Catalog, [], [Kill(1, id, true)]);
        var known = Bestiary.Project(killed, Catalog).Single(e => e.Id == id);
        Assert.True(known.Defeated); Assert.NotEmpty(known.CombatNotes); Assert.Contains("hunt.orrun_without_oath", known.HuntIds);
        known.Regions[0] = "changed"; Assert.DoesNotContain("changed", Catalog.Find(id)!.Regions);
        Assert.Empty(seen.Defeats); Assert.Equal(1, known.EliteDefeats);
    }
    [Fact]
    public void ObservationsArePureAndRepeatedOrReloadedDeathTokensNeverAddCounts()
    {
        var session = ProductionSession.Create(Read("combat.json"), AdventureContent.Parse(Read("adventure.json")), ProgressionContent.Parse(Read("progression.json")));
        string state = session.StateHash, replay = JsonData.Hash(session.CaptureReplay());
        var first = Record(Kill(1), Kill(1), Kill(2, elite: true));
        string original = JsonData.Hash(first);
        var repeated = Bestiary.Observe(first, Catalog, ["enemy.cinder_priest"], [Kill(1), Kill(2, elite: true)]);
        Assert.Equal(original, JsonData.Hash(first)); Assert.Equal(2, repeated.Defeats.Length);
        var loaded = BestiaryStore.Load(SavePath, "wanderer", Catalog); Assert.True(loaded.CanWrite);
        BestiaryStore.Write(SavePath, repeated, Catalog);
        loaded = BestiaryStore.Load(SavePath, "wanderer", Catalog);
        Assert.Equal(2, Bestiary.Observe(loaded.Memory, Catalog, [], [Kill(1)]).Defeats.Length);
        Assert.Equal(state, session.StateHash); Assert.Equal(replay, JsonData.Hash(session.CaptureReplay()));
    }
    [Fact]
    public void ConcurrentStaleWritersMergeUnionWithoutLosingDiscoveriesOrDoubleCounting()
    {
        BestiaryStore.Write(SavePath, Record(Kill(1)), Catalog);
        var result = BestiaryStore.Write(SavePath, Record(Kill(2, "enemy.cinder_priest", true)), Catalog);
        result = BestiaryStore.Write(SavePath, Record(Kill(1)), Catalog);
        Assert.Equal(2, result.Defeats.Length); Assert.Contains("enemy.cinder_priest", result.DiscoveredEntries);
        string original = File.ReadAllText(Sidecar);
        BestiaryStore.Write(SavePath, result, Catalog); Assert.Equal(original, File.ReadAllText(Sidecar));
        Assert.Throws<InvalidDataException>(() => BestiaryStore.Write(SavePath, Record(Kill(1, "enemy.cinder_priest")), Catalog));
        Assert.Equal(original, File.ReadAllText(Sidecar));
    }
    [Fact]
    public void TokenNormalizationAndValidationRejectConflictsAndMalformedMemory()
    {
        var lowercase = new BestiaryDefeat(new string('a', 64), "enemy.ash_ghoul", false);
        var memory = Record(lowercase, lowercase with { Token = lowercase.Token.ToUpperInvariant() });
        Assert.Equal(new string('A', 64), Assert.Single(memory.Defeats).Token);
        Assert.Throws<InvalidDataException>(() => Bestiary.Observe(memory, Catalog, [], [lowercase with { IsElite = true }]));
        Assert.Throws<InvalidDataException>(() => Record(Kill(1) with { Token = "not-a-hash" }));
        Assert.Throws<InvalidDataException>(() => Record(Kill(1, "enemy.unknown")));
        Assert.Throws<InvalidDataException>(() => Bestiary.Observe(Empty, Catalog, ["enemy.ritual_anchor"], []));
        Assert.Throws<InvalidDataException>(() => Bestiary.Validate(memory with { DiscoveredEntries = [] }, Catalog));
        Assert.Throws<InvalidDataException>(() => Bestiary.Validate(memory with { Defeats = [memory.Defeats[0], memory.Defeats[0]] }, Catalog));
        Assert.Throws<InvalidDataException>(() => Bestiary.Empty(""));
    }
    [Fact]
    public void FullLedgerPreservesEarlierDedupAndStillDiscoversNewCreatures()
    {
        var full = Record(Enumerable.Range(1, Bestiary.MaximumDefeats).Select(i => Kill(i)).ToArray());
        Assert.False(full.RecordLimitReached);
        var capped = Bestiary.Observe(full, Catalog, ["enemy.cinder_priest"], [Kill(Bestiary.MaximumDefeats + 1, "enemy.cinder_priest")]);
        Assert.True(capped.RecordLimitReached); Assert.Equal(Bestiary.MaximumDefeats, capped.Defeats.Length);
        Assert.Contains("enemy.cinder_priest", capped.DiscoveredEntries);
        Assert.Equal(JsonData.Hash(capped), JsonData.Hash(Bestiary.Observe(capped, Catalog, [], [Kill(1)])));
        BestiaryStore.Write(SavePath, capped, Catalog);
        Assert.True(new FileInfo(Sidecar).Length < 4 * 1024 * 1024);
        var loaded = BestiaryStore.Load(SavePath, "wanderer", Catalog);
        Assert.True(loaded.CanWrite); Assert.Equal(JsonData.Hash(capped), JsonData.Hash(loaded.Memory));
    }
    [Fact]
    public void MissingMemoryDoesNotCreateFilesAndCharacterFilenamesAreIsolated()
    {
        Assert.True(BestiaryStore.Load(SavePath, "wanderer", Catalog).CanWrite); Assert.False(Directory.Exists(directory));
        BestiaryStore.Write(SavePath, Record(Kill(1)), Catalog);
        string other = Path.Combine(directory, "echo2.save.json");
        Assert.Empty(BestiaryStore.Load(other, "wanderer", Catalog).Memory.DiscoveredEntries);
        File.Copy(Sidecar, BestiaryStore.PathFor(other));
        var result = BestiaryStore.Load(other, "wanderer", Catalog); Assert.False(result.CanWrite); Assert.Empty(result.Memory.DiscoveredEntries);
        Assert.Throws<IOException>(() => BestiaryStore.Write(other, Empty, Catalog));
        Assert.False(BestiaryStore.Load(SavePath, "other-character", Catalog).CanWrite);
    }
    [Fact]
    public void GoodBackupRecoversCorruptPrimaryWithoutRepairUntilWrite()
    {
        BestiaryStore.Write(SavePath, Record(Kill(1)), Catalog); BestiaryStore.Write(SavePath, Record(Kill(2)), Catalog);
        string backup = File.ReadAllText(Sidecar + ".bak"); File.WriteAllText(Sidecar, "broken");
        var loaded = BestiaryStore.Load(SavePath, "wanderer", Catalog);
        Assert.True(loaded.RecoveredBackup); Assert.True(loaded.CanWrite); Assert.Single(loaded.Memory.Defeats);
        Assert.Equal("broken", File.ReadAllText(Sidecar));
        BestiaryStore.Write(SavePath, loaded.Memory, Catalog); Assert.Equal(backup, File.ReadAllText(Sidecar + ".bak"));
        Assert.False(BestiaryStore.Load(SavePath, "wanderer", Catalog).RecoveredBackup);
    }
    [Theory]
    [InlineData("future")]
    [InlineData("foreign")]
    [InlineData("unknown-catalog")]
    [InlineData("duplicate")]
    public void ProtectedPrimaryNeverFallsBackOrOverwrites(string variant)
    {
        BestiaryStore.Write(SavePath, Record(Kill(1)), Catalog); BestiaryStore.Write(SavePath, Record(Kill(2)), Catalog);
        var node = JsonNode.Parse(File.ReadAllText(Sidecar))!;
        if (variant == "future") node["schemaVersion"] = 99;
        if (variant == "foreign") node["saveIdentity"] = "foreign";
        if (variant == "unknown-catalog") node["memory"]!["discoveredEntries"]![0] = "enemy.future";
        string bytes = node.ToJsonString(); if (variant == "duplicate") bytes = bytes.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1");
        File.WriteAllText(Sidecar, bytes);
        var loaded = BestiaryStore.Load(SavePath, "wanderer", Catalog);
        Assert.False(loaded.CanWrite); Assert.False(loaded.RecoveredBackup); Assert.Empty(loaded.Memory.DiscoveredEntries);
        Assert.Throws<IOException>(() => BestiaryStore.Write(SavePath, Empty, Catalog)); Assert.Equal(bytes, File.ReadAllText(Sidecar));
    }
    [Theory]
    [InlineData("invalid-json")]
    [InlineData("oversized")]
    [InlineData("hash")]
    [InlineData("null")]
    public void UnrecoverableMemoryIsPreserved(string variant)
    {
        BestiaryStore.Write(SavePath, Record(Kill(1)), Catalog);
        string bytes = variant switch { "oversized" => new string('x', 4 * 1024 * 1024 + 1), "hash" => File.ReadAllText(Sidecar).Replace("enemy.ash_ghoul", "enemy.cinder_priest"), "null" => "null", _ => "bad" };
        File.WriteAllText(Sidecar, bytes);
        var result = BestiaryStore.Load(SavePath, "wanderer", Catalog); Assert.False(result.CanWrite);
        Assert.Throws<IOException>(() => BestiaryStore.Write(SavePath, Empty, Catalog)); Assert.Equal(bytes, File.ReadAllText(Sidecar));
    }
    [Fact]
    public void ForeignBackupAndSymbolicLinksAreProtected()
    {
        BestiaryStore.Write(SavePath, Record(Kill(1)), Catalog);
        var node = JsonNode.Parse(File.ReadAllText(Sidecar))!; node["schemaVersion"] = 99; File.WriteAllText(Sidecar + ".bak", node.ToJsonString());
        Assert.False(BestiaryStore.Load(SavePath, "wanderer", Catalog).CanWrite);
        File.Delete(Sidecar + ".bak"); string destination = Path.Combine(directory, "target.json"); File.Move(Sidecar, destination);
        File.CreateSymbolicLink(Sidecar, destination);
        Assert.False(BestiaryStore.Load(SavePath, "wanderer", Catalog).CanWrite); Assert.Throws<IOException>(() => BestiaryStore.Write(SavePath, Empty, Catalog));
        File.Delete(Sidecar); File.Delete(Sidecar + ".lock"); File.CreateSymbolicLink(Sidecar + ".lock", destination);
        Assert.Throws<IOException>(() => BestiaryStore.Write(SavePath, Empty, Catalog));
    }
}
