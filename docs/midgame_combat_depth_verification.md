# Acts II–III combat-depth verification

Baseline source: `99405ec` (midgame legendary builds). Tools: repository-pinned .NET SDK 8.0.425 and Godot 4.6.2 Mono; native checks use the macOS debug export. The milestone adds four authored formations, two interruptible support channels, a bounded forge buff and authoritative boss-window presentation. Individual enemy stats, collision, boss timing, XP/material grants and signature reward sources are unchanged.

## Save compatibility

The focused migration run passes **120 cases**, with zero skips: 19 new midgame-depth cases plus the maintained pacing, testament, opening-depth and two legendary-generation suites. Frozen campaign/overlay bytes come directly from `99405ec`. Active and cached encounters, already announced attacks, item ownership, reward receipts, XP/materials, RNG, Fracture manifests and Bound Memory states are preserved. Authenticated opening-depth characters do not receive pacing XP twice. Read-only inspection, exact-byte backups, corrupt-primary recovery and incompatible-state rejection are covered. See [migration details](midgame_depth_migration.md); evidence is in `artifacts/midgame-combat-depth/migration/`.

## Review and corrections

Prism/Gemini core review `921fcba620a529254a5bf8091dbb1bd5` reports one medium and one low finding. The medium pattern-fallback concern overlooks the first guard in `TryMidgameSupport`, which rejects every pattern except the two supported names before the ternary mapping executes. The low suggestion proposes constants for behavior names; the implementation intentionally distinguishes registry pattern names from namespaced event IDs, following existing campaign code. No required change resulted. Raw findings and dispositions: `prism-core*.json` in the milestone artifact directory.

Prism/Gemini full staged review `880ff020282b6ed16d7c3ff6cd007c37` reports one medium and two low findings, with no high findings. The dictionary-access concern is excluded by `DirgeAllies`: its nullable `IsEcho == false` predicate requires an existing campaign actor, and its enemy faction requirement excludes player summons. The alleged empty detail begins with a guaranteed nonempty literal, so no leading blank line is possible. The remaining formatter suggestion is nonblocking style: a fixed prefix and an optional prior detail differ from the variable collection joined elsewhere. Evidence and dispositions are in `prism-final*.json`.

The capture-driver reviews are `830cfd633725a77fe7fa84361b573782` and `b4402fa7f09e10a66d293ee17172841a`, both with no high findings. The first review's fail-open reflection check was corrected: expected fields and initialized controls are now mandatory, with explicit diagnostic errors. The same helper improves actor-field errors. The remaining suggestions concern intentionally stronger local visibility checks on buff expiry, bounded diagnostic traversal, asynchronous native resizing and system-cursor ownership. Native tests run one window at a time, align the physical cursor only in native mode, retry a bounded number of frames and reject a stale or obscured focus card at the exact frame saved. The final capture path uses the existing Cinder/Verdant diagnostic pattern (`ForceDraw`/`ForceSync`) because an occluded macOS window can stop emitting `FramePostDraw` while process frames continue. These constraints and the limited tested hardware scope are explicit; raw findings and dispositions are in `prism-capture*.json`.

The client diagnostic found a real boundary error in the new recovery view: a zero-countdown warning still awaited resolution at the start of its due tick. Recovery now stays hidden until all source warnings and pending abilities are actually removed. A separate integration audit found that Devourer directly kills its meal without the ordinary death path; a consumed empowered sentinel or channeling support enemy could therefore leave invalid new state. The consumption path now clears only the new timer and two new support warnings, preserving historical behavior, loot and corpse arbitration. Focused regression cases cover immediate capture/restore at those boundaries.

## Focused combat and build comparisons

The new combat suites provide **106 passing cases**: 101 mechanics/encounter cases and five paired archetype comparisons. Coverage includes both support channels, fixed warning geometry, live range/line of sight, heal caps, caster/boss/mechanism/copy exclusions, actual hard-control and kill cancellation, attack/channel alternation, duplicate bellows refresh without stacking, actual damage increase and expiry, budgets, malformed snapshots, original-catalog behavior, preserved RNG and replay. Three Devourer cases immediately save and restore consumed empowered sentinels or channeling support enemies. Both boss sequences explicitly visit a zero-countdown warning frame and require recovery to remain hidden until resolution.

Thirty cases clear and replay the six published midgame encounters across all five disciplines. These isolated encounter tests use valid level-appropriate builds; the separate campaign measurements exercise progression and travel. The comparison fixtures use public progression operations to validate level eight, seven passive points, owned properties and legal slots, then hold base gear stats equal while toggling each power. They have no anatomy. Warden and Arcanist carry 50 resource into both variants; the other variants begin with zero. This is controlled combat evidence, not equipment earned in that test through a fresh campaign.

| Discipline / property | Encounter | Control → enabled ticks | Control → enabled damage taken | Observed power |
| --- | --- | ---: | ---: | --- |
| Veilwalker / Virulent Wake | Plague Village | 295 → 253 | 158 → 158 | One spread, two targets |
| Warden / Rallying Chorus | Extraction Floor | 211 → 211 | 0 → 0 | Two choruses |
| Vanguard / Guard engraving | Extraction Floor | 206 → 206 | 20 → 5 | Four wards, 60 total barrier granted |
| Arcanist / Cinder Cycle | Extraction Floor | 182 → 202 | 0 → 0 | One trigger, 12 extra cooling |
| Gravecaller / Cinder Cycle | Plague Village | 420 → 447 | 258 → 271 | Three triggers, 24 actual extra Remains |

