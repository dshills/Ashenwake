# Phase 3 combat foundation

`CombatProgressionBuild` is the validated bridge from permanent progression into simulation. It contains discipline, level, passive/affix additions, bounded fork/chain counts, dodge/summon properties, ultimate access, unlocked mutations, and purified fragments. Item base values remain on item instances and are counted exactly once. Updating the same discipline preserves health, resources, active actions, and cooldowns. Changing discipline requires a hub, cleared room, or no live hostiles, then clears pending/buffered actions and resets the resource. `CombatView.Skills` exposes only that discipline's six slots; its `Resource`, `MaxResource`, and `ResourceName` expose the shared meter without making clients infer resource semantics.

## Disciplines and resources

| Discipline | Six skills | Resource rules |
| --- | --- | --- |
| Vanguard | Cleave, Shield Breaker, Seismic Wave, Breaker Charge, Iron Guard, Cataclysm | Momentum comes from successful direct skill actions and pays for heavy attacks. Decays after three seconds without aggression. Existing IDs and timing remain compatible with the earlier sandbox. |
| Veilwalker | Venom Knife, Shadow Step, Dusk Fan, Shroud, Terror, Shadow Execution | Exposure comes from weakness-oriented attacks; striking behind the enemy's committed facing adds eight. Execution consumes Marked for doubled increased damage. Exposure pays for mobility/control/execution and decays out of combat. |
| Arcanist | Fire Lance, Frost Nova, Storm Arc, Vent, Ember Stride, Starfall | Ordinary spells add Instability instead of spending it. Instability increases damage up to 40%; casting above 80 causes three self-damage, never a direct self-kill. Over-cap casts are rejected without side effects. Vent removes 40 and cleanses Burning. Starfall spends 65. Idle heat dissipates three every 15 ticks. |
| Gravecaller | Grave Bolt, Bone Lance, Raise Ancestor, Soul Siphon, Grave Command, Procession | Kills grant 25 Remains; explicitly harvesting an available corpse grants 25. Raising a minion additionally consumes a real, unclaimed nearby corpse. Procession raises up to three from distinct corpses. The basic bolt, lance, and siphon work without corpses, so boss arenas remain playable. |
| Warden | Thorn Shot, Entangle, Feral Companion, Barkskin, Adaptive Strike, Primal Awakening | Hits generate Adaptation; incoming health damage generates eight and builds resistance to that damage family, up to 20%, for 150 ticks. Spending creates traps, healing/barriers, a companion, or poison detonation. Adaptation decays outside combat. |

All meters cap at 100. Invalid casts validate skill availability, target/visibility, range, line of sight, summon/corpse requirements, resource, cooldown, and action state before payment. Resource generation uses saved action receipts, so multi-target hits, area pulses, and save/load cannot multiply a single action's generation. Receipts expire after 300 ticks and are bounded. Resource bonuses add to actual generation rather than granting free resource on every hit.

## Projectile and mutation composition

Fire Lance is a finite piercing projectile. Its four mutually exclusive mutations are:

- Forking Flame creates up to two lower-damage branches after the first collision. Branches cannot fork again.
- Furnace removes piercing and creates a 2.4 m explosion at the first hit.
- Cauterize reduces direct damage; Burning explicitly attributed to Fire Lance heals its owner.
- Living Flame has a seeded 50% chance on a direct Fire Lance kill to consume the corpse and create a temporary fire spirit.

Equipment fork/chain effects are bounded, mutually exclusive, preserve owner/root/depth and visited target IDs, and cannot repeatedly hit the same target. Every projectile has a lifetime and participates in the existing global projectile/effect limits. Chain and fork events are available to inspectors and replays.

Shield Breaker's Executioner's Pace resets its cooldown on a staggered enemy kill. Orrun's Patience commits a charged windup for up to 60 ticks and grants 15% temporary defense while held. `ReleaseCharge` resolves early with damage proportional to elapsed ticks. Dodge and hard control cancel charging without a refund. Existing Avalanche and No Ground Given remain available.

## Complete status vocabulary

The existing damage pipeline still determines mitigation, immunity, barrier, triggers, and one death transition.

| Status | Runtime behavior |
| --- | --- |
| Burning | Six Fire damage each 10 ticks; never crits. |
| Bleeding | Four Physical Slash damage each 15 ticks. |
| Poisoned | Four Venom damage per stack each 15 ticks, at most three stacks. |
| Chilled | Movement slows to two thirds; three applications consume Chilled and attempt Frozen. |
| Frozen | Movement and combat actions stop for 30 ticks. |
| Shocked | A direct Storm hit causes one bounded additional chain hit; chain damage cannot recurse. |
| Staggered | Cancels windup and blocks actions for its duration. |
| Cursed | The affected source deals 20% less increased damage. |
| Terrified | Ordinary AI cancels its action and flees; player combat actions are blocked. |
| Marked | Shadow Execution gains its consumption bonus, then removes the mark. |
| Vulnerable | Increases damage taken by 25%. |
| Rooted | Prevents displacement while leaving nonmovement actions usable. |

