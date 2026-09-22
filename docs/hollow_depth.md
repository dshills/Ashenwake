# Hollow Night visual depth

Act V's Repeating Rooms, Identity Memory, Unremembered Vault and Breach Heart now have deeper architecture, worn slate and spectral lighting.

- Curved nested mouldings frame sealed arches. Recessed memorial panels, layered vault niches and fractured obelisks reinforce the region's repeating, displaced architecture. Real passages retain their anchor glyphs.
- Broad slate slabs have chamfered edges, deeper joints and occasional chipped corners. Small displaced inlays stay off the routes; the Breach center remains quiet beneath combat warnings. Memory and the vault have distinct violet and cooler slate palettes.
- Small inset lamps cast steady cyan or violet light onto existing facades and side monuments. Suspended slate fragments drift above broken plinths outside the fighting floor.
- Breach Heart gains layered orbital rings, shaped shutters, engraved channel seals and a more detailed central fracture. Its aperture light follows actual phase, shield and channel state. Victory draws the surviving wound into containment from the current pose and light level.

## Preferences and boundaries

High enables four environmental lights and eight suspended fragments. Performance retains the two main facade lights and four fragments, split between both side clusters. The Breach has one additional shadowless aperture light in either preset. All new local lights are steady and shadowless.

Pause freezes optional motion. Reduced Effects hides floating fragments, suppresses the existing fog/motes and settles active containment even while paused. Real phase poses, channel identities, echo countdowns and sweep warnings remain visible. Restoring effects cannot replay victory.

Raised scenery, fixtures and fragments stay outside the playable rectangle. Floor vertices remain below Y=0; the boss backdrop remains behind the north wall through all phases and containment. Meshes, materials and instance buffers belong to their room and are released on replacement; shared surface textures remain cached. Core rules, routes, collisions, saves and random streams are unchanged.

## Inspecting the result

From Bash with a current exported macOS app:

```bash
source tools/env.sh
artifacts/export/macos/Ashenwake.app/Contents/MacOS/Ashenwake \
  --quit-after 28000 --log-file "$PWD/artifacts/hollow-depth/manual/smoke.log" -- \
  --hollow-smoke --capture-hollow --output="$PWD/artifacts/hollow-depth/manual"
python3 tools/check-godot-log.py artifacts/hollow-depth/manual/smoke.log
```

The diagnostic reaches all four Act V contexts with real campaign commands, observes all three boss phases and actual warning cycles, completes the ending and verifies replay. It also captures both quality presets and Reduced Effects and inspects actual geometry, animation bounds, preference changes and resource lifetime. See [verification results](hollow_depth_verification.md).
