using System.Diagnostics;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Godot;
using FileAccess = Godot.FileAccess;

namespace Ashenwake.Client;

public partial class Main : Node3D
{
    private ContentBundle _content = null!;
    private SimulationWorld _world = null!;
    private ReplayRecorder _replay = null!;
    private FixedStepClock _clock = new();
    private readonly Dictionary<int, MeshInstance3D> _actors = [];
    private readonly Dictionary<long, MeshInstance3D> _drops = [];
    private readonly Dictionary<int, Vector3> _previous = [];
    private readonly Queue<string> _messages = new();
    private readonly List<GameCommand> _pending = [];
    private readonly List<double> _frameTimes = [];
    private readonly List<double> _frameIntervals = [];
    private readonly List<double> _tickTimes = [];
    private readonly List<SimulationEvent> _eventLog = [];
    private Label _status = null!, _log = null!, _metrics = null!;
    private ProgressBar _enemyHealth = null!;
    private bool _smoke;
    private int _frames;
    private string _output = "";
    private string? _capturePath;
    private double _lastTickMs;
    private long _lastTickBytes;
    private int _moveX, _moveZ;
    private string _session = Guid.NewGuid().ToString("N");

    public override void _Ready()
    {
        try
        {
            _smoke = OS.GetCmdlineUserArgs().Contains("--smoke");
            _output = Argument("--output=") ?? ProjectSettings.GlobalizePath("user://phase0");
            _capturePath = Argument("--capture=");
            _content = ContentCompiler.LoadBundle(FileAccess.GetFileAsString("res://content.bundle.json"));
            Bind("left", Key.A); Bind("right", Key.D); Bind("up", Key.W); Bind("down", Key.S);
            Bind("attack", Key.Space); Bind("pickup", Key.E); Bind("pause", Key.P); Bind("step", Key.Period);
            Bind("reset", Key.R); Bind("save", Key.F5); Bind("load", Key.F9); Bind("replay", Key.F6);
            BuildRoom(); BuildHud(); ResetWorld();
            Message("Ember of Vael implanted. Strike once; watch the Burning finish the hunt.");
        }
        catch (Exception ex) { Fail(ex); }
    }

    private static string? Argument(string prefix) => OS.GetCmdlineUserArgs()
        .FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private static void Bind(string name, Key key)
    {
        if (!InputMap.HasAction(name)) InputMap.AddAction(name);
        InputMap.ActionAddEvent(name, new InputEventKey { PhysicalKeycode = key });
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_smoke || _world is null) return;
        try
        {
            if (@event.IsActionPressed("pause")) { _clock.Paused = !_clock.Paused; return; }
            if (@event.IsActionPressed("step")) { _clock.SingleStep(StepWorld); return; }
            if (@event.IsActionPressed("reset")) { ResetWorld(); return; }
            if (@event.IsActionPressed("save"))
            {
                SaveStore.Write(Path.Combine(_output, "character.json"), _content, _world.Capture());
                Message("Saved logical state, fragment, item rolls, and RNG streams."); return;
            }
            if (@event.IsActionPressed("load"))
            {
                var save = SaveStore.Load(Path.Combine(_output, "character.json"), _content);
                SetWorld(new(_content, save.State));
                Message(save.RecoveredBackup ? "Recovered the backup save." : "Save restored."); return;
            }
            if (@event.IsActionPressed("replay")) { VerifyReplay(); return; }
            if (_clock.Paused) return;
            if (@event.IsActionPressed("attack") || @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                Queue(CommandKind.Attack, target: 2);
            if (@event.IsActionPressed("pickup")) Queue(CommandKind.Pickup);
        }
        catch (Exception ex) { Message(ex.Message); GD.PushError(ex.Message); }
    }

