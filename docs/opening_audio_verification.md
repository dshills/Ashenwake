# Opening audio verification

Verified on macOS with the pinned .NET 8.0.425 and Godot 4.6.2 Mono toolchain. This is a client-only audio pass; content catalogs, Core gameplay, saves and replay formats are unchanged.

## Evidence

The solution builds without warnings. Content compilation and the release engineering audit pass their automated checks; the existing release acceptance gates remain open. The packaged Journey regression passes **347 checks** and verifies two replay segments. The packaged Settings regression passes **140 checks**, including real bus gain/mute, preference restoration, modal/manual/focus pause and scene teardown. These initial regressions preceded the focused audio pause fix; the final audio diagnostic exercises that fix directly.

The packaged opening-audio diagnostic passes **257 native checks** and **243 headless checks**. Both runs earn the opening in 1,725 commands and reproduce state hash `0944B2C0B491A20D63C769A2B94DC7473DE22D83AC9FC639969B4B4BAAD9800D`; the CLI independently replays both archives. Native output includes six captured scenes and both music and positional-foley playhead measurements. Headless runs explicitly skip device timing and do not claim rendered or audible verification. Evidence is in `artifacts/opening-audio/package-native/` and `package-headless/`.

The focused `--opening-audio-smoke` diagnostic renders every authored stem twice and compares PCM bytes. It checks format, frame counts, RMS, peak limits, DC offset, loop seams, distinct fingerprints, non-looping foley endpoints and WAV headers. Its 31 audition files contain fifteen regional mode mixes and sixteen individual foley cues. These are source PCM before the runtime's per-player and user bus gains.

Native playback evidence checks three synchronized music playheads, advancement, frozen positions while paused and continuation on resume. A separate positional voice proves its actual playback position freezes and resumes. Mixer checks cover crossfades, two-bank and voice bounds, late preparation after departure, independent bus mute, duck/recovery, phase priority, enemy warning precedence, pending cue cancellation, low-health hysteresis and deferred delivery, real Core movement footsteps, stationary/dodge/teleport/rollback suppression and load reset without replaying cues.

The earned route uses public commands through Greyhaven, the road, Widow's Crypt, monastery and all Bell Saint phases, then verifies its save and replay hashes. Presentation must leave Core's hash unchanged after every command. A separate Gravecaller encounter earns a real allied cast and checks that it does not produce an enemy warning.

## Review and fixes

Three Prism reviews use Gemini `gemini-3-flash-preview`, default secret redaction and `tools/prism-implementation.json`. Reports (`prism-initial.json`, `prism-final.json`, `prism-shipping.json`) and per-finding dispositions are retained under `artifacts/opening-audio/`.

- Hardened wavetable lookup for an exact cycle boundary. The separate claim of unbounded oscillator phase was not reproduced: every oscillator increment already calls `Fraction`, and rendering is finite.
- Simplified threat detection to one indexed pass over opening actors with integer squared distances. Temporary `Vector3` values were not heap allocations; the new path also skips threat scanning outside the opening styles.
- Retained a cached startup foley warm-up. The measured cold creation time for all sixteen streams was approximately 58 ms, before play, with no synthesis on subsequent attacks or visits. Long music preparation runs on one background worker and takes approximately 0.4 seconds per region on this machine.
- Independent review fixed allied casts producing enemy warnings, low-health crossings being consumed while the phase voice was occupied, and magical opening hazards receiving physical impact sounds. Bell Saint warnings now preempt weaker creature tells, and positional gain caps prevent proximity amplification above authored levels.
- The final review prompted bounded effect-slot cycling instead of an indefinitely increasing index, and a defensive non-finite signal check before PCM encoding. The latter had no failing authored input: every region/stem already rendered reproducibly with finite measurements. Footstep slot selection and cue-delivery detection also tolerate diagnostic counter wraparound.

Native testing caught a Godot timing edge: a positional Play request has a playback handle before its next physics update registers that playback with the audio server. Pausing that request does not retain the pause. The director now attempts the pause and stops any voice whose pause did not take effect, while active voices retain their position. This follows the pinned [3D player implementation](https://github.com/godotengine/godot/blob/4.6.2-stable/scene/3d/audio_stream_player_3d.cpp) and [audio player internals](https://github.com/godotengine/godot/blob/4.6.2-stable/scene/audio/audio_stream_player_internal.cpp), and is covered by native playback checks. The diagnostic also distinguishes an inactive pool slot from a paused playback; Godot reports `Playing` false during a pause.

## Limits

Automated signal and playback checks do not replace listening on headphones and speakers, perceived balance judgments, or an end-to-end audio loopback capture. They do not certify every possible mix of legacy and new effects against clipping. The command-driven campaign runs faster than real time and is not a human combat playtest. Later acts retain their existing ambient soundscapes; this pass establishes the opening's adaptive score and foley system.
