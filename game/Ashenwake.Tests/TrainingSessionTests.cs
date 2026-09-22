using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Training;
using Xunit;

namespace Ashenwake.Tests;

public sealed class TrainingSessionTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static readonly Lazy<string> Json = new(() => EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson,
        Read("endgame-combat.json"), EndgameContent.Parse(Read("endgame.json"))).CombatJson);
    private static EndgameRuntimeSession Fresh(string discipline = "Vanguard") => EndgameRuntimeSession.Create(Json.Value,
        AdventureContent.Parse(Read("adventure.json")), ProgressionContent.Parse(Read("progression.json")), CampaignContent.Parse(Read("campaign.json")),
        EndgameContent.Parse(Read("endgame.json")), discipline: discipline);
    private static EndgameRuntimeSession AtGround(string discipline = "Vanguard")
    {
        var source = Fresh(discipline);
        for (int i = 0; i < 18; i++) Assert.True(source.Step(new CombatCommand(CombatCommandKind.Move, Z: 1)).Success);
        source.Step(new CombatCommand(CombatCommandKind.Stop)); return source;
    }
    private static CombatCommand Attack(TrainingSession session)
    {
        var target = session.Combat.View.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        return new(CombatCommandKind.Cast, SkillId: session.Combat.View.Skills.First(s => s.Available && s.Cost == 0).Id,
            TargetId: target.Id, X: target.Position.X, Z: target.Position.Z);
    }
    [Fact]
    public void EntryRequiresGreyhavenProximityAndRejectsInvalidMode()
    {
        var source = Fresh(); string hash = source.StateHash;
        Assert.Throws<InvalidOperationException>(() => source.CreateTrainingSession()); Assert.Equal(hash, source.StateHash);
        var near = AtGround(); Assert.Throws<ArgumentOutOfRangeException>(() => near.CreateTrainingSession((TrainingTargetMode)9));
    }
    [Theory]
    [InlineData("Vanguard")]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void CopiesEarnedBuildAndNeverChangesJourneyOrProfile(string discipline)
    {
        var source = AtGround(discipline); string before = source.StateHash;
        var original = source.Combat.Capture(); var training = source.CreateTrainingSession(TrainingTargetMode.Group);
        var projected = training.Combat.Capture();
        Assert.Equal(JsonData.Hash(original.ProgressionBuild), JsonData.Hash(projected.ProgressionBuild));
        Assert.Equal(original.Inventory, projected.Inventory); Assert.Equal(original.Equipment, projected.Equipment);
        Assert.Equal(original.Fragments, projected.Fragments); Assert.Equal(original.Mutations, projected.Mutations);
        Assert.Equal(original.Build, projected.Build);
        for (int i = 0; i < 110; i++) training.Step(Attack(training));
        training.Reset(TrainingTargetMode.Single); training.Close();
        Assert.Equal(before, source.StateHash); Assert.Empty(training.Step());
        Assert.Throws<InvalidOperationException>(() => training.Reset(TrainingTargetMode.Single));
    }
    [Fact]
    public void RealDamageRowsReconcileEventsAndResourceConservationAndReplay()
    {
        var source = AtGround(); string hash = source.StateHash; var training = source.CreateTrainingSession();
        long damage = 0;
        for (int i = 0; i < 300; i++)
        {
            var events = training.Step(Attack(training));
            damage += events.Where(e => e.Kind == "DamageApplied" && e.TargetId != 1).Sum(e => e.Amount);
            Assert.DoesNotContain(events, e => e.Kind == "LootDropped");
        }
        Assert.True(damage > 0); Assert.Equal(damage, training.Report.TotalDamage);
        Assert.Equal(damage, training.Report.Damage.Sum(r => r.Damage));
        Assert.Equal(training.Report.CurrentResource, training.Report.ResourceIncreased - training.Report.ResourceDecreased);
        Assert.Equal(10, training.Report.ElapsedSeconds); Assert.Equal(damage / 10d, training.Report.DamagePerSecond);
        Assert.Contains(training.Report.Damage, row => row.Category == "Ability" && row.Family == DamageFamily.PhysicalSlash);
        Assert.True(TrainingSession.VerifyReplay(Json.Value, training.CaptureReplay()));
        Assert.Equal(hash, source.StateHash);
    }
    [Fact]
    public void PermanentCommandsAndNonPlayerCommandsAreRejectedWithoutBuildMutation()
    {
        var training = AtGround().CreateTrainingSession(); var original = training.Combat.Capture();
        foreach (var command in new[] { new CombatCommand(CombatCommandKind.Equip, ItemId: original.Inventory[0].Id),
            new(CombatCommandKind.EquipFragment, ContentId: "fragment.eye_vael"), new(CombatCommandKind.UnequipFragment, ContentId: "fragment.eye_vael"),
            new(CombatCommandKind.SetMutation, SkillId: "skill.cleave", ContentId: "mutation.cinder_arc"), new(CombatCommandKind.Pickup, ItemId: 5),
            new(CombatCommandKind.Cast, ActorId: 2, SkillId: "skill.cleave") })
            Assert.Contains(training.Step(command), e => e.Kind == "CommandRejected" && e.ContentId == "training_build_is_read_only");
        var after = training.Combat.Capture(); Assert.Equal(original.Equipment, after.Equipment); Assert.Equal(original.Fragments, after.Fragments);
        Assert.Equal(original.Mutations, after.Mutations); Assert.Empty(after.Loot);
    }
    [Fact]
    public void PracticeTargetsRemainStationaryAndResetReproduciblyWithoutCountingWallTime()
    {
        var training = AtGround().CreateTrainingSession(TrainingTargetMode.Group); var initial = training.Combat.Capture();
        string reportHash = JsonData.Hash(training.Report);
        for (int i = 0; i < 10; i++) Assert.Equal(reportHash, JsonData.Hash(training.Report));
        for (int i = 0; i < 180; i++) training.Step();
        Assert.Equal(initial.Actors.Select(a => a.Position), training.Combat.Capture().Actors.Select(a => a.Position));
        Assert.Equal(initial.Actors[0].Health, training.Combat.View.Actors.Single(a => a.Id == 1).Health);
        training.Reset(TrainingTargetMode.Group); Assert.Equal(JsonData.Hash(initial), training.Combat.StateHash);
        Assert.Equal(0, training.Report.TotalDamage); Assert.Equal(0, training.Report.ElapsedTicks);
    }
    [Fact]
    public void GravecallerUsesPracticeCorpsesThroughRealCommandsAndRecordsActualResource()
    {
        var training = AtGround("Gravecaller").CreateTrainingSession();
        var corpse = training.Combat.View.Actors.First(a => a.Health == 0);
        Assert.Contains(training.Step(new CombatCommand(CombatCommandKind.ConsumeCorpse, TargetId: corpse.Id)), e => e.Kind == "CorpseConsumed");
        Assert.True(training.Report.ResourceIncreased > 0);
        Assert.Equal(training.Report.CurrentResource, training.Report.ResourceIncreased - training.Report.ResourceDecreased);
        Assert.Equal(0, training.Report.TargetsDefeated);
        Assert.True(TrainingSession.VerifyReplay(Json.Value, training.CaptureReplay()));
    }
    [Fact]
    public void TrainingEndsAtBoundAndRejectsTamperedReplay()
    {
        var training = AtGround().CreateTrainingSession();
        for (int i = 0; i < TrainingSession.MaximumTicks + 1; i++) training.Step();
        Assert.True(training.IsComplete); Assert.Equal(TrainingSession.MaximumTicks, training.Report.ElapsedTicks);
        var replay = training.CaptureReplay(); replay.Frames[0] = replay.Frames[0] with { ReportHash = "bad" };
        Assert.False(TrainingSession.VerifyReplay(Json.Value, replay));
    }
    // Detached fixtures exercise effect attribution; earned-only admission is covered above for all disciplines.
    private static TrainingSession EffectFixture(CombatProgressionBuild build)
    {
        var body = CombatSession.CreateEncounter(Json.Value, 42, "hub"); body.ApplyProgressionBuild(build);
        return (TrainingSession)Activator.CreateInstance(typeof(TrainingSession),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
            new object[] { Json.Value, body.Capture(), TrainingTargetMode.Single }, null)!;
    }
    private static CombatCommand CastAtTarget(TrainingSession training, string skill)
    {
        var target = training.Combat.View.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        return new(CombatCommandKind.Cast, SkillId: skill, TargetId: target.Id, X: target.Position.X, Z: target.Position.Z);
    }
    [Fact]
    public void FragmentBurningIsAttributedToFragmentAndActualFireFamily()
    {
        var training = EffectFixture(new(CriticalBasisPoints: 7500));
        for (int i = 0; i < 300 && !training.IsComplete; i++) training.Step(CastAtTarget(training, "skill.cleave"));
        Assert.Contains(training.Report.Damage, row => row.Category == "Fragment" && row.SourceId == "fragment.eye_vael" && row.Family == DamageFamily.Fire && row.Damage > 0);
        Assert.Contains(training.Report.Triggers, row => row.Kind == "FragmentTriggered" && row.SourceId == "fragment.eye_vael");
        Assert.True(TrainingSession.VerifyReplay(Json.Value, training.CaptureReplay()));
    }
    [Fact]
    public void WidowEchoDamageAndHeatVentCostsAreMeasuredFromActualEffects()
    {
        var training = EffectFixture(new("Arcanist") { WidowEcho = true, CinderCycle = true });
        training.Step(new CombatCommand(CombatCommandKind.Dodge, X: 1));
        for (int i = 0; i < 90; i++) training.Step(CastAtTarget(training, "skill.fire_lance"));
        for (int i = 0; i < 60; i++) training.Step(CastAtTarget(training, "skill.vent"));
        Assert.Contains(training.Report.Damage, row => row.Category == "Legendary" && row.SourceId == "effect.widow_echo" && row.Family == DamageFamily.Void);
        Assert.Contains(training.Report.Resources, row => row.Reason == "Skill heat" && row.Increased > 0);
        Assert.Contains(training.Report.Resources, row => row.Reason == "Vent" && row.Decreased > 0);
        Assert.Equal(training.Report.CurrentResource, training.Report.ResourceIncreased - training.Report.ResourceDecreased);
        Assert.True(TrainingSession.VerifyReplay(Json.Value, training.CaptureReplay()));
    }
    [Fact]
    public void EffectiveOverkillIsCappedAndDeathsCreateNoLoot()
    {
        var training = AtGround().CreateTrainingSession();
        var state = (CombatSnapshot)typeof(CombatSession).GetField("_state", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(training.Combat)!;
        state.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0).Health = 1;
        var events = new List<CombatEvent>();
        for (int i = 0; i < 20 && !training.IsComplete; i++) events.AddRange(training.Step(CastAtTarget(training, "skill.cleave")));
        Assert.True(training.IsComplete); Assert.Equal(1, training.Report.TargetsDefeated);
        Assert.Equal(1, training.Report.TotalDamage); Assert.Empty(training.Combat.View.Loot);
        Assert.Contains(events, e => e.Kind == "EntityKilled"); Assert.DoesNotContain(events, e => e.Kind == "LootDropped");
    }
    [Fact]
    public void OrdinarySummonHitsKeepTheirOwnCategory()
    {
        var training = EffectFixture(new("Warden"));
        for (int i = 0; i < 90; i++) training.Step(CastAtTarget(training, "skill.thorn_shot"));
        training.Step(CastAtTarget(training, "skill.feral_companion"));
        for (int i = 0; i < 150; i++) training.Step();
        Assert.Contains(training.Report.Damage, row => row.Category == "Summon" && row.SourceId == "summon.companion_bite" && row.Damage > 0);
        Assert.True(TrainingSession.VerifyReplay(Json.Value, training.CaptureReplay()));
    }

}
