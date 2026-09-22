# Hollow Night visual depth verification

Validated on 2026-09-22 from `7e2fe9a4ff367a2c48c05bd232241edfa02aa15a`, using .NET 8.0.425 and the exported Godot 4.6.2 Mono macOS app, Compatibility renderer, Apple M4 Pro.

## Regional evidence

The solution build has zero warnings and errors. Formatting verification, `git diff --check` and `aw experiment compile` pass. The native Hollow diagnostic passes **851 checks**, with no skipped checks, and produces **27 captures** across all four environments, High/Performance, Reduced Effects, mouse destinations, echo warnings, all three Breach phases, seal sweeps and final containment.

The route executes **9,380 real campaign commands**, completes Act V, presents the ending, returns to Greyhaven and revisits the completed region. Both the command count and final gameplay hash exactly match the pre-change baseline: `3C42AF0BAA812027C5722A592FA39B3974004AFA9F84102E99C6063055B465A9`. Independent restored branches verify Memory departure, actual death cleanup, canonical boss identity among Mirrorborn copies, and first/returning echoes and seal sweeps without altering the live campaign. Checkpointed replay matches.

Actual transformed vertices establish safe scenery placement, unchanged obstacle footprints and ground height:

| Environment | Architecture meshes/materials | Architecture vertices | Ground meshes | Highest floor vertex |
| --- | ---: | ---: | ---: | ---: |
| Repeating Rooms | 7 / 7 | 63,384 | 9 | −0.0015m |
| Unremembered Vault | 7 / 7 | 27,360 | 11 | −0.0015m |
| Identity Memory | 6 / 6 | 10,872 | 11 | −0.0015m |
| Breach Heart | 6 / 6 | 8,340 | 8 | −0.0040m |

Each room's fixtures and suspended fragments use six renderers: five material batches and one MultiMesh. High enables four shadowless environmental lights and eight fragments; Performance enables two lights and four fragments split between both side clusters. Reduced Effects hides the fragments immediately, including while paused. All fixture and fragment vertices stay outside the playable rectangle throughout two complete 36-second motion cycles.

Detached and live checks verify fixed resource identities, finite clocks, invalid delta handling, pause, preference changes while paused, same-style resizing, style replacement, hub cleanup and completed-region revisits. Owned meshes, materials and the instance buffer are detached and released; cached surface textures survive. Native MultiMesh transforms are inspected directly.

The Breach retains thirteen articulated parts and sixteen pooled shards and stays below its hundred-mesh budget. Its shaped shutters, engraved seals and curved fracture have nondegenerate outward-facing triangles and finite normals/UVs. Every transformed vertex stays behind the north wall through idle motion and containment. Tests use actual observed phase, shield and channel states, retain channel identities when actor order changes, and distinguish the canonical boss from copies. The single shadowless aperture light follows those same combat signals in both presets.

Containment starts from the captured ring, heart, shutter, emission and light state without a snap or flash, fades once and leaves a small visible fracture. Pause, restored victory and effects changes cannot replay it. Resource isolation and cleanup pass for concurrent rigs, including shared texture survival.

The same exported package passes **156 native Hollow exploration checks** with a clean Godot log. Exploration uses 9,749 campaign setup commands, 226 recorded input commands and eight verified replay branches, with no focus recoveries. Shipping mouse input exercises physical passages, the single named vault reward, retained floor loot, local maps, backtracking and the Fracture gate. F5/F9 preserve the vault and unresolved final choice; both Share and Guard endings are earned through the actual three-seal fight, with all seals defeated and Fractures unlocked.

The opening journey passes **463 native checks**, with no skipped checks and a clean Godot log, covering shared environment setup, lighting and quality controls, combat presentation, navigation, persistence and two replay segments. Together the three accepted diagnostics pass **1,470 native checks**. Regression captures were omitted; the regional diagnostic supplies the 27 rendered captures.

## Rendered inspection

High captures from all four rooms were inspected, along with Repeating Rooms in Performance, the Breach in Reduced Effects, phases two and three, simultaneous first/returning echoes, the seal sweep, settled containment and the ending story. Curved sealed arches, recessed memorials and vault niches have visibly deeper profiles. Inset cyan/violet lamps illuminate masonry; suspended fragments remain peripheral. Worn slate and displaced inlays keep routes and the Breach fighting floor readable beneath the actual warnings.

The Breach has one additional shadowless aperture light in both quality presets, bringing its local-light total to five in High or three in Performance. Reduced Effects retains the actual phase, channel and warning signals.

Thirty frame intervals per room and preset were sampled under a 60 FPS cap. Medians were 16.66–16.67ms and 95th percentiles 16.80–17.61ms. These short local samples establish frame pacing, not uncapped performance or a broad hardware benchmark.

## Prism review

Prism/Gemini run `a1ae07dc199000c039eab91e32f24aa7` reviewed the staged implementation and reported three candidates. Source inspection and native lifecycle/replay evidence establish that none requires a change:

- **Potential shared mesh disposal (high):** the existing Breach cleanup already deduplicates meshes with `DistinctBy(mesh => mesh.GetInstanceId())`, then detaches every renderer's mesh and material override before disposal. Recorded resources belong to the rig; cached textures are not disposed. Native isolation and free checks pass.
- **Resources overwritten by repeated Build (medium):** `Build` is private and has exactly one caller. `Create` constructs a fresh rig and calls it once; state updates, animation and scene refresh never call it. There is no reachable repeated-build path.
- **Delta clamp causing simulation desynchronization (low):** the cited smoke has no delta clamp. The cap exists in cosmetic animation methods, which read combat state and advance optional visual clocks. Core commands, ticks and warning deadlines are independent. The unchanged 9,380-command replay and identical baseline hash confirm gameplay is unaffected.

Raw review and disposition: `artifacts/hollow-depth/prism-initial.json` and `prism-disposition.json`. No actionable finding remains.

## Artifacts and scope

- Accepted app: `artifacts/export/macos/Ashenwake.app`.
- Accepted archive: `artifacts/export/Ashenwake.zip`.
- Archive SHA-256: `4b7c81f0226a9f66a744f2d04cd833b658b8c778117d97d6c8fc7e429898ace1`.
- Baseline report/captures: `artifacts/hollow-depth/before/`.
- Accepted regional report/captures: `artifacts/hollow-depth/after/`.
- Regression reports: `artifacts/hollow-depth/exploration/` and `journey/`.
- Build, content validation, formatting, import/export and Prism evidence: `artifacts/hollow-depth/`.

The phase changes client presentation, diagnostics and documentation. Core/content sources, save schemas and random streams are unchanged. The full Core unit suite was not rerun; native diagnostics execute real commands and replay through the changed client presentation.
