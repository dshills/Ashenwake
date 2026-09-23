using System.Text.Json;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Xunit;
using Xunit.Abstractions;

namespace Ashenwake.Tests;

/// <summary>Earned Act I routes. These measure deterministic input policies, not human completion times.</summary>
public sealed class OpeningGuidanceBalanceTests(ITestOutputHelper output)
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly AdventureContent Adventure = AdventureContent.Parse(Read("adventure.json"));
    private static readonly ProgressionContent Policy = ProgressionContent.Parse(Read("progression.json"));
    private static readonly CampaignContent Campaign = CampaignContent.Parse(Read("campaign.json"));
    private static readonly EndgameContent Endgame = EndgameContent.Parse(Read("endgame.json"));
    private static readonly string Combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson;

    public static TheoryData<string, ulong> OpeningRoutes => new()
    {
        { "Vanguard", 42 }, { "Vanguard", 43 }, { "Veilwalker", 42 }, { "Veilwalker", 43 },
        { "Arcanist", 42 }, { "Arcanist", 43 }, { "Gravecaller", 42 }, { "Gravecaller", 43 },
        { "Warden", 42 }, { "Warden", 43 }
    };

    private sealed class RoomMeasurement(string room)
    {
        public string Room { get; } = room;
        public int Commands { get; set; }
        public int CombatTicks { get; set; }
        public int HealthLost { get; set; }
        public int DamageApplied { get; set; }
        public int Potions { get; set; }
        public int LowestHealthPercent { get; set; } = 100;
        public int BellWarnings { get; set; }
        public void Observe(CombatView before, CombatView after, CombatEvent[] events, bool tick)
        {
            Commands++;
            if (tick && before.Actors.Any(a => a.Faction == CombatFaction.Enemy && a.Health > 0)) CombatTicks++;
            var prior = before.Actors.Single(a => a.Id == 1); var next = after.Actors.Single(a => a.Id == 1);
            HealthLost += Math.Max(0, prior.Health - next.Health);
            LowestHealthPercent = Math.Min(LowestHealthPercent, next.Health * 100 / next.MaxHealth);
            DamageApplied += events.Where(e => e.Kind == "DamageApplied" && e.TargetId == 1).Sum(e => e.Amount);
            Potions += events.Count(e => e.Kind == "Healed" && e.TargetId == 1 && e.ContentId == "potion");
            BellWarnings += events.Count(e => e.Kind == "CampaignHazardWarned" && e.ContentId == "campaign.champion_bell");
        }
    }

    [Theory]
    [MemberData(nameof(OpeningRoutes))]
    public void EveryFreshDisciplineCanFinishActOneAndClaimOptionalPilgrimWithoutInjectedBuild(string discipline, ulong seed)
    {
        var session = EndgameRuntimeSession.Create(Combat, Adventure, Policy, Campaign, Endgame, seed, discipline);
        int initialLevel = session.Production.ProgressionView.Level;
        var measurements = new Dictionary<string, RoomMeasurement>(StringComparer.Ordinal);
        bool claimed = false; string source = ""; int commands = 0;
        for (; commands < CampaignRuntimeSmoke.MaximumCommands; commands++)
        {
            var campaign = session.Campaign.Capture().Campaign;
            if (campaign.CompletedActs.Contains(1) && session.Combat.View.Loot.Count == 0 && !session.InRoamingChampion) break;
            var sighting = session.RoamingChampions.Entries.FirstOrDefault(e => e.Id == "champion.pilgrim" && e.Here);
            bool championRoute = session.InRoamingChampion || !claimed && sighting is not null;
            string room = session.Combat.EncounterId;
            if (!measurements.TryGetValue(room, out var measurement)) measurements.Add(room, measurement = new(room));
            var before = session.Combat.View;
            if (championRoute)
            {
                if (sighting is not null) source = sighting.SourceEncounterId;
                var command = RoamingChampionSmoke.Next(session, "champion.pilgrim");
                var result = session.Execute(command);
                Assert.True(result.Success, discipline + "/" + seed + " " + room + " " + result.Reason + " " + command);
                if (room == session.Combat.EncounterId) measurement.Observe(before, session.Combat.View, result.CombatEvents, command.Action == EndgameRuntimeAction.Tick);
                Assert.NotEqual("Failed", session.RoamingChampions.Run?.Stage);
                claimed |= session.RoamingChampions.Run?.Stage == "Claimed";
            }
            else
            {
                var command = CampaignRuntimeSmoke.Next(session.Campaign);
                var result = session.ExecuteCampaign(command);
                Assert.True(result.Success, discipline + "/" + seed + " " + room + " " + result.Reason + " " + command);
                if (room == session.Combat.EncounterId) measurement.Observe(before, session.Combat.View, result.CombatEvents, command.Action == CampaignRuntimeAction.Tick);
            }
        }
        var state = session.Campaign.Capture().Campaign;
        var character = session.Production.Capture().Progression.Character;
        var report = new
        {
            Discipline = discipline,
            Seed = seed,
            Commands = commands,
            Source = source,
            Deaths = state.Deaths,
            ActCompleted = state.CompletedActs.Contains(1),
            PilgrimClaimed = claimed,
            InitialLevel = initialLevel,
            FinalLevel = session.Production.ProgressionView.Level,
            PyreboundOwned = character.Items.Any(i => i.DefinitionId == "item.pyrebound_treads"),
            HeartOwned = character.OwnedFragments.Contains("fragment.heart_serath"),
            Rooms = measurements.Values.ToArray()
        };
        output.WriteLine("OPENING_BALANCE " + JsonSerializer.Serialize(report));
        Assert.True(state.CompletedActs.Contains(1), "Opening route exceeded its command bound.");
        Assert.Equal(0, state.Deaths);
        Assert.True(claimed, "The optional Pilgrim must be reachable on both source-room variants.");
        Assert.Equal(RoamingChampionCatalog.SourceEncounter(RoamingChampionCatalog.Find("champion.pilgrim")!, seed), source);
        Assert.Single(character.Items, i => i.DefinitionId == "item.last_toll");
        Assert.Contains(character.Items, i => i.DefinitionId == "item.pyrebound_treads");
        Assert.Contains("fragment.heart_serath", character.OwnedFragments);
        Assert.Contains(CampaignRuntimeSession.CryptEvent, state.CompletedExploration);
        Assert.Contains("campaign.bell_saint", state.CompletedEncounters);
        Assert.True(measurements["championarena.pilgrim"].CombatTicks > 0);
        Assert.True(measurements["championarena.pilgrim"].LowestHealthPercent > 0);
        Assert.Equal(session.StateHash, EndgameRuntimeSession.Restore(Combat, Adventure, Policy, Campaign, Endgame, session.Capture()).StateHash);
    }
}
