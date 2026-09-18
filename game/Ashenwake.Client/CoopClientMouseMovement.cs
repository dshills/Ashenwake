using Ashenwake.Core.Combat;
using Ashenwake.Core.Content;
using Ashenwake.Core.Coop;
using Ashenwake.Core.Simulation;
using Godot;
using CorePosition = Ashenwake.Core.Simulation.Position;

namespace Ashenwake.Client;

/// <summary>Converts local click intent into ordinary movement input; snapshots remain authoritative.</summary>
internal sealed class CoopMouseMovement
{
    private enum Travel { Cruise, Braking, Correction }
    private const int MaximumCorrections = 32, MaximumUnacknowledgedInputs = 64, StopAcknowledgementTicks = 180;
    private ClickMovePlanner? planner;
    private SpatialWorld? spatial;
    private RoomDefinition? room;
    private readonly List<CoopInput> sent = [];
    private CoopInput? lastProcessed;
    private CorePosition? goal;
    private Travel travel;
    private long neutralSequence, brakingTick;
    private int corrections;
    private string match = "", context = "";
    private int attempt = -1, player = -1, connections = -1;
    private long lastTick = -1, lastAcknowledged = -1;
    private Vector2 direction;
    public CorePosition? Destination => goal;

    public bool SetDestination(CoopView view, int playerId, CorePosition requested)
    {
        Synchronize(view, playerId);
        var self = view.Actors.FirstOrDefault(a => a.PlayerId == playerId);
        if (planner is null || !CanMove(view, playerId, self)) { Cancel(); return false; }
        Cancel();
        lastTick = -1;
        if (!planner.TrySetDestination(self!.Position, requested, Occupied(view, self.Id))) return false;
        goal = new(Math.Clamp(requested.X, -view.Room.HalfWidth + CoopCombatSession.ActorRadius, view.Room.HalfWidth - CoopCombatSession.ActorRadius),
            Math.Clamp(requested.Z, -view.Room.HalfDepth + CoopCombatSession.ActorRadius, view.Room.HalfDepth - CoopCombatSession.ActorRadius));
        if (planner.Destination is null) Brake(view.Tick);
        return true;
    }

    public Vector2 Resolve(CoopView view, int playerId, Vector2 manual, bool enabled)
    {
        Synchronize(view, playerId);
        var self = view.Actors.FirstOrDefault(a => a.PlayerId == playerId);
        long processed = view.Players.FirstOrDefault(p => p.Id == playerId)?.ProcessedSequence ?? 0;
        if (processed != lastAcknowledged)
        {
            lastProcessed = sent.LastOrDefault(input => input.Sequence <= processed) ?? lastProcessed;
            sent.RemoveAll(input => input.Sequence <= processed);
            lastAcknowledged = processed;
        }
        if (!enabled || !CanMove(view, playerId, self)) { Cancel(); return Vector2.Zero; }
        if (manual != Vector2.Zero) { Cancel(); return manual; }
        if (planner is null || goal is null) return Vector2.Zero;
        if (self!.State == "Windup" || self.TelegraphTicks > 0 || self.Statuses.Any(status => status is "Frozen" or "Rooted" or "Staggered" or "Terrified"))
        {
            // Suppressed movement is temporary, not evidence that this route is obstructed.
            direction = Vector2.Zero; lastTick = view.Tick;
            return Vector2.Zero;
        }
        if (travel == Travel.Braking)
        {
            direction = Vector2.Zero;
            if (neutralSequence == 0 || processed < neutralSequence)
            {
                if (view.Tick - brakingTick > StopAcknowledgementTicks) Cancel();
                return Vector2.Zero;
            }
            // A processed neutral input, not a stale nearby position, proves that walking stopped.
            // Until this point the goal and its marker remain visible even if prediction reached it.
            if (Near(self.Position, goal.Value)) { Cancel(); return Vector2.Zero; }
            if (corrections >= MaximumCorrections || !planner.TrySetDestination(self.Position, goal.Value, Occupied(view, self.Id)))
            { Cancel(); return Vector2.Zero; }
            travel = Travel.Correction; lastTick = -1;
        }
        // Render frames can outnumber snapshots. Reusing this direction avoids interpreting
        // several frames of the same authoritative position as a stalled route.
        if (lastTick != view.Tick)
        {
            lastTick = view.Tick;
            if (travel == Travel.Cruise && WillReachGoal(self.Position, Occupied(view, self.Id)))
            { Brake(view.Tick); return Vector2.Zero; }
            var next = planner.NextDirection(self!.Position, Occupied(view, self.Id));
            direction = new(next.X, next.Z);
            if (planner.Destination is null)
            {
                if (Near(self.Position, goal.Value)) Brake(view.Tick);
                else Cancel();
            }
        }
        return direction;
    }

    public void ObserveSent(CoopInput input)
    {
        if (sent.Any(previous => previous.Sequence == input.Sequence)) return;
        sent.Add(input);
        if (sent.Count > MaximumUnacknowledgedInputs) sent.RemoveAt(0);
        if (goal is null) return;
        if (travel == Travel.Correction && (input.MoveX != 0 || input.MoveZ != 0))
        {
            corrections++;
            Brake(lastTick);
        }
        else if (travel == Travel.Braking && input.MoveX == 0 && input.MoveZ == 0 && neutralSequence == 0)
            neutralSequence = input.Sequence;
    }

