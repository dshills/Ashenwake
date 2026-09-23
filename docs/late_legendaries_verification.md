# Late-game legendary verification

Baseline: `f93bbbc` (defensive training). The expansion adds three named items, bounded combat powers, campaign and repeatable reward sources, equipment models, icons and authoritative HUD feedback. Validation uses the pinned .NET 8.0.425 and Godot 4.6.2 Mono toolchain.

## Gameplay and compatibility

The **11 acquisition cases pass**: all five disciplines earn the trio through ordinary campaign commands, collect and equip them at Torren, restore their saved state and replay the earned route. Three campaign reward comparisons preserve RNG and drop/object counts against the exact previous catalog. Three God Hunt cases play all phases, confirm the new item appears only at final victory, and compare reward RNG with the previous catalog. These cases use the real campaign policy; controlled reward comparisons are separate fixtures. Evidence: `artifacts/late-legendaries/tests/acquisition.trx`.

The **41 focused combat cases pass**, covering successful and resisted interrupts, warning timing, cooldown/barrier caps, direct-action ownership, multi-hit deduplication, target switches, damage and nonrecursive triggers, ultimate acceptance for all disciplines, rejected casts, charge spending, death/unequip/encounter cleanup, invalid state, and save/replay. Reward room checks also cover the three new Fracture sources.

The **38 focused compatibility/progression cases pass**, including 22 new cases plus the preceding legendary migration suite. They cover exact published catalog fixtures, active/cached rooms, pending actions, production and endgame saves, Borrowed Memory, original checksums, incompatible item rejection, read-only inspection, exact backups, extraction/engraving restrictions and duplicate-power suppression. Evidence: `artifacts/late-legendary/catalog-tests.log`. See [migration contract](late_legendary_migration.md).

The baseline fixture SHA-256 values are:

- Combat: `112efa7c2cb9840599539f0f13d694f8d740ef6fa26d5ebf5bb8c656b9b62736`.
- Progression: `61e08796e29bc241fe15c8218b121890a3256dbcff3622a5ced5e340eeb0bc1e`.

The broad regression run passes **1,353 cases**, with zero failures or skips, in 10 minutes 58 seconds (`tests/core-regression.trx`). It excludes the 11 acquisition cases recorded separately above. The final allocation optimization adds two tests and passes the 76-case focused rerun, giving passing coverage for **1,366 distinct Core cases**. The broad run predates that behavior-preserving optimization; the focused rerun and exact before/after hash comparison verify the final change. This is not a claim that one invocation executed all 1,366 cases.

All **17 server tests pass** (`tests/server.trx`). The final integrated solution build has zero warnings/errors, formatting verification passes, and all content bundles compile.

## Native desktop checks

Each native run uses the exported macOS app, one process at a time, with an isolated fresh output directory. The export-specific log check and strict runtime log checks pass.

The four runs below use the pre-optimization export, SHA-256 `80d88b554ebd71c682abb6f02811b5553587e39910a207eb7f335c430a0db7d5`. After the behavior-preserving receipt allocation change, the final solution build and formatting verification pass, and the game is exported again. The final package at `artifacts/export/Ashenwake.zip` has SHA-256 `6b0b035a1c8362deceaf199dca1dd2b3d4b573598dac6886d17bd85a22006d09`; its repeated native combat run passes all **360 checks and 35 captures**, with a clean runtime log (`native-feedback-final/`). Equipment, crafting and front-menu presentation code is unchanged apart from diagnostic formatting.

| Diagnostic | Checks | Captures | Evidence under `artifacts/late-legendaries` |
| --- | ---: | ---: | --- |
| Combat feedback | 360 | 35 | `native-feedback/combat-feedback-smoke.json` |
| Equipment appearance | 765 | 41 | `native-appearance/appearance-smoke.json` |
| Crafting | 105 | 16 | `native-crafting/crafting-review.json` |
| Front menu and saves | 146 | 27 | `native-front-menu/front-menu-review.json` |

The equipment run includes 41 native drag gestures, all-discipline geometry checks, cosmetic isolation, resource bounds, all nine item descriptions and existing save/replay flows. Inspected captures show distinct Crown, Witness amulet and Hour greaves, readable names/lore/source hints and fitting comparison cards at 780 pixels.

Combat feedback uses explicitly labelled isolated equipped fixtures for actual Core interrupt, four-action burst and ultimate/charge events. All eight readiness-bearing powers fit at 1280 and 780 pixels; trigger text stays above readiness and remains available with Reduced Effects. Presentation preserves the Core state hash. These visual fixtures do not claim earned late-game acquisition; the ordinary campaign tests establish that separately.

Crafting verifies five actual service commits, confirmations, previews, costs, protection, save and replay; the new powers' extraction and engraving specifics are covered by Core tests. Front-menu checks cover creation, character navigation, Continue, archive byte preservation, backup recovery, future-format rejection and Echoes return.

## Review

Prism/Gemini staged review `12ba58701db530f0bd288cbea55bb712` raised a Witness reset concern and a collection-allocation suggestion. The reset suggestion conflicts with the intended one-contribution-per-action rule: receipts must survive target switches, expiry and bursts while the same area/projectile remains live. Every new cast has a fresh action ID. Clearing these receipts would let one lingering action count repeatedly; passing multi-pulse, target-switch and save/restore cases verify the intended behavior.

The full-context follow-up `397b8297081bfada7fa9ce9530cb48fc` did not repeat the reset concern and flagged per-tick live-action collection allocation. The implementation now reuses session-local scratch storage, appends receipts to an ordered list and compacts that list in place. This removes repeated live-set construction and receipt-array copying while preserving deterministic order and the serialized JSON array shape. Two additional tests check prior JSON compatibility and stable receipt ordering. All **76 focused combat, migration, save and replay cases pass** after the optimization. A before/after probe across three disciplines, restoring every 17 ticks, produced byte-identical state and event hashes for **1,260 ticks**; evidence is in `allocation-equivalence/` and `core-optimized.log`. Raw reviews are retained as `prism-review.json` and `prism-context-review.json` in the milestone artifact directory.

Final staged review `25d5aeb7151e4b7b58019bb3e9dbcf8e` reports **zero high or medium findings** and two low suggestions (`prism-final.json`). The first proposes replacing the bounded duplicate check used only during restoration/validation; it is not in the tick loop, and a new local set would still allocate. The second proposes consolidating the existing 200-barrier cap, shared by the current guard, dodge, fragment and validation rules. Neither identifies a behavior defect, and broader cap refactoring is outside this expansion. No actionable review findings remain.

## Evidence limits

Scripted victories and controlled combat checks establish functional behavior and reproducibility. They do not establish optimal builds, equal class difficulty or independent player acceptance. Native evidence applies to the tested macOS export; it is not certification of other operating systems or GPUs.
