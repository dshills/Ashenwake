using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class MouseMovementSmoke
{
    private void CoopNavigation()
    {
        var session = CoopCombatSession.Create(_contentJson, 42, matchId: "mouse-navigation-smoke");
        var initial = session.View; var navigation = new CoopMouseMovement();
        var goal = new CorePosition(1800, -5000);
        Check("coop_ground_destination_accepted", navigation.SetDestination(initial, 1, goal));
        string initialHash = session.StateHash;
        Vector2 first = navigation.Resolve(initial, 1, Vector2.Zero, true);
        bool repeated = first != Vector2.Zero;
        for (int frame = 0; frame < 512; frame++) repeated &= navigation.Resolve(initial, 1, Vector2.Zero, true) == first;
        Check("coop_same_snapshot_render_frames_preserve_route", repeated && navigation.Destination == goal && session.StateHash == initialHash);

        var recorder = new CoopRecorder(session); bool accepted = true, clear = true;
        var directions = new HashSet<Vector2>();
        for (int tick = 0; tick < 240 && navigation.Destination is not null; tick++)
        {
            var view = session.View;
            var move = navigation.Resolve(view, 1, Vector2.Zero, true);
            if (move != Vector2.Zero) directions.Add(move);
            var input = new CoopInput(view.Players.Single(p => p.Id == 1).AcceptedSequence + 1, view.Tick, (int)move.X, (int)move.Y);
            navigation.ObserveSent(input);
            var receipt = recorder.Submit(session, 1, input); navigation.ObserveReceipt(receipt);
            accepted &= receipt.Accepted;
            recorder.Step(session);
            var position = session.View.Actors.Single(a => a.PlayerId == 1).Position;
            clear &= !view.Room.Obstacles.Any(o => position.X >= o.MinX && position.X <= o.MaxX && position.Z >= o.MinZ && position.Z <= o.MaxZ);
        }
        Check("coop_mouse_route_uses_accepted_server_inputs", accepted && directions.Count >= 2);
        Check("coop_server_reaches_ground_around_obstacle", clear && navigation.Destination is null &&
            Near(session.View.Actors.Single(a => a.PlayerId == 1).Position, goal, 240));
        var replay = recorder.Capture();
        Check("coop_mouse_movement_replay_matches", CoopReplayRunner.Run(_contentJson, replay).Success);
        System.IO.File.WriteAllText(Path.Combine(_output, "mouse-coop-replay.json"), JsonData.Write(replay));

        // The remaining checks are projection-only fixtures. They never manufacture server state,
        // rewards or inputs; each independently starts from a real initial server projection.
        void Start() { navigation = new(); if (!navigation.SetDestination(initial, 1, goal)) throw new InvalidDataException("Co-op fixture route unavailable."); }
        Start();
        Check("coop_manual_input_cancels_route", navigation.Resolve(initial, 1, Vector2.Left, true) == Vector2.Left && navigation.Destination is null);
        Start();
        Check("coop_disabled_input_cancels_route", navigation.Resolve(initial, 1, Vector2.Zero, false) == Vector2.Zero && navigation.Destination is null);
        Start(); navigation.Cancel();
        Check("coop_explicit_stop_cancels_route", navigation.Destination is null && navigation.Resolve(initial, 1, Vector2.Zero, true) == Vector2.Zero);

        var boundaries = new (string Name, CoopView View)[]
        {
            ("death", initial with { Actors = initial.Actors.Select(a => a.PlayerId == 1 ? a with { Health = 0, State = "Dead" } : a).ToArray() }),
            ("local_disconnect", initial with { Players = initial.Players.Select(p => p.Id == 1 ? p with { Connected = false } : p).ToArray() }),
            ("peer_disconnect", initial with { Players = initial.Players.Select(p => p.Id == 2 ? p with { Connected = false } : p).ToArray() }),
            ("context", initial with { ContextKey = initial.ContextKey + ":next" }),
            ("attempt", initial with { Attempt = initial.Attempt + 1 }),
            ("match", initial with { MatchId = "different-match" }),
            ("room", initial with { Room = initial.Room with { HalfWidth = initial.Room.HalfWidth + 500 } }),
            ("retry", initial with { AwaitingRetry = true }),
            ("completion", initial with { Completed = true })
        };
        foreach (var (name, view) in boundaries)
        {
            Start();
            Check("coop_" + name + "_clears_route", navigation.Resolve(view, 1, Vector2.Zero, true) == Vector2.Zero && navigation.Destination is null);
        }
        Start();
        Check("coop_player_slot_change_clears_route", navigation.Resolve(initial, 2, Vector2.Zero, true) == Vector2.Zero && navigation.Destination is null);
        foreach (string condition in new[] { "Staggered", "Windup" })
        {
            Start(); bool held = true;
            for (int tick = 1; tick <= 80; tick++)
            {
                var suppressed = initial with
                {
                    Tick = tick,
                    Actors = initial.Actors.Select(a => a.PlayerId != 1 ? a : condition == "Windup"
                        ? a with { State = "Windup", TelegraphTicks = 3 }
                        : a with { Statuses = [condition] }).ToArray()
                };
                held &= navigation.Resolve(suppressed, 1, Vector2.Zero, true) == Vector2.Zero && navigation.Destination == goal;
            }
            Check("coop_" + condition + "_holds_without_losing_route", held);
            Check("coop_" + condition + "_release_resumes_route", navigation.Resolve(initial with { Tick = 81 }, 1, Vector2.Zero, true) != Vector2.Zero && navigation.Destination == goal);
        }
    }
}
