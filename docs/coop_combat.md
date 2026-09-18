# Phase 7 shared-world combat prototype

This is an engine-free, server-authoritative two-player prototype of Ossuary, Cloister and all three Bell Saint phases. Both players occupy **one** actor table and share enemy health, collision, projectiles, warnings, corpses, random streams and encounter progression. It does not run two solo simulations. Solo campaign/endgame state schemas and hashes are unchanged.

The approved prototype scope uses two fixed Vanguard loadouts authored by the server. Player 1 has Ash Axe, March Plate and Ember Lens; player 2 has Oath Hammer, March Plate and Ember Lens. Their health is 350/390, and actual equipment definitions supply armor, damage and critical chance. All six existing Vanguard skills use their authored damage, family, cost, generation, range and timings. Multiplayer enemy health is authored health × 1.6; enemy damage/timings remain authored. Character import, other disciplines, anatomical fragment loadouts, trading and permanent equipment changes are outside this bounded combat prototype.

## Host contract

```csharp
using Ashenwake.Core.Coop;
var session = CoopCombatSession.Create(combatJson, seed: 42,
    encounterId: "coop.ossuary", matchId: serverAllocatedMatchId);
CoopInputResult admission = session.Submit(authenticatedPlayerId, input);
IReadOnlyList<CoopEvent> events = session.Step(); // exactly one authoritative 1/30-second tick
CoopView snapshotForClients = session.View;
CoopSnapshot logicalSave = session.Capture();
var restored = CoopCombatSession.Restore(combatJson, logicalSave);
```

`MatchId`, player-slot binding, content, tick scheduling, snapshot persistence and payout consumption belong to the host. A match ID must be server-allocated and unique within the control plane's reward namespace. Neither player identity nor power values are accepted from `CoopInput`. The transport must derive `authenticatedPlayerId` from its authenticated connection, never from the submitted body. `SubmitJson` applies strict JSON parsing and a 4096-character input cap; unknown fields, numeric/unknown enums and missing sequence/tick fields fail admission.

Input contains `Sequence`, `ClientTick`, `MoveX/Z`, `Action` (`None`, `Cast`, `Dodge`, `Potion`, `Ready`), `SkillId` and `TargetId`. Movement axes are integers in [-1,1]. Admission checks a six-tick late/future window, monotonically increasing sequence and application ticks, at most one input per player per application tick, a maximum sequence jump of 1024 and at most eight queued inputs per player. The current six-tick future window usually imposes the tighter queue limit. A receipt retains the last 64 accepted payload hashes: exact duplicates acknowledge the previous admission without replaying it; changed duplicates fail. Older sequences remain rejected after receipts expire. Accepted and processed sequence numbers are separate view fields. A semantic cast rejection still processes/acknowledges its admitted intent and emits `InputRejected`.

An input updates held movement; movement expires after ten ticks without fresh intent. `SetConnected` clears queued/held movement and readiness, while preserving accepted sequence, current attacks, health, resources, cooldowns and rewards. Disconnected bodies remain damageable. Enemies acquire the nearest connected living player; an already committed warning retains its position. Reconnect resumes the same actor, so the client starts its next sequence above `AcceptedSequence`. The host calls these APIs on one serialized simulation thread.

`Ready` never resolves a live encounter. Both connected players must ready after all hostiles die to enter the next room. A real party wipe similarly requires both ready to retry the same room with an incremented attempt. Retries restore the fixed builds' health/potions and reset encounter effects. Ordinary successful transitions carry living players' health, Momentum and potions; fallen teammates return at half health at the transition. Completion of the fifth encounter ends the slice. `ContextKey` combines match, room and attempt and changes on retries; projections/reconnects do not change it.

## Shared simulation and counterplay

Movement uses integer millimeters, `SpatialWorld` swept wall collision and shared living-actor separation. Actions use the existing `DamageRules` ordered integer pipeline, barriers, armor, critical rolls and vulnerability. Damage never hits the source's player/enemy faction. Source IDs and action IDs remain attached to statuses and projectiles. Burning ticks, vulnerability and stagger are the bounded prototype statuses. Boss stagger lasts four ticks and has a 90-tick immunity window; ordinary enemies stagger for fourteen ticks with a 40-tick immunity window.

