# Acts IV–V combat-depth verification

Baseline: accepted commit `6259723`. Toolchain: repository-pinned .NET 8.0.425 and Godot 4.6.2 Mono, with native verification using the macOS debug package. The milestone adds contract-keeper wards, four authored formations and clearer final-boss attack/protection/recovery cues. Individual enemy statistics, collision, boss timings, XP/material grants and signature reward sources are unchanged.

## Focused mechanics

`LateCampaignCombatDepthTests` passes **100 cases**, with zero skips. Coverage includes actual barrier absorption, the shared 36-point cap, live range and line of sight inside fixed warning geometry, eligible types and copy/mechanism exclusions, ordinary-attack fallback, interruption and kill cancellation, Devourer cleanup, preservation of unrelated barrier/health, budgets, malformed saves, old behavior catalogs, RNG and exact replay.

Eighteen state-projection cases cover protection, guard, death, copies, pending actions and warnings including the zero-countdown frame. Five real boss sequences exercise both Covenant phases and all three Breach phases. Three damage-driven phase transitions verify removal of old warnings and correct ordinals in the next sequence. Ordinals remain stable as earlier strikes resolve.

Thirty isolated runs clear all six late-campaign encounters across all five disciplines and replay exactly. These fixtures use valid levels 10–15 with allocated offense/defense passives, default 350 health, no unlocked ultimate and no mutations. They are explicitly separate from the fresh-character campaign measurements below. The minimum remaining health is 274; the longest encounter takes 758 ticks. They establish supported mechanics and deterministic completion, not the difficulty of arriving with naturally earned equipment. Evidence: `tests/late-combat-depth-final.trx`, `tests/focused-final.log` and the TRX-backed `tests/late-encounter-results.json` in `artifacts/late-campaign-depth/`.

## Save compatibility

The focused migration checks pass **144 cases**, including 24 new late-depth cases and 120 maintained predecessor/reward cases, with zero failures or skips. Exact `6259723` catalog bytes are embedded. Active and cached actors, old Oath Mark and delayed boss warnings, midgame support state, gear, XP/materials, receipts, RNG, Fracture manifests and Bound Memory ownership are preserved. Authenticated characters cannot receive the pacing adjustment twice. Old replay bytes remain unchanged; read-only load, exact-byte backups and corrupted-primary recovery are tested. See [migration details](late_campaign_depth_migration.md); evidence is retained under `artifacts/late-campaign-depth/migration/`.

## Review

Prism/Gemini core review `fa6942b64190704ab0c9e1436458b438` proposed two missing-source safeguards. Both were added: view projection tolerates an absent source, and ward validation reports an explicit `InvalidDataException`. Existing ownership validation already rejects inconsistent archives; the review did not demonstrate a valid-state crash. The added guards leave valid combat behavior unchanged. Raw findings and dispositions are in `prism-core*.json`. Independent read-only audits of combat integration and migration found no actionable issues; they checked exact predecessor bytes, authentication before rebinding, ward cleanup, reward ownership and phase-transition warning removal.

The full staged Prism/Gemini review `5756e1a60255005ae561a22252ff4481` reported a high save concern and a medium camera concern. Both are false positives. `ApplyStatus` immediately removes source warnings when applying any status recognized by `Stunned`, and stunned actors cannot begin a ward. The validator correctly rejects a forged live-stun/pending-ward combination. A strengthened test restores at the exact interruption event while the stun is still active, and passes. Production uses its explicit owned `_camera`; the diagnostic's `Single` searches only the direct children of its one Sandbox. Nested preview cameras do not participate. Raw findings and proof are recorded in `prism-final*.json`; no actionable finding remains unresolved.

## Full regression suite

The complete Core suite passes **1,149 tests** and the server suite passes **17 tests**, with zero failures or skips: **1,166 total** (`tests/core-full.trx`, `tests/server.trx`). The final two strengthened interruption-boundary cases also pass (`tests/interruption-boundary.trx`). Campaign and endgame content compilation succeed, and the solution formatting check passes.

## Earned campaign progression

