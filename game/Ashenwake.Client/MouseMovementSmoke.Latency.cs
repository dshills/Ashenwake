using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

public partial class MouseMovementSmoke
{
    private void CoopLatency()
    {
        var reports = new List<object>();
        foreach (int delay in new[] { 0, 2, 3 })
            DelayedRoute("straight_" + delay, delay, new(-9500, -450));
        DelayedRoute("obstacle_3", 3, new(1800, -5000));
        DelayedRoute("suppressed_3", 3, new(-9500, -450), suppress: true);
        DelayedRoute("rejected_neutral_2", 2, new(-9500, -450), rejectNeutral: true);

        // A close destination still requires processed neutral input, and cancelled intent must
        // never be revived by a receipt or snapshot that arrives after the user has stopped.
        var original = CoopCombatSession.Create(_contentJson, 42, matchId: "mouse-late-ack").View;
        var closeGoal = original.Actors.Single(a => a.PlayerId == 1).Position;
        var pending = new CoopMouseMovement(); pending.SetDestination(original, 1, closeGoal);
        pending.ObserveSent(new(1, original.Tick));
        Check("coop_arrival_waits_for_processed_neutral", pending.Resolve(original, 1, Vector2.Zero, true) == Vector2.Zero && pending.Destination == closeGoal);
        var acknowledged = original with
        {
            Tick = original.Tick + 1,
            Players = original.Players.Select(p => p.Id == 1 ? p with { AcceptedSequence = 1, ProcessedSequence = 1 } : p).ToArray()
        };
        Check("coop_arrival_clears_after_processed_neutral", pending.Resolve(acknowledged, 1, Vector2.Zero, true) == Vector2.Zero && pending.Destination is null);
        pending.SetDestination(original, 1, new(-9500, -450));
        pending.ObserveSent(new(2, original.Tick, -1)); pending.Cancel();
        pending.ObserveReceipt(new(true, "accepted", 2, 1, 2, 1));
        Check("coop_late_ack_cannot_resume_cancelled_route", pending.Resolve(acknowledged, 1, Vector2.Zero, true) == Vector2.Zero && pending.Destination is null);
        System.IO.File.WriteAllText(Path.Combine(_output, "mouse-coop-latency.json"), JsonData.Write(reports));

        void DelayedRoute(string name, int delay, CorePosition goal, bool suppress = false, bool rejectNeutral = false)
        {
            var session = CoopCombatSession.Create(_contentJson, 42, matchId: "mouse-latency-" + name);
            var navigation = new CoopMouseMovement(); var received = session.View;
            var spatial = new SpatialWorld(received.Room);
            var inputs = new List<(int Due, CoopInput Input)>();
            var snapshots = new List<(int Due, CoopView View)>();
            var receipts = new List<(int Due, CoopInputResult Receipt)>();
            var sent = new Dictionary<long, CoopInput>();
            long sequence = 0, lastSentTick = -1, rejectedSequence = -1;
            int completed = -1, reversals = 0, previousSign = 0;
            bool accepted = true, legal = true, stable = true, held = true, sameFrame = true, proof = false;
            var trace = new List<object>();
            CorePosition settled = default;
            Check("coop_latency_" + name + "_starts", navigation.SetDestination(received, 1, goal));
            for (int wall = 0; wall < 1000; wall++)
            {
                foreach (var delivery in snapshots.Where(item => item.Due <= wall).ToArray())
                { received = delivery.View; snapshots.Remove(delivery); }
                foreach (var delivery in receipts.Where(item => item.Due <= wall).ToArray())
                { navigation.ObserveReceipt(delivery.Receipt); receipts.Remove(delivery); }
                bool suppressed = suppress && wall is >= 10 and < 95;
                // Projection-only suppression exercises presentation interruptions. All actual
                // movement below still runs through ordinary inputs and the real server Step.
                var shown = suppressed ? received with
                {
                    Actors = received.Actors.Select(a => a.PlayerId == 1 ? a with { Statuses = ["Staggered"] } : a).ToArray()
                } : received;
                var move = navigation.Resolve(shown, 1, Vector2.Zero, true);
                if (wall % 4 == 0 || navigation.Destination is null && completed < 0)
                    trace.Add(new { wall, shown.Tick, moveX = move.X, moveZ = move.Y, position = shown.Actors.Single(a => a.PlayerId == 1).Position, actual = session.View.Actors.Single(a => a.PlayerId == 1).Position, destination = navigation.Destination });
                for (int frame = 0; frame < 8; frame++) sameFrame &= navigation.Resolve(shown, 1, Vector2.Zero, true) == move;
                if (suppressed) held &= move == Vector2.Zero && navigation.Destination == goal;
                if (completed < 0 && navigation.Destination is null)
                {
                    completed = wall; settled = session.View.Actors.Single(a => a.PlayerId == 1).Position;
                    long processed = received.Players.Single(p => p.Id == 1).ProcessedSequence;
                    proof = sent.TryGetValue(processed, out var stopped) && stopped.MoveX == 0 && stopped.MoveZ == 0 && Near(settled, goal, ClickMovePlanner.ArrivalTolerance);
                }
                if (received.Tick > lastSentTick)
                {
                    var input = new CoopInput(++sequence, received.Tick, (int)move.X, (int)move.Y);
                    if (rejectNeutral && rejectedSequence < 0 && completed < 0 && sequence > 2 && move == Vector2.Zero)
                    {
                        // Fault injection makes the first braking input fail the real server's
                        // existing age check; the client must keep sending neutral replacements.
                        input = input with { ClientTick = 0 }; rejectedSequence = input.Sequence;
                    }
                    navigation.ObserveSent(input); sent.Add(input.Sequence, input);
                    inputs.Add((wall + delay, input)); lastSentTick = received.Tick;
                    int sign = Math.Sign(input.MoveX);
                    if (sign != 0) { if (previousSign != 0 && sign != previousSign) reversals++; previousSign = sign; }
                }
                foreach (var delivery in inputs.Where(item => item.Due <= wall).ToArray())
                {
                    var receipt = session.Submit(1, delivery.Input);
                    accepted &= receipt.Accepted || receipt.Sequence == rejectedSequence && receipt.Code == "tick_window";
                    receipts.Add((wall + delay, receipt)); inputs.Remove(delivery);
                }
                session.Step();
                var actual = session.View.Actors.Single(a => a.PlayerId == 1).Position;
                legal &= spatial.CanOccupy(actual, CoopCombatSession.ActorRadius);
                if (completed >= 0) stable &= actual == settled;
                snapshots.Add((wall + 1 + delay, session.View));
                if (completed >= 0 && wall >= completed + 24) break;
            }
            var final = session.View.Actors.Single(a => a.PlayerId == 1).Position;
            reports.Add(new
            {
                name,
                delayTicksEachWay = delay,
                completedWallTick = completed,
                serverTick = session.Tick,
                goal,
                final,
                sentInputs = sequence,
                reversals,
                rejectedSequence,
                suppressedProjection = suppress,
                proof,
                accepted,
                stable,
                legal,
                held,
                sameFrame,
                health = session.View.Actors.Single(a => a.PlayerId == 1).Health,
                trace
            });
            System.IO.File.WriteAllText(Path.Combine(_output, "mouse-coop-latency.json"), JsonData.Write(reports));
            Check("coop_latency_" + name + "_inputs_accepted", accepted);
            Check("coop_latency_" + name + "_confirmed_arrival", completed >= 0 && proof && Near(final, goal, ClickMovePlanner.ArrivalTolerance));
            Check("coop_latency_" + name + "_stays_stopped", stable && legal && sameFrame);
            if (suppress) Check("coop_latency_suppression_preserves_goal", held);
            if (rejectNeutral) Check("coop_latency_rejected_neutral_recovers", rejectedSequence > 0);
            if (name.StartsWith("straight", StringComparison.Ordinal)) Check("coop_latency_" + name + "_avoids_oscillation", reversals <= 1);
        }
    }
}