    public override void _Process(double delta)
    {
        if (_world is null) return;
        try
        {
            var start = Stopwatch.GetTimestamp();
            if (!_smoke && !_clock.Paused)
            {
                int x = (Input.IsActionPressed("right") ? 1 : 0) - (Input.IsActionPressed("left") ? 1 : 0);
                int z = (Input.IsActionPressed("down") ? 1 : 0) - (Input.IsActionPressed("up") ? 1 : 0);
                if (x != _moveX || z != _moveZ) { Queue(CommandKind.Move, x, z); _moveX = x; _moveZ = z; }
            }
            _clock.Advance(_smoke ? FixedStepClock.SecondsPerTick : delta, StepWorld);
            Present();
            _frames++;
            if (_frames > 10) _frameIntervals.Add(delta * 1000);
            if (_frameIntervals.Count > 3600) _frameIntervals.RemoveAt(0);
            _frameTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            if (_frameTimes.Count > 3600) _frameTimes.RemoveAt(0);
            if (_capturePath is not null && _frames == 50 && DisplayServer.GetName() != "headless")
            {
                Callable.From(() => GetViewport().GetTexture().GetImage().SavePng(_capturePath)).CallDeferred();
            }
            if (_smoke && _world.Tick >= 90) CompleteSmoke();
        }
        catch (Exception ex) { Fail(ex); }
    }

    private void Queue(CommandKind kind, int x = 0, int z = 0, int target = 0) =>
        _pending.Add(new(_world.Tick, 1, _pending.Count, kind, x, z, target));

    private void StepWorld()
    {
        // Keep an exact, bounded recent replay window for long interactive sessions.
        if (_replay.FrameCount >= 3600) _replay = new(_world);
        foreach (var view in _world.Entities) _previous[view.Id] = ToVector(view.Position);
        if (_smoke)
        {
            if (_world.Tick == 0) Queue(CommandKind.Attack, target: 2);
            if (_world.Tick == 40) Queue(CommandKind.Move, x: 1);
            if (_world.Tick == 43) Queue(CommandKind.Move);
            if (_world.Tick == 44) Queue(CommandKind.Pickup);
        }
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var events = _replay.Step(_world, _pending.ToArray());
        _pending.Clear();
        _lastTickMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _lastTickBytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
        _tickTimes.Add(_lastTickMs);
        if (_tickTimes.Count > 3600) _tickTimes.RemoveAt(0);
        foreach (var e in events)
        {
            _eventLog.Add(e);
            if (_eventLog.Count > 4096) _eventLog.RemoveAt(0);
            switch (e.Kind)
            {
                case "AbilityStarted": Message("Ember Strike · windup"); Pulse(e.EntityId, new Color("fff0bf")); break;
                case "DamageApplied": Message($"{(e.ContentId == "effect.burning" ? "Burning" : "Strike")} · {e.Amount} damage"); Pulse(e.EntityId, new Color("ff7041")); break;
                case "FragmentTriggered": Message("Ember of Vael → Burning"); break;
                case "EntityKilled": Message("Ash Ghoul slain · +10 experience"); break;
                case "LootDropped": Message($"Ash Iron dropped · {e.Amount} damage roll · E to collect"); break;
                case "LootCollected": Message("Ash Iron collected."); break;
                case "CommandRejected": Message("Cannot act: check range, target, or cooldown."); break;
                case "AbilityMissed": Message("Strike missed: target left reach or line of sight."); break;
            }
        }
    }

    private void Present()
    {
        var entities = _world.Entities;
        var lootView = _world.Loot;
        var liveLootIds = lootView.Select(loot => loot.Id).ToHashSet();
        foreach (var e in entities)
        {
            var mesh = _actors[e.Id];
            mesh.Visible = e.Health > 0;
            mesh.Position = _previous[e.Id].Lerp(ToVector(e.Position), (float)_clock.Alpha) + Vector3.Up * .7f;
            mesh.Rotation = new(0, e.Attacking ? .3f : 0, 0);
            if (e.Id == 2)
            {
                _enemyHealth.Value = e.Health * 100.0 / e.MaxHealth;
                _status.Text = e.Health > 0 ? $"ASH GHOUL   {e.Health}/{e.MaxHealth} HP{(e.Burning ? "   •   BURNING" : "")}" : "HUNT COMPLETE   •   Fragment interaction proven";
            }
        }
        foreach (var loot in lootView)
        {
            if (!_drops.TryGetValue(loot.Id, out var mesh))
            {
                mesh = Box(new(.3f, .3f, .3f), ToVector(loot.Position) + Vector3.Up * .3f, new Color("ffc764"));
                _drops.Add(loot.Id, mesh);
            }
            mesh.RotateY(.025f);
        }
        foreach (var id in _drops.Keys.Where(id => !liveLootIds.Contains(id)).ToArray())
        { _drops[id].QueueFree(); _drops.Remove(id); }
        _metrics.Text = $"{(_clock.Paused ? "PAUSED" : "30 HZ CORE")}   /   TICK {_world.Tick}\n" +
            $"Tick + replay {_lastTickMs:F3} ms  •  {_lastTickBytes:N0} B\n" +
            $"{entities.Count(e => e.Health > 0)} actors  •  {entities.Count(e => e.Burning)} effects  •  {lootView.Count} drops\n" +
            $"Catch-up ticks dropped {_clock.DroppedTicks}  •  Content {_content.Content.ContentVersion}";
    }

