# Opening environment art

The solo opening run now uses repository-authored low-poly scenery. Greyhaven has varied stone and timber workshops, pitched roofs, an outdoor forge, an apothecary, lanterns, a funerary gateway, storage and warm windows. Restored workshop stages add small supplies. Residents retain their authoritative interaction positions and stand on colored service mats.

Act I has three separate compositions. The road passes graves, memorials, dead trees and a broken gate. The monastery has pointed arches, funeral cloth, open tracery and a burial aisle. The Bell Saint sanctuary has a large bronze bell, timber yoke, individual chain links and ruined side arches. Peripheral ritual lights change during the anchor phase; the bell tilts and its chains break in the unbound phase. These are cosmetic responses to Core's boss phase.

Stone paving, missing slabs, moss marks and muted sanctuary inlays replace the diagnostic grid. The floor tops remain below Y=0, beneath combat warnings and interaction rings. Existing Core obstacle footprints are represented by masonry blocks; their complete presentation fades when it overlaps the player on screen. No architecture adds physics, navigation, encounter rules, interaction ranges or barriers. Tall scenery sits outside the authoritative room rectangle, and foreground edges stay low. The closer player-follow camera and mouse-wheel zoom remain available.

Greyhaven uses warm lighting; Act I uses cooler light and distant depth fog supported by the Compatibility renderer. Sparse embers or memory motes stay outside the combat floor. Pause freezes their motion. Reduced visual effects hides ambient motes and disables this fog while retaining gameplay warnings.

`EnvironmentBuilder` merges static primitives into one mesh per palette color/emission setting on room entry. It retains no global environment cache and discards temporary authoring primitives after merging. Architecture does not allocate geometry per frame. Greyhaven uses 22 material batches and two shadowless local lights; Act I uses 8–11 batches, including the extra ritual-light material during phase II. The floor uses 6–9 batches. Obstacles have independent opacity materials so fading one cannot fade the whole environment.

The normal launcher and the retained Campaign, Production and Adventure entry scenes use these opening builders. [Act II's Verdant Maw](verdant_maw.md) now has its own organic terrain, inhabited ruins, tracking clues and animated boss setting. [Act III's Cinder Reach](cinder_reach.md) adds volcanic industrial settings, a state-driven Furnace Spindle and Burning Rain atmosphere. Cooperative arenas and Acts IV–V retain their earlier environment presentation; the later acts still use the shared stone floor. These are procedural art passes, not final textured assets, changed level layouts, dynamic destruction, or measured hardware performance certification.

## Reproduce the opening run

Compile content and build the client, then run:

```bash
source tools/env.sh
scene_output=$(mktemp -d "$AW_ROOT/artifacts/environment-journey.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 2400 \
  --log-file "$scene_output/smoke.log" -- \
  --journey-smoke --capture-journey --output="$scene_output"
python3 tools/check-godot-log.py "$scene_output/smoke.log"
```

The diagnostic uses production stage builders and real Core encounters. It captures Greyhaven, the road, the monastery and all Bell Saint phases while navigating the actual Journey controls. Its report records visible environment transitions, cosmetic node types, ground clearance and material counts. Structural checks complement visual inspection; they do not replace it. The exported package runs the same journey headlessly through `tools/export.sh`.
