# Reward audio and Quiet mode verification

Evidence is retained locally under `artifacts/reward-audio/`. This change affects presentation and device preferences; Core rules, RNG, progression archives and replay formats are unchanged.

## Completed checks

- Pinned solution build: zero warnings or errors. Targeted equipment, loot-management and secret-chamber tests: 131 passed.
- Reward audio: final package passed 53 headless and 54 native checks. Twelve distinct repeatable PCM cues satisfy duration, headroom and endpoint checks. Diagnostics verify material selection, two-voice bounds, priority, collection coalescing, separate gear gestures, bus routing/mute, nested reset and native playhead movement. Twelve audition WAVs are exported per run.
- Equipment/appearance: 808 source headless checks and 858 final-package native checks passed, with 47 native captures. These exercise real equip/unequip buttons, 41 viewport drag gestures, incompatible releases, stale authoritative denial, hovering, Escape, focus/menu cancellation, save/load, cosmetic isolation and replay.
- Combat feedback: 949 headless checks passed. Added checks cover Rare/Relic routing, bounded pickup receipts, an earned equipment pickup, repeated event delivery, restored inventory and exact combat replay.
- Secret chambers: 254 headless checks passed across all three earned chambers. Clues and unclaimed treasure stay quiet; actual claims produce one receipt; duplicate presentation and loading claimed state stay quiet. The final replay matches.
- Settings: 162 headless checks and 200 final-package native checks passed, with 23 native captures. Quiet mode persists, restores to off, loads safely from older preferences, preserves independent volume/mute choices and does not stack effects across scene restarts. Native output logs are clean.
- Export log, formatting, shell syntax and staged diff checks passed. Accepted macOS ZIP SHA-256: `6eacb8ab2fd4f06fcbf27e05c59be7f04fece506d2d61bfebbd4315764114971`.

## Measured mix

The final package captures actual audio before and after the Master DSP chain. Stress signals remain muted downstream, including while the final buffered audio drains. Three category-bus tones produce a measured input peak of 2.4000, exceeding digital full scale. Normal-mode output peaks at 0.891258 (−1 dB within float tolerance); Quiet-mode output peaks at 0.483490. A low-level detail fixture retains its steady RMS within 0.3% between modes.

A representative busy Hollow encounter combines all three score stems, regional ambience, legacy combat audio, phase/sweep/seal/impact/footstep sounds and the new secret treasure cue at their declared gains, before music ducking. Output peak falls from 0.402544 to 0.205060 with Quiet mode. This demonstrates the shipped global signal path for these fixtures; it does not certify perceived balance for every possible encounter or listening device.

## Review

Independent integration review found that shared starter gear needed discipline-aware material selection. The fix makes Vanguard plate sound metallic and non-Vanguard hoods use cloth, while retaining explicit named-item materials. Four targeted checks cover those distinctions.

Prism/Gemini review `a82f08adde792747a452f046f98d48c1` reported zero high, two medium and one low finding. No actionable findings remain:

- The resource-disposal finding assumes disposing a Godot `RefCounted` wrapper bypasses native reference counting. Probe streams are privately owned; players stop and clear their stream references before the muted drain and scope disposal. Native measurements and teardown pass cleanly.
- The index finding assumes another thread rearranges bus effects between insertion at index zero and its immediately following enable call. Those operations run synchronously on the scene thread; the project has no concurrent bus-list mutation. Subsequent toggles locate effects by name, and reload/idempotency checks pass.
- The wait finding assumes audio failure prevents the awaited signal. The bounded cleanup waits for `SceneTree.ProcessFrame`, not audio completion; missing audio is separately measured and fails the diagnostic.

Detailed dispositions are in `prism-dispositions.json`. Screenshots confirm the Audio page at supported sizes, the Quiet mode control, equipment interactions and unchanged presentation. The diagnostics do not replace listening tests on speakers/headphones or a full human campaign playtest.
