# Combat sandbox rules (combat.1)

The engine-free `CombatSession` owns combat at 30 ticks/second. Content is read once, validated, and hashed; runtime state never holds mutable content references. Positions are integer millimeters in the shared flat X/Z collision model. `View` and `Capture` return detached data. Commands are applied in caller order, actors and spatial results in entity-ID order, and damage in FIFO order. Combat, loot, AI, and encounter RNG streams remain separate. AI currently needs no randomness; seeded placement and item rolls use their own streams.

## Actions and timing

The six authored Vanguard abilities are Cleave, Shield Breaker, Seismic Wave, Breaker Charge, Iron Guard, and Cataclysm. Definitions specify windup, recovery, cooldown, range, damage family, Momentum cost/gain, effect shape, radius, and status. Shapes are melee, projectile, persistent area, charge, and barrier. Shield Breaker's Avalanche changes it to an area; No Ground Given changes it to a barrier with nearby retaliation. One mutation per ability makes these mutually exclusive. Sandbox mutation access is unlocked and respec costs nothing; progression gating belongs to later phases.

A cast validates actor life, stagger, target, range, line of sight, cooldown, resource, and action availability before spending. Momentum and cooldown are committed at windup start. Damage occurs only at resolution, with range and line-of-sight checked again for aimed skills. Movement stops during windup. Recovery permits movement but rejects new casts, except one cast can buffer during its final six ticks; buffered input is fully revalidated when executed. Stop clears movement and the buffer. Dodge cancels the windup and buffer without refund, has seven ticks of invulnerability and a 32-tick cooldown, and moves by swept collision. Charge stops outside actor contact distance. Stagger interrupts windup and prevents combat actions. Potion heals up to 120, consumes one of three charges only when healing is possible, and has a 120-tick cooldown. Both remain separate from the six active slots.

Successful direct player hits generate the ability's Momentum once per action, even for area/multi-target hits. Cap is 100; Momentum decays by two every 15 ticks after 90 ticks without player-owned damage. Saves preserve it. Projectiles move 650 mm/tick, are blocked by geometry, collide with the first eligible actor in stable ID order, and expire after 90 ticks. Areas pulse every 20 ticks and expire after 61 ticks. Their source, owner, action and depth survive save/load.

## Damage order

`DamageRules.Resolve` is independently testable and applies:

1. Base damage plus flat attacker equipment bonuses.
2. Additive increased modifiers, then multiplicative more modifier.
3. Critical multiplier (150% by default); damage over time never crits.
4. Optional full conversion to another damage family.
5. Armor or resistance minus penetration, capped to 0–75% mitigation.
6. Vulnerability, capped at 100% increased damage taken.
7. Immunity, then barrier absorption, then health damage.
8. On-hit/fragment and on-damage retaliation triggers, then one death check.

Every multiplication floors before the next stage; results are bounded to one million damage. Zero final damage does not generate resources, statuses, or triggers. Damage fully absorbed by a barrier counts as a successful hit but reports zero health damage. Crit chance is 15% plus equipped rolls, capped at 75%. Physical Slash/Pierce/Crush, Fire, Frost, Storm, Decay, Venom, and Void are typed; the authored sandbox uses physical, Fire, and Venom. Conversion/modifier/penetration support is in the resolver; no sandbox item supplies conversion or penetration yet.

## Status and death semantics

Burning deals six Fire damage every 10 ticks for 90 ticks. Poisoned deals four Venom per stack every 15 ticks, up to three stacks, for 90 ticks. Reapplication refreshes duration without resetting the next damage tick; poison additionally gains a stack. The first source retains credit through refreshes. Staggered lasts 18 ticks (eight on elites), and Vulnerable lasts 90 ticks with 25% increased damage taken. Expiration happens before the tick's status processing. Barrier does not suppress an on-hit status. Existing status ticks continue when the source dies or a timed summon expires.

Death sets health to zero, clears pending action/movement, creates a persistent dead actor/corpse record once, and emits exactly one kill and one seeded equipment drop for an enemy. Damage queued against an already dead target is ignored. Loot belongs to the encounter; pickup requires proximity, preserves item identity/rolls, and caps inventory at 512. Items equip by compatible slot with no stat accumulation: derived bonuses are calculated from the current item IDs. Items use deterministic common/tempered/rare presentation and bounded damage/armor/critical rolls. The three functional sandbox slots are MainHand, Chest, and Amulet; all twelve equipment slots are Phase 3 scope.

## Fragment causality and bounds

All six anatomy slots are represented and slot compatibility is validated. The sandbox populates Eyes, Heart, and Spine. Fragments carry lineage, Resonance, and authored trigger/effect pairs:

- Eye of Vael: direct critical hits apply Burning.
- Heart of Serath: owner-attributed damage-over-time kills raise a 180-tick spirit.
- Nerve of Ilyra: spirit hits apply Poisoned.

These are dispatched as common trigger/effect pairs, without a named-combination check. Every event exposes source/target, root action, chain depth, and content ID. Spirits retain player ownership and generation. Summon poison may raise a second generation; higher generations are refused. At most eight spirits, 160 actor records, 128 projectiles, 64 areas, six chain levels, and 256 queued/processed damage effects per tick are allowed. A root action can generate Momentum once and a specific fragment trigger once per target within a tick. Retaliation cannot retaliate or trigger fragments; it inherits the action and increments depth. Bounds emit `EffectBudgetExceeded` and increment a visible counter instead of silently dropping work.

Unequipping/replacing a fragment removes its trigger registration, statuses explicitly sourced by that fragment, its owned spirits, and those spirits' projectiles and statuses. Equipment/fragment changes cannot accumulate derived bonuses. No corpse is consumed twice because only the single death transition can invoke DotDeath; raising the spirit does not resurrect the corpse.

## Enemies, arenas, and persistence

Melee pressure approaches and telegraphs strikes. Ranged enemies keep distance, reposition, and launch aimed projectiles. Armored brutes move slowly, have armor, and expose long windup/recovery. AI perception checks target life, distance, and occlusion; decisions are staggered over three ticks while movement remains continuous. A Stormbound elite prototype adds a telegraphed ground field. This phase authors one modifier, so incompatible modifier pairs cannot yet be authored. Dead actors stop acting.

`standard`, `dense`, `projectiles`, `summons`, and `chain` presets support reset by creating a new session. The last two are explicitly diagnostic and begin with a spirit and full Momentum; chain enemies have reduced health. Gameplay state has schema, rules, and content identities; restore rejects mismatches, malformed references, invalid bounds, duplicate identities, illegal equipped slots/mutations, and invalid timers. Save continuation and commands reproduce state hashes in the pinned runtime. There is no network determinism guarantee across arbitrary platforms.

## Phase 1 limits and remaining validation

This sandbox provides the functional combat rules and exposes feedback data to the Godot bridge. It does not establish final game balance, production art/audio quality, external player satisfaction, complete equipment-slot content, five disciplines, or campaign progression. Controller navigation and human observations of target readability/input feel must be assessed on the actual client. Automated repeatability, stress bounds, and damage/resource behavior are covered in `CombatTests`; they are not a substitute for that playtest gate.
