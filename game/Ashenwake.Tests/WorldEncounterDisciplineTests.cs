using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class WorldEncounterDisciplineTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly AdventureContent Adventure = AdventureContent.Parse(Read("adventure.json"));
    private static readonly ProgressionContent Policy = ProgressionContent.Parse(Read("progression.json"));
    private static readonly CampaignContent Campaign = CampaignContent.Parse(Read("campaign.json"));
    private static readonly EndgameContent Endgame = EndgameContent.Parse(Read("endgame.json"));
    private static readonly string Combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson,
        Read("endgame-combat.json"), Endgame).CombatJson;
    private static readonly Dictionary<string, Lazy<Dictionary<string, EndgameRuntimeSnapshot>>> Sources = CombatSession.Disciplines
        .ToDictionary(d => d, d => new Lazy<Dictionary<string, EndgameRuntimeSnapshot>>(() => EarnSources(d)), StringComparer.Ordinal);

    private static Dictionary<string, EndgameRuntimeSnapshot> EarnSources(string discipline)
    {
        var sources = new Dictionary<string, EndgameRuntimeSnapshot>();
        var session = EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame, discipline: discipline);
        // Source builds come solely from the ordinary campaign reference policy:
        // no injected health, equipment, resources, unlocks, or enemy alterations.
        for (int i = 0; i < CampaignRuntimeSmoke.MaximumCommands && sources.Count < WorldEncounterCatalog.Definitions.Count; i++)
        {
            foreach (var d in WorldEncounterCatalog.Definitions)
                if (!session.InHub && session.Campaign.ActiveEncounterId == d.SourceEncounterId && session.Campaign.EncounterCleared && !sources.ContainsKey(d.Id))
                    sources.Add(d.Id, session.Capture());
            if (sources.Count == WorldEncounterCatalog.Definitions.Count) break;
            var step = session.ExecuteCampaign(CampaignRuntimeSmoke.Next(session.Campaign));
            Assert.True(step.Success, discipline + " campaign route: " + step.Reason);
        }
        Assert.Equal(WorldEncounterCatalog.Definitions.Count, sources.Count);
        return sources;
    }

    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void EveryDisciplineCanCompleteAllOptionalBattlesWithEarnedSourceBuilds(string discipline)
    {
        foreach (var d in WorldEncounterCatalog.Definitions)
        {
            var session = EndgameRuntimeSession.Restore(Combat, Adventure, Policy, Campaign, Endgame, Sources[discipline].Value[d.Id]);
            for (int tick = 0; tick < 7000 && session.WorldEncounters.Run?.Stage != "Claimed"; tick++)
            {
                var result = session.Execute(WorldEncounterSmoke.Next(session, d.Id, caravanCombat: true));
                Assert.True(result.Success, $"{discipline}/{d.Id}: {result.Reason}");
                Assert.True(session.WorldEncounters.Run?.Stage != "Failed", $"{discipline}/{d.Id} died using an earned source build.");
            }
            Assert.True(session.WorldEncounters.Run?.Stage == "Claimed", $"{discipline}/{d.Id} did not complete within 7000 commands.");
            Assert.Equal("Combat", session.WorldEncounters.Run!.Outcome);
            Assert.True(session.Combat.View.Actors.Single(a => a.Id == 1).Health > 0);
            Assert.Empty(session.Combat.View.Loot);
            Assert.False(session.Combat.View.ResonanceStormActive);
            Assert.False(session.Combat.View.ShrineDamagePenalty);
            Assert.Equal(session.StateHash, EndgameRuntimeSession.Restore(Combat, Adventure, Policy, Campaign, Endgame, session.Capture()).StateHash);
            Assert.True(session.ExitWorldEncounter().Success);
            Assert.Equal(d.SourceEncounterId, session.Campaign.ActiveEncounterId);
        }
    }
}
