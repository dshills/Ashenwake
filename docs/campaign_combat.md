# Campaign combat runtime

The campaign is playable greybox content. `content/campaign-combat.json` authors the 15 required fights and three exploration fights named by `content/campaign.json`. It adds 20 enemy definitions to the existing regional Cinder pack and maps regional enemies to bounded simulation behaviors. The current scenes reuse the sandbox geometry, placeholder silhouettes and procedural presentation. They do not constitute finished regional art, animation, music, voice acting, or external playtest approval.

`CampaignCombatContent.Parse(baseCombatJson, campaignCombatJson)` produces a validated `CombatJson` catalog, its `EncounterIds`, and a `CreateEncounter(id, seed, previous, restoreAtAnchor)` convenience method. The existing `CombatSession.CreateEncounter` accepts the composed catalog directly. Skills, resource loops, statuses, equipment, fragments, permanent-stat projection, damage attribution and loot use the same rules as Production. Base combat content and inactive snapshots omit the optional campaign field, preserving previous identities.

The campaign coordinator controls ownership, travel, rewards and anchors. Combat controls victory: an encounter is clear only when no living enemy remains. Boss phase transitions restore the boss to its next phase before a final death event, add actual enemies, and cannot be completed through a narrative flag. All phases carry player wounds and resources; only hub/anchor creation refills health and potions.

## Regional encounters

| Region | Fights | Actual mechanics |
| --- | --- | --- |
| Grey March | Road, Monastery, Bell Saint | Ghoul pressure, priest healing/fire warnings, sonic lanes, memory arrows, Mirrorborn copies; Bell chain/sonic attacks, then ritual anchors and corpse resurrection, then beast rushes and independent bells. |
| Verdant Maw | Living Ruins, Plague Village, Rootheart | Mixed vine/swarm/carrier formations, interruptible spore mending, rooting/poison circles and quarantine blooms; three targetable feeding roots initially protect Rootheart, severing one exposes its mobile core, and a second phase continues root/spore pressure. |
| Cinder Reach | Cinder Pack, Extraction Floor, Furnace Spindle | Priest/brute formations, Emberling death bursts, alternating conveyor heat and interruptible Heat Tender bellows that briefly empower nearby sentinels; Furnace alternates vent orientation with a delayed slag circle and guarded/exposed core windows. |
| Shattered Spine | Bone Causeway, Contract Hall, Covenant Warden | Three ordered seismic lanes, visible oath zones, giants and keepers; Covenant combines a local oath mark with a delayed fault, then exposes itself during recovery. |
| Hollow Night | Repeating Rooms, Identity Memory, Breach Heart | Concealed shadows, memory arrows, delayed causal warnings and Riftborn breaches; three seal channels initially protect the Heart, one broken channel exposes it, and later phases add real echoes and returning attacks. |

Boss roots, seal channels and anchors remain targetable by every damage family. Furnace and Covenant gain 60 percentage points of defense while their announced attack resolves, then lose that bonus during recovery. Ordinary enemy warnings can be interrupted by hard control; bosses retain the established hard-control resistance. Boss phase adds use collision-safe spawn alternatives so they cannot trap the player by appearing at the same position.

The [midgame combat-depth contract](midgame_combat_depth.md) defines the two support channels, their recipient rules and bounded timers. Support warnings do no damage and are excluded from the diagnostic policy's damaging-area avoidance. The new actor-view countdowns report temporary forge power and Rootheart/Furnace recovery without advancing simulation or granting a new damage bonus.

## Warning contract and scoped rules

`CombatView.CampaignHazards` contains circles and bounded line segments, each with a source, effect identity, radius and ticks until impact. Line radius is its half-width. Damage uses the same geometry displayed by the client; the target position is fixed when announced. At most 32 campaign warnings can exist, and impacts enter the existing 256-effects-per-tick queue. Killing or interrupting an ordinary source cancels its pending campaign warnings. Boss phase transitions discard prior-phase warnings. `BossPhase`, `CampaignRule`, `SuppressedFragmentId`, and actor `EliteModifiers` are read-only presentation data.

