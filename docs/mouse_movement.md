# Mouse movement

Solo and co-op accept a ground destination without changing the movement speed, collision rules or combat simulation.

| Action | Control |
|---|---|
| Walk to a destination | Left-click clear ground |
| Attack a specific enemy | Left-click its visible body |
| Use the primary skill without walking | Shift + left-click |
| Use the secondary skill | Right-click |
| Take over movement | WASD or the left stick |
| Stop walking | X |

A mint ring marks the destination. The character routes around room obstacles and living bodies, then stops within 18 cm of the selected point. Click again to replace the destination. A click outside the room clamps to its edge; a point inside an obstacle or occupied by another actor is rejected. Solo displays a short notice when no route exists. This is single-click travel; holding the mouse does not continuously steer.

Primary, secondary and skill-bar attacks cancel the route, as do dodges. Opening a menu, losing focus, disconnecting a controller, loading, changing rooms, dying or reconnecting also cancels it. Co-op cancels when party connection membership changes. Temporary roots, freezes and attack windups preserve the destination until movement is possible again. A blocked route has a bounded retry period and cannot keep trying indefinitely.

Walking does not automatically interact, collect loot or pursue an out-of-range enemy. Use F near a person or mechanism, E near loot, and ordinary skill controls during combat. Existing keyboard rebinding remains available in Settings.

## Implementation

`ClickMovePlanner` generates the same signed eight-direction movement input as the keyboard. It uses the room's authoritative rectangles, actor clearance and current living actor positions. It caches static path edges and bounds dynamic detours, searches and route lifetime. No destination, path, new command or random value enters a save, replay, server snapshot or network message.

Solo advances navigation once per eligible fixed tick. Co-op advances once per new authoritative snapshot and reuses the result between snapshots. It estimates movement already in flight to brake early, keeps the ring until the server confirms a neutral input, and corrects a remaining gap with bounded move/stop pulses. History is limited to 64 inputs, prediction to 16 ticks, corrections to 32 pulses, and waiting for a stop acknowledgement to 180 snapshot ticks. A persistent failure cancels the route. Server collision and movement remain authoritative. The destination ring is reused across clicks and hidden on cancellation.

## Reproduction

From Bash after building with `source tools/env.sh`:

```bash
mouse_output="$(mktemp -d "$AW_ROOT/artifacts/mouse-input.XXXXXX")"
"$GODOT" --path game/Ashenwake.Client -- --mouse-movement-smoke --capture-mouse --output="$mouse_output"
```

The same arguments work with the exported app executable. Omit `--capture-mouse` and add `--headless` for a headless run. The diagnostic requires a fresh output directory, sends actual viewport mouse/key events, advances production fixed ticks, checks interruptions and verifies recorded commands. `tools/export.sh` includes the headless diagnostic. Co-op network regression runs separately through `tools/coop-package-verify.sh`.

Verification results and review status are recorded in [mouse movement verification](mouse_movement_verification.md).