    private void ResetWorld() { SetWorld(new(_content)); Message("Encounter reset · same seed, same loot."); }
    private void SetWorld(SimulationWorld world)
    {
        _world = world; _clock = new(); _replay = new(world); _pending.Clear(); _moveX = 0; _moveZ = 0;
        // After load, reconcile saved movement intent against the current held keys on the next frame.
        _moveX = world.Capture().Entities[0].MoveX; _moveZ = world.Capture().Entities[0].MoveZ;
        foreach (var e in world.Entities) _previous[e.Id] = ToVector(e.Position);
        foreach (var mesh in _drops.Values) mesh.QueueFree();
        _drops.Clear(); _eventLog.Clear();
    }

    private void VerifyReplay()
    {
        Directory.CreateDirectory(_output);
        var replay = _replay.Capture();
        File.WriteAllText(Path.Combine(_output, "session.awr"), JsonData.Write(replay));
        var result = ReplayRunner.Run(_content, replay);
        if (!result.Success) throw new InvalidDataException($"Replay diverged at tick {result.DivergentTick}: {result.Detail}");
        Message($"Replay verified · {replay.Frames.Length} ticks · state and events match.");
    }

    private void CompleteSmoke()
    {
        VerifyReplay();
        SaveStore.Write(Path.Combine(_output, "smoke.save.json"), _content, _world.Capture());
        var restored = SaveStore.Load(Path.Combine(_output, "smoke.save.json"), _content);
        if (JsonData.Hash(restored.State) != _world.StateHash || restored.State.Inventory.Count != 2 ||
            !_eventLog.Any(e => e.Kind == "FragmentTriggered") || _eventLog.Count(e => e.Kind == "EntityKilled") != 1)
            throw new InvalidDataException("Godot smoke did not complete combat, fragment, loot, save, and replay loop.");
        _frameTimes.Sort(); _tickTimes.Sort(); _frameIntervals.Sort();
        var report = new
        {
            kind = "ClientSmokePassed",
            sessionId = _session,
            buildId = BuildIdentity.RulesVersion,
            contentHash = _content.Hash,
            contentVersion = _content.Content.ContentVersion,
            tick = _world.Tick,
            stateHash = _world.StateHash,
            display = DisplayServer.GetName(),
            cpu = OS.GetProcessorName(),
            processorCount = OS.GetProcessorCount(),
            memoryBytes = OS.GetStaticMemoryUsage(),
            clientProcessP95Ms = _frameTimes[(int)(_frameTimes.Count * .95)],
            frameIntervalP95Ms = _frameIntervals[(int)(_frameIntervals.Count * .95)],
            tickAndReplayP95Ms = _tickTimes[(int)(_tickTimes.Count * .95)],
            managedMemoryBytes = GC.GetTotalMemory(false),
            drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            renderProcessSeconds = Performance.GetMonitor(Performance.Monitor.TimeProcess),
            note = "Client _Process CPU sample includes replay hashing; not GPU frame time. Two actors, one status, one drop."
        };
        File.WriteAllText(Path.Combine(_output, "client-report.json"), JsonData.Write(report));
        File.WriteAllText(Path.Combine(_output, "events.jsonl"), string.Join('\n', _eventLog.Select(e => JsonData.Write(new
        { sessionId = _session, encounterId = "room.phase0", buildId = BuildIdentity.RulesVersion, contentHash = _content.Hash, simulationEvent = e }))));
        GD.Print(JsonData.Write(report));
        GetTree().Quit();
    }

