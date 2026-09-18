using System.Text.Json.Nodes;
using Ashenwake.Core.Adventure;
using Ashenwake.Core.Campaign;
using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Endgame;
using Ashenwake.Core.Expedition;
using Ashenwake.Core.Experiments;
using Ashenwake.Core.Production;
using Ashenwake.Core.Progression;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class ExperimentTests
{
    private static string Read(string path) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, path));
    private static AdventureContent Adventure => AdventureContent.Parse(Read("adventure.json"));
    private static ProgressionContent Policy => ProgressionContent.Parse(Read("progression.json"));
    private static CampaignContent Campaign => CampaignContent.Parse(Read("campaign.json"));
    private static EndgameContent Endgame => EndgameContent.Parse(Read("endgame.json"));
    private static ExperimentContent Content => ExperimentContent.Default();
    private static readonly Lazy<string> Previous = new(() => CampaignCombatContent.Parse(Read("fixtures/combat-phase4.json"), Read("fixtures/campaign-combat-phase4.json")).CombatJson);
    private static readonly Lazy<string> Composed = new(() => EndgameCombatContent.Parse(CampaignCombatContent.Parse(Read("combat.json"), Read("campaign-combat.json")).CombatJson, Read("endgame-combat.json"), Endgame).CombatJson);
    private static string CombatJson => Composed.Value;
    private static EndgameRuntimeSession Original() => EndgameRuntimeMigration.ImportPhaseFour(Read("fixtures/phase4-campaign-complete.json"), Previous.Value, CombatJson, Adventure, Policy, Campaign, Endgame);
    private static ExperimentRuntimeSession Import(ExperimentContent? content = null) => ExperimentRuntimeSession.FromEndgame(CombatJson, Adventure, Policy, Campaign, Endgame, content ?? Content, Original().Capture());
    private static ExperimentRuntimeSession Restore(ExperimentSnapshot snapshot, ExperimentContent? content = null) => ExperimentRuntimeSession.Restore(CombatJson, Adventure, Policy, Campaign, Endgame, content ?? Content, snapshot);
    private static ExperimentResult Apply(ExperimentRuntimeSession session, ExperimentCommand command)
    { var result = session.Execute(command); Assert.True(result.Success, result.Reason + " / " + command); return result; }
    private static void Until(ExperimentRuntimeSession session, Func<ExperimentRuntimeSession, bool> done, int maximum = 8000)
    {
        for (int i = 0; i < maximum && !done(session); i++) Apply(session, ExperimentRuntimeSmoke.Next(session));
        Assert.True(done(session), "Experiment route stalled: " + session.View + " / " + session.Endgame.RunView);
    }
    private static ExperimentRuntimeSession Offered()
    { var session = Import(); Until(session, s => s.View.Memory?.Status == "Offered"); return session; }
    private static ExperimentRuntimeSession Bound()
    { var session = Offered(); Until(session, s => s.View.Memory?.Status == "Bound"); return session; }
    private static void At(ExperimentRuntimeSession session, Position position)
    {
        for (int i = 0; i < 1000; i++)
        {
            var player = session.Combat.View.Actors.Single(a => a.Id == 1);
            if (Position.DistanceSquared(player.Position, position) <= 1900L * 1900) return;
            var move = CombatProductionSmoke.MovementDirection(player.Position, position, session.Endgame.Room); Apply(session, new(ExperimentAction.Tick, Commands: [new(CombatCommandKind.Move, X: move.X, Z: move.Z)]));
        }
        Assert.Fail("Interaction route did not converge.");
    }

    [Fact]
    public void AbsentExperimentPreservesOriginalStateAndOptInDoesNotImportPower()
    {
        var original = Original(); string before = original.StateHash;
        var session = ExperimentRuntimeSession.FromEndgame(CombatJson, Adventure, Policy, Campaign, Endgame, Content, original.Capture());
        Assert.Equal(before, session.Endgame.StateHash); Assert.Null(session.Combat.Capture().Experiment);
        string baselineJson = JsonData.Write(original.Capture()); Assert.DoesNotContain("\"experiment\":", baselineJson);
        Assert.Empty(session.View.Cosmetics); Assert.Equal(JsonData.Hash(original.Production.Capture()), JsonData.Hash(session.Production.Capture()));
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }
    [Fact]
    public void ActualBorrowBindCastCompleteSaveAndReplayGrantOneCosmetic()
    {
        var session = Import(); var before = session.Production.Capture().Expedition.Adventure.Anatomy;
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-experiment-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "echoes.json"); bool bound = false, cast = false, warned = false; int room = -1;
            for (int i = 0; i < 8000 && !ExperimentRuntimeSmoke.Complete(session); i++)
            {
                var outcome = Apply(session, ExperimentRuntimeSmoke.Next(session));
                bound |= outcome.WorldEvents.Contains("ExperimentMemoryBound"); cast |= outcome.CombatEvents.Any(e => e.Kind == "CapturedAbilityUsed"); warned |= outcome.CombatEvents.Any(e => e.Kind == "ExperimentStormWarning");
                int nextRoom = session.Endgame.RunView?.EncounterIndex ?? -1;
                if (nextRoom != room || i % 600 == 0)
                {
                    var replay = ExperimentReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, Endgame, Content, session.CaptureReplay()); Assert.True(replay.Success, replay.ToString());
                    ExperimentSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, Content, session.Capture());
                    var loaded = ExperimentSaveStore.Load(path, CombatJson, Adventure, Policy, Campaign, Endgame, Content); Assert.Equal(session.StateHash, loaded.Session.StateHash); session = loaded.Session; room = nextRoom;
                }
            }
            Assert.True(ExperimentRuntimeSmoke.Complete(session)); Assert.True(bound); Assert.True(cast); Assert.True(warned);
            Assert.Single(session.View.Cosmetics); Assert.Equal(before, session.Production.Capture().Expedition.Adventure.Anatomy); Assert.Null(session.Combat.Capture().Experiment);
            string hash = session.StateHash; Assert.False(session.BindMemory(1).Success); Assert.False(session.ReleaseMemory().Success); Assert.Equal(hash, session.StateHash);
            Assert.Single(Restore(session.Capture()).View.Cosmetics);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void WrongMemoryOrDistantBindingFailsWithoutChangingState()
    {
        var session = Offered(); var memory = session.View.Memory!; string before = session.StateHash;
        Assert.False(session.BindMemory(memory.SourceActorId + 1).Success); Assert.Equal(before, session.StateHash);
        if (!memory.CanBind) { Assert.False(session.BindMemory(memory.SourceActorId).Success); Assert.Equal(before, session.StateHash); }
        Until(session, s => s.View.Memory?.Status == "Bound"); Assert.Equal("skill.echo_storm", session.Combat.View.CapturedSkillId);
        string bound = session.StateHash; Assert.False(session.BindMemory(memory.SourceActorId).Success); Assert.Equal(bound, session.StateHash);
    }
    [Fact]
    public void OwnedMindIsSuppressedWithoutLosingItsFragmentOrResonanceAndReleaseRestoresIt()
    {
        var session = Import(); At(session, new(-4500, -1800));
        var install = new EndgameRuntimeCommand(EndgameRuntimeAction.Production, Production: new(ProductionAction.Expedition, new(ExpeditionAction.InstallFragment, "Mind", "fragment.last_memory")));
        Assert.True(session.ExecuteEndgame(install).Success);
        int resonance = session.Combat.View.Resonance; Until(session, s => s.View.Memory?.Status == "Offered");
        Assert.Equal("fragment.last_memory", session.View.Memory!.SuppressedMindId); Assert.Equal("fragment.last_memory", session.Combat.Capture().Fragments["Mind"]); Assert.Equal(resonance, session.Combat.View.Resonance);
        Assert.Equal("", session.Combat.View.CapturedSkillId); Assert.True(session.ReleaseMemory().Success);
        Assert.Equal("", session.View.Memory!.SuppressedMindId); Assert.Equal("fragment.last_memory", session.Production.Capture().Expedition.Adventure.Anatomy["Mind"]);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }
    [Fact]
    public void LoanExpiryDeathAndAbandonClearAbilityWithoutPermanentChanges()
    {
        var session = Bound(); var state = session.Capture(); var body = session.Production.Capture().Expedition.Adventure.Anatomy;
        // Expiry is an ordinary combat timer, not a wall-clock or seasonal deadline.
        for (int i = 0; i < 500 && session.View.Memory?.Status == "Bound"; i++) session.Step(new CombatCommand(CombatCommandKind.Stop));
        Assert.Contains(session.View.Memory!.Status, new[] { "Expired", "Lost" }); Assert.Equal("", session.Combat.View.CapturedSkillId);
        session = Restore(state); Assert.True(session.ExecuteEndgame(new(EndgameRuntimeAction.Abandon)).Success);
        Assert.True(session.InHub); Assert.Null(session.Combat.Capture().Experiment); Assert.Equal("", session.Combat.View.CapturedSkillId); Assert.Equal(body, session.Production.Capture().Expedition.Adventure.Anatomy);
        Assert.Empty(session.View.Cosmetics); Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
    }
    [Fact]
    public void RetirementPreservesActiveRunAndReplayButClosesNewBorrowing()
    {
        var session = Bound(); var retired = Content.WithAdmission("Retired"); Assert.Equal(Content.Hash, retired.Hash);
        var replay = session.CaptureReplay(); Assert.True(ExperimentReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, Endgame, retired, replay).Success);
        session = Restore(session.Capture(), retired); Until(session, ExperimentRuntimeSmoke.Complete);
        var sigil = session.Endgame.View.AvailableSigils.Single(); At(session, new(6500, 0)); string hash = session.StateHash;
        Assert.False(session.StartContract(sigil.Id).Success); Assert.Equal(hash, session.StateHash);
        Assert.True(session.StartContract(sigil.Id, ExperimentChoice.KeepMind).Success); Assert.Null(session.View.Memory); Assert.Single(session.View.Cosmetics);
    }
    [Fact]
    public void StormWarningUsesRealDamageAndCanBeEvaded()
    {
        var session = Bound(); Until(session, s => s.View.Run?.EchoActionId > 0);
        var memory = session.View.Memory!; Assert.Equal("Warning", memory.HazardStage);
        var snapshot = session.Capture(); var standing = Restore(snapshot); var escaping = Restore(snapshot);
        long action = snapshot.Run!.EchoActionId; int stood = 0, escaped = 0;
        var target = new Position(memory.HazardPosition.X - 4500, memory.HazardPosition.Z);
        if (!new SpatialWorld(escaping.Endgame.Room).CanOccupy(target, CombatSession.ActorRadius)) target = new(memory.HazardPosition.X + 4500, memory.HazardPosition.Z);
        for (int i = 0; i < 85; i++)
        {
            var a = standing.Step(new CombatCommand(CombatCommandKind.Stop)); stood += a.CombatEvents.Where(e => e.Kind == "DamageApplied" && e.TargetId == 1 && e.ContentId == "enemy.stormbound" && e.ActionId == action).Sum(e => e.Amount);
            var player = escaping.Combat.View.Actors.Single(a => a.Id == 1); var move = CombatProductionSmoke.MovementDirection(player.Position, target, escaping.Endgame.Room);
            var b = escaping.Step(new CombatCommand(CombatCommandKind.Move, X: move.X, Z: move.Z)); escaped += b.CombatEvents.Where(e => e.Kind == "DamageApplied" && e.TargetId == 1 && e.ContentId == "enemy.stormbound" && e.ActionId == action).Sum(e => e.Amount);
        }
        Assert.True(stood > 0); Assert.Equal(0, escaped);
    }
    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("rulesVersion")]
    public void FutureScopedMemoryHeadersPreserveOriginalAndDoNotRecoverBackup(string field)
    {
        var session = Bound(); string directory = Path.Combine(Path.GetTempPath(), "ashenwake-experiment-future-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "echoes.json");
        try
        {
            ExperimentSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, Content, session.Capture());
            ExperimentSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, Content, session.Capture());
            var node = JsonNode.Parse(File.ReadAllText(path))!; var memory = node["state"]!["endgame"]!["combat"]!["experiment"]!;
            memory[field] = field == "schemaVersion" ? JsonValue.Create(2) : JsonValue.Create("borrowed-memory.2"); memory["futureOnly"] = true;
            string future = node.ToJsonString(); File.WriteAllText(path, future);
            Assert.Throws<SaveCompatibilityException>(() => ExperimentSaveStore.Load(path, CombatJson, Adventure, Policy, Campaign, Endgame, Content));
            Assert.Throws<SaveCompatibilityException>(() => ExperimentSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, Content, session.Capture()));
            Assert.Equal(future, File.ReadAllText(path));
            var endgameNode = JsonNode.Parse(JsonData.Write(new EndgameRuntimeSave(1, session.Endgame.StateHash, session.Endgame.Capture())))!;
            endgameNode["state"]!["combat"]!["experiment"]![field] = memory[field]!.DeepClone(); endgameNode["state"]!["combat"]!["experiment"]!["futureOnly"] = true;
            Assert.Throws<SaveCompatibilityException>(() => EndgameRuntimeSaveStore.Read(CombatJson, Adventure, Policy, Campaign, Endgame, endgameNode.ToJsonString()));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void ChangedRulesAndPhantomCosmeticRejectWhileReservedProfilePathsRemainProtected()
    {
        var session = Import(); var changed = ExperimentContent.Create(new(1, "Available", Content.Capture() with { HazardDamage = 19 }));
        Assert.Throws<InvalidDataException>(() => Restore(session.Capture(), changed));
        var forged = session.Capture(); forged.Cosmetics.Add("cosmetic.borrowed_memory", new("cosmetic.borrowed_memory", Content.Hash, 1, 4, 5, session.Tick));
        Assert.Throws<InvalidDataException>(() => Restore(forged));
        Assert.Throws<ArgumentException>(() => ExperimentSaveStore.Write(Path.Combine(Path.GetTempPath(), "profile.json.bak"), CombatJson, Adventure, Policy, Campaign, Endgame, Content, session.Capture()));
    }
    [Fact]
    public void KeepingMindIsExactlyTheOrdinaryRunAndGivesNoExperimentReward()
    {
        var session = Import(); Until(session, s => s.InHub && s.Endgame.View.AvailableSigils.Length > 0);
        var baseline = EndgameRuntimeSession.Restore(CombatJson, Adventure, Policy, Campaign, Endgame, session.Endgame.Capture());
        long id = session.Endgame.View.AvailableSigils[0].Id;
        Assert.True(session.StartContract(id, ExperimentChoice.KeepMind).Success);
        Assert.True(baseline.StartFracture(id).Success); Assert.Equal(baseline.StateHash, session.Endgame.StateHash); Assert.Null(session.View.Run); Assert.Null(session.View.Memory);
        Until(session, s => s.InHub); Assert.Empty(session.View.Cosmetics);
        var choice = Assert.Single(Restore(session.Capture()).View.Entries); Assert.Equal(ExperimentChoice.KeepMind, choice.Choice); Assert.Equal("Completed", choice.Outcome);
    }
    [Fact]
    public void ActualDeathRetryDoesNotReissueTheLoanAndNoSecondCastOrRewardIsPossible()
    {
        var session = Import(); Until(session, s => s.View.Memory?.Status == "Pending");
        for (int i = 0; i < 5000 && !session.Endgame.AwaitingRetry; i++)
        {
            if (session.Endgame.RunView?.CanAdvance == true) { Assert.True(session.ExecuteEndgame(new(EndgameRuntimeAction.AdvanceEncounter)).Success); continue; }
            var player = session.Combat.View.Actors.Single(a => a.Id == 1);
            var enemies = session.Combat.View.Actors.Where(a => a.Faction == CombatFaction.Enemy && a.Health > 0).ToArray();
            var enemy = enemies.Where(a => a.Role != "Support").OrderBy(a => a.Role == "Ranged").ThenBy(a => Position.DistanceSquared(a.Position, player.Position)).FirstOrDefault();
            if (enemy is null) { session.Step(EndgameCombatSmoke.Commands(session.Combat.View, session.Endgame.Room)); continue; }
            var move = Position.DistanceSquared(player.Position, enemy.Position) <= 650L * 650 && new SpatialWorld(session.Endgame.Room).HasLineOfSight(player.Position, enemy.Position) ? new Position(0, 0) : CombatProductionSmoke.MovementDirection(player.Position, enemy.Position, session.Endgame.Room);
            session.Step(new CombatCommand(CombatCommandKind.Move, X: move.X, Z: move.Z));
        }
        Assert.True(session.Endgame.AwaitingRetry, string.Join(" / ", session.Combat.View.Actors.Select(a => $"{a.Id} {a.Role} {a.Position} HP{a.Health} {a.State}"))); Assert.Equal("Lost", session.View.Memory!.Status); Assert.Equal("", session.Combat.View.CapturedSkillId);
        session = Restore(session.Capture()); Assert.True(session.ExecuteEndgame(new(EndgameRuntimeAction.RetryEncounter)).Success);
        Assert.Equal("Lost", session.View.Memory!.Status); Assert.False(session.BindMemory(session.View.Memory.SourceActorId).Success);
        Assert.True(session.ExecuteEndgame(new(EndgameRuntimeAction.Abandon)).Success); Assert.Empty(session.View.Cosmetics); Assert.Null(session.Combat.Capture().Experiment);
    }
    [Fact]
    public void DeathAfterBindingCanRetryWithHistoricalSourceReceiptAndNoNewLoan()
    {
        var session = Bound(); var wounded = session.Capture(); var arena = wounded.Endgame.Combat!;
        int source = session.View.Memory!.SourceActorId;
        var attacker = arena.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Role == "Ranged" && a.Health > 0);
        var space = new SpatialWorld(session.Endgame.Room);
        var position = new[] { new Position(attacker.Position.X - 3000, attacker.Position.Z), new Position(attacker.Position.X + 3000, attacker.Position.Z), new Position(attacker.Position.X, attacker.Position.Z - 3000), new Position(attacker.Position.X, attacker.Position.Z + 3000) }
            .First(p => space.CanOccupy(p, CombatSession.ActorRadius) && space.HasLineOfSight(p, attacker.Position) && arena.Actors.Where(a => a.Id != 1 && a.Health > 0).All(a => Position.DistanceSquared(p, a.Position) > 560L * 560));
        // The memory is earned normally. A validated wounded-position fixture
        // bounds this lifecycle test; actual enemy damage must cause the death.
        var player = arena.Actors.Single(a => a.Id == 1); player.Health = 1; player.Barrier = 0; player.Position = position; player.InvulnerableUntil = arena.Tick; player.Statuses.Clear();
        session = Restore(wounded); bool killed = false;
        for (int i = 0; i < 300 && !session.Endgame.AwaitingRetry; i++)
            killed |= session.Step(new CombatCommand(CombatCommandKind.Stop)).CombatEvents.Any(e => e.Kind == "EntityKilled" && e.TargetId == 1);
        Assert.True(killed); Assert.True(session.Endgame.AwaitingRetry); Assert.Equal("Lost", session.View.Memory!.Status);
        Assert.True(session.ExecuteEndgame(new(EndgameRuntimeAction.RetryEncounter)).Success);
        Assert.Equal("Lost", session.View.Memory!.Status); Assert.Equal(source, session.View.Memory.SourceActorId);
        Assert.DoesNotContain(session.Combat.View.Actors, a => a.Id == source); Assert.Equal("", session.Combat.View.CapturedSkillId);
        Assert.False(session.BindMemory(source).Success); Assert.Empty(session.View.Cosmetics);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
        Assert.True(ExperimentReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, Endgame, Content, session.CaptureReplay()).Success);
        Assert.True(session.ExecuteEndgame(new(EndgameRuntimeAction.Abandon)).Success); Assert.Null(session.Combat.BorrowedMemory);
    }
    [Fact]
    public void A_live_bound_memory_still_requires_its_real_elite_source()
    {
        var session = Bound(); var forged = session.Capture();
        int falseSource = forged.Endgame.Combat!.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0).Id;
        forged.Endgame.Combat.Experiment!.SourceActorId = falseSource; forged.Run!.SourceActorId = falseSource;
        Assert.Throws<InvalidDataException>(() => Restore(forged));
    }
    [Fact]
    public void CastIsOneUseAndSubsequentSuccessfulContractCannotRerollCosmeticReceipt()
    {
        var session = Bound(); Until(session, s => s.View.Run?.EchoActionId > 0);
        long action = session.View.Run!.EchoActionId;
        var target = session.Combat.View.Actors.FirstOrDefault(a => a.Faction == CombatFaction.Enemy && a.Health > 0);
        if (target is not null)
        {
            var repeated = session.Step(new CombatCommand(CombatCommandKind.CastEcho, TargetId: target.Id));
            Assert.DoesNotContain(repeated.CombatEvents, e => e.Kind == "ExperimentStormWarning"); Assert.Equal(action, session.View.Run!.EchoActionId);
        }
        Until(session, ExperimentRuntimeSmoke.Complete); var receipt = session.Capture().Cosmetics.Single().Value;
        At(session, new(6500, 0)); Assert.True(session.StartContract(session.Endgame.View.AvailableSigils.Single().Id).Success);
        Until(session, s => s.View.Run?.EchoActionId > 0); Until(session, s => s.InHub);
        Assert.Equal(receipt, session.Capture().Cosmetics.Single().Value);
    }
    [Fact]
    public void CharacterFirstPublicationFailureCannotPublishExperimentProfileMetadata()
    {
        var session = Import(); var snapshot = session.Capture(); snapshot.Endgame.Campaign.Production.Progression.Profile.Unlocks.Add("profile.secret_hunt");
        string directory = Path.Combine(Path.GetTempPath(), "ashenwake-experiment-commit-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "echoes.json"); Directory.CreateDirectory(path);
        try
        {
            string profilePath = ExperimentSaveStore.ProfilePath(path); var profile = session.Production.Capture().Progression.Profile; profile.Unlocks.Remove("profile.secret_hunt");
            LocalProfileStore.Merge(profilePath, session.Production.Content, profile); string before = File.ReadAllText(profilePath);
            Assert.ThrowsAny<IOException>(() => ExperimentSaveStore.Write(path, CombatJson, Adventure, Policy, Campaign, Endgame, Content, snapshot));
            Assert.Equal(before, File.ReadAllText(profilePath));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("Veilwalker")]
    [InlineData("Arcanist")]
    [InlineData("Gravecaller")]
    [InlineData("Warden")]
    public void BorrowedEchoRunsThroughOtherEarnedDisciplineBuilds(string discipline)
    {
        var session = Import(); At(session, new(-4500, -1800));
        Assert.True(session.ExecuteEndgame(new(EndgameRuntimeAction.Production, Production: new(ProductionAction.Retrain, Id: discipline))).Success);
        Assert.Equal(discipline, session.Combat.View.Discipline);
        Until(session, s => s.View.Run?.EchoActionId > 0);
        Assert.Equal(discipline, session.Combat.View.Discipline); Assert.Equal("Spent", session.View.Memory!.Status);
        Assert.Equal(session.StateHash, Restore(session.Capture()).StateHash);
        Assert.True(session.ExecuteEndgame(new(EndgameRuntimeAction.Abandon)).Success); Assert.Null(session.Combat.Capture().Experiment); Assert.Empty(session.View.Cosmetics);
    }
    [Fact]
    public void KeepMindChoiceSurvivesReplayRolloverAndMalformedChoiceHistoryRejects()
    {
        var session = Import(); Until(session, s => s.InHub && s.Endgame.View.AvailableSigils.Length > 0);
        Assert.True(session.StartContract(session.Endgame.View.AvailableSigils.Single().Id, ExperimentChoice.KeepMind).Success);
        Assert.True(session.ExecuteEndgame(new(EndgameRuntimeAction.Abandon)).Success);
        for (int i = 0; i < 1801; i++) session.Step(new CombatCommand(CombatCommandKind.Stop));
        var restored = Restore(session.Capture()); var choice = Assert.Single(restored.View.Entries);
        Assert.Equal(ExperimentChoice.KeepMind, choice.Choice); Assert.Equal("Abandoned", choice.Outcome);
        Assert.True(ExperimentReplayRunner.Run(CombatJson, Adventure, Policy, Campaign, Endgame, Content, session.CaptureReplay()).Success);
        var forged = session.Capture(); forged.Entries[choice.RunId] = choice with { SigilId = 999999 };
        Assert.Throws<InvalidDataException>(() => Restore(forged));
    }

    [Fact]
    public void MaintainedCatalogAndStormAreaRemainBoundToTheirExactRules()
    {
        Assert.Equal(Content.Hash, ExperimentContent.Parse(Read("experiments.json")).Hash);
        Assert.Throws<InvalidDataException>(() => ExperimentContent.Create(new(1, "Unknown", Content.Capture())));
        var session = Bound(); Until(session, s => s.View.Run?.EchoActionId > 0);
        var snapshot = session.Capture(); long hazard = snapshot.Endgame.Combat!.Experiment!.HazardId;
        Assert.True(snapshot.Endgame.Combat.Areas.RemoveAll(a => a.Id == hazard) > 0);
        Assert.Throws<InvalidDataException>(() => Restore(snapshot));
    }

}
