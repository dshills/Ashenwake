# Graphics polish verification

Baseline: `8ad9d4a`. Pinned toolchain: Godot 4.6.2 Mono and .NET 8 on macOS Apple Silicon (Apple M4 Pro). The selected direction is polished stylized dark fantasy. No Core, server, save-schema or authored combat-content changes are included.

## Corrections from rendered inspection

Actual captures exposed a mixed-mesh batching defect: Godot's `SurfaceTool.AppendFrom` retains existing indices, so combining an unindexed custom shape with indexed native primitives omitted the custom triangles. Beveled shapes and robe panels now supply indices. The native regression compares winding against Godot's `BoxMesh` and verifies triangle counts and placements for both mixed input orders. Faces, boots, robes and armor are visible in the corrected captures.

The first shadow tuning produced broad diagonal self-shadow bands and weak cast shadows. The final profile uses four splits on High, two on Performance, a bounded 60-unit shadow distance, and conservative depth/normal bias. Matching Greyhaven screenshots confirm the bands are gone and cast shadows remain visible. Texture and material projection were also checked; the ground correctly uses Earth, and the Compatibility shader supplies the tangent basis for triplanar mapping.

Rendered galleries also exposed a pinned GLES3 glow issue: the solid clear color bypasses the luminance scale used by the scene, then receives its inverse in post-processing. The predicted RGB value matched the overly blue background within one channel level. A shared sky shader now draws the authored solid background through the proper scaling path, with a separate gradient only for the reflection cubemap. Pixel checks confirm dark, consistent backgrounds in High, Performance and Reduced Effects. The corrected appearance suite passes **663 checks** in `background-final.NjzR1J`, including these three additional checks. The implementation follows the pinned engine's [scene renderer](https://github.com/godotengine/godot/blob/4.6.2-stable/drivers/gles3/rasterizer_scene_gles3.cpp), [post shader](https://github.com/godotengine/godot/blob/4.6.2-stable/drivers/gles3/shaders/post.glsl) and [sky shader](https://github.com/godotengine/godot/blob/4.6.2-stable/drivers/gles3/shaders/sky.glsl).

## Automated and rendered evidence

- Full solution build: zero warnings/errors. Formatting and whitespace checks pass.
- All **514 Core tests** and **17 server tests** pass; no failures or skips.
- Corrected source runs pass **2,115 native assertions** across appearance (660), Settings (156), opening Journey (107), full character/monster catalogs (174), combat feedback (245), Verdant Maw (141), Cinder Reach (187), Shattered Spine (216), and Hollow Night (229).
- The Settings suite exercises actual input at 1280×800, 1000×720 and 780×720, High/Performance renderer flags, immediate bloom suppression, persistence/restarts, legacy/unknown/null values, tab-specific reset and character/replay isolation.
- Surface checks cover all seven shared texture sets, complete mip chains, valid normals, palette retention, isolated fading, smooth emissive accents, distinct metal/cloth roughness, bounded caches and batching. Character checks cover native winding, finite extents/UVs, tapered geometry, equipment transactions, rotation and replay parity.
- Visual inspection covers matching Greyhaven views, equipped characters, monsters, regional environments, hazards and compact Graphics settings. Local artifacts are under `artifacts/graphics-polish/`; they are intentionally ignored by Git.

The fixed Greyhaven sample (`final-journey.FrRXV5/graphics-frame-samples.json`) recorded 90 frames per preset after warm-up, at a 60 FPS cap. High: median **16.66 ms**, p95 **16.76 ms**; Performance: median **16.67 ms**, p95 **17.50 ms**. Both reported 348 draw calls and 66,841 primitives. These are local capped frame-pacing samples, not a GPU-speed comparison, uncapped benchmark, or low-end hardware certification.

## Prism review

Prism/Gemini review `ff71dc3a9920b855f31d3a9506808fca` reports zero high, one medium and one low finding. Both were checked against the actual implementation:

- `79c33160576bac6d` alleges incomplete color mapping at a nonexistent `src/Graphics/CharacterVisual.Geometry.cs` path. The real `SurfaceMaterials.EnvironmentKind` switch has an explicit Stone fallback; character materials receive a valid surface kind directly. Geometry construction does not perform that mapping. The all-model and material suites pass.
- `25a657af1fe68531` alleges that Godot 4.6.2 does not exist and asks for a 4.3/4.4 reference. The pinned executable and every native log identify **Godot Engine v4.6.2.stable.mono.official**. The documentation correctly names the toolchain in use.

