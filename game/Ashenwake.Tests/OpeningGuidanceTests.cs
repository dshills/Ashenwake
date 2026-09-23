using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class OpeningGuidanceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ashenwake-guidance-" + Guid.NewGuid().ToString("N"));
    private string SavePath => Path.Combine(directory, "first.save.json");
    private string Sidecar => OpeningGuidanceStore.PathFor(SavePath);
    private static OpeningGuidanceMemory Empty => OpeningGuidance.Empty("wanderer");
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly AdventureContent Adventure = AdventureContent.Parse(Read("adventure.json"));
    private static readonly ProgressionContent Policy = ProgressionContent.Parse(Read("progression.json"));
    private static readonly CampaignContent Campaign = CampaignContent.Parse(Read("campaign.json"));
    private static readonly EndgameContent Endgame = EndgameContent.Parse(Read("endgame.json"));
    private static readonly string Combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson;
    private static EndgameRuntimeSession Create() => EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame);
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

    [Fact]
    public void MissingOptionalMemoryDoesNotCreateFiles()
    {
        var result = OpeningGuidanceStore.Load(SavePath, "wanderer");
        Assert.True(result.CanWrite); Assert.False(result.RecoveredBackup); Assert.True(result.Memory.Enabled);
        Assert.Empty(result.Memory.Dismissed); Assert.Empty(result.Memory.Completed); Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void UpdatesAreIdempotentBoundedAndDoNotAliasInputArrays()
    {
        var original = OpeningGuidance.Dismiss(Empty, "hint.move");
        var changed = OpeningGuidance.Complete(OpeningGuidance.SetEnabled(original, false), "hint.dodge");
        Assert.True(original.Enabled); Assert.Empty(original.Completed); Assert.False(changed.Enabled);
        Assert.Single(OpeningGuidance.Complete(changed, "hint.dodge").Completed);
        Assert.Same(changed, OpeningGuidance.Complete(changed, "hint.dodge"));
        Assert.Same(original, OpeningGuidance.Dismiss(original, "hint.move"));
        Assert.Same(original, OpeningGuidance.SetEnabled(original, true));
        changed.Dismissed[0] = "hint.loot"; Assert.Equal("hint.move", original.Dismissed[0]);
        Assert.Throws<ArgumentException>(() => OpeningGuidance.Dismiss(original, "secret.future"));
        Assert.Throws<InvalidDataException>(() => OpeningGuidance.Validate(original with { Dismissed = ["hint.move", "hint.move"] }));
        Assert.Throws<InvalidDataException>(() => OpeningGuidance.Validate(original with { Completed = ["unknown"] }));
        Assert.Throws<InvalidDataException>(() => OpeningGuidance.Validate(original with { Completed = null! }));
        Assert.Throws<InvalidDataException>(() => OpeningGuidance.Empty(new string('x', 81)));
    }

    [Fact]
    public void WritesMergeAcknowledgmentsPersistPreferenceAndNeverTouchArchive()
    {
        Directory.CreateDirectory(directory); File.WriteAllText(SavePath, "authoritative archive");
        OpeningGuidanceStore.Write(SavePath, OpeningGuidance.Dismiss(Empty, "hint.move"));
        var memory = OpeningGuidance.SetEnabled(OpeningGuidance.Complete(Empty, "hint.loot"), false);
        var merged = OpeningGuidanceStore.Write(SavePath, memory);
        Assert.Contains("hint.move", merged.Dismissed); Assert.Contains("hint.loot", merged.Completed); Assert.False(merged.Enabled);
        var loaded = OpeningGuidanceStore.Load(SavePath, "wanderer");
        Assert.Equal(JsonData.Hash(merged), JsonData.Hash(loaded.Memory)); Assert.True(File.Exists(Sidecar + ".bak"));
        Assert.Equal("authoritative archive", File.ReadAllText(SavePath));
        Assert.False(OpeningGuidanceStore.Load(SavePath, "other").CanWrite);
        Assert.Throws<IOException>(() => OpeningGuidanceStore.Write(SavePath, OpeningGuidance.Empty("other")));
    }

    [Fact]
    public void CorruptPrimaryRecoversWithoutReplacingGoodBackupWithBadBytes()
    {
        OpeningGuidanceStore.Write(SavePath, OpeningGuidance.Dismiss(Empty, "hint.move"));
        OpeningGuidanceStore.Write(SavePath, OpeningGuidance.Complete(Empty, "hint.loot"));
        string backup = File.ReadAllText(Sidecar + ".bak"); File.WriteAllText(Sidecar, "{broken");
        var loaded = OpeningGuidanceStore.Load(SavePath, "wanderer");
        Assert.True(loaded.RecoveredBackup); Assert.True(loaded.CanWrite); Assert.Equal("{broken", File.ReadAllText(Sidecar));
        OpeningGuidanceStore.Write(SavePath, OpeningGuidance.SetEnabled(loaded.Memory, false));
        Assert.Equal(backup, File.ReadAllText(Sidecar + ".bak")); Assert.False(OpeningGuidanceStore.Load(SavePath, "wanderer").Memory.Enabled);
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    public void BadOptionalMemoryIsPreservedAndDoesNotBlockCharacterUse(string bytes)
    {
        Directory.CreateDirectory(directory); File.WriteAllText(Sidecar, bytes);
        var loaded = OpeningGuidanceStore.Load(SavePath, "wanderer");
        Assert.False(loaded.CanWrite); Assert.NotEmpty(loaded.Notice); Assert.Empty(loaded.Memory.Dismissed);
        Assert.Throws<IOException>(() => OpeningGuidanceStore.Write(SavePath, Empty));
        Assert.Equal(bytes, File.ReadAllText(Sidecar));
    }

    [Fact]
    public void FuturePrimaryAndForeignBackupNeverFallBackOrGetOverwritten()
    {
        OpeningGuidanceStore.Write(SavePath, Empty);
        string valid = File.ReadAllText(Sidecar); File.WriteAllText(Sidecar + ".bak", valid);
        File.WriteAllText(Sidecar, "{\"schemaVersion\":9}");
        Assert.False(OpeningGuidanceStore.Load(SavePath, "wanderer").CanWrite);
        File.WriteAllText(Sidecar, valid);
        var foreign = JsonNode.Parse(valid)!; foreign["saveIdentity"] = "other";
        File.WriteAllText(Sidecar + ".bak", foreign.ToJsonString());
        Assert.False(OpeningGuidanceStore.Load(SavePath, "wanderer").CanWrite);
        Assert.Throws<IOException>(() => OpeningGuidanceStore.Write(SavePath, Empty));
        Assert.Equal(valid, File.ReadAllText(Sidecar));
    }

    [Fact]
    public void OversizedAndSymlinkSidecarsAreNotFollowedOrOverwritten()
    {
        Directory.CreateDirectory(directory); File.WriteAllText(Sidecar, new string('x', 16 * 1024 + 1));
        Assert.False(OpeningGuidanceStore.Load(SavePath, "wanderer").CanWrite); File.Delete(Sidecar);
        string target = Path.Combine(directory, "other.json"); File.WriteAllText(target, "untouched");
        File.CreateSymbolicLink(Sidecar, target);
        Assert.False(OpeningGuidanceStore.Load(SavePath, "wanderer").CanWrite);
        Assert.Throws<IOException>(() => OpeningGuidanceStore.Write(SavePath, Empty)); Assert.Equal("untouched", File.ReadAllText(target));
    }

    [Fact]
    public void HashTamperingAndMissingRequiredPreferenceCannotResetDismissalsSilently()
    {
        OpeningGuidanceStore.Write(SavePath, OpeningGuidance.Dismiss(Empty, "hint.move"));
        var node = JsonNode.Parse(File.ReadAllText(Sidecar))!; node["memory"]!["enabled"] = false;
        File.WriteAllText(Sidecar, node.ToJsonString()); Assert.False(OpeningGuidanceStore.Load(SavePath, "wanderer").CanWrite);
        node["memory"]!.AsObject().Remove("enabled"); File.WriteAllText(Sidecar, node.ToJsonString());
        Assert.False(OpeningGuidanceStore.Load(SavePath, "wanderer").CanWrite);
    }

    [Fact]
    public void FreshProjectionIsReadOnlyOptionalAndDoesNotRevealSecretsOrUnearnedServices()
    {
        var s = Create(); string hash = s.StateHash, replay = JsonData.Hash(s.CaptureReplay());
        var view = OpeningGuidance.Project(s, Empty);
        Assert.Equal("hint.move", view.Hint!.Id); Assert.Equal(5, view.Basics.Length); Assert.Equal(3, view.Steps.Length);
        Assert.Contains(view.Services, c => c.Id == "service.training");
        Assert.DoesNotContain(view.Services, c => c.Id is "service.stash" or "service.hunts" or "unlock.Tempering");
        Assert.All(view.Steps, c => Assert.False(c.Completed));
        string text = JsonData.Write(view);
        Assert.DoesNotContain("champion.", text); Assert.DoesNotContain("secret.", text); Assert.DoesNotContain("Heart of Serath", text);
        var disabled = OpeningGuidance.Project(s, OpeningGuidance.SetEnabled(Empty, false));
        Assert.Null(disabled.Hint); Assert.Equal(5, disabled.Basics.Length); Assert.Equal(3, disabled.Steps.Length);
        var dismissed = OpeningGuidance.Project(s, OpeningGuidance.Dismiss(Empty, "hint.move"));
        Assert.NotEqual("hint.move", dismissed.Hint?.Id); Assert.Contains(dismissed.Basics, c => c.Id == "hint.move");
        Assert.Equal(hash, s.StateHash); Assert.Equal(replay, JsonData.Hash(s.CaptureReplay()));
        Assert.Throws<InvalidDataException>(() => OpeningGuidance.Project(s, OpeningGuidance.Empty("foreign")));
    }

    [Fact]
    public void EarnedActOneProgressUnlocksBuildAndServicesFromLiveState()
    {
        var s = Create();
        var seenHints = new HashSet<string>();
        bool observedSupportWarning = false;
        for (int i = 0; i < 10000 && !s.Campaign.Capture().Campaign.CompletedActs.Contains(1); i++)
        {
            var context = OpeningGuidance.Project(s, Empty);
            if (context.Hint is { } hint) seenHints.Add(hint.Id);
            if (s.Combat.View.CampaignHazards?.Any(h => h.RemainingTicks > 0 && CombatSession.IsSupportHazard(h.ContentId)) == true)
            { observedSupportWarning = true; Assert.Equal("hint.interrupt", context.Hint?.Id); }
            var result = s.Execute(new(EndgameRuntimeAction.Campaign, Campaign: CampaignRuntimeSmoke.Next(s.Campaign)));
            Assert.True(result.Success, result.Reason);
        }
        Assert.Contains(1, s.Campaign.Capture().Campaign.CompletedActs);
        Assert.Contains("hint.interrupt", seenHints); Assert.Contains("hint.dodge", seenHints); Assert.Contains("hint.loot", seenHints);
        Assert.True(observedSupportWarning, "Act I should expose a real announced support channel.");
        Assert.True(s.Execute(new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.ReturnToHub))).Success);
        var memory = OpeningGuidance.SetEnabled(Empty, false);
        var view = OpeningGuidance.Project(s, memory);
        Assert.Contains(view.Services, c => c.Id == "service.stash"); Assert.Contains(view.Services, c => c.Id == "service.hunts");
        Assert.Contains(view.Services, c => c.Id == "unlock.Tempering");
        Assert.Equal("inspect_gear", view.Steps.Single(c => c.Id == "build.pyre").Action);
        Assert.Equal("inspect_anatomy", view.Steps.Single(c => c.Id == "build.heart").Action);
        long boots = s.Production.Capture().Progression.Character.Items.First(i => i.DefinitionId == LegendaryEquipment.Pyre).Id;
        ApplyAt("service.torren", new(ProductionAction.Equip, ItemId: boots, Slot: EquipmentSlot.Boots));
        ApplyAt("service.mara", new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, "Heart", "fragment.heart_serath")));
        string before = s.StateHash;
        var equipped = OpeningGuidance.Project(s, OpeningGuidance.Complete(memory, "build.practice"));
        Assert.All(equipped.Steps, c => Assert.True(c.Completed)); Assert.Equal(before, s.StateHash);
        Assert.DoesNotContain("hint.", OpeningGuidance.Project(s, Empty).Hint?.Id ?? "");
        ApplyAt("service.torren", new(ProductionAction.Unequip, Slot: EquipmentSlot.Boots));
        Assert.False(OpeningGuidance.Project(s, memory).Steps.Single(c => c.Id == "build.pyre").Completed);

        void ApplyAt(string target, ProductionCommand command)
        {
            for (int i = 0; i < 200; i++)
            {
                var next = CampaignRuntimeSmoke.AtInteraction(s.Campaign, target, new(CampaignRuntimeAction.Production, Production: command));
                var result = s.Execute(new(EndgameRuntimeAction.Campaign, Campaign: next)); Assert.True(result.Success, result.Reason);
                if (next.Action == CampaignRuntimeAction.Production) return;
            }
            throw new InvalidOperationException("Could not approach " + target);
        }
    }
}
