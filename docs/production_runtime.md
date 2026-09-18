# Production runtime and persistence boundary

`ProductionSession` connects the permanent progression policy to the actual `ExpeditionSession` and deterministic combat simulation. The Godot production scene uses these same commands as the headless smoke and replay tools. It is a playable systems foundation with representative content; it does not establish completed campaign art, external playtest acceptance, localization coverage, or release certification.

## One permanent owner

`ProgressionSnapshot.Character` owns materials, experience, skill mastery, mutation selections, passives, equipment, every item instance, Godwrought awakening/evolution, service objectives, and crafting receipts. Stable item identities use monotonically increasing `long` values. The adventure's Godwrought string identity is retained as a migration alias, never as a second independently spendable item.

The expedition owns room/quest progression, encounter actors, RNG state, temporary effects, deaths/anchors, installed anatomy, and Manifestation choices. Its materials, Godwrought records, equipped items, mutations, and combat progression bonuses are validated projections of the permanent owner. Restore rejects disagreement. All Godwrought stash entries receive permanent identities even when combat's 512-item projection is full; the bounded permanent inventory supports 10,000 Godwrought entries plus 512 other entries. Equipment is included before other stash items when building that projection.

Actual player-owned ability-start events award mastery. Actual encounter transitions award XP and materials with payload-bound receipts containing expedition and encounter identity. Clearing Ossuary and Cloister yields 150 XP each; the first two Bell Saint phases yield 100 each; the final phase yields 300. The five receipts grant 800 XP in one expedition. A currency cap saturates the reward without preventing the encounter transition. Visiting all four authored locations unlocks profile memory cartography. UI commands cannot invoke arbitrary XP or item grants through the production coordinator.

## Commands and gameplay gates

`Create`, `Restore`, `Capture`, `CaptureReplay`, `Execute`, and `Step` are the shared runtime boundary. Convenience methods cover travel, interaction, fragment installation, Manifestations, equipment, six crafting services, passive allocation/respec, retraining, and mutations. `View`, `ProgressionView`, `Interactions`, `WorldEvents`, and `Combat.View` are presentation data.

Permanent changes require Greyhaven and proximity to the relevant specialist. Mara accepts the expedition quest and opens anatomy research. Torren and Cael require the Ossuary/Cloister discoveries; Oris requires Bell Saint victory; Kesh requires returning to Mara and Torren's prerequisite. The workshop investment requires the rescued specialists and costs 15 materials. Quest definitions validate prerequisites, services, journal references, and cycles.

All twelve equipment slots use a single ownership map. Rings support their declared compatible positions and one instance can occupy only one slot. Two-handed/off-hand conflicts and discipline restrictions are checked before spending or equipping. Advanced Fork and Chain affixes are mutually exclusive across the complete loadout. Affix/passive values project separately from retained item base rolls, avoiding double-counted damage and armor. Both active Manifestation tiers and owned purification choices reach combat.

Mastery unlocks the active skill's authored mutations at 100; mastery caps at 1,000. Level ten unlocks the discipline's ultimate. Level five permits paid retraining, preserving learned mastery while clearing incompatible active mutations/equipment. Passive respec preserves mastery. These balance values are explicit prototype policy in `progression.json`.

## Transactions, archives, and profiles

Each service validates a candidate before committing its cost, receipt, equipment effects, and resulting item. A repeated crafting operation ID and identical payload does not spend twice; a different payload under that ID fails. Non-tick production actions retain rollback state across the joined subsystems. Ordinary simulation ticks do not restore or simulate a second full expedition; permanent reconciliation runs when authoritative loot, mastery, or world events occur. The production replay owns operation recording, so the wrapped expedition does not also retain duplicate frames.

`ProductionSaveStore` stores one checksummed combined character/expedition snapshot and a validated previous backup using atomic replacement. Rules or catalog mismatches require an explicit migration and preserve the incompatible file. Replays store a bounded 1,800-command window with its own initial snapshot and compare both state and event hashes.

`LocalProfileStore` is a distinct local ledger beside character files. It validates known unlock/discovery IDs, holds an exclusive merge lease, rejects different profile identities, and unions metadata from each character. New characters inherit this metadata without importing XP, currency, equipment, quests, or mastery. Corrupt primary data can recover a validated backup; newer schemas/content are preserved. A profile merge and a character checkpoint are separate atomic replacements: monotonic profile metadata may persist if a subsequent character write fails. This is deliberately documented rather than described as a two-file atomic transaction.

The maintained Phase 2 migration verifies the original envelope checksum and registered old content identity before promoting fields. It accepts the declared Greyhaven boundary, retains item base rolls, materials, discoveries, anatomy, chosen mutations, and Godwrought history, and never silently starts a replacement character. Historic string IDs become stable permanent IDs once; selected historic mutations receive the corresponding mastery eligibility. Newly gated ultimates follow production level policy.

## Automated evidence

`ProductionTests` exercises all five fresh disciplines, actual dungeon/boss rewards at capped wealth, replay parity, all six live crafting service projections, payload-bound retries, mastery/mutation/passive/retraining persistence, corrupted projections, and metadata sharing across new characters. `ProgressionTests` checks all twelve equipment slots and 3,000 deterministic weighted affix rolls with rarity/slot/exclusion validation. The client production smoke additionally traverses the specialists and runs each discipline against actual encounter actors. Archive and migration tests maintain actual previous-build bytes separately from newly generated test state.
