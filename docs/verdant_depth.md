# Verdant Maw visual depth

Act II now carries the opening's stylized dark-fantasy finish through Living Ruins, Plague Village, Briarheart Shrine, the Antler Grove and Rootheart.

## What changes in play

- Trees have tapered trunks, buttress roots and asymmetric branching crowns. Ferns have curved fronds and paired pointed leaves. Overlapping leaf shingles soften the village roofs.
- Connected earth and moss replace the repeated floor discs. Worn ruin pavers and village boardwalks follow the existing routes; sparse leaf litter and restrained canopy shading leave combat warnings and tracking clues readable.
- Warm village lanterns and pale fungal clusters add steady local light. Gentle foliage motion stays beyond the fighting area. Regional daylight has a warmer highlight and quieter green tint.
- Rootheart has layered curved petals, raised veins, a lobed seed core, bark stalks and small new-growth leaves. Its existing feeding-root and victory states drive the same thirteen joints.

## Settings and boundaries

High uses 72 moving leaf instances and four shadowless local lights per Verdant room. Performance uses 36 instances and two lights. Reduced Effects returns foliage and Rootheart breathing to a static pose, including while paused, and settles an active victory bloom. Restoring effects does not replay that victory. The existing settings continue to suppress regional motes and fog.

Scenery has no collision or navigation. Raised architecture and the animated plants stay outside the authoritative room rectangle; the floor stays below Y=0. Rootheart's entire animation remains behind the north wall. Routes, monsters, loot, movement, save data and simulation randomness are unchanged.

## Implementation and reproduction

`BotanicalGeometry` supplies a small indexed, double-sided curved leaf with normals, tangents and UVs. `EnvironmentBuilder` batches those leaves and tapered branches with the existing palette materials. `VerdantAtmosphere` owns one foliage MultiMesh and a fixed set of fixture meshes and lights; its clock advances only during unpaused play and its resources are released when the room changes. Shared surface textures remain owned by their cache.

Build using the pinned tools, then run a fresh diagnostic output directory:

```bash
source tools/env.sh
dotnet build Ashenwake.sln --no-restore -m:1 -p:UseSharedCompilation=false
artifacts/export/macos/Ashenwake.app/Contents/MacOS/Ashenwake \
  --quit-after 7200 --log-file "$PWD/artifacts/verdant-depth/manual/smoke.log" -- \
  --verdant-smoke --capture-verdant --output="$PWD/artifacts/verdant-depth/manual"
```

The app must first be exported from the current source using the repository's macOS export preset. The diagnostic completes the real Act II route, visits all five contexts, captures High/Performance and reduced-effects views, tests every Rootheart pose, and verifies deterministic replay. [Verification results](verdant_depth_verification.md) record the accepted package and review.