    public void ObserveReceipt(CoopInputResult receipt)
    {
        if (receipt.Accepted) return;
        sent.RemoveAll(input => input.Sequence == receipt.Sequence);
        if (receipt.Sequence == neutralSequence) neutralSequence = 0;
    }

    private void Brake(long tick)
    {
        travel = Travel.Braking; brakingTick = tick; neutralSequence = 0; direction = Vector2.Zero;
    }

    private bool WillReachGoal(CorePosition position, IReadOnlyList<CorePosition> occupied)
    {
        if (goal is not { } destination) return false;
        if (Near(position, destination)) return true;
        // Delayed changes of direction can orbit a goal without crossing its arrival circle.
        // Once a visible goal is inside the queued-motion stopping radius, settle first and
        // correct from the acknowledged resting point instead of steering through that orbit.
        int movingInputs = Math.Min(CoopCombatSession.InputTimeoutTicks + CoopCombatSession.MaxInputAge,
            sent.Count(input => input.MoveX != 0 || input.MoveZ != 0));
        long stoppingRadius = ClickMovePlanner.ArrivalTolerance + movingInputs * 150L;
        if (movingInputs > 0 && CorePosition.DistanceSquared(position, destination) <= stoppingRadius * stoppingRadius && spatial!.HasLineOfSight(position, destination)) return true;
        // Inputs normally arrive once per fresh server snapshot. Estimate motion already in flight,
        // including gaps between sends; this estimate only chooses when to send neutral input.
        // Real positions still come solely from snapshots. Irregular delivery or moving bodies may
        // make this estimate imperfect, so any settled error uses bounded move/neutral corrections.
        var previous = lastProcessed;
        int predictedTicks = 0;
        foreach (var input in sent)
        {
            int gap = previous is null ? 1 : (int)Math.Clamp(input.ClientTick - previous.ClientTick, 1, CoopCombatSession.InputTimeoutTicks);
            for (int tick = 0; tick < gap; tick++)
            {
                if (++predictedTicks > CoopCombatSession.InputTimeoutTicks + CoopCombatSession.MaxInputAge) return false;
                var intent = tick == gap - 1 ? input : previous!;
                int step = intent.MoveX != 0 && intent.MoveZ != 0 ? 106 : 150;
                var moved = spatial!.Move(position, new(position.X + intent.MoveX * step, position.Z + intent.MoveZ * step), CoopCombatSession.ActorRadius);
                if (!occupied.Any(body => CorePosition.DistanceSquared(moved, body) < 4L * CoopCombatSession.ActorRadius * CoopCombatSession.ActorRadius)) position = moved;
                if (Near(position, destination)) return true;
            }
            previous = input;
        }
        return false;
    }

    private static bool Near(CorePosition position, CorePosition destination)
        => CorePosition.DistanceSquared(position, destination) <= (long)ClickMovePlanner.ArrivalTolerance * ClickMovePlanner.ArrivalTolerance;

    public void Cancel()
    {
        planner?.Cancel();
        goal = null; travel = Travel.Cruise; neutralSequence = 0; corrections = 0;
        direction = Vector2.Zero;
    }

    private void Synchronize(CoopView view, int playerId)
    {
        int connected = view.Players.Aggregate(0, (mask, p) => mask | (p.Connected ? 1 << p.Id : 0));
        bool sameRoom = room is not null && room.HalfWidth == view.Room.HalfWidth && room.HalfDepth == view.Room.HalfDepth &&
            room.Obstacles.AsSpan().SequenceEqual(view.Room.Obstacles);
        if (sameRoom && match == view.MatchId && context == view.ContextKey && attempt == view.Attempt && player == playerId && connections == connected) return;
        // Transport validation allows a wider range of rooms than this optional input aid.
        // An unsupported room still permits the existing keyboard/controller movement.
        try { planner = new(view.Room); }
        catch (ArgumentException) { planner = null; }
        spatial = new(view.Room);
        room = view.Room;
        match = view.MatchId; context = view.ContextKey; attempt = view.Attempt; player = playerId; connections = connected;
        Cancel(); sent.Clear(); lastProcessed = null; lastTick = lastAcknowledged = -1;
    }

    private static bool CanMove(CoopView view, int playerId, CoopActorView? self)
        => self is { Health: > 0 } && view.Players.Any(p => p.Id == playerId && p.Connected) && !view.AwaitingRetry && !view.Completed;
    private static CorePosition[] Occupied(CoopView view, int actorId)
        => view.Actors.Where(a => a.Id != actorId && a.Health > 0).Select(a => a.Position).ToArray();
}

public partial class CoopClient
{
    private readonly CoopMouseMovement mouseMovement = new();

    private void CancelMouseMovement()
    {
        mouseMovement.Cancel();
        movement = Vector2.Zero;
        if (stage is not null) stage.SetDestination(null);
    }

    private void ToggleConnection()
    {
        connection.Visible = !connection.Visible;
        actions.Clear(); CancelMouseMovement();
    }
}
