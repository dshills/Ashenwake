# The Hollow Night

Act V now has three distinct settings beneath ordinary reality:

- **Repeating Rooms:** nested blind arches, repeated stonework and displaced shadow outlines surround the existing encounter.
- **Identity Memory:** asymmetric record galleries and split memorials suggest a room remembering different versions of itself.
- **Breach Heart:** a chamber of obelisks frames a large suspended aperture, three channel seals and articulated containment rings.

Blue-grey and muted violet paving keep attack warnings and the mint mouse destination ring prominent. The distant architecture has no walkable doors or new collision. The original room boundaries, obstacle footprints and campaign progression remain authoritative.

The Breach Heart backdrop follows all three actual boss phases. Each of its three channel seals tracks the same channel actor across deaths. The boss is **SEALED · 3 CHANNELS** while all three live and **BREACH EXPOSED** once that protection breaks, using the Core's read-only `Shielded` projection. Channel actors carry persistent **BREAK SEAL** instructions. Mirrorborn copies are labelled **BREACH ECHO**; only the original boss drives the phase, warning and victory presentation.

Live boss-owned echo, returning-echo and seal-sweep warnings activate distinct cues on the aperture. Floor circles for causal echoes display **ECHO** and their remaining time in seconds. The boss's overlapping echo circles display **FIRST** and **RETURN** at opposing offsets, each with its own countdown. These labels are derived from the existing warning deadlines, remain visible under reduced effects and disappear with their warning, including after a phase transition, death or room departure.

Final victory draws a bounded set of fragments inward and settles the breach into containment over a finite transition. A small wound remains: the ending stabilizes the immediate crisis without depicting a restored god. Restoring a completed fight starts with the breach settled. The occupied chamber remains visible during loot collection and the existing ending/Story screen; both story outcomes still unlock the Fracture gate.

## Atmosphere and controls

Cool light, distant haze and peripheral motes distinguish the region. Identity Memory adds subdued lilac light. Three locally synthesized ten-second ambience loops combine low drones, repeating resonances and a rising Memory texture. Streams are prepared before play, cached within a fixed cue list and routed through the existing Master volume. The single regional player switches or stops on travel.

Pause freezes optional motion and pauses ambience. **Reduced visual effects** disables motes and fog, holds optional aperture movement and settles victory immediately. Phase poses, channel states, warning cues, labels and countdowns remain readable. Re-enabling effects never repeats the victory transition.

Mouse controls remain left-click ground to walk, left-click an enemy to attack, Shift-click to attack in place, right-click for the secondary skill, WASD/left stick to take over and X to stop. F interacts and E collects loot by default. Scene geometry does not change picking or the movement planner.

Hollow Night endgame arenas reuse Repeating Rooms scenery, ground and ambience. The Breach Heart rig appears only in its campaign encounter. Regional procedural art now covers Greyhaven and all five campaign acts. Cooperative environments retain their earlier presentation; final textures, independent player acceptance and performance across hardware remain separate work.

## Inspection run

After compiling content and building the solution, use Bash and a fresh output directory:

```bash
source tools/env.sh
hollow_output=$(mktemp -d "$AW_ROOT/artifacts/hollow-inspection.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 7200 \
  --log-file "$hollow_output/smoke.log" -- \
  --hollow-smoke --capture-hollow --output="$hollow_output"
python3 tools/check-godot-log.py "$hollow_output/smoke.log"
```

For headless verification, add `--headless` before `--path` and omit `--capture-hollow`. The exported app accepts the same diagnostic flags. Recorded evidence is in [verification](hollow_night_verification.md).
