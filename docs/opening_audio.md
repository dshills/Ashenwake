# Opening audio

Greyhaven, the Ashen Road, the monastery, Widow's Crypt and the Bell Saint sanctuary now have original regional music. Bowed strings, breathy choir textures, incomplete melodies and worn bells share an eight-bar motif. Nearby threats bring in percussion and an ostinato; the Bell Saint adds a third layer that grows through its phases. After combat, the score settles back into exploration.

The road uses granular dirt footsteps. Greyhaven and the interiors use stone and armor foley. Footsteps follow actual movement, including mouse navigation, and stop while standing, paused, dodging or changing rooms. Weapon, spell and barrier impacts have separate sounds. Funeral guards, memory archers, crypt creatures and the Bell Saint have distinct warning signatures. The second and third boss phases have separate cues; a double heartbeat marks crossing below one-quarter health, with healing hysteresis and a cooldown.

Important warnings lower the music temporarily. Creature warnings and critical phase cues have reserved voices so ordinary impacts cannot cut them off. **Settings → Audio → Music** controls both music and regional ambience; **Effects** controls footsteps, combat and warnings. Zero volume mutes that channel. Pausing through menus, manual pause or focus loss freezes playback; resuming continues the current phrase. Loading restores the current situation without replaying old warnings.

## Implementation

This pass covers the solo opening campaign, from Greyhaven through the Bell Saint. The later [Verdant Maw audio pass](verdant_audio.md) extends this shared system through Act II; Acts III–V retain their existing ambient beds. Existing attack, loot and legendary sounds continue alongside the new foley. Core content, commands, RNG, saved data and replay formats are unchanged.

`OpeningScore` composes five opening sets of three synchronized stereo PCM16 stems at 22,050 Hz. Each loop is 24 seconds at 80 BPM. Circular finite reverb preserves tails across the loop boundary. The three source peaks sum to at most 0.631 before mixer attenuation. Including the five Verdant sets, the cache holds at most thirty streams, or 63,504,000 bytes of native PCM. One background task at a time prepares pure managed samples; Godot streams are created on the main thread. Subsequent visits reuse cached streams.

`OpeningAudio` crossfades between two banks of three music players. Combat and phase changes adjust stem gains without restarting the phrase. Six positional combat voices, two positional footstep voices and two non-positional warning voices form a fixed pool; the existing eight generic combat voices remain separate. A listener at the player uses the camera's orientation, keeping the isometric camera's height from making nearby impacts inaudible. Ducking uses player gains and never rewrites the user's bus volumes.

All audio is authored synthesis in source, with no downloaded samples or external generation service. The asset inventory records provenance; existing release ownership and distribution decisions remain unchanged.

## Checking the pass

Build and export with the repository's pinned .NET and Godot versions. The exported application routes `-- --opening-audio-smoke --output=<fresh-directory>` to the audio diagnostic, also included in `tools/export.sh`. It writes PCM measurements, audition WAVs and engine/campaign evidence. See [verification](opening_audio_verification.md) for completed checks and remaining listening work.
