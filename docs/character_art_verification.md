# Character art verification

This milestone replaces combat placeholders with articulated low-poly characters, adds individual Greyhaven specialists, and brings the gameplay camera closer with player following. It preserves Core rules and content. See `character_art.md` for art scope and reproduction commands.

## Rendered inspection

The final OpenGL preview on the development Mac built all 44 enemy definitions, five disciplines and five specialists. Its 174 checks passed, including finite geometry, no physics/navigation nodes, walking and windup poses, frozen paused poses, shared mesh/base-material resources, and independent accent colors. The inspected hero, monster, opening-enemy and Greyhaven galleries use production models. The maximum was 21 visible mesh nodes and seven materials per model; the gallery populated 54 mesh templates and 159 shared base materials, within the 96/256 cache limits.

The rendered opening journey passed 18 checks and captured the first encounter plus all three Bell Saint phases. Inspection confirmed readable hero/monster silhouettes, the bell-to-beast transformation, ground warnings, and overhead labels at the closer camera distance. The real Greyhaven interaction test passed 17 checks, including NPC idle motion without root movement and freezing that motion during pause. Ordinary Greyhaven was also captured from the normal launcher with a fresh save directory.

An earlier rendered journey attempt missed a synthetic map-opening click; no deterministic HUD/Core defect was found and the isolated rerun passed. The diagnostic now delivers mouse motion/press/release together, positively asserts that the map opened, and captures a failure screen. The final rendered run passed those stronger checks.

## Prism

Prism reviewed the staged implementation using the repository rules and the configured Gemini provider. Final run `a71aee9d9eef84c02b428cb4f574e18f` returned zero high and two medium findings.

- Earlier mesh/resource findings were addressed with bounded immutable mesh and base-material caches. Only the accent material is owned and changed per actor. Resource-sharing and accent-isolation checks pass.
- Earlier NPC-animation feedback was addressed with stage-driven idle updates and actual pause/resume checks.
- The final concurrency concern has no execution path in this implementation. Godot `_Ready`/`_Process` and GUI callbacks construct the models. Co-op transport enqueues network frames in `ConcurrentQueue`; `CoopClient._Process` drains them and calls `CoopClientPresentation.Render`. A lock would not make Godot scene construction safe on a background thread.
- Temporary primitive-node creation on a cache hit remains a valid optimization opportunity. Cached actors skip vertex merging and share resulting GPU geometry, but still construct the authoring hierarchy before replacing its static parts. This bounded spawn-time work is not performed during ordinary per-frame animation. Direct instantiation of cached rig templates remains future profiling/production work; no hardware frame-time certification is claimed.

An earlier co-op class concern was a false positive: Core constructs both cooperative players as Vanguard and validates that fixed loadout. No selectable co-op discipline is hidden by the renderer. A proposed unbounded/LRU cache is unnecessary for the current fixed content; the populated preview is below both limits, and new equipment does not create visual keys in this pass.

## Delivery evidence

The solution build and formatting verification pass. Final package and two-player results, exact replay hashes, archive SHA-256, reviewed patch, source commit, and screenshot paths are recorded in the local `artifacts/character-art/evidence.json`. Test outputs use fresh artifact directories and do not modify the player's saves.

The exported macOS application passed 87 UI checks (33 release, 17 interactions, 18 journey, 19 services) and 170 headless model/resource checks. Its complete campaign/endgame route executed 32,190 ordinary commands and independently replayed to `07CA337276B242FCB437F668EFFFFB490C8F0CB6F61CCC08695D42DBD6E32D55`. The 2,516-command Borrowed Memory route replayed to `7650112FD3FBE668E200951C6996FC29D1A47939941DB424EBDF5EC831FA6E71`. Both canonical hashes match the prior gameplay milestone.

The same archive passed the actual two-player WebSocket route: 968 matching snapshots, zero mismatches and ten distinct reward receipts. The delivered archive SHA-256 is `d3af663f696c9a429c1b32ab130bc275ddc8c59f20019e5e8f22227a399862b1`.

These are first-pass procedural models. Equipment-specific visual swaps, production skinning/LODs, dedicated hit/dodge/death clips, final environment art, and independent hardware/performance acceptance remain open.
