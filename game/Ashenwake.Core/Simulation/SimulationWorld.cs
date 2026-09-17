using Ashenwake.Core.Content;

namespace Ashenwake.Core.Simulation;

public sealed class SimulationWorld
{
    public const int ActorRadius = 350;
    public const int PlayerId = 1;
    private readonly ContentBundle _bundle;
    private readonly WorldState _state;
    private readonly SpatialWorld _spatial;
    private readonly List<SimulationEvent> _events = [];
    public long Tick => _state.Tick;
    public string ContentHash => _bundle.Hash;
    public string StateHash => JsonData.Hash(_state);
    public WorldState Capture() => JsonData.Copy(_state);
    public IReadOnlyList<EntityView> Entities => _state.Entities.Select(e => new EntityView(e.Id, e.Position, e.Health,
        e.MaxHealth, e.Statuses.Count > 0, e.Ability is not null)).ToArray();
    public IReadOnlyList<LootState> Loot => _state.Loot.ToArray();

    public SimulationWorld(ContentBundle bundle, ulong seed = 42)
    {
        _bundle = JsonData.Copy(bundle);
        ContentCompiler.LoadBundle(JsonData.Write(_bundle));
        _spatial = new(_bundle.Content.Room);
        var doc = _bundle.Content;
        var enemy = doc.Enemies.Single(e => e.Id == doc.StartingEnemy);
        var item = doc.Items.Single(i => i.Id == doc.StartingItem);
        var streams = SeededRandom.Streams(seed);
        var loot = streams.Loot;
        _state = new()
        {
            Seed = seed,
            Rng = streams,
            Entities =
            [
                new() { Id = PlayerId, Position = doc.Room.PlayerSpawn, Health = 100, MaxHealth = 100 },
                new() { Id = 2, DefinitionId = enemy.Id, Position = doc.Room.EnemySpawn, Health = enemy.Health, MaxHealth = enemy.Health }
            ],
            Inventory = [new(1, item.Id, item.MinDamage + SeededRandom.Range(ref loot, item.MaxDamage - item.MinDamage + 1))],
            Fragments = [doc.StartingFragment]
        };
        _state.Rng = streams with { Loot = loot };
    }

    public SimulationWorld(ContentBundle bundle, WorldState snapshot)
    {
        _bundle = JsonData.Copy(bundle);
        ContentCompiler.LoadBundle(JsonData.Write(_bundle));
        ValidateState(snapshot, _bundle);
        _state = JsonData.Copy(snapshot);
        _spatial = new(_bundle.Content.Room);
    }

