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
| Approach and interact in the campaign | Left-click a visible NPC, clue or available mechanism |
| Approach and collect a campaign drop | Left-click its visible item or rarity marker |

A mint ring marks the destination. The character routes around room obstacles and living bodies, then stops within 18 cm of the selected point. Click again to replace the destination. A click outside the room clamps to its edge; a point inside an obstacle or occupied by another actor is rejected. Solo displays a short notice when no route exists. This is single-click travel; holding the mouse does not continuously steer.

Primary, secondary and skill-bar attacks cancel the route, as do dodges. Opening a menu, losing focus, disconnecting a controller, loading, changing rooms, dying or reconnecting also cancels it. Co-op cancels when party connection membership changes. Temporary roots, freezes and attack windups preserve the destination until movement is possible again. A blocked route has a bounded retry period and cannot keep trying indefinitely.

In the shipping solo campaign, clicking an NPC, clue, available hunt mechanism or visible loot drop requests one action after approaching within its authoritative range. Hover highlights the target and names the action. Map actions labelled **Walk to** use the same approach. X, WASD, attacks, menus, focus loss, loading, death and room changes cancel the request; it also stops if the target disappears or the route becomes blocked. After a main campaign encounter is cleared, a **WAY FORWARD** marker offers the same contextual action as the objective card. Click it to approach and continue, choose an outcome, or review remaining rewards. It never bypasses the explicit travel warning for uncollected loot or story confirmation. A ground click requests movement only. Clicking an enemy still attacks without pursuing it automatically. F and E remain available for nearby interactions and loot. Existing keyboard rebinding remains available in Settings.

Loot filters apply to mouse picking: hidden drops do not consume floor clicks, and holding Alt reveals them. Only the selected drop is collected; collection uses the ordinary Core command and inventory checks. The cooperative prototype retains its existing movement and combat controls.

## Implementation

`ClickMovePlanner` generates the same signed eight-direction movement input as the keyboard. It uses the room's authoritative rectangles, actor clearance and current living actor positions. It caches static path edges and bounds dynamic detours, searches and route lifetime. No destination, path, new command or random value enters a save, replay, server snapshot or network message.

Solo advances navigation once per eligible fixed tick. Co-op advances once per new authoritative snapshot and reuses the result between snapshots. It estimates movement already in flight to brake early, keeps the ring until the server confirms a neutral input, and corrects a remaining gap with bounded move/stop pulses. History is limited to 64 inputs, prediction to 16 ticks, corrections to 32 pulses, and waiting for a stop acknowledgement to 180 snapshot ticks. A persistent failure cancels the route. Server collision and movement remain authoritative. The destination ring is reused across clicks and hidden on cancellation.

`TrySetApproach` samples a bounded set of reachable destinations inside the supplied interaction radius, with room for normal arrival tolerance. The input adapter stops before submitting the one selected command. NPC callbacks are revalidated after that tick, so a death or room replacement cannot open a stale conversation. No queued action enters persistent state or a replay; the resulting movement, pickup, mechanism and interaction commands do.

## Reproduction

From Bash after building with `source tools/env.sh`:

```bash
mouse_output="$(mktemp -d "$AW_ROOT/artifacts/mouse-input.XXXXXX")"
"$GODOT" --path game/Ashenwake.Client -- --mouse-movement-smoke --capture-mouse --output="$mouse_output"
```

The same arguments work with the exported app executable. Omit `--capture-mouse` and add `--headless` for a headless run. The diagnostic requires a fresh output directory, sends actual viewport mouse/key events, advances production fixed ticks, checks interruptions and verifies recorded commands. `tools/export.sh` includes the headless diagnostic. Co-op network regression runs separately through `tools/coop-package-verify.sh`.

Verification results and review status are recorded in [mouse movement verification](mouse_movement_verification.md).

The additional campaign interaction diagnostic uses `--mouse-actions-smoke --discipline=Vanguard --capture-mouse-actions --output=<fresh-directory>`. Its results and the HUD/objective follow-through are recorded in [mouse actions verification](mouse_actions_verification.md).
