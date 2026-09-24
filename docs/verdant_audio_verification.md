# Verdant audio verification

Verified on 2026-09-24 using .NET 8.0.425 and Godot 4.6.2 Mono on macOS, starting from `6dc4c59`. This is a client-only audio change; Core rules, content catalogs, save formats and gameplay RNG are unchanged.

## Signal and compatibility evidence

The expanded opening diagnostic passes **443 headless checks** in source and in the exported app, plus **457 rendered checks** in the exported app with six captures. It generates each of the 30 stereo music stems twice, verifies identical bytes, 24-second alignment, finite/headroom measurements, DC offset and loop seams. It also checks all 27 non-looping effects for reproducibility, distinct hashes, silent endpoints, no full-scale samples, and bounded cached streams. There are **57 audition WAVs**: three regional mixes for each of ten styles, and 27 individual effects.

All **15 original opening stems and 16 original effects** match the PCM hashes recorded in the previously verified opening package. The comparison is retained in `artifacts/verdant-audio/opening-pcm-compatibility.json`. This compares generated sample bytes, not merely catalog names or source declarations.

The solution builds with zero warnings/errors. Changed C# formatting and shell syntax checks pass. The focused campaign-combat, Act II exploration and midgame-combat test selection passes **155 tests**, zero failures or skips. The full Core suite was not rerun for this presentation-only change; the preceding playability audit passed all 1,597 tests.

## Earned route and playback

The final source Verdant diagnostic passes **49 headless checks** and **59 rendered checks**, including seven captures. The exact exported Mac app also passes **59 rendered checks**. Each route earns Acts I and II in **3,370 public commands**, including Briarheart Shrine, the Antler hunt, three feeding-root deaths, both Rootheart phases and final victory. Every presentation step leaves Core's hash unchanged. Final save restoration and continuous replay agree on:

`81ED45D98DCBB5D7FA880A2A6D76E8A433795358775A034970914D164F622024`

The CLI independently replays the recorded native route with the same hash. The original opening route also passes in the expanded library and package, completing 1,559 commands; those route counts reflect the current campaign content rather than the earlier audio milestone's historical route.

Checks cover:

- All five regional styles and creature warning streams selected through actual earned events.
- Stronger Rootheart music after the first root falls, without restarting the musical phrase or changing Core state; full intensity in phase two and release after victory.
- Six bounded music players and ten foley/warning voices, critical reservations during dense effect bursts, boss precedence over lesser calls, finale precedence over phases/heartbeat, and duck recovery.
- Native aligned music playheads advancing, freezing on pause and continuing on resume.
- Independent settings-bus mute without score restart; forest ambience ducking while the saved Music bus gain remains unchanged.
- Pause freezing envelopes/transients, departure releasing banks and tails, return without replaying one-shots, and restore/reset of the defeated boss without replaying victory.
- Two explicitly constructed, read-only presentation fixtures for Rootheart's Venom impacts. They require one newly assigned spell sound, no physical-impact stream, and unchanged Core state. They do not claim the earned route was hit by both hazards.

Separate fixture evidence is labeled in the diagnostic report. The source captures confirm the intended arena and Rootheart phase are being presented, but screenshots are not evidence of audible quality.

## Review

An independent implementation review found `campaign.root_spores` missing from the magical-impact mapping. It is now classified with the other Venom attacks, and both Rootheart hazard routes have explicit presentation regressions.

Prism/Gemini review `8b703b2cbcf3f9a0acfa618787253814` used default secret redaction and `tools/prism-implementation.json`. It returned zero high, two medium and one low findings. Each was checked against the implementation:

- **Equal-priority creature calls:** the single creature-warning voice intentionally coalesces equal-priority calls until their tail ends, avoiding repeated interruption. Boss calls can preempt lesser tells. Critical phase cues use a separate voice and may replace earlier phases, while collapse outranks both. The review conflated creature and phase slots; the behavior is deliberate and covered by dense-warning checks.
- **Dead-root stability:** `CombatSession.View` projects the authoritative actor collection, not visible meshes or camera range. Dead enemy feeding roots remain in that collection; the automatic removal in `Step` applies to dead allies. Moving the camera or consuming a corpse does not remove an enemy actor. Deriving intensity from this view also restores the correct state without an audio-only saved counter.
- **String comparison cost:** cue dispatch uses a fixed 27-entry catalog and bounded voices, with no observed performance defect. The proposed enum rewrite is speculative optimization and would not change the tested behavior.

No actionable Prism findings remain. The raw review is retained in `artifacts/verdant-audio/prism-review.json`.

## Artifacts and limits

- Source results: `artifacts/verdant-audio/opening-headless/`, `verdant-headless-final/`, `verdant-native/`.
- Exact-package results: `artifacts/verdant-audio/package-opening-headless/`, `package-opening-native/`, `package-verdant/`.
- Auditions: `artifacts/verdant-audio/package-opening-headless/auditions/`.
- Updated playable archive: `artifacts/export/Ashenwake.zip`.
- Archive SHA-256: `d64be9881fe2fa9170bd7f5d3938076d481cd41b76320ad7e1dd902c3d63dc1d`.

Export and successful diagnostic logs pass the repository's Godot log checker. PCM checks and native playback clocks do not replace listening on headphones/speakers or audio loopback capture. Simulated combat runs faster than wall time and does not measure human encounter experience. No new art, gameplay, networking or release-certification claims are implied by this audio verification.
