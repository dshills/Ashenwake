# Death recap verification

This milestone adds bounded runtime incoming-damage observation and a paused recovery interface to the shipping solo campaign/endgame director. It preserves the existing damage, checkpoint, reward and attempt rules. [Player controls and measurement contract](death_recap.md).

## Evidence scope

Core tests verify actual capped health loss, barrier/immune exclusion, fatal-hit identity, status damage provenance, dead-source attribution, harmful conditions, bounded recent history, preservation across permanent-build projection and rollback, and campaign recovery/endgame retry behavior. Save snapshots and command replay retain their existing hashes; transient recaps are intentionally absent after load.

The native diagnostic begins with a fresh Vanguard and uses ordinary enemy attacks to cause a campaign death. Endgame access comes from the maintained campaign-complete archive, followed by ordinary Fracture entry and three actual deaths. It checks the restored checkpoint, retained progress, pause ownership, repeated-action guards, restored dead-run messaging, retry, exhaustion, return to Greyhaven, menu cleanup, save/replay, and default/minimum-window presentation. It does not fabricate damage history or health for those routes. God Hunt recovery shares the runtime and UI path; the native route does not claim a separate earned God Hunt playthrough.

## Results

The solution build passed with zero warnings/errors, and final formatting verification passed. The complete Core suite passed **1,286 tests** and the server suite passed **17 tests**, with no failures or skips (`artifacts/death-recap/tests/core-full.trx` and `server-full.trx`). The ten new recap cases are included in that Core total.

The headless recovery diagnostic passed **140 checks**, **2,153 explicit commands** and **4 save/replay verifications** (`artifacts/death-recap/headless-03/death-recap-review.json`). The packaged native diagnostic passed **147 checks**, the same command/replay counts, and **7 captures with zero skips** (`native-final/death-recap-review.json`). Both passed the runtime log checker. The native run produced no runtime errors or warnings. Default 1280×800 and minimum 780×720 captures were inspected, including the scrolled recovery consequences and restored-save unavailable-history message. Early runs caught and corrected initial wrapped-content minimum-size expansion and a shutdown call after child tree exit.

The packaged headless menu regression passed **119 checks** (`front-menu/front-menu-review.json`). Its deliberate directory-at-file-path failures exercise safe save publication and produce expected warnings. The final training regression passed **156 checks**, **933 explicit commands** and **11 replay verifications** across all five disciplines and equipment presets (`training-final/training-review.json`). A verbose rerun attributed its immediate-exit warning to three audio streams and their playback objects; releasing those streams before quitting removed the warning. Both regressions passed the runtime log checker; final training emitted no warnings. These adjacent headless runs explicitly skip screenshots.

The final macOS package is `artifacts/export/Ashenwake.zip`, SHA-256 `e5cada080ff020e362f5eb9843389803a2f4769a6a8eec79b0521780178ee9bc`. Its export passed the export-specific log checker. The full unrelated release regression matrix was not rerun.

## Prism review

Prism reviewed the staged implementation with Gemini under the user's standing approval and default secret redaction. The first two reviews raised smoke-flag and save/load concerns that do not match the execution paths: `_smoke` is enabled only by `--endgame-smoke`, and transient-report preservation is restricted to build projection/rejected-command rollback. The passing shipping-director diagnostic exercises both automatic opening and save/load at the same encounter/tick, asserting null damage history after load. A suggestion to move English display labels out of Core is deferred localization work; raw identities are retained and existing combat-view contracts already expose names and encounter guidance.

Review reports and individual dispositions are retained under `artifacts/death-recap/`. No changes to damage rules, serialized state or replay hashes were needed to address these reports.

The last staged review (`prism-complete.json`) reported no high-severity issues, one medium content-maintenance suggestion and one low layout concern. The layout suggestion was implemented by avoiding unchanged position/size assignments. Moving attack-specific hint text into authored content remains a future localization/content-maintenance improvement: the current mapping only selects explanatory text and changes no combat or recovery rules. Existing authoritative encounter guidance is already read from Core.

## Limits

Damage history is bounded and local to the running encounter. It is explanatory telemetry, not a persistent combat log or a causal attribution of every defensive stat. Scripted commands and UI signals are automated evidence, not an independent human playtest or hardware/controller certification.
