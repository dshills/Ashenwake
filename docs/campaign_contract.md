# Five-act campaign and exploration contract

`CampaignRuntimeSession` now joins the five-act narrative ledger to actual combat, canonical production progression, the Godot campaign client, atomic saves, and deterministic replay. All fifteen critical-path encounters and three optional explorations are playable with all five disciplines. This remains a greybox campaign: it does not establish production-quality environments, animation, voice acting, cinematic delivery, external playtest acceptance, or release certification.

## Integration boundary

`CampaignSession.Create(content)`, `Capture()`, and `Restore(content,state)` own logical campaign progression. `EnterAct(1..5)` enters an unlocked act, `ReturnToHub()` returns to Greyhaven, `Choose(choiceId,outcomeId)` commits an available consequence, and `CompleteEncounter(encounterId)` advances an encounter only when the authoritative combat coordinator reports success. That method does not simulate victory itself.

`CampaignView.EncounterId` identifies the pending encounter. Its content definition supplies enemy IDs, named mechanic, explicit counterplay, elite modifiers, reward amounts, and rescued resident. The first act's Bell Saint contract maps to the established three-phase adventure encounter. Later authored bosses—Rootheart, Furnace Spindle, Covenant Warden, and Breach Heart—are implemented greybox designs rather than names taken from the source specification. Root/anchor targets, visible hazards, guarded windows, elite behavior, and boss phase state are actual combat actors and rules. The coordinator requires every living hostile to be defeated before recording victory.

Successful completions atomically update the encounter, resident, act, and reward-history state. `CampaignResult.Experience` and `.Materials` are the newly earned amounts, zero on non-reward actions. `CampaignRuntimeSession` transfers each result into `ProductionSession` with a payload-bound encounter or exploration receipt. Story, production ownership, and the active arena share one checksummed campaign snapshot. Restore checks that completion and permanent reward receipts agree, including the reward payload. Repeated completion cannot grant a second reward. Validation recomputes cumulative reward totals from completed content and rejects inconsistent saves.

## Authored critical path

| Act | Encounters | Choice and payoff |
| --- | --- | --- |
| Grey March | Road rescue, monastery, Bell Saint | Choose the fragment's custodian; Mara's implantation and false-history evidence establish the mystery. |
| Verdant Maw | Living ruins, plague village, Rootheart | Permit voluntary transformation or conventional quarantine/care; later envoys or clinic needs reach Greyhaven. |
| Cinder Reach | Full Cinder Pack, extraction floor, Furnace Spindle | Keep essential extraction or stop the shafts; medicine/heat benefits and seal damage arrive in Act V. |
| Shattered Spine | Bone causeway, contract hall, Covenant Warden | Enforce the binding or free the debtors; the divine bodies are revealed as seals, with later guards or refugees. |
| Hollow Night | Repeating rooms, identity memory, Breach Heart | Share the truth or guard a transition; Nhal never fell, and stabilizing the breach begins a common endgame. |

Every act requires its central choice before the final confrontation. The choices use explicit faction alliances and world flags, never a single morality score. Immediate flags and delayed consequences derive from the committed outcome and highest visited act. Older-region revisits cannot roll back consequences. The ending records alliances, known surviving residents, Greyhaven's material condition, and restrained versus transformed relationship to Resonance. Both authored future choices unlock Fractures.

Rescues populate Greyhaven with Mara, Torren, Sister Cael, Oris, Kesh, and the Pale Child. `ViewForResonance(value)` generates reactions to visible transformation, medicine supplies, a hard winter, Ilyran envoys, freed debtors, and the discovery that mountains were divine seals. Final dialogue editing, acting, presentation, and surviving-leader branch breadth remain content production work.

## Scoped exploration

`BeginExploration(eventId)` installs exactly one temporary context for its authored act. `View.ExplorationRules` exposes population/hazard/fragment/reward rules for the encounter adapter. The combat adapter installs and removes the actual scoped rules when that context starts or ends.

- The Burning Rain (`event.resonance_storm`) lasts 900 simulation ticks; its combat rule expires overcharge, Stormbound effects, and announced fire hazards at the same lifetime boundary. If the encounter remains unfinished, the coordinator returns to the prior cleared campaign context without awarding its unique materials. After victory, visible loot can be collected before the completed event returns.
- The Promise Before Stone (`event.divine_memory`) uses a bounded reversed-fault memory encounter and returns to the recorded act/anchor context.
- The Antler That Walks (`event.wake_hunt`) requires shed bark, reversed tracks, then the heartwood nest before the named hunt encounter can award its unique reward.

