# Shattered Spine exploration

Act IV connects Bone Causeway, Contract Hall and Covenant Warden through authored collision layouts. Bone-lined corridors and tribunal courts shape movement while keeping the numbered faults and boss warnings readable. Regional paving, physical passages and the local map use the same authoritative room geometry.

## Routes and rewards

- Secure **Bone Causeway**, then follow its northern branch to **Oathkeeper’s Archive**. Elite guardians protect the **Archive Testament**, a rare **Vowkeeper’s Carapace**, 40 materials and lost testimony about the people bound by the city's contracts.
- The eastern passage leads to **Contract Hall**. Secure the hall and resolve its oath choice before continuing east to **Covenant Warden**.
- The hall's southern passage enters **The Promise Before Stone**. This Divine Memory retains its reversed fault sequence and has no timer. Victory records its discovery and reward immediately; the cleared sanctuary and uncollected drops remain available until you take its western return passage. Leaving an unfinished attempt restarts its guardians on the next entry.
- Western passages connect secured main rooms. Both completed branches can be revisited without respawning their enemies or granting rewards again.

Click a visible passage to approach and interact, or press F within reach. Journey offers the same approach actions. Press M to inspect discovered terrain, exits and loot, and click revealed ground to move there.

## Persistence and compatibility

The bounded campaign cache now covers nineteen authored rooms across Acts I–IV. Cleared rooms retain their corpses and uncollected drops. Transient attacks and hazards are removed when leaving a secured room. Permanent inventory is projected back into the room on return, with item identities checked against active loot, other cached rooms and the entire permanent inventory. Archive equipment and materials use separate once-only receipts. Partially completed fights restart when abandoned.

The catalogs are `campaign.spine.5` and `campaign-combat.spine.5`. Frozen `fixtures/campaign-cinder.json` and `fixtures/campaign-combat-cinder.json` authenticate the preceding release. Campaign, Endgame, Echoes and shared-profile archives continue through the maintained predecessor chain.

Migration validates the original archive before changing its catalog identity or geometry. Only newly obstructed positions relocate, deterministically. Ownership, progression, random state and item identities persist. Previously discovered terrain stays intact in unchanged rooms; new layouts reset the affected room's fog. Older Divine Memories retain their original secured regional return, including saves started before Contract Hall. Generic cleared Act IV arenas gain a secured physical room, including saves waiting on the oath choice. Loading does not rewrite the source archive or profile; the next explicit save preserves original bytes as backups. Historical replays remain bound to their original catalogs.

## Inspection

After building and compiling content, use Bash and a fresh output directory:

```bash
source tools/env.sh
spine_exploration_output=$(mktemp -d "$AW_ROOT/artifacts/spine-exploration-inspection.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 24000 \
  --log-file "$spine_exploration_output/smoke.log" -- \
  --spine-exploration-smoke --discipline=Vanguard --capture-spine-exploration \
  --output="$spine_exploration_output"
python3 tools/check-godot-log.py "$spine_exploration_output/smoke.log"
```

The exported macOS app accepts the same diagnostic arguments. Add `--headless` and omit the capture flag for structural checks without rendered inspection.

## Verification

The milestone passed the full solution build with zero warnings or errors, content compilation, formatting verification and shell syntax checks. The Core suite ran 666 tests: 665 passed initially, and a new reversed-fault assertion sampled one tick too early. After correcting only that test's timing, all 32 Spine tests passed on the rebuilt solution, covering the remaining case and save migration regressions.

The exported macOS application passed 178 exploration checks and the existing presentation diagnostic passed 286 checks, with clean engine logs. These cover actual mouse approaches, physical returns, archive rewards, live-memory save/load, once-only receipts, retained loot and terrain, all five rooms, the oath choice, Warden guard/fault states, reduced effects and replay consistency. Six exploration replays verified. The two runs captured 32 screenshots; visual inspection included the archive before and after claiming its reward, reversed Memory warnings and the Warden's local map.

Prism reviewed the staged implementation with Gemini. Its missing-item candidate was disproven by the shared equipment catalog and successful reward checks; its paving concern described a hypothetical larger layout, with current static geometry bounded by native checks. No actionable findings remained. Reports, logs, screenshots and dispositions are under `artifacts/spine-exploration/`; the rebuilt playable app is `artifacts/export/macos/Ashenwake.app`.
