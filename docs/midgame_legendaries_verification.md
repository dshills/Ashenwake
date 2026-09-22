# Midgame legendary verification

Baseline source is `9c63c93` (Act I combat depth). The milestone adds three exclusive equipment definitions and properties, bounded Core power rules, authored item presentation and optional combat feedback. It does not alter enemy stats, campaign layout, XP/material rewards or existing legendary sources. Validation uses the repository's pinned .NET 8.0.425 and Godot 4.6.2 Mono tools.

## Save compatibility

The focused compatibility run passes **49 tests**, including **16 new midgame migration cases**, the maintained original legendary and opening-depth migration cases, and production archive checks. Exact previous combat/progression bytes are frozen from the baseline commit. Sequential additive-catalog fallback authenticates each original before rebinding: midgame additions are removed first, then the original legendary additions if needed. New inactive booleans/timers are omitted from serialized state.

Tests cover production, campaign, active/cached combat, Fractures, Bound Memory, profile reads, direct earlier imports, original checksums, backups and nonmutating inspection. Production archive reading now validates the original raw state checksum before deserializing new optional fields; inserting explicit default fields without updating that checksum is rejected. Existing fixtures are not regenerated. Evidence: `artifacts/midgame-legendaries/tests/midgame-migration-final.trx` and `migration-final-tests.log`. See [migration contract](midgame_legendary_migration.md).

## Automated regression

The full Core run executed **900 cases**: 899 passed, and one older Fracture assertion still assumed that *every* signature item was exclusive to a final room. The new Rotwake reward intentionally occupies Verdant Maw's first room. That assertion now checks that the original trio remains final-room only. The subsequent **seven-case Fracture run passes**, covering the corrected three regional cases and all four published midgame reward rooms. The latter also directly asserts that the legendary's drop source is the last killed enemy. Together these runs provide passing evidence for all 900 distinct cases; this is not a claim that the initial full invocation passed. Evidence: `tests/core-full.trx` and `tests/fracture-rewards-final.trx` under the milestone artifact directory.

The total includes 79 new power/acquisition cases and 16 migration cases. Coverage includes ownership, poison origin, nonrecursive triggers, line of sight, summon generation, target/status budgets, all five resource disciplines, cooldown/charge bounds, rejected actions, actual capped resource changes, power removal/death, save/replay, unchanged loot RNG draws, extraction/engraving costs and compatibility, duplicate-power suppression, and all five disciplines earning and equipping the three rewards through ordinary campaign commands. All **17 server tests** also pass (`tests/server.trx`). Solution build finishes with zero warnings/errors, formatting verification passes, and content compilation succeeds.

## Campaign comparison

The unchanged `earned-build` policy completed all **15 runs** (five disciplines, seeds 42–44), each covering 15 encounters and eight exploration rooms. There were zero deaths or failed runs. Baseline evidence is the accepted previous milestone's `artifacts/opening-combat-depth/after`, whose simulation/content match `9c63c93`; new reports are `artifacts/midgame-legendaries/campaign-after`. The policy hash is `CEFC5E8F35447C5E6E9ACFCAF0D8F2445E7CB4A56A16BE87017DFE59804E7E69`.

| Discipline (three seeds) | Combat ticks before → after | Damage taken before → after |
| --- | ---: | ---: |
| Vanguard | 14,581 → 14,015 | 1,295 → 1,415 |
| Veilwalker | 11,283 → 11,797 | 1,211 → 1,026 |
| Arcanist | 8,567 → 9,029 | 363 → 297 |
| Gravecaller | 13,333 → 13,245 | 1,245 → 1,050 |
| Warden | 14,044 → 14,376 | 593 → 627 |
| Total | 61,808 → 62,462 | 4,707 → 4,415 |

Total combat duration rises 1.1%; damage taken falls 6.2%. Final levels, XP and material totals are unchanged, and neither sample uses potions. These are observations under the fixed stat-upgrade policy, not claims of equal class difficulty or optimized power combinations. The comparison tool verifies matching routes, room coverage and policy before producing `balance-comparison.json`. The only subsequent Core addition was the read-only summon-availability view used by the HUD; combat rules and serialized state remained unchanged.

## Desktop presentation

The final macOS debug package is `artifacts/export/Ashenwake.zip`, SHA-256 `2873c2caad67f7f251dcfab8f3cee22f3d204b1c417a805eda44bcc56fef2a4c`. Native checks run one app at a time against that export, using fresh isolated artifact directories and strict runtime-log validation. The export log passes the repository's export-specific check.

