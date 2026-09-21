# Stylized dark-fantasy graphics polish

Ashenwake retains its stylized low-poly art direction, with beveled character geometry, tapered armor, defined facial features and textured surfaces. Lighting combines the regional palettes with a restrained cool fill and sky reflections. Metal, cloth, skin, bone, wood, earth and stone now have distinct albedo, roughness and normal detail.

Open **Settings → Graphics** to choose **High** or **Performance**. High is the default: 4× MSAA, ambient occlusion, a 4096-pixel directional shadow atlas, four shadow splits and subtle bloom. Performance uses 2× MSAA, a 2048-pixel atlas and two splits, without ambient occlusion or bloom. Both retain the same art and combat warnings. Reduced visual effects suppresses bloom regardless of quality. Preferences apply immediately, persist between sessions and do not modify characters or replays.

The rotating equipment preview uses the same material and lighting profile in its isolated world. Hidden previews still stop rendering. Gameplay remains in Core: collision bounds, actor sizes, hit tests, simulation and item stats are unchanged.

## Implementation boundaries

The pinned Godot 4.6.2 Compatibility renderer supports the chosen MSAA, simplified ambient occlusion and glow settings. There is no new renderer requirement, asset download or external texture dependency. The material factory builds at most seven sets of three 128×128 mipmapped textures and shares those textures. Materials remain independently mutable so obstacle fading and character accents cannot affect another model. Static environment geometry remains grouped by palette and surface type; beveled character templates use a bounded cache.

Ambient occlusion supplies contact shading, not ray-traced illumination. Directional shadow filtering uses PCF; this pass does not depend on PCSS or volumetric fog. The result remains stylized procedural art. Platform-wide performance certification and authored high-resolution models remain separate work.

## Reproduction

Source the pinned toolchain with `source tools/env.sh`, build `Ashenwake.sln`, then launch Godot with `--path game/Ashenwake.Client`. Native diagnostics accept a fresh directory through `--output=<directory>`:

- `--appearance-smoke --capture-appearance`: equipment changes, preview, armor, geometry and surface invariants.
- `--visual-smoke --capture-visuals`: all model definitions, animation and character/monster galleries.
- `--settings-smoke --capture-settings`: actual Settings input, quality application, accessibility, persistence, recovery and compact layouts.
- `--journey-smoke --capture-journey`: real opening campaign encounters, warnings and matching High/Performance Greyhaven captures. `graphics-frame-samples.json` records a short fixed-view sample at a 60 FPS cap; it is frame-pacing evidence, not a GPU benchmark.
- Regional and combat-feedback diagnostics retain their existing routes and capture flags.

See [verification](graphics_polish_verification.md) for the completed checks and Prism review.
