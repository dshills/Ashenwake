# Shattered Spine audio

Act IV has five adaptive scores for Bone Causeway, Contract Hall, Oathkeeper’s Archive, Divine Memory and the Covenant Warden’s court. Deep horns, stone percussion and restrained choral textures give the present-day courts their sound; Divine Memory has a warmer musical palette. Exploration, combat and boss layers stay aligned while nearby enemies and the Warden’s current phase and guard state change their levels.

Oath giants, contract keepers and bone sentinels have distinct warnings. The Warden announces its oath first, then the approaching fault when the oath actually resolves. Its exposed window, second phase and final defeat have separate cues. Numbered regional faults receive a first warning and follow-up beats as earlier faults resolve, including Divine Memory’s reversed lanes. Follow-ups require the next hazard to remain active; there are no independent audio timers to outlive cancelled attacks. Floors retain the shared stone footsteps.

Boss warnings outrank ordinary creature calls. Phase and victory sounds have a separate reserved voice, and victory outranks exposure and heartbeat. Simultaneous creature warnings can coalesce to keep the mix readable. Important cues lower both the score and the regional ambience without changing user volume settings. Pausing holds playback; restoring a save or changing rooms cancels transient cues and baselines guard state.

## Implementation

The existing `OpeningScore`, `OpeningFoley` and `OpeningAudio` classes now cover Acts I–IV. Append-only catalog entries and separate synthesis preserve earlier PCM. All music and effects are authored synthesis in repository source, without downloaded recordings or external generation services. Provenance remains in `assets/credits.json`.

Twenty regions each have three synchronized 24-second, 80-BPM stereo PCM16 stems at 22,050 Hz. Summed source peaks remain below 0.631 before mixer gains. The native cache holds at most 60 score buffers, totaling 127,008,000 PCM bytes. Managed preparation uses one worker at a time, with Godot stream creation on the scene thread. Playback still uses two banks of three music players, six combat foley voices, two footsteps and two warning voices. The foley catalog contains 47 cues. Hollow Night retains its existing ambience.

Audio observes Core events, hazards and actor state. It does not alter rules, content, RNG, commands, saves or replay formats. The Covenant Warden retains its two existing phases. Exposure follows an actual living guarded-to-unguarded transition, not an attack-start event or a loaded snapshot.

## Verification

`--opening-audio-smoke --output=<fresh-directory>` checks all PCM, loop seams, repeatability, headroom and cache bounds, and writes audition WAVs. `--spine-audio-smoke --output=<fresh-directory>` earns Acts I–IV using public campaign commands, visits the Archive and Divine Memory, and checks sequential hazards, boss states, save/replay, pause, mute and reset behavior. Separately labeled fixtures replay captured earned events with a free warning voice to verify routing without requiring every overlapping call to play during the campaign route.

Add `--capture-spine-audio` for native captures; headless checks omit device timing and images. Both diagnostics are included in `tools/export.sh`. See [verification results](spine_audio_verification.md). Automated signal and playhead checks do not establish perceived musical quality or headphone/speaker balance; those require listening to the exported game.
