# Slice encounter and build rules

Phase 2 extends the Phase 1 combat engine with real dungeon encounters and all three Bell Saint phases. `CombatSession.CreateEncounter(contentJson, seed, encounterId, previous, restoreAtAnchor)` accepts `hub`, `encounter.ossuary`, `encounter.cloister`, `bell_saint.1`, `bell_saint.2`, `bell_saint.3`, and `clear`. The Expedition coordinator owns travel permission, progression, fragment ownership, and rewards; combat owns every hit, kill, corpse, ritual anchor, cooldown, and hostile behavior.

## Transfer and reset contract

Encounter changes preserve item instances and rolls, equipped item IDs, anatomy, mutations, RNG streams, logical tick, Momentum, and cooldowns. They discard old projectiles, areas, enemy actors, uncollected loot, summons, barriers, statuses, pending casts, and buffered input. Health and potion charges are preserved between encounters. Entering the hub or explicitly restoring at a death anchor heals/refills and clears cooldowns. A dead character cannot enter a fresh hostile encounter without that recovery flag. `clear` and `hub` have no enemies.

A newly created adventure starts with Eye of Vael and the first item definition for each functional equipment slot. Heart of Serath is unlocked only through the Expedition reward policy. The separate diagnostic arena retains unlocked fragment experimentation. Ashcleaver is excluded from ordinary randomized loot and the arena's automatically granted items. Its explicit development starter and boss rewards are per-instance grants in Expedition.

## Enemy and boss behavior

The original ghoul, acolyte, and brute provide melee pressure, ranged pressure, and armored threat. Cinder Priest is a specialist: it selects the most wounded visible ally within 9 m, telegraphs a 30-tick heal, then restores up to 30 health and grants 12 barrier. It launches projectiles when nobody needs healing. Prioritizing or staggering it stops support. Emberling closes quickly and telegraphs a 21-tick explosion in a 2.2 m radius, then dies once; move away, dodge, stagger, or kill it during that warning.

The Bell Saint has distinct encounter states:

| State | Behavior | Counterplay |
| --- | --- | --- |
| Bell Saint 1 | Alternates targeted chain impacts (1.5 m, Physical Pierce plus stagger) and sonic impacts (2.3 m, Storm). Each locks its target position at the start of a 36-tick warning and leaves 40 recovery ticks. | Walk or dodge out of the fixed warning; use the recovery window for melee or ranged hits. Armor and barriers mitigate chain hits; avoidance also works without defenses. |
| Bell Saint 2 | Two physical ritual anchors at X = -5500/+5500, Z = 0 shield the saint. It raises two authored ghoul corpses, then resumes chains/sonic attacks. | Target and destroy the 100-health anchors. Either direct attacks, area damage, or owned summons can break them. The saint becomes vulnerable when both die. No special skill, damage family, or interaction shortcut is required. |
| Bell Saint 3 | The 550-health beast rushes toward its last telegraphed player position and strikes within 2.2 m. Two destructible broken bells independently target 2 m sonic circles with 45-tick tells and 65-tick recovery. | Move perpendicular to the rush, prioritize bells to reduce overlapping hazards, or use defenses during the beast's recovery. |

`CombatActorView` exposes definition ID, fixed telegraph position, radius, and ticks remaining. These are gameplay warning positions, not prediction from animation. Bosses, ritual anchors, and bells resist Staggered so repeated Shield Breaker cannot permanently cancel the encounter. Other damage/status builds remain viable. The coordinator replaces encounters at phase transitions, which removes prior-phase effects and prevents cross-phase damage carryover.

Only the two authored corpses can be resurrected in the phase-two encounter. Each corpse ID is recorded once, is raised with half health, and cannot be raised again. Resurrected actors grant no ordinary item drop. Each raised ghoul may grant one burning kill on its actual defeat; its initial authored corpse did not emit a kill or grant progression. This supports attribution without repeated reward farming inside a single phase.

## Curated builds

