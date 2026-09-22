# Cinder visual depth verification

Validated on 2026-09-22 from `af24daf61032f314181c0dfee28697ec99d4adbc`, using .NET 8.0.425 and the exported Godot 4.6.2 Mono macOS app, Compatibility renderer, Apple M4 Pro.

## Regional evidence

The solution build has zero warnings and errors. Formatting verification, `git diff --check` and `aw experiment compile` pass. The native Cinder diagnostic passes **478 checks** and produces **23 captures** across all five regional environments, High/Performance settings, Reduced Effects, mouse destinations, furnace guard/exposure, both vent directions and shutdown.

The route executes **5,609 real campaign commands**, completes Act III, returns to Greyhaven, revisits the completed region and verifies checkpointed replay. Both the command count and final gameplay hash exactly match the pre-change baseline: `5FC01B1047446DAF12655E3A1F021E72538E0892F271444B24A8D2C2023F0C30`. A separate restored branch also exercises ordinary movement and potion commands until the Burning Rain timer expires.

Actual transformed vertices establish the scenery boundary, unchanged obstacle footprints and floor height. Static architecture uses 11–15 mesh/material batches; ground uses 9–15. The highest ground vertices range from −0.005m to −0.0004m, below the combat plane. Room fixtures and complete flywheel rotations remain outside the playable rectangle.

Detached and live checks verify fixed mesh identities, bounded clocks, invalid delta handling, pause, immediate preference changes while paused, scene resizing, room replacement, hub cleanup and completed-region revisits. All owned machinery meshes and materials are released. Furnace tests retain and inspect shared texture references after freeing independent rigs to establish that the factory cache remains valid.

The furnace retains eleven joints and twelve pooled embers, remains below its hundred-mesh budget and stays behind the north wall throughout motion. Its chamfered armor has indexed, nondegenerate outward-facing triangles and finite normals/UVs. Tests use real observed guarded/exposed and vent views, sample a full axle rotation, verify exact vent cues with Reduced Effects, and exercise shutdown from each observed state. Shutdown preserves its initial heat level and then cools monotonically, including when the boss was guarded; there is no initial brightness jump. Restored victory and preference toggles do not replay the effect.

The accepted package also passes **191 native Cinder exploration checks** and **463 opening-journey checks**: **1,132 native checks in total**, all with clean Godot logs. Exploration uses 5,900 campaign setup commands, 299 recorded input commands and six verified replay branches. It exercises passage clicks, foundry treasure, storm abandonment/expiry/victory, backtracking, retained loot, local map navigation and F5/F9 save/load. Nine native focus interruptions were recovered through the diagnostic's existing pause/state-preservation checks. The opening journey covers shared environment setup, quality controls, lighting lifecycle, combat presentation and persistence. Regression runs omitted optional captures; regional visual evidence comes from the 23 Cinder captures.

## Rendered inspection

High captures from every environment were inspected, along with Extraction Floor in Performance mode, reduced-effects Furnace Spindle, guarded/exposed and horizontal-vent states, and the settled shutdown. Forge light spills onto the city facade; cage lamps illuminate the pumps, gantries and sealed doors; the exposed core lights its reinforced frame. Paths and actual attack warnings remain distinct from the subdued ground and distant canal lighting.

High enables four room lights and Performance two. The furnace's state-driven core light is present in both modes, bringing that encounter to five/three. The lights are shadowless. Optional pump motion freezes with pause and returns to neutral under Reduced Effects; the sealed foundry and storm have no moving machinery.

Thirty frame intervals per room and preset were sampled under a 60 FPS cap. Medians were 16.63–16.67ms and 95th percentiles 16.73–17.70ms. These short local samples are frame-pacing evidence, not an uncapped performance comparison or broad hardware benchmark.

## Prism review

Prism/Gemini run `e036eefa4c5f333f629e45d80c88a810` reviewed the staged implementation and reported one medium candidate: calling `SurfaceGetMaterial(0)` on a possibly empty renderer during atmosphere resource registration.

Source inspection establishes that the candidate is unreachable in this construction path. The private `Build` method registers only nodes it synchronously creates through `EnvironmentBuilder`, before returning the atmosphere or attaching it to a tree. `Flush` creates a renderer only for a nonempty palette group and commits its geometry with a material. An empty fixture group produces no renderer, and each wheel always contains explicit primitives. No external scene supplies children during this call. Native diagnostics construct all five styles and inspect and release their actual resources with clean logs. No actionable finding remains.

The raw review and disposition are retained in `artifacts/cinder-depth/prism-initial.json` and `prism-disposition.json`.

## Artifacts and scope

- Accepted app: `artifacts/export/macos/Ashenwake.app`.
- Accepted archive: `artifacts/export/Ashenwake.zip`.
- Archive SHA-256: `c7bb13e6b7a4fc5e123b2461e83f8acdafb3978766bb2c217b969adb26ba9cb0`.
- Baseline report/captures: `artifacts/cinder-depth/before/`.
- Accepted regional report/captures: `artifacts/cinder-depth/after/`.
- Regression reports: `artifacts/cinder-depth/exploration/` and `journey/`.
- Build, content validation, formatting, import/export and Prism evidence: `artifacts/cinder-depth/`.

The phase changes client presentation, diagnostics and documentation. Core/content sources, save schemas and random streams are unchanged. The full Core unit suite was not rerun; native diagnostics execute the real commands, persistence and replay paths relevant to the changed client presentation.