Completion, death, hub return, and region travel all clear the temporary context and its rules. Story discoveries and successfully committed rewards persist. Completed exploration cannot be repeated for its unique reward. Death restores the current campaign anchor; combat resets its unfinished encounter while completed quest outcomes remain intact.

## Automated evidence

`CampaignTests` traverses all 64 authored choice combinations, restores state at every act boundary, and verifies common endgame access. Separate cases cover delayed consequences, reactive Greyhaven dialogue, ordered hunt clues, storm expiry, memory return, context cleanup on death/travel, duplicate reward prevention, skipped-encounter/choice rejection, and incompatible/corrupt state validation. `CampaignRuntimeTests` adds all five complete actual-combat routes, the level-ten final-boss gate, victory-only reward receipts, all optional encounters, clue proximity/order, timed storm expiry under live enemy AI, exploration/death cleanup, completed-region revisits, profile unlocks, checkpoint restore/replay parity, corrupt backup recovery, nested future-version preservation, and the actual Phase 3 hub import fixture. Human understanding, perceived difficulty, fun, and regional presentation still require external acceptance evidence.


## Runtime commands and permanent ownership

Create with the composed `CampaignCombatContent.Parse(baseCombatJson, campaignCombatJson).CombatJson`, the adventure/progression catalogs, and the campaign catalog. `CampaignRuntimeSession.Create` starts a fresh discipline in Greyhaven; `Restore` validates the complete checkpoint. `Execute` and convenience methods cover ticks, entering an act, advancing a cleared encounter, returning to Greyhaven, committing a choice, beginning/leaving an exploration, following a nearby hunt clue, and invoking permanent production services.

`Production` remains the sole permanent owner of items, XP, mastery, currency, equipment, mutations, Godwrought history, and profile metadata. Its Greyhaven combat body is dormant while the campaign arena runs. Actual campaign ability events, pickups, and first-time burning deaths reconcile into that owner; already-consumed/resurrected bodies cannot award another permanent awakening kill; the arena receives a checked build projection. Both Manifestation tiers, purified fragments, current equipment/affixes, and Godwrought evolution affect the same combat rules as the production slice. Rooting, hazard, summon, echo, and elite state remain encounter-local.

Rescued residents appear in Greyhaven and unlock their existing production services. Markers and commands both enforce resident presence. Equipment/skill changes use the existing hub proximity rules. A new region encounter or death return starts from its authored checkpoint with health and consumables restored; completed encounters, choices, and rewards remain committed. Players may collect visible loot or explicitly continue/start exploration while leaving those uncollected drops behind. The UI labels that tradeoff and the runtime emits the number left behind. A full 512-item combat inventory never blocks forward progress. Explicitly finishing an exploration after all hostiles are defeated (and hunt clues completed) commits its unique reward even when drops remain; leaving an unfinished exploration remains abandonment. Returning to the hub can abandon unfinished enemies or ground loot, but does not repeat already-earned campaign rewards on revisit.

Campaign XP totals 5,150. Recovering Act V's identity memory grants 1,200 XP so a fresh character reaches level ten before the final boss and can use its ultimate. The previous 4,425-XP total left that unlock permanently unreachable on the critical path. These are explicit prototype pacing values, not established full-game leveling balance.

## Archives and historic import

`CampaignRuntimeSaveStore` uses a combined snapshot with canonical content identities, cross-catalog enemy/elite/lifetime references, raw header checks before typed parsing, checksums, exclusive write leases, atomic replacement, and a validated prior backup. Unsupported schema/rules/catalog identities preserve the source instead of being treated as corruption. Character paths cannot overwrite the reserved local profile ledger. Profile union uses the same separate monotonic metadata transaction as production; loading a merged profile establishes a new replay origin.

`CampaignRuntimeReplayRunner` checks state and event hashes for a bounded 1,800-command window. The CLI validates rolling windows while traversing the longer campaign. `CampaignRuntimeSmoke` uses the same player commands, geometry-aware navigation, actual loot pickup, explicit choices, ordered clue interactions, and combat warning avoidance; it does not directly complete encounters or grant rewards.

`CampaignRuntimeMigration.ImportPhaseThree` verifies the original raw production checksum, matching original catalogs, logical state, and safe Greyhaven boundary. It returns a new campaign character with retained permanent power, items/base rolls, mastery, currency, discoveries, and existing service unlocks; campaign encounters and choices begin fresh. The source save/profile are never written. In-flight expeditions must return to the previous build's hub before import. Maintained files `fixtures/phase3-production-hub.json`, `combat-phase3.json`, `adventure-phase3.json`, and `progression-phase3.json` come from the actual frozen Phase 3 validation build; their byte hashes are recorded in `phase3-migration-manifest.json`, checked by a focused test, and must not be regenerated to make migration tests pass.