    private void BuildRoom()
    {
        var environment = new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("10141c"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("8ba4b8"),
                AmbientLightEnergy = .6f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        };
        AddChild(environment);
        var light = new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightColor = new Color("ffca8e"), LightEnergy = 1.3f, ShadowEnabled = true };
        AddChild(light);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 19, Position = new(10, 13, 12), Current = true };
        AddChild(camera); camera.LookAt(new Vector3(0, 0, 0));
        var room = _content.Content.Room;
        Box(new(room.HalfWidth * .002f, .2f, room.HalfDepth * .002f), new(0, -.15f, 0), new Color("303b45"));
        // Floor grid is cosmetic. Collision comes from the exact same content geometry as Core.
        for (int x = -room.HalfWidth / 1000; x <= room.HalfWidth / 1000; x++)
            Box(new(.018f, .008f, room.HalfDepth * .002f), new(x, -.044f, 0), new Color("47515a"));
        for (int z = -room.HalfDepth / 1000; z <= room.HalfDepth / 1000; z++)
            Box(new(room.HalfWidth * .002f, .008f, .018f), new(0, -.044f, z), new Color("47515a"));
        foreach (var b in room.Obstacles)
        {
            Box(new((b.MaxX - b.MinX) * .001f, 1.5f, (b.MaxZ - b.MinZ) * .001f),
                new((b.MinX + b.MaxX) * .0005f, .75f, (b.MinZ + b.MaxZ) * .0005f), new Color("616165"));
        }
        foreach (var pair in new[] { (1, new Color("75dcca")), (2, new Color("d76956")) })
        {
            var mesh = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = .35f, Height = 1.4f }, MaterialOverride = Material(pair.Item2) };
            AddChild(mesh); _actors.Add(pair.Item1, mesh);
        }
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore }; layer.AddChild(root);
        root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Label LabelAt(string text, Vector2 position, int size, Color color)
        {
            var label = new Label { Text = text, Position = position, MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", size); label.AddThemeColorOverride("font_color", color);
            root.AddChild(label); return label;
        }
        LabelAt("A S H E N W A K E", new(36, 26), 28, new Color("eaddc4"));
        LabelAt("ARCHITECTURE SPIKE   /   THE FIRST EMBER", new(38, 66), 13, new Color("9da8b3"));
        _status = LabelAt("ASH GHOUL", new(38, 118), 16, new Color("efb18b"));
        _enemyHealth = new ProgressBar { Position = new(38, 148), Size = new(360, 8), ShowPercentage = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        root.AddChild(_enemyHealth);
        _metrics = LabelAt("", new(910, 30), 13, new Color("a9bbc7"));
        _log = LabelAt("", new(38, 510), 15, new Color("e3ccb5"));
        LabelAt("W A S D  Move     SPACE / CLICK  Strike     E  Collect     R  Reset", new(38, 710), 16, new Color("ede7da"));
        LabelAt("P  Pause     .  Single tick     F5  Save     F9  Load     F6  Verify & save replay", new(38, 742), 13, new Color("98a9b7"));
    }

    private void Message(string message)
    {
        _messages.Enqueue(message);
        while (_messages.Count > 7) _messages.Dequeue();
        if (_log is not null) _log.Text = string.Join('\n', _messages);
    }
    private MeshInstance3D Box(Vector3 size, Vector3 position, Color color)
    {
        var mesh = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = position, MaterialOverride = Material(color) };
        AddChild(mesh); return mesh;
    }
    private static StandardMaterial3D Material(Color color) => new() { AlbedoColor = color, Roughness = .85f };
    private static Vector3 ToVector(Position p) => new(p.X * .001f, 0, p.Z * .001f);
    private void Pulse(int id, Color color)
    {
        if (DisplayServer.GetName() == "headless" || !_actors.TryGetValue(id, out var actor)) return;
        var spark = Box(new(.8f, .06f, .8f), actor.Position + Vector3.Up * .3f, color);
        var tween = CreateTween();
        tween.TweenProperty(spark, "scale", new Vector3(1.8f, .1f, 1.8f), .16);
        tween.TweenCallback(Callable.From(spark.QueueFree));
    }
    private void Fail(Exception ex)
    {
        GD.PushError(ex.ToString());
        GetTree().Quit(1);
        SetProcess(false);
    }
}
