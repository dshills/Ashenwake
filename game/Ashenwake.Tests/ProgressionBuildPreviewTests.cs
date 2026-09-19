using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Xunit;

namespace Ashenwake.Tests;

public sealed class ProgressionBuildPreviewTests
{
    private static readonly Lazy<ProgressionContent> Content = new(() => ProductionContent.Resolve(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json")), ProgressionContent.Default()));

    [Theory]
    [InlineData(ProgressionBuildAction.AllocatePassive, "Offense", "")]
    [InlineData(ProgressionBuildAction.Respec, "", "")]
    [InlineData(ProgressionBuildAction.SelectMutation, "skill.shield_breaker", "mutation.avalanche")]
    public void EveryBuildPreviewMatchesTheActualTransactionWithoutChangingLiveState(ProgressionBuildAction action, string id, string value)
    {
        var session = BuildSession(); Assert.True(session.AllocatePassive("previous", "Defense").Success);
        string original = session.StateHash; var request = new ProgressionBuildRequest(action, id, value);
        var preview = session.PreviewBuild(request);
        Assert.True(preview.Success, preview.Reason); Assert.Empty(preview.Reason);
        Assert.Equal(original, session.StateHash); Assert.Equal(original, JsonData.Hash(preview.Before));
        Assert.Single(preview.After.Character.OperationReceipts.Keys.Except(preview.Before.Character.OperationReceipts.Keys));
        Assert.True(Apply(session, request).Success);
        Assert.Equal(WithoutNewReceipts(session.Capture(), preview.Before), WithoutNewReceipts(preview.After, preview.Before));
        Assert.Equal(JsonData.Hash(session.View), JsonData.Hash(preview.AfterView));
        Assert.Equal(JsonData.Hash(ProgressionSession.Restore(Content.Value, preview.Before).View), JsonData.Hash(preview.BeforeView));
    }

    [Theory]
    [InlineData("Offense")]
    [InlineData("Defense")]
    [InlineData("Resource")]
    public void PassivePreviewsSpendOneAvailablePointAndExposeDetachedDerivedStats(string passive)
    {
        var session = BuildSession(); var preview = session.PreviewBuild(new(ProgressionBuildAction.AllocatePassive, passive));
        Assert.True(preview.Success, preview.Reason);
        Assert.Equal(preview.BeforeView.AvailablePassivePoints - 1, preview.AfterView.AvailablePassivePoints);
        Assert.DoesNotContain("passive." + passive, preview.BeforeView.Stats.Keys);
        Assert.Equal(1, preview.AfterView.Stats["passive." + passive]);
        Assert.Equal(preview.Before.Character.Materials, preview.After.Character.Materials);
        Assert.Empty(session.Capture().Character.Passives);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)preview.AfterView.Stats)["passive." + passive] = 90);
        Assert.True(session.AllocatePassive("live", passive).Success);
        Assert.True(session.AllocatePassive("live-again", passive).Success);
        Assert.Equal(1, preview.AfterView.Stats["passive." + passive]);
        Assert.DoesNotContain("passive." + passive, preview.BeforeView.Stats.Keys);
    }

    [Fact]
    public void RespecPreviewRefundsAllPointsAndChargesOnlyTheExistingCost()
    {
        var session = BuildSession();
        Assert.True(session.AllocatePassive("a", "Offense").Success); Assert.True(session.AllocatePassive("b", "Offense").Success);
        Assert.True(session.AllocatePassive("c", "Resource").Success);
        Assert.True(session.SelectMutation("mutation", "skill.shield_breaker", "mutation.avalanche").Success);
        var preview = session.PreviewBuild(new(ProgressionBuildAction.Respec));
        Assert.True(preview.Success, preview.Reason); Assert.Empty(preview.After.Character.Passives);
        Assert.Equal(preview.BeforeView.AvailablePassivePoints + 3, preview.AfterView.AvailablePassivePoints);
        Assert.Equal(preview.Before.Character.Materials - Content.Value.Capture().RespecCost, preview.After.Character.Materials);
        Assert.Equal(JsonData.Hash(preview.Before.Character.Mastery), JsonData.Hash(preview.After.Character.Mastery));
        Assert.Equal("mutation.avalanche", preview.After.Character.SelectedMutations["skill.shield_breaker"]);
        Assert.Equal(2, session.Capture().Character.Passives["Offense"]);
    }

    [Fact]
    public void MutationPreviewEnforcesMasteryAndAllowsRemovingASelectedMutation()
    {
        var session = BuildSession(mastered: false); var request = new ProgressionBuildRequest(ProgressionBuildAction.SelectMutation, "skill.fire_lance", "mutation.forking_flame");
        AssertRejected(session, request);
        request = request with { Id = "skill.shield_breaker", Value = "mutation.avalanche" };
        AssertRejected(session, request);
        Assert.True(session.GainMastery("almost-mastered", request.Id, 99).Success); AssertRejected(session, request);
        Assert.True(session.GainMastery("mastered", request.Id, 1).Success);
        var preview = session.PreviewBuild(request); Assert.True(preview.Success, preview.Reason);
        Assert.Empty(session.Capture().Character.SelectedMutations);
        Assert.True(Apply(session, request).Success);
        var remove = session.PreviewBuild(request with { Value = "" });
        Assert.True(remove.Success, remove.Reason); Assert.Empty(remove.After.Character.SelectedMutations);
        Assert.Equal("mutation.avalanche", session.Capture().Character.SelectedMutations[request.Id]);
        Assert.Equal(remove.Before.Character.Materials, remove.After.Character.Materials);
        Assert.Equal(JsonData.Hash(remove.Before.Character.Mastery), JsonData.Hash(remove.After.Character.Mastery));
        Assert.True(Apply(session, request with { Value = "" }, "remove").Success);
        Assert.Equal(WithoutNewReceipts(remove.After, remove.Before), WithoutNewReceipts(session.Capture(), remove.Before));
    }

    [Fact]
    public void AllAuthoredMutationChoicesUseTheExistingDisciplineAndMasteryRules()
    {
        var combat = CombatContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json")));
        foreach (var mutation in combat.Mutations)
        {
            string discipline = combat.Skills.Single(skill => skill.Id == mutation.SkillId).Discipline;
            var session = BuildSession(discipline); var request = new ProgressionBuildRequest(ProgressionBuildAction.SelectMutation, mutation.SkillId, mutation.Id);
            var preview = session.PreviewBuild(request);
            Assert.True(preview.Success, mutation.Id + ": " + preview.Reason);
            Assert.Equal(mutation.Id, preview.After.Character.SelectedMutations[mutation.SkillId]);
            Assert.True(Apply(session, request).Success);
            Assert.Equal(WithoutNewReceipts(preview.After, preview.Before), WithoutNewReceipts(session.Capture(), preview.Before));
        }
    }

    [Theory]
    [InlineData("invalid-passive")]
    [InlineData("no-points")]
    [InlineData("empty-respec")]
    [InlineData("poor-respec")]
    [InlineData("unknown-skill")]
    [InlineData("wrong-discipline")]
    [InlineData("wrong-mutation")]
    public void RejectedBuildPreviewsMatchLiveReasonsAndLeaveBothSnapshotsUnchanged(string scenario)
    {
        var session = BuildSession();
        ProgressionBuildRequest request = scenario switch
        {
            "invalid-passive" => new(ProgressionBuildAction.AllocatePassive, "Magic"),
            "no-points" => new(ProgressionBuildAction.AllocatePassive, "Offense"),
            "empty-respec" or "poor-respec" => new(ProgressionBuildAction.Respec),
            "unknown-skill" => new(ProgressionBuildAction.SelectMutation, "skill.unknown", ""),
            "wrong-discipline" => new(ProgressionBuildAction.SelectMutation, "skill.fire_lance", "mutation.forking_flame"),
            _ => new(ProgressionBuildAction.SelectMutation, "skill.shield_breaker", "mutation.furnace")
        };
        if (scenario == "no-points")
            for (int i = 0; i < 9; i++) Assert.True(session.AllocatePassive("allocated." + i, "Offense").Success);
        if (scenario == "poor-respec")
        {
            Assert.True(session.AllocatePassive("allocated", "Defense").Success);
            var state = session.Capture(); state.Character.Materials = 0; session = ProgressionSession.Restore(Content.Value, state);
        }
        AssertRejected(session, request);
    }

    [Fact]
    public void UnknownBuildActionsReturnARejectionWithoutCreatingAReceipt()
    {
        var session = BuildSession(); string original = session.StateHash;
        var preview = session.PreviewBuild(new((ProgressionBuildAction)999));
        Assert.False(preview.Success); Assert.Equal("Unknown build action.", preview.Reason);
        Assert.Equal(original, JsonData.Hash(preview.Before)); Assert.Equal(original, JsonData.Hash(preview.After));
        Assert.Equal(original, session.StateHash);
        Assert.Throws<ArgumentNullException>(() => session.PreviewBuild(null!)); Assert.Equal(original, session.StateHash);
    }

    [Fact]
    public void PreviewReceiptCollisionsCannotTurnANewBuildChangeIntoAReplay()
    {
        var session = BuildSession(); var request = new ProgressionBuildRequest(ProgressionBuildAction.AllocatePassive, "Offense");
        for (int i = 0; i < 3; i++)
        {
            var preview = session.PreviewBuild(request); Assert.True(preview.Success, preview.Reason);
            string receipt = Assert.Single(preview.After.Character.OperationReceipts.Keys.Except(preview.Before.Character.OperationReceipts.Keys));
            Assert.True(session.EarnExperience(receipt, 0).Success);
        }
        string original = session.StateHash;
        var first = session.PreviewBuild(request); var second = session.PreviewBuild(request);
        Assert.True(first.Success, first.Reason); Assert.Equal(1, first.After.Character.Passives["Offense"]);
        Assert.Equal(JsonData.Hash(first), JsonData.Hash(second)); Assert.Equal(original, session.StateHash);
    }

    [Fact]
    public void ReturnedViewsAndSnapshotsCannotAliasTheLiveCharacterOrEachOther()
    {
        var session = BuildSession(); string original = session.StateHash;
        var preview = session.PreviewBuild(new(ProgressionBuildAction.AllocatePassive, "Offense"));
        string after = JsonData.Hash(preview.After); string beforeView = JsonData.Hash(preview.BeforeView); string afterView = JsonData.Hash(preview.AfterView);
        preview.Before.Character.Passives["Offense"] = 8; preview.Before.Character.Mastery.Clear(); preview.Before.Profile.Unlocks.Add("profile.fractures");
        Assert.Equal(after, JsonData.Hash(preview.After));
        preview.After.Character.Passives.Clear(); preview.After.Character.OperationReceipts.Clear(); preview.After.Profile.Discoveries.Add("discovery.greyhaven");
        Assert.Equal(beforeView, JsonData.Hash(preview.BeforeView)); Assert.Equal(afterView, JsonData.Hash(preview.AfterView));
        ((IDictionary<EquipmentSlot, long>)preview.BeforeView.Equipment)[EquipmentSlot.Head] = 999;
        Assert.Empty(preview.AfterView.Equipment); Assert.Empty(session.View.Equipment); Assert.Equal(original, session.StateHash);
    }

    private static void AssertRejected(ProgressionSession session, ProgressionBuildRequest request)
    {
        string original = session.StateHash; var preview = session.PreviewBuild(request);
        Assert.False(preview.Success); Assert.NotEmpty(preview.Reason);
        Assert.Equal(original, JsonData.Hash(preview.Before)); Assert.Equal(original, JsonData.Hash(preview.After));
        Assert.Equal(JsonData.Hash(preview.BeforeView), JsonData.Hash(preview.AfterView));
        var result = Apply(session, request); Assert.False(result.Success); Assert.Equal(result.Reason, preview.Reason);
        Assert.Equal(original, session.StateHash);
    }

    private static ProgressionResult Apply(ProgressionSession session, ProgressionBuildRequest request, string operation = "live") => request.Action switch
    {
        ProgressionBuildAction.AllocatePassive => session.AllocatePassive(operation, request.Id),
        ProgressionBuildAction.Respec => session.Respec(operation),
        _ => session.SelectMutation(operation, request.Id, request.Value)
    };

    private static string WithoutNewReceipts(ProgressionSnapshot state, ProgressionSnapshot before)
    {
        var detached = JsonData.Copy(state); detached.Character.OperationReceipts = new(before.Character.OperationReceipts); return JsonData.Hash(detached);
    }

    private static ProgressionSession BuildSession(string discipline = "Vanguard", bool mastered = true)
    {
        // Public XP/mastery transactions use the production-composed skill registry, as the playable campaign does.
        var session = ProgressionSession.Create(Content.Value, discipline);
        Assert.True(session.EarnExperience("level", 4500, 500).Success);
        if (mastered)
            foreach (var skill in Content.Value.Capture().Skills.Where(skill => skill.Discipline == discipline))
                Assert.True(session.GainMastery("mastery." + skill.Id, skill.Id, 100).Success);
        return session;
    }
}
