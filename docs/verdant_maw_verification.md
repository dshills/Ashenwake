# Verdant Maw verification

Validated on 2026-09-18 with Godot 4.6.2 Mono and .NET SDK 8.0.425 on macOS. Implementation starts from `b38d520a9accea3939dd03276a92ad6df7ccff14`.

## Implemented scope

Act II has four distinct environments, organic ground and obstacle coverings, physical tracking clues, a Rootheart backdrop that responds to feeding roots and victory, regional lighting and three synthesized ambience loops. The occupied arena remains visible after combat until the player travels. A completed-region revisit retains its regional floor and atmosphere. All changes are presentation and diagnostic code; Core rules, content, save formats and random streams are unchanged.

## Evidence

- Solution build: zero warnings or errors. Formatting verification and `git diff --check` passed.
- Core regression suite: **312 passed**.
- The opening journey also passed **45 rendered checks** from the same Mac app, including the shared atmosphere, pause and combat presentation regressions.
- Verdant diagnostic: **135 headless checks**, **138 rendered checks**, and **2,985 real campaign commands**. The route unlocks Act II, completes the ordered hunt and the region, returns to Greyhaven, revisits completed Act II and verifies checkpointed replay.
- The same **138 rendered checks** passed from the exported Mac app. Eleven captures cover the four contexts, three clues, and guarded, exposed, opening and settled Rootheart states. The settled capture gives ordinary actor death clips time to finish.
- Geometry inspection uses actual transformed mesh vertices. Raised architecture stays outside the authoritative room rectangle; every floor remains below Y=0; obstacle art stays inside its original footprint. Static architecture uses 11–14 material batches per observed context, and ground uses 9–12.
- Clues use actual Core anchors, retain quiet tracked remains, expose no future clue, reuse presentation instances and clean up when the hunt ends. Rootheart observes real root counts 3, 2, 1 and 0 and real final defeat. Its bounded animation freezes on pause, settles after victory, and does not replay on restore or when reduced effects are disabled again.
- Rendered MultiMesh checks verify moving, paused and perimeter-only motes; these three checks are explicitly skipped by the headless renderer. Reduced effects removes motes and fog. Audio checks cover Master-bus routing, pause/resume, leaving the region, fixed cache size, deterministic distinct PCM loops and non-silent unclipped samples.
- The Act II diagnostic ends with state hash `768208208F99845EB946150E4394B576F01BB7C3E0E5DD68875DE34A624D41A3`.

The full exported verifier completed successfully, including its Godot log checks:

- **710 checks:** release 33, interaction 17, opening journey 42, Verdant 135, services 19, original character visuals 170, combat feedback 168, appearance 126.
- Campaign/endgame: **32,190 commands**, verified save/replay, final hash `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`.
- Borrowed Memory: **2,516 commands**, verified save/replay, final hash `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`.

Both full-route hashes match the prior milestone.

## Prism status

Prism reviewed the staged milestone with Gemini after the user explicitly approved transmitting these source diffs and confirmed standing Prism approval. Initial review `373299aaa25bf143af9f82897f7d7df6` reported one medium and one low finding. Both were fixed: the three ambience loops are now prewarmed during initial scene construction, and room bounds are installed before atmosphere setup, including immediate same-style resizes while paused.

Follow-up review `48c31aa3ffcea1b22e4f51d5ec5d8baf` reported one medium nullability warning. It is a false positive: `_trails` is a readonly dictionary initialized at declaration and never reassigned. There are no outstanding actionable findings; full dispositions are in `artifacts/verdant-maw/prism-disposition.json`.

After the fixes, the solution build and formatting check passed. The focused diagnostic passed **136 headless checks** and **141 rendered checks**, including preloaded audio and both immediate-bounds cases, with the same 2,985 commands and final hash. Rendered assertions completed without screenshot capture; screenshot-enabled runs timed out waiting for rendering and are not counted as passes. Evidence is in `prism-headless.8t1f0O/` and `prism-rendered.zwE1SZ/` under this milestone's artifact directory.

## Artifacts and limits

- Initial Mac package, before the Prism fixes: `artifacts/verdant-maw/Ashenwake.zip`. The following mouse-movement milestone supplies the refreshed combined build.
- Package SHA-256: `5fb3ab99a14096587d5f86c0b6bc82331396c693333b41fbb831bc551f9c10f5`.
- Exact-package rendered report and captures: `artifacts/verdant-maw/package-rendered.aLgcm3/`.
- Initial rendered inspection: `artifacts/verdant-maw/rendered.Krx3Cv/`.
- Focused headless report: `artifacts/verdant-maw/headless.AB1CxU/`.
- Build, format, Core-test and export logs: `artifacts/verdant-maw/`.

This is procedural art and automated software evidence. It is not hardware performance certification, a listening study, final textured assets or an external player study. Cooperative environment art and prototype landmarks in Acts III–V remain outside this pass. Reproduction steps are in [The Verdant Maw environment](verdant_maw.md).