    public IReadOnlyList<SimulationEvent> Step(IEnumerable<GameCommand>? commands = null)
    {
        var input = (commands ?? []).ToArray();
        if (input.Length > 1024 || input.Any(c => c is null || c.Tick != Tick || c.Sequence < 0 || !Enum.IsDefined(c.Kind)) ||
            input.Select(c => (c.ActorId, c.Sequence)).Distinct().Count() != input.Length)
            throw new InvalidDataException("Commands require the current tick, unique actor/sequence pairs, and at most 1024 entries.");
        _events.Clear();
        foreach (var c in input.OrderBy(c => c.ActorId).ThenBy(c => c.Sequence)) Apply(c);
        foreach (var e in _state.Entities.Where(e => e.Health > 0).OrderBy(e => e.Id))
        {
            var speed = e.MoveX != 0 && e.MoveZ != 0 ? 85 : 120;
            var desired = new Position(e.Position.X + e.MoveX * speed, e.Position.Z + e.MoveZ * speed);
            var moved = _spatial.Move(e.Position, desired, ActorRadius);
            if (!_state.Entities.Any(other => other.Id != e.Id && other.Health > 0 &&
                Position.DistanceSquared(moved, other.Position) < 4L * ActorRadius * ActorRadius)) e.Position = moved;
        }
        foreach (var e in _state.Entities.OrderBy(e => e.Id))
        {
            if (e.Health <= 0) { e.Ability = null; continue; }
            if (e.Ability is { } ability && ability.ResolveTick <= Tick)
            {
                e.Ability = null;
                var skill = _bundle.Content.Abilities.Single(a => a.Id == ability.DefinitionId);
                var target = _state.Entities.SingleOrDefault(t => t.Id == ability.TargetId);
                if (target is null || target.Health <= 0 || !InRange(e, target, skill.Range))
                { Emit("AbilityMissed", e.Id, ability.TargetId, contentId: skill.Id); continue; }
                var weapon = _state.Inventory.Single(i => i.InstanceId == _state.EquippedItemId);
                var combat = _state.Rng.Combat;
                int damage = skill.Damage + weapon.Damage + SeededRandom.Range(ref combat, 3);
                _state.Rng = _state.Rng with { Combat = combat };
                Damage(e.Id, target, damage, skill.Id);
                // The Phase 0 OnHit trigger cannot recursively trigger on status damage.
                foreach (var fragmentId in _state.Fragments.Order(StringComparer.Ordinal))
                {
                    if (target.Health <= 0) break;
                    var fragment = _bundle.Content.Fragments.Single(f => f.Id == fragmentId);
                    var status = _bundle.Content.Statuses.Single(s => s.Id == fragment.StatusId);
                    target.Statuses.RemoveAll(s => s.DefinitionId == status.Id);
                    target.Statuses.Add(new(status.Id, e.Id, Tick + status.IntervalTicks, Tick + status.DurationTicks));
                    Emit("FragmentTriggered", e.Id, target.Id, contentId: fragment.Id);
                    Emit("StatusApplied", target.Id, e.Id, contentId: status.Id);
                }
            }
        }
        foreach (var e in _state.Entities.OrderBy(e => e.Id))
        {
            foreach (var active in e.Statuses.OrderBy(s => s.DefinitionId, StringComparer.Ordinal).ToArray())
            {
                if (e.Health <= 0) break;
                var definition = _bundle.Content.Statuses.Single(s => s.Id == active.DefinitionId);
                if (Tick >= active.NextTick && Tick <= active.ExpiresTick)
                {
                    Damage(active.SourceId, e, definition.Damage, definition.Id);
                    int index = e.Statuses.IndexOf(active);
                    e.Statuses[index] = active with { NextTick = active.NextTick + definition.IntervalTicks };
                }
            }
            e.Statuses.RemoveAll(s => s.ExpiresTick <= Tick || e.Health <= 0);
        }
        // No autonomous attack behavior in Phase 0; consume a separate AI decision stream.
        // Future AI commands must target the following tick, never re-enter this command phase.
        if (Tick % 30 == 0)
        {
            var ai = _state.Rng.Ai;
            foreach (var _ in _state.Entities.Where(e => e.Id != PlayerId && e.Health > 0)) SeededRandom.Next(ref ai);
            _state.Rng = _state.Rng with { Ai = ai };
        }
        foreach (var e in _state.Entities.Where(e => e.Health <= 0 && !e.DeathProcessed).OrderBy(e => e.Id))
        {
            e.DeathProcessed = true; e.Ability = null; e.MoveX = 0; e.MoveZ = 0;
            Emit("EntityKilled", e.Id);
            if (e.Id != PlayerId)
            {
                var definition = _bundle.Content.Enemies.Single(d => d.Id == e.DefinitionId);
                var table = _bundle.Content.LootTables.Single(t => t.Id == definition.LootTableId);
                var loot = _state.Rng.Loot;
                int roll = SeededRandom.Range(ref loot, table.Entries.Sum(x => x.Weight));
                var chosen = table.Entries[0];
                foreach (var entry in table.Entries)
                {
                    chosen = entry; roll -= entry.Weight;
                    if (roll < 0) break;
                }
                var item = _bundle.Content.Items.Single(i => i.Id == chosen.ItemId);
                var instance = new ItemInstance(_state.NextItemId++, item.Id,
                    item.MinDamage + SeededRandom.Range(ref loot, item.MaxDamage - item.MinDamage + 1));
                _state.Loot.Add(new(instance.InstanceId, e.Position, instance));
                _state.Rng = _state.Rng with { Loot = loot };
                _state.Progression = _state.Progression with { Experience = _state.Progression.Experience + 10 };
                Emit("LootDropped", e.Id, amount: instance.Damage, contentId: item.Id);
            }
        }
        _state.Tick++;
        return _events.ToArray();
    }

    private bool InRange(EntityState a, EntityState b, int range) =>
        Position.DistanceSquared(a.Position, b.Position) <= (long)range * range && _spatial.HasLineOfSight(a.Position, b.Position);

