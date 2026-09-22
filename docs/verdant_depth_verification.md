# Verdant visual depth verification

Validated on 2026-09-22 from `520d41d7b27e18f5e6dd457c718990641450cada`, using .NET 8.0.425 and the exported Godot 4.6.2 Mono macOS app, Compatibility renderer, Apple M4 Pro.

## Accepted evidence

- Solution build: zero warnings and errors. `dotnet format --verify-no-changes`, `git diff --check` and `aw experiment compile` pass.
- **391 native Verdant checks**, **19 captures**, all five regional contexts, **3,678 real campaign commands**, completed Act II and checkpointed replay. The final gameplay hash is `ADFC64124AFB2DE04E2185FAECA0FA2CF757E227F76E6D266EC45D24381B00C5`; both hash and command count exactly match the pre-change baseline.
- **146 native exploration checks**, including physical passage clicks, all three hunt clues, shrine treasure, backtracking, map navigation, F5/F9 persistence and five verified replay branches. No focus-loss recovery was needed.
- **463 native opening-journey checks**, covering shared builders, opening lights, layout geometry, preferences, save/restore and combat presentation.
- **651 native appearance/equipment checks**, including 41 drag gestures and 1,477 commands. These regression runs omitted optional screenshot capture; the Verdant suite supplied this phase's visual evidence.
- Total: **1,651 native checks**. All four accepted runtime logs pass `tools/check-godot-log.py` without exceptions.

The first Verdant attempt stopped at the retained seed-mesh cleanup assertion before running campaign commands. The rig cleanup implementation was restored to dispose all owned meshes and materials once; the rebuilt package then passed the full route. Shared surface textures remain valid across repeated destruction and construction. The failed attempt is retained separately and is not counted as passing evidence.

## Geometry, settings and visual inspection

Actual transformed vertices verify that raised architecture stays outside the combat rectangle, floor geometry remains below Y=0 and obstacle art retains the authoritative footprint. Architecture uses 12–16 mesh/material batches per room; ground uses 8–11. The highest floor vertices range from −0.017m to −0.003m. Rootheart retains thirteen joints, stays below its eighty-mesh ceiling, and remains behind the wall through every root count and the complete victory animation.

The indexed leaves have nondegenerate double-sided triangles, paired opposite normals, finite UVs and pointed curved tips. Native MultiMesh readback verifies motion and every leaf's perimeter placement over two wind periods. Detached and live checks cover invalid inputs, fixed resource identities, paused quality switches, Reduced Effects while paused, same-style resizing, style changes, room exit, completed-region revisits and release of owned resources.

High uses 72 moving leaves and four shadowless lights; Performance uses 36 leaves and two lights. Reduced Effects preserves steady local light and static foliage while settling Rootheart's active victory transition. Preference changes leave Core state unchanged.

The High captures for all five environments, village Performance, guarded and settled Rootheart, and reduced-effects Rootheart were visually inspected. Village lanterns light the porch; tapered crowns, leaf shingles, moss/earth transitions and the petal veins read at the gameplay camera distance. Paths, clues and enemies remain visible.

Thirty frame intervals were sampled in each room and quality mode under the diagnostic's 60 FPS cap. Medians were 16.66–16.69ms and 95th percentiles 16.74–20.67ms. These short samples confirm local frame pacing during this run; they are not an uncapped performance comparison or broad hardware benchmark.

## Prism review

Prism/Gemini run `7a316fb7c1ea9f5a3459c988caa7ad2d` reviewed the staged implementation. It reported no high or medium findings and two low-priority observations:

1. **Frame-timed visual clocks:** intentional for wind and breathing. The clocks are bounded and pause-aware, use no simulation randomness, and do not enter save/replay state. Deterministic gameplay is verified against the baseline; frame-identical cosmetic animation is not a replay requirement.
2. **Linear boardwalk occupancy lookup:** bounded to at most 77 candidate boards from the fixed village routes, with fewer than 3,000 distance comparisons once during room construction. It does not run per frame. A spatial index is unnecessary for the present authored layout.

No actionable findings remain. The review and dispositions are retained in `artifacts/verdant-depth/prism-initial.json` and `prism-disposition.json`.

## Artifacts

- Accepted app: `artifacts/export/macos/Ashenwake.app`.
- Accepted archive: `artifacts/export/Ashenwake.zip`.
- Archive SHA-256: `5eb6d95f462b1294c044eddd7111223678158d7db15d77a0e62d07390973dfe0`.
- Baseline: `artifacts/verdant-depth/before/`.
- Accepted Verdant report/captures: `artifacts/verdant-depth/after-v2/`.
- Regression reports: `artifacts/verdant-depth/exploration/`, `journey/`, `appearance/`.
- Build, content validation, formatting, import/export and review logs: `artifacts/verdant-depth/`.

This phase changes client presentation and diagnostics only. No Core or content files changed, and the full Core unit suite was not rerun; the selected native diagnostics execute real Core commands, save/restore and replay checks.
