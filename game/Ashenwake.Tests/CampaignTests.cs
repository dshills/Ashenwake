using Ashenwake.Core.Campaign;
using Ashenwake.Core.Content;
using Xunit;

namespace Ashenwake.Tests;

public sealed class CampaignTests
{
    private static CampaignSession Session() => CampaignSession.Create(CampaignContent.Default());
    private static void CompleteAct(CampaignSession session, int number, string? outcomeId = null)
    {
        var act = CampaignContent.Default().Capture().Acts[number - 1];
        var choice = CampaignContent.Default().Capture().Choices.Single(c => c.Id == act.RequiredChoice);
        Assert.True(session.EnterAct(number).Success);
        foreach (var encounter in act.Encounters.Take(act.Encounters.Length - 1)) Assert.True(session.CompleteEncounter(encounter.Id).Success);
        Assert.True(session.Choose(choice.Id, outcomeId ?? choice.Outcomes[0].Id).Success);
        Assert.True(session.CompleteEncounter(act.Encounters[^1].Id).Success);
    }
    [Fact]
    public void EveryAuthoredChoicePermutationReachesEndingAndCommonEndgame()
    {
        var content = CampaignContent.Default(); var choices = content.Capture().Choices;
        int variants = choices.Aggregate(1, (count, choice) => count * choice.Outcomes.Length);
        for (int variant = 0; variant < variants; variant++)
        {
            var session = CampaignSession.Create(content); int index = variant;
            for (int act = 1; act <= 5; act++)
            {
                var choice = choices[act - 1]; string outcome = choice.Outcomes[index % choice.Outcomes.Length].Id; index /= choice.Outcomes.Length;
                CompleteAct(session, act, outcome);
                session = CampaignSession.Restore(content, JsonData.Copy(session.Capture()));
            }
            var ending = Assert.IsType<CampaignEnding>(session.Ending(variant % 2 == 0 ? 20 : 60));
            Assert.True(ending.FracturesUnlocked); Assert.Equal(5, session.Capture().CompletedActs.Count);
            Assert.Contains("Nhal is still beyond the breach", ending.Summary);
            Assert.Equal(5150, session.Capture().EarnedExperience);
        }
    }
    [Fact]
    public void ChoicesHaveImmediateDelayedAndReactiveHubConsequences()
    {
        var session = Session(); CompleteAct(session, 1, "mara");
        Assert.Contains("fragment.anatomists", session.View.WorldFlags); Assert.Contains("anatomists.seal_evidence", session.View.PendingConsequences);
        CompleteAct(session, 2, "permit"); Assert.DoesNotContain("ilyra.envoys_arrive", session.View.WorldFlags);
        CompleteAct(session, 3, "stop"); Assert.Contains("anatomists.seal_evidence", session.View.WorldFlags);
        session.EnterAct(4); Assert.Contains("ilyra.envoys_arrive", session.View.WorldFlags);
        CompleteAct(session, 4, "break"); session.EnterAct(5);
        Assert.Contains("city.hard_winter", session.View.WorldFlags); Assert.Contains("greyhaven.refugees_arrive", session.View.WorldFlags);
        Assert.Contains(session.View.HubReactions, reaction => reaction.Contains("freed debtors", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(session.ViewForResonance(60).HubReactions, reaction => reaction.Contains("changed body", StringComparison.Ordinal));
    }
    [Fact]
    public void RegionStormExpiresAndAllScopedRulesCleanUpOnTravelDeathOrCompletion()
    {
        var session = Session(); CompleteAct(session, 1); CompleteAct(session, 2); session.EnterAct(3);
        Assert.True(session.BeginExploration("event.resonance_storm").Success); Assert.NotEmpty(session.View.ExplorationRules);
        session.AdvanceTicks(899); Assert.NotNull(session.Capture().Exploration);
        session.AdvanceTicks(1); Assert.Null(session.Capture().Exploration); Assert.Empty(session.View.ExplorationRules);
        session.BeginExploration("event.resonance_storm"); session.PlayerDied(); Assert.Empty(session.View.ExplorationRules);
        session.BeginExploration("event.resonance_storm"); session.ReturnToHub(); Assert.Null(session.Capture().Exploration);
        CompleteAct(session, 3); session.EnterAct(4);
        Assert.True(session.BeginExploration("event.divine_memory").Success);
        Assert.True(session.CompleteExploration("exploration.first_oath").Success);
        Assert.Equal(4, session.View.Act); Assert.Empty(session.View.ExplorationRules);
        Assert.Contains("discovery.divine_seals", session.Capture().Discoveries);
    }
    [Fact]
    public void HuntRequiresOrderedCluesAndRewardsCannotDuplicate()
    {
        var session = Session(); CompleteAct(session, 1); session.EnterAct(2);
        session.BeginExploration("event.wake_hunt");
        Assert.False(session.CompleteExploration("exploration.antler_hunt").Success);
        Assert.False(session.TrackClue("clue.heartwood_nest").Success);
        foreach (string clue in new[] { "clue.shed_bark", "clue.reversed_tracks", "clue.heartwood_nest" }) Assert.True(session.TrackClue(clue).Success);
        var result = session.CompleteExploration("exploration.antler_hunt"); Assert.True(result.Success); Assert.Equal(20, result.Materials);
        string hash = session.StateHash; Assert.False(session.CompleteExploration("exploration.antler_hunt").Success);
        Assert.False(session.BeginExploration("event.wake_hunt").Success); Assert.Equal(hash, session.StateHash);
    }
    [Fact]
    public void CampaignBlocksSkippingChoicesEncountersAndIncompatibleRestore()
    {
        var content = CampaignContent.Default(); var session = Session();
        string hash = session.StateHash; Assert.False(session.EnterAct(5).Success); Assert.Equal(hash, session.StateHash);
        session.EnterAct(1); Assert.False(session.CompleteEncounter("campaign.bell_saint").Success);
        session.CompleteEncounter("campaign.road"); session.CompleteEncounter("campaign.monastery");
        Assert.False(session.CompleteEncounter("campaign.bell_saint").Success);
        Assert.True(session.Choose("choice.fragment", "community").Success);
        hash = session.StateHash; Assert.True(session.Choose("choice.fragment", "community").Success); Assert.Equal(hash, session.StateHash);
        Assert.False(session.Choose("choice.fragment", "mara").Success); Assert.Equal(hash, session.StateHash);
        var state = session.Capture(); state.WorldFlags.Add("fake.flag"); Assert.Throws<InvalidDataException>(() => CampaignSession.Restore(content, state));
        state = session.Capture(); state.EarnedMaterials += 1; Assert.Throws<InvalidDataException>(() => CampaignSession.Restore(content, state));
        state = session.Capture() with { ContentHash = "old-content" }; Assert.Throws<InvalidDataException>(() => CampaignSession.Restore(content, state));
        var definition = content.Capture(); definition.Choices[0] = definition.Choices[0] with { RequiredEncounter = "campaign.bell_saint" };
        Assert.Throws<InvalidDataException>(() => CampaignContent.Create(definition));
    }
}