All ten runs clear and replay exactly. The resource policy adapts its spending/recovery choices; extra resource does not guarantee a shorter fight under that policy. These samples demonstrate engagement and bounds, not a universal ranking or an optimal rotation. Final TRX-backed results are in `build-comparison-summary.json`, `tests/midgame-depth-final.trx` and `tests/midgame-build-comparisons-final.trx` under the milestone artifact directory. All **17 server regression tests** also pass.

## Full regression suite

The complete Core suite passes **1,025 tests**, with zero failures and zero skips (`tests/core-full.trx`). Together with the 17 server tests, all 1,042 tests pass. The solution build completes with no warnings or errors, content compilation succeeds and the solution formatting check passes.

## Earned campaign comparison

All **15 complete campaigns** (five disciplines, seeds 42–44) finish all 15 main encounters and eight optional events with **zero deaths or failures**. Their policy hash matches the accepted `99405ec` baseline: `CEFC5E8F35447C5E6E9ACFCAF0D8F2445E7CB4A56A16BE87017DFE59804E7E69`. Every run finishes at level 10 with 5,150 XP and 475 materials, unchanged from the baseline. The runs execute 102,464 public commands across 548 saved and replay-verified segments; final saved profiles restore to the same hash.

Across all runs, total combat ticks change from 62,462 to 62,069 (−0.6%) and damage taken from 4,415 to 4,841 (+9.6%). No run uses a potion. Mean per-room measurements are:

| Encounter | Combat ticks before → after | Damage taken before → after |
| --- | ---: | ---: |
| Living Ruins | 160.2 → 186.6 | 21.6 → 91.0 |
| Plague Village | 114.4 → 150.7 | 68.1 → 54.2 |
| Cinder fields | 131.0 → 118.2 | 11.2 → 12.7 |
| Extraction Floor | 138.4 → 130.9 | 2.8 → 1.6 |
| Rootheart | 323.2 → 315.0 | 6.9 → 12.1 |
| Furnace Spindle | 238.7 → 237.9 | 4.5 → 4.5 |

Living Ruins creates more pressure under this policy; additional Act II enemies and support priorities do not make every encounter slower. Normal item drops from the added enemies advance the ordinary loot stream, and the earned-build policy can subsequently equip different items. Later boss measurements therefore do not isolate the effect of a formation or label. These are repeatable route comparisons, not evidence that every player or build faces the same difficulty. Raw reports are in `campaign-after/`; `balance-comparison.json` validates matching policy, route, discipline and seed coverage against `artifacts/midgame-legendaries/campaign-after/`.

## Desktop regional checks

The exported Act III diagnostic passes **478 checks**, zero skips and 23 captures across 5,088 public commands (`native-cinder/cinder-review.json`). It covers five environments, furnace vent orientations, guarded/exposed states, storm expiry, ordinary travel, mouse navigation, saved progression and exact replay. Native screenshots were inspected for the vent/slag countdowns, guarded core label and preserved encounter instructions. The runtime log is clean. The only later client change is to the new midgame diagnostic's pointer/focus driver; this regional run's production code is unchanged.

The exported Act II diagnostic passes **391 checks**, zero skips and 19 captures across 3,373 public commands (`native-verdant/verdant-review.json`). It covers five environments, all three tracking clues, feeding roots, Rootheart protection/exposure and victory, regional audio, reduced effects, travel, save/restore and exact replay. Its runtime log is clean.

## Final native combat acceptance

The final macOS debug package passes **209 checks**, 474 commands and all **27 captures**, with zero skips, warnings or errors (`native-combat-accepted/midgame-combat-review.json`). Controlled fixtures cover actual support scheduling/resolution, skill interruption, ten exact save/replay checkpoints, both boss sequences, High and Reduced/Performance effects, and the 780 × 720 minimum viewport. Presentation does not change simulation state.

The forge focus images are exactly 1,280 × 800 and 780 × 720 pixels. Both accepted frames have the expected hovered actor and target card, no visible blocking controls, no pause, contained card bounds and the exact `OVERCHARGED +20% DAMAGE · 3.0s` detail. Both pass on the first capture attempt. The minimum-size forge card, Rootheart recovery countdown and Furnace vent/slag/core warnings were visually inspected in the final package. The source-native diagnostic additionally passes 210 checks; its extra conditional check exercises the shipping Resume control.

Earlier captures exposed stale hover/pause state, and a later attempt stalled waiting for an occluded window to draw. Those runs are retained as debugging evidence, not acceptance evidence. The final driver validates the rendered frame and uses the established forced-render pattern to avoid that wait. No production gameplay changed during this diagnostic correction.

Final package: `artifacts/export/Ashenwake.zip`; SHA-256 `0021d429be288192dff2f08bcaa27b2b5bc8dd5499863c618753f48ca311ecbf`. The final solution build has zero warnings/errors; the complete formatting check and the final diagnostic-only formatting check pass.

## Evidence limits

Controlled support, boss-window and build fixtures establish mechanics, cancellation, ownership and presentation behavior. Their altered setup geometry, player protection or isolated enemies are identified separately from ordinary campaign measurements. Automated victories do not establish human difficulty, optimal builds, accessibility acceptance or enjoyment. Native captures apply to the tested macOS package, not an untested hardware/input matrix.
