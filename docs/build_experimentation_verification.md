# Build experimentation verification

This milestone adds the Greyhaven training ground and named equipment presets. Training is a disposable copy of an earned build, with no production reconciliation or reward path. Presets are authoritative character transactions using the existing ownership, service, save and replay contracts.

## Core and compatibility

The complete Core suite passed **1,259 tests** and the server suite passed **17 tests**, with no failures or skipped cases. Reports are `artifacts/build-experimentation/tests/core-full.trx` and `server.trx`. The focused training suite passed 15 cases, and the preset run passed 43 new and adjacent cases; these overlap the full suite and are not added to its count.

Training tests exercise five fresh disciplines, real fragment and legendary damage provenance, summon damage, actual resource increases/decreases, capped overkill, stationary targets, reset/close/time limits, rejected build commands, and deterministic replay/report hashes. Controlled effect fixtures are identified separately from fresh-character admission. The preset tests include full arrangement swaps, hands/rings/empty slots, missing/extracted gear, retraining, affix conflicts, malformed state, bounded names, receipt reuse, and an earned Torren rescue followed by outer Endgame save/replay.

The added preset state is omitted when unused, preserving legacy snapshot hashes. Runtime training hooks do not change ordinary combat events, content identities or resource values. No save schema or catalog migration was needed.

## Engine diagnostic

The explicit exported entry point is:

```sh
Ashenwake -- --training-smoke --capture-training --discipline=Vanguard --output=<fresh-directory>
```

`TrainingSmoke` instantiates the shipping `EndgameDirector`, `Sandbox`, training HUD and equipment UI. Each discipline begins with a fresh character and uses ordinary movement to reach the training marker. The test follows the mouse approach-to-interact path, performs actual practice commands, verifies damage and unchanged journey hashes, opens the paused report, verifies stable report node identities on repeated refresh, switches to a group, resets, and returns to Greyhaven. The original journeys are saved/loaded and replayed independently of the practice copies.

A fresh Vanguard earns Torren's rescue through ordinary campaign commands before preset operations. The UI checks save an outfit, remove and restore equipped gear, type gameplay shortcut letters in the name field, invalidate an overwrite confirmation through a real equipment change, reject the stale confirmation, discard an owned item, and show the resulting missing-item error. Closing the panel releases its pause. Saving, loading and opening the main menu during training preserve the original character and end the practice copy where appropriate.

The diagnostic owns its simulation clock and explicitly advances public commands. Native frames use bounded focus/resume and synchronous `ForceDraw`/`ForceSync` before capture. Headless screenshots are explicitly skipped. The terminal-state button check supplies a presentation-only completed report; actual time/kill completion is covered in Core tests. Native button signals and injected key events exercise the controls, rather than claiming an independent human playtest.

The headless pass at `artifacts/build-experimentation/headless-03/training-review.json` passed 143 checks with 933 public commands and 11 verified replay routes. Later diagnostic-only adjustments handle native application-focus pauses before interaction and synchronize the HUD presentation before screenshots.

The accepted native pass at `artifacts/build-experimentation/native-03/training-review.json` passed **171 checks**, with **15 captures, zero skips, 933 public commands and 11 verified replay routes**. The process exited successfully and passed the strict Godot runtime log checker. Visual inspection covered the minimum-size group HUD, report and preset preview. All six skill buttons fit, the resource display matches the live measurement, and the report and preset controls remain readable at 780×720. Earlier native iterations are superseded: the first exposed diagnostic focus pausing, and the second exposed stale HUD sizing in the capture harness.

The same exported package passed the existing native appearance/equipment regression (**722 checks, 37 captures, 1,477 commands**) and skills regression (**78 checks, 15 captures, 5,119 commands**). Their reports are `native-appearance/appearance-smoke.json` and `native-skills/skills-review.json` under the milestone artifact directory. Both exited successfully with clean strict runtime logs.

The solution build and final formatting verification passed. The exported `artifacts/export/Ashenwake.zip` has SHA-256 `3dd77d9203ac704c2eed9ad914f86ec3d90235c6d96b40784193a210323941d8`. The export log passed the export-specific checker, which permits the known Godot 4.6.2 Android editor-settings shutdown message after a successful export; runtime checks use the strict checker. The complete export script's unrelated regression matrix was not rerun.

## Prism

Prism reviewed the staged implementation and the final follow-up through Gemini, with default secret redaction and the user's standing approval.

- Run `8325fff1b8ae198be0577b147c94f888` reported report-node rebuild overhead. Opening the report already pauses simulation, so the claimed per-render-frame path was absent. An explicit dirty-measurement guard was added as well; the engine diagnostic verifies unchanged node identities under repeated refresh.
- Run `0c9422d4296e6a8c60b0072dcf6b9b6a` reported per-frame report hashing in the bounded offline replay verifier and preset hashing during UI refresh. The verifier intentionally checks every command's combat and measurement result. Preset refresh returns immediately when the panel is closed and holds a modal pause while open; its bounded hash invalidates stale confirmations on user operations. These scope and execution-path findings require no further production change.

The raw reports and finding-by-finding dispositions are `artifacts/build-experimentation/prism-implementation.json`, `prism-implementation-disposition.json`, `prism-final.json`, and `prism-final-disposition.json`. No actionable review issue remains.

## Limits

Passive targets cannot exercise effects that require incoming attacks or elite enemies. Training damage is a measurement against zero defense, not an estimate of every enemy's damage taken. Amplifiers remain included in the skill they modify; no counterfactual contribution is invented. Resource measurements distinguish gross increases and decreases, including Heat and passive cooling.

The diagnostic is bounded and uses fresh disposable characters. It does not establish human combat feel, build optimality, long-session rendering performance or other hardware/controller acceptance. The release checklist remains separate.