Poison detonation consumes stacks for additional Venom damage; Cauterize benefits from attributed Burning; Chilled transforms into Frozen; projectile branching spreads authored statuses. Bosses, ritual anchors, and bells resist Staggered, Frozen, Terrified, and Rooted. This preserves their telegraphs and prevents repeated hard-control locks. Status lists are bounded at 32 entries and carry source, owner, source generation, origin skill, root action, depth, tick schedule, and expiration through saves.

## Corpses, summons, and advanced fragments

All corpse-consuming effects share a saved set of consumed actor IDs. A corpse can be claimed once by harvesting, raising, a Godwrought revenant, or Heart of Serath. Bell resurrection skips consumed corpses. Ritual anchors and broken bells do not produce usable bodies. Ordering follows command order and the deterministic damage queue; an automatic death trigger can claim a body before the next player command. Summons retain player ownership, generation, target command, and expiration; eight concurrent summons and two descendant generations remain the limits.

Heart of Vael accumulates 25 Heat per critical hit. At 100, the next heavy skill consumes Heat and releases one Overheated ignition burst. Serath's Last Memory captures a single temporary, curated Storm Echo on an elite kill; `CastEcho` spends it once within 450 ticks. It never executes an arbitrary enemy script. Orrun's Knuckle stores up to 90 ticks of stationary defense/seismic power and releases a nearby stagger wave when the player starts moving. Nhal's Shadowed Tendon fills anatomy Legs and grants ten barrier on dodge. Fragment replacement removes its owned effects and clears stored heat, echo, or seismic state as applicable.

`ApplyAnatomy` is the validated runtime projection used by Expedition and Production. It validates the complete replacement before changing anything and routes every removed fragment through the same cleanup as a sandbox unequip. Charges can cross room boundaries while their owning fragment remains equipped; echo expiration continues on the simulation clock. Removing the owner clears those charges immediately, including a previously armed Overheated action. Restore rejects stored heat, echo, or seismic charge without the matching active fragment.

Ashcleaver's Serath branch now raises burning victims as temporary flaming revenants, respecting corpse arbitration and generation limits. Orrun's branch replaces awakened flame projectiles with a molten seismic area hit that ignites enemies. Their previously implemented small healing/defense bonuses remain. All Godwrought effects require the actual equipped Ashcleaver instance; permanent advancement and evolution choices remain in Production/Adventure.

## Manifestations and equipment

Both threshold choices operate concurrently through `CombatBuildModifiers.Manifestation` and `SecondaryManifestation`.

- Burning Blood triggers from incoming physical health damage, releasing nearby fire. Outgoing/reflected hits cannot retrigger it. Potions heal 25% less.
- Stone Memory escalates resistance against repeated identical attacks while making dodge recover 25% slower.
- Whispering Shadow reveals concealed enemies. Its false silhouettes are separate decorative positions, never targetable actors, and are omitted near the player or actual warning/hazard zones.
- Voracious Renewal explicitly consumes a corpse to heal and add temporary maximum life, up to 60 for 180 ticks. Ordinary healing is reduced near unconsumed corpses; corpse-consumption healing itself is exempt.

Purification halves the associated complication: Eye of Vael for potion reduction, Orrun's Bone for dodge recovery, Nerve of Ilyra for ordinary healing, and Heart of Serath for false-silhouette duration. Purification changes neither primary fragment effects nor Resonance.

All twelve equipment slots are validated and contribute through the same stat path. Compatible ring slots allow one item to move between Ring1 and Ring2; the same instance cannot occupy both. Two-handed weapons exclude OffHand and enforce discipline eligibility. Godwrought items stay out of ordinary randomized loot; Echo Ring is Legendary. Other natural drops use the existing six-way base roll: one Common, two Tempered, two Rare, and one Relic outcome. Item selection and base roll still consume exactly two values from the loot RNG stream; permanent affixes use the separate instance-seeded policy. Equipment Legs is separate from anatomy Legs.

## Reproducibility and scope

`CombatProductionSmoke.Commands` is a shared deterministic baseline driver for comparative simulations. It understands heat versus spending, basic targeting, potions, and warning avoidance. It is deliberately a baseline policy, not proof of optimal balance or external player satisfaction. Tests cover all five classes defeating the same boss with replay/restore continuity, resource differences, corpse arbitration, projectile mutations, status transformations and boss resistance, advanced fragments, both Manifestation tiers, equipment compatibility, charge release, and evolved Godwrought effects.

The exact Phase 2 combat content is retained in `fixtures/combat-phase2.json`; its canonical identity still reads alongside the frozen hub save. Migration validates the raw old save first, then promotes to current structures; arbitrary old in-flight state is not silently treated as a current replay. New defaults do not rewrite or pretend to preserve an old state hash.

The feature set provides playable representative rules for each discipline and a composable content vocabulary. Production art, animation, localized presentation quality, cross-platform determinism, external playtests, and the campaign's full enemy/content inventory remain separate acceptance work.
