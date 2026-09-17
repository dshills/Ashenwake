# Adventure state contract

The Phase 2 logical layer is `Ashenwake.Core.Adventure`. It owns travel, encounters, rewards, anatomy choices, discoveries, and per-instance Godwrought progress. Combat remains authoritative for damage, enemy deaths, ritual-object destruction, and encounter completion. A client must dispatch `EncounterCompleted` only after its corresponding authoritative combat encounter is cleared. An interaction request does not itself prove combat victory.

## Content and public API

`AdventureContent.Parse(json)` validates `content/adventure.json`, copies the content privately, and assigns a SHA-256 content identity. `Capture()` returns a detached copy. `Default()` contains the matching development baseline; production uses the explicit JSON bundle.

`AdventureSession.Create(content, seed, characterId)` creates the hub state. `Restore(content, state)` validates and copies logical state. `Capture()` and `View` return detached data. Every action returns `AdventureResult(Success, Reason, Events)`; a failed action leaves the complete state unchanged. Successful actions mutate a private candidate and validate it before replacing the live state.

The route and action identifiers are:

| Action | ID / contract |
| --- | --- |
| Start / return dialogue | `Interact("npc.mara")` in `room.greyhaven` |
| First room | `EnterRoom("room.ossuary")`, clear `encounter.ossuary` |
| Anchor room | `EnterRoom("room.cloister")`, clear `encounter.cloister` |
| Boss | `EnterRoom("room.bell_sanctum")` |
| Chains and shockwaves | clear `bell_saint.1`; enters phase 2 |
| Resurrection ritual | destroy `ritual.anchor_left` and `ritual.anchor_right`, then clear `bell_saint.2` |
| Released creature / independent bells | clear `bell_saint.3`; atomically grants victory and rewards |
| Return | `EnterRoom("room.greyhaven")`; speak with Mara |
| Replay | `Interact("dungeon.replay")`; begins a new expedition after a completed run |

`View.EncounterId` identifies the currently pending combat encounter. Boss phase 0 means inactive, 1–3 means active, and 4 means defeated. Phase two cannot complete while ritual anchors remain. Leaving an unfinished boss resets all phases and anchors. `PlayerDied()` returns to the latest anchor, resets its nearby encounter and an unfinished boss, and removes temporary Ashcleaver stacks; earned rewards and permanent progression survive. Cleared boss rewards cannot be repeated by death, save/load, or duplicate completion calls. Loot chance derives only from character seed plus expedition, so death cannot reroll it.

The phase contract is intended for a greybox playable encounter. This state layer does not provide the Bell Saint's combat attacks, production animation, environment art, audio, or qualitative playtest acceptance.

## Build transactions

All six anatomy slots are recognized independently of equipment slots. `InstallFragment(slot, fragmentId)` requires owned content in its allowed slot; passing null removes it without cost. Slots are changed at the hub. Tags discover `concordance.funeral_flame` when Flame and Death coexist; discovery persists after removing a fragment.

Resonance is derived from installed fragments. `SelectManifestation(id)` chooses one manifestation per reached threshold at the hub. Choices are reversible and automatically suppressed below their threshold, preserving low-Resonance play. The view exposes active manifestation IDs and a matching Mara reaction. Combat applies the definition's benefit and complication; the logical layer never grants damage or healing itself.

`RecordBurningKill(sequence, instanceId)` is called from a credited authoritative burning-enemy kill with the equipped Ashcleaver instance. Sequences must increase for that item across the character lifetime, including save/load and room resets. Duplicate/out-of-order events do not count. The canonical awakening is exactly 1,000 kills; no development override changes production data. Five temporary stacks enable awakened flame waves; `ElapseCombatTicks` expires them after 150 ticks without another credited kill. Combat applies stack attack-speed, flame-wave, and evolved effects.

`Temper(instanceId)` atomically deducts materials and raises its tier (maximum five). `Graft(instanceId, "Serath" | "Orrun", confirmPermanentChoice)` requires awakening, the lineage fragment, 20 materials, and explicit confirmation. The selected branch is permanent and saved. Fragments establish lineage knowledge and are retained; grafting consumes the materials. The branches represent flaming revenants and molten seismic waves respectively.

## Save boundary

`AdventureSaveStore` schema 2 stores the whole logical transaction in one checksum-protected envelope tied to the immutable content hash. It validates before writing, flushes a unique temporary file, and replaces the primary atomically. The prior valid primary becomes `.bak`; a corrupt primary cannot overwrite a valid backup. Newer/incompatible primary files are rejected without falling back or overwriting. Unknown content IDs are rejected and preserved for their original bundle; no silent removal policy exists.

Schema 1 is the maintained hub-only legacy fixture (seed, materials, accepted quest). Its migration creates the modern inventory, hub/checkpoint state, and quest journal before normal validation. The test fixture is an exact legacy JSON object in `AdventureTests.SaveRoundTripCorruptionRecoveryMigrationAndNewerVersionProtection`.

Combat checkpoints and logical adventure state must be saved under a shared application transaction when the application promises mid-combat resume. Saving only `AdventureSaveStore` deliberately resumes logical state and requires reconstruction/reset of the active combat encounter; it does not snapshot combat health, projectiles, or cooldowns. A combined application envelope is the integration boundary for exact mid-combat resume.
