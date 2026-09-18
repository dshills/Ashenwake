# Endgame combat adapter

The Phase 5 adapter runs Fractures and God Hunts in `Ashenwake.Core`, using the existing fixed-tick skills, damage rules, statuses, projectiles, corpse ownership, elite behaviors and permanent-build projection. Godot displays the authoritative state and sends commands. The environments, bosses and effects remain greybox assets; automated completion does not establish final visual quality or human readability.

## Catalog, manifest and lifetime

Compose the base catalog through `CampaignCombatContent.Parse(baseJson, campaignOverlayJson)`, then call `EndgameCombatContent.Parse(campaignJson, endgameOverlayJson, actualEndgamePolicy)`. The overlay is `content/endgame-combat.json`. The exact endgame policy is embedded in the composed catalog and exposed by `PolicyHash`; the runtime rejects a coordinator policy mismatch. `FromComposed` reconstructs the adapter for saves and replays.

`CreateFractureManifest(sigil, runId)` produces three regional pack rooms followed by the selected divine-family boss. There are three authored spatial variants and three pack variants for each of the five regions. Seeded selection permutes the regional variants, selects actual elite traits and scales pack composition with tier. The boss has supporting enemies so inherited Stormbound, Gravewake, Devourer and Martyr have real interactions. Difficulty does not merely multiply boss health.

The manifest records the exact room order, geometry IDs, room seeds, spawns, elite traits, rules, reward tendency, source identity and inheritance decisions. Restore regenerates this manifest and requires an exact match. All three preceding rooms contribute an inheritance candidate in order. Shared combat validation admits at most two distinct compatible traits; later candidates record `duplicate`, `incompatible` or `cap` rather than disappearing. The shared exclusions are Mirrorborn/Gravewake, Null/Hunter and Devourer/Martyr.

`CreateHuntManifest(huntId, seed, runId)` produces three authored phases. `CreateEncounter(manifest, index, attempt, previous, restoreAtAnchor)` creates a real arena. `CombatSession.Room` and `RoomFor` expose its actual geometry. Player health and potion charges carry between rooms/phases. Entry or a spent-attempt retry can explicitly restore at an anchor. The coordinator controls attempts and advancement; the combat adapter cannot grant permanent rewards.

`CombatView.Endgame.ContextKey` identifies run, room and attempt, so presentation resets effects on a retry while retaining state through an ordinary permanent-build projection. `CombatSnapshot.Endgame` contains versioned rule timers, hazards, mechanism ownership and counters. It is omitted when absent, as are the new catalog and resistance fields; campaign-only identities remain unchanged.

`SealEndgameVictory` requires all hostiles to be dead. It removes hostile projectiles, areas, warnings and player damage statuses while retaining loot and room identity. The runtime can then wait for explicit advancement without killing the player during loot inspection. Transition, retry and hub return clear all endgame effects.

## Executable Fracture rules

| Rule | Actual behavior and bounds |
| --- | --- |
| Fevered Cinders | Burning enemies move 25% faster while approaching, repositioning or fleeing. Teleport and attack destinations retain their authored geometry. |
| Borrowed Relief | Positive player healing schedules a hostile circular echo at the healing position, with a 42-tick warning. Each causal healing event can create one echo, at most eight per encounter. Echo damage cannot recursively heal or create another echo. |
| Scars of the Mighty | Eligible final elite deaths leave warned, repeating Decay hazards until encounter exit. Each actor contributes once, with eight scars maximum. Mirror copies and consumed/reanimated bodies cannot create extra scars. A western approach remains clear. |
| Unequal Shelter | The largest equipped family resistance supplies one quarter of its value as increased damage. The lowest family loses 1,500 basis points and can reach -15%, increasing incoming damage. Ties use stable damage-family order. Physical-family resistance adds to armor; the other families add to their existing resistance defense. The permanent build remains unchanged. |
| Divine Surges | The first 30 ticks of each 120-tick encounter cycle multiply fragment-owned damage and granted fragment barriers by 1.5. Ownership is tracked through statuses and summons; ordinary skill damage and unrelated chained effects receive no bonus. Proc probability, cooldowns and resource generation stay unchanged. |
| Accumulated Memory | The final boss uses the manifest's selected traits from the actual preceding elite rooms, under the shared two-trait compatibility cap. All selected and skipped sources remain inspectable. |

