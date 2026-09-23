# Defensive training verification

This milestone extends the isolated Greyhaven training ground with ordinary melee, ranged and mixed sparring opponents, resolved incoming-damage measurements, defensive effect counts and one previous-attempt comparison. [Controls and measurement contract](training_ground.md).

## Evidence scope

Core tests cover active opponent behavior, authoritative damage partitions, barrier/immunity/overkill cases, defensive effect triggers, clean resets, defeat, unchanged earned source state and bounded deterministic training replay. Ordinary campaign/endgame rules, rewards and saved character history are outside the training wrapper.

The client diagnostic uses fresh characters, ordinary training entry and actual sparring attacks. It checks report pause and input isolation, safe defeat/reset/exit, comparison retention and clearing, native opponent presentation, default/minimum layout and save/replay. Fixtures used to isolate individual defensive effects in Core tests are distinguished from earned-character client routes.

## Results

The solution build passed with zero warnings/errors and formatting verification passed. The complete Core suite passed **1,292 tests** and the server suite passed **17 tests**, with no failures or skips (`artifacts/defensive-training/tests/core-full.trx` and `server-full.trx`). The focused Core run passed **21 cases**, including six new defensive-training cases; these overlap the full suite and are not added to its total.

The shipping-director headless diagnostic passed **88 checks**, **1,969 ordinary commands** and **6 replay verifications** (`artifacts/defensive-training/headless02/defensive-training-review.json`). The final packaged native diagnostic passed **130 checks**, the same command/replay counts, and **9 captures with zero skips** (`native-final/defensive-training-review.json`). This includes ordinary Iron Guard absorption, natural training defeat, Torren's rescue followed by a real equipment change, and comparison retention/clearing. The runtime logs passed their checker. Native output had no runtime warnings or errors.

Desktop 1280×800 and minimum 780×720 captures were inspected. Earlier native inspection caught an oversized live summary and legacy item names in the comparison. Both were corrected; the final diagnostic checks all live summary bounds, all six mode/action controls and non-overlap with player vitals at every live capture. The scrolled comparison and its unequal-conditions explanation remain accessible at minimum size.

The packaged existing training regression passed **156 checks**, **933 commands** and **11 replay verifications** across all five disciplines and equipment presets. The adjacent death-recap regression passed **140 checks**, **2,153 commands** and **4 replay verifications**. Headless screenshots are explicitly skipped; runtime log checks passed. Final reports are under `training-final/` and `death-regression/` in this milestone's artifact directory.

All **10 actual archived schema-1 training recordings** from two earlier milestones passed the new verifier, covering all five disciplines with real damage, resource activity and fragment triggers (`legacy-replays.json`). These are historical artifacts, not regenerated recordings claimed as compatibility evidence.

The final macOS package is `artifacts/export/Ashenwake.zip`, SHA-256 `35013330deddaa644b08c286e1e6ed3ced17e2591dea677f021c4d233f9df287`. Its export passed the export-specific checker. The entire unrelated release regression matrix was not rerun.

## Prism review

Prism reviewed the staged changes with Gemini under the user's standing approval and default secret redaction. Reports and individual dispositions are retained in `artifacts/defensive-training/prism-review.json`, `prism-final.json`, `prism-complete.json` and `prism-dispositions.md`. The reported duplicate hit count is absent: each resolved hit increments the count once, with an explicit three-hit test. Historical report compatibility was checked against the ten archived recordings above. Extra damage-resolver calls are deliberate, training-only measurements that preserve the authoritative integer rounding and caps.

The last review also raised report hashing/allocation, modal-input and lookup concerns. A bounded local measurement of full `Step` plus another `Report` read, including existing state/report hashes and replay serialization, measured **0.183 ms p95** over 9,000 passive ticks and **0.070 ms p95** over 2,244 mixed-sparring ticks. Allocation was approximately **104.5/110.8 KB per tick**. This is an optimization opportunity, but the measured run did not substantiate the proposed severe performance regression. Training runs at 30 Hz and retains its deterministic report-hash contract. The report's settings lookup runs only while a paused report is waiting to arm; the input guard deliberately requires release of held accept/attack controls. See `report-performance.json` and the dispositions for exact scope. These timings exclude Godot rendering, were collected with other tests running, and are not target-hardware certification.

## Limits

These are automated checks and native visual inspection, not a human balance study or hardware/controller certification. Training opponents use existing combat definitions. Results reflect the particular movement, timing and exposure of each attempt, not a claim that one build is generally superior. No new persistent report archive, progression reward, matchmaking or difficulty system is included.
