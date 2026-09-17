# Phase 0 simulation contract

`RulesVersion = phase0.1`, save schema 1, replay schema 1, content schema/bundle 1.

## One tick

1. Validate the entire command batch structurally before mutation: current tick, known command kind, nonnegative sequence, unique actor/sequence pairs, at most 1,024 commands.
2. Process commands in actor-ID/sequence order. Reject nonexistent/dead/non-player actors, invalid movement axes, illegal attack targets, range/line-of-sight violations, and cooldown violations through `CommandRejected` events.
3. Move alive actors in entity-ID order. Each axis is -1, 0, or 1; cardinal speed is 120 mm/tick and diagonal components are 85 mm/tick. Resolve static collision by sweep and actor overlap by rejecting the movement.
4. Resolve pending abilities in entity-ID order. Recheck the target, health, range, and line of sight at impact. Misses do not consume combat RNG or apply statuses. The cooldown begins when the ability starts, including if the hit later misses.
5. Apply damage: skill base + equipped weapon's persisted roll + combat RNG [0,2]. Clamp applied damage to remaining health. Core emits `DamageApplied`.
6. Direct surviving hits trigger the equipped Arms fragment and apply Burning. Its first tick occurs after its interval. Reapplication replaces its timer/source. Burning is not an OnHit trigger source.
7. Process statuses in entity-ID/status-ID order. Apply scheduled damage including a tick exactly at expiry; remove expired statuses and statuses on dead entities. Current content cannot form a recursive trigger graph.
8. Advance the isolated placeholder AI stream every 30 ticks for living enemies. Future AI action commands must enter the next tick, not mutate a completed command phase.
9. Process each newly dead entity once, cancel ability/movement, emit death, then select/roll one loot item and award 10 XP. Item instance IDs increase monotonically. Loot pickup transfers an instance instead of creating a replacement and accepts items within 1,800 mm.
10. Increment the simulation tick and return immutable semantic-event records. The replay captures state/event hashes after this tick.

An action submitted at tick 0 with a three-tick windup resolves while processing tick 3. Its Burning pulses at ticks 9, 15, 21, 27, and 33 if the target survives. The default seed/weapon permits that one strike plus Burning to kill the 30-HP enemy. A 90-tick demo also approaches and collects the item.

The full design's criticals, resistance/defense, conversion, barriers, resource cost, crowd control, summons, reflection, generic triggers, and complex effects are not implemented by this reduced pipeline. Add them in the documented Phase 1 order with interaction tests before widening the content vocabulary.

## Replay and save contract

Snapshots contain the initial seed, all four RNG states, current tick, actor state, pending ability/cooldown/status schedules, inventory and ground loot, equipped item identity, installed fragment IDs, progression stub, and next item ID. Serialization ordering is part of this version's contract. Replay commands reference simulation ticks and stable IDs, not wall time or Godot object handles.

Runtime content must match the stored content hash. Saves/replays must match the rules/schema versions. Unknown IDs and invalid state are rejected before constructing the simulation. Hashes detect accidental corruption; they are not anti-cheat signatures. Save IO and timing/profiling live outside the rule loop.

The client records at most 3,600 ticks per replay segment. Reset/load and periodic segment rollover capture a new initial snapshot. F6 writes and headlessly verifies the current segment. The test harness additionally compares a client-recorded 90-tick scenario to the CLI-run scenario; matching only the same implementation's own replay is not the sole integration check.
