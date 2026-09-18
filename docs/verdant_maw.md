# The Verdant Maw environment

Act II presents Ilyra's ecosystem as a jungle growing through bone, stone and inhabited structures. Four existing encounter contexts receive separate compositions:

- **Living Ruins:** roofless temples, roots bound around broken columns, layered ferns, carnivorous flowers and a shed insect carapace. Worn paving survives among soil, moss islands and leaf litter.
- **Plague Village:** shelters raised on stilts, overlapping leaf roofs, screened beds, quarantine bindings and sap collectors. Flat boardwalks and channels distinguish the inhabited ground from the surrounding jungle.
- **Rootheart:** a huge rib-cage apse frames a suspended heart and eight-petaled corpse-flower. Muted growth rings and tissue seams sit beneath the fighting floor.
- **Antler hunt:** a many-tined bone tree, ancient trunks and small memorials identify the grove. Leaf litter and exposed root lines replace the temple paving.

## Track and confront

The ordered tracking hunt uses physical **shed bark**, **reversed hoofprints** and a **heartwood nest** at the existing Core interaction positions. Approach the current clue and use the normal Interact binding, **F** by default. Its name and a thin broken halo identify the nearest available clue. Small marks remain local to a known clue; they do not draw a route through obstacles or reveal the next clue early. Tracked remains stay visible in the current grove, with their fresh detail and focus removed. Leaving or restarting the encounter rebuilds this presentation from the current state.

The Rootheart setting responds to actual living feeding roots and boss defeat. Destroyed roots make the surrounding stalks wilt and expose the seed; victory opens the flower and reveals quiet new growth over a short, finite transition. These are cosmetic responses, not additional health phases, targets or rewards. The occupied arena remains visible after victory while its loot is collected, even when the Journey objective advances.

## Atmosphere and accessibility

Green ambient light, restrained sunlight and distant depth haze distinguish the region, with a warmer village and darker heart chamber. Twenty-four small motes remain around the perimeter. Ground art stays muted so combat warnings retain their contrast.

Forest, village and heart each select a quiet, locally synthesized ten-second audio loop: filtered breeze and leaves, sparse insect calls, timber creaks, water and drips. The three loops are prepared during initial scene construction so entering a region only selects a cached stream. The forest loop also serves the hunt. Playback uses the Master bus and stops when leaving Verdant contexts. No downloaded audio samples are used.

Pause freezes decorative motion and pauses ambient playback. **Reduced visual effects** in Settings hides motes and fog, removes optional clue and heart pulsing, and presents the settled victory form without replaying the bloom. Clue shapes, tracked state, the heart's response to destroyed roots and gameplay warnings remain readable.

## Presentation bounds

`VerdantMawArt` and `VerdantGround` use `EnvironmentBuilder` to merge room-entry primitives by palette. Static architecture uses at most 17 palette colors, and a floor uses at most 12. The dynamic heart and clue assemblies have their own bounded meshes; animation changes transforms and private material values without creating geometry per frame. Ground variation is deterministic and does not consume simulation random numbers. Terrain generation is capped at a 32-by-28 set of decorative island positions, and the ambience cache contains only three fixed cues.

Tall architecture remains outside the authoritative room rectangle; camera-facing decoration stays low. All authored floor surfaces lie below Y=0, beneath warning geometry. Organic coverings reuse existing obstacle footprints and their player-occlusion fade. Clues add small cosmetic props at existing interaction anchors. None of these meshes adds collision, navigation, interaction range, damage, timing or encounter rules.

This is repository-authored procedural art. It does not add a new level layout, new campaign content, final textured assets or network environment synchronization. Cooperative arenas retain their earlier presentation, and Acts III–V retain prototype regional landmarks.

## Reproduce an inspection run

After compiling content and building the client, run these commands in Bash. The fresh output directory isolates the diagnostic from normal player saves:

```bash
source tools/env.sh
verdant_output=$(mktemp -d "$AW_ROOT/artifacts/verdant-visual.XXXXXX")
"$GODOT" --path game/Ashenwake.Client --quit-after 2400 \
  --log-file "$verdant_output/smoke.log" -- \
  --verdant-smoke --capture-verdant --output="$verdant_output"
python3 tools/check-godot-log.py "$verdant_output/smoke.log"
```

Add `--headless` before `--path` and omit `--capture-verdant` for a run without screenshots. Rendered captures still need inspection for composition, actor visibility and warning readability; structural diagnostics alone do not establish visual quality or hardware performance.
