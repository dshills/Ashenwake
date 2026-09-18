using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Xunit;

namespace Ashenwake.Tests;

public sealed class EndgameTests
{
    private static EndgameSession Session() => EndgameSession.Create(EndgameContent.Default(), true);
    private static EndgameReward Finish(EndgameSession session)
    {
        EndgameReward? reward = null;
        for (int i = 0; i < 4 && session.View.EncounterId is not null; i++)
        {
            string[] candidates = ["Stormbound", "Riftborn", "Hunter"];
            int room = session.Capture().Run!.EncounterIndex;
            var result = session.CompleteEncounter(session.View.EncounterId!, room < candidates.Length ? candidates[room] : null); Assert.True(result.Success, result.Reason); reward = result.Reward ?? reward;
        }
        return Assert.IsType<EndgameReward>(reward);
    }
    private static void ReachTier(EndgameSession session, int tier)
    {
        session.AwardSigil("initial", 42, 1);
        for (int i = 1; i <= tier; i++) { Assert.True(session.StartFracture(session.View.AvailableSigils.First(s => s.Tier == i).Id).Success); Finish(session); }
    }
    [Fact]
    public void SigilGenerationIsDeterministicBoundedAndExcludesForbiddenPairs()
    {
        var content = EndgameContent.Default();
        for (ulong seed = 0; seed < 1000; seed++)
        {
            int tier = 1 + (int)(seed % 10); var sigil = EndgameContent.GenerateSigil(content, 1, seed, tier);
            Assert.Equal(JsonData.Hash(sigil), JsonData.Hash(EndgameContent.GenerateSigil(content, 1, seed, tier)));
            EndgameContent.ValidateSigil(content, sigil); Assert.InRange(sigil.Modifiers.Length, 1, 3);
            Assert.False(sigil.Modifiers.Contains("fracture.healing_echoes") && sigil.Modifiers.Contains("fracture.elite_hazards"));
        }
        var invalid = EndgameContent.GenerateSigil(content, 1, 0, 5) with { Modifiers = ["fracture.healing_echoes", "fracture.elite_hazards"] };
        Assert.Throws<InvalidDataException>(() => EndgameContent.ValidateSigil(content, invalid));
    }
    [Fact]
    public void FiniteAttemptsConsumeSigilAndFailureCannotAwardOrRestartIt()
    {
        var session = Session(); session.AwardSigil("sigil", 1, 1); Assert.True(session.StartFracture(1).Success);
        string encounter = session.View.EncounterId!;
        Assert.True(session.PlayerDied().Success); Assert.Equal(2, session.View.AttemptsRemaining);
        session.PlayerDied(); session.PlayerDied(); Assert.Equal("Failed", session.View.RunStatus);
        string hash = session.StateHash; Assert.False(session.CompleteEncounter(encounter).Success); Assert.False(session.StartFracture(1).Success);
        Assert.Equal(hash, session.StateHash); Assert.Empty(session.Capture().Rewards); Assert.Empty(session.View.Rules);
    }
    [Fact]
    public void ClearCommitsRewardAndNextSigilExactlyOnceAcrossRestore()
    {
        var content = EndgameContent.Default(); var session = Session(); session.AwardSigil("sigil", 42, 1); session.StartFracture(1);
        session.PlayerDied(); session = EndgameSession.Restore(content, JsonData.Copy(session.Capture()));
        var reward = Finish(session); Assert.Equal(1, reward.Deaths); Assert.Equal(1, session.View.HighestClearedTier);
        Assert.Single(session.View.AvailableSigils); Assert.Equal(2, session.View.AvailableSigils[0].Tier);
        string hash = session.StateHash;
        Assert.False(session.CompleteEncounter("run.1.encounter.3").Success); Assert.Equal(hash, session.StateHash);
        Assert.True(session.AwardSigil("sigil", 42, 1).Success); Assert.Equal(hash, session.StateHash);
        Assert.False(session.AwardSigil("sigil", 99, 1).Success); Assert.Equal(hash, session.StateHash);
        Assert.Equal(hash, EndgameSession.Restore(content, session.Capture()).StateHash);
        var malformed = session.Capture(); malformed.Rewards[reward.RunId] = reward with { Materials = reward.Materials + 1 };
        Assert.Throws<InvalidDataException>(() => EndgameSession.Restore(content, malformed));
    }
    [Fact]
    public void FractureRuleEffectsAreBoundedAndBossInheritanceIsCompatible()
    {
        var content = EndgameContent.Default(); var session = Session(); ReachTier(session, 4);
        var state = session.Capture(); var sigil = state.Sigils.Single(s => !s.Consumed);
        state.Sigils = state.Sigils.Select(s => s.Id == sigil.Id ? s with { Modifiers = ["fracture.healing_echoes", "fracture.fragment_overcharge", "fracture.inherited_boss"] } : s).ToArray();
        session = EndgameSession.Restore(content, state); session.StartFracture(sigil.Id);
        for (int i = 0; i < 20; i++) Assert.True(session.ObserveRuleEvent("heal." + i, "PlayerHealed", 10).Success);
        Assert.Equal(8, session.RulesAt(0).HostileHealingEchoes); Assert.Equal(150, session.RulesAt(0).FragmentPowerPercent); Assert.Equal(100, session.RulesAt(30).FragmentPowerPercent);
        string hash = session.StateHash; session.ObserveRuleEvent("heal.0", "PlayerHealed", 10); Assert.Equal(hash, session.StateHash);
        Assert.False(session.ObserveRuleEvent("heal.0", "PlayerHealed", 20).Success);
        string beforeMissing = session.StateHash; Assert.False(session.CompleteEncounter(session.View.EncounterId!).Success); Assert.Equal(beforeMissing, session.StateHash);
        foreach (string candidate in new[] { "Stormbound", "Riftborn", "Hunter" }) session.CompleteEncounter(session.View.EncounterId!, candidate);
        var inherited = session.RulesAt(0).BossModifiers; Assert.InRange(inherited.Length, 1, 2); Assert.Equal(inherited.Length, inherited.Distinct().Count());
        Ashenwake.Core.Combat.CombatSession.ValidateEliteModifiers(inherited);
        session.Abandon(); Assert.Equal(100, session.RulesAt(0).FragmentPowerPercent); Assert.Empty(session.RulesAt(0).BossModifiers); Assert.Equal(0, session.RulesAt(0).HostileHealingEchoes);
    }
    [Fact]
    public void GodHuntsUnlockThroughTierProgressAndSecretRequiresAllFourPrimaryHunts()
    {
        var session = Session(); Assert.False(session.StartGodHunt("hunt.false_vael", 0).Success);
        ReachTier(session, 8); Assert.Equal(4, session.View.UnlockedHunts.Length);
        foreach (string hunt in new[] { "hunt.false_vael", "hunt.ilyra_teeth", "hunt.thousand_memories", "hunt.orrun_without_oath" })
        {
            Assert.True(session.StartGodHunt(hunt, 42).Success); var reward = Finish(session);
            Assert.Equal("GodHunt", reward.Kind); Assert.Equal(1, reward.EvolutionCount); Assert.NotEmpty(reward.EvolutionMaterial);
        }
        Assert.Contains("hunt.nhal_reconstruction", session.View.UnlockedHunts);
        Assert.True(session.StartGodHunt("hunt.nhal_reconstruction", 2).Success); Finish(session);
        Assert.Equal(1, session.View.TotalEvolutionMaterialsAwarded["material.nhal_absence"]);
    }
    [Fact]
    public void LockedCampaignAndForgedRewardOrModifierStateAreRejected()
    {
        var content = EndgameContent.Default(); var locked = EndgameSession.Create(content);
        Assert.False(locked.AwardSigil("early", 1, 1).Success);
        var session = Session(); session.AwardSigil("sigil", 1, 1); session.StartFracture(1);
        var state = session.Capture(); state.Run!.Status = "Completed";
        Assert.Throws<InvalidDataException>(() => EndgameSession.Restore(content, state));
        state = session.Capture(); state.Run!.InheritedEliteModifiers = ["Null", "Devourer"];
        Assert.Throws<InvalidDataException>(() => EndgameSession.Restore(content, state));
        state = session.Capture(); state.Sigils[0].Modifiers[0] = "fracture.unknown";
        Assert.Throws<InvalidDataException>(() => EndgameSession.Restore(content, state));
    }
}
