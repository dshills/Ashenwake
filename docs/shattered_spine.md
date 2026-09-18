# The Shattered Spine

Act IV gives Orrun's mountainous skeleton four distinct settings around the existing encounters:

- **Bone Causeway:** dwellings carved into vertebrae, suspended oath chains, enormous ribs and cold mountain stone.
- **Contract Hall:** an open tribunal surrounded by inscribed law tablets, witness desks and ceremonial cloth.
- **Covenant Warden:** a seal court with an articulated covenant tablet, four protective shields and six binding seals.
- **The Promise Before Stone:** the Divine Memory's intact ivory sanctuary, warmer light, ceremonial paving and unbroken inscriptions contrast with the present-day ruins.

The Warden's backdrop follows the boss's actual defense window. Its shields close while guarded and open while exposed; the boss label reads **OATH GUARDED** or **WARDEN EXPOSED**. Live, boss-owned oath marks light the central halo, and north/south fault announcements light the corresponding pointer. Defeat releases the binding seals and separates the tablet over a finite transition. A bounded set of sixteen glyphs disperses, then disappears. The occupied arena remains visible while collecting loot, and restored victories start settled.

The ordinary causeway's three fault lanes now display **1, 2, 3**, taken directly from the announced hazard IDs. In the Divine Memory those same numbers follow the actual reversed lane order. Labels sit above the floor, retain outlines and stay visible with reduced effects. They disappear with their authoritative warning or when leaving the room. Memory completion, voluntary departure and death restore the current regional setting and remove its scoped presentation.

Cool mountain light, distant haze and peripheral dust accompany the present day. The Memory uses warmer ivory light. Four locally synthesized ten-second loops add mountain air, stone resonance and low drones, with a gentler harmonic texture in the Memory. All streams are prepared before the first playable frame and share one Master-bus player. Pause freezes optional motion and pauses ambience; **Reduced visual effects** removes fog and dust, holds optional Warden motion and settles victory immediately. Guard, oath and fault signals, floor warnings and sequence numbers remain readable.

## Presentation and navigation

Static geometry is merged by palette when entering a room. Tall scenery stays beyond the authoritative arena, floor vertices stay below Y=0, and obstacle coverings remain inside the original Core rectangles. Player-occlusion fading, targeting, collision, combat timing, random streams, saves and replay commands are unchanged.

Left-click ground to walk, left-click an enemy to attack, Shift-click to attack in place, right-click for the secondary skill, WASD/left stick to take over and X to stop. The mint destination ring remains above the new floors. Interact and loot remain F and E by default.

Shattered Spine endgame arenas reuse the causeway setting, floor and ambience. The animated covenant appears only in its authored campaign boss encounter. This pass covers solo procedural presentation. Cooperative environments and Act V retain their earlier treatment; these assets do not establish final textured art or measured performance across hardware.

## Inspection run

After compiling content and building the solution, use Bash and a fresh output directory:

```bash
source tools/env.sh
spine_output=$(mktemp -d "$AW_ROOT/artifacts/spine-inspection.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 7200 \
  --log-file "$spine_output/smoke.log" -- \
  --spine-smoke --capture-spine --output="$spine_output"
python3 tools/check-godot-log.py "$spine_output/smoke.log"
```

For headless verification, add `--headless` before `--path` and omit `--capture-spine`. The exported app accepts the same diagnostic arguments. See [verification](shattered_spine_verification.md) for the recorded checks and Prism review.
