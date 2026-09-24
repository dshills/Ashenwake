# Verdant Maw audio

Act II now has five original adaptive scores: the Living Ruins, Plague Village, Briarheart Shrine, Antler Grove and Rootheart chamber. Reeds, hollow timber, seed-rattle percussion and low bowed textures give the region its own sound. Each location has synchronized exploration, combat and boss layers. Nearby threats bring in the combat pulse; the Antler adds its answering call, while Rootheart's layer builds as feeding roots fall and reaches full intensity in its second phase. After the encounter, the score settles back to exploration.

Carnivorous vines, needle swarms, bloom carriers, the Antler and Rootheart have distinct attack warnings. Actual feeding-root deaths produce a tearing timber cue. Rootheart's second phase and final collapse have separate reserved sounds. Forest paths and sacred groves use three moss footstep variations; the village retains stone footsteps. Impacts retain the shared physical, armor and spell sounds.

Boss warnings can replace lesser creature calls. Ordinary impacts cannot take either warning voice; a heartbeat cannot interrupt a phase cue, and Rootheart's final collapse takes priority over both. Critical cues lower the music and forest ambience temporarily. **Settings → Audio** retains independent Music & ambience and Effects controls, including true mute. Pausing freezes playback and musical changes. Leaving a room or restoring a save cancels transient sounds rather than replaying old roots, phase changes or deaths.

## Implementation

The existing `OpeningScore`, `OpeningFoley` and `OpeningAudio` classes now serve the opening, Verdant Maw, Cinder Reach and Shattered Spine. Their names remain stable to avoid duplicating the playback/mixing infrastructure. New catalog entries and separate forest synthesis extend the opening assets without changing their existing sample generation. All music and effects are authored synthesis in this repository, without downloaded recordings or external generation services. Provenance is recorded in `assets/credits.json`.

Music remains 24-second, 80-BPM stereo PCM16 loops at 22,050 Hz. Each of the twenty regions has three aligned stems, with a maximum summed source peak of 0.631 before runtime gain. The native stream cache is bounded to 60 buffers, totaling 127,008,000 PCM bytes when every region has been visited. One worker prepares managed samples at a time; Godot streams are installed on the scene thread. There are still just two banks of three music players, six combat foley voices, two footsteps and two reserved warning voices. The 47-cue foley catalog is prewarmed and cached.

Audio reads Core actors, phase state and emitted events. It never changes combat, progression, gameplay RNG, saves or replay formats. Rootheart has two phases; no extra combat phase is inferred or added. Act V keeps its existing ambient beds. Verdant ambience continues beneath the score at a lower per-player gain, following its duck envelope without rewriting user bus settings.

## Verification and auditions

`--opening-audio-smoke` validates PCM repeatability, headroom, loop seams, distinct samples and cache contracts for the expanded catalogs. It writes exploration/combat/boss audition WAVs for every region and individual effect WAVs.

`--verdant-audio-smoke --output=<fresh-directory>` earns Acts I and II through public campaign commands, presents actual Act II events, and checks music, warnings, pause/reset, save and replay behavior. Add `--capture-verdant-audio` for rendered captures; `--headless` omits device timing and visual evidence. Both diagnostics are included in the package verification script.

See [verification](verdant_audio_verification.md) for results. Automated PCM and playback checks do not establish perceived musical quality, headphone/speaker balance or clipping across every possible combination of legacy and new effects; those require listening to the exported game.