- Burning Rain lasts 900 ticks from entry. Its population includes Stormbound; announced fire lanes accompany 45-tick overcharge windows starting after 45 ticks and repeating every 150 ticks. During those windows player-owned nested hits, including fragment-derived damage, gain 25% increased damage. Expiration removes the storm's rule warnings, overcharge, pending links and Stormbound modifier; leaving clears the entire scope.
- The Promise Before Stone reverses the three announced fault lanes. Timing and lane order are saved and restored. The coordinator owns the return anchor and reward.
- The Antler That Walks has a brief protected burrow followed by an announced rooting charge and an exposed recovery. The coordinator owns its prerequisite clues and unique reward.

Creating any other encounter discards the previous campaign state, hazards, suppression, copies and population. Permanent equipment, anatomy and resource state use normal room-transfer policy.

## Elite behavior and bounds

| Modifier | Behavior and counterplay |
| --- | --- |
| Mirrorborn | Announces up to two short-lived copies once per elite; four copies maximum per encounter. Copies cannot create further copies, produce corpses, grant loot, or advance Godwrought kills. |
| Gravewake | Announces one resurrection of a nearby unclaimed ordinary corpse per elite. The body is consumed for arbitration and cannot yield another reward or be resurrected repeatedly. Kill/interrupt the caster or consume the corpse first. |
| Stormbound | Announces a damaging lightning segment between nearby living enemies. Step outside the segment or interrupt its source. |
| Devourer | Announces consumption of a nearby living ordinary ally to heal and gain bounded damage. Two consumptions maximum; kill the meal or interrupt the caster. Consumed allies grant no kill reward. |
| Null | Announces a small suppression zone. A hit suppresses one equipped fragment for 75 ticks, followed by at least 150 ticks without suppression. Dodging or leaving the circle avoids it. Stored heat/echo remains owned but cannot be activated while its owner is suppressed. |
| Hunter | Moves 25% faster when pursuing and resists hard control. It still commits attacks and retains their dodge/recovery windows. |
| Martyr | On its final death, grants nearby surviving allies a small barrier and 15% increased damage, capped at two stacks. Isolate it or remove supporting enemies first. |
| Riftborn | Announces the destination of a void breach, then teleports with a recovery period. The destination cannot overlap a living actor. |
| Dirgebound | The Act I monastery guard chants for 36 ticks inside a fixed three-meter circle. Nearby allies in line of sight gain barrier up to 24; the caster, bosses, mechanisms and copies are excluded. Kill/interrupt the caster or draw allies out before it resolves. Repeated chants do not stack the ward. |

Phase 4 permits at most two modifiers per actor. It rejects Mirrorborn+Gravewake, Null+Hunter and Devourer+Martyr. This is the campaign's current compatibility contract; future endgame inheritance must use an explicitly reconciled rule set rather than assuming a larger modifier allowance.

Dirgebound is an authored Act I addition. The original eight-trait Fracture pool remains fixed so seeded expedition manifests and historical replays do not change. See [Act I combat depth](opening_combat_depth.md) for formations, readable support warnings and first-upgrade guidance.

Campaign snapshots validate source ownership, elite counts, timers, modifier compatibility, warning geometry, budgets, counters and object identities. Timed copies cannot become eligible corpses. The damage queue and existing summon, projectile, status and proc-depth limits remain in force.

## Verification

`CampaignCombatTests` compares actual events and state hashes through all 18 fights, restoring every 75 ticks. The early Bell Saint is exercised with all five disciplines at level two, without an unlocked ultimate. Focused tests cover every elite behavior, corpse arbitration, copy limits, warning evasion, guarded boss damage, storm overcharge/expiration, room cleanup and malformed saves. `CampaignCombatSmoke` is a deterministic input policy with geometry-aware navigation and warning avoidance; it uses actual movement, attacks, potions and class abilities and does not grant victory or adjust combat stats.

These checks establish playable deterministic mechanics. They do not establish encounter pacing across human skill levels, audiovisual quality, accessibility acceptance, or final campaign balance; those remain review and playtest work.
