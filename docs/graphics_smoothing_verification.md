# Graphics smoothing verification

This milestone changes client presentation and device preferences only. Core gameplay, authored content bundles and archive formats are unchanged.

Evidence is retained in `artifacts/graphics-smoothing/`. The `before` gallery was captured from the previously committed macOS package before rebuilding this implementation.

## Checks

- Integrated solution build: zero warnings and errors.
- Final headless appearance run: 743 checks pass, covering the new mesh bounds, watertight seams, normals, tangents and batching; all 21 shared texture maps and mip levels; equipment interactions, previews, ownership, saves and replay.
- Headless settings: 152 checks pass, including legacy preferences, 3D scale persistence, Performance fallback, restore defaults and normalization. Headless front menu: 119 checks pass, including all five character-selection previews and saved-character flows.
- Packaged model gallery: 174 checks pass, with four native captures. The before/after galleries retain matching framing. Faces, rounded joints, skulls and curved silhouettes were visually compared; broad armor and weapon planes remain distinct.
- Packaged settings: 175 checks pass with clean runtime logs. Fullscreen switches the OS window to 5120×1440; the aspect-preserving game image is 2304×1440 on this ultrawide display. Returning to Windowed produces 1600×1000. Compact and larger graphics pages were visually inspected for legible controls and scrolling.
- Packaged appearance: 793 checks pass with 47 captures, including rendered equipment previews, rotation, native drag/drop, filtering, anatomy and save/replay isolation. Packaged Journey: 464 checks pass with 27 captures. Both runtime logs pass the strict Godot error check. Equipment and the High-quality Greyhaven view were visually inspected.
- Fixed Greyhaven sample on Apple M4 Pro, 1280×800 window, 90 frames per preset at a 60 FPS cap: High (125%, 8× MSAA) median 16.67 ms, p95 17.60 ms; Performance (100%, 2× MSAA) median 16.66 ms, p95 17.55 ms. This is a short local sample, not a claim for every resolution or combat population.
- Final macOS package SHA-256: `46ebd5f5192b12b4649af99e9e713ae36e23c80bd3d6f9ba08676962e7bc35df`.

## Prism review

Prism/Gemini reviewed the implementation (`7bacc651aeb9e2f0397511a099b2fa3a`) and settings-cache fix (`f5114933563e8dca1d1f1726c17d4432`) with default secret redaction. Both reports contain two low findings and no medium/high findings. Raw reports are retained as `prism-review.json` and `prism-followup.json`.

The real per-frame resolution-label allocation was fixed by caching window dimensions and the applied scale before formatting the string. The mesh-cache comment describes the deliberate bounded-cache policy: after 128 distinct templates of a kind, additional authored shapes remain usable without retaining unbounded static resources. It does not establish dictionary growth beyond the cap. Mesh generation occurs when building models, not each animation frame.

The follow-up's float-fluctuation hypothesis does not apply to the three explicitly assigned, exactly representable scales (1, 1.25 and 1.5); no adaptive-resolution system modifies them. The label intentionally says **Window** and shows window pixels, with a separate 3D-detail percentage. Substituting the fixed logical UI size would misrepresent the active display resolution.

## Reproduction

Source `tools/env.sh`, build the solution, then run the native package with a fresh `--output` directory per diagnostic:

```sh
Ashenwake -- --visual-smoke --capture-visuals --output=/absolute/fresh/gallery
Ashenwake -- --appearance-smoke --capture-appearance --output=/absolute/fresh/appearance
Ashenwake -- --settings-smoke --capture-settings --output=/absolute/fresh/settings
Ashenwake -- --front-menu-smoke --output=/absolute/fresh/front-menu
Ashenwake -- --journey-smoke --capture-journey --output=/absolute/fresh/journey
```

Keep the native diagnostic window focused during input tests. Headless runs can validate geometry and interaction contracts but cannot establish rendered appearance, physical display transitions or GPU performance. The Journey diagnostic retains short capped frame-time samples for the same Greyhaven view; these are local observations rather than hardware certification.
