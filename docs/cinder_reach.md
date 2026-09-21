# The Cinder Reach

Act III gives Vael's industrial landscape four distinct settings built around the existing encounters:

- **Cinder fields:** black-glass city ruins, cooled volcanic paving and lava channels outside the fighting floor.
- **Extraction Floor:** gantries, pipework and industrial machinery surrounding an inlaid work floor.
- **Furnace Spindle:** an abandoned forge and a large extraction engine framing the boss arena.
- **The Burning Rain:** damaged storm collectors and ash-covered ruins accompany the existing timed Resonance Storm.

The Furnace Spindle's backdrop reads the actual boss's defense window and live vent warning geometry. Its armor plates close while the core is guarded; horizontal or vertical vents open for the corresponding announced attack. The boss's on-screen label identifies **CORE GUARDED** and **CORE EXPOSED**. Defeat shuts down the engine over a finite transition, cools the core and releases a bounded set of embers. The occupied forge remains visible while loot is collected. Restoring a defeated boss starts with the engine already settled.

Warm light and distant ash haze distinguish the ordinary region; the storm has cooler light and stronger peripheral ash drift. Neither uses screen flashes. The ground stays muted so real attack warnings and the mint mouse destination ring remain legible. Four locally synthesized ten-second loops add wind, metal resonance, machinery, furnace pressure and distant storm rumble. All streams are prepared before the first playable frame, reuse one Master-bus audio player and stop or switch on leaving their context.

Pause freezes optional movement and pauses ambience. **Reduced visual effects** removes ash and fog, suppresses optional core movement and victory embers, and settles the shutdown immediately. Armor position, vent direction, core state, boss labels and existing attack warnings still communicate the mechanic.

## Presentation and navigation

Static art uses `EnvironmentBuilder` to merge primitives by palette on room entry. Tall scenery stays outside the authoritative arena; lava is scenery beyond the playable boundary. All floor surfaces stay below Y=0 and combat warnings. Obstacle art uses the same authoritative rectangles and player-occlusion fade as other regions. No art adds collision or changes movement, targeting, rewards, encounter timing or random streams.

Mouse controls remain left-click ground to walk, left-click an enemy to attack, Shift-click to attack in place, right-click for the secondary skill, WASD/left stick to take over and X to stop. Interactions and loot remain F and E. The new scenery does not add implied walkable bridges or routes outside the room boundary.

The only Core presentation addition is a read-only `CombatActorView.Guarded` projection of the existing campaign defense timer. This avoids deriving defense from animation state, which can lag the timer at a tick boundary. Persistent state, replay commands and combat rules are unchanged.

Cinder-region endgame arenas reuse the city setting, floor and ambience; the campaign boss rig appears only in its authored Furnace encounter. This pass covers solo presentation. [Act IV’s Shattered Spine](shattered_spine.md) now has bone cities and an animated covenant. [Act V’s Hollow Night](hollow_night.md) completes the regional pass. Cooperative arenas retain their previous environment treatment. The assets are repository-authored procedural geometry and synthesized audio.

## Connected exploration

The later [Cinder exploration milestone](cinder_exploration.md) adds authoritative room layouts, physical passages, persistent backtracking and the Sealed Foundry. The presentation-only scope above describes the original regional art pass.

## Inspection run

After compiling content and building the solution, use Bash and a fresh output folder:

```bash
source tools/env.sh
cinder_output=$(mktemp -d "$AW_ROOT/artifacts/cinder-inspection.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 3600 \
  --log-file "$cinder_output/smoke.log" -- \
  --cinder-smoke --capture-cinder --output="$cinder_output"
python3 tools/check-godot-log.py "$cinder_output/smoke.log"
```

For headless verification, add `--headless` before `--path` and omit `--capture-cinder`. The exported app accepts the same diagnostic arguments. Structural checks complement rendered inspection; they do not establish hardware performance or external player acceptance. See [verification](cinder_reach_verification.md) for the recorded build and review evidence.