`content/combat.json` defines twenty named `loadouts`, each with three distinct equipment slots, fragment choices, mutations, and a specific combat purpose. These are twenty distinct equipment combinations, not twenty random rolls. The matrix spans low-Resonance physical builds, armor/barrier retaliation, critical ignition, spirit/poison chains, concentrated fire, and broad Vulnerability/control. Reference/slot/mutation validation rejects unsupported combinations.

| Direction | Representative matrix rows | Tradeoff |
| --- | --- | --- |
| Low Resonance | Bare Iron, Storm March, Quiet Pilgrim | Fewer automatic effects; direct timing and reliable physical damage. |
| Defensive | March Guard, Stone Avalanche, Memory Wall | Armor and barriers trade some critical/offensive stats for recovery opportunities. |
| Critical ignition | Ember Duelist, Pilgrim Ember, Cinder Rain | Fire starts often, while lighter armor makes mistakes cost more. |
| Godwrought aggression | Ash Reaper, Furnace Guard, Tempered Sun | Burning kills build temporary attack speed and persistent awakening progress. |
| Spirit/poison | Ancestral Fire, Venom Procession, Serath Avalanche | Damage-over-time deaths create bounded owned summons; low-density fights offer fewer corpses. |
| Hybrid high Resonance | Hollow Bulwark, Wild Furnace, Iron Witness, Last Mercy | Wider fragment interactions and transformation choices, with their explicit complications. |

Reaping Arc changes Cleave into close persistent sweeps, costs five Momentum, and generates its usual Momentum only once per action. Iron Rain changes Seismic Wave into a broad field around the player, trades damage for coverage, and costs ten extra Momentum. Orrun's Knuckle occupies anatomy Arms and grants 12 barrier after health damage, no more than once every 45 ticks. It supplies a defensive direction independent of critical/summon chains.

## Manifestations and Ashcleaver

`CombatBuildModifiers` is a validated, saved projection of the Adventure progression record. The coordinator supplies the active Manifestation and the equipped Godwrought instance's progress. Actual Ashcleaver effects additionally require `item.ashcleaver` in MainHand, so swapping weapons cannot retain its bonuses.

- Burning Blood responds to incoming physical health damage by releasing eight Fire damage around the player once per root action per tick. Outgoing attacks and reflected damage cannot trigger it, and its Fire burst cannot retrigger itself. Potions heal 90 rather than 120.
- Stone Memory tracks the identity of incoming attacks. Repeated identical damage gains 10%, 20%, then 30% resistance for 150 ticks; another attack resets the pattern. While active, dodge's 32-tick cooldown becomes 40 ticks. Removing or changing the Manifestation clears the memory.
- Ashcleaver's five stacks each provide 5% attack speed; windup, recovery, and skill cooldown use integer-ceiling division. Tempering adds two direct skill damage per level. The coordinator owns the canonical 1,000 burning-kill awakening threshold and stack expiration.
- At five stacks while awakened, successful direct skill hits emit a Fire projectile at most once every 30 ticks. It hits within 1.6 m and applies Burning. Waves do not produce further waves.
- Serath evolution heals 10% of direct skill health damage (minimum one) on hit. Orrun evolution grants 500 defense basis points and four barrier on each successful direct skill hit. These effects require the selected awakened, equipped instance. Permanent choice and crafting payment are handled transactionally by Adventure.

## Verification and limits

`SliceCombatTests` covers encounter save continuation, travel/recovery state, anchor immunity, finite resurrection, independent bells, specialist support/explosion, both Manifestation benefits/complications, actual Ashcleaver equipment/attack speed/wave/evolution effects, matrix references, and successful low/high-Resonance completion of the same first Saint phase. Phase 1 combat tests continue to cover damage/resource timing and bounded chains. The root Expedition smoke test exercises all phases, real anchor kills, unique reward, return, save, and replay.

The arena geometry and client visuals remain a greybox production baseline. These deterministic encounter checks do not establish external playtest satisfaction, final boss difficulty, animation/audio quality, or a production-ready environment kit. Those remain explicit acceptance work rather than claims inferred from unit tests.
