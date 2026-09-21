# Hollow Night exploration

Act V connects Repeating Rooms, Identity Memory and Breach Heart through authored collision layouts. Repeated architecture and shadow doorways create an uncertain setting; real passages carry a consistent anchor glyph. Decorative doorways do not accept interactions or cover combat warnings. Mouse movement, physical passage markers and the local map use the same room geometry.

## Routes and rewards

- Secure **Repeating Rooms**, then take its northern branch to **The Unremembered Vault**. Elite echoes protect the **Vault Testament**, a legendary **Choir of the Unburied** ring, 45 materials and the testimony of people whose names the Hollow Night erased.
- The eastern passage leads to **Identity Memory**. Secure it and resolve the final choice before continuing east to **Breach Heart**.
- The Breach Heart retains its three seal channels, returning echoes and existing boss phases. Both ending choices preserve their outcomes and unlock Fractures. The chamber remains available for loot collection after victory.
- Western passages connect secured main rooms. The vault can be revisited after claiming its testament; enemies and rewards do not return.

Click a marked passage to approach and interact, or press F within reach. Journey offers the same approach actions. Press M to inspect discovered terrain, exits and loot; click revealed ground to move. Real exits retain their anchor marks among the repeated doorways.

## Persistence and compatibility

The bounded campaign cache covers twenty-three authored rooms across all five acts. Cleared rooms retain corpses and uncollected drops, while transient attacks and hazards are removed when leaving. Discovered terrain persists separately for each room. Vault equipment and material rewards have separate once-only receipts, and item identities are checked against active loot, retained rooms and the complete permanent inventory. Abandoned unfinished fights restart.

The catalogs are `campaign.hollow.6` and `campaign-combat.hollow.6`. Frozen `fixtures/campaign-spine.json` and `fixtures/campaign-combat-spine.json` authenticate the preceding release. Campaign, Endgame, Echoes and shared-profile archives retain the existing predecessor chain. Migration validates the source archive before rebinding catalogs, preserves progression and random state, and deterministically relocates only positions newly obstructed by authored geometry. Changed layouts reset their fog; unchanged rooms keep it.

Earlier saves waiting on the final choice in a generic cleared arena resume in secured Identity Memory. Completed-act revisits resume in secured Repeating Rooms. Saves within the Breach Heart retain their live seal identities or completed outcome and uncollected drops. Loading leaves the source save and profile untouched; explicit saving preserves their original bytes as backups. Historical replays stay bound to their original catalogs.

## Inspection

After building and compiling content, use Bash and a fresh output directory:

```bash
source tools/env.sh
hollow_exploration_output=$(mktemp -d "$AW_ROOT/artifacts/hollow-exploration-inspection.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 36000 \
  --log-file "$hollow_exploration_output/smoke.log" -- \
  --hollow-exploration-smoke --discipline=Vanguard --capture-hollow-exploration \
  --output="$hollow_exploration_output"
python3 tools/check-godot-log.py "$hollow_exploration_output/smoke.log"
```

The exported macOS app accepts the same diagnostic arguments. Add `--headless` and omit the capture flag for structural checks without rendered inspection.

## Verification

The full solution builds with zero warnings or errors. The final full Core suite passed **694 tests**, including all 27 new Hollow gameplay, navigation and migration cases. Content compilation, formatting verification, shell syntax and diff checks passed. Initial targeted tests caught a Rare reward incompatible with the ring's existing behavior-changing power. The vault now grants Legendary rarity, with matching receipts and assertions; the shared equipment definition and validation rules remain intact.

The exported macOS app passed **159 exploration checks** and **292 existing Hollow presentation checks**, with clean engine logs. The exploration run earned both `share` and `guard` endings through real three-seal Breach fights, displayed both ending stories and opened the Fracture gate after each. It verified native passage clicks, the once-only vault reward, F5/F9, four-room maps, retained loot, backtracking and eight deterministic replays. The presentation regression retained the original boss phase, seal identity, shield, echo-warning, reduced-effects, pause, victory, audio and geometry checks.

The two native runs produced 37 screenshots. Visual inspection covered the vault before and after claiming its reward, the shared-stewardship ending, overlapping first/return echo warnings, and the Breach Heart local map. Current scenery remains bounded at 6–7 architecture meshes and 9–10 ground meshes per context; these structural checks do not certify performance on other hardware.

Prism/Gemini reviewed the staged implementation twice. The final review reported two low-severity notes, no medium or high findings. Multiple sources for the requested Legendary ring are intentional; each grant has a separate item identity and receipt. The ground-routing note described possible future scaling, with fixed current layouts and geometry generated only on room creation. Initial relocation and player-identity concerns were disproven by the existing relocation helper and validated player invariant. No actionable review findings remained.

Reports, logs, screenshots and review dispositions are under `artifacts/hollow-exploration/`. The rebuilt playable app is `artifacts/export/macos/Ashenwake.app`.
