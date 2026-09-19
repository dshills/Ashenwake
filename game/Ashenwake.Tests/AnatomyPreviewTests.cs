using Ashenwake.Core.Adventure;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class AnatomyPreviewTests
{
    [Fact]
    public void EarnedHeartPreviewMatchesRealInstallationWithoutCommittingRewardOrDiscovery()
    {
        var content = AdventureContent.Default();
        var session = AdventureSession.Create(content, 42);
        ReachBellFinalPhase(session);
        Assert.True(session.EncounterCompleted("bell_saint.3").Success);
        Assert.True(session.EnterRoom("room.greyhaven").Success);
        string saved = AdventureSaveStore.Serialize(content, session);
        string hash = session.StateHash;

        var preview = session.PreviewFragment("Heart", "fragment.heart_serath");

        Assert.True(preview.Success);
        Assert.True(preview.CanApply);
        Assert.Equal(12, preview.Before.Resonance);
        Assert.Equal(30, preview.After.Resonance);
        Assert.Empty(preview.Before.QualifyingConcordances);
        Assert.Equal(new[] { "concordance.funeral_flame" }, preview.After.QualifyingConcordances);
        Assert.Equal(new[] { "concordance.funeral_flame" }, preview.After.DiscoveredConcordances);
        Assert.Empty(session.Capture().Concordances);
        Assert.Equal(hash, session.StateHash);
        Assert.Equal(saved, AdventureSaveStore.Serialize(content, session));
        Assert.True(session.InstallFragment("Heart", "fragment.heart_serath").Success);
        AssertMatchesCommitted(preview.After, session);
        Assert.Equal(1, session.Capture().BellVictories);
    }

    [Fact]
    public void ReplacementUsesTheSameSlotAndConcordanceRulesAsItsRealTransaction()
    {
        var original = AdventureContent.Default().Capture();
        var content = AdventureContent.Create(original with
        {
            Fragments = [.. original.Fragments, new("fragment.eye_orrun", "Eyes", ["Oath"], 8)]
        });
        var session = AdventureSession.Create(content);
        ReachBellFinalPhase(session);
        Assert.True(session.EncounterCompleted("bell_saint.3").Success);
        Assert.True(session.EnterRoom("room.greyhaven").Success);
        Assert.True(session.InstallFragment("Heart", "fragment.heart_serath").Success);

        var preview = session.PreviewFragment("Eyes", "fragment.eye_orrun");

        Assert.True(preview.Success);
        Assert.Equal(30, preview.Before.Resonance);
        Assert.Equal(26, preview.After.Resonance);
        Assert.Equal("fragment.eye_orrun", preview.After.Anatomy["Eyes"]);
        Assert.Equal(new[] { "concordance.funeral_flame" }, preview.Before.QualifyingConcordances);
        Assert.Empty(preview.After.QualifyingConcordances);
        Assert.Equal(preview.Before.DiscoveredConcordances, preview.After.DiscoveredConcordances);
        Assert.True(session.InstallFragment("Eyes", "fragment.eye_orrun").Success);
        AssertMatchesCommitted(preview.After, session);
    }

    [Theory]
    [InlineData("Tail", null)]
    [InlineData("Heart", "fragment.heart_serath")]
    [InlineData("Eyes", "fragment.nerve_ilyra")]
    [InlineData("Eyes", "fragment.missing")]
    public void InvalidFragmentPreviewRejectsWithoutChangingEitherProjectionOrLiveState(string slot, string? id)
    {
        var session = AdventureSession.Create(AdventureContent.Default());
        string hash = session.StateHash;

        var preview = session.PreviewFragment(slot, id);
        var real = session.InstallFragment(slot, id);

        Assert.False(preview.Success);
        Assert.False(preview.CanApply);
        Assert.False(real.Success);
        Assert.Equal(real.Reason, preview.Reason);
        Assert.Equal(JsonData.Write(preview.Before), JsonData.Write(preview.After));
        Assert.Equal(hash, session.StateHash);
    }

    [Theory]
    [InlineData("manifestation.burning_blood")]
    [InlineData("manifestation.missing")]
    public void InvalidManifestationPreviewUsesTheRealTransactionRejection(string id)
    {
        var session = AdventureSession.Create(AdventureContent.Default());
        string hash = session.StateHash;
        var preview = session.PreviewManifestation(id);
        var real = session.SelectManifestation(id);

        Assert.False(preview.Success);
        Assert.False(preview.CanApply);
        Assert.False(real.Success);
        Assert.Equal(real.Reason, preview.Reason);
        Assert.Equal(JsonData.Write(preview.Before), JsonData.Write(preview.After));
        Assert.Equal(hash, session.StateHash);
    }

    [Fact]
    public void RemovingAndReinstallingAnatomySuppressesAndReactivatesRememberedChoices()
    {
        var session = RewardedLoadout();
        Assert.True(session.SelectManifestation("manifestation.burning_blood").Success);
        Assert.True(session.SelectManifestation("manifestation.whispering_shadow").Success);

        var removal = session.PreviewFragment("Heart", null);

        Assert.Equal(64, removal.Before.Resonance);
        Assert.Equal(46, removal.After.Resonance);
        Assert.True(Threshold(removal.After, 40).Active);
        Assert.False(Threshold(removal.After, 60).Available);
        Assert.False(Threshold(removal.After, 60).Active);
        Assert.Equal("manifestation.whispering_shadow", Threshold(removal.After, 60).SelectedId);
        Assert.Empty(removal.After.QualifyingConcordances);
        Assert.Equal(new[] { "concordance.funeral_flame" }, removal.After.DiscoveredConcordances);
        Assert.True(session.InstallFragment("Heart", null).Success);
        AssertMatchesCommitted(removal.After, session);

        var restored = session.PreviewFragment("Heart", "fragment.heart_serath");

        Assert.True(Threshold(restored.After, 60).Available);
        Assert.True(Threshold(restored.After, 60).Active);
        Assert.Equal("manifestation.whispering_shadow", Threshold(restored.After, 60).SelectedId);
        Assert.Equal(new[] { "concordance.funeral_flame" }, restored.After.QualifyingConcordances);
        Assert.Equal(removal.After.DiscoveredConcordances, restored.After.DiscoveredConcordances);
        var committed = session.InstallFragment("Heart", "fragment.heart_serath");
        Assert.True(committed.Success);
        Assert.DoesNotContain(committed.Events, e => e.StartsWith("ConcordanceDiscovered:", StringComparison.Ordinal));
        AssertMatchesCommitted(restored.After, session);
    }

    [Fact]
    public void ManifestationPreviewReplacesOneThresholdAndLeavesOtherChoicesIntact()
    {
        var session = RewardedLoadout();
        Assert.True(session.SelectManifestation("manifestation.burning_blood").Success);
        Assert.True(session.SelectManifestation("manifestation.whispering_shadow").Success);
        string hash = session.StateHash;

        var preview = session.PreviewManifestation("manifestation.stone_memory");

        Assert.True(preview.Success);
        Assert.True(preview.CanApply);
        Assert.Equal("manifestation.burning_blood", Threshold(preview.Before, 40).SelectedId);
        Assert.Equal("manifestation.stone_memory", Threshold(preview.After, 40).SelectedId);
        Assert.True(Threshold(preview.After, 40).Active);
        Assert.Equal(Threshold(preview.Before, 60), Threshold(preview.After, 60));
        Assert.Equal(hash, session.StateHash);
        Assert.True(session.SelectManifestation("manifestation.stone_memory").Success);
        AssertMatchesCommitted(preview.After, session);
    }

    [Theory]
    [InlineData(39, false, false)]
    [InlineData(40, true, false)]
    [InlineData(59, true, false)]
    [InlineData(60, true, true)]
    public void ThresholdAvailabilityMatchesTheRealSelectionAtItsExactBoundary(int resonance, bool firstAvailable, bool secondAvailable)
    {
        var original = AdventureContent.Default().Capture();
        var content = AdventureContent.Create(original with
        {
            Fragments = original.Fragments.Select(f => f.Id == "fragment.eye_vael" ? f with { Resonance = resonance } : f).ToArray()
        });
        var session = AdventureSession.Create(content);
        var preview = session.PreviewFragment("Eyes", "fragment.eye_vael");

        Assert.Equal(resonance, preview.After.Resonance);
        Assert.Equal(firstAvailable, Threshold(preview.After, 40).Available);
        Assert.Equal(secondAvailable, Threshold(preview.After, 60).Available);
        Assert.All(preview.After.Manifestations, threshold => Assert.False(threshold.Active));
        Assert.Equal(firstAvailable, session.PreviewManifestation("manifestation.burning_blood").Success);
        Assert.Equal(firstAvailable, session.SelectManifestation("manifestation.burning_blood").Success);
        Assert.Equal(secondAvailable, session.PreviewManifestation("manifestation.whispering_shadow").Success);
        Assert.Equal(secondAvailable, session.SelectManifestation("manifestation.whispering_shadow").Success);
    }

    [Fact]
    public void AwayPreviewAllowsPlanningButDoesNotGrantEitherServiceTransaction()
    {
        var content = AdventureContent.Default();
        var session = RewardedLoadout();
        Assert.True(session.EnterRoom("room.ossuary").Success);
        string hash = session.StateHash;
        string saved = AdventureSaveStore.Serialize(content, session);
        var atHub = session.Capture(); atHub.RoomId = content.Capture().Hub;
        var control = AdventureSession.Restore(content, atHub);

        var fragment = session.PreviewFragment("Heart", null);
        var manifestation = session.PreviewManifestation("manifestation.stone_memory");

        Assert.True(fragment.Success);
        Assert.False(fragment.CanApply);
        Assert.True(manifestation.Success);
        Assert.False(manifestation.CanApply);
        Assert.False(session.InstallFragment("Heart", null).Success);
        Assert.False(session.SelectManifestation("manifestation.stone_memory").Success);
        Assert.Equal(JsonData.Write(control.PreviewFragment("Heart", null).After), JsonData.Write(fragment.After));
        Assert.Equal(JsonData.Write(control.PreviewManifestation("manifestation.stone_memory").After), JsonData.Write(manifestation.After));
        Assert.Equal(hash, session.StateHash);
        Assert.Equal(saved, AdventureSaveStore.Serialize(content, session));
    }

    [Fact]
    public void RepeatedPreviewDuringTheBossPreservesRestoreHashAndTheNextReward()
    {
        var content = AdventureContent.Default();
        var session = AdventureSession.Create(content, 123);
        ReachBellFinalPhase(session);
        var control = AdventureSession.Restore(content, session.Capture());
        string hash = session.StateHash;
        string saved = AdventureSaveStore.Serialize(content, session);
        string definitionHash = content.Hash;

        for (int i = 0; i < 25; i++)
        {
            Assert.True(session.PreviewFragment("Spine", "fragment.nerve_ilyra").Success);
            Assert.True(session.PreviewFragment("Eyes", null).Success);
            Assert.False(session.PreviewManifestation("manifestation.burning_blood").Success);
        }

        Assert.Equal(hash, session.StateHash);
        Assert.Equal(hash, AdventureSaveStore.Deserialize(content, saved).StateHash);
        Assert.Equal(saved, AdventureSaveStore.Serialize(content, session));
        Assert.Equal(definitionHash, content.Hash);
        var afterPreview = session.EncounterCompleted("bell_saint.3");
        var withoutPreview = control.EncounterCompleted("bell_saint.3");
        Assert.True(afterPreview.Success);
        Assert.Equal(withoutPreview.Events, afterPreview.Events);
        Assert.Equal(control.StateHash, session.StateHash);
    }

    [Fact]
    public void ReturnedPresentationCollectionsCannotAlterTheSessionOrLaterPreviews()
    {
        var session = RewardedLoadout();
        var preview = session.PreviewManifestation("manifestation.burning_blood");
        string expected = JsonData.Write(preview);
        string hash = session.StateHash;

        preview.Before.QualifyingConcordances[0] = "concordance.changed";
        preview.After.DiscoveredConcordances[0] = "concordance.changed";
        preview.After.Manifestations[0] = new(999, "manifestation.changed", true, true);
        var anatomy = Assert.IsAssignableFrom<IDictionary<string, string>>(preview.After.Anatomy);
        Assert.Throws<NotSupportedException>(() => anatomy["Eyes"] = "fragment.changed");

        Assert.Equal(hash, session.StateHash);
        Assert.Equal(expected, JsonData.Write(session.PreviewManifestation("manifestation.burning_blood")));
    }

    private static ManifestationThresholdPreview Threshold(AnatomyLoadoutPreview preview, int threshold) =>
        Assert.Single(preview.Manifestations, m => m.Threshold == threshold);

    private static void AssertMatchesCommitted(AnatomyLoadoutPreview preview, AdventureSession session)
    {
        var state = session.Capture();
        Assert.Equal(session.View.Resonance, preview.Resonance);
        Assert.Equal(state.Anatomy.ToArray(), preview.Anatomy.ToArray());
        Assert.Equal(state.Concordances.ToArray(), preview.DiscoveredConcordances);
        Assert.Equal(session.View.ActiveManifestations, preview.Manifestations.Where(m => m.Active).Select(m => m.SelectedId).ToArray());
        Assert.Equal(state.Manifestations.ToArray(), preview.Manifestations.Where(m => m.SelectedId is not null)
            .Select(m => new KeyValuePair<int, string>(m.Threshold, m.SelectedId!)).ToArray());
    }

    private static AdventureSession RewardedLoadout()
    {
        var session = AdventureSession.Create(AdventureContent.Default(), 42);
        ReachBellFinalPhase(session);
        Assert.True(session.EncounterCompleted("bell_saint.3").Success);
        Assert.True(session.EnterRoom("room.greyhaven").Success);
        Assert.True(session.InstallFragment("Heart", "fragment.heart_serath").Success);
        Assert.True(session.InstallFragment("Spine", "fragment.nerve_ilyra").Success);
        Assert.True(session.InstallFragment("Arms", "fragment.orrun_bone").Success);
        return session;
    }

    private static void ReachBellFinalPhase(AdventureSession session)
    {
        Assert.True(session.Interact("npc.mara").Success);
        Assert.True(session.EnterRoom("room.ossuary").Success);
        Assert.True(session.EncounterCompleted("encounter.ossuary").Success);
        Assert.True(session.EnterRoom("room.cloister").Success);
        Assert.True(session.EncounterCompleted("encounter.cloister").Success);
        Assert.True(session.EnterRoom("room.bell_sanctum").Success);
        Assert.True(session.EncounterCompleted("bell_saint.1").Success);
        Assert.True(session.Interact("ritual.anchor_left").Success);
        Assert.True(session.Interact("ritual.anchor_right").Success);
        Assert.True(session.EncounterCompleted("bell_saint.2").Success);
    }
}
