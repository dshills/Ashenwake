using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Serialization;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Viewport input, fixed gameplay ticks and command replays for the normal solo mouse controls.</summary>
public partial class MouseMovementSmoke : Node
{
    private readonly Dictionary<string, bool> _checks = [];
    private readonly List<string> _captures = [];
    private readonly List<CombatCommand> _commands = [];
    private readonly List<CombatEvent> _events = [];
    private readonly List<CombatReplay> _replays = [];
    private Sandbox _sandbox = null!;
    private Camera3D _camera = null!;
    private CombatSession? _recordedSession;
    private CombatRecorder? _recorder;
    private string _output = "", _contentJson = "";
    private bool _writeReport;
    private CorePosition Player => _sandbox.Session.View.Actors.Single(a => a.Id == 1).Position;

    public override async void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            _output = args.FirstOrDefault(a => a.StartsWith("--output=", StringComparison.Ordinal))?[9..] ?? "";
            if (!args.Contains("--mouse-movement-smoke") || _output.Length == 0 ||
                Directory.Exists(_output) && Directory.EnumerateFileSystemEntries(_output).Any(p => !p.EndsWith(".log", StringComparison.Ordinal)))
                throw new InvalidDataException("Mouse movement smoke requires --mouse-movement-smoke --output=<fresh-artifact-directory>.");
            Directory.CreateDirectory(_output); _writeReport = true; Engine.MaxFps = 60;
            GetWindow().Size = new(1280, 800); GetWindow().ContentScaleSize = new(1280, 800);
            _contentJson = Godot.FileAccess.GetFileAsString("res://combat.json");
            _sandbox = new Sandbox { ContentJsonOverride = _contentJson, AutomaticStep = false };
            AddChild(_sandbox); _sandbox.SetProcess(false); _sandbox.EnableCampaign();
            _camera = Descendants(_sandbox).OfType<Camera3D>().Single();
            _sandbox.AdvanceOverride = Record;
            Reset();
            // Native focus notifications can arrive after scene creation. Settle startup and
            // use the real Resume button if they paused the game before viewport input begins.
            if (DisplayServer.GetName() != "headless") GetWindow().GrabFocus();
            await Frames(8);
            if (_sandbox.IsPaused) { await ClickButton("Resume playing"); Ticks(2); }
            await GroundTravel();
            await Interruptions();
            await CombatControls();
            CoopNavigation();
            CoopLatency();
            CloseRecording();
            Check("all_actual_input_command_replays_match", _replays.Count > 0 && _replays.All(r => CombatReplayRunner.Run(_contentJson, r).Success));
            System.IO.File.WriteAllText(Path.Combine(_output, "mouse-command-replays.json"), JsonData.Write(_replays));
            Finish(true, "");
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); Finish(false, ex.Message); }
    }

    private IReadOnlyList<CombatEvent> Record(CombatCommand[] commands)
    {
        if (!ReferenceEquals(_recordedSession, _sandbox.Session))
        { CloseRecording(); _recordedSession = _sandbox.Session; _recorder = new(_recordedSession); }
        _commands.AddRange(commands);
        var events = _recorder!.Step(_recordedSession!, commands); _events.AddRange(events); return events;
    }
    private void CloseRecording()
    {
        if (_recorder is { FrameCount: > 0 }) _replays.Add(_recorder.Capture());
        _recorder = null; _recordedSession = null;
    }
    private void Reset(string encounter = "hub")
    {
        CloseRecording();
        _sandbox.SetSession(CombatSession.CreateEncounter(_contentJson, 42, encounter));
        Ticks(2);
    }
    private void Ticks(int count = 1)
    {
        for (int i = 0; i < count; i++) _sandbox._Process(FixedStepClock.SecondsPerTick);
    }

    private async Task GroundTravel()
    {
        var origin = Player; var destination = new CorePosition(1800, -5000);
        int firstCommand = _commands.Count, firstEvent = _events.Count;
        await Ground(destination);
        Check("floor_click_sets_visible_destination", _sandbox.ClickMoveDestination is not null && _sandbox.MouseDestinationVisible);
        Check("floor_pick_matches_world_position", Near(_sandbox.ClickMoveDestination!.Value, destination, 35));
        ulong marker = Descendants(_sandbox).Single(n => n.Name == "MouseMoveDestination").GetInstanceId();
        await Capture("mouse-route-start.png");
        var room = CombatContent.Parse(_contentJson).Room;
        for (int i = 0; i < 240 && _sandbox.ClickMoveDestination is not null; i++)
        {
            Ticks();
            if (room.Obstacles.Any(o => Player.X >= o.MinX && Player.X <= o.MaxX && Player.Z >= o.MinZ && Player.Z <= o.MaxZ))
                throw new InvalidDataException("Mouse travel entered an authoritative obstacle.");
        }
        Check("mouse_route_reaches_selected_ground_around_obstacle", Near(Player, destination, 240) &&
            _commands.Skip(firstCommand).Where(c => c.Kind == CombatCommandKind.Move && (c.X != 0 || c.Z != 0)).Select(c => (c.X, c.Z)).Distinct().Count() >= 2);
        Check("arrival_clears_destination_and_marker", _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible);
        var arrived = Player; Ticks(20);
        Check("arrival_remains_stopped", Player == arrived && Player != origin);
        Check("floor_click_never_casts_primary", !_commands.Skip(firstCommand).Any(c => c.Kind == CombatCommandKind.Cast) &&
            !_events.Skip(firstEvent).Any(e => e.ActorId == 1 && e.Kind == "AbilityStarted"));
        await Capture("mouse-route-arrived.png");
        foreach (float zoom in new[] { 18f, 45f })
        {
            _camera.Size = zoom;
            var point = new CorePosition(3000, -2000); await Ground(point);
            Check("floor_pick_matches_zoom_" + zoom, _sandbox.ClickMoveDestination is { } pick && Near(pick, point, 35));
            Check("destination_marker_reused_at_zoom_" + zoom, Descendants(_sandbox).Single(n => n.Name == "MouseMoveDestination").GetInstanceId() == marker);
            await KeyPress(Key.X); Ticks();
        }
        _camera.Size = 25;
    }

    private async Task Interruptions()
    {
        Reset(); await Ground(new(3000, 0)); Ticks(3);
        Input.ActionPress("aw_down");
        Ticks(3); Input.ActionRelease("aw_down"); Ticks();
        Check("keyboard_movement_cancels_mouse_route", _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible && _commands.Any(c => c.Kind == CombatCommandKind.Move && c.Z > 0));
        await Ground(new(3000, 0)); await KeyPress(Key.X); Ticks();
        var stopped = Player; Ticks(12);
        Check("stop_key_cancels_destination_and_motion", Player == stopped && _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible);

        await Ground(new(3000, 0)); Ticks(2);
        int count = _commands.Count;
        await ClickButton("Settings [Esc]");
        Check("settings_click_is_consumed_and_cancels_route", _sandbox.IsPaused && _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible);
        var paused = Player; Ticks(8);
        Check("settings_pauses_without_floor_attack", Player == paused && !_commands.Skip(count).Any(c => c.Kind == CombatCommandKind.Cast));
        await KeyPress(Key.Escape); Ticks(10);
        Check("closing_settings_does_not_resume_old_route", !_sandbox.IsPaused && Player == paused && _sandbox.ClickMoveDestination is null);

        await Ground(new(3000, 0)); await KeyPress(Key.P);
        Check("manual_pause_cancels_destination", _sandbox.IsPaused && _sandbox.ClickMoveDestination is null);
        await KeyPress(Key.P); Ticks(8);
        Check("manual_resume_stays_stopped", !_sandbox.IsPaused && _sandbox.ClickMoveDestination is null && Player == paused);

        await Ground(new(3000, 0));
        _sandbox.Notification((int)NotificationApplicationFocusOut);
        Check("focus_loss_pauses_and_cancels_destination", _sandbox.IsPaused && _sandbox.ClickMoveDestination is null);
        await ClickButton("Resume playing"); Ticks(3);
        await Ground(new(3000, 0));
        Input.Singleton.EmitSignal(Input.SignalName.JoyConnectionChanged, 0L, false);
        Check("controller_loss_pauses_and_cancels_destination", _sandbox.IsPaused && _sandbox.ClickMoveDestination is null);
        await ClickButton("Resume playing"); Ticks(3);

        await Ground(new(3000, 0));
        _sandbox.AdoptSession(_sandbox.Session);
        Check("same_session_projection_preserves_route", _sandbox.ClickMoveDestination is not null);
        var room = CombatContent.Parse(_contentJson).Room;
        _sandbox.PresentAuthoredRoom(room, "mouse-smoke-room", "greyhaven"); Ticks();
        Check("room_context_change_clears_destination", _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible);
        await Ground(new(3000, 0)); Reset();
        Check("new_session_clears_destination", _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible);

        await KeyPress(Key.F5); var saved = Player;
        await Ground(new(3000, 0)); Ticks(4);
        await KeyPress(Key.F9); Ticks(10);
        Check("loading_saved_character_clears_route_and_stays_stopped", Player == saved && _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible);
        Check("isolated_save_was_written", System.IO.File.Exists(Path.Combine(_output, "sandbox.save.json")));
    }

    private async Task CombatControls()
    {
        Reset("encounter.ossuary"); await Frames();
        var enemy = _sandbox.Session.View.Actors.First(a => a.Faction == CombatFaction.Enemy && a.Health > 0 && a.Visible);
        int count = _commands.Count;
        await Click(_camera.UnprojectPosition(World(enemy.Position) + Vector3.Up)); Ticks();
        Check("visible_enemy_body_click_casts_primary_on_that_enemy", _commands.Skip(count).Any(c => c.Kind == CombatCommandKind.Cast && c.TargetId == enemy.Id && c.SkillId == _sandbox.Session.View.Skills[0].Id));
        Check("enemy_click_does_not_create_ground_destination", _sandbox.ClickMoveDestination is null && !_sandbox.MouseDestinationVisible);
        await Capture("mouse-enemy-primary.png");
        count = _commands.Count;
        await Click(_camera.UnprojectPosition(World(new(-6000, 1800))), shift: true); Ticks();
        Check("shift_left_forces_primary_on_ground", _commands.Skip(count).Any(c => c.Kind == CombatCommandKind.Cast && c.SkillId == _sandbox.Session.View.Skills[0].Id) && _sandbox.ClickMoveDestination is null);
        count = _commands.Count;
        await Click(_camera.UnprojectPosition(World(enemy.Position) + Vector3.Up), MouseButton.Right); Ticks();
        Check("right_click_preserves_secondary_skill", _commands.Skip(count).Any(c => c.Kind == CombatCommandKind.Cast && c.SkillId == _sandbox.Session.View.Skills[1].Id));
    }

    private Task Ground(CorePosition point) => Click(_camera.UnprojectPosition(World(point)));
    private static Vector3 World(CorePosition point) => new(point.X * .001f, 0, point.Z * .001f);
    private static bool Near(CorePosition a, CorePosition b, int distance) => CorePosition.DistanceSquared(a, b) <= (long)distance * distance;
    private async Task Click(Vector2 position, MouseButton button = MouseButton.Left, bool shift = false)
    {
        GetViewport().PushInput(new InputEventMouseMotion { Position = position }, true);
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventMouseButton { ButtonIndex = button, Position = position, Pressed = pressed, ShiftPressed = shift }, true);
        await Frames();
    }
    private async Task KeyPress(Key key)
    {
        foreach (bool pressed in new[] { true, false })
            GetViewport().PushInput(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed }, true);
        await Frames();
    }
    private async Task ClickButton(string text)
    {
        var button = Descendants(_sandbox).OfType<Button>().Single(b => b.Text == text && b.IsVisibleInTree());
        await Click(button.GetGlobalRect().GetCenter());
    }
    private async Task Frames(int count = 2) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private static IEnumerable<Node> Descendants(Node root) => root.GetChildren().SelectMany(n => new[] { n }.Concat(Descendants(n)));
    private async Task Capture(string filename)
    {
        if (!OS.GetCmdlineUserArgs().Contains("--capture-mouse") || DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        Check("capture_" + filename, image.GetWidth() > 0 && image.SavePng(Path.Combine(_output, filename)) == Error.Ok); _captures.Add(filename);
    }
    private void Check(string name, bool passed) { _checks[name] = passed; if (!passed) throw new InvalidDataException("Mouse movement check failed: " + name); }
    private void Finish(bool passed, string error)
    {
        var report = new
        {
            kind = passed ? "MouseMovementClientSmokePassed" : "MouseMovementClientSmokeFailed",
            passed,
            checks = _checks,
            captures = _captures,
            commandCount = _commands.Count,
            replayCount = _replays.Count,
            error,
            scope = "Actual viewport clicks exercise floor picking, obstacle traversal, combat buttons, settings and pause. Production fixed ticks poll keyboard actions. Application-focus and controller signals exercise interruption handling. Saves and all generated combat commands are isolated and replayed. Socket-free co-op movement uses real server inputs and a replay; projection fixtures isolate interruption and suppression cases."
        };
        if (_writeReport) System.IO.File.WriteAllText(Path.Combine(_output, "mouse-movement-review.json"), JsonData.Write(report));
        GD.Print(JsonData.Write(report)); GetTree().Quit(passed ? 0 : 1);
    }
}
