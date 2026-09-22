# Campaign pacing verification

## Scope and evidence

Baseline: `09670a6290161b604ca1ef12645878d000ad2690`, `campaign.hollow.6`. Revised catalogs: `campaign.pacing.7` and `campaign-combat.pacing.7`. The frozen predecessor fixtures match the baseline source files byte for byte.

Artifacts are under ignored `artifacts/campaign-pacing/`:

- `baseline-source/`: original catalogs, commit identity and measurement binary.
- `baseline-managed/`: original binary, five fresh characters, all optional branches.
- `baseline-instrumented/`: corrected measurement fields using the revised runtime with the original catalogs. All five final hashes match the original binary (`historical-behavior.json`).
- `after-managed/`: revised catalogs, five fresh characters, all optional branches.
- `baseline-main/` and `after-main/`: five fresh characters each, optional branches skipped.
- `comparison-managed.json` and `comparison-main.json`: matching policy/route/discipline/seed comparisons from `tools/compare-campaign-balance.py`.

The 20 compared playthroughs cover **117,615 public commands in 572 saved replay segments**. Every segment replays and restores with identical state hashes; every final campaign save/profile loads identically. Each character completes all 15 main encounters and, on the full route, all eight optional encounters. All runs finish at level 10 with 5,150 XP, zero deaths, and 255 main-only or 475 full-route materials before spending. No world command fails.

All runs use seed 42 and the same versioned `earned-build` policy within each route. The policy uses fresh ownership, compatible earned gear and earned passive points. It does not optimize powers, craft or spend materials. This is deterministic regression evidence, not a statistical estimate of difficulty or campaign length.

## Measured combat

Total simulated seconds with living enemies, excluding travel, pickups and menus:

| Discipline | Full route before | Full route after | Main route before | Main route after |
| --- | ---: | ---: | ---: | ---: |
| Vanguard | 174.0 | 181.8 | 133.3 | 133.3 |
| Veilwalker | 159.2 | 140.7 | 119.4 | 119.4 |
| Arcanist | 105.9 | 105.9 | 81.4 | 81.4 |
| Gravecaller | 148.4 | 148.4 | 105.4 | 105.4 |
| Warden | 169.6 | 169.6 | 129.4 | 129.7 |

These changes include deterministic consequences of gear selection, critical rolls and altered command sequences. They are not a claim that each class became faster. The report retains per-room damage, skills, resource limits, attempts and unlocks for inspection. No run used a potion. Enemy health, damage and resource rules remain unchanged; this evidence supports the specific progression, reward and navigation fixes, not broad difficulty tuning.

The level schedule now gives level 3 after Bell Saint and level 5 after Rootheart. Repeating Rooms gives level 9, followed by level 10 after Identity Memory, instead of a two-level jump there. The total campaign budget and pre-final ultimate unlock remain intact.

## Native client

The exported macOS application passed **175 assertions, 19 captured frames and eight verified replay segments** in `native/hollow-exploration-review.json`. Both campaign endings were earned through actual combat and story commands. Real clicks verified the new board action closes its modal, queues the gate walk without changing progression, can be cancelled with X, and reopens the board at the gate with recovery enabled. Arrival neither claims a Sigil nor begins a run.

The before-arrival and arrival captures were visually inspected: the action and its explanatory text fit, the disabled recovery action is clear, and arrival replaces the walking action with the enabled recovery control. Existing Hollow tests also cover vault claiming, persistent loot, backtracking, maps and save/load.

## Regression checks and build

All **753 unique Core tests have passing final results**, verified from the full-suite run and subsequent affected-suite reruns in `test-summary.json`. The initial full run executed 751 tests and exposed five older migration assertions that assumed XP receipt payloads could not change. Updated expectations calculate the exact authored XP changes and continue comparing every unrelated field. The affected-suite rerun exercised all migration, reward and measurement suites; the last migration rerun passed all 43 cases, including the two new valid reordered/subset projection cases. Original failed runs remain in the artifacts for traceability.

Migration coverage includes every completed campaign prefix, capped/imported XP, an in-flight cast, old-catalog replays, forged receipts and checksums, read-only load, backups, Phase 4 imports, active Fractures and Echoes. Reward tests cover all five disciplines, Tempering, exact legacy/current fingerprints, crafted/discarded/extracted rewards, old pre-claim behavior and persistence.

The final solution builds with zero warnings/errors. Formatting and diff checks pass. Campaign/experiment content compilation, Godot import and macOS export pass; the playable application at `artifacts/export/macos/Ashenwake.app` includes the final save-preservation correction. The native run precedes that migration-only correction; the final Core regression reruns cover it.

## Review

Prism/Gemini reviewed the staged implementation with default secret redaction and the existing implementation rules. The first review reported a hypothetical future XP reduction and a supposedly missing resource null check. Neither is an actionable defect in this release: the migration targets an exact version with every completed-prefix delta tested, and the null check is already present. See `prism-initial.json` and `prism-dispositions.md`.

Local review found an inventory preservation edge case in XP-only migration. The fix applies the derived progression build without rebuilding the old runtime inventory; dedicated regressions retain valid reordered and subset projections. The follow-up `prism-final.json` covers that correction. It reports no high/medium findings and two low-severity suggestions about unchanged code: duplicating an existing headless capture guard and extracting an opening interaction-range constant. Both were assessed as optional refactoring; no actionable correctness, persistence or determinism finding remains.

Human playtesting across skill levels, crafting preferences, alternate builds and multiple seeds remains necessary before further difficulty changes. The simulation policy does not measure reading time, presentation performance or enjoyment.
