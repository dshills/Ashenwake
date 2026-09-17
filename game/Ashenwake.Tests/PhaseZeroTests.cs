using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Xunit;

namespace Ashenwake.Tests;

public sealed class PhaseZeroTests
{
    private static ContentBundle Content() => ContentCompiler.Compile(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "phase0.json")));
    private static GameCommand Attack(long tick = 0) => new(tick, 1, 0, CommandKind.Attack, TargetId: 2);

    [Fact]
    public void FragmentBurnKillsExactlyOnceAndDropsOneSeededItem()
    {
        var world = new SimulationWorld(Content());
        var all = new List<SimulationEvent>();
        for (int tick = 0; tick < 100; tick++) all.AddRange(world.Step(tick == 0 ? [Attack()] : []));
        Assert.Single(all, e => e.Kind == "AbilityStarted");
        Assert.Single(all, e => e.Kind == "FragmentTriggered");
        Assert.Single(all, e => e.Kind == "StatusApplied");
        Assert.Single(all, e => e.Kind == "EntityKilled");
        Assert.Single(all, e => e.Kind == "LootDropped");
        Assert.Equal(3, all.First(e => e.Kind == "DamageApplied").Tick);
        Assert.Equal(9, all.First(e => e.Kind == "DamageApplied" && e.ContentId == "effect.burning").Tick);
        Assert.Equal("effect.burning", all.Last(e => e.Kind == "DamageApplied").ContentId);
        Assert.Equal(10, world.Capture().Progression.Experience);
        Assert.Single(world.Loot);
        Assert.Equal(0, world.Entities.Single(e => e.Id == 2).Health);
    }

    [Fact]
    public void InvalidAndCooldownCommandsDoNotAdvanceCombatRng()
    {
        var world = new SimulationWorld(Content());
        var rng = world.Capture().Rng.Combat;
        Assert.Contains(world.Step([Attack()]), e => e.Kind == "AbilityStarted");
        Assert.Contains(world.Step([Attack(1)]), e => e.Kind == "CommandRejected");
        Assert.Equal(rng, world.Capture().Rng.Combat);
        Assert.Throws<InvalidDataException>(() => world.Step([Attack(20)]));
        Assert.Equal(2, world.Tick);
        Assert.Contains(world.Step([new(2, 1, 0, CommandKind.Move, X: int.MaxValue)]), e => e.Kind == "CommandRejected");
    }

    [Fact]
    public void MovingOutOfRangeDuringWindupMissesWithoutStatus()
    {
        var content = Content();
        var state = new SimulationWorld(content).Capture();
        state.Entities[0].Position = new(-2390, 0);
        var world = new SimulationWorld(content, state);
        world.Step([Attack(), new(0, 1, 1, CommandKind.Move, X: -1)]);
        world.Step(); world.Step();
        var events = world.Step();
        Assert.Contains(events, e => e.Kind == "AbilityMissed");
        Assert.Equal(30, world.Entities.Single(e => e.Id == 2).Health);
    }

    [Fact]
    public void CollisionSweepsThinObstaclesAndQueriesReturnStableOrder()
    {
        var room = new RoomDefinition(8000, 6000, new(-2000, 0), new(2000, 0), [new(0, -1000, 10, 1000)]);
        var queries = new SpatialWorld(room);
        Assert.False(queries.HasLineOfSight(new(-500, 0), new(500, 0)));
        Assert.Equal(new Position(-500, 0), queries.Move(new(-500, 0), new(500, 0), 100));
        Assert.False(queries.CanOccupy(new(7990, 0), 100));
        Assert.Equal(new[] { 1, 2 }, queries.Overlap(new(0, 0), 2000,
            [new() { Id = 2, Health = 1 }, new() { Id = 1, Health = 1 }]));
    }

    [Fact]
    public void SameSeedProducesSameOutcomeAndAiStreamDoesNotChangeLoot()
    {
        var content = Content();
        var first = new SimulationWorld(content);
        var state = first.Capture();
        var ai = state.Rng.Ai;
        for (int i = 0; i < 100; i++) SeededRandom.Next(ref ai);
        state.Rng = state.Rng with { Ai = ai };
        var second = new SimulationWorld(content, state);
        var third = new SimulationWorld(content);
        for (int tick = 0; tick < 80; tick++)
        {
            GameCommand[] commands = tick == 0 ? [Attack()] : [];
            first.Step(commands); second.Step(commands); third.Step(commands);
        }
        Assert.Equal(first.StateHash, third.StateHash);
        Assert.Equal(first.Capture().Loot, second.Capture().Loot);
        Assert.Equal(first.Capture().Rng.Loot, second.Capture().Rng.Loot);
    }

    [Fact]
    public void ReplayMatchesAndReportsFirstDivergentTick()
    {
        var content = Content();
        var world = new SimulationWorld(content);
        var recorder = new ReplayRecorder(world);
        for (int tick = 0; tick < 60; tick++) recorder.Step(world, tick == 0 ? [Attack()] : []);
        var replay = JsonData.Copy(recorder.Capture());
        Assert.True(ReplayRunner.Run(content, replay).Success);
        replay.Frames[7] = replay.Frames[7] with { StateHash = "corrupt" };
        var result = ReplayRunner.Run(content, replay);
        Assert.False(result.Success);
        Assert.Equal(7, result.DivergentTick);
        Assert.Throws<InvalidDataException>(() => ReplayRunner.Run(content, replay with { ContentHash = "other" }));
    }

    [Fact]
    public void SaveRoundTripResumesPendingAbilityAndStatusExactly()
    {
        var content = Content();
        var original = new SimulationWorld(content);
        original.Step([Attack()]);
        var clone = new SimulationWorld(content, SaveStore.Read(JsonData.Write(SaveStore.Create(content, original.Capture())), content));
        for (int i = 0; i < 12; i++) { original.Step(); clone.Step(); Assert.Equal(original.StateHash, clone.StateHash); }
        clone = new(content, SaveStore.Read(JsonData.Write(SaveStore.Create(content, original.Capture())), content));
        for (int i = 0; i < 60; i++) { original.Step(); clone.Step(); Assert.Equal(original.StateHash, clone.StateHash); }
    }

    [Fact]
    public void SaveBackupRecoversCorruptionWithoutOverwritingCompatibleBackup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ashenwake-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "save.json");
        try
        {
            var content = Content(); var world = new SimulationWorld(content);
            SaveStore.Write(path, content, world.Capture());
            world.Step([Attack()]); SaveStore.Write(path, content, world.Capture());
            File.WriteAllText(path, "truncated{");
            File.WriteAllText(path + ".unfinished.tmp", "unfinished write");
            var recovered = SaveStore.Load(path, content);
            Assert.True(recovered.RecoveredBackup);
            Assert.Equal(0, recovered.State.Tick);
            SaveStore.Write(path, content, world.Capture());
            Assert.Equal(0, SaveStore.Read(File.ReadAllText(path + ".bak"), content).Tick);
            Assert.False(SaveStore.Load(path, content).RecoveredBackup);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void NewerSaveIsRejectedWithoutFallingBackOrOverwriting()
    {
        var content = Content(); var world = new SimulationWorld(content);
        var save = SaveStore.Create(content, world.Capture()) with { SchemaVersion = 99 };
        Assert.Throws<SaveCompatibilityException>(() => SaveStore.Read(JsonData.Write(save), content));
        var dir = Path.Combine(Path.GetTempPath(), "ashenwake-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "save.json");
        try
        {
            var future = JsonData.Write(save);
            File.WriteAllText(path, future);
            File.WriteAllText(path + ".bak", JsonData.Write(SaveStore.Create(content, world.Capture())));
            Assert.Throws<SaveCompatibilityException>(() => SaveStore.Load(path, content));
            Assert.Throws<SaveCompatibilityException>(() => SaveStore.Write(path, content, world.Capture()));
            Assert.Equal(future, File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void MissingReferencesDuplicateIdsAndUnknownEffectsAreRejected()
    {
        var document = Content().Content;
        Assert.Throws<InvalidDataException>(() => ContentCompiler.Validate(document with { Abilities = [document.Abilities[0], document.Abilities[0]] }));
        Assert.Throws<InvalidDataException>(() => ContentCompiler.Validate(document with { Fragments = [document.Fragments[0] with { StatusId = "effect.missing" }] }));
        Assert.Throws<InvalidDataException>(() => ContentCompiler.Validate(document with { Statuses = [document.Statuses[0] with { Effect = "Unknown" }] }));
        Assert.Throws<InvalidDataException>(() => ContentCompiler.Validate(document with { LootTables = [new("loot.phase0", [new("item.missing", 0)])] }));
        Assert.Throws<InvalidDataException>(() => ContentCompiler.Validate(document with { Strings = [] }));
        Assert.Throws<InvalidDataException>(() => ContentCompiler.LoadBundle(JsonData.Write(Content() with { Hash = "tampered" })));
    }

    [Fact]
    public void MalformedSourceRequiresCoordinatesAndRejectsUnknownFields()
    {
        var source = JsonData.Write(Content().Content);
        var missing = System.Text.Json.Nodes.JsonNode.Parse(source)!;
        missing["room"]!["playerSpawn"]!.AsObject().Remove("z");
        Assert.Throws<System.Text.Json.JsonException>(() => ContentCompiler.Compile(missing.ToJsonString()));
        var unknown = System.Text.Json.Nodes.JsonNode.Parse(source)!;
        unknown["typo"] = true;
        Assert.Throws<System.Text.Json.JsonException>(() => ContentCompiler.Compile(unknown.ToJsonString()));
        var doc = Content().Content;
        Assert.Throws<InvalidDataException>(() => ContentCompiler.Validate(doc with
        {
            Room = doc.Room with { PlayerSpawn = new(int.MinValue, int.MinValue), EnemySpawn = new(int.MaxValue, int.MaxValue) }
        }));
    }

    [Fact]
    public void FixedClockPausesStepsAndBoundsCatchUp()
    {
        var clock = new FixedStepClock(); int steps = 0;
        Assert.Equal(1, clock.Advance(1.0 / 30, () => steps++));
        clock.Paused = true;
        Assert.Equal(0, clock.Advance(1, () => steps++));
        clock.SingleStep(() => steps++); Assert.Equal(2, steps);
        clock.Paused = false;
        Assert.Equal(5, clock.Advance(.5, () => steps++));
        Assert.True(clock.DroppedTicks > 0);
        Assert.InRange(clock.Alpha, 0, 1);
    }

    [Fact]
    public void SnapshotsCannotMutateWorldAndCoreDoesNotReferenceGodot()
    {
        var world = new SimulationWorld(Content());
        var hash = world.StateHash;
        world.Capture().Entities[0].Health = 1;
        Assert.Equal(hash, world.StateHash);
        Assert.DoesNotContain(typeof(SimulationWorld).Assembly.GetReferencedAssemblies(), a => a.Name!.Contains("Godot", StringComparison.OrdinalIgnoreCase));
    }
}