    private void Apply(GameCommand c)
    {
        var actor = _state.Entities.SingleOrDefault(e => e.Id == c.ActorId);
        if (actor is null || actor.Id != PlayerId || actor.Health <= 0) { Emit("CommandRejected", c.ActorId); return; }
        switch (c.Kind)
        {
            case CommandKind.Move:
                if (c.X is < -1 or > 1 || c.Z is < -1 or > 1) { Emit("CommandRejected", actor.Id); return; }
                actor.MoveX = c.X; actor.MoveZ = c.Z;
                break;
            case CommandKind.Attack:
                var skill = _bundle.Content.Abilities.Single(a => a.Id == _bundle.Content.StartingAbility);
                var target = _state.Entities.SingleOrDefault(e => e.Id == c.TargetId);
                if (actor.Ability is not null || actor.ReadyTick > Tick || target is null || target.Id == actor.Id ||
                    target.Health <= 0 || !InRange(actor, target, skill.Range))
                { Emit("CommandRejected", actor.Id, c.TargetId); return; }
                actor.Ability = new(skill.Id, target.Id, Tick + skill.WindupTicks);
                actor.ReadyTick = Tick + skill.CooldownTicks;
                Emit("AbilityStarted", actor.Id, target.Id, contentId: skill.Id);
                break;
            case CommandKind.Pickup:
                foreach (var loot in _state.Loot.Where(l => Position.DistanceSquared(actor.Position, l.Position) <= 1800L * 1800).OrderBy(l => l.Id).ToArray())
                {
                    _state.Inventory.Add(loot.Item); _state.Loot.Remove(loot);
                    Emit("LootCollected", actor.Id, amount: loot.Item.Damage, contentId: loot.Item.DefinitionId);
                }
                break;
        }
    }

    private void Damage(int source, EntityState target, int amount, string contentId)
    {
        int actual = Math.Min(target.Health, amount);
        target.Health -= actual;
        Emit("DamageApplied", target.Id, source, actual, contentId);
    }
    private void Emit(string kind, int entity, int target = 0, int amount = 0, string? contentId = null) =>
        _events.Add(new(Tick, kind, entity, target, amount, contentId));

    public static void ValidateState(WorldState state, ContentBundle bundle)
    {
        if (state is null || state.Tick < 0 || state.Tick > long.MaxValue - 10000 || state.Rng is null || state.Entities is null ||
            state.Inventory is null || state.Fragments is null || state.Loot is null || state.Progression is null)
            throw new InvalidDataException("Invalid or missing save state fields.");
        var doc = bundle.Content;
        var spatial = new SpatialWorld(doc.Room);
        if (state.Entities.Count != 2 || state.Entities.Any(e => e is null) ||
            state.Entities.Select(e => e.Id).Order().SequenceEqual(new[] { 1, 2 }) == false)
            throw new InvalidDataException("Phase 0 state must contain player 1 and enemy 2.");
        foreach (var e in state.Entities)
        {
            if (e.Health < 0 || e.Health > e.MaxHealth || e.MaxHealth <= 0 || e.MoveX is < -1 or > 1 || e.MoveZ is < -1 or > 1 ||
                !spatial.CanOccupy(e.Position, ActorRadius) || e.Statuses is null || e.ReadyTick < 0 ||
                (e.DeathProcessed && e.Health != 0) || (e.Id == 1 ? e.DefinitionId != "player.unbound" : !doc.Enemies.Any(d => d.Id == e.DefinitionId)))
                throw new InvalidDataException($"Invalid entity {e.Id}.");
            if (e.Statuses.Any(s => s is null || !doc.Statuses.Any(d => d.Id == s.DefinitionId) || s.NextTick < state.Tick ||
                s.ExpiresTick < state.Tick || s.SourceId is < 1 or > 2) || e.Statuses.Select(s => s.DefinitionId).Distinct().Count() != e.Statuses.Count)
                throw new InvalidDataException($"Invalid statuses on entity {e.Id}.");
            if (e.Ability is { } a && (!doc.Abilities.Any(d => d.Id == a.DefinitionId) || a.TargetId is < 1 or > 2 || a.ResolveTick < state.Tick))
                throw new InvalidDataException($"Invalid pending ability on entity {e.Id}.");
        }
        if (state.Loot.Any(l => l is null || l.Item is null || l.Id != l.Item.InstanceId || !spatial.CanOccupy(l.Position, 0)))
            throw new InvalidDataException("Invalid loot state.");
        var allItems = state.Inventory.Concat(state.Loot.Select(l => l.Item)).ToArray();
        if (allItems.Any(i => i is null || i.InstanceId <= 0 || !doc.Items.Any(d => d.Id == i.DefinitionId && i.Damage >= d.MinDamage && i.Damage <= d.MaxDamage)) ||
            allItems.Select(i => i.InstanceId).Distinct().Count() != allItems.Length || allItems.Any(i => i.InstanceId >= state.NextItemId) ||
            !state.Inventory.Any(i => i.InstanceId == state.EquippedItemId)) throw new InvalidDataException("Invalid inventory/equipped item or instance IDs.");
        if (state.Fragments.Count > 1 || state.Fragments.Any(f => !doc.Fragments.Any(d => d.Id == f)) ||
            state.Progression.Level != 1 || state.Progression.Experience < 0)
            throw new InvalidDataException("Invalid fragments or progression.");
    }
}
