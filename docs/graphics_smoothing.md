# Smoother models and higher rendering resolution

This pass softens the visible polygon edges on heroes, residents and monsters while keeping their silhouettes, equipment identities and attack poses. Heads, hands, rounded bodies, horns and rings use denser shared meshes. Armor and weapons keep their broad surfaces, with three-segment curved bevels and smoothly joined corners. Organic cheeks and jaws are rounder. Collision, targeting and combat remain unchanged.

Seven shared surface texture sets now use 512×512 albedo, normal and roughness maps, up from 128×128. Full mip chains and anisotropic filtering keep distant materials stable. Normal-map gradients retain the authored relief as texel spacing shrinks. The 21 cached RGBA textures occupy about 28 MiB including mipmaps; colors reuse the same textures.

## Playing

Open **Settings → Graphics**:

- **High** uses 8× MSAA edge smoothing and detailed shadows. **Enhanced · 125%** is the default 3D resolution: it renders extra pixels and reduces the result to the display size.
- **Native · 100%** reduces rendering cost. **Maximum · 150%** supplies more detail at greater GPU cost. Those scales render approximately 1×, 1.56× and 2.25× the native pixel count.
- **Performance** uses 2× MSAA and native resolution, retaining your preferred supersampling setting for when you return to High.
- **Windowed** opens up to 1600×1000, fitted to the available display area. Resize or maximize it as desired. **Fullscreen** uses the native display resolution while preserving the game's aspect ratio; ultrawide displays can have side margins.

Display and rendering preferences apply immediately and persist on this device. Existing settings files receive safe defaults for the new fields. The 1280×800 base remains the interface's design size; `canvas_items` renders at the actual window resolution, so it is not a 3D resolution cap. HiDPI remains enabled.

Equipment and character-selection previews now render at their actual screen pixel size, bounded to 2048 pixels on the longest edge, before applying the selected 3D resolution. Hidden previews still stop rendering. This avoids enlarging a small preview texture on high-density screens.

The implementation uses [Godot 4.6 bilinear resolution scaling](https://docs.godotengine.org/en/4.6/tutorials/3d/resolution_scaling.html), supported by the existing Compatibility renderer, and [canvas-item screen transforms](https://docs.godotengine.org/en/4.6/classes/class_canvasitem.html#class-canvasitem-method-get-screen-transform) for preview pixel sizing. It introduces no renderer migration or external asset dependency.

See [verification](graphics_smoothing_verification.md) for rendered comparisons, diagnostics and review evidence. Hardware-wide performance acceptance remains separate from the local captures.
