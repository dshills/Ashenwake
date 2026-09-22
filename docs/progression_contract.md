# Production progression contract

`Ashenwake.Core.Progression` provides permanent systems for the production foundation. `ProgressionContent` validates and privately copies definitions; `ProgressionSession` owns a character snapshot and a distinct shared local profile snapshot. The `ProductionSession` adapter now connects these rules to actual expedition combat, the production client, atomic character archives, and a separate local profile ledger. See `production_runtime.md` for that boundary. Production art, complete campaign breadth, and human playtest acceptance remain separate gates.

## Application integration

Create with `ProgressionSession.Create(content, discipline, characterId, profile)` or validate an existing `ProgressionSnapshot` with `Restore`. `Capture` and `View` return detached data. Serialize the snapshot inside the application's atomic character save envelope, including its `ContentHash`. Reject missing content instead of silently dropping owned equipment or profile unlocks. A different character may start with a copied `LocalProfileState`, carrying discoveries/unlocks but none of the first character's inventory, XP, mastery, or quests.

Every permanent command takes a caller-assigned operation ID. The receipt records the hash of the complete typed operation payload. Repeating the same key and payload succeeds without repeating its effects; changing the payload under that key fails. Invalid operations never consume materials, items, or an operation key. Keys must identify authoritative reward events, not repeated button clicks; receipt persistence and application saving must share the same transaction as rewards. XP and materials saturate at their declared caps, so a full purse cannot block an authoritative boss clear. The bounded receipt ledger is never silently evicted; capacity exhaustion requires a declared migration/archive policy.

## Progression and equipment

XP thresholds are `100 × level × (level − 1) / 2` by default, with a level cap of 50. Each level after the first grants one point in Offense, Defense, or Resource. Mastery reaches its first behavioral-unlock eligibility at 100 and caps at 1,000. Ultimates unlock at level ten. Retraining begins at level five, costs five common materials, retains learned disciplines/mastery, and unequips weapons incompatible with the new discipline. Respec refunds all passive points for five materials without erasing mastery. These remain tunable prototype values; the [campaign pacing schedule](campaign_pacing.md) distributes 5,150 XP across its fifteen main encounters.

The five discipline definitions name Vanguard/Momentum, Veilwalker/Exposure, Arcanist/Instability, Gravecaller/Remains, and Warden/Adaptation. This module owns permanent identity and unlocks. The combat simulation owns resource generation, spending, cap/decay, summons, corpse arbitration, and adaptation timing.

Permanent item IDs are monotonically increasing `long` values. The twelve `EquipmentSlot` spellings are Head, Shoulders, Chest, Gloves, Belt, Legs, Boots, Amulet, Ring1, Ring2, MainHand, and OffHand. Anatomy Legs remains a separate subsystem. One item instance cannot occupy two slots; a two-handed MainHand weapon conflicts with OffHand. Weapon discipline requirements are validated both on equip and restore. All equipped affix values feed the detached `View.Stats` dictionary; combat adapters consume this view when applying/rebuilding the loadout.

All six rarities enforce affix counts. New loot uses `ProgressionLoot.RollAffixes`: a pure world-seed/item-ID roll with authored affix weights, at most one bounded selection per allowed affix, and no consumption of the combat RNG stream. Imported historic items retain their prior base rolls and progression. Advanced affixes require Relic or greater, with explicit eligible slots, min/max ranges, weights, and incompatible properties. Fork/Chain exclusions apply to the complete equipped loadout as well as a single item. Definitions carrying special behaviors require Legendary or Godwrought rarity. Core content validation checks objective cycles, reference integrity, and missing localization keys before runtime.

## Crafting and hub progression

`CompleteObjective` advances a declarative prerequisite graph and atomically unlocks its specialist service, journal entry, and derived Greyhaven rebuild stage. The baseline chain covers Mara, Torren, Sister Cael, Oris, Kesh, and the restored workshops.

| Service | Transaction |
| --- | --- |
| Tempering | Improve a present conventional affix within its declared maximum. Ashcleaver retains five +2 damage steps, creating its damage affix on the first step. |
| Rebinding | Replace one existing affix with an eligible, compatible affix at its minimum roll. |
| Engraving | Fill one empty engraving slot with a learned, slot-compatible property. |
| Extraction | With permanent-destruction confirmation, destroy a Legendary, remove it from equipment, and preserve its special property. |
| Divine Grafting | With permanent-choice confirmation, evolve an awakened Ashcleaver into Serath or Orrun exactly once. |
| Purification | Pay to reduce one known fragment's complication; duplicate purification is rejected. |

`RecordGodwroughtKill` requires the Godwrought Ashcleaver instance to be equipped and uses the authoritative burning-kill event's unique operation ID. Its canonical awakening threshold is 1,000. The maintained Phase 2 migration transfers the slice's string instance IDs/history to permanent long item IDs once; permanent inventory becomes the sole owner of item progress. The adventure Godwrought state is a checked projection; burning kills observed by the expedition are reconciled into canonical inventory before checkpointing. Save migration must preserve burning count, selected branch, and committed reward history.

Purification records an owned fragment's policy flag; the production combat adapter applies the reduced complication. It does not silently grant the fragment. Likewise engravings, special properties, mastery, and passives expose state for explicit combat application. None of these logical commands directly inflict damage.

## Validation evidence

`ProgressionTests` exercises all twelve slots, item/stat removal, two-handed conflicts, rarity and incompatible affix rejection, level/mastery/respec/retraining, profile separation, all six crafting services, irreversible extraction/grafting, payload-bound retries after restore, failed-transaction rollback, objective prerequisites, localization references, and quest-cycle rejection.
