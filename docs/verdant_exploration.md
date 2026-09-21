# Verdant Maw exploration

Act II's existing forest art now follows authored collision layouts. Root walls divide the Living Ruins, quarantine channels shape the Plague Village, and low buttresses surround Rootheart's feeding roots. The core and all three roots remain reachable. Floor paths, physical passages and the local map use the same room geometry.

## Routes and rewards

- Secure the Living Ruins, then follow its northern trail to **Briarheart Shrine**. Defeat a Devourer vine and its guardians, then open the **Briar Testament** for a rare **Orrun's Oathseal**, 30 materials and testimony about refugees who sheltered beneath an old oathstone before the roots arrived.
- Follow the eastern passage to the Plague Village. Secure its channels and resolve the transformation choice before entering Rootheart.
- The village's southern trail leads to **the Antler Grove**. Investigate shed bark, reversed hoofprints and the heartwood nest. The Antler appears in the same grove; the final clue preserves player health and position and the discovered map.
- Western return passages connect secured main rooms. Shrine and grove returns reconnect their parent area. Completed branches remain accessible without respawning their defeated guardians or granting rewards again.

Click a passage to approach and interact, or press F within reach. Journey offers the corresponding approach actions. Press M to inspect discovered terrain, exits and remaining loot; clicking revealed ground moves the character.

## Persistence and compatibility

Cleared Act II rooms retain corpses and uncollected drops in the existing bounded campaign room cache. Transient attacks and hazards are removed when leaving a secured room. Permanent inventory is projected back into a cached room on return, and item identities are reserved before any further reward is created. Partially completed fights restart when abandoned. Shrine treasure is a transactional, once-only item and material reward validated against its permanent receipt. Hunt victory is recorded immediately, so leaving for Greyhaven or dying afterward preserves the defeated grove and its drops. Cached loot is checked against all permanent equipment, including items beyond the combat inventory projection.

The catalogs are `campaign.verdant.3` and `campaign-combat.verdant.3`. Frozen `fixtures/campaign-grey-march.json` and `fixtures/campaign-combat-grey-march.json` authenticate the preceding release. Campaign, Endgame and Echoes saves can traverse the exact published catalog chain, including the previous Grey March and legendary upgrades. Shared profile discoveries migrate through the same predecessor policies.

Original archives are validated before rebinding. Only positions obstructed by new geometry relocate, using deterministic nearest legal candidates; character ownership, progression, loot identities and random state persist. Old hunt tracking saves acquire the grove layout and retain their original regional return, including hunts started before the village was secured. Fog survives in unchanged rooms; rooms with changed collision geometry start a new discovery mask. Loading does not rewrite the source save or profile, and the next explicit save preserves their original bytes as backups. Historical replays remain tied to their original catalogs.

## Verification

All **600 Core tests** are verified. The full run passed 594 and exposed six obsolete opening-room expectations; the corrected room test class passed all 20 cases on rerun. `artifacts/verdant-exploration/core-summary.json` records the combined results. Coverage includes physical passage gates, all five discipline campaigns, shrine receipts, full inventory overflow, duplicate ownership rejection, hunt completion and death, old-catalog tracking, migration authentication, save backups, retained loot, fog and replay determinism.

The exported macOS game passed **172 native Verdant exploration checks**, with **13 captures and five verified replays**, in `artifacts/verdant-exploration/native-verified/`. It earns the campaign victories through real combat, clicks the shrine and all three hunt clues, navigates the map, backtracks through all five rooms and exercises F5/F9 persistence. Native testing found and fixed clue mouse picking: active clues now expose their visible model to the picker, and spent clues are removed from the interaction set. The diagnostic explicitly resumes only focus-loss pauses and checks that those interruptions preserve world state and cancel pending input.

Existing packaged Settings, Local Map and Verdant presentation diagnostics also passed **162**, **74** and **176** checks, respectively, with clean Godot logs: **584 packaged checks in total**. Settings checks cover the shared pause behavior; shutdown now defers release of an actually held expedition-panel pause until child removal has finished. The presentation diagnostic verifies all five distinct environments and their replay; its clue assertions now account for the physical return passage and quiet, spent traces that remain in the completed grove. The latest build has zero warnings or errors, and the updated app is `artifacts/export/macos/Ashenwake.app`.

Prism/Gemini reviewed the staged milestone three times; reports and candidate dispositions are retained under `artifacts/verdant-exploration/`. No actionable findings remain. Source checks confirmed that route arrays are never empty, invalid migration positions must fail safely, claimed shrines use cleared-room restoration even without a cache entry, and UI passage buttons approach their physical targets before interacting. Final candidates about missing player actors and retained-room validation were also checked: restore requires exactly one player, and full cached snapshot validation is intentional and bounded to nine rooms. Final formatting verification passed.