Melee, ranged and armored enemies use authored ranges and tells. Priests heal and shield a wounded ally or fire a projectile when none needs healing. Emberlings rush and show a locked detonation circle; moving out or killing them during windup prevents the impact. Vanguard cleave, shield breaker, seismic projectiles, charge, iron guard and cataclysm have actual server-owned windup/recovery/resource behavior. Invalid targets/ranges/line of sight, insufficient Momentum and busy/cooldown actions cannot spend or grant resources. Dodge cancels the player's windup and grants seven ticks of invulnerability to that player only.

Bell Saint phase 1 alternates three locked chain lashes with sonic circles on both living heroes. The line warning's radius is its half-width; its full visual width is `2 * Radius`. Phase 2 has two targetable ritual anchors shielding the shared boss; two original corpses can each be resurrected once. Killing the anchors removes the shield. Phase 3 combines the beast's locked landing/rush circle and two independent bell emitters that can be killed to remove their warnings. Windup locations remain fixed even if their source or target later moves. Warnings clear when their source dies. Killing all enemies seals the room, clearing hostile in-flight effects before the reward/ready pause.

Caps are 24 actors, 64 projectiles, 48 warnings, 16 queued inputs, 512 emitted events per tick and 10 personal encounter reward receipts. There is no unbounded summon or resurrection chain. Views expose current and peak populations. Corpses are retained only within their room and cannot award repeat kill loot; reward authority is actual room completion.

## Rewards, snapshots and replay

Each actual room clear grants one personal receipt to each server-owned slot, including a fallen/disconnected participant. Receipt IDs are `<match>:<encounter-index>:player:<slot>`. The simulation never duplicates them. Each receipt contains authored experience/Ash and a seeded item; these are not automatically equipped and cannot alter the fixed combat loadout. The host must apply each receipt transactionally and idempotently to the corresponding authenticated account. Receipts cannot be requested through an input action. Enemy or resurrected-corpse deaths never independently mint permanent rewards.

Snapshots use their own schema 1 and rules `coop.1`, content identity, shared RNG, actor/action/object counters, bounded collections, intent receipts and queued application ticks. Restore validates owners, fixed build definitions, actor values, references, resource limits, timers, input order, object identity and earned clear/reward conditions. `Capture` and `View` are detached copies; mutating them does not mutate the session. Snapshots must be loaded from trusted server storage with the host's integrity/version envelope, not accepted as a player's state upload.

`CoopRecorder` records `Submit`, `SetConnected` and `Step` operations with state/result hashes. `CoopReplayRunner.Run(combatJson, replay)` checks each admission result, tick event batch and full state. Connection timing and rejected submissions therefore reproduce deterministically. Replay journals cap at 100,000 operations. This is a server replay, not client prediction or rollback networking.

## Reproducible checks

`CoopSmoke.Input(view, playerId, sequence)` emits ordinary intents for the full slice. The socket host and client smoke tests can use it directly, once per new authoritative tick. It understands warning geometry, safe dodges, occupied actors and the targetable anchor/bell counterplay. It cannot edit health, rewards, encounters or simulation time.

`CoopDiagnostics.Run(combatJson, seeds)` runs the two input streams, captures/replays the authoritative journal, checks periodic restoration and returns completion, wipes, personal receipts, player health, peak populations and step percentiles. Reported timing includes the recorder's state/result hashing; network transport and rendering are measured separately. The executable tests cover multiple complete seeds, shared health/no friendly fire, authority rejection, sequence/queue limits, reconnect, stale held movement, ritual shield/resurrection, telegraph ownership, actual idle-input party wipes, receipt uniqueness and replay tampering. Client prediction quality, public-network latency, simultaneous machine failure and production database deployment require the separate host/control-plane integration gates.

The initial local reference run (`artifacts/coop-core-diagnostics.json`) completed seeds 42/43/44 in 962/944/945 ticks with zero party wipes, 10 receipts each, all replay/restore checks passing, and surviving player health 230/245, 284/307, 302/241 respectively. Observed peak populations were 7 actors, 2 projectiles, 3 warnings and 2 queued inputs; recorded-tick p99 was 0.544 ms or lower on this machine. These are measured prototype results, not a network latency or cross-hardware performance guarantee.
