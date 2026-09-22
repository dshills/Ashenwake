using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class EndgameBalanceTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Progression => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign => CampaignContent.Parse(Read("campaign.json"));
    private static EndgameContent Endgame => EndgameContent.Parse(Read("endgame.json"));
    private static readonly Lazy<string> Composed = new(() => EndgameCombatContent.Parse(
        CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson);
    private static EndgameRuntimeSession Fresh() => EndgameRuntimeSession.Create(Composed.Value, Adventure, Progression, Campaign, Endgame);
    private static EndgameRuntimeSession Restore(EndgameRuntimeSnapshot state) => EndgameRuntimeSession.Restore(Composed.Value, Adventure, Progression, Campaign, Endgame, state);

    [Fact]
    public void FreshPolicyPreservesManagedCampaignInputsAndDoesNotMutateWhenRead()
    {
        var session = Fresh(); var policy = new EndgameBalancePolicy();
        Assert.Equal(new CampaignBalancePolicy(managedBuild: true).Hash, policy.Campaign.Hash);
        Assert.False(policy.Complete(session)); Assert.False(session.View.Unlocked);
        for (int i = 0; i < 160; i++)
        {
            string hash = session.StateHash;
            var command = policy.Next(session);
            Assert.Equal(EndgameRuntimeAction.Campaign, command.Action);
            Assert.Equal(JsonData.Hash(policy.Campaign.Next(session.Campaign)), JsonData.Hash(command.Campaign));
            Assert.Equal(hash, session.StateHash);
            var result = session.Execute(command); Assert.True(result.Success, result.Reason);
        }
        var replay = EndgameRuntimeReplayRunner.Run(Composed.Value, Adventure, Progression, Campaign, Endgame, session.CaptureReplay());
        Assert.True(replay.Success); Assert.Equal(session.StateHash, replay.FinalHash);
    }

    [Fact]
    public void PolicyIdentityDeclaresItsFreshEarnedScopeAndIsStable()
    {
        var policy = new EndgameBalancePolicy();
        Assert.Equal(policy.Hash, new EndgameBalancePolicy().Hash);
        Assert.NotEqual(policy.Hash, policy.Campaign.Hash);
        Assert.Equal(3, EndgameBalancePolicy.TargetTier);
        Assert.Contains("No God Hunts", policy.Description);
        Assert.Contains("lowest-ID earned sigil", policy.Description);
    }

    [Fact]
    public void MeasurementCountsOnlySimulatedInputsAndPlayerIncomingEvents()
    {
        var session = Fresh(); var measured = new EndgameBalanceMeasurement(session);
        string hash = session.StateHash;
        var before = EndgameBalanceObservation.Capture(session) with { InHub = false, LiveEnemies = true, RunId = 1, Tier = 1 };
        var result = new EndgameRuntimeResult(true, "", [
            new(0, "DamageApplied", ActorId: 2, TargetId: 1, Amount: 8),
            new(0, "DamageApplied", ActorId: 1, TargetId: 2, Amount: 700),
            new(0, "DamageApplied", ActorId: 3, TargetId: 4, Amount: 600),
            new(0, "AbilityStarted", ActorId: 1, ContentId: "skill.cleave"),
            new(0, "AbilityStarted", ActorId: 2, ContentId: "enemy.skill"),
            new(0, "Healed", TargetId: 1, Amount: 20, ContentId: "potion")], []);
        var row = measured.Observe(before, new(EndgameRuntimeAction.Tick), result, session);
        measured.Observe(before, new(EndgameRuntimeAction.Campaign, Campaign: new(CampaignRuntimeAction.Tick)), new(true, "", [], []), session);
        measured.Observe(before, new(EndgameRuntimeAction.AdvanceEncounter), new(true, "", [], []), session);
        Assert.Equal(hash, session.StateHash);
        Assert.Equal(2, row.CombatTicks); Assert.Equal(1, row.MenuCommands); Assert.Equal(3, measured.Commands);
        Assert.Equal(8, row.DamageTaken); Assert.Equal(1, row.Potions);
        Assert.Single(row.SkillsActivated); Assert.Equal(1, row.SkillsActivated["skill.cleave"]);
    }

    [Fact]
    public void MeasurementSeparatesClearLootTravelRetriesAndCommittedRoomRewards()
    {
        var session = Fresh(); var measured = new EndgameBalanceMeasurement(session);
        var before = EndgameBalanceObservation.Capture(session) with { InHub = false, LiveEnemies = true, RunId = 1, Tier = 1 };
        var row = measured.Observe(before, new(EndgameRuntimeAction.Tick), new(true, "", [], ["ExpeditionAttemptConsumed"]), session);
        measured.Observe(before, new(EndgameRuntimeAction.RetryEncounter), new(true, "", [], ["EndgameAttemptRestarted"]), session);
        measured.Observe(before, new(EndgameRuntimeAction.Tick), new(true, "", [], ["EndgameRoomCleared:0"]), session);
        var cleared = before with { LiveEnemies = false, Cleared = true, HasLoot = true };
        measured.Observe(cleared, new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Pickup)]), new(true, "", [], []), session);
        measured.Observe(cleared, new(EndgameRuntimeAction.Tick, Commands: [new(CombatCommandKind.Move, X: 1)]), new(true, "", [], []), session);
        measured.Observe(cleared, new(EndgameRuntimeAction.AdvanceEncounter), new(true, "", [], ["EndgameEncounterCompleted:run.1.encounter.0"]), session);
        Assert.Equal(2, row.Attempts); Assert.Equal(1, row.Deaths); Assert.True(row.Cleared); Assert.True(row.RewardCommitted);
        Assert.Equal(2, row.CombatTicks); Assert.Equal(1, row.LootTicks); Assert.Equal(1, row.TravelTicks); Assert.Equal(2, row.MenuCommands);
        Assert.Equal(before.RoomIndex, row.Exit.RoomIndex); Assert.Equal(before.RunId, row.Exit.RunId);
    }

    [Fact]
    public void RejectedRuntimeTickIsRecordedWithoutInventingSimulationDuration()
    {
        var session = Fresh(); var measured = new EndgameBalanceMeasurement(session);
        var before = EndgameBalanceObservation.Capture(session) with { InHub = false, LiveEnemies = true, RunId = 1, Tier = 1 };
        var row = measured.Observe(before, new(EndgameRuntimeAction.Tick), new(false, "Retry required.", [], []), session);
        Assert.Equal(1, row.RejectedOperations); Assert.Equal(1, measured.Commands);
        Assert.Equal(0, row.CombatTicks + row.TravelTicks + row.LootTicks + row.MenuCommands);
    }

    [Fact]
    public void FreshEarnedCampaignManagesItsOwnedBuildAndUsesOrdinaryFiniteFractureInputs()
    {
        var session = Fresh(); var policy = new EndgameBalancePolicy(); EndgameBalanceMeasurement? measured = null;
        int commands = 0; int segment = 0; int segments = 0;
        while (!EndgameRuntimeSmoke.Complete(session, 1) && commands < 20000)
        {
            if (measured is null && policy.Campaign.Complete(session.Campaign)) measured = new(session);
            var before = EndgameBalanceObservation.Capture(session); string hash = session.StateHash;
            var command = policy.Next(session);
            if (measured is not null && !session.InHub) Assert.Equal(JsonData.Hash(EndgameRuntimeSmoke.Next(session, 3)), JsonData.Hash(command));
            Assert.Equal(hash, session.StateHash);
            var result = session.Execute(command); Assert.True(result.Success, result.Reason);
            measured?.Observe(before, command, result, session); commands++; segment++;
            if (segment == 600)
            {
                var replay = EndgameRuntimeReplayRunner.Run(Composed.Value, Adventure, Progression, Campaign, Endgame, session.CaptureReplay());
                Assert.True(replay.Success); Assert.Equal(session.StateHash, replay.FinalHash);
                session = Restore(session.Capture()); segment = 0; segments++;
            }
        }
        Assert.True(EndgameRuntimeSmoke.Complete(session, 1)); Assert.True(segments > 0);
        Assert.NotNull(measured); Assert.True(policy.Campaign.Complete(session.Campaign));
        var finalReplay = EndgameRuntimeReplayRunner.Run(Composed.Value, Adventure, Progression, Campaign, Endgame, session.CaptureReplay());
        Assert.True(finalReplay.Success); Assert.Equal(session.StateHash, finalReplay.FinalHash);
        Assert.Equal(4, measured.Rooms.Count(r => r.RewardCommitted));
        Assert.All(measured.Rooms.Where(r => !r.Entry.InHub), row => Assert.True(row.Cleared));
        Assert.Equal(session.RunView!.Deaths, measured.Rooms.Sum(r => r.Deaths));
        Assert.Single(session.Capture().Endgame.Rewards); Assert.Equal(2, Assert.Single(session.View.AvailableSigils).Tier);
        Assert.Empty(session.Production.Capture().Progression.Character.SelectedMutations);
        Assert.False(policy.Complete(session));
    }
}