- Combat feedback: **314 checks**, 26 captured screens (`native-feedback/combat-feedback-smoke.json`). Headless coverage also passes 288 checks. Native captures verify all-equipped readiness at 1280, 1024 and 780 pixels, Reduced Effects, actual poison spread and summon strikes, and prepared/consumed Cinder Cycle for all five disciplines. The paused-resize check exposed and fixed a HUD reflow omission: resizing now refreshes the legend labels even while simulation is paused.
- Equipment appearance: **722 checks**, 37 captured screens and **41 viewport drag gestures** (`native-appearance/appearance-smoke.json`). Headless coverage passes 682 checks. Geometry tests exercise all five disciplines, both ring slots, nonmutating presentation and bounded meshes. Visual inspection covers the new three-item gallery and all three power/source comparisons at 780 pixels; names, descriptions and comparison values fit. Existing pickup, equipment, cancellation, invalid drop, save and replay checks pass.
- Crafting regression: **91 checks**, 16 captured screens, five committed services and one viewport drag gesture (`native-crafting/crafting-review.json`). Existing preview, destructive confirmation, extraction, engraving, insufficient-material and save/replay flows pass with the new catalog. The three new powers' specific crafting costs, slot restrictions and extraction/engraving results are covered by the Core cases above.
- Front menu/save regression: **146 checks**, 27 captured screens and one exact gameplay replay (`native-front-menu/front-menu-review.json`). Native keyboard/pointer flows cover five discipline previews, character creation, Continue and character routing after restart, byte preservation, blocked saves, corrupt-primary backup recovery, future-format rejection and Echoes origin return. All four native runtime logs are clean.

Useful captures under `artifacts/midgame-legendaries`: `native-appearance/midgame-legendary-armor.png`, `native-appearance/equipment-mourning_choir-description.png`, `native-appearance/equipment-furnaceheart_cinch-description.png`, `native-appearance/equipment-rotwake_signet-description.png`, `native-feedback/midgame-readiness-780.png` and `native-feedback/midgame-cinder-trigger-Arcanist.png`. The power feedback captures use labeled isolated combat fixtures; ordinary campaign acquisition is covered separately by Core command/replay tests.

## Review

Prism/Gemini review `c3e051a10e56dfd1bf2f8a0dc95daf5f` inspected the staged combat, content and migration changes with default redaction. It reported two low-priority maintainability suggestions and no correctness findings: extracting unchanged Pyre tuning constants and introducing a global campaign-ID registry. Neither identifies a behavior defect; existing independent contract checks cover Pyre values and real earned-route tests exercise the new reward IDs. Raw findings and per-entry dispositions are under `artifacts/midgame-legendaries/prism-core*.json`.

The complete staged review, `331d6703faac5d76d859cd4dfd64d5dd`, reported one medium and two low findings. The medium claim that the first enemy receives the reward is a false positive: `CombatSession.Kill` requires no living reward-eligible enemies before replacing the ordinary drop. The seven Fracture regression cases pass, including explicit final-kill source assertions. The low suggestions propose a region-ID registry and nullable reward strings; existing explicit identifiers and empty-string contracts are unchanged conventions, with tested callers.

A follow-up Prism review containing the full reward selector, caller, campaign eligibility and final reward tests (`fa2a181919ce2f725d6c30b4e92dfb3e`) did not repeat the reward concern. It suggested avoiding hypothetical per-tick snapshot validation and replacing an existing index-from-end actor assignment. Inspection confirms `ValidateSnapshot` is called only on encounter creation/replacement and restoration, never by `Step`; the synchronous actor factory appends the actor immediately before the existing assignment. Neither identifies an actionable defect in this change. The reviewer supplied a nonexistent `logic/campaign_manager.cs` location; dispositions use the actual Core paths and call sites. No actionable review findings remain. Raw results and dispositions are retained in `prism-final*.json` and `prism-reward-context*.json`.

The final test/document delta review (`ae6e5f37c2380a6d96d228ee4679598b`) raised a hypothetical unordered-death concern and a test-array allocation suggestion. `Step` explicitly orders the damaging actor/status loop by actor ID, drains a FIFO hit queue and records events in order. The assertion checks the documented final-death relationship without hardcoding an actor ID; it also retains the independent single-legendary check. The cited allocation line is outside the file; the setup's actor array is necessary because its loop replaces entries in the source list. Other test arrays capture observations for multiple assertions or snapshot loot before pickup mutates it. Neither requires a fix. Evidence and dispositions: `prism-final-delta*.json`.

## Evidence limits

Controlled power fixtures and scripted campaign victories establish functional behavior and reproducibility. They do not establish optimal builds, equal class difficulty, player enjoyment or independent human acceptance. The campaign policy selects raw-stat improvements rather than optimizing legendary combinations; dedicated power fixtures separately exercise the intended interactions. Native screenshots and checks apply to the tested macOS package, not an untested OS/GPU/input matrix.
