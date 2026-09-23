using Ashenwake.Core.Campaign;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class RoamingChampionCollectionTests
{
    [Theory]
    [InlineData("champion.pilgrim")]
    [InlineData("champion.rootwidow")]
    [InlineData("champion.tithekeeper")]
    public void ChampionSourcesStayHiddenUntilSightingAndExplainOutstandingTreasure(string id)
    {
        var champion = RoamingChampionCatalog.Find(id)!;
        var campaign = CampaignContent.Default(); var story = CampaignSession.Create(campaign);
        var endgame = EndgameContent.Default();
        var locked = new EndgameRuntimeView(false, false, 0, [], [], 0, new Dictionary<string, int>(), false, null, 0, 0);
        LegendaryCollectionSourceView Source(RoamingChampionsView? view = null) => Assert.Single(LegendaryCollectionSources.Project(
            campaign.Capture(), story.Capture(), story.View, "hub", endgame.Capture(), locked, champion.RewardItemId, champions: view));
        var hidden = Source(); Assert.Equal(LegendaryCollectionSourceState.Locked, hidden.State);
        Assert.Empty(hidden.ChampionId); Assert.Empty(hidden.EncounterId); Assert.DoesNotContain(champion.Name, hidden.Label);
        var seen = new RoamingChampionEntryView(id, champion.Name, champion.Act, champion.SourceEncounterIds[0], false, true, true, false, false,
            champion.Description, champion.Counterplay, champion.RewardItemId);
        var waiting = Source(new([seen], null));
        Assert.Equal(id, waiting.ChampionId); Assert.Equal(LegendaryCollectionSourceState.Available, waiting.State);
        Assert.Contains("waiting treasure", waiting.Requirement); Assert.False(waiting.Repeatable);
        var claimed = Source(new([seen with { Claimed = true }], null));
        Assert.Equal(LegendaryCollectionSourceState.Completed, claimed.State); Assert.Contains("does not grant another copy", claimed.Requirement);
    }

    [Fact]
    public void LearningExistingPowerDoesNotRevealUndiscoveredChampionEquipment()
    {
        var policy = ProgressionContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "progression.json")));
        var character = ProgressionSession.Create(policy).Capture().Character;
        character.PropertyLibrary.UnionWith(RoamingChampionCatalog.Definitions.Select(d => LegendaryCollectionCatalog.Entries.Single(e => e.ItemId == d.RewardItemId).PowerId));
        var observed = LegendaryCollection.Observe(LegendaryCollection.Empty(character.CharacterId), character);
        Assert.DoesNotContain(observed.DiscoveredItems, id => RoamingChampionCatalog.Definitions.Any(d => d.RewardItemId == id));
    }
}
