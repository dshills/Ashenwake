# Shattered Spine visual depth verification

Validated on 2026-09-22 from `2dcb88628023b84e8c37450b017e9b46e2e2530f`, using .NET 8.0.425 and the exported Godot 4.6.2 Mono macOS app, Compatibility renderer, Apple M4 Pro.

## Regional evidence

The solution build has zero warnings and errors. Formatting verification, `git diff --check` and `aw experiment compile` pass. The native Spine diagnostic passes **588 checks**, with no skipped checks, and produces **26 captures** across all five environments, High/Performance, Reduced Effects, mouse destinations, reversed numbered faults, Warden guard/exposure, both fault lanes and victory release.

The route executes **7,557 real campaign commands**, completes Act IV, returns to Greyhaven, revisits the completed region and verifies checkpointed replay. Both the command count and final gameplay hash exactly match the pre-change baseline: `ACCB444884238D9D149737B1243F56675B96B033AD6C6DD3274F85A44454BBA1`. Independent restored branches verify Memory departure, death cleanup and both actual Warden fault lanes without altering the live campaign.

Actual transformed vertices establish safe scenery placement, unchanged obstacle footprints and ground height. Architecture uses 5–10 mesh/material batches; each floor uses nine meshes. The highest floor vertices are −0.0015m, below the combat plane. Each room's moving cloth and fixtures use eight meshes, with two banners and four shadowless lights in High or two in Performance. Fixtures and the banners' full motion stay outside the playable rectangle.

Detached and live checks verify fixed resource identities, two complete 24-second wind cycles, invalid delta handling, pause, immediate preference changes while paused, room resizing/replacement, hub cleanup and completed-region revisits. Owned banner meshes and materials are released, including shared panel materials deduplicated by instance ID.

The Warden retains fourteen joints and sixteen pooled glyphs, stays under its hundred-mesh budget and remains behind the north wall throughout its motion and release. Its carved tablets, shields and medallions have indexed, nondegenerate outward-facing triangles and finite normals/UVs. Tests use real observed guard, oath and fault views; they verify exact Reduced Effects signals, wrapped finite idle time and release from each observed state. Law/seal emission and the steady law light fade from their captured pre-defeat values without an initial flash or shield snap. Pause, restore and effect toggles cannot replay a completed release.

The same exported package also passes **164 native Spine exploration checks** and **463 opening-journey checks**: **1,215 native checks in total**, all with clean Godot logs. Exploration uses 6,937 campaign setup commands, 258 recorded input commands and six verified replay branches, with no focus recoveries. It exercises physical passage clicks, one archive reward, retained floor loot, local map navigation, Memory abandonment/retry/victory, adjacent backtracking and shipping F5/F9 persistence including live reversed fault warnings. The opening journey verifies shared environment setup, lighting and quality controls, combat presentation and persistence. Regression captures were omitted; the regional diagnostic supplies the 26 rendered captures.

## Rendered inspection

High captures from all five rooms were inspected, along with the archive in Performance mode, the Warden in Reduced Effects, the reversed Memory fault sequence, exposed and announced north-fault Warden states, and settled victory. Warm practical light reaches the causeway windows and archive shelves; deeper court joints and shaped bones remain subdued around the route. Divine Memory reads as a warmer, intact version of the region. Actual numbered fault bands and boss warnings remain distinct from decorative paving and lights.

High uses four environmental local lights; Performance uses two. The Warden retains one additional shadowless law light in both modes, bringing that court to five/three. Its state follows Core guard signals rather than decorative animation timing.

Thirty frame intervals per room and preset were sampled under a 60 FPS cap. Medians were 16.66–16.67ms and 95th percentiles 16.81–18.57ms. These short local samples establish frame pacing, not uncapped performance or a broad hardware benchmark.

## Prism review

Prism/Gemini run `bafce9288254c51d75d61ec2209c2a00` reviewed the staged implementation and reported two resource-lifetime candidates. Neither has a reachable failure path in the changed code:

- **Shared material disposal (high):** `SurfaceMaterials.Create` allocates a new `StandardMaterial3D` for every call. Its cache contains only texture sets. `EnvironmentBuilder` also creates owned per-batch materials. Both new cleanup paths dispose only recorded owned meshes/materials, deduplicated by instance ID, and never dispose textures. Native Warden checks create two concurrent rigs, verify distinct mutable law/seal materials but shared texture identity, prove one rig cannot change the other, and verify owned resources are released while shared textures survive.
- **Repeated disposal or stale references (medium):** `SpineAtmosphere._Notification` returns after `_released` is set, sets that guard before cleanup, detaches every renderer mesh before disposing resources, and clears all three resource collections afterward. `Animate` also returns after release. There is no second cleanup or post-release animation path that dereferences these resources. Native free/replacement checks pass with clean logs.

Raw review and disposition: `artifacts/spine-depth/prism-initial.json` and `prism-disposition.json`. No actionable finding remains.

## Artifacts and scope

- Accepted app: `artifacts/export/macos/Ashenwake.app`.
- Accepted archive: `artifacts/export/Ashenwake.zip`.
- Archive SHA-256: `39f7bf5286337164118dc0b80e37d1952526cce3058ab665b1dd1136a0d62219`.
- Baseline report/captures: `artifacts/spine-depth/before/`.
- Accepted regional report/captures: `artifacts/spine-depth/after/`.
- Regression reports: `artifacts/spine-depth/exploration/` and `journey/`.
- Build, content validation, formatting, import/export and Prism evidence: `artifacts/spine-depth/`.

The phase changes client presentation, diagnostics and documentation. Core/content sources, save schemas and random streams are unchanged. The full Core unit suite was not rerun; the native diagnostics execute real commands and replay through the changed client presentation.
