namespace Ashenwake.Core.Simulation;

public static class BuildIdentity
{
    // Bump when simulation semantics change; content changes have an independent hash/version.
    public const string RulesVersion = "phase0.1";
    public const int SaveSchemaVersion = 1;
}

public sealed record ItemInstance(long InstanceId, string DefinitionId, int Damage);
public sealed record ActiveStatus(string DefinitionId, int SourceId, long NextTick, long ExpiresTick);
public sealed record PendingAbility(string DefinitionId, int TargetId, long ResolveTick);
public sealed record EntityState
{
    public int Id { get; init; }
    public string DefinitionId { get; init; } = "player.unbound";
    public Position Position { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; init; }
    public int MoveX { get; set; }
    public int MoveZ { get; set; }
    public long ReadyTick { get; set; }
    public PendingAbility? Ability { get; set; }
    public bool DeathProcessed { get; set; }
    public List<ActiveStatus> Statuses { get; init; } = [];
}
public sealed record LootState(long Id, Position Position, ItemInstance Item);
public sealed record ProgressionState(int Level, int Experience);
public sealed record RngStates(ulong Combat, ulong Loot, ulong Ai, ulong Encounters);
public sealed record WorldState
{
    public long Tick { get; set; }
    public ulong Seed { get; init; }
    public RngStates Rng { get; set; } = new(1, 2, 3, 4);
    public List<EntityState> Entities { get; init; } = [];
    public List<ItemInstance> Inventory { get; init; } = [];
    public List<string> Fragments { get; init; } = [];
    public long EquippedItemId { get; init; } = 1;
    public List<LootState> Loot { get; init; } = [];
    public ProgressionState Progression { get; set; } = new(1, 0);
    public long NextItemId { get; set; } = 2;
}

public enum CommandKind { Move, Attack, Pickup }
public sealed record GameCommand(long Tick, int ActorId, int Sequence, CommandKind Kind, int X = 0, int Z = 0, int TargetId = 0);
public sealed record SimulationEvent(long Tick, string Kind, int EntityId, int TargetId = 0, int Amount = 0, string? ContentId = null);
public sealed record EntityView(int Id, Position Position, int Health, int MaxHealth, bool Burning, bool Attacking);

public static class SeededRandom
{
    public static ulong Next(ref ulong state)
    {
        // SplitMix64: algorithm is part of the replay contract, unlike System.Random.
        state = unchecked(state + 0x9E3779B97F4A7C15UL);
        var z = state;
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    public static int Range(ref ulong state, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        // Rejection sampling avoids modulo bias in loot weights and item rolls.
        var bound = (ulong)count;
        var threshold = unchecked(0UL - bound) % bound;
        ulong sample;
        do sample = Next(ref state); while (sample < threshold);
        return (int)(sample % bound);
    }

    public static RngStates Streams(ulong seed)
    {
        return new(Next(ref seed), Next(ref seed), Next(ref seed), Next(ref seed));
    }
}

public sealed class FixedStepClock
{
    public const double SecondsPerTick = 1.0 / 30;
    private double _accumulator;
    public bool Paused { get; set; }
    public long DroppedTicks { get; private set; }
    public double Alpha => _accumulator / SecondsPerTick;

    public int Advance(double seconds, Action step)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Paused) return 0;
        _accumulator += Math.Min(seconds, 1.0);
        int count = 0;
        while (_accumulator + 1e-12 >= SecondsPerTick && count < 5)
        {
            step(); _accumulator = Math.Max(0, _accumulator - SecondsPerTick); count++;
            // A step can open a modal. Discard the remaining wall-clock debt so
            // neither this frame nor resuming the game advances behind that modal.
            if (Paused) { _accumulator = 0; return count; }
        }
        if (_accumulator >= SecondsPerTick)
        {
            var dropped = (long)(_accumulator / SecondsPerTick);
            DroppedTicks += dropped; _accumulator -= dropped * SecondsPerTick;
        }
        return count;
    }
    public void SingleStep(Action step) { if (Paused) { step(); _accumulator = 0; } }
}