Healing echoes and persistent scars remain mutually excluded by policy. Endgame hazards are capped at 32, alongside the existing 32 campaign/elite warnings. The shared simulation limits remain 160 actors, eight summons, 128 projectiles, 64 areas, 256 queued effects per tick and a causal depth of six.

## God Hunts and counterplay

Each phase has actual targetable actors or positional/interact mechanics. Weak points cannot produce loot, corpses or permanent kill rewards. A false memory can reform only once. `InteractMechanism` validates the player, current room, available mechanism and range.

| Hunt | Phase 1 | Phase 2 | Phase 3 |
| --- | --- | --- | --- |
| The False Vael | Rotating active vents retain an open lane. | Destroy rebuilding limbs to open a vulnerability window; surviving limbs telegraph fire channels. | Marked solar lines can be avoided spatially or blocked inside displayed cooling gaps. |
| Ilyra Reborn in Teeth | A root trail announces a delayed surfacing jaw. | Brood channels shield the body and create bounded reinforcements; destroying them opens the boss. | Seed guards protect the body between visible feeding pauses. |
| The Thousand Memories of Serath | Memory processions leave moving gaps; false bodies carry distinct state markers. | The marked repeating copy controls the shield; false copies reform once. | Ring the silent bell to open vulnerability while crossing the gaps between bell pulses. |
| Orrun Without an Oath | Numbered fault lanes resolve sequentially. | Carry two broken terms to separate plinths to release the contract shield. | Break seals and exploit recovery after a three-strike slam sequence. |
| Optional Nhal reconstruction | Break actual anchors amid absent-space lanes. | Interrupt the marked copy while distinguishing false bodies. | Open the unremembered door through the silent mechanism and follow numbered, reversed-order lanes. |

The client receives warning/active hazard stages, sequence numbers, exact circle/line geometry, weak-point state markers and interaction prompts. Line radius is half-width. Passive cooling gaps and anchor glyphs are displayed even though they do not accept interaction commands.

## Verification and limits

`EndgameCombatTests` covers seeded manifests and inheritance, policy identity, all five disciplines completing every three-phase hunt, four-room encounters, actual haste/healing/scar/overcharge/resistance effects, mechanism immunity, bounded malformed-state rejection, retry cleanup, victory cleanup and save continuation. Input-only regression cases cover a paid area mutation starting at zero resource and a safe generator cast completing before a later enemy warning.

`EndgameCombatDiagnostics.Run(catalog, seed)` supplies the reproducible build matrix used by `aw endgame builds`. It records damage, used skills, inferred damage families, summon creation, entity/effect peaks, continuation checks and hashes. These are explicitly authored isolated level-20 loadouts, separate from the earned campaign-to-endgame CLI routes.

For seed 42, the matrix contains 100 baseline routes: five disciplines, 12/64 Resonance, five regional tier-four Fractures and all five three-phase hunts. Two additional low-Resonance counter-builds swap 22% Fire resistance for 22% Venom resistance without adding stat budget. The measured result is 101/102 successful routes. Every hunt and high-Resonance route succeeds. Unprepared low-Resonance Veilwalker fails the Verdant Maw healing-echo route; its poison-prepared version clears all four rooms with 154 health remaining. This reference route calls for resistance preparation and attention to the hostile healing echoes.

The same run exercises all nine damage families, actual Vanguard Fire conversion, and summon-producing builds. All 102 continuation comparisons match. Observed maxima are ten actors, four live summons, four projectiles, five combined hazards and five queued effects. These measured cases are below the hard limits; they are not a substitute for broader seed coverage, target-hardware profiling or player balance/readability testing. The report retains the failed baseline instead of treating a retry or counter-build as an unqualified win.