All **15 fresh-character campaigns** complete all 15 main and eight optional encounters: five disciplines across seeds 42–44, with **102,843 public commands**, **548 saved segments verified by exact replay**, zero deaths and zero failures. The driver spends earned passive points and equips owned upgrades at Greyhaven; it grants no fixture health, levels or gear. It uses the same policy as the accepted midgame baseline (`CEFC5E8F35447C5E6E9ACFCAF0D8F2445E7CB4A56A16BE87017DFE59804E7E69`). Evidence: `campaign-after/` and `balance-comparison.json`.

Across the fifteen runs, combat ticks change from **62,069 to 61,872** (−0.3%) and damage taken from **4,841 to 4,892** (+1.1%). Neither batch uses potions. Every character finishes at level **10**, **5,150 XP** and **475 materials**, unchanged from baseline. Travel ticks change from 26,099 to 27,235 and loot ticks from 13,119 to 12,556.

Mean encounter results under this fixed policy:

| Encounter | Combat ticks before → after | Damage taken before → after |
| --- | ---: | ---: |
| Bone Causeway | 140.6 → 132.7 | 2.1 → 1.3 |
| Contract Hall | 121.0 → 89.5 | 3.7 → 0.0 |
| Covenant Warden | 154.3 → 147.3 | 0.0 → 0.0 |
| Repeating Rooms | 90.9 → 129.3 | 8.8 → 12.9 |
| Identity Memory | 99.5 → 100.9 | 5.1 → 3.5 |
| Breach Heart | 322.3 → 306.9 | 3.3 → 4.1 |

The changes do not universally increase clear time. Repeating Rooms applies more pressure to this policy, while ward casting trades some keeper attack time for allied barrier. Two added ordinary enemies can advance loot RNG, changing later earned gear and route timings. Boss statistics and timings are unchanged; these measurements do not establish human difficulty.

## Native presentation

The corrected macOS diagnostic passes **675 checks**, **573 public commands**, **22 exact save/replay checkpoints** and **69 captures**, with zero skips, warnings or errors (`presentation/native-placement/late-campaign-combat-review.json`). It checks ward scheduling, skill interruption, actual barrier absorption, ordinary oath/shadow/memory/causal warnings, both Covenant phases, all three Breach phases, zero-countdown warnings and real recovery deadlines. High, Performance and Reduced Effects are covered.

All 69 PNG headers match the requested dimensions, including 16 at **780 × 720** (`presentation/native-placement/capture-dimensions.json`). The minimum-width ward focus shows `BARRIER 36`; Covenant displays its actual guard and numbered mark/fault; the phase-three Breach warning labels remain separate from the seal and boss labels. Shifted labels keep a leader to their original warning anchor. Placement is bounded to 81 candidates per named warning and the existing 32-warning budget, with cached leader ownership; it never changes collision or damage geometry.

The initial native pass exposed a visual overlap despite passing the earlier assertions. That package is retained as debugging evidence. The corrected acceptance run includes projected separation checks for required actor labels and viewport bounds, followed by screenshot inspection. Controlled positions, health and player protection are explicitly fixture-only.

The same final package also passes the native Hollow regression (**851 checks, 8,608 commands, 27 captures**), Spine regression (**588 checks, 6,797 commands, 26 captures**) and midgame combat regression (**209 checks, 474 commands, 27 captures**), all without skips, warnings or errors. These cover existing regional presentation and the shared warning-label placement. Reports are retained in `presentation/native-hollow/`, `presentation/native-spine/` and `native-midgame/`.

The tested `artifacts/export/Ashenwake.zip` has SHA-256 `3ea7dc9ef34c85861eed242bed4cbb54987dbbe7240737364604f5f2b047bcbd`. Its content identities are campaign `769CCC23B778D3E588116DBD868AB454875E4A9B98472AAEED24D2D746DAACB2`, campaign combat `7AA4BE32AEBD5A424BF00B4051584257E9C9EDA28332FAD7D560B09B90D0959E` and endgame combat `33AA0EB02531A73A8441B616BEEF268ED7649E781EF5462716E1DAEC75C6F384`.

## Evidence limits

Controlled support and boss-window fixtures modify setup state to isolate the feature under inspection; they do not represent earned progression or establish human difficulty. Native captures establish presentation on the tested macOS package, including reduced effects and the minimum viewport, not general hardware certification. Automated wins and warning checks do not establish human readability, enjoyment or an optimal build. Independent player testing remains a separate acceptance step.
