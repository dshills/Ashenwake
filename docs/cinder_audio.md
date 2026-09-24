# Cinder Reach audio

Act III has five original adaptive scores: the ash fields, Extraction Floor, Sealed Foundry, Burning Rain and Furnace Spindle. Tuned steel, furnace drones, drifting ash and industrial percussion distinguish each location. Exploration, combat and boss layers remain synchronized; nearby threats raise the combat pulse. Furnace music grows in phase two and brightens during its actual exposed windows.

Emberlings, furnace brutes and forge sentinels have distinct attack warnings. Burning Rain announces impending storm strikes. Furnace Spindle has separate attack, exposure, second-phase and final-shutdown cues. Metal footsteps sound on extraction, foundry and furnace floors; ash fields and Burning Rain use dirt. Fire hazards use the shared magic impact sound.

Exposure follows the living boss's guarded-to-unguarded transition. The simulation's `BossCoreWindow` event announces the beginning of guarding, so it does not trigger an exposure sound. Loading into an exposed state, pausing, dying and changing rooms do not replay a transition. Phase and shutdown cues take priority over exposure and low-health warnings. Important cues lower the score and regional ambience temporarily; the user's Music and Effects settings remain authoritative.

## Implementation

`OpeningScore`, `OpeningFoley` and `OpeningAudio` share playback across Acts I–IV. Cinder assets are appended to the catalogs with separate synthesis; previous asset indices, seeds and rendering paths remain stable. All music and effects are synthesized in repository source, without downloaded recordings or external generation services. `assets/credits.json` records provenance.

The twenty regional scores each have three aligned 24-second, 80-BPM stereo PCM16 stems at 22,050 Hz. Their combined source peaks remain below 0.631 before mixer gains. The cache holds at most 60 streams, totaling 127,008,000 native PCM bytes. One worker prepares managed samples at a time, while the scene thread installs Godot streams. Playback remains bounded to six music players, six combat foley voices, two footsteps and two warning voices. The foley catalog contains 47 cues. Act V retains its existing ambience.

This is presentation-only work. Core combat, content, commands, RNG, save data and replay formats remain unchanged. Furnace Spindle retains its existing two phases.

## Verification and auditions

`--opening-audio-smoke --output=<fresh-directory>` measures every score and effect, checks repeatability, seams, headroom and cache bounds, and exports audition WAVs. `--cinder-audio-smoke --output=<fresh-directory>` earns Acts I–III through public campaign commands and checks the new event routing, all four Furnace phase/guard combinations, playback controls, save and replay behavior. Add `--capture-cinder-audio` for native rendered evidence; headless runs omit device timing and captures. The package verification script includes both diagnostics.

See [verification](cinder_audio_verification.md) for results. Automated PCM and playback checks do not establish perceived musical quality or headphone/speaker balance; those still require listening to the exported game.
