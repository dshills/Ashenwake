# Opening environment composition verification

Verified September 21, 2026 against baseline `a88e5db`, using the pinned Godot 4.6.2 Mono Compatibility renderer and .NET 8 on Apple M4 Pro/macOS.

Greyhaven now has distinct service courts and surrounding workshop, shrine and market scenery. The March has a bending stone road, a ruined monastery courtyard, offset architecture and layered cliffs. Six chimney wisps or eight peripheral leaves provide bounded ambient motion. All changes are presentation: Core content, room collision, progression, saves and replay rules are unchanged. Earth beside the painted paths remains walkable.

## Completed checks

`dotnet build Ashenwake.sln --no-restore -m:1 -p:UseSharedCompilation=false` completed with zero warnings/errors. The existing solution tests passed: 514 Core and 17 server tests, 531 total. Changed C# files were formatted; `git diff --check` passed.

The macOS export passed the export log gate. The following native diagnostics ran sequentially from the actual exported application, each in an isolated artifact directory. All accepted runs passed their report assertions and `tools/check-godot-log.py`, including shutdown.

| Packaged diagnostic | Passed assertions | Evidence directory under `artifacts/opening-composition/package.mqMzK3/` |
| --- | ---: | --- |
| Opening journey with captures | 314 | `journey/` |
| Mouse actions with captures | 115 | `mouse-actions/` |
| Mouse movement | 83 | `mouse-movement/` |
| Settings with captures | 162 | `settings.3eeFxW/` |
| Character rendering and lifecycle | 170 | `visual.AowjXY/` |
| **Total** | **844** | |

The journey earns encounters, rewards, choices and Bell Saint phases through real Core commands, verifies save/replay restoration, and observes production scenery. Additional checks inspect transformed mesh triangles for intrusion into walkable character corridors, confirm all ground remains below Y=0, verify each obstacle stays within its authoritative footprint and has independent fade materials, and traverse the painted routes with the real click planner and Core sweeps. Service targets are checked for reachable approaches within their interaction range. The rendered opening includes Mara; later rescued resident states are not a claim of this run.

Mouse actions exercise actual viewport input: hovering and clicking Mara, approaching her from outside interaction range, combat and loot, and the eastbound Way Forward marker. Exit arrival opens reward review exactly once, cancellation stops the approach, and departure retains the existing confirmation behavior. Native Settings verifies preference persistence, accessibility, pause/resume, recovery and repeated scene teardown. The rendering diagnostic also covers sky reattachment and cleanup.

Smoke/leaves advance only while playing, freeze under pause, and hide immediately under Reduced Effects, including while paused. Scene checks confirm one atmosphere node per opening room, no replacement on same-room projections, correct replacement on a paused resize, and removal when leaving the opening region.

## Visual and geometry evidence

Inspected the packaged Greyhaven, road, monastery, Bell Saint and Way Forward captures at the gameplay camera. An early repetitive ground pattern was replaced with one softly varying earth/grass surface. Landmarks stay behind the floor, the exit is visible and clickable, and combat warnings and character silhouettes remain clear.

| Scene | Architecture triangles | Architecture materials | Ground triangles | Ground materials |
| --- | ---: | ---: | ---: | ---: |
| Greyhaven | 13,858 | 24 | 8,028 | 18 |
| Road | 6,274 | 9 | 5,112 | 7 |
| Monastery | 7,772 | 9 | 7,536 | 9 |
| Sanctuary | 5,184 | 9 | 6,048 | 9 |

The complete scene inspections, including route simulation and software visibility samples, took approximately 10–44 ms per scene in the accepted package. These diagnostic-only checks run once per observation, not in the gameplay frame loop. In a fixed Greyhaven view capped at 60 FPS, High/Performance median frame intervals were 16.66/16.68 ms and p95 intervals 18.22/17.17 ms. These local samples are not broader hardware certification.

## Cleanup correction and Prism review

Early journey runs passed gameplay assertions but failed the clean-log gate on shutdown. Tracing accounted for every owned Sky creation and release. A world created and removed before its first render exposed the pinned GLES3 dirty-sky lifetime issue: the renderer retains a pending raw Sky pointer after freeing that sky, then allocates orphaned radiance textures. The [pinned renderer's sky update and free paths](https://github.com/godotengine/godot/blob/4.6.2-stable/drivers/gles3/rasterizer_scene_gles3.cpp) establish the mechanism. The Compatibility-only cleanup now drains pending work before releasing a world that has not survived a draw frame, with a reentrancy guard. Ordinary scene exits do not force a draw. Room-owned atmosphere resources also release in dependency order. Source and final package runs exit cleanly.

Prism/Gemini reviewed the staged implementation with `tools/prism-implementation.json` and default redaction. Initial review `b4bef7c26d03cd88a8149483d8314416` raised a diagnostic raycasting performance concern and a general resource-disposal concern. Measured diagnostic runtimes are bounded as above; scenery deliberately has no physics nodes for a physics raycast to hit. Resources released explicitly here belong to their room; shared surface textures and shaders are not disposed. Native cleanup verifies the final ordering.

Final review `9df6924a72728dbf698ceb4aa12ebf54` reports zero high, zero medium and two low findings:

- `432f0d98d534e275`: synchronous teardown flushing can hitch. The workaround is restricted to Compatibility worlds removed in their entry draw frame, addressing the reproduced leak; routine transitions retain asynchronous rendering.
- `73c9dffbb606b79f`: the 64×128 smoke texture could be cached. It is generated once when Greyhaven's atmosphere is created, never per frame. Keeping this small resource room-owned preserves deterministic cleanup without another global GPU cache.

The initial Settings command supplied an unsupported discipline argument and was rejected before creating a character. The accepted `settings.3eeFxW` run uses the required startup arguments. Rejected runs are retained as diagnostics and excluded from the passing totals.

## Playable artifact

- Application: `artifacts/export/macos/Ashenwake.app`
- Archive: `artifacts/export/Ashenwake.zip`
- SHA-256: `be920e65dfa626a8496437e00aa33900dbb1644b04a35d638e396d69dbba1fe5`

This completes the opening composition pass within the existing rooms. Connected exploration geometry, new collision layouts, independent playtests and other hardware remain separate work.