Final implementation review `eaebb0990f75476c79b35f720e7fb9a8` also reports zero high, one medium and one low finding:

- `3c2455f3b6117835` notes that an uncached shape can be generated after the 128-entry shape cache fills, but claims this occurs every frame. Shape creation only runs while constructing model/equipment templates; animation transforms existing joints and does not call `PolishedBoxMesh`. The existing template caches also reuse assembled models. The cap deliberately bounds retained shape resources; uncached results remain owned by their consumer. Cache eviction is a possible later optimization if measured template-construction stalls justify it. No per-frame allocation path or demonstrated regression is present.
- `11fb6f4c9758f783` suggests replacing modulo with a bit mask in texture generation. The factory generates seven small texture sets once, then reuses them; this is a nonblocking micro-optimization, not a frame-loop defect.

Default secret redaction remained enabled. No actionable blocker remains. Final package validation is recorded below.

## Packaged diagnostic correction

The initial final-app attempt in `package.QcuxMV` was deliberately stopped after the new pixel check waited for an automatic frame draw while the native window was inactive. No pass is claimed for that incomplete attempt. The check now uses the existing diagnostic capture contract, `RenderingServer.ForceDraw(false)` followed by `ForceSync`, after settling scene frames. This changes only the diagnostic; the game rendering implementation reviewed by Prism is unchanged. Final package execution also explicitly requires each successful JSON report, rather than treating process exit alone as acceptance.

The next package (`package-final.d5YmZi`) passed the appearance and Settings assertions but failed the clean-log gate: shutdown reported four 349,524-byte GPU textures. The pinned GLES3 allocation size identifies the radiance/raw-radiance pair of two surviving Sky resources. Managed wrappers can outlive their scene until garbage collection. Every world using the graphics profile now explicitly releases its owned Sky and ShaderMaterial on exit; reattachment recreates them while preserving environment settings. New Settings checks intentionally retain managed environment wrappers during teardown and verify their skies are detached. The gallery also tests detach/reattach. The source cleanup run exits cleanly. That earlier package is not claimed as accepted.

Final lifecycle review `80a1aa58c2b36016f947bee313ae8b32` reports zero high, one medium and one low finding. `1a604550ec450242` warns against disposing shared resources. These Sky and ShaderMaterial instances are created separately for each private environment, detached before disposal, and released after their world exits; the shared Shader is never disposed by this path. Explicit disposal releases the C# reference deterministically instead of leaving GPU cleanup to GC. Teardown/reattachment and clean native shutdown are tested. `dbee90c2300782a1` suggests approximate Vector3 cache keys. Shape dimensions come from authored constants and repeatable construction expressions, not integrated animation state; exact keys preserve intentionally distinct dimensions, and retained entries remain capped. Neither concern establishes a defect in these ownership or input paths.

## Accepted macOS package

The final extracted app passes **2,126 assertions** across all nine rendered suites, with clean logs: appearance **665**, Settings **162**, Journey **107**, full visual catalogs **174**, combat feedback **245**, Verdant **141**, Cinder **187**, Spine **216**, and Hollow **229**. All JSON reports explicitly indicate success. The evidence is in `artifacts/graphics-polish/accepted-package.Iaiod7`; `artifacts/graphics-polish/verification.json` records counts, paths, review IDs and archive identity. The older Combat Feedback and Verdant capture routes needed their native windows raised to continue automatic frame draws; both completed successfully. The final app has no texture-leak shutdown errors.

The accepted ZIP is `artifacts/export/Ashenwake.zip`, SHA-256 **90725b1d701881632178a11a0095fb23cf00fd9a275cc0a45de1ba000e540ea6**. The playable bundle is `artifacts/export/macos/Ashenwake.app`. Build, formatting and whitespace checks pass. Final packaged Greyhaven pacing samples remain near the 60 FPS cap: High median **16.66 ms**, p95 **17.43 ms**; Performance median **16.66 ms**, p95 **17.12 ms**. No claim is made for other GPUs, operating systems or uncapped performance.
