using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class LegendaryCollectionMemoryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ashenwake-collection-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(directory, "first.save.json");
    private string Sidecar => LegendaryCollectionStore.PathFor(SavePath);
    private static LegendaryCollectionMemory Empty => LegendaryCollection.Empty("wanderer");
    private static LegendaryCollectionMemory Discovered(params string[] ids) => new("wanderer", ids);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public void MissingMemoryLoadIsReadOnlyAndDoesNotCreateDirectory()
    {
        var loaded = LegendaryCollectionStore.Load(SavePath, "wanderer");
        Assert.True(loaded.CanWrite); Assert.False(loaded.RecoveredBackup); Assert.Empty(loaded.Memory.DiscoveredItems);
        Assert.Empty(loaded.Memory.TrackedItem); Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void ObserveRemembersActualItemsAfterSalvageAndExtractedPowersAfterItemConsumptionWithoutChangingState()
    {
        string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
        var content = ProductionContent.Resolve(Read("combat.json"), ProgressionContent.Parse(Read("progression.json")));
        var session = ProgressionSession.Create(content);
        Assert.True(session.GrantItem("grant.crown", LegendaryEquipment.Crown, ItemRarity.Legendary).Success);
        string ownedHash = session.StateHash;
        var memory = LegendaryCollection.Observe(Empty, session.Capture().Character);
        Assert.Equal(ownedHash, session.StateHash);
        Assert.True(session.Salvage("salvage.crown", 1, true).Success);
        memory = LegendaryCollection.Observe(memory, session.Capture().Character);
        Assert.Contains(LegendaryEquipment.Crown, memory.DiscoveredItems);
        // An old save whose only copy was already salvaged has no evidence to backfill.
        Assert.Empty(LegendaryCollection.Observe(Empty, session.Capture().Character).DiscoveredItems);
        foreach (string id in new[] { "mara", "torren", "cael", "oris", "kesh", "haven" })
            Assert.True(session.CompleteObjective("quest." + id, "objective." + id).Success);
        Assert.True(session.GrantItem("grant.witness", LegendaryEquipment.Witness, ItemRarity.Legendary).Success);
        Assert.True(session.Craft(new("extract.witness", CraftingService.Extraction, 2, ConfirmPermanent: true)).Success);
        string extractedHash = session.StateHash;
        memory = LegendaryCollection.Observe(memory, session.Capture().Character);
        Assert.Contains(LegendaryEquipment.Witness, memory.DiscoveredItems);
        Assert.Contains(LegendaryEquipment.Witness, LegendaryCollection.Observe(Empty, session.Capture().Character).DiscoveredItems);
        Assert.Equal(extractedHash, session.StateHash);
        Assert.Empty(session.Capture().Character.Items);
    }

    [Fact]
    public void ObservationsIgnoreQuestHistoryAndEngravedReceiversAndNeverInventTheOtherItems()
    {
        var state = new ProgressionState
        {
            Items = [new() { Id = 1, DefinitionId = "item.starter_head", Engraving = LegendaryEquipment.CrownPower }],
            CompletedObjectives = ["campaign.contract_hall", "objective.haven"]
        };
        string before = JsonData.Hash(state);
        Assert.Empty(LegendaryCollection.Observe(Empty, state).DiscoveredItems);
        state.PropertyLibrary.Add(LegendaryEquipment.HourPower);
        Assert.Equal(new[] { LegendaryEquipment.Hour }, LegendaryCollection.Observe(Empty, state).DiscoveredItems);
        state.PropertyLibrary.Remove(LegendaryEquipment.HourPower); Assert.Equal(before, JsonData.Hash(state));
    }

    [Fact]
    public void TrackingIsIndependentOfDiscoveryAndCanBeClearedWithoutMutatingInput()
    {
        var original = Discovered(LegendaryEquipment.Crown);
        var tracked = LegendaryCollection.Track(original, LegendaryEquipment.Hour);
        Assert.Empty(original.TrackedItem); Assert.Equal(LegendaryEquipment.Hour, tracked.TrackedItem);
        Assert.Equal(original.DiscoveredItems, tracked.DiscoveredItems);
        Assert.Empty(LegendaryCollection.Track(tracked, "").TrackedItem);
        Assert.Throws<ArgumentException>(() => LegendaryCollection.Track(original, "item.starter_head"));
        Assert.Throws<InvalidDataException>(() => LegendaryCollection.Observe(original, new() { CharacterId = "other" }));
    }

    [Fact]
    public void WritesPersistTrackingMergeDiscoveriesAndLeaveAuthoritativeSaveBytesUntouched()
    {
        Directory.CreateDirectory(directory); File.WriteAllText(SavePath, "untouched character archive");
        LegendaryCollectionStore.Write(SavePath, LegendaryCollection.Track(Discovered(LegendaryEquipment.Crown), LegendaryEquipment.Hour));
        var merged = LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Witness));
        Assert.Equal(2, merged.DiscoveredItems.Length); Assert.Empty(merged.TrackedItem);
        var loaded = LegendaryCollectionStore.Load(SavePath, "wanderer");
        Assert.True(loaded.CanWrite); Assert.Equal(merged.DiscoveredItems, loaded.Memory.DiscoveredItems);
        Assert.Equal("untouched character archive", File.ReadAllText(SavePath));
        Assert.True(File.Exists(Sidecar + ".bak"));
        loaded.Memory.DiscoveredItems[0] = LegendaryEquipment.Hour;
        Assert.DoesNotContain(LegendaryEquipment.Hour, LegendaryCollectionStore.Load(SavePath, "wanderer").Memory.DiscoveredItems);
    }

    [Fact]
    public void CorruptPrimaryRecoversReadOnlyThenPreservesGoodBackupOnWrite()
    {
        LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Crown));
        LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Witness));
        string backup = File.ReadAllText(Sidecar + ".bak"); File.WriteAllText(Sidecar, "{broken");
        var loaded = LegendaryCollectionStore.Load(SavePath, "wanderer");
        Assert.True(loaded.RecoveredBackup); Assert.True(loaded.CanWrite);
        Assert.Equal("{broken", File.ReadAllText(Sidecar)); Assert.Equal(backup, File.ReadAllText(Sidecar + ".bak"));
        var merged = LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Hour));
        Assert.Contains(LegendaryEquipment.Crown, merged.DiscoveredItems); Assert.Contains(LegendaryEquipment.Hour, merged.DiscoveredItems);
        Assert.Equal(backup, File.ReadAllText(Sidecar + ".bak"));
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("{\"schemaVersion\":\"wrong\"}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":2,\"schemaVersion\":1}")]
    [InlineData("null")]
    public void UnreadableOrFutureSidecarsArePreservedAndOptional(string invalid)
    {
        Directory.CreateDirectory(directory); File.WriteAllText(Sidecar, invalid);
        var loaded = LegendaryCollectionStore.Load(SavePath, "wanderer");
        Assert.False(loaded.CanWrite); Assert.Empty(loaded.Memory.DiscoveredItems); Assert.NotEmpty(loaded.Notice);
        Assert.Throws<IOException>(() => LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Crown)));
        Assert.Equal(invalid, File.ReadAllText(Sidecar)); Assert.False(File.Exists(Sidecar + ".bak"));
    }

    [Fact]
    public void FuturePrimaryDoesNotFallBackAndFutureBackupIsNeverOverwritten()
    {
        LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Crown));
        string current = File.ReadAllText(Sidecar); File.WriteAllText(Sidecar + ".bak", current);
        const string future = "{\"schemaVersion\":2}"; File.WriteAllText(Sidecar, future);
        var loaded = LegendaryCollectionStore.Load(SavePath, "wanderer");
        Assert.False(loaded.CanWrite); Assert.False(loaded.RecoveredBackup); Assert.Empty(loaded.Memory.DiscoveredItems);
        File.WriteAllText(Sidecar, current); File.WriteAllText(Sidecar + ".bak", future);
        loaded = LegendaryCollectionStore.Load(SavePath, "wanderer"); Assert.False(loaded.CanWrite);
        Assert.Contains(LegendaryEquipment.Crown, loaded.Memory.DiscoveredItems);
        Assert.Throws<IOException>(() => LegendaryCollectionStore.Write(SavePath, Empty));
        Assert.Equal(current, File.ReadAllText(Sidecar)); Assert.Equal(future, File.ReadAllText(Sidecar + ".bak"));
    }

    [Fact]
    public void DifferentSaveFilenamesAndCharacterIdentityCannotShareDiscoveriesEvenWhenSidecarIsCopied()
    {
        LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Crown));
        string other = Path.Combine(directory, "second.save.json");
        Assert.Empty(LegendaryCollectionStore.Load(other, "wanderer").Memory.DiscoveredItems);
        File.Copy(Sidecar, LegendaryCollectionStore.PathFor(other));
        Assert.False(LegendaryCollectionStore.Load(other, "wanderer").CanWrite);
        var differentCharacter = LegendaryCollectionStore.Load(SavePath, "other");
        Assert.False(differentCharacter.CanWrite); Assert.Empty(differentCharacter.Memory.DiscoveredItems);
        Assert.Throws<IOException>(() => LegendaryCollectionStore.Write(SavePath, LegendaryCollection.Empty("other")));
    }

    [Fact]
    public void MovingTheWholeSaveFolderPreservesDiscoveriesTrackingAndBackupRecovery()
    {
        Directory.CreateDirectory(directory); File.WriteAllText(SavePath, "unchanged character archive");
        var tracked = LegendaryCollection.Track(Discovered(LegendaryEquipment.Crown), LegendaryEquipment.Hour);
        LegendaryCollectionStore.Write(SavePath, tracked);
        LegendaryCollectionStore.Write(SavePath, LegendaryCollection.Track(Discovered(LegendaryEquipment.Witness), LegendaryEquipment.Hour));
        string primary = File.ReadAllText(Sidecar), backup = File.ReadAllText(Sidecar + ".bak");
        string movedDirectory = directory + "-moved";
        Directory.Move(directory, movedDirectory);
        try
        {
            string movedSave = Path.Combine(movedDirectory, Path.GetFileName(SavePath));
            string movedSidecar = LegendaryCollectionStore.PathFor(movedSave);
            var loaded = LegendaryCollectionStore.Load(movedSave, "wanderer");
            Assert.True(loaded.CanWrite); Assert.False(loaded.RecoveredBackup);
            Assert.Contains(LegendaryEquipment.Crown, loaded.Memory.DiscoveredItems);
            Assert.Contains(LegendaryEquipment.Witness, loaded.Memory.DiscoveredItems);
            Assert.Equal(LegendaryEquipment.Hour, loaded.Memory.TrackedItem);
            Assert.Equal(primary, File.ReadAllText(movedSidecar)); Assert.Equal(backup, File.ReadAllText(movedSidecar + ".bak"));
            Assert.Equal("unchanged character archive", File.ReadAllText(movedSave));
            File.WriteAllText(movedSidecar, "{broken");
            loaded = LegendaryCollectionStore.Load(movedSave, "wanderer");
            Assert.True(loaded.CanWrite); Assert.True(loaded.RecoveredBackup);
            Assert.Equal(tracked.DiscoveredItems, loaded.Memory.DiscoveredItems); Assert.Equal(tracked.TrackedItem, loaded.Memory.TrackedItem);
            LegendaryCollectionStore.Write(movedSave, loaded.Memory);
            Assert.Equal(backup, File.ReadAllText(movedSidecar + ".bak"));
        }
        finally { Directory.Move(movedDirectory, directory); }
    }

    [Theory]
    [InlineData("checksum")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    public void ChecksumAndBoundedMemoryValidationRejectMalformedData(string kind)
    {
        LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Crown));
        var root = JsonNode.Parse(File.ReadAllText(Sidecar))!;
        var memory = root["memory"]!;
        if (kind == "checksum") memory["trackedItem"] = LegendaryEquipment.Hour;
        else
        {
            if (kind == "unknown") memory["discoveredItems"] = new JsonArray("item.unknown");
            if (kind == "duplicate") memory["discoveredItems"] = new JsonArray(LegendaryEquipment.Crown, LegendaryEquipment.Crown);
            if (kind == "missing") memory.AsObject().Remove("trackedItem");
            if (kind == "null") memory["discoveredItems"] = null;
            root["stateHash"] = JsonData.Hash(System.Text.Json.JsonDocument.Parse(memory.ToJsonString()).RootElement);
        }
        string malformed = root.ToJsonString(); File.WriteAllText(Sidecar, malformed);
        Assert.False(LegendaryCollectionStore.Load(SavePath, "wanderer").CanWrite);
        Assert.Throws<IOException>(() => LegendaryCollectionStore.Write(SavePath, Empty));
        Assert.Equal(malformed, File.ReadAllText(Sidecar));
    }

    [Fact]
    public void OversizedAndInvalidUtf8SidecarsAreBoundedAndPreserved()
    {
        Directory.CreateDirectory(directory); File.WriteAllText(Sidecar, new string(' ', 16385));
        Assert.False(LegendaryCollectionStore.Load(SavePath, "wanderer").CanWrite);
        Assert.Equal(16385, new FileInfo(Sidecar).Length);
        File.WriteAllBytes(Sidecar, [0xff, 0xfe, 0xfd]);
        Assert.False(LegendaryCollectionStore.Load(SavePath, "wanderer").CanWrite);
        Assert.Equal(new byte[] { 0xff, 0xfe, 0xfd }, File.ReadAllBytes(Sidecar));
    }

    [Fact]
    public void ActiveWriteLeaseRejectsConcurrentWriterWithoutLosingMemory()
    {
        LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Crown));
        string before = File.ReadAllText(Sidecar);
        using var lease = new FileStream(Sidecar + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Hour)));
        Assert.Equal(before, File.ReadAllText(Sidecar));
    }

    [Fact]
    public void SymbolicLinkSidecarsAndLeasesCannotRedirectWrites()
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "unrelated.json"); File.WriteAllText(target, "preserve me");
        File.CreateSymbolicLink(Sidecar, target);
        Assert.False(LegendaryCollectionStore.Load(SavePath, "wanderer").CanWrite);
        Assert.Throws<IOException>(() => LegendaryCollectionStore.Write(SavePath, Empty));
        Assert.Equal("preserve me", File.ReadAllText(target));
        File.Delete(Sidecar); File.Delete(Sidecar + ".lock");
        File.CreateSymbolicLink(Sidecar + ".lock", target);
        Assert.Throws<IOException>(() => LegendaryCollectionStore.Write(SavePath, Empty));
        Assert.Equal("preserve me", File.ReadAllText(target)); Assert.False(File.Exists(Sidecar));
    }

    [Fact]
    public void DanglingSidecarAndBackupLinksAreProtectedWithoutCreatingTheirTargets()
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, "missing-target.json");
        File.CreateSymbolicLink(Sidecar, target);
        Assert.False(LegendaryCollectionStore.Load(SavePath, "wanderer").CanWrite);
        Assert.Throws<IOException>(() => LegendaryCollectionStore.Write(SavePath, Empty));
        Assert.False(File.Exists(target)); Assert.NotNull(new FileInfo(Sidecar).LinkTarget);
        File.Delete(Sidecar);
        LegendaryCollectionStore.Write(SavePath, Discovered(LegendaryEquipment.Crown));
        string primary = File.ReadAllText(Sidecar);
        File.CreateSymbolicLink(Sidecar + ".bak", target);
        Assert.False(LegendaryCollectionStore.Load(SavePath, "wanderer").CanWrite);
        Assert.Throws<IOException>(() => LegendaryCollectionStore.Write(SavePath, Empty));
        Assert.False(File.Exists(target)); Assert.Equal(primary, File.ReadAllText(Sidecar));
        Assert.NotNull(new FileInfo(Sidecar + ".bak").LinkTarget);
    }
}
