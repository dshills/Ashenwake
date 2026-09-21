# Opening environment art

The solo opening run uses repository-authored low-poly scenery. Greyhaven has separate shrine, workshop and market courts connected by a stone street. Smaller service medallions sit beneath residents at their authoritative interaction positions. Unequal rooflines, an outdoor forge, an apothecary, low market tables, a loaded cart and warm windows give the districts distinct landmarks. Restored workshop stages add small supplies. Terraces, trees and faceted cliffs provide depth behind the settlement.

Act I has three separate compositions. A bending road passes graves, memorials, dead trees, a broken gate and an interrupted aqueduct. The monastery has a paved courtyard, offset pointed arches, funeral cloth, a raised burial aisle and fallen columns. The Bell Saint sanctuary has a large bronze bell, timber yoke, individual chain links, distant buttresses and ruined side arches. Peripheral ritual lights change during the anchor phase; the bell tilts and its chains break in the unbound phase. These are cosmetic responses to Core's boss phase.

Stone paving, blended earth and grass, recessed drains and muted sanctuary inlays replace the diagnostic grid. The floor tops remain below Y=0, beneath combat warnings and interaction rings. Existing Core obstacle footprints become an anvil bench and market chest in Greyhaven, or broken masonry in the March; their complete presentation fades when it overlaps the player on screen. Low wayposts frame the actual eastbound Way Forward marker. The painted roads are visual guidance: the surrounding earth remains walkable, and the existing rectangular Core room still owns collision, navigation and interaction ranges. Tall scenery stays outside that rectangle and foreground edges stay low. The player-follow camera and mouse-wheel zoom remain available.

Greyhaven uses warm lighting; Act I uses cooler light and distant depth fog supported by the Compatibility renderer. Six restrained chimney wisps rise over the town; eight tumbling leaves drift around the March's outer edges. Sparse embers or memory motes stay outside the combat floor. Pause freezes motion. Reduced visual effects hides this optional motion and disables the fog while retaining gameplay warnings. Resizing or replacing a room rebuilds its atmosphere at the correct bounds, including while paused.

`EnvironmentBuilder` merges static primitives by color, surface and emission on room entry. It retains no global environment cache and discards temporary authoring primitives after merging. Faceted cliffs use three indexed mesh batches sharing existing stone materials. The earth field uses one mesh with interpolated vertex colors. Architecture does not allocate geometry per frame. Greyhaven uses 24 architecture materials and two shadowless local lights; Act I uses nine architecture materials. Obstacles have independent opacity materials so fading one cannot fade the whole environment. Opening motion uses one MultiMesh per room, with room-owned resources and no extra lights or shadows.

The normal launcher and retained Campaign, Production and Adventure entry scenes use these opening builders. [Act II's Verdant Maw](verdant_maw.md), [Act III's Cinder Reach](cinder_reach.md), [Act IV’s Shattered Spine](shattered_spine.md) and [Act V’s Hollow Night](hollow_night.md) retain their distinct regional environments. Cooperative arenas retain their earlier environment presentation. This pass changes visual composition within existing rooms; connected exploration layouts, collision changes, dynamic destruction and hardware performance certification remain separate work.

## Reproduce the opening run

Compile content and build the client, then run:

```bash
source tools/env.sh
scene_output=$(mktemp -d "$AW_ROOT/artifacts/environment-journey.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 18000 \
  --log-file "$scene_output/smoke.log" -- \
  --journey-smoke --capture-journey --output="$scene_output"
python3 tools/check-godot-log.py "$scene_output/smoke.log"
```

The diagnostic uses production stage builders and real Core encounters. It captures Greyhaven, the road, the monastery and all Bell Saint phases while navigating the actual Journey controls. Its report records triangle-level scenery clearance, obstacle bounds, material budgets, walkable routes, service/exit approaches and atmosphere lifecycle checks. Structural checks complement visual inspection. See [composition verification](opening_composition_verification.md) for completed native package and mouse-input checks.
