using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class AppearanceWardrobeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ashenwake-wardrobe-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(directory, "wanderer.save.json");
    private string Sidecar => AppearanceWardrobeStore.PathFor(SavePath);
    private static string Read(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));
    private static readonly ProgressionDefinition Definition = ProductionContent.Resolve(Read("combat.json"), ProgressionContent.Parse(Read("progression.json"))).Capture();
    private static AppearanceWardrobeMemory Empty => AppearanceWardrobe.Empty("wanderer");
    private static AppearanceWardrobeMemory Observed(params (string Id, ItemRarity Rarity)[] items) => AppearanceWardrobe.Observe(Empty,
        new ProgressionState { Items = items.Select((i, index) => new PermanentItem { Id = index + 1, DefinitionId = i.Id, Rarity = i.Rarity }).ToArray() }, Definition);
    private static AppearanceWardrobeMemory Starter => Observed(("item.starter_head", ItemRarity.Common), ("item.starter_chest", ItemRarity.Rare));
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public void ObserveIncludesStoredArmorKeepsHighestRarityAndNeverInfersWeaponsOrEngravings()
    {
        var state = new ProgressionState
        {
            Items = [new() { Id = 1, DefinitionId = "item.starter_head", Rarity = ItemRarity.Rare }, new() { Id = 2, DefinitionId = "item.starter_head", Rarity = ItemRarity.Common },
                new() { Id = 3, DefinitionId = "item.starter_chest", Rarity = ItemRarity.Legendary }, new() { Id = 4, DefinitionId = "item.ashcleaver", Rarity = ItemRarity.Godwrought, Engraving = "property.unspoken_verdict" }],
            Stash = new() { Locations = [new(3, "stash.1")] },
            PropertyLibrary = ["property.unspoken_verdict"]
        };
        string source = JsonData.Hash(state); var memory = AppearanceWardrobe.Observe(Empty, state, Definition);
        Assert.Equal(source, JsonData.Hash(state)); Assert.Equal(2, memory.Unlocks.Length);
        Assert.Equal(ItemRarity.Rare, memory.Unlocks.Single(u => u.ItemId == "item.starter_head").Rarity);
        Assert.Equal(ItemRarity.Legendary, memory.Unlocks.Single(u => u.ItemId == "item.starter_chest").Rarity);
        state.Items = []; state.Stash = null;
        Assert.Equal(JsonData.Hash(memory), JsonData.Hash(AppearanceWardrobe.Observe(memory, state, Definition)));
        Assert.Empty(AppearanceWardrobe.Observe(Empty, state, Definition).Unlocks);
        Assert.Throws<InvalidDataException>(() => AppearanceWardrobe.Observe(memory, state with { CharacterId = "another" }, Definition));
    }
    [Fact]
    public void EveryChoiceAndLookOperationIsPureAndCannotAffectGameplayOrReplay()
    {
        var session = ProductionSession.Create(Read("combat.json"), AdventureContent.Parse(Read("adventure.json")), ProgressionContent.Parse(Read("progression.json")));
        string hash = session.StateHash, replay = JsonData.Hash(session.CaptureReplay());
        var memory = AppearanceWardrobe.Observe(Empty, session.Capture().Progression.Character, session.Content.Capture());
        string before = JsonData.Hash(memory);
        var selected = AppearanceWardrobe.Select(memory, EquipmentSlot.Head, "item.starter_head", Definition);
        selected = AppearanceWardrobe.SetHelmetHidden(selected, true);
        var saved = AppearanceWardrobe.SaveLook(selected, "  Ash Pilgrim  ", Definition);
        Assert.Equal(before, JsonData.Hash(memory)); Assert.Empty(memory.Looks); Assert.False(memory.HideHelmet);
        var reset = AppearanceWardrobe.Reset(saved);
        Assert.Empty(reset.Overrides); Assert.False(reset.HideHelmet); Assert.Single(reset.Looks); Assert.Equal(saved.Unlocks, reset.Unlocks);
        var applied = AppearanceWardrobe.ApplyLook(reset, "ash pilgrim", Definition); Assert.True(applied.HideHelmet); Assert.Equal("item.starter_head", applied.Overrides[EquipmentSlot.Head]);
        var deleted = AppearanceWardrobe.DeleteLook(applied, "Ash Pilgrim"); Assert.Empty(deleted.Looks); Assert.Single(applied.Looks);
        applied.Overrides.Clear(); Assert.Single(saved.Overrides); Assert.Single(reset.Looks[0].Overrides);
        Assert.Equal(hash, session.StateHash); Assert.Equal(replay, JsonData.Hash(session.CaptureReplay())); Assert.Equal(0, saved.Revision);
    }
    [Theory]
    [InlineData(EquipmentSlot.MainHand, "item.starter_head")]
    [InlineData(EquipmentSlot.Ring1, "item.starter_head")]
    [InlineData(EquipmentSlot.Chest, "item.starter_head")]
    [InlineData(EquipmentSlot.Head, "item.crown_unsworn")]
    [InlineData(EquipmentSlot.Head, "item.missing")]
    public void InvalidLockedAndIncompatibleSelectionsAreRejected(EquipmentSlot slot, string id)
    {
        string hash = JsonData.Hash(Starter);
        Assert.ThrowsAny<Exception>(() => AppearanceWardrobe.Select(Starter, slot, id, Definition));
        Assert.Equal(hash, JsonData.Hash(Starter));
    }
    [Fact]
    public void EightLooksSupportCaseInsensitiveReplacementDeletionAndIndependentHelmetChoice()
    {
        var memory = Starter;
        for (int i = 0; i < 8; i++) memory = AppearanceWardrobe.SaveLook(memory, "Look " + i, Definition);
        Assert.Throws<InvalidOperationException>(() => AppearanceWardrobe.SaveLook(memory, "Ninth", Definition));
        memory = AppearanceWardrobe.SetHelmetHidden(memory, true);
        memory = AppearanceWardrobe.SaveLook(memory, "LOOK 0", Definition);
        Assert.Equal(8, memory.Looks.Length); Assert.True(memory.Looks[0].HideHelmet);
        Assert.False(AppearanceWardrobe.ApplyLook(memory, "Look 1", Definition).HideHelmet);
        memory = AppearanceWardrobe.DeleteLook(memory, "look 2"); Assert.Equal(7, memory.Looks.Length);
        Assert.Throws<ArgumentException>(() => AppearanceWardrobe.ApplyLook(memory, "look 2", Definition));
        Assert.Throws<ArgumentException>(() => AppearanceWardrobe.SaveLook(memory, new string('a', 33), Definition));
        Assert.Throws<ArgumentException>(() => AppearanceWardrobe.SaveLook(memory, "\n\t", Definition));
        Assert.Throws<ArgumentException>(() => AppearanceWardrobe.SaveLook(memory, "bad\nname", Definition));
        Assert.Throws<InvalidDataException>(() => AppearanceWardrobe.Validate(memory with { Looks = [new("Locked", new() { [EquipmentSlot.Head] = "item.crown_unsworn" }, false)] }, Definition));
    }
    [Fact]
    public void MissingMemoryReadCreatesNothingAndRoundTripDoesNotTouchCharacterArchive()
    {
        var missing = AppearanceWardrobeStore.Load(SavePath, "wanderer", Definition);
        Assert.True(missing.CanWrite); Assert.Empty(missing.Memory.Unlocks); Assert.False(Directory.Exists(directory));
        Directory.CreateDirectory(directory); File.WriteAllText(SavePath, "unchanged gameplay archive");
        var memory = AppearanceWardrobe.SaveLook(AppearanceWardrobe.Select(Starter, EquipmentSlot.Head, "item.starter_head", Definition), "Watcher", Definition);
        var stored = AppearanceWardrobeStore.Write(SavePath, memory, Definition); Assert.Equal(1, stored.Revision);
        var loaded = AppearanceWardrobeStore.Load(SavePath, "wanderer", Definition);
        Assert.True(loaded.CanWrite); Assert.False(loaded.RecoveredBackup); Assert.Equal(JsonData.Hash(stored), JsonData.Hash(loaded.Memory));
        Assert.Equal("unchanged gameplay archive", File.ReadAllText(SavePath)); Assert.False(File.Exists(Sidecar + ".bak"));
        string bytes = File.ReadAllText(Sidecar);
        Assert.Equal(1, AppearanceWardrobeStore.Write(SavePath, stored, Definition).Revision); Assert.Equal(bytes, File.ReadAllText(Sidecar));
    }
    [Fact]
    public void ConcurrentChoiceConflictPreservesCurrentWhileObservationOnlyWritesUnionUnlocks()
    {
        var original = AppearanceWardrobeStore.Write(SavePath, Starter, Definition);
        var newest = AppearanceWardrobeStore.Write(SavePath, AppearanceWardrobe.SetHelmetHidden(original, true), Definition);
        string bytes = File.ReadAllText(Sidecar);
        var staleChoice = AppearanceWardrobe.Select(original, EquipmentSlot.Chest, "item.starter_chest", Definition);
        Assert.Throws<IOException>(() => AppearanceWardrobeStore.Write(SavePath, staleChoice, Definition)); Assert.Equal(bytes, File.ReadAllText(Sidecar));
        // A stale observation whose choices still agree can safely add knowledge without rolling back an outfit.
        var observed = AppearanceWardrobe.Observe(newest, new ProgressionState { Items = [new() { Id = 2, DefinitionId = "item.starter_boots", Rarity = ItemRarity.Relic }] }, Definition);
        newest = AppearanceWardrobeStore.Write(SavePath, observed, Definition);
        var concurrent = AppearanceWardrobe.Observe(observed, new ProgressionState { Items = [new() { Id = 3, DefinitionId = "item.starter_head", Rarity = ItemRarity.Legendary }] }, Definition);
        var merged = AppearanceWardrobeStore.Write(SavePath, concurrent, Definition);
        Assert.True(merged.HideHelmet); Assert.Equal(3, merged.Unlocks.Length); Assert.Equal(ItemRarity.Legendary, merged.Unlocks.Single(u => u.ItemId == "item.starter_head").Rarity);
        Assert.True(merged.Revision > newest.Revision);
        Assert.Throws<IOException>(() => AppearanceWardrobeStore.Write(SavePath, merged with { Revision = merged.Revision + 1 }, Definition));
    }
    [Fact]
    public void CorruptPrimaryRecoversReadOnlyAndLaterWriteKeepsGoodBackup()
    {
        var first = AppearanceWardrobeStore.Write(SavePath, Starter, Definition);
        AppearanceWardrobeStore.Write(SavePath, AppearanceWardrobe.SetHelmetHidden(first, true), Definition);
        string backup = File.ReadAllText(Sidecar + ".bak"); File.WriteAllText(Sidecar, "{broken");
        var recovered = AppearanceWardrobeStore.Load(SavePath, "wanderer", Definition);
        Assert.True(recovered.RecoveredBackup); Assert.True(recovered.CanWrite); Assert.Equal(first.Revision, recovered.Memory.Revision);
        Assert.Equal("{broken", File.ReadAllText(Sidecar)); Assert.Equal(backup, File.ReadAllText(Sidecar + ".bak"));
        AppearanceWardrobeStore.Write(SavePath, AppearanceWardrobe.SetHelmetHidden(recovered.Memory, true), Definition);
        Assert.Equal(backup, File.ReadAllText(Sidecar + ".bak"));
    }
    [Theory]
    [InlineData("schema")]
    [InlineData("foreign")]
    [InlineData("unknown")]
    [InlineData("duplicates")]
    public void ProtectedPrimaryCannotFallBackToValidBackupOrBeOverwritten(string kind)
    {
        var first = AppearanceWardrobeStore.Write(SavePath, Starter, Definition);
        AppearanceWardrobeStore.Write(SavePath, AppearanceWardrobe.SetHelmetHidden(first, true), Definition);
        var node = JsonNode.Parse(File.ReadAllText(Sidecar))!;
        if (kind == "schema") node["schemaVersion"] = 2;
        if (kind == "foreign") node["memory"]!["characterId"] = "other";
        if (kind == "unknown") node["memory"]!["unlocks"]![0]!["itemId"] = "item.future_armor";
        string protectedBytes = kind == "duplicates" ? "{\"schemaVersion\":1,\"schemaVersion\":2}" : node.ToJsonString();
        File.WriteAllText(Sidecar, protectedBytes); string backup = File.ReadAllText(Sidecar + ".bak");
        var loaded = AppearanceWardrobeStore.Load(SavePath, "wanderer", Definition);
        Assert.False(loaded.CanWrite); Assert.False(loaded.RecoveredBackup); Assert.Empty(loaded.Memory.Unlocks);
        Assert.Throws<IOException>(() => AppearanceWardrobeStore.Write(SavePath, Starter, Definition));
        Assert.Equal(protectedBytes, File.ReadAllText(Sidecar)); Assert.Equal(backup, File.ReadAllText(Sidecar + ".bak"));
    }
    [Fact]
    public void SaveFilenameAndCharacterBindSidecarWhileFolderMoveRemainsSupported()
    {
        AppearanceWardrobeStore.Write(SavePath, Starter, Definition);
        Assert.False(AppearanceWardrobeStore.Load(SavePath, "another", Definition).CanWrite);
        string other = Path.Combine(directory, "other.save.json"); File.Copy(Sidecar, AppearanceWardrobeStore.PathFor(other));
        Assert.False(AppearanceWardrobeStore.Load(other, "wanderer", Definition).CanWrite);
        string moved = directory + "-moved"; Directory.Move(directory, moved);
        try { Assert.True(AppearanceWardrobeStore.Load(Path.Combine(moved, Path.GetFileName(SavePath)), "wanderer", Definition).CanWrite); }
        finally { Directory.Move(moved, directory); }
    }
    [Fact]
    public void OversizedInvalidUtf8AndInvalidChecksumFilesRemainPreserved()
    {
        Directory.CreateDirectory(directory);
        foreach (byte[] bytes in new[] { new byte[256 * 1024 + 1], new byte[] { 0xff, 0xfe, 0xfd } })
        {
            File.WriteAllBytes(Sidecar, bytes); Assert.False(AppearanceWardrobeStore.Load(SavePath, "wanderer", Definition).CanWrite);
            Assert.Throws<IOException>(() => AppearanceWardrobeStore.Write(SavePath, Starter, Definition)); Assert.Equal(bytes, File.ReadAllBytes(Sidecar));
        }
        File.Delete(Sidecar); AppearanceWardrobeStore.Write(SavePath, Starter, Definition);
        var node = JsonNode.Parse(File.ReadAllText(Sidecar))!; node["memory"]!["hideHelmet"] = true;
        string corrupt = node.ToJsonString(); File.WriteAllText(Sidecar, corrupt);
        Assert.False(AppearanceWardrobeStore.Load(SavePath, "wanderer", Definition).CanWrite); Assert.Equal(corrupt, File.ReadAllText(Sidecar));
    }
    [Theory]
    [InlineData("")]
    [InlineData(".bak")]
    [InlineData(".lock")]
    public void SymbolicLinkAliasesAndDanglingLinksCannotRedirectWrites(string suffix)
    {
        Directory.CreateDirectory(directory); string target = Path.Combine(directory, "target.json");
        File.WriteAllText(target, "preserve target"); File.CreateSymbolicLink(Sidecar + suffix, target);
        Assert.Throws<IOException>(() => AppearanceWardrobeStore.Write(SavePath, Starter, Definition)); Assert.Equal("preserve target", File.ReadAllText(target));
        File.Delete(Sidecar + suffix); File.Delete(target); File.CreateSymbolicLink(Sidecar + suffix, target);
        Assert.Throws<IOException>(() => AppearanceWardrobeStore.Write(SavePath, Starter, Definition)); Assert.False(File.Exists(target));
    }
    [Fact]
    public void ActiveWriteLeasePreventsConcurrentFileReplacement()
    {
        var memory = AppearanceWardrobeStore.Write(SavePath, Starter, Definition); string bytes = File.ReadAllText(Sidecar);
        using var lease = new FileStream(Sidecar + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => AppearanceWardrobeStore.Write(SavePath, AppearanceWardrobe.SetHelmetHidden(memory, true), Definition));
        Assert.Equal(bytes, File.ReadAllText(Sidecar));
    }
}
