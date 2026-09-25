# RC1 validation record

This record covers preparation of Ashenwake **1.0.0-rc.1** from the prior pet milestone `04d784f`. It is not public-release acceptance. See [scope and independent playtest criteria](release_candidate_1.md).

## Changes under validation

- Correct the roaming-champion historical catalog regression by comparing the frozen generations involved, while separately retaining exact current champion definitions and reward mappings. The later equipment-set additions remain covered.
- Initialize permanent pets before new-character and imported-character first durable saves. The native front-menu diagnostic caught a live/save hash mismatch caused by enabling pets only during the subsequent refresh.
- Label the application and assemblies `1.0.0-rc.1`; use numeric `1.0.0` in macOS bundle metadata and show the prerelease label on the main menu.
- Extend the immutable release fixture inventory with actual pre-pet and pet-owning client archives. Preserve original bytes and all logical progression; distinguish frozen-document integrity checks from executable restore claims.
- Add pet, secret/champion and equipment-set visual sources to the asset inventory without inventing distribution approval.
- Rebuild release helpers from the reviewed source before generating content or attestations, and test a fresh extraction of the exported Mac ZIP using its exact executable.

## Evidence

Raw evidence is retained in `artifacts/rc1/`. The opening audit passed its maintained fixtures, ten-session/9,000-tick checkpoint-and-replay soak, and projectile-ceiling check. These are bounded simulation checks, not rendered performance or a long hardware playtest.

The corrected catalog plus related equipment-set selection passed 27 tests (`artifacts/rc-champion-catalog-tests.log`). Expanded release diagnostics passed eight tests. The full Core suite passes **1,769/1,769** with no skips (14 minutes 46 seconds), and the server suite passes **17/17**. Formatting, eight source-identity tests, seven release-pipeline tests, the log-checker test, Go vet and Go tests pass. The final fixture audit passes all **20** entries (`readiness.json`).

## Remaining acceptance

Independent playtests and pacing assessment, additional OS/GPU/physical-input coverage, longer rendered sessions, owner-approved distribution/credits and final signing/install/update acceptance remain open. No public release or tag is authorized by a passing automated report alone.

## Prism review

Prism/Gemini reviewed the complete staged RC changes with default redaction (`51d7df4c383be74461246229b6c4a984`, `artifacts/rc1/prism.json`). It returned zero high findings and two medium suggestions, both checked against the execution path:

- The claimed server reconciliation race does not apply to this offline solo director. The client issues a synchronous authoritative Core `EnablePets` command before writing and validating the new/imported save. Core owns its rules and ledger. There is no network transition or the suggested `EndgameService`/`GameStateMachine` in this path. The native front-menu test now verifies durable/live equality and original/Echoes separation.
- The new generic endgame fixture check compares the entire loaded authoritative state with the original state after the explicitly supported catalog rebind, then compares the round-trip hash. Campaign progression, equipment, resources and pet state remain part of that comparison and are validated by the loader. The hub/tier-10/five-hunts assertions remain specific to the completed PhaseFive fixture; imposing them on a genuine early-game save would be incorrect.

No actionable review finding remains.

The accepted native menu run (`front-menu3`) passed **146 checks** and produced **27 captures**, covering new/imported characters, original/Echoes switching, archive preservation, failures/recovery and compact menus. The log passes the engine-error checker; deliberate I/O-failure warnings belong to the diagnostic. The RC version and compact main menu were visually inspected.

## Campaign and persistence measurements

All five fresh disciplines complete the main campaign under the earned-build policy on seed 42, totaling **23,195 public commands**, with no failed route or death (`campaign-main/summary.json`). Each route covers the 15 main encounters and validates replay/restore segments and its final save. This does not measure first-time human difficulty, crafting choices or time spent learning. No balance numbers were changed from this result.

The continuing endgame character completes ten tiers, five God Hunts and one extra tier-ten Fracture: **25,756 commands**, **43 save/load checkpoints and replay checks**, 11 completed Fractures, five hunts and a matching final state (`persistent.log`). Full-command p95 was 25.22 ms on this run; disk, replay verification, rendering and the driver are outside those samples. Managed memory retention is diagnostic evidence, not a leak certification.

## Packaged validation and follow-up fixes

The first complete package attempt used source tree `1d6aabdd8174eb88af41cd726814a7c8b08c9485` (commit `d0a90bd`). Its main campaign/endgame route passed **34,685 commands**, tier ten, five hunts, retry, abandonment, recovery, attunement, death save/reload and legacy import; the recorded replay matched. Release controls and death-recovery checks passed. The fail-closed package gate then stopped at Anatomy inspection, so this attempt did not create an accepted candidate manifest.

