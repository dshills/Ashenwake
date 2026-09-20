using Ashenwake.Client;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CharacterCatalogTests
{
    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "ashenwake-catalog-" + Guid.NewGuid().ToString("N"));
        public string SavePath => Path.Combine(DirectoryPath, "endgame.save.json");
        public ClientCharacterCatalog Catalog { get; }
        public Fixture()
        {
            string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
            var adventure = AdventureContent.Parse(Read("adventure.json"));
            var policy = ProgressionContent.Parse(Read("progression.json"));
            var campaign = CampaignContent.Parse(Read("campaign.json"));
            var endgame = EndgameContent.Parse(Read("endgame.json"));
            string combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), endgame).CombatJson;
            Catalog = new(combat, adventure, policy, campaign, endgame, ExperimentContent.Parse(Read("experiments.json")));
            var session = EndgameRuntimeSession.Create(combat, adventure, policy, campaign, endgame);
            EndgameRuntimeSaveStore.Write(SavePath, combat, adventure, policy, campaign, endgame, session.Capture());
        }
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }

    [Fact]
    public async Task RepeatedScansReuseValidatedPreviewsAndDetectReplacedOrDeletedArchives()
    {
        using var fixture = new Fixture();
        var first = await fixture.Catalog.ScanAsync(fixture.DirectoryPath);
        Assert.True(Assert.Single(first.Slots).Available); Assert.Equal(1, first.ValidatedArchives);
        var cached = await fixture.Catalog.ScanAsync(fixture.DirectoryPath);
        Assert.Equal(0, cached.ValidatedArchives); Assert.Equal(first.Slots, cached.Slots);
        File.WriteAllText(fixture.SavePath, "{broken");
        var changed = await fixture.Catalog.ScanAsync(fixture.DirectoryPath);
        Assert.False(Assert.Single(changed.Slots).Available); Assert.Equal(1, changed.ValidatedArchives);
        File.Delete(fixture.SavePath);
        Assert.Empty((await fixture.Catalog.ScanAsync(fixture.DirectoryPath)).Slots);
    }

    [Fact]
    public async Task BackupAndProfileChangesInvalidatePreviewWithoutWritingFiles()
    {
        using var fixture = new Fixture();
        string original = File.ReadAllText(fixture.SavePath);
        await fixture.Catalog.ScanAsync(fixture.DirectoryPath);
        File.WriteAllText(fixture.SavePath, "{broken");
        File.WriteAllText(fixture.SavePath + ".bak", original);
        var recovered = await fixture.Catalog.ScanAsync(fixture.DirectoryPath);
        Assert.True(Assert.Single(recovered.Slots).RecoveredBackup); Assert.Equal(1, recovered.ValidatedArchives);
        Assert.Equal("{broken", File.ReadAllText(fixture.SavePath)); Assert.Equal(original, File.ReadAllText(fixture.SavePath + ".bak"));
        string profile = EndgameRuntimeSaveStore.ProfilePath(fixture.SavePath);
        File.Delete(profile + ".bak"); File.WriteAllText(profile, "{broken");
        var unavailable = await fixture.Catalog.ScanAsync(fixture.DirectoryPath);
        Assert.False(Assert.Single(unavailable.Slots).Available); Assert.Equal(1, unavailable.ValidatedArchives);
        Assert.Equal("{broken", File.ReadAllText(profile));
    }

    [Fact]
    public async Task ExplicitInspectionNeverTrustsAnAdvisoryCachedCard()
    {
        using var fixture = new Fixture();
        Assert.True(Assert.Single((await fixture.Catalog.ScanAsync(fixture.DirectoryPath)).Slots).Available);
        // Simulate a writer preserving a file's size and modification time.
        string original = File.ReadAllText(fixture.SavePath); var time = File.GetLastWriteTimeUtc(fixture.SavePath);
        File.WriteAllText(fixture.SavePath, "[" + original[1..]); File.SetLastWriteTimeUtc(fixture.SavePath, time);
        Assert.False(fixture.Catalog.Inspect(fixture.DirectoryPath, "endgame.save.json").Available);
    }

    [Fact]
    public async Task ScanIncludesSelectedSlotOutsideTheBoundAndRetainsTheSlotLimit()
    {
        using var fixture = new Fixture();
        for (int i = 0; i < ClientCharacterCatalog.MaximumSlots + 2; i++)
            File.WriteAllText(Path.Combine(fixture.DirectoryPath, $"endgame.extra-{i}.save.json"), "{broken");
        var initial = fixture.Catalog.Scan(fixture.DirectoryPath);
        string selected = Directory.EnumerateFiles(fixture.DirectoryPath, "endgame*.save.json").Select(Path.GetFileName)
            .First(name => initial.All(slot => slot.Filename != name))!;
        var result = await fixture.Catalog.ScanAsync(fixture.DirectoryPath, selected);
        Assert.True(result.LimitReached); Assert.Equal(ClientCharacterCatalog.MaximumSlots, result.Slots.Length);
        Assert.Contains(result.Slots, slot => slot.Filename == selected);
    }

    [Fact]
    public async Task ChangingContinueTargetRetainsEveryUnchangedPreviewInTheBoundedWindow()
    {
        using var fixture = new Fixture();
        for (int i = 0; i < ClientCharacterCatalog.MaximumSlots + 2; i++)
            File.WriteAllText(Path.Combine(fixture.DirectoryPath, $"endgame.extra-{i}.save.json"), "{broken");
        var initial = fixture.Catalog.Scan(fixture.DirectoryPath);
        var outside = Directory.EnumerateFiles(fixture.DirectoryPath, "endgame*.save.json").Select(Path.GetFileName)
            .Where(name => initial.All(slot => slot.Filename != name)).Take(2).ToArray();
        Assert.Equal(2, outside.Length);
        await fixture.Catalog.ScanAsync(fixture.DirectoryPath, outside[0]!);
        var changed = await fixture.Catalog.ScanAsync(fixture.DirectoryPath, outside[1]!);
        Assert.Equal(1, changed.ValidatedArchives);
        Assert.Equal(ClientCharacterCatalog.MaximumSlots, changed.Slots.Length);
        var repeated = await fixture.Catalog.ScanAsync(fixture.DirectoryPath, outside[1]!);
        Assert.Equal(0, repeated.ValidatedArchives);
        Assert.Equal(changed.Slots, repeated.Slots);
    }

    [Fact]
    public async Task CancelledRefreshDoesNotPreventAFutureScan()
    {
        using var fixture = new Fixture(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Catalog.ScanAsync(fixture.DirectoryPath, cancellation: cancellation.Token));
        Assert.True(Assert.Single((await fixture.Catalog.ScanAsync(fixture.DirectoryPath)).Slots).Available);
    }
}
