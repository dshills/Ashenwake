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

public sealed class BuildLoadoutTests
{
    private static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));
    private static string CombatJson => Read("combat.json");
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static ProductionSession Restore(ProductionSnapshot state) => ProductionSession.Restore(CombatJson, Adventure, Policy, state);
    private static ProductionSession Fresh()
    {
        var state = ProductionSession.Create(CombatJson, Adventure, Policy).Capture();
        state.Expedition.Combat.Actors.Single(a => a.Id == 1).Position = new(-4500, -1800);
        return Restore(state);
    }
    private static void Visit(ProductionSession session, string service)
    {
        var action = new ProductionCommand(ProductionAction.Expedition, new(ExpeditionAction.Interact, service));
        for (int i = 0; i < 300; i++)
        {
            var next = ProductionSmoke.AtInteraction(session, service, action);
            if (next == action) return;
            Assert.True(session.Execute(next).Success);
        }
        Assert.Fail("Unable to reach specialist.");
    }
    private static readonly Lazy<ProductionSnapshot> Earned = new(() =>
    {
        var session = Fresh();
        for (int i = 0; i < ProductionSmoke.MaximumCommands && !ProductionSmoke.Complete(session); i++)
        { var result = session.Execute(ProductionSmoke.Next(session)); Assert.True(result.Success, result.Reason); }
        Assert.True(ProductionSmoke.Complete(session)); Visit(session, "service.mara"); return session.Capture();
    });

    [Fact]
    public void AtomicApplyRestoresGearAnatomyMutationsAndPassivesWithoutGrantsAndReplays()
    {
        var session = Restore(Earned.Value);
        if (session.ProgressionView.AvailablePassivePoints > 0) Assert.True(session.AllocatePassive("Offense").Success);
        var skill = session.Content.Capture().Skills.First(s => s.Discipline == session.ProgressionView.Discipline && s.Mutations.Length > 0);
        // Explicit mastered-skill fixture; mutations still enter through ordinary production commands.
        var mastered = session.Capture(); mastered.Progression.Character.Mastery[skill.Id] = 100;
        mastered.Expedition.Combat.ProgressionBuild = mastered.Expedition.Combat.ProgressionBuild with
        { UnlockedMutations = session.Content.Capture().Skills.Where(s => mastered.Progression.Character.Mastery.GetValueOrDefault(s.Id) >= 100).SelectMany(s => s.Mutations).Distinct().Order(StringComparer.Ordinal).ToArray() };
        session = Restore(mastered);
        Assert.True(session.SetMutation(skill.Id, skill.Mutations[0]).Success);
        Assert.True(session.InstallFragment("Spine", "fragment.nerve_ilyra").Success);
        Assert.True(session.SaveBuildLoadout("loadout.1", "Funeral Storm").Success);
        var saved = session.Capture();
        Assert.True(session.InstallFragment("Spine", null).Success);
        Assert.True(session.SetMutation(skill.Id, "").Success);
        Assert.True(session.Respec().Success); Assert.True(session.AllocatePassive("Defense").Success);
        Visit(session, "service.torren"); var slot = session.ProgressionView.Equipment.Keys.First(); Assert.True(session.Unequip(slot).Success); Visit(session, "service.mara");
        var before = session.Capture(); var preview = session.PreviewBuildLoadout("loadout.1");
        Assert.True(preview.Success, preview.Reason); Assert.Equal(Policy.Capture().RespecCost, preview.MaterialCost); Assert.Equal(0, preview.FragmentRemovalCost);
        Assert.Equal(JsonData.Hash(before), session.StateHash);
        Assert.True(session.ApplyBuildLoadout("loadout.1").Success);
        var after = session.Capture();
        Assert.Equal(JsonData.Hash(saved.Progression.Character.Equipment), JsonData.Hash(after.Progression.Character.Equipment));
        Assert.Equal(JsonData.Hash(saved.Expedition.Adventure.Anatomy), JsonData.Hash(after.Expedition.Adventure.Anatomy));
        Assert.Equal(JsonData.Hash(saved.Progression.Character.SelectedMutations), JsonData.Hash(after.Progression.Character.SelectedMutations));
        Assert.Equal(JsonData.Hash(saved.Progression.Character.Passives), JsonData.Hash(after.Progression.Character.Passives));
        Assert.Equal(JsonData.Hash(before.Progression.Character.Items), JsonData.Hash(after.Progression.Character.Items));
        Assert.Equal(before.Progression.Character.Materials - preview.MaterialCost, after.Progression.Character.Materials);
        Assert.Equal(before.Progression.Character.Experience, after.Progression.Character.Experience);
        Assert.Equal(JsonData.Hash(before.Progression.Profile), JsonData.Hash(after.Progression.Profile));
        Assert.Equal(before.Expedition.Combat.Actors.Single(a => a.Id == 1).Health, after.Expedition.Combat.Actors.Single(a => a.Id == 1).Health);
        Assert.Equal(0, session.PreviewBuildLoadout("loadout.1").MaterialCost);
        Assert.Equal(session.StateHash, Restore(after).StateHash);
        var replay = ProductionReplayRunner.Run(CombatJson, Adventure, Policy, session.CaptureReplay()); Assert.True(replay.Success, replay.Detail);
    }

    [Fact]
    public void StaleGearBlocksEveryPartAndIsIncludedInDestructionWarnings()
    {
        var session = Fresh(); Assert.True(session.SaveBuildLoadout("loadout.1", "Lost Oath").Success);
        var slot = session.ProgressionView.Equipment.Keys.First(); long item = session.ProgressionView.Equipment[slot];
        Assert.Contains("Build: Lost Oath", ProgressionSession.ItemPresetNames(session.Capture().Progression, item));
        Visit(session, "service.torren"); Assert.True(session.Unequip(slot).Success); Assert.True(session.Discard(item, true).Success); Visit(session, "service.mara");
        session = Restore(session.Capture()); string before = session.StateHash;
        Assert.Contains("no longer owned", session.PreviewBuildLoadout("loadout.1").Reason);
        Assert.False(session.ApplyBuildLoadout("loadout.1").Success); Assert.Equal(before, session.StateHash);
    }

    [Fact]
    public void InsufficientMaterialsCannotPartiallyRestoreAnatomyOrEquipment()
    {
        var session = Restore(Earned.Value); Assert.True(session.AllocatePassive("Offense").Success);
        Assert.True(session.SaveBuildLoadout("loadout.1", "Iron Vow").Success);
        Assert.True(session.Respec().Success); Assert.True(session.AllocatePassive("Defense").Success);
        Assert.True(session.InstallFragment("Spine", "fragment.nerve_ilyra").Success);
        var state = session.Capture(); state.Progression.Character.Materials = 0; state.Expedition.Adventure.Materials = 0; session = Restore(state);
        string before = session.StateHash; Assert.False(session.PreviewBuildLoadout("loadout.1").Success);
        Assert.False(session.ApplyBuildLoadout("loadout.1").Success); Assert.Equal(before, session.StateHash);
    }

    [Fact]
    public void AddingPassivesAndFreeFragmentChangesDoNotChargeRespec()
    {
        var session = Restore(Earned.Value); Assert.True(session.AllocatePassive("Offense").Success);
        Assert.True(session.SaveBuildLoadout("loadout.1", "Ascending Pyre").Success); Assert.True(session.Respec().Success);
        int materials = session.ProgressionView.Materials;
        Assert.Equal(0, session.PreviewBuildLoadout("loadout.1").MaterialCost);
        Assert.True(session.ApplyBuildLoadout("loadout.1").Success); Assert.Equal(materials, session.ProgressionView.Materials);
    }

    [Fact]
    public void WrongDisciplineDoesNotRetrainOrSpend()
    {
        var session = Restore(Earned.Value); Assert.True(session.SaveBuildLoadout("loadout.1", "Vanguard Oath").Success);
        // A structurally valid saved build from another unlocked discipline may remain after retraining.
        var state = session.Capture(); var original = state.Progression.Character.BuildLoadouts![0];
        state.Progression.Character.BuildLoadouts = [original with { Discipline = "Arcanist", Mutations = [] }];
        session = Restore(state); string before = session.StateHash;
        Assert.Contains("requires Arcanist", session.PreviewBuildLoadout("loadout.1").Reason);
        Assert.False(session.ApplyBuildLoadout("loadout.1").Success); Assert.Equal(before, session.StateHash);
    }

    [Theory]
    [InlineData(ProductionAction.SaveBuildLoadout)]
    [InlineData(ProductionAction.RenameBuildLoadout)]
    [InlineData(ProductionAction.DeleteBuildLoadout)]
    [InlineData(ProductionAction.ApplyBuildLoadout)]
    public void AllManagementRequiresMaraProximity(ProductionAction action)
    {
        var session = Fresh(); Assert.True(session.SaveBuildLoadout("loadout.1", "Oath").Success);
        Visit(session, "service.torren"); string before = session.StateHash;
        Assert.False(session.Execute(new(action, Id: "loadout.1", Value: "Other")).Success); Assert.Equal(before, session.StateHash);
        Assert.Contains("Mara", session.PreviewBuildLoadout("loadout.1").Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bad\nName")]
    [InlineData("Invisible\u200B")]
    [InlineData("123456789012345678901234567890123")]
    public void InvalidNamesDoNotChangeState(string name)
    {
        var session = Fresh(); string before = session.StateHash;
        Assert.False(session.SaveBuildLoadout("loadout.1", name).Success); Assert.Equal(before, session.StateHash);
    }

    [Fact]
    public void EightSlotsRenameOverwriteDeleteAndDetachedViewsRoundTrip()
    {
        var session = Fresh();
        for (int i = 1; i <= 8; i++) Assert.True(session.SaveBuildLoadout("loadout." + i, "Build " + i).Success);
        var views = session.BuildLoadouts; string before = session.StateHash;
        Assert.False(session.SaveBuildLoadout("loadout.9", "Overflow").Success);
        Assert.False(session.RenameBuildLoadout("loadout.1", "build 2").Success); Assert.Equal(before, session.StateHash);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)views[0].Fragments).Clear());
        Assert.True(session.RenameBuildLoadout("loadout.1", "New Oath").Success); Assert.Equal("Build 1", views[0].Name);
        Assert.True(session.InstallFragment("Spine", "fragment.nerve_ilyra").Success);
        Assert.True(session.SaveBuildLoadout("loadout.1", "New Oath").Success); Assert.Contains("Spine", session.BuildLoadouts[0].Fragments.Keys);
        var loaded = ProductionSaveStore.Read(CombatJson, Adventure, Policy, JsonData.Write(new ProductionSave(1, session.StateHash, session.Capture())));
        Assert.Equal(session.StateHash, loaded.StateHash);
        for (int i = 1; i <= 8; i++) Assert.True(session.DeleteBuildLoadout("loadout." + i).Success);
        Assert.Null(session.Capture().Progression.Character.BuildLoadouts);
    }

    [Fact]
    public void LegacySnapshotsOmitLoadoutsAndPreserveHashes()
    {
        var session = Fresh(); string json = JsonData.Write(session.Capture());
        Assert.DoesNotContain("buildLoadouts", json); Assert.Equal(session.StateHash, Restore(JsonData.Read<ProductionSnapshot>(json)).StateHash);
    }

    [Theory]
    [InlineData("fragment-slot")]
    [InlineData("manifestation")]
    [InlineData("passive")]
    [InlineData("mutation")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("null-collections")]
    [InlineData("null-row")]
    public void MalformedSavedChoicesAreRejected(string kind)
    {
        var session = Fresh(); Assert.True(session.SaveBuildLoadout("loadout.1", "First").Success);
        var state = session.Capture(); var build = state.Progression.Character.BuildLoadouts![0];
        switch (kind)
        {
            case "fragment-slot": build.Fragments["Heart"] = "fragment.nerve_ilyra"; break;
            case "manifestation": build.Manifestations[40] = "manifestation.unknown"; break;
            case "passive": build.Passives["Offense"] = 100; break;
            case "mutation": build.Mutations["skill.unknown"] = "mutation.unknown"; break;
            case "duplicate": state.Progression.Character.BuildLoadouts = [build, build]; break;
            case "empty": state.Progression.Character.BuildLoadouts = []; break;
            case "null-collections": state.Progression.Character.BuildLoadouts = [build with { Fragments = null! }]; break;
            case "null-row": state.Progression.Character.BuildLoadouts = [null!]; break;
        }
        Assert.Throws<InvalidDataException>(() => Restore(state));
    }

    [Fact]
    public void ManifestationThresholdMustBeReachedBeforeChangingItsChoice()
    {
        var session = Fresh(); Assert.True(session.SaveBuildLoadout("loadout.1", "Stone Choir").Success);
        var state = session.Capture(); var manifestation = Adventure.Capture().Manifestations.First();
        state.Progression.Character.BuildLoadouts![0].Manifestations[manifestation.Threshold] = manifestation.Id;
        session = Restore(state); string before = session.StateHash;
        Assert.Contains("resonance", session.PreviewBuildLoadout("loadout.1").Reason);
        Assert.False(session.ApplyBuildLoadout("loadout.1").Success); Assert.Equal(before, session.StateHash);
    }

    [Fact]
    public void ApplyDiscoversConcordanceThroughOrdinaryAnatomyRules()
    {
        var session = Restore(Earned.Value);
        Assert.True(session.InstallFragment("Heart", "fragment.heart_serath").Success);
        Assert.True(session.SaveBuildLoadout("loadout.1", "Funeral Flame").Success);
        Assert.True(session.InstallFragment("Heart", null).Success);
        // Imported historical choices may precede a concordance discovery; application must discover it.
        var state = session.Capture(); state.Expedition.Adventure.Concordances.Clear(); session = Restore(state);
        var result = session.ApplyBuildLoadout("loadout.1"); Assert.True(result.Success, result.Reason);
        Assert.Contains("concordance.funeral_flame", session.Capture().Expedition.Adventure.Concordances);
        Assert.Contains("ConcordanceDiscovered:concordance.funeral_flame", result.WorldEvents);
    }

    [Fact]
    public void StaleFragmentAndUnmasteredMutationExplainRequirementsWithoutPartialApply()
    {
        var session = Fresh(); Assert.True(session.InstallFragment("Spine", "fragment.nerve_ilyra").Success);
        Assert.True(session.SaveBuildLoadout("loadout.1", "Unlearned Storm").Success);
        Assert.True(session.InstallFragment("Spine", null).Success);
        var state = session.Capture();
        state.Progression.Character.OwnedFragments.Remove("fragment.nerve_ilyra"); state.Expedition.Adventure.OwnedFragments.Remove("fragment.nerve_ilyra");
        var skill = session.Content.Capture().Skills.First(s => s.Discipline == session.ProgressionView.Discipline && s.Mutations.Length > 0);
        state.Progression.Character.BuildLoadouts![0].Mutations[skill.Id] = skill.Mutations[0];
        session = Restore(state); string before = session.StateHash; var preview = session.PreviewBuildLoadout("loadout.1");
        Assert.False(preview.Success); Assert.Contains("no longer owned", preview.Reason); Assert.Contains("Master", preview.Reason);
        Assert.False(session.ApplyBuildLoadout("loadout.1").Success); Assert.Equal(before, session.StateHash);
    }

    [Fact]
    public void SavedAllocationCannotSpendUnearnedPoints()
    {
        var session = Fresh(); Assert.True(session.SaveBuildLoadout("loadout.1", "Distant Strength").Success);
        var state = session.Capture(); state.Progression.Character.BuildLoadouts![0].Passives["Offense"] = 1;
        session = Restore(state); string before = session.StateHash;
        Assert.Contains("Not enough passive points", session.PreviewBuildLoadout("loadout.1").Reason);
        Assert.False(session.ApplyBuildLoadout("loadout.1").Success); Assert.Equal(before, session.StateHash);
    }

    [Fact]
    public void CampaignTorrenGateAndEndgameArchiveReplayUseBuildCommands()
    {
        var campaign = CampaignContent.Parse(Read("campaign.json")); var endgame = EndgameContent.Parse(Read("endgame.json"));
        string combat = EndgameCombatContent.Parse(CampaignCombatContent.Parse(CombatJson, Read("campaign-combat.json")).CombatJson,
            Read("endgame-combat.json"), endgame).CombatJson;
        var state = EndgameRuntimeSession.Create(combat, Adventure, Policy, campaign, endgame).Capture();
        foreach (var body in new[] { state.Campaign.Combat, state.Campaign.Production.Expedition.Combat }) body.Actors.Single(a => a.Id == 1).Position = new(-4500, -1800);
        var session = EndgameRuntimeSession.Restore(combat, Adventure, Policy, campaign, endgame, state);
        Assert.True(session.ExecuteProduction(new(ProductionAction.SaveBuildLoadout, Id: "loadout.1", Value: "New Dawn")).Success);
        // A full build with unchanged equipment remains useful before Torren is rescued.
        Assert.True(session.PreviewBuildLoadout("loadout.1").Success);
        Assert.True(session.ExecuteProduction(new(ProductionAction.ApplyBuildLoadout, Id: "loadout.1")).Success);
        state = session.Capture(); state.Campaign.Production.Progression.Character.BuildLoadouts![0].Equipment.Clear();
        session = EndgameRuntimeSession.Restore(combat, Adventure, Policy, campaign, endgame, state);
        string before = session.StateHash; Assert.Contains("Torren", session.PreviewBuildLoadout("loadout.1").Reason);
        Assert.False(session.ExecuteProduction(new(ProductionAction.ApplyBuildLoadout, Id: "loadout.1")).Success); Assert.Equal(before, session.StateHash);
        Assert.True(session.ExecuteProduction(new(ProductionAction.SaveBuildLoadout, Id: "loadout.1", Value: "New Dawn")).Success);
        Assert.True(session.ExecuteProduction(new(ProductionAction.RenameBuildLoadout, Id: "loadout.1", Value: "Dawn Guard")).Success);
        var loaded = EndgameRuntimeSaveStore.Read(combat, Adventure, Policy, campaign, endgame, JsonData.Write(new EndgameRuntimeSave(1, session.StateHash, session.Capture())));
        Assert.Equal(session.StateHash, loaded.StateHash);
        var replay = EndgameRuntimeReplayRunner.Run(combat, Adventure, Policy, campaign, endgame, session.CaptureReplay()); Assert.True(replay.Success, replay.Detail);
    }
}
