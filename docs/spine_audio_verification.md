# Shattered Spine audio verification

Act IV audio extends the shared presentation system without changing Core rules, content, commands, RNG or save/replay formats. Local evidence is under `artifacts/spine-audio/`.

## Completed checks

- Pinned .NET build: zero warnings or errors. Focused campaign combat, Spine exploration/rooms and late-campaign combat tests: 163 passed.
- Expanded headless audio diagnostic: 733 checks passed across 60 score stems and 47 effects, with 107 audition WAVs. All 83 previous audio asset fingerprints match the Cinder package exactly.
- Earned Spine route: 106 headless checks passed across 6,794 public commands, five regional styles, both Warden phases and all four phase/guard combinations. Save restoration and replay match `078385B172F19C6CE69D56BB42BF44216067622D261ECE9B90013F5528586528`.
- Native macOS Spine diagnostic: 120 checks passed, including three aligned advancing playheads, pause/resume, real route events and 11 rendered captures. Its final hash equals the source route and standalone replay. Inspection found and corrected only the diagnostic caption (`CINDER AUDIO` to `SPINE AUDIO`) after this run.
- Final rebuilt macOS package: 747 shared native audio checks and six captures passed. All 83 earlier asset fingerprints also match in this package. Export/runtime log checks, formatting, shell syntax and diff checks passed. ZIP SHA-256: `10bb0d96032cd7256bf44a6657a165244121cfa8135a4e4df243c961fc935744`.
- Cinder regression: 67 headless checks passed, including the shared guarded-boss transition behavior, save/replay and captured-event routing.

Independent integration review caught a regional fault cue whose original .72-second tail exceeded the .667-second follow-up spacing. It was shortened to .55 seconds. A new regression delivers all three cues on one lane at the actual intervals without resetting between them.

The initial Spine route diagnostic expected pre-step hazard countdowns in a post-step view. Core emits events and then increments the combat tick before returning. The corrected checks record both ticks and verify remaining ticks plus elapsed time against authored delays: 30/50/70 for regional faults, 36/54 for Warden oath/fault, and 18 between oath resolution and its follow-up. The failed artifact remains labeled as failed.

## Prism review

Prism/Gemini run `9ece2a30e2d1ac3d2afdf44e3b9a3953` reported no high findings, one medium and one low. The medium concerns hypothetical multiple bosses with conflicting guard states; the authored Warden encounter contains one Warden with Null, and its adds have distinct non-boss IDs. No such dual-boss encounter is introduced. The low concerns the player-ID check; Core's solo session explicitly creates and retrieves the player as ID 1, while cooperative play uses a separate client. Neither requires a code change. Detailed dispositions are retained in `prism-dispositions.json`.

A follow-up review (`520ef86e265f476774f2914dd9cdb618`) of the timing correction reported no high and two medium findings. One concerns strict paired-hazard assertions inside the diagnostic; a mismatch is caught, reported and exits the smoke with failure, while ordinary gameplay uses nullable hazard checks. The other assumes a nullable Warden view can enter the oath branch, but cue selection already requires that actor’s Warden definition and no mutation or await intervenes. No actionable findings remain.

## Limits

PCM headroom, seams, repeatability and playhead advancement are automated evidence, not listening assessments. Perceived composition, headphone/speaker balance and the complete legacy/new-effects mix still require listening in the exported game. Captured event fixtures are separately labeled and do not imply that every overlapping creature call plays during the earned campaign route.
