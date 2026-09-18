using Ashenwake.Core.Adventure;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Xunit;

namespace Ashenwake.Tests;

public sealed class ProductionTests
{
    private static string CombatJson => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "combat.json"));
    private static AdventureContent Adventure => AdventureContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "adventure.json")));
    private static ProgressionContent Policy => ProgressionContent.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "progression.json")));
    private static ProductionSession Fresh(string discipline = "Vanguard") => ProductionSession.Create(CombatJson, Adventure, Policy, discipline: discipline);
    private static ProductionSession Restore(ProductionSnapshot snapshot) => ProductionSession.Restore(CombatJson, Adventure, Policy, snapshot);
    private static void Complete(ProductionSession session)
    {
        for (int i = 0; i < ProductionSmoke.MaximumCommands && !ProductionSmoke.Complete(session); i++)
        {
            var result = session.Execute(ProductionSmoke.Next(session)); Assert.True(result.Success, result.Reason);
        }
        Assert.True(ProductionSmoke.Complete(session));
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
        Assert.Fail("Could not reach specialist.");
    }

    [Fact]
    public void FreshDisciplinesUseActualCombatAndRejectBypassingPermanentOwnership()
    {
        foreach (string discipline in CombatSession.Disciplines)
        {
            var session = Fresh(discipline);
            Assert.Equal(discipline, session.Combat.View.Discipline); Assert.Equal(6, session.Combat.View.Skills.Count);
            Assert.Single(session.Combat.View.Skills, s => !s.Available);
            Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
            var command = new ProductionCommand(ProductionAction.Expedition, new(ExpeditionAction.Tick, Commands: [new(CombatCommandKind.EquipFragment, ContentId: "fragment.heart_serath")]));
            string before = session.StateHash; Assert.False(session.Execute(command).Success); Assert.Equal(before, session.StateHash);
            Assert.False(session.Retrain("Arcanist").Success);
        }
    }

    [Fact]
    public void ActualBossRewardsAtCurrencyCapCommitXpReceiptsProfileAndReplay()
    {
        var snapshot = Fresh().Capture(); snapshot.Progression.Character.Materials = 1000000; snapshot.Expedition.Adventure.Materials = 1000000;
        var session = Restore(snapshot); Complete(session);
        Assert.Equal(800, session.ProgressionView.Experience); Assert.Equal(1000000, session.ProgressionView.Materials);
        Assert.Equal(session.ProgressionView.Materials, session.View.Materials); Assert.Equal(1, session.View.Victories);
        Assert.Equal(5, session.Capture().Progression.Character.OperationReceipts.Keys.Count(id => id.StartsWith("encounter.", StringComparison.Ordinal)));
        Assert.Contains("profile.memory_cartography", session.Capture().Progression.Profile.Unlocks);
        Assert.Contains("fragment.heart_serath", session.Capture().Progression.Character.OwnedFragments);
        Assert.True(ProductionReplayRunner.Run(CombatJson, Adventure, Policy, session.CaptureReplay()).Success);
        var restored = Restore(session.Capture()); restored.Step(); Assert.Equal(800, restored.ProgressionView.Experience);
        Assert.Equal(0, session.Capture().Expedition.Adventure.Deaths);
    }

    [Theory]
    [InlineData("npc.mara", 2400, true)]
    [InlineData("npc.mara", 2401, false)]
    [InlineData("npc.mara", 2600, false)]
    [InlineData("service.mara", 2400, true)]
    [InlineData("service.mara", 2401, false)]
    [InlineData("service.mara", 2600, false)]
    public void MaraInteractionPromptMatchesTheAuthoritativeBoundary(string action, int distance, bool allowed)
    {
        var session = Fresh(); var interaction = session.Interactions.Single(i => i.ActionId == action);
        var snapshot = session.Capture();
        snapshot.Expedition.Combat.Actors.Single(a => a.Id == 1).Position = new(interaction.Position.X + distance, interaction.Position.Z);
        session = Restore(snapshot);
        Assert.Equal(allowed, distance <= session.Interactions.Single(i => i.ActionId == action).Range);
        string before = session.StateHash; var result = session.Interact(action);
        Assert.Equal(allowed, result.Success);
        if (allowed) Assert.Contains(action == "npc.mara" ? "Dialogue:mara.false_history" : "ServiceOpened:service.mara", result.WorldEvents);
        else Assert.Equal(before, session.StateHash);
        Assert.True(ProductionReplayRunner.Run(CombatJson, Adventure, Policy, session.CaptureReplay()).Success);
    }

    [Theory]
    [InlineData(2600, true)]
    [InlineData(2601, false)]
    public void MaraPermanentCommandsKeepTheirExistingReachForReplayCompatibility(int distance, bool allowed)
    {
        var snapshot = Fresh().Capture(); snapshot.Progression.Character.Experience = 4500;
        snapshot.Expedition.Combat.ProgressionBuild = snapshot.Expedition.Combat.ProgressionBuild with { Level = 10, UltimateUnlocked = true };
        snapshot.Expedition.Combat.Actors.Single(a => a.Id == 1).Position = new(-4500 + distance, -1800);
        var session = Restore(snapshot);
        string before = session.StateHash; var result = session.AllocatePassive("Offense");
        Assert.Equal(allowed, result.Success);
        if (allowed) Assert.Equal(1, session.Combat.ProgressionBuild.Offense);
        else Assert.Equal(before, session.StateHash);
        Assert.True(ProductionReplayRunner.Run(CombatJson, Adventure, Policy, session.CaptureReplay()).Success);
    }

    [Fact]
    public void SpecialistServicesApplyToTheLiveBuildAndCraftRetriesCannotDoubleSpend()
    {
        var session = Fresh(); Assert.False(session.Craft(new("early", CraftingService.Tempering, 1, AffixId: "affix.damage")).Success);
        Complete(session);
        foreach (string id in new[] { "npc.torren", "npc.cael", "npc.oris", "npc.kesh", "hub.workshops" })
        { Visit(session, id); var result = session.Interact(id); Assert.True(result.Success, result.Reason); }
        Assert.Equal(6, session.ProgressionView.Services.Length); Assert.Equal(3, session.ProgressionView.HubStage);
        long ash = session.Capture().Progression.Character.Items.First(i => i.DefinitionId == "item.ashcleaver").Id;
        Visit(session, "service.torren"); Assert.True(session.Equip(ash, EquipmentSlot.MainHand).Success);
        var temper = new CraftingRequest("test.temper", CraftingService.Tempering, ash, AffixId: "affix.damage");
        Assert.True(session.Craft(temper).Success); Assert.Equal(2, session.Combat.ProgressionBuild.FlatDamage);
        int materials = session.ProgressionView.Materials;
        Assert.True(session.Craft(temper).Success); Assert.Equal(materials, session.ProgressionView.Materials);
        Assert.False(session.Craft(temper with { ItemId = ash + 1 }).Success); Assert.Equal(materials, session.ProgressionView.Materials);
        Visit(session, "npc.oris"); Assert.True(session.Craft(new("test.rebind", CraftingService.Rebinding, ash, AffixId: "affix.damage", ReplacementId: "affix.resource")).Success);
        Assert.Equal(0, session.Combat.ProgressionBuild.FlatDamage); Assert.Equal(1, session.Combat.ProgressionBuild.ResourceBonus);
        Visit(session, "hub.workshops"); Assert.True(session.Craft(new("test.engrave", CraftingService.Engraving, ash, PropertyId: "rune.guard")).Success);
        Assert.True(session.Combat.ProgressionBuild.BarrierOnDodge);
        Visit(session, "npc.cael"); Assert.True(session.Craft(new("test.purify", CraftingService.Purification, FragmentId: "fragment.eye_vael")).Success);
        Assert.Contains("fragment.eye_vael", session.Combat.ProgressionBuild.PurifiedFragments!);
        var ringReward = session.Capture().Progression.Character.Items.Last(i => i.DefinitionId == "item.echo_ring");
        Assert.Equal(CombatContent.Parse(CombatJson).Items.Single(i => i.Id == "item.echo_ring").CriticalBasisPoints, ringReward.BaseCriticalBasisPoints);
        long ring = ringReward.Id;
        Visit(session, "npc.kesh"); Assert.True(session.Craft(new("test.extract", CraftingService.Extraction, ring, ConfirmPermanent: true)).Success);
        Assert.DoesNotContain(session.Combat.View.Inventory, i => i.Id == ring);
        Assert.Contains("property.summon_burst", session.Capture().Progression.Character.PropertyLibrary);
        // A boundary fixture supplies the thousand already-earned kills; the live service must project its permanent decision.
        var awakened = session.Capture(); awakened.Progression.Character.Items.Single(i => i.Id == ash).BurningKills = 1000;
        awakened.Expedition.Adventure.Godwrought.Single(i => awakened.Expedition.GodwroughtItems[i.InstanceId] == ash).BurningKills = 1000;
        awakened.Expedition.Combat.Build = awakened.Expedition.Combat.Build with { AshcleaverAwakened = true }; session = Restore(awakened);
        Visit(session, "service.mara"); Assert.True(session.Craft(new("test.graft", CraftingService.DivineGrafting, ash, Lineage: "Orrun", ConfirmPermanent: true)).Success);
        Assert.Equal("Orrun", session.Combat.Build.AshcleaverEvolution);
        Assert.False(session.Craft(new("test.regraft", CraftingService.DivineGrafting, ash, Lineage: "Serath", ConfirmPermanent: true)).Success);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }

    [Fact]
    public void PermanentMasteryPassivesAndRetrainingProjectWithoutLosingEarnedMastery()
    {
        var snapshot = Fresh().Capture(); snapshot.Progression.Character.Experience = 4500;
        snapshot.Progression.Character.Mastery["skill.cleave"] = 100;
        var mutations = CombatContent.Parse(CombatJson).Mutations.Where(m => m.SkillId == "skill.cleave").Select(m => m.Id).ToArray();
        snapshot.Expedition.Combat.ProgressionBuild = snapshot.Expedition.Combat.ProgressionBuild with { Level = 10, UltimateUnlocked = true, UnlockedMutations = mutations };
        var session = Restore(snapshot); Assert.True(session.AllocatePassive("Offense").Success); Assert.Equal(1, session.Combat.ProgressionBuild.Offense);
        Assert.True(session.SetMutation("skill.cleave", mutations[0]).Success); Assert.Equal(mutations[0], session.Combat.Capture().Mutations["skill.cleave"]);
        Assert.True(session.Retrain("Arcanist").Success); Assert.Equal("Instability", session.Combat.View.ResourceName);
        Assert.Empty(session.Combat.Capture().Mutations); Assert.Equal(100, session.Capture().Progression.Character.Mastery["skill.cleave"]);
        Assert.True(session.Respec().Success); Assert.Equal(0, session.Combat.ProgressionBuild.Offense);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }

    [Fact]
    public void RestoreRejectsConflictingCanonicalItemsCurrencyOrGodwroughtHistory()
    {
        var session = Fresh(); var currency = session.Capture(); currency.Expedition.Adventure.Materials++;
        Assert.Throws<InvalidDataException>(() => Restore(currency));
        var item = session.Capture(); item.Progression.Character.Items[0] = item.Progression.Character.Items[0] with { BaseDamage = item.Progression.Character.Items[0].BaseDamage + 1 };
        Assert.Throws<InvalidDataException>(() => Restore(item));
        var god = session.Capture(); god.Progression.Character.Items.Single(i => i.DefinitionId == "item.ashcleaver").BurningKills++;
        Assert.Throws<InvalidDataException>(() => Restore(god));
    }

    [Fact]
    public void LocalProfileMergesValidatedMetadataAcrossCharactersWithoutImportingPower()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ashenwake-profile-" + Guid.NewGuid().ToString("N")), path = Path.Combine(folder, "profile.json");
        try
        {
            var first = Fresh(); var profile = first.Capture().Progression.Profile;
            profile.Unlocks.Add("profile.memory_cartography"); LocalProfileStore.Merge(path, first.Content, profile);
            var second = new LocalProfileState { Discoveries = ["discovery.false_history"] };
            var merged = LocalProfileStore.Merge(path, first.Content, second);
            Assert.Contains("discovery.greyhaven", merged.Discoveries); Assert.Contains("discovery.false_history", merged.Discoveries);
            var next = ProductionSession.Create(CombatJson, Adventure, Policy, discipline: "Warden", profile: LocalProfileStore.Load(path, first.Content).Profile);
            Assert.Equal(1, next.ProgressionView.Level); Assert.Empty(next.Capture().Progression.Character.Mastery);
            Assert.Contains("profile.memory_cartography", next.ProgressionView.ProfileUnlocks);
            string unchanged = File.ReadAllText(path);
            Assert.Throws<InvalidDataException>(() => LocalProfileStore.Merge(path, first.Content, new() { ProfileId = "someone-else" }));
            Assert.Throws<InvalidDataException>(() => LocalProfileStore.Merge(path, first.Content, new() { Discoveries = ["discovery.unknown"] }));
            Assert.Equal(unchanged, File.ReadAllText(path));
            File.WriteAllText(path, "{truncated"); Assert.True(LocalProfileStore.Load(path, first.Content).RecoveredBackup);
            File.WriteAllText(path, "{\"schemaVersion\":99}");
            Assert.Throws<SaveCompatibilityException>(() => LocalProfileStore.Merge(path, first.Content, profile));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"invalid\"")]
    [InlineData("{\"schemaVersion\":null}")]
    [InlineData("{\"schemaVersion\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\"}")]
    public void ProfileBackupRecoversMalformedRootAndSchemaTypes(string damaged)
    {
        string folder = Path.Combine(Path.GetTempPath(), "ashenwake-profile-recovery-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "profile.json");
        try
        {
            var session = Fresh(); var profile = session.Capture().Progression.Profile;
            profile.Unlocks.Add("profile.memory_cartography");
            LocalProfileStore.Merge(path, session.Content, profile);
            LocalProfileStore.Merge(path, session.Content, profile);
            File.WriteAllText(path, damaged);

            var restored = LocalProfileStore.Load(path, session.Content);

            Assert.True(restored.RecoveredBackup);
            Assert.Equal(JsonData.Hash(profile), JsonData.Hash(restored.Profile));
            Assert.Equal(damaged, File.ReadAllText(path));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
