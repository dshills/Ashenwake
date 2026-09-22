# Cinder Reach visual depth

Act III's city, Extraction Floor, Sealed Foundry, Burning Rain and Furnace Spindle now use richer volcanic surfaces, machinery and practical lighting.

- Connected basalt, ash and soot replace the full grid of repeated floor tiles. Fractured stone gathers toward the shoulders; worn paving and inset metal panels follow the existing routes. Grates and sparse rivets add industrial detail without glowing floor markings.
- Round pipework, flanges, reinforced joints, sagging cables and broken basalt improve the scenery's silhouette. The fields and working machinery retain their original anchors and walkable boundaries.
- Forge doors and windows spill warm light. Caged inspection lamps light the pumps and extraction gantries. Small shelter lamps preserve the sealed foundry's cold doors, while Burning Rain keeps steady collector lamps. Low canal light stays outside the arena.
- Pump flywheels turn slowly in the city, Extraction Floor and furnace room. The sealed foundry and storm collectors remain still.
- Furnace Spindle gains layered armor, a detailed exposed core and reinforced pipework. A shadowless core light follows the actual guarded/exposed state and fades during the finite shutdown. The existing vent direction and boss labels continue to communicate combat timing.

## Controls and limits

High enables four environmental local lights; Performance keeps the first two, placed at the room's main fixtures. Furnace Spindle retains its own core light in both modes, bringing that room's maximum to five and three. All local lights are shadowless. Reduced Effects neutralizes optional machinery motion, suppresses the existing ash and fog, and settles an active furnace shutdown even while paused. It preserves the boss's guard and vent signals; restoring effects cannot replay victory.

Raised scenery and moving machinery remain outside the fighting rectangle. Ground geometry stays below Y=0, and Furnace Spindle remains behind the north wall through its animation. The room owns its new meshes and materials and releases them when replaced; shared surface textures remain cached. No new collision, random streams, save fields or combat rules are introduced.

## Inspecting the result

With a current exported macOS app, use Bash and a fresh diagnostic directory:

```bash
source tools/env.sh
artifacts/export/macos/Ashenwake.app/Contents/MacOS/Ashenwake \
  --quit-after 12000 --log-file "$PWD/artifacts/cinder-depth/manual/smoke.log" -- \
  --cinder-smoke --capture-cinder --output="$PWD/artifacts/cinder-depth/manual"
python3 tools/check-godot-log.py artifacts/cinder-depth/manual/smoke.log
```

The diagnostic earns Act III through real campaign commands, visits all five contexts, checks timed storm expiry and both furnace vent orientations, and completes the boss and replay. It captures High/Performance and Reduced Effects views and inspects transformed scenery vertices, live preferences, animation bounds and resource cleanup. See [verification results](cinder_depth_verification.md).
