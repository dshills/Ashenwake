# Cinder Reach audio verification

This pass extends the existing presentation audio through Act III. Core rules, content and archive formats are unchanged. Evidence is retained locally under `artifacts/cinder-audio/`.

## Completed checks

- Pinned .NET build: no warnings or errors. Focused campaign combat, Cinder exploration and midgame combat suite: 158 tests passed.
- Expanded PCM diagnostic: 593 headless checks passed. All 45 music stems and 38 effects were measured; 83 audition WAVs were written. All 57 previous music/effect fingerprints match the prior Act II package exactly.
- Earned Cinder route: 67 headless checks passed across 5,104 public commands, all five regions, both phases and four guarded/exposed state combinations. Four exposure transitions were observed. Save restoration and the standalone replay agree on `DBDA15681BD83042F8460FBF6ECD5B257081FF284E1C802F3E1AECE892F46BB9`.
- Shared native package diagnostic: 607 checks passed, including real playback clocks, pause/resume and six rendered captures. This run preceded the final diagnostic-only route correction and descriptive cue-label edits.
- Final macOS package: all 81 Cinder checks passed with 11 rendered captures and the same campaign/replay hash. Package SHA-256: `d81e89581a4accb34f21cd17a07391dcdd4bfabbea354ca583b207fbaf86e758`. Godot export/runtime log checks, whitespace verification, shell syntax and diff checks passed.
- Act II regression: 49 headless checks passed with its existing save/replay and warning behavior.

Early Cinder diagnostics incorrectly required every overlapping creature warning to be delivered. Event evidence showed that real brute windups were coalesced behind already-playing crypt and storm warnings, consistent with the reserved-voice policy. The final diagnostic separates actual event/coalescing evidence from a clearly labeled read-only fixture that replays a captured earned brute event with an available warning voice. Other earned cue assertions remain. An intermediate navigation experiment and report-field collision were corrected; failed run artifacts are retained rather than relabeled as successful evidence.

## Prism review

Prism/Gemini run `11abd93f3f56c23d58777dface947283` reported zero high, one medium and two low findings. The medium priority concern does not apply: each reserved voice stores its active priority and expiry, rejects lower-priority replacements, and allows shutdown to preempt phase/exposure/heartbeat. The dense-foley fixture exercises that ordering. One low finding proposes reducing diagnostic state hashes; those checks intentionally establish that presentation never changes Core. The other assumes an unused success return, but `Play` returns cue ownership and its callers use that result for fallback sounds. Dispositions are recorded in `prism-dispositions.json`.

The diagnostic correction received a separate Prism review. Its two high findings assume that historical cue coverage should be cleared each tick and that an inactive warning player disappears from the pool. Both conflict with the implementation: coverage is deliberately cumulative until the two warnings have been heard, and `_Ready` creates two persistent warning players. Three medium findings concern bounded diagnostic allocations and the solo player-ID invariant; the low finding assumes a mobile/console AOT export outside this desktop Mono target. These findings require no code changes; detailed evidence and both review IDs are retained in the disposition report.

The final full staged review (`4530f198bd19a1d17b1eedfaaeb97035`) reported one high and one medium finding, both based on incorrect catalog indices. Independent enumeration confirms moss cues at 24–26, emberling at 27 and metal cues at 35–37, exactly matching synthesis dispatch. Metal variants 0/1/2 intentionally produce positive pitch multipliers 0.965/1/1.035; there is no array access. All 38 cues render successfully, and previous PCM fingerprints remain unchanged. Neither finding requires a code change.

## Limits

PCM headroom, loop seams and native playhead advancement are automated evidence, not a listening assessment. Perceived composition, headphone/speaker balance and the full combined legacy/new-effects mix still need listening in the exported game. Rendered captures establish scene and phase context rather than audible output quality.
