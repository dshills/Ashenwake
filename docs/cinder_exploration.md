# Cinder Reach exploration

Act III connects the Cinder fields, Extraction Floor and Furnace Spindle through authored collision layouts. Ruined street blocks, machinery channels and furnace buttresses now shape movement. The environment's paving and the local map follow the same authoritative room geometry.

## Routes and rewards

- Secure the Cinder fields, then follow the northern passage into **the Sealed Foundry**. Its elite guardians protect the **Foundry Testament**, a rare **Cinderwake Saber**, 35 materials and testimony from the city's furnace workers.
- The eastern passage leads to the **Extraction Floor**. Secure it and resolve the extraction choice before following the next eastern passage into **Furnace Spindle**.
- The floor's southern branch enters **Burning Rain**, the existing 30-second Resonance Storm. Defeat its guardians before time runs out. Victory records the reward immediately and stops the timer, leaving the arena and its drops available to explore. The western passage returns to the Extraction Floor. Leaving an unfinished attempt or allowing its timer to expire grants no completion reward; retrying starts a fresh attempt.
- Western passages connect secured main rooms. Completed branches remain accessible without respawning enemies or granting their rewards again.

Click a passage to approach and interact, or press F within reach. Journey offers corresponding approach actions. Press M to inspect discovered terrain, exits and loot; click revealed ground to move there. Storm timing and departure consequences appear in the journey guidance.

## Persistence and compatibility

Cleared Cinder rooms retain corpses and uncollected loot in the bounded campaign cache, now covering fourteen authored rooms across the first three acts. Leaving a secured room removes transient attacks and hazards. Permanent inventory is projected into its cached arena on return, with item identity checks across active rooms, cached loot and the complete permanent inventory. Abandoned unfinished fights restart. Foundry equipment and materials are transactional, once-only rewards with permanent receipts.

The catalogs are `campaign.cinder.4` and `campaign-combat.cinder.4`. Frozen `fixtures/campaign-verdant.json` and `fixtures/campaign-combat-verdant.json` authenticate the preceding release. Campaign, Endgame and Echoes archives traverse the maintained predecessor chain; shared profiles use the matching discovery policies.

Original archives are validated before rebinding. Only positions obstructed by new geometry relocate, using deterministic legal candidates. Character ownership, progression, item identities and random state persist. Fog survives in unchanged rooms; changed room layouts reset their discovery mask. Older storms retain their remaining timer and original secured return, including storms started before the Extraction Floor was completed. Older generic cleared Act III arenas gain a secured physical room, including saves waiting on the extraction choice. Loading does not rewrite a source save or profile; the next explicit save keeps original bytes as backups. Historical replays remain bound to their original catalogs.

## Inspection

After building and compiling content, use Bash and a fresh output directory:

```bash
source tools/env.sh
cinder_exploration_output=$(mktemp -d "$AW_ROOT/artifacts/cinder-exploration-inspection.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 24000 \
  --log-file "$cinder_exploration_output/smoke.log" -- \
  --cinder-exploration-smoke --discipline=Vanguard --capture-cinder-exploration \
  --output="$cinder_exploration_output"
python3 tools/check-godot-log.py "$cinder_exploration_output/smoke.log"
```

The exported macOS app accepts the same diagnostic arguments. Add `--headless` and omit the capture flag for structural checks without rendered inspection.

## Verification

All **633 Core tests passed**, including all five discipline campaigns, physical passage boundaries, foundry receipts and inventory overflow, storm victory/expiry/death, retained loot and fog, authenticated migration, shared-profile backups and replay determinism. The final solution build has zero warnings or errors, and formatting verification passed. The reports are `artifacts/cinder-exploration/core-complete/core.trx`, `build-complete.log` and `format-complete.log`.

The exported macOS app passed **178 native exploration checks**, including **15 captures** and **six verified replay branches**, in `artifacts/cinder-exploration/native-verified/`. Its real campaign commands earn the regional victories, and native viewport inputs enter/leave branches, claim the testament, navigate discovered map terrain and backtrack. F5/F9 checks restore both claimed treasure and a live storm timer. The storm is separately abandoned, expired through ordinary movement and potion commands, won, and revisited without duplicated rewards.

The existing Cinder presentation diagnostic passed **257 checks** with **16 captures**, covering five distinct environments, Furnace guard/vent states, defeat and restore, reduced effects, pause, ambience, floor/scenery bounds and replay. Both final native Godot logs are clean: **435 packaged checks in total**. The latest app is `artifacts/export/macos/Ashenwake.app`.

Prism/Gemini reviewed the staged milestone twice. Its actionable passage-radius inconsistency was corrected with a shared 1800-unit boundary and a regression test for both command paths. The remaining candidates were checked against their actual implementation: rewards read content values, choice gates and room identities are validated, storm enemies spawn synchronously, and static paving work is bounded. The crossed forge bars are distinct geometry, and Cinderwake Saber's stable item ID resolves through localization. Reports and detailed dispositions are retained under `artifacts/cinder-exploration/`.
