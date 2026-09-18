# Phase 8 — permanent Borrowed Memory experiment

Status: implementation, automated validation, exact-source packaging and Prism review are complete. Human acceptance of novelty, balance and readability remains open.

Echoes is an explicit, permanent opt-in at the Fracture gate. A new archive preserves the original character. The player can keep their installed Mind or temporarily suppress its effect, approach an actual defeated elite to bind one existing Echo Storm cast, and avoid the hostile Storm warning produced by using it. Finishing the ordinary four-room Fracture after using the Echo earns one cosmetic receipt; the existing endgame authority owns all ordinary rewards. Release, expiry, death, room changes, safe exit and retirement have explicit behavior. There is no reset, daily schedule, additional power reward or required online service.

The wrapper records both choices and outcomes through bounded replay rollover. Saves include content/rules identity, the temporary loan and hazard, original installed anatomy, permanent progression and character-local cosmetic receipts. Existing ordinary archives omit the optional experiment field and retain their hashes. Future nested versions are rejected before recovery could overwrite them. Retirement closes new borrowing while preserving active runs and historical replay policy.

## Verification evidence

- Core focused validation covers ordinary-run equivalence, owned Mind suppression/restoration, proximity and line of sight, actual damage and warning counterplay, expiry/release/death/retry/abandon, active save/replay, receipt idempotency, future-header preservation, original archive preservation and retirement. Additional discipline routes include Gravecaller summons.
- Actual Godot headless and rendered routes each completed 2,516 ordinary commands with hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`: all four rooms, 15/15 drops, bound-memory and active-Storm save/load/replay, Keep my Mind and Release branches, installed Serath Mind suppression, actual completion receipt/cosmetic, and byte-preserved original character after safe exit/resume.
- Rendered evidence in `artifacts/experiment-agent/render-bounded` shows the memory offer, warning, active Storm and cosmetic, with matching frame-state JSON. Labels and geometry supplement color. Captures have a bounded draw wait and the route has frame/command watchdogs.
- The isolated pre-audit full suite passes 302 tests, and its macOS package passes the 32,190-command ordinary endgame route with the unchanged Phase 5 hash, all 17 release UI checks, and the 2,516-command Echoes route. The CLI completes 2,515 commands with five checkpoint/replay verifications. Final counts/package identity after the audit corrections are recorded below. Automated action drivers are deterministic acceptance routes, not independent player studies.

## Review and release boundary

The experiment is engineered as permanent optional local content, with one contract, one borrowed skill, one hazard and one cosmetic. Supporting a regular content cadence, final production assets, a human study on at least three different builds and public distribution acceptance remain separate decisions. See `permanent_experiment_design.md` and `permanent_experiment_contract.md` for the published identifiers, compatibility and retirement contract.

## Audit corrections

An independent audit reproduced a retry failure after actually binding an elite memory and dying in that room: the respawn replaces enemy IDs, but validation still required the historical corpse to be present. Historical memory receipts now survive that replacement; live Offered/Bound memories still require their actual elite source. Two added regressions prove enemy-caused death, retry without another loan, saved receipt preservation, exact restore/replay, and rejection of a forged live source. All 22 focused experiment tests pass after the correction.

The standard `aw endgame validate/compile` command now validates and packages the experiment resource used by the default client. This prevents a fresh `tools/verify.sh` checkout from relying on `experiments.json` left by an earlier optional verification. Export and packaged co-op commands likewise compile it explicitly. Generated script UID companions are ignored while previously tracked UIDs remain tracked.

## Final corrected snapshot

The isolated corrected snapshot `c5286d7676eb4488bbd9bfdaa30ddec9836d9e58` passes exact-source verification for 293 tracked files, a warning-free solution build, formatting, **304 Core tests**, **12 server tests**, and **eight source-gate tests**. The two additional Core tests cover the reproduced retry failure and strict active-source validation. The final exported-candidate identity and routes are recorded after packaging below.

## Prism review disposition

Prism reviewed the complete staged Phase 8 diff with `gemini-3-flash-preview`, using the repository implementation-review rules. Report `8254c328b6f2650e949b0b63194a4615` contains one high and one medium finding. Both are contradicted by the reviewed source; there are no unresolved actionable findings.

- `c00037b50b4c8f5a` claims an unguarded null hazard source and a crash when Echo Storm is cast in an empty room. `CombatProductionEffects.HandleProductionCommand` requires a living enemy target and rejects the command before emitting `CapturedAbilityUsed` if none exists. Damage is queued, and the event is emitted synchronously while that target still exists. `CombatBorrowedMemory.ObserveBorrowedMemory` also explicitly checks `source is null` before accessing `source.Id`. The proposed missing guard is already present. No simulation change is justified by this finding.
- `834bf9fa0d8634c9` cites a 1,000-frame limit in a nonexistent `ExperimentReplayRunner.cs`. The runner is in `ExperimentArchive.cs` and permits 1,800 commands. `ExperimentRuntimeSession` creates a fresh checkpoint and rolls the bounded window at that same limit. `KeepMindChoiceSurvivesReplayRolloverAndMalformedChoiceHistoryRejects` exercises 1,801 steps; the longer CLI/client routes verify overlapping checkpoints. The bound intentionally limits memory without preventing continued simulation or exact verification of each recorded segment.

The raw report, exact reviewed diff and finding dispositions are retained in `artifacts/phase8-review-final`. Existing full-suite and package results apply because this review required only evidence/documentation updates.

The final corrected macOS ZIP SHA-256 is `699395A7C1EBE178B40391D9EDF4729EE8624FA067EDEDA5B9E7C5A1E22A2390`; its manifest identity is `1B23A12B1C469E7ACCA46B69796C6F1E46D36F15EC23F5F1921BDF7C694B703B`. Pre- and post-export source checks pass against `c5286d7676eb4488bbd9bfdaa30ddec9836d9e58`. The actual package completes the full 32,190-command endgame route with unchanged hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`, all 17 release UI checks, and 2,516 Echoes commands with hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. The default build includes both optional entry flows. Evidence is retained in `artifacts/phase8-review-final`; these final evidence notes follow the tested runtime snapshot.

The same final ZIP also passes a two-peer packaged co-op regression: 966 matching snapshots, zero mismatches, ten personal receipts and final hash `29528AF021466367A42487B067D3AC16E28C905984221A14B010396E52F9F486`. The script rebuilt the final dedicated server, obtained fresh one-time tickets after service readiness, and cleaned up its private services. Report: `artifacts/phase8-review-final/coop-package/report.json`.