The Anatomy failure was diagnostic timing and outdated navigation, not a lost reward or changed character state. A click occurred before deferred overlay layout settled; an old ground coordinate subsequently selected the newer training entrance. The diagnostic now waits for layout, checks native button activation and selects genuinely clear ground. Its reward, equipment, travel and replay assertions remain.

The run also exposed WAV/playback resources still attached during world shutdown. `OpeningAudio` now stops and detaches its owned music/effect/warning streams on exit, and the audio diagnostic checks detach/re-entry behavior. The brief before/after engine reproduction exits cleanly after the fix. The focused Anatomy run passed 140 checks with two matching replay segments, and the audio run passed 867 checks including detach/re-entry assertions with a clean verbose exit. Additional diagnostic shutdown cleanup and the rebuilt package remain under validation.

The continuing package triage found an intermittent visual diagnostic exit crash after its assertions passed. The macOS crash report places it in native C# binding finalization. Visual and Anatomy diagnostics now release their owned scenes and allow engine frames before reporting completion and quitting. Three focused visual runs pass all 171 assertions with clean verbose exits. Three retries of the original binary also passed, so these repetitions alone do not establish that the intermittent engine crash can never recur; the rebuilt package must still pass its process-exit gate.

The final focused Anatomy teardown run passes **141/141 checks**, 1,577 setup commands, 89 native-input commands and two matching replays. Its verbose log contains no warnings, errors or retained-object lines (`anatomy-teardown.log`). Final follow-up formatting passes.

Prism reviewed the follow-up changes (`cb308c7f93562af6d705ffdfd4de9fb0`, `prism-followup.json`) with no high findings. Its two medium suggestions were inspected: audio collections are already initialized at field declaration and banks are added only after their player array is complete, so the alleged uninitialized-collection exception does not apply. The six candidate ground coordinates are intentional bounded test fixtures; each is checked against current collision geometry, interaction/service ranges, viewport visibility and GUI obstruction, and the resulting native movement is asserted. A future layout that invalidates every candidate should stop this regression test for review. No production navigation change or unbounded coordinate search is warranted for the frozen RC.

## Accepted local test candidate

The full rebuilt package gate exits successfully from reviewed commit **`00c4cbf50fdb9fd0884ccbf7af8e98445781dc83`**. It exports fresh bytes, passes the 34,685-command campaign/endgame route and matching replay, then passes **all 43 packaged feature diagnostics** with their required process exits, engine-error checks and replay checks. The Anatomy, visual and opening-audio packaged logs have no warnings or errors. A separate native-window run of these same exported bytes passes **119 menu/input checks**, including durable new/imported saves, recovery and original/Echoes isolation (`final-native-menu/`).

| Candidate field | Value |
| --- | --- |
| Version | `1.0.0-rc.1` |
| Source commit | `00c4cbf50fdb9fd0884ccbf7af8e98445781dc83` |
| Local candidate | `artifacts/release/candidate.Lp7b5g/` |
| App archive | `package/Ashenwake.zip` (139,769,325 bytes) |
| Archive SHA-256 | `BAB65E75CC66E35DC0929B05BF73EDD1F1306164C48890345742277575C7A519` |
| Manifest file SHA-256 | `40AE3A4CBD20FA7F41F11D7E325C35C28F59FB840DEEA940BEA693A6452847E7` |
| Tested extraction | `artifacts/package/e8956a90b8a8.0kG3Tl/` |
| Platform | macOS universal export, exercised on this Apple Silicon host |
| Public release accepted | **No** |

The candidate includes runtime notices, source/content/asset identity, a verified 11-file package manifest and the packaged diagnostic evidence. Additional suite, audit, campaign, native-menu and Prism records are retained under its `evidence/rc1/`. The manifest covers `package/`; diagnostic evidence is retained separately. This record is a later documentation commit and does not change the source identity of the tested app.

Known limitations remain visible: 16 older feature diagnostics emit a generic ObjectDB shutdown advisory while passing their assertions and process/error gates; this is not a claim of warning-free shutdown across every diagnostic or a blanket leak certification. The front-menu warnings are deliberate I/O fault injection. Headless audio reports explicitly skip native playhead/pause measurements, and automated routes do not assess perceived audio, human pacing or rendered performance. Independent playtests, longer rendered sessions, remaining lifecycle-warning triage, hardware coverage and owner-approved distribution/signing remain open before public 1.0 acceptance.
