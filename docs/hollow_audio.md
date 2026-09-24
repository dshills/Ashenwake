# Hollow Night audio

Hollow Night completes adaptive music across all five campaign acts. Repeating Rooms, Identity Memory, Unremembered Vault and Breach Heart each have a spectral score built from distant choir textures, fractured melodies, low pulses and sparse echoes. Three synchronized layers move between exploration, nearby combat and the finale. The Heart's score grows through its three phases, with a quieter first-phase layer while its seals keep it shielded.

Doubled shadows and breach echoes have distinct calls. Causal echo hazards have their own warning, while memory archers retain their established signature. Breach Heart announces its initial echo, then the next active attack when the preceding hazard resolves: a returning echo in phase two, or a sweep followed by the return in phase three. Cancelled hazards cannot leave an independent audio timer running. The short echo and sweep cues fit the final sequence's twenty-tick spacing.

Real seal deaths produce a separate breaking sound. The first actual shielded-to-exposed transition has a critical cue; loading into an already exposed state does not replay it. Phase two, phase three and final containment have separate sounds. Mirrorborn copies share the boss definition, so presentation consistently selects the original encounter actor by its lowest ID, including after death. A surviving copy cannot inherit the original's music, exposure, phase or containment cues.

Critical cues temporarily lower music and regional ambience without changing user bus settings. Boss tells outrank lesser creature calls; phase and containment sounds have a separate reserved voice. Simultaneous creature warnings may coalesce. Pausing holds playback, and room changes or save restoration cancel transient effects. Footsteps retain the shared stone variations.

## Implementation

`OpeningScore`, `OpeningFoley` and `OpeningAudio` remain the shared playback system. Four new styles and eleven cues are appended, preserving earlier indices, seeds and synthesis paths. All audio is repository-authored synthesis without downloaded recordings or external generation services; provenance is recorded in `assets/credits.json`.

Twenty-four regional scores each contain three aligned 24-second, 80-BPM stereo PCM16 stems at 22,050 Hz. Source peaks sum to at most 0.631 before runtime gain. The native score cache is bounded to 72 buffers, totaling 152,409,600 PCM bytes. One worker prepares managed samples at a time, and Godot streams are created on the scene thread. Playback still uses six music players, six combat foley voices, two footstep voices and two warning voices. The foley catalog contains 58 cues.

Core combat, content, progression, RNG, commands, saves and replay formats are unchanged. Audio observes actual events and actor/hazard views. It does not invent extra boss phases or change when a seal exposes the Heart.

## Verification

`--opening-audio-smoke --output=<fresh-directory>` validates every score and effect, checks repeatability, loop seams, headroom and cache limits, and exports audition WAVs. `--hollow-audio-smoke --output=<fresh-directory>` earns the campaign through Act V and its ending, including the optional vault. It checks all four themes, actual boss phases and seal events, sequential warnings, pause/reset/mute, save and replay consistency. Separately labeled fixtures verify captured-event routing and copy isolation without claiming that every overlapping call plays during the earned route.

Add `--capture-hollow-audio` for native scene captures. Headless runs omit audio-device timing and images. The package verification script includes both diagnostics. See [verification results](hollow_audio_verification.md). Automated PCM and playhead checks do not establish perceived musical quality or listening balance; those require listening in the exported game.
